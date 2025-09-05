from flask import Flask, request, jsonify
from flask_cors import CORS
import vosk
import json
import wave
import os
import tempfile
from werkzeug.utils import secure_filename

app = Flask(__name__)
CORS(app)

# Vosk model yolu - Türkçe model için
MODEL_PATH = "vosk-model-small-tr-0.3"
model = None

def load_model():
    global model
    try:
        if os.path.exists(MODEL_PATH):
            model = vosk.Model(MODEL_PATH)
            print(f"Model yüklendi: {MODEL_PATH}")
        else:
            print(f"Model bulunamadı: {MODEL_PATH}")
            print("Türkçe model indiriliyor...")
            # Model yoksa basit bir fallback kullan
            model = None
    except Exception as e:
        print(f"Model yükleme hatası: {e}")
        model = None

@app.route('/health', methods=['GET'])
def health_check():
    return jsonify({
        "status": "ok",
        "model_loaded": model is not None,
        "model_path": MODEL_PATH
    })

@app.route('/transcribe', methods=['POST'])
def transcribe():
    try:
        if 'audio' not in request.files:
            return jsonify({"error": "Audio file not found"}), 400
        
        audio_file = request.files['audio']
        if audio_file.filename == '':
            return jsonify({"error": "No file selected"}), 400
        
        # Geçici dosya oluştur
        with tempfile.NamedTemporaryFile(delete=False, suffix='.wav') as temp_file:
            audio_file.save(temp_file.name)
            temp_path = temp_file.name
        
        try:
            # WAV dosyasını oku
            with wave.open(temp_path, 'rb') as wav_file:
                # Ses parametrelerini kontrol et
                channels = wav_file.getnchannels()
                sample_width = wav_file.getsampwidth()
                sample_rate = wav_file.getframerate()
                
                print(f"Audio: {channels} channels, {sample_width} bytes, {sample_rate} Hz")
                
                # Ses verisini oku
                audio_data = wav_file.readframes(wav_file.getnframes())
                
                if model is None:
                    # Model yoksa basit bir test yanıtı döndür
                    return jsonify({
                        "text": "Test: Model henüz yüklenmedi",
                        "partial": "",
                        "model_status": "not_loaded"
                    })
                
                # Vosk recognizer oluştur
                rec = vosk.KaldiRecognizer(model, sample_rate)
                rec.SetWords(True)
                
                # Ses tanıma
                if rec.AcceptWaveform(audio_data):
                    result = json.loads(rec.Result())
                    text = result.get('text', '')
                    print(f"Recognition result: {text}")
                    
                    return jsonify({
                        "text": text,
                        "partial": "",
                        "model_status": "loaded"
                    })
                else:
                    # Partial result
                    partial = json.loads(rec.PartialResult())
                    partial_text = partial.get('partial', '')
                    print(f"Partial result: {partial_text}")
                    
                    return jsonify({
                        "text": partial_text,
                        "partial": partial_text,
                        "model_status": "loaded"
                    })
                    
        finally:
            # Geçici dosyayı sil
            os.unlink(temp_path)
            
    except Exception as e:
        print(f"Transcription error: {e}")
        return jsonify({"error": str(e)}), 500

@app.route('/download_model', methods=['POST'])
def download_model():
    """Türkçe modeli indir"""
    try:
        import urllib.request
        import zipfile
        
        model_url = "https://alphacephei.com/vosk/models/vosk-model-small-tr-0.3.zip"
        zip_path = "vosk-model-small-tr-0.3.zip"
        
        print(f"Model indiriliyor: {model_url}")
        
        # Model zip dosyasını indir
        urllib.request.urlretrieve(model_url, zip_path)
        
        # Zip dosyasını çıkar
        with zipfile.ZipFile(zip_path, 'r') as zip_ref:
            zip_ref.extractall(".")
        
        # Zip dosyasını sil
        os.remove(zip_path)
        
        # Modeli yeniden yükle
        load_model()
        
        return jsonify({
            "status": "success",
            "message": "Model başarıyla indirildi ve yüklendi"
        })
        
    except Exception as e:
        print(f"Model indirme hatası: {e}")
        return jsonify({"error": str(e)}), 500

if __name__ == '__main__':
    print("Vosk STT Servisi başlatılıyor...")
    print("Model yükleniyor...")
    
    load_model()
    
    if model is None:
        print("UYARI: Model yüklenemedi!")
        print("Model indirmek için: POST /download_model")
    
    print("Servis http://localhost:5001 adresinde başlatılıyor...")
    app.run(host='0.0.0.0', port=5001, debug=True)
