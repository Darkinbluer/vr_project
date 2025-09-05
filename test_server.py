#!/usr/bin/env python3
import http.server
import socketserver
import json
import threading
import time

class SimpleSTTHandler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == '/health':
            self.send_response(200)
            self.send_header('Content-type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            
            response = {
                "status": "ok",
                "service": "Simple STT Server",
                "model_loaded": True,
                "message": "Server çalışıyor!"
            }
            self.wfile.write(json.dumps(response, ensure_ascii=False).encode('utf-8'))
            print(f"[{time.strftime('%H:%M:%S')}] Health check - OK")
            
        else:
            self.send_response(404)
            self.end_headers()
            self.wfile.write(b"Not Found")
    
    def do_POST(self):
        if self.path == '/transcribe':
            try:
                # Content-Length header'ını al
                content_length = int(self.headers.get('Content-Length', 0))
                print(f"[{time.strftime('%H:%M:%S')}] Transcribe request - Content-Length: {content_length}")
                
                # POST data'yı oku
                post_data = self.rfile.read(content_length)
                print(f"[{time.strftime('%H:%M:%S')}] POST data alındı: {len(post_data)} bytes")
                
                # Başarılı yanıt döndür
                self.send_response(200)
                self.send_header('Content-type', 'application/json')
                self.send_header('Access-Control-Allow-Origin', '*')
                self.end_headers()
                
                # Test transcript'i
                test_response = {
                    "text": "Merhaba, bu bir test transkriptidir. Unity bağlantısı başarılı!",
                    "partial": "",
                    "model_status": "test_mode",
                    "success": True
                }
                
                response_json = json.dumps(test_response, ensure_ascii=False)
                self.wfile.write(response_json.encode('utf-8'))
                print(f"[{time.strftime('%H:%M:%S')}] Test transcript gönderildi")
                
            except Exception as e:
                print(f"[{time.strftime('%H:%M:%S')}] Hata: {e}")
                self.send_response(500)
                self.end_headers()
                self.wfile.write(f"Error: {e}".encode())
            
        else:
            self.send_response(404)
            self.end_headers()
            self.wfile.write(b"Not Found")
    
    def do_OPTIONS(self):
        # CORS preflight request
        self.send_response(200)
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
        self.send_header('Access-Control-Allow-Headers', 'Content-Type')
        self.end_headers()
        print(f"[{time.strftime('%H:%M:%S')}] CORS preflight request")

    def log_message(self, format, *args):
        # Varsayılan log mesajlarını bastır
        pass

if __name__ == '__main__':
    PORT = 5001
    
    print(f"[{time.strftime('%H:%M:%S')}] Simple STT Server başlatılıyor...")
    print(f"[{time.strftime('%H:%M:%S')}] Port: {PORT}")
    print(f"[{time.strftime('%H:%M:%S')}] URL: http://localhost:{PORT}")
    print(f"[{time.strftime('%H:%M:%S')}] Unity'de test edebilirsiniz!")
    print(f"[{time.strftime('%H:%M:%S')}] Ctrl+C ile durdurun")
    print("-" * 50)
    
    try:
        with socketserver.TCPServer(("", PORT), SimpleSTTHandler) as httpd:
            httpd.serve_forever()
    except KeyboardInterrupt:
        print(f"\n[{time.strftime('%H:%M:%S')}] Server durduruldu.")
    except Exception as e:
        print(f"[{time.strftime('%H:%M:%S')}] Server hatası: {e}")

