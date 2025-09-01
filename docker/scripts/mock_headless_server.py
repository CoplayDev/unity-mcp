#!/usr/bin/env python3
"""
Mock headless server for CI testing
Provides basic health endpoints without Unity functionality
"""

import argparse
import json
import logging
import time
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse, parse_qs

# Setup logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

# Global start time for the server
SERVER_START_TIME = time.time()

class MockHeadlessHandler(BaseHTTPRequestHandler):
    
    def do_GET(self):
        if self.path == '/health':
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.end_headers()
            health_data = {
                "status": "healthy",
                "timestamp": time.time(),
                "mode": "ci-mock",
                "unity_connected": False,
                "uptime_seconds": int(time.time() - SERVER_START_TIME)
            }
            self.wfile.write(json.dumps(health_data).encode())
        else:
            self.send_error(404, "Not Found")
    
    def do_POST(self):
        if self.path == '/execute-command':
            # Read request body
            content_length = int(self.headers.get('Content-Length', 0))
            post_data = self.rfile.read(content_length)
            
            try:
                request_data = json.loads(post_data.decode('utf-8'))
            except json.JSONDecodeError:
                self.send_error(400, "Invalid JSON")
                return
            
            # Mock command execution
            command_id = f"mock-cmd-{int(time.time() * 1000)}"
            
            response_data = {
                "commandId": command_id,
                "status": "completed",
                "result": {
                    "success": True,
                    "message": f"Mock execution of {request_data.get('action', 'unknown')} command",
                    "data": request_data.get('params', {})
                },
                "timestamp": time.time()
            }
            
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.end_headers()
            self.wfile.write(json.dumps(response_data).encode())
        else:
            self.send_error(404, "Not Found")
    
    def log_message(self, format, *args):
        logger.info(format % args)

def main():
    parser = argparse.ArgumentParser(description='Mock Unity MCP Headless Server')
    parser.add_argument('--host', default='0.0.0.0', help='Host to bind to')
    parser.add_argument('--port', type=int, default=8080, help='Port to listen on')
    parser.add_argument('--unity-port', type=int, default=6400, help='Unity MCP port (ignored in mock)')
    parser.add_argument('--log-level', default='INFO', help='Log level')
    parser.add_argument('--max-concurrent', type=int, default=5, help='Max concurrent commands (ignored in mock)')
    
    args = parser.parse_args()
    
    logger.info(f"Starting mock headless server on {args.host}:{args.port}")
    
    server = HTTPServer((args.host, args.port), MockHeadlessHandler)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        logger.info("Shutting down mock server")
        server.shutdown()

if __name__ == '__main__':
    main()