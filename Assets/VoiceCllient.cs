using UnityEngine;
using System;
using System.Text;
using System.IO;
using System.Collections;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.Networking; // UnityWebRequest i�in

public class VoiceClient : MonoBehaviour
{
    public KeyCode pushToTalkKey = KeyCode.V;
    public bool useTwoKeys = true; // İki tuşla başlat/durdur modu
    public KeyCode startRecordKey = KeyCode.R;
    public KeyCode stopRecordKey = KeyCode.T;
    public AudioSource audioSource;
    public TMPro.TextMeshProUGUI subtitle;

    // Sunucu adresi (opsiyonel - eski akf)
    public string serverUrl = "http://127.0.0.1:5005/chat";

    private AudioClip recording;
    private string micDevice;
    private bool isRecording;
    
    // WAV kaydetme ayarları
    public bool saveOnStop = true;
    public string saveFilePrefix = "recording_";
    public string lastSavedPath;
    private byte[] lastWav;

    // Sabit dosya adıyla kaydet ve her seferinde üzerine yaz
    public bool useFixedFileName = true;
    public string fixedFileName = "kayıt.wav";

    // STT entegrasyonu (harici servis)
    public bool uploadToServer = true; // R/T akışı sonrası STT tetikle
    public SpeechToTextService sttService;

    // Chatbot entegrasyonu (OpenRouter API)
    public bool enableChatbot = true; // Chatbot'u aktif et
    public string openRouterApiKey = ""; // OpenRouter API Key - Inspector'da girin
    public string openRouterUrl = "https://openrouter.ai/api/v1/chat/completions";
    public string openRouterModel = "anthropic/claude-3-haiku"; // Kullanılacak model
    public bool autoSendToChatbot = true; // STT sonrası otomatik chatbot'a gönder

    // Unity ifi anflndanfcalfmayfcalf kapat/afac (varsaylan kapal)
    public bool playbackInUnity = false;

    void Start()
    {
        // Başlangıçta mikrofonu kullanmayalım; sadece kayıt başlatınca kontrol edilecek
        if (!audioSource) audioSource = gameObject.AddComponent<AudioSource>();
        if (subtitle) subtitle.text = "R ile kayd başlat, T ile durdur.";
        if (sttService == null) sttService = FindObjectOfType<SpeechToTextService>();
    }

    void Update()
    {
        // Her zaman klasik Input ile kontrol et (daha garantili)
        if (useTwoKeys)
        {
            if (Input.GetKeyDown(startRecordKey))
            {
                StartRec();
            }
            if (Input.GetKeyDown(stopRecordKey))
            {
                StopRecAndSend();
            }
        }
        else
        {
            if (Input.GetKeyDown(pushToTalkKey))
            {
                StartRec();
            }
            if (isRecording && Input.GetKeyUp(pushToTalkKey))
            {
                StopRecAndSend();
            }
        }
    }

    void StartRec()
    {
        if (string.IsNullOrEmpty(micDevice))
        {
                    if (Microphone.devices.Length == 0)
        {
            if (subtitle) subtitle.text = "Mikrofon yok. Windows > Gizlilik > Mikrofon: AIK yap.";
            Debug.LogError("Mikrofon bulunamad!");
            return;
        }
        micDevice = Microphone.devices[0];
        Debug.Log("Mikrofon bulundu.");
        }

        if (Microphone.IsRecording(micDevice))
            Microphone.End(micDevice);

        recording = Microphone.Start(micDevice, false, 30, 16000);
        Debug.Log(" Kayıt başladı.");
        StartCoroutine(WaitForMicStart());

        if (subtitle) subtitle.text = "Kayıt alınıyor... (T ile durdur)";
    }

    IEnumerator WaitForMicStart()
    {
        int guard = 0;
        while (Microphone.GetPosition(micDevice) <= 0 && guard < 200)
        {
            guard++;
            yield return null;
        }
        isRecording = true;
    }

