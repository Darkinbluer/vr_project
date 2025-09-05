import http.server
import socketserver
import json
import urllib.parse
from urllib.parse import parse_qs

class TestSTTHandler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == '/health':
            self.send_response(200)
            self.send_header('Content-type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            
            response = {
                "status": "ok",
                "service": "Test STT Server",
                "model_loaded": True
            }
            self.wfile.write(json.dumps(response).encode())
            
        else:
            self.send_response(404)
            self.end_headers()
            self.wfile.write(b"Not Found")
    
    def do_POST(self):
        if self.path == '/transcribe':
            # Content-Length header'ını al
            content_length = int(self.headers.get('Content-Length', 0))
            
            # Multipart form data'yı basitçe parse et
            post_data = self.rfile.read(content_length)
            
            # Basit bir test yanıtı döndür
            self.send_response(200)
            self.send_header('Content-type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            
            # Test transcript'i
            test_response = {
                "text": "Merhaba, bu bir test transkriptidir",
                "partial": "",
                "model_status": "test_mode"
            }
            
            self.wfile.write(json.dumps(test_response).encode())
            
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

if __name__ == '__main__':
    PORT = 5002
    
    with socketserver.TCPServer(("", PORT), TestSTTHandler) as httpd:
        print(f"Test STT Server başlatıldı: http://localhost:{PORT}")
        print("Unity'de test edebilirsiniz!")
        print("Ctrl+C ile durdurun")
        
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nServer durduruldu.")
            httpd.shutdown()
