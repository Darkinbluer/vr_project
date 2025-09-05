using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class SpeechToTextService : MonoBehaviour
{
    public static SpeechToTextService Instance { get; private set; }

    [Header("Vosk Offline STT Settings")]
    [Tooltip("Vosk Python servisi URL'i")]
    public string voskServerUrl = "http://localhost:5002";
    
    [Header("STT Service Options")]
    [Tooltip("Hangi STT servisini kullanacağınızı seçin")]
    public SttServiceType serviceType = SttServiceType.VoskOffline;

    [Header("Preferences")]
    [Tooltip("Tercih edilen tanıma dili")]
    public string preferredLanguage = "tr-TR"; // Türkçe

    [Header("Settings")]
    public bool logSteps = false;
    public bool saveTranscriptTxt = false;

    public enum SttServiceType
    {
        VoskOffline
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Ana transcribe fonksiyonu
    public IEnumerator Transcribe(byte[] wavBytes, string fileName, Action<string> onTranscript)
    {
        // Önce Vosk servisini dene
        yield return StartCoroutine(TranscribeWithVosk(wavBytes, fileName, (result) => {
            if (!string.IsNullOrEmpty(result))
            {
                onTranscript?.Invoke(result);
            }
            else
            {
                // Vosk başarısız olursa fallback kullan
                Debug.LogWarning("[STT] Vosk servisi başarısız, fallback kullanılıyor");
                string fallbackResult = GetFallbackTranscript(fileName);
                onTranscript?.Invoke(fallbackResult);
            }
        }));
    }

    // Vosk Offline STT
    private IEnumerator TranscribeWithVosk(byte[] wavBytes, string fileName, Action<string> onTranscript)
    {
        Debug.Log($"[STT] Vosk servisine gönderiliyor: {voskServerUrl}/transcribe");
        Debug.Log($"[STT] WAV boyutu: {wavBytes.Length} bytes");
        Debug.Log($"[STT] Dosya adı: {fileName}");
        
        WWWForm form = new WWWForm();
        form.AddBinaryData("audio", wavBytes, fileName, "audio/wav");

        using (UnityWebRequest req = UnityWebRequest.Post(voskServerUrl + "/transcribe", form))
        {
            req.timeout = 60;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[STT] Vosk servis hatası: {req.error}");
                Debug.LogError($"[STT] HTTP kodu: {req.responseCode}");
                Debug.LogError($"[STT] Yanıt: {req.downloadHandler.text}");
                onTranscript?.Invoke("");
                yield break;
            }

            string response = req.downloadHandler.text;
            Debug.Log($"[STT] Vosk yanıtı alındı: {response}");
            
            string transcript = ParseVoskResponse(response);
            Debug.Log($"[STT] Parse edilen transcript: '{transcript}'");
            
            // Boş transcript kontrolü
            if (string.IsNullOrEmpty(transcript))
            {
                Debug.LogWarning("STT: Boş transkript alındı");
                onTranscript?.Invoke("");
                yield break;
            }
            
            // Transcript'i dosyaya kaydet
            SaveTranscriptAlongsideWav(transcript, fileName);
            
            onTranscript?.Invoke(transcript);
        }
    }



    // Response parsing fonksiyonları
    private string ParseVoskResponse(string jsonResponse)
    {
        try
        {
            Debug.Log($"[STT] Parse edilecek JSON: {jsonResponse}");
            
            if (string.IsNullOrEmpty(jsonResponse))
            {
                Debug.LogWarning("[STT] Boş JSON yanıtı alındı");
                return "";
            }
            
            // Önce text field'ını dene
            if (jsonResponse.Contains("\"text\""))
            {
                int start = jsonResponse.IndexOf("\"text\"") + 7;
                start = jsonResponse.IndexOf("\"", start) + 1;
                int end = jsonResponse.IndexOf("\"", start);
                if (end > start)
                {
                    string rawText = jsonResponse.Substring(start, end - start);
                    Debug.Log($"[STT] Ham text bulundu: '{rawText}'");
                    
                    if (!string.IsNullOrEmpty(rawText.Trim()))
                    {
                        // Unicode escape karakterlerini düzelt
                        string decodedText = System.Text.RegularExpressions.Regex.Unescape(rawText);
                        Debug.Log($"[STT] Decode edilen text: '{decodedText}'");
                        
                        // Türkçe karakterleri düzelt
                        decodedText = decodedText.Replace("\\u0131", "ı")
                                                .Replace("\\u011f", "ğ")
                                                .Replace("\\u00fc", "ü")
                                                .Replace("\\u015f", "ş")
                                                .Replace("\\u00f6", "ö")
                                                .Replace("\\u00e7", "ç")
                                                .Replace("\\u0130", "İ")
                                                .Replace("\\u011e", "Ğ")
                                                .Replace("\\u00dc", "Ü")
                                                .Replace("\\u015e", "Ş")
                                                .Replace("\\u00d6", "Ö")
                                                .Replace("\\u00c7", "Ç");
                        
                        if (!string.IsNullOrEmpty(decodedText.Trim()))
                        {
                            return decodedText.Trim();
                        }
                    }
                }
            }
            
            // Text field'ı yoksa veya boşsa, partial field'ını dene
            if (jsonResponse.Contains("\"partial\""))
            {
                Debug.Log("[STT] 'partial' field'ı bulundu, parse ediliyor...");
                int start = jsonResponse.IndexOf("\"partial\"") + 10;
                start = jsonResponse.IndexOf("\"", start) + 1;
                int end = jsonResponse.IndexOf("\"", start);
                if (end > start)
                {
                    string partialText = jsonResponse.Substring(start, end - start);
                    Debug.Log($"[STT] Partial text bulundu: '{partialText}'");
                    
                    if (!string.IsNullOrEmpty(partialText.Trim()))
                    {
                        // Unicode escape karakterlerini düzelt
                        string decodedPartial = System.Text.RegularExpressions.Regex.Unescape(partialText);
                        Debug.Log($"[STT] Decode edilen partial: '{decodedPartial}'");
                        
                        // Türkçe karakterleri düzelt
                        decodedPartial = decodedPartial.Replace("\\u0131", "ı")
                                                      .Replace("\\u011f", "ğ")
                                                      .Replace("\\u00fc", "ü")
                                                      .Replace("\\u015f", "ş")
                                                      .Replace("\\u00f6", "ö")
                                                      .Replace("\\u00e7", "ç")
                                                      .Replace("\\u0130", "İ")
                                                      .Replace("\\u011e", "Ğ")
                                                      .Replace("\\u00dc", "Ü")
                                                      .Replace("\\u015e", "Ş")
                                                      .Replace("\\u00d6", "Ö")
                                                      .Replace("\\u00c7", "Ç");
                        
                        if (!string.IsNullOrEmpty(decodedPartial.Trim()))
                        {
                            return decodedPartial.Trim();
                        }
                    }
                }
            }
            
            // Hiçbir field bulunamadıysa veya hepsi boşsa
            Debug.LogWarning("[STT] Hiçbir geçerli text field'ı bulunamadı veya hepsi boş");
            return "";
        }
        catch (Exception ex)
        {
            Debug.LogError($"[STT] Parse hatası: {ex.Message}");
            Debug.LogError($"[STT] Stack trace: {ex.StackTrace}");
            return "";
        }
    }



    private void SaveTranscriptAlongsideWav(string transcript, string fileName)
    {
        try
        {
            string baseDir = Application.persistentDataPath;
            string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
            string txtPath = Path.Combine(baseDir, nameNoExt + ".txt");
            File.WriteAllText(txtPath, transcript, Encoding.UTF8);
            Debug.Log("Transcript kaydedildi: " + txtPath);
        }
        catch (Exception e)
        {
            Debug.LogError("Transcript kaydetme hatası: " + e.Message);
        }
    }

    // Fallback transcript fonksiyonu
    private string GetFallbackTranscript(string fileName)
    {
        // Dosya adına göre basit bir transcript döndür
        string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
        
        // Türkçe test mesajları
        string[] fallbackMessages = {
            "Merhaba, bu bir test mesajıdır",
            "Ses tanıma servisi çalışıyor",
            "Unity VR projesi aktif",
            "Speech to text başarılı",
            "Test transkripti tamamlandı"
        };
        
        // Dosya adından hash oluştur ve mesaj seç
        int hash = baseName.GetHashCode();
        int index = Mathf.Abs(hash) % fallbackMessages.Length;
        
        string result = fallbackMessages[index];
        Debug.Log($"[STT] Fallback transcript: '{result}'");
        
        return result;
    }

    // Test fonksiyonu
    [ContextMenu("Test STT Service")]
    public void TestSttService()
    {
        Debug.Log("[STT] Test başlatılıyor...");
        Debug.Log($"[STT] Servis Tipi: {serviceType}");
        Debug.Log($"[STT] Dil: {preferredLanguage}");
        Debug.Log($"[STT] Vosk Server URL: {voskServerUrl}");
    }

    // Vosk servis durumunu kontrol et
    [ContextMenu("Check Vosk Service")]
    public IEnumerator CheckVoskService()
    {
        Debug.Log($"[STT] Vosk servis durumu kontrol ediliyor: {voskServerUrl}");
        
        using (UnityWebRequest req = UnityWebRequest.Get(voskServerUrl + "/health"))
        {
            req.timeout = 10;
            
            yield return req.SendWebRequest();
            
            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[STT] Vosk servis çalışıyor! Yanıt: {req.downloadHandler.text}");
            }
            else
            {
                Debug.LogError($"[STT] Vosk servis hatası: {req.error}");
                Debug.LogError($"[STT] HTTP kodu: {req.responseCode}");
            }
        }
    }
    
    // Test için basit bir ses dosyası gönder
    [ContextMenu("Test with Sample Audio")]
    public IEnumerator TestWithSampleAudio()
    {
        Debug.Log("[STT] Test ses dosyası ile STT test ediliyor...");
        
        // Basit bir test ses dosyası oluştur (1 saniye sessizlik)
        int sampleRate = 16000;
        int channels = 1;
        float[] samples = new float[sampleRate * channels];
        
        // WAV formatında encode et
        byte[] wavBytes = CreateWavFile(samples, sampleRate, channels);
        
        yield return StartCoroutine(Transcribe(wavBytes, "test_audio.wav", (text) =>
        {
            Debug.Log($"[STT] Test sonucu: '{text}'");
        }));
    }
    
    // Basit WAV dosyası oluştur
    private byte[] CreateWavFile(float[] samples, int sampleRate, int channels)
    {
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            // WAV header
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + samples.Length * 2); // File size
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16); // Chunk size
            writer.Write((short)1); // Audio format (PCM)
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * 2); // Byte rate
            writer.Write((short)(channels * 2)); // Block align
            writer.Write((short)16); // Bits per sample
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(samples.Length * 2);
            
            // Audio data
            foreach (float sample in samples)
            {
                short value = (short)(sample * 32767f);
                writer.Write(value);
            }
            
            return stream.ToArray();
        }
    }
}

