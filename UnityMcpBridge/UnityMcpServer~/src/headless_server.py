#!/usr/bin/env python3
"""
Unity MCP Headless Server - Extended for REST API and headless Unity operations.

This server extends the existing MCP server with REST API endpoints for command execution,
designed for headless Unity operations in containerized environments.

Features:
- REST API endpoints (/execute-command, /health, /status)
- Concurrent command handling (up to 5 simultaneous)
- Comprehensive logging for debugging
- Compatible with existing Unity MCP Bridge
- Designed for Docker deployment
"""

import asyncio
import json
import logging
import os
import sys
import time
import uuid
from contextlib import asynccontextmanager
from dataclasses import dataclass, asdict
from datetime import datetime
from pathlib import Path
from typing import Dict, Any, List, Optional
from concurrent.futures import ThreadPoolExecutor
import threading
import http.server
import socketserver
from urllib.parse import urlparse, parse_qs

# Import existing MCP server components
from config import config
from unity_connection import get_unity_connection, send_command_with_retry

# Configure enhanced logging for headless mode
LOG_FORMAT = "%(asctime)s - %(name)s - %(levelname)s - [%(threadName)s] %(message)s"
logging.basicConfig(
    level=getattr(logging, os.getenv("LOG_LEVEL", config.log_level)),
    format=LOG_FORMAT,
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler("/tmp/unity-mcp-headless.log") if os.path.exists("/tmp") else logging.NullHandler()
    ]
)
logger = logging.getLogger("unity-mcp-headless")

@dataclass
class CommandExecution:
    command_id: str
    action: str
    params: Dict[str, Any]
    user_id: str
    created_at: datetime
    status: str = "pending"  # pending, running, completed, failed
    result: Optional[Dict[str, Any]] = None
    error: Optional[str] = None
    execution_time: Optional[float] = None