    void StopRecAndSend()
    {
        if (!isRecording || recording == null) return;

        int pos = Microphone.GetPosition(micDevice);
        Microphone.End(micDevice);
        Debug.Log(" Kayıt durdu.");
        isRecording = false;

        if (pos <= 0)
        {
            if (subtitle) subtitle.text = "Kayıt çok kısa oldu. V'ye biraz daha uzun bas.";
            return;
        }

        int channels = recording.channels;
        float[] data = new float[pos * channels];
        recording.GetData(data, 0);

        // Unity ifi hoparlfreden gericalmay opsiyonel yap
        if (playbackInUnity)
        {
            var clip = AudioClip.Create("recorded", pos, channels, 16000, false);
            clip.SetData(data, 0);
            audioSource.Stop();
            audioSource.clip = clip;
            audioSource.Play();
        }

        // WAV'aevir
        byte[] wav = EncodeAsWav(data, channels, 16000);
        lastWav = wav;

        // Durdurunca otomatik kaydet (isteğe bağlı)
        if (saveOnStop)
        {
            SaveWavToDisk(wav);
        }

        // Yalnızca yerel kaydetme isteniyorsa burada dur
        if (!uploadToServer)
        {
            Debug.Log("STT atlandı: uploadToServer=false");
            if (subtitle) subtitle.text = "Kayıt tamamlandı (WAV kaydedildi).";
            return;
        }

        // STT servisini kontrol et
        if (sttService == null) 
        {
            sttService = FindObjectOfType<SpeechToTextService>();
            if (sttService == null)
            {
                if (subtitle) subtitle.text = "STT servisi yok";
                return;
            }
        }

        if (subtitle) subtitle.text = "Yükleniyor...";
        
        string suggestedName = useFixedFileName ? fixedFileName : (saveFilePrefix + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".wav");
        
        StartCoroutine(sttService.Transcribe(wav, suggestedName, (text) =>
        {
            if (string.IsNullOrEmpty(text))
            {
                Debug.LogWarning("STT: Boş transkript alındı");
                if (subtitle) subtitle.text = "Ses tanınmadı. Lütfen tekrar konuşun.";
                
                // Kullanıcıya yardımcı öneriler
                StartCoroutine(ShowSttHelp());
            }
            else
            {
                Debug.Log("STT: Transcript alındı: " + text);
                if (subtitle) subtitle.text = "Siz: " + text;
                
                // Chatbot'a otomatik gönder
                if (enableChatbot && autoSendToChatbot)
                {
                    StartCoroutine(SendToChatbot(text));
                }
            }
        }));
    }