class HeadlessUnityServer:
    """Enhanced Unity MCP server with REST API and headless support."""
    
    def __init__(self):
        self.command_queue: Dict[str, CommandExecution] = {}
        self.active_commands = 0
        self.max_concurrent_commands = 5
        self.executor = ThreadPoolExecutor(max_workers=self.max_concurrent_commands)
        self.command_lock = threading.Lock()
        self.unity_connection = None
        
        # Performance metrics
        self.total_commands = 0
        self.successful_commands = 0
        self.failed_commands = 0
        self.avg_execution_time = 0.0
        self.start_time = time.time()
        
        logger.info("HeadlessUnityServer initialized")
    
    def initialize_unity_connection(self):
        """Initialize Unity connection."""
        try:
            self.unity_connection = get_unity_connection()
            logger.info("Successfully connected to Unity")
            return True
        except Exception as e:
            logger.warning(f"Could not connect to Unity: {e}")
            self.unity_connection = None
            return False
    
    def execute_command(self, action: str, params: Dict[str, Any], user_id: str = "anonymous", timeout: float = 30.0) -> Dict[str, Any]:
        """Execute a Unity command synchronously."""
        
        # Check concurrent command limit
        if self.active_commands >= self.max_concurrent_commands:
            return {
                "success": False,
                "error": f"Maximum concurrent commands ({self.max_concurrent_commands}) exceeded"
            }
        
        command_id = str(uuid.uuid4())
        start_time = time.time()
        
        # Create command execution record
        command_exec = CommandExecution(
            command_id=command_id,
            action=action,
            params=params,
            user_id=user_id,
            created_at=datetime.utcnow()
        )
        
        with self.command_lock:
            self.command_queue[command_id] = command_exec
            self.active_commands += 1
        
        try:
            # Update status to running
            command_exec.status = "running"
            
            logger.info(f"Executing command {command_id}: {action}")
            
            # Map action to MCP tool command
            mcp_command = self._map_action_to_mcp_command(action)
            
            # Execute command with retry logic
            result = send_command_with_retry(mcp_command, params)
            
            execution_time = time.time() - start_time
            
            # Update command execution record
            with self.command_lock:
                command_exec.status = "completed"
                command_exec.result = result
                command_exec.execution_time = execution_time
                self.active_commands -= 1
                self.total_commands += 1
                self.successful_commands += 1
                
                # Update average execution time
                if self.total_commands > 0:
                    self.avg_execution_time = (
                        (self.avg_execution_time * (self.total_commands - 1) + execution_time) 
                        / self.total_commands
                    )
            
            logger.info(f"Completed command {command_id} in {execution_time:.2f}s")
            
            return {
                "success": True,
                "data": result,
                "commandId": command_id,
                "executionTime": execution_time
            }
            
        except Exception as e:
            execution_time = time.time() - start_time
            error_msg = str(e)
            
            logger.error(f"Command {command_id} failed: {error_msg}")
            
            # Update command execution record
            with self.command_lock:
                command_exec.status = "failed"
                command_exec.error = error_msg
                command_exec.execution_time = execution_time
                self.active_commands -= 1
                self.total_commands += 1
                self.failed_commands += 1
            
            return {
                "success": False,
                "error": error_msg,
                "commandId": command_id,
                "executionTime": execution_time
            }
    
    def get_command_status(self, command_id: str) -> Dict[str, Any]:
        """Get the status and result of a command execution."""
        
        with self.command_lock:
            if command_id not in self.command_queue:
                return {"success": False, "error": "Command not found"}
            
            command_exec = self.command_queue[command_id]
        
        if command_exec.status == "completed":
            return {
                "success": True,
                "data": command_exec.result,
                "message": "Command completed successfully",
                "commandId": command_id,
                "executionTime": command_exec.execution_time
            }
        elif command_exec.status == "failed":
            return {
                "success": False,
                "error": command_exec.error,
                "message": "Command execution failed",
                "commandId": command_id,
                "executionTime": command_exec.execution_time
            }
        else:
            return {
                "success": True,
                "message": f"Command is {command_exec.status}",
                "commandId": command_id
            }
    
    def health_check(self) -> Dict[str, Any]:
        """Health check for Kubernetes readiness/liveness probes."""
        try:
            # Test Unity connection
            if not self.unity_connection:
                self.initialize_unity_connection()
            
            unity_connected = False
            if self.unity_connection:
                # Send ping to Unity
                result = self.unity_connection.send_command("ping")
                unity_connected = result.get("message") == "pong"
            
            return {
                "status": "healthy" if unity_connected else "degraded",
                "unity_connected": unity_connected,
                "active_commands": self.active_commands,
                "total_commands": self.total_commands,
                "timestamp": datetime.utcnow().isoformat()
            }
        except Exception as e:
            logger.error(f"Health check failed: {e}")
            return {
                "status": "unhealthy",
                "unity_connected": False,
                "error": str(e),
                "timestamp": datetime.utcnow().isoformat()
            }
    
    def readiness_check(self) -> Dict[str, Any]:
        """Readiness check for Kubernetes readiness probes."""
        try:
            # Check server health
            health = self.health_check()
            
            # Additional readiness checks
            server_ready = True
            unity_ready = health.get("unity_connected", False)
            
            # Check if we're not overloaded
            if self.active_commands >= self.max_concurrent_commands:
                server_ready = False
            
            # Check uptime (server should be running for at least 30 seconds to be ready)
            uptime = time.time() - self.start_time
            if uptime < 30:
                server_ready = False
            
            # System resource checks (if available)
            system_resources = self._check_system_resources()
            
            overall_ready = server_ready and (unity_ready or os.getenv("CI_MODE", "false") == "true")
            
            return {
                "server_ready": server_ready,
                "unity_ready": unity_ready,
                "overall_ready": overall_ready,
                "uptime_seconds": int(uptime),
                "active_commands": self.active_commands,
                "max_commands": self.max_concurrent_commands,
                "system_resources": system_resources,
                "timestamp": datetime.utcnow().isoformat()
            }
        except Exception as e:
            logger.error(f"Readiness check failed: {e}")
            return {
                "server_ready": False,
                "unity_ready": False,
                "overall_ready": False,
                "error": str(e),
                "timestamp": datetime.utcnow().isoformat()
            }
    
    def _check_system_resources(self) -> Dict[str, Any]:
        """Check system resource availability."""
        resources: Dict[str, Any] = {
            "memory_ok": True,
            "disk_ok": True,
            "cpu_ok": True
        }
        
        try:
            import psutil
            
            # Memory check
            memory = psutil.virtual_memory()
            memory_ok = memory.percent <= 90
            resources["memory_ok"] = memory_ok
            resources["memory_percent"] = memory.percent
            
            # Disk check
            disk = psutil.disk_usage('/')
            disk_ok = disk.percent <= 85
            resources["disk_ok"] = disk_ok
            resources["disk_percent"] = disk.percent
            
            # CPU check (average over last minute)
            cpu_percent = psutil.cpu_percent(interval=1)
            cpu_ok = cpu_percent <= 95
            resources["cpu_ok"] = cpu_ok
            resources["cpu_percent"] = cpu_percent
            
        except ImportError:
            # psutil not available, assume resources are OK
            logger.debug("psutil not available for resource checking")
        except Exception as e:
            logger.warning(f"Resource check failed: {e}")
        
        return resources
    
    def server_status(self) -> Dict[str, Any]:
        """Detailed server status including performance metrics."""
        return {
            "server": "Unity MCP Headless Server",
            "version": "1.0.0",
            "uptime": time.time() - self.start_time,
            "active_commands": self.active_commands,
            "max_concurrent_commands": self.max_concurrent_commands,
            "queue_size": len(self.command_queue),
            "metrics": {
                "total_commands": self.total_commands,
                "successful_commands": self.successful_commands,
                "failed_commands": self.failed_commands,
                "success_rate": self.successful_commands / max(self.total_commands, 1) * 100,
                "avg_execution_time": self.avg_execution_time
            },
            "unity_connected": self.unity_connection is not None,
            "timestamp": datetime.utcnow().isoformat()
        }
    
    def clear_completed_commands(self) -> Dict[str, Any]:
        """Clear completed/failed commands from the queue to free memory."""
        cleared = 0
        with self.command_lock:
            to_remove = [
                cmd_id for cmd_id, cmd_exec in self.command_queue.items()
                if cmd_exec.status in ["completed", "failed"]
            ]
            for cmd_id in to_remove:
                del self.command_queue[cmd_id]
                cleared += 1
        
        logger.info(f"Cleared {cleared} completed commands from queue")
        return {
            "message": f"Cleared {cleared} completed commands",
            "remaining_commands": len(self.command_queue)
        }
    
    def _map_action_to_mcp_command(self, action: str) -> str:
        """Map REST API action to MCP tool command."""
        # Common action mappings
        action_map = {
            "create_gameobject": "manage_gameobject",
            "create_scene": "manage_scene", 
            "load_scene": "manage_scene",
            "build_webgl": "manage_editor",
            "get_scene_state": "manage_scene",
            "create_material": "manage_asset",
            "create_script": "manage_script",
            # Add more mappings as needed
        }
        
        return action_map.get(action, action)