    // OpenRouter API ile chatbot'a mesaj gönder
    IEnumerator SendToChatbot(string message)
    {
        Debug.Log("OpenRouter API'ye gönderiliyor: " + message);
        if (subtitle) subtitle.text = "AI'ya gönderiliyor...";

        // API Key kontrolü
        if (string.IsNullOrEmpty(openRouterApiKey))
        {
            Debug.LogError("OpenRouter API Key boş! Inspector'da girin.");
            if (subtitle) subtitle.text = "API Key eksik! Inspector'da girin.";
            yield break;
        }

        // OpenRouter request oluştur
        OpenRouterMessage userMessage = new OpenRouterMessage { role = "user", content = message };
        OpenRouterMessage systemMessage = new OpenRouterMessage { 
            role = "system", 
            content = "Sen Türkçe konuşan yardımcı bir AI asistanısın. Kısa ve net yanıtlar ver. Türkçe konuş." 
        };
        
        OpenRouterRequest request = new OpenRouterRequest
        {
            model = openRouterModel,
            messages = new OpenRouterMessage[] { systemMessage, userMessage },
            max_tokens = 1000,
            temperature = 0.7f
        };

        string jsonRequest = JsonUtility.ToJson(request);

        using (UnityWebRequest www = new UnityWebRequest(openRouterUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonRequest);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + openRouterApiKey);
            www.SetRequestHeader("HTTP-Referer", "https://vr-project.unity");
            www.SetRequestHeader("X-Title", "VR Project");

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("OpenRouter API hatası: " + www.error + " | " + www.downloadHandler.text);
                if (subtitle) subtitle.text = "AI hatası: " + www.error;
            }
            else
            {
                // OpenRouter yanıtını işle
                string json = www.downloadHandler.text;
                OpenRouterResponse response = JsonUtility.FromJson<OpenRouterResponse>(json);

                if (response != null && response.choices != null && response.choices.Length > 0)
                {
                    string reply = response.choices[0].message.content;
                    Debug.Log("AI yanıtı: " + reply);
                    if (subtitle) subtitle.text = "AI: " + reply;
                }
                else
                {
                    Debug.LogWarning("OpenRouter'dan boş yanıt alındı");
                    if (subtitle) subtitle.text = "AI: Yanıt alınamadı";
                }
            }
        }
    }

    IEnumerator SendToServer(byte[] wav)
    {
        WWWForm form = new WWWForm();
        form.AddBinaryData("audio", wav, "speech.wav", "audio/wav");

        using (UnityWebRequest www = UnityWebRequest.Post(serverUrl, form))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(www.error + " | " + www.downloadHandler.text);
                if (subtitle) subtitle.text = "Bağlantı hatası: " + www.error;
            }
            else
            {
                // Beklenen JSON: { "text": "...", "wav_base64": "..." }
                string json = www.downloadHandler.text;
                ServerReply reply = JsonUtility.FromJson<ServerReply>(json);

                if (subtitle) subtitle.text = reply != null && !string.IsNullOrEmpty(reply.text)
                    ? reply.text
                    : "Sunucudan yanıt geldi.";

                //leride TTS dnerse buradanalacaz
                if (reply != null && !string.IsNullOrEmpty(reply.wav_base64))
                {
                    try
                    {
                        byte[] ttsBytes = Convert.FromBase64String(reply.wav_base64);
                        var ttsClip = WavToClip(ttsBytes, "bot");
                        if (ttsClip != null)
                        {
                            audioSource.Stop();
                            audioSource.clip = ttsClip;
                            audioSource.Play();
                        }
                    }
                    catch (Exception e) { Debug.LogError("Base64 parse error: " + e.Message); }
                }
            }
        }
    }

    [Serializable] class ServerReply { public string text; public string wav_base64; }
    
    [Serializable] class OpenRouterMessage { public string role; public string content; }
    [Serializable] class OpenRouterRequest { public string model; public OpenRouterMessage[] messages; public int max_tokens = 1000; public float temperature = 0.7f; }
    [Serializable] class OpenRouterChoice { public OpenRouterMessage message; }
    [Serializable] class OpenRouterResponse { public OpenRouterChoice[] choices; }

    // ===== WAV Yardımcıları =====
    byte[] EncodeAsWav(float[] samplesInterleaved, int channels, int hz)
    {
        // 16-bit PCM WAV
        int sampleCount = samplesInterleaved.Length;
        int byteRate = hz * channels * 2;
        var stream = new System.IO.MemoryStream();
        var writer = new System.IO.BinaryWriter(stream, Encoding.UTF8, true);

        void WriteStr(string s) => writer.Write(Encoding.ASCII.GetBytes(s));

        WriteStr("RIFF");
        writer.Write(36 + sampleCount * 2);
        WriteStr("WAVEfmt ");
        writer.Write(16);
        writer.Write((short)1);                 // PCM
        writer.Write((short)channels);
        writer.Write(hz);
        writer.Write(byteRate);
        writer.Write((short)(channels * 2));    // block align
        writer.Write((short)16);                // bits per sample
        WriteStr("data");
        writer.Write(sampleCount * 2);

        for (int i = 0; i < sampleCount; i++)
        {
            short s = (short)Mathf.Clamp(samplesInterleaved[i] * 32767f, -32768f, 32767f);
            writer.Write(s);
        }
        writer.Flush();
        return stream.ToArray();
    }

    // WAV'ı diske kaydet
    void SaveWavToDisk(byte[] wav)
    {
        try
        {
            string path;
            if (useFixedFileName && !string.IsNullOrEmpty(fixedFileName))
            {
                // Sabit isim, her kayıtta üzerine yaz
                path = Path.Combine(Application.persistentDataPath, fixedFileName);
            }
            else
            {
                string fileName = saveFilePrefix + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".wav";
                path = Path.Combine(Application.persistentDataPath, fileName);
            }

            File.WriteAllBytes(path, wav); // overwrite
            lastSavedPath = path;
            Debug.Log("WAV kaydedildi: " + path);
            if (subtitle) subtitle.text = "WAV kaydedildi: " + path;
        }
        catch (Exception e)
        {
            Debug.LogError("WAV kaydetme hatası: " + e.Message);
            if (subtitle) subtitle.text = "WAV kaydetme hatası";
        }
    }

    [ContextMenu("Save Last WAV")]
    void SaveLastWav()
    {
        if (lastWav == null || lastWav.Length == 0)
        {
            Debug.LogWarning("Kaydedilecek son WAV bulunamadı.");
            if (subtitle) subtitle.text = "Son WAV yok";
            return;
        }
        SaveWavToDisk(lastWav);
    }

    // UI butonları için yardımcılar
    public void StartRecordingButton() { StartRec(); }
    public void StopRecordingButton() { StopRecAndSend(); }

    AudioClip WavToClip(byte[] wavData, string clipName)
    {
        try
        {
            int channels = BitConverter.ToInt16(wavData, 22);
            int hz = BitConverter.ToInt32(wavData, 24);
            int dataIdx = 44;
            int bytes = wavData.Length - dataIdx;
            int samples = bytes / 2;
            float[] f = new float[samples];
            int j = 0;
            for (int i = dataIdx; i < wavData.Length; i += 2)
            {
                short s = (short)(wavData[i] | (wavData[i + 1] << 8));
                f[j++] = s / 32768f;
            }
            var clip = AudioClip.Create(clipName, samples / channels, channels, hz, false);
            clip.SetData(f, 0);
            return clip;
        }
        catch (Exception e)
        {
            Debug.LogError("WAV parse error: " + e);
            return null;
        }
    }

    // STT yardım mesajları
    IEnumerator ShowSttHelp()
    {
        if (!subtitle) yield break;
        
        string[] helpMessages = {
            "Ses tanınmadı. Lütfen tekrar konuşun.",
            "Mikrofonunuzun çalıştığından emin olun.",
            "Daha net ve yavaş konuşmayı deneyin.",
            "Gürültülü ortamdan uzaklaşın."
        };
        
        for (int i = 0; i < helpMessages.Length; i++)
        {
            subtitle.text = helpMessages[i];
            yield return new WaitForSeconds(2f);
        }
        
        subtitle.text = "R ile kayıt başlat, T ile durdur.";
    }
}