class HeadlessHTTPHandler(http.server.BaseHTTPRequestHandler):
    """HTTP handler for the headless Unity server."""
    
    def __init__(self, request, client_address, server):
        super().__init__(request, client_address, server)
        
    @property 
    def server_instance(self):
        """Access the server instance."""
        # Cast server to ThreadedHTTPServer to access server_instance
        return getattr(self.server, 'server_instance', None)
    
    def log_message(self, format, *args):
        """Override to use our logger."""
        logger.info(f"{self.address_string()} - {format % args}")
    
    def _send_json_response(self, data: Dict[str, Any], status: int = 200):
        """Send JSON response."""
        self.send_response(status)
        self.send_header('Content-type', 'application/json')
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'GET, POST, DELETE, OPTIONS')
        self.send_header('Access-Control-Allow-Headers', 'Content-Type')
        self.end_headers()
        
        json_data = json.dumps(data, indent=2, default=str)
        self.wfile.write(json_data.encode('utf-8'))
    
    def _parse_json_body(self) -> Dict[str, Any]:
        """Parse JSON body from POST request."""
        content_length = int(self.headers.get('Content-Length', 0))
        if content_length > 0:
            body = self.rfile.read(content_length).decode('utf-8')
            return json.loads(body)
        return {}
    
    def do_OPTIONS(self):
        """Handle CORS preflight."""
        self.send_response(200)
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'GET, POST, DELETE, OPTIONS')
        self.send_header('Access-Control-Allow-Headers', 'Content-Type')
        self.end_headers()
    
    def do_POST(self):
        """Handle POST requests."""
        try:
            if not self.server_instance:
                self._send_json_response({"error": "Server instance not available"}, 500)
                return
                
            if self.path == '/execute-command':
                data = self._parse_json_body()
                
                action = data.get('action')
                params = data.get('params', {})
                user_id = data.get('userId', 'anonymous')
                timeout = data.get('timeout', 30.0)
                
                if not action:
                    self._send_json_response({
                        "success": False,
                        "error": "Missing required field: action"
                    }, 400)
                    return
                
                result = self.server_instance.execute_command(action, params, user_id, timeout)
                self._send_json_response(result)
            else:
                self._send_json_response({"error": "Not found"}, 404)
                
        except Exception as e:
            logger.error(f"POST request error: {e}")
            self._send_json_response({"error": f"Server error: {str(e)}"}, 500)
    
    def do_GET(self):
        """Handle GET requests."""
        try:
            if not self.server_instance:
                self._send_json_response({"error": "Server instance not available"}, 500)
                return
                
            parsed = urlparse(self.path)
            path = parsed.path
            query = parse_qs(parsed.query)
            
            if path == '/health':
                result = self.server_instance.health_check()
                self._send_json_response(result)
            elif path == '/ready':
                result = self.server_instance.readiness_check()
                self._send_json_response(result)
            elif path == '/status':
                result = self.server_instance.server_status()
                self._send_json_response(result)
            elif path.startswith('/command/'):
                command_id = path.split('/')[-1]
                result = self.server_instance.get_command_status(command_id)
                self._send_json_response(result)
            else:
                self._send_json_response({"error": "Not found"}, 404)
                
        except Exception as e:
            logger.error(f"GET request error: {e}")
            self._send_json_response({"error": f"Server error: {str(e)}"}, 500)
    
    def do_DELETE(self):
        """Handle DELETE requests."""
        try:
            if not self.server_instance:
                self._send_json_response({"error": "Server instance not available"}, 500)
                return
                
            if self.path == '/commands':
                result = self.server_instance.clear_completed_commands()
                self._send_json_response(result)
            else:
                self._send_json_response({"error": "Not found"}, 404)
                
        except Exception as e:
            logger.error(f"DELETE request error: {e}")
            self._send_json_response({"error": f"Server error: {str(e)}"}, 500)

class ThreadedHTTPServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    """Threaded HTTP server for handling concurrent requests."""
    allow_reuse_address = True
    
    def __init__(self, server_address, RequestHandlerClass, server_instance):
        self.server_instance = server_instance
        super().__init__(server_address, RequestHandlerClass)

def main():
    """Main entry point for headless server."""
    # Parse command line arguments
    import argparse
    parser = argparse.ArgumentParser(description="Unity MCP Headless Server")
    parser.add_argument("--host", default="0.0.0.0", help="Host to bind to")
    parser.add_argument("--port", type=int, default=8080, help="Port to bind to") 
    parser.add_argument("--unity-port", type=int, default=6400, help="Unity connection port")
    parser.add_argument("--max-concurrent", type=int, default=5, help="Max concurrent commands")
    parser.add_argument("--log-level", default="INFO", help="Log level")
    
    args = parser.parse_args()
    
    # Update configuration
    config.unity_port = args.unity_port
    
    # Set log level
    logging.getLogger().setLevel(getattr(logging, args.log_level.upper()))
    
    # Check if Unity is running in headless mode
    if not os.getenv("UNITY_HEADLESS", "false").lower() == "true":
        logger.warning("UNITY_HEADLESS environment variable not set. Ensure Unity is running in headless mode.")
    
    # Create server instance
    server_instance = HeadlessUnityServer()
    server_instance.max_concurrent_commands = args.max_concurrent
    
    # Initialize Unity connection
    server_instance.initialize_unity_connection()
    
    # Create HTTP server
    server_address = (args.host, args.port)
    httpd = ThreadedHTTPServer(server_address, HeadlessHTTPHandler, server_instance)
    
    logger.info(f"Starting Unity MCP Headless Server on {args.host}:{args.port}")
    logger.info(f"Max concurrent commands: {args.max_concurrent}")
    logger.info(f"Unity port: {args.unity_port}")
    
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        logger.info("Server stopped by user")
    except Exception as e:
        logger.error(f"Server error: {e}")
        sys.exit(1)
    finally:
        httpd.server_close()
        server_instance.executor.shutdown(wait=True)

if __name__ == "__main__":
    main()