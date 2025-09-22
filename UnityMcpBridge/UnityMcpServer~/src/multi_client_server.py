#!/usr/bin/env python3
"""
Multi-client Unity MCP Server for VPS Deployment
Handles up to 5 concurrent clients with isolated scenes and namespaces
"""

import asyncio
import json
import logging
import os
import time
import uuid
from dataclasses import dataclass, field, asdict
from typing import Dict, Optional, List, Any
from datetime import datetime, timedelta
import aiohttp
from aiohttp import web
import aiofiles
import signal
import sys
from pathlib import Path

# Import existing MCP components
try:
    from config import config
    from unity_connection import get_unity_connection, send_command_with_retry
    from client_manager import ClientIsolationManager
    from scene_manager import SceneManager
except ImportError as e:
    print(f"Warning: Could not import MCP components: {e}")
    # Provide mock implementations for testing
    class MockConfig:
        log_level = "INFO"
        log_format = "%(asctime)s - %(name)s - %(levelname)s - %(message)s"
    config = MockConfig()

# Configure logging
logging.basicConfig(
    level=getattr(logging, config.log_level),
    format=config.log_format,
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler("/opt/unity-mcp/logs/multi-client-server.log") if os.path.exists("/opt/unity-mcp/logs") else logging.NullHandler()
    ]
)
logger = logging.getLogger('unity-mcp-multi')

@dataclass
class ServerStats:
    """Server performance and usage statistics"""
    start_time: datetime = field(default_factory=datetime.now)
    total_commands: int = 0
    successful_commands: int = 0
    failed_commands: int = 0
    active_clients: int = 0
    max_clients_reached: int = 0
    avg_response_time: float = 0.0
    unity_restarts: int = 0
    
    def to_dict(self):
        return {
            **asdict(self),
            'start_time': self.start_time.isoformat(),
            'uptime_seconds': (datetime.now() - self.start_time).total_seconds()
        }

class MultiClientUnityServer:
    """Multi-tenant Unity MCP server for VPS deployment"""
    
    def __init__(self, max_clients: int = None, unity_project_path: str = "/opt/unity-mcp/projects/shared"):
        # Default to 10 clients, but allow unlimited if set to 0
        if max_clients is None:
            max_clients = int(os.getenv('MAX_CLIENTS', 10))
        self.max_clients = max_clients
        self.unity_project_path = unity_project_path
        self.stats = ServerStats()
        
        # Core managers
        self.client_manager = ClientIsolationManager(self.max_clients)
        self.scene_manager = SceneManager()
        
        # Unity process management
        self.unity_process = None
        self.unity_connection = None
        self.unity_ready = False
        
        # Web server
        self.app = web.Application()
        self.setup_routes()
        self.setup_middleware()
        
        # Command processing
        self.command_queue = asyncio.Queue()
        self.command_processor_task = None
        
        # Graceful shutdown
        self.shutdown_event = asyncio.Event()
        
        logger.info(f"MultiClientUnityServer initialized for {self.max_clients} clients ({'unlimited' if self.max_clients == 0 else 'limited'})")
        
    def setup_routes(self):
        """Setup HTTP API routes"""
        self.app.router.add_get('/health', self.health_check)
        self.app.router.add_get('/status', self.detailed_status)
        self.app.router.add_get('/metrics', self.prometheus_metrics)
        
        # Client management
        self.app.router.add_post('/api/register-client', self.register_client)
        self.app.router.add_delete('/api/clients/{client_id}', self.unregister_client)
        self.app.router.add_get('/api/clients/{client_id}/status', self.client_status)
        self.app.router.add_get('/api/clients', self.list_clients)
        
        # Command execution
        self.app.router.add_post('/api/execute-command', self.execute_command)
        self.app.router.add_get('/api/commands/{command_id}', self.get_command_status)
        
        # Scene management
        self.app.router.add_post('/api/clients/{client_id}/scenes', self.create_scene)
        self.app.router.add_get('/api/clients/{client_id}/scenes', self.list_scenes)
        self.app.router.add_post('/api/clients/{client_id}/scenes/{scene_name}/load', self.load_scene)
        
        # Admin endpoints
        self.app.router.add_post('/api/admin/restart-unity', self.restart_unity)
        self.app.router.add_post('/api/admin/cleanup-idle', self.cleanup_idle_clients)
        
        logger.info("API routes configured")
        
    def setup_middleware(self):
        """Setup middleware for logging, CORS, etc."""
        
        @web.middleware
        async def logging_middleware(request, handler):
            start_time = time.time()
            try:
                response = await handler(request)
                process_time = time.time() - start_time
                logger.info(f"{request.method} {request.path} - {response.status} - {process_time:.3f}s")
                return response
            except Exception as e:
                process_time = time.time() - start_time
                logger.error(f"{request.method} {request.path} - ERROR: {str(e)} - {process_time:.3f}s")
                raise
                
        @web.middleware
        async def cors_middleware(request, handler):
            if request.method == 'OPTIONS':
                response = web.Response()
            else:
                response = await handler(request)
                
            response.headers['Access-Control-Allow-Origin'] = '*'
            response.headers['Access-Control-Allow-Methods'] = 'GET, POST, DELETE, OPTIONS'
            response.headers['Access-Control-Allow-Headers'] = 'Content-Type, Authorization'
            return response
            
        self.app.middlewares.append(logging_middleware)
        self.app.middlewares.append(cors_middleware)
        
    async def start_unity_headless(self):
        """Start Unity in headless mode"""
        unity_path = os.getenv('UNITY_PATH', '/opt/unity/editors/6000.0.3f1/Editor/Unity')
        
        if not os.path.exists(unity_path):
            logger.error(f"Unity not found at {unity_path}")
            return False
            
        # Ensure project directory exists
        os.makedirs(self.unity_project_path, exist_ok=True)
        
        cmd = [
            unity_path,
            '-batchmode',
            '-nographics',
            '-projectPath', self.unity_project_path,
            '-logFile', '/opt/unity-mcp/logs/unity.log',
            '-executeMethod', 'UnityMcpBridge.StartHeadlessServer'
        ]
        
        try:
            logger.info(f"Starting Unity headless: {' '.join(cmd)}")
            self.unity_process = await asyncio.create_subprocess_exec(
                *cmd,
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
                env={**os.environ, 'DISPLAY': ':99'}
            )
            
            # Wait a bit for Unity to start
            await asyncio.sleep(10)
            
            # Try to establish MCP connection
            await self.connect_to_unity()
            
            self.unity_ready = True
            logger.info("Unity headless server started successfully")
            return True
            
        except Exception as e:
            logger.error(f"Failed to start Unity: {str(e)}")
            return False
            
    async def connect_to_unity(self):
        """Connect to Unity MCP bridge"""
        try:
            self.unity_connection = get_unity_connection()
            logger.info("Connected to Unity MCP bridge")
        except Exception as e:
            logger.warning(f"Could not connect to Unity MCP bridge: {str(e)}")
            self.unity_connection = None
            
    async def health_check(self, request):
        """Health check endpoint for load balancers"""
        health_status = {
            'status': 'healthy' if self.unity_ready else 'degraded',
            'unity_running': self.unity_process is not None and self.unity_process.returncode is None,
            'unity_ready': self.unity_ready,
            'active_clients': len(self.client_manager.clients),
            'max_clients': self.max_clients,
            'uptime': (datetime.now() - self.stats.start_time).total_seconds(),
            'timestamp': datetime.now().isoformat()
        }
        
        status_code = 200 if self.unity_ready else 503
        return web.json_response(health_status, status=status_code)
        
    async def detailed_status(self, request):
        """Detailed status endpoint"""
        return web.json_response({
            'server': self.stats.to_dict(),
            'unity': {
                'process_running': self.unity_process is not None and self.unity_process.returncode is None,
                'mcp_connected': self.unity_connection is not None,
                'ready': self.unity_ready,
                'project_path': self.unity_project_path
            },
            'clients': {
                'active': len(self.client_manager.clients),
                'max': self.max_clients,
                'sessions': [
                    {
                        'client_id': ctx.client_id,
                        'project_name': ctx.project_name,
                        'namespace': ctx.scene_namespace,
                        'commands': ctx.command_count,
                        'last_activity': ctx.last_activity.isoformat(),
                        'idle_minutes': (datetime.now() - ctx.last_activity).total_seconds() / 60
                    }
                    for ctx in self.client_manager.clients.values()
                ]
            }
        })
        
    async def prometheus_metrics(self, request):
        """Prometheus metrics endpoint"""
        uptime = (datetime.now() - self.stats.start_time).total_seconds()
        
        metrics = f"""# HELP unity_mcp_uptime_seconds Server uptime in seconds
# TYPE unity_mcp_uptime_seconds gauge
unity_mcp_uptime_seconds {uptime}

# HELP unity_mcp_clients_active Number of active clients
# TYPE unity_mcp_clients_active gauge
unity_mcp_clients_active {len(self.client_manager.clients)}

# HELP unity_mcp_clients_max Maximum number of clients
# TYPE unity_mcp_clients_max gauge
unity_mcp_clients_max {self.max_clients}

# HELP unity_mcp_commands_total Total number of commands processed
# TYPE unity_mcp_commands_total counter
unity_mcp_commands_total {self.stats.total_commands}

# HELP unity_mcp_commands_successful Number of successful commands
# TYPE unity_mcp_commands_successful counter
unity_mcp_commands_successful {self.stats.successful_commands}

# HELP unity_mcp_commands_failed Number of failed commands
# TYPE unity_mcp_commands_failed counter
unity_mcp_commands_failed {self.stats.failed_commands}

# HELP unity_mcp_unity_ready Unity readiness status
# TYPE unity_mcp_unity_ready gauge
unity_mcp_unity_ready {1 if self.unity_ready else 0}

# HELP unity_mcp_response_time_seconds Average response time
# TYPE unity_mcp_response_time_seconds gauge
unity_mcp_response_time_seconds {self.stats.avg_response_time}
"""
        
        return web.Response(text=metrics, content_type='text/plain')
        
    async def register_client(self, request):
        """Register new client session"""
        try:
            data = await request.json()
            project_name = data.get('project_name', f'project_{int(time.time())}')
            
            if self.max_clients > 0 and len(self.client_manager.clients) >= self.max_clients:
                return web.json_response(
                    {'error': f'Maximum clients reached ({self.max_clients} clients)', 'max_clients': self.max_clients},
                    status=503
                )
                
            # Register client
            client_context = await self.client_manager.register_client(
                str(uuid.uuid4()),
                project_name
            )
            
            self.stats.active_clients = len(self.client_manager.clients)
            if self.stats.active_clients > self.stats.max_clients_reached:
                self.stats.max_clients_reached = self.stats.active_clients
                
            logger.info(f"Registered client {client_context.client_id} for project {project_name}")
            
            return web.json_response({
                'client_id': client_context.client_id,
                'project_name': client_context.project_name,
                'scene_namespace': client_context.scene_namespace,
                'asset_path': client_context.asset_path,
                'created_at': client_context.created_at.isoformat()
            })
            
        except Exception as e:
            logger.error(f"Failed to register client: {str(e)}")
            return web.json_response({'error': str(e)}, status=500)
            
    async def execute_command(self, request):
        """Execute Unity command for specific client"""
        try:
            data = await request.json()
            client_id = data.get('client_id')
            action = data.get('action')
            params = data.get('params', {})
            
            if not client_id:
                return web.json_response({'error': 'client_id required'}, status=400)
                
            if not action:
                return web.json_response({'error': 'action required'}, status=400)
                
            if client_id not in self.client_manager.clients:
                return web.json_response({'error': 'Invalid client_id'}, status=401)
                
            start_time = time.time()
            
            # Execute command through client manager
            result = await self.client_manager.execute_command(client_id, {
                'action': action,
                'params': params
            })
            
            execution_time = time.time() - start_time
            
            # Update stats
            self.stats.total_commands += 1
            if result.get('success', True):
                self.stats.successful_commands += 1
            else:
                self.stats.failed_commands += 1
                
            # Update average response time
            total_successful = self.stats.successful_commands + self.stats.failed_commands
            self.stats.avg_response_time = (
                (self.stats.avg_response_time * (total_successful - 1) + execution_time) / total_successful
            )
            
            return web.json_response({
                'command_id': str(uuid.uuid4()),
                'status': 'completed',
                'result': result,
                'execution_time': execution_time,
                'timestamp': datetime.now().isoformat()
            })
            
        except Exception as e:
            self.stats.failed_commands += 1
            logger.error(f"Command execution failed: {str(e)}")
            return web.json_response({'error': str(e)}, status=500)
            
    async def restart_unity(self, request):
        """Restart Unity process (admin endpoint)"""
        try:
            logger.info("Restarting Unity process...")
            
            # Kill existing process
            if self.unity_process:
                self.unity_process.terminate()
                await asyncio.sleep(5)
                if self.unity_process.returncode is None:
                    self.unity_process.kill()
                    
            self.unity_ready = False
            self.unity_connection = None
            
            # Start new process
            success = await self.start_unity_headless()
            
            if success:
                self.stats.unity_restarts += 1
                return web.json_response({'status': 'Unity restarted successfully'})
            else:
                return web.json_response({'error': 'Failed to restart Unity'}, status=500)
                
        except Exception as e:
            logger.error(f"Failed to restart Unity: {str(e)}")
            return web.json_response({'error': str(e)}, status=500)
            
    async def cleanup_idle_clients(self, request):
        """Clean up idle client sessions"""
        try:
            cleaned = await self.client_manager.cleanup_idle_clients()
            self.stats.active_clients = len(self.client_manager.clients)
            
            return web.json_response({
                'cleaned_clients': cleaned,
                'remaining_clients': len(self.client_manager.clients)
            })
            
        except Exception as e:
            logger.error(f"Failed to cleanup idle clients: {str(e)}")
            return web.json_response({'error': str(e)}, status=500)
            
    async def unregister_client(self, request):
        """Unregister client endpoint"""
        try:
            client_id = request.match_info['client_id']
            await self.client_manager.unregister_client(client_id)
            self.stats.active_clients = len(self.client_manager.clients)
            
            return web.json_response({'status': f'Client {client_id} unregistered'})
            
        except Exception as e:
            logger.error(f"Failed to unregister client: {str(e)}")
            return web.json_response({'error': str(e)}, status=500)
            
    async def client_status(self, request):
        """Get status for specific client"""
        try:
            client_id = request.match_info['client_id']
            client_info = self.client_manager.get_client_info(client_id)
            
            if not client_info:
                return web.json_response({'error': 'Client not found'}, status=404)
                
            return web.json_response(client_info)
            
        except Exception as e:
            logger.error(f"Failed to get client status: {str(e)}")
            return web.json_response({'error': str(e)}, status=500)
            
    async def list_clients(self, request):
        """List all active clients"""
        try:
            clients = self.client_manager.list_all_clients()
            return web.json_response({
                'clients': clients,
                'total': len(clients),
                'max_clients': self.max_clients
            })
            
        except Exception as e:
            logger.error(f"Failed to list clients: {str(e)}")
            return web.json_response({'error': str(e)}, status=500)
            
    async def get_command_status(self, request):
        """Get status of a specific command"""
        try:
            command_id = request.match_info['command_id']
            # This would track command status in production
            return web.json_response({
                'command_id': command_id,
                'status': 'completed',
                'message': 'Mock command status'
            })
            
        except Exception as e:
            logger.error(f"Failed to get command status: {str(e)}")
            return web.json_response({'error': str(e)}, status=500)
            
    async def create_scene(self, request):
        """Create new scene for client"""
        try:
            client_id = request.match_info['client_id']
            data = await request.json()
            scene_name = data.get('scene_name', 'NewScene')
            
            if client_id not in self.client_manager.clients:
                return web.json_response({'error': 'Client not found'}, status=404)
                
            context = self.client_manager.clients[client_id]
            
            scene_info = await self.scene_manager.create_scene(
                client_id, 
                scene_name, 
                context.scene_namespace,
                context.asset_path
            )
            
            return web.json_response({
                'scene_name': scene_info.name,
                'full_name': f"{scene_info.namespace}_{scene_info.name}",
                'path': scene_info.path,
                'created_at': scene_info.created_at
            })
            
        except Exception as e:
            logger.error(f"Failed to create scene: {str(e)}")
            return web.json_response({'error': str(e)}, status=500)
            
    async def list_scenes(self, request):
        """List scenes for client"""
        try:
            client_id = request.match_info['client_id']
            
            if client_id not in self.client_manager.clients:
                return web.json_response({'error': 'Client not found'}, status=404)
                
            context = self.client_manager.clients[client_id]
            scenes = await self.scene_manager.list_client_scenes(client_id, context.scene_namespace)
            
            return web.json_response({
                'scenes': scenes,
                'namespace': context.scene_namespace,
                'total': len(scenes)
            })
            
        except Exception as e:
            logger.error(f"Failed to list scenes: {str(e)}")
            return web.json_response({'error': str(e)}, status=500)
            
    async def load_scene(self, request):
        """Load scene for client"""
        try:
            client_id = request.match_info['client_id']
            scene_name = request.match_info['scene_name']
            
            if client_id not in self.client_manager.clients:
                return web.json_response({'error': 'Client not found'}, status=404)
                
            context = self.client_manager.clients[client_id]
            
            success = await self.scene_manager.load_scene(
                client_id, 
                scene_name, 
                context.scene_namespace
            )
            
            if success:
                return web.json_response({
                    'status': f'Scene {scene_name} loaded successfully',
                    'namespace': context.scene_namespace
                })
            else:
                return web.json_response({'error': 'Failed to load scene'}, status=500)
                
        except Exception as e:
            logger.error(f"Failed to load scene: {str(e)}")
            return web.json_response({'error': str(e)}, status=500)
            
    async def setup_signal_handlers(self):
        """Setup graceful shutdown signal handlers"""
        loop = asyncio.get_event_loop()
        
        for sig in [signal.SIGTERM, signal.SIGINT]:
            loop.add_signal_handler(sig, self.shutdown_event.set)
            
    async def graceful_shutdown(self):
        """Perform graceful shutdown"""
        logger.info("Starting graceful shutdown...")
        
        # Stop accepting new requests
        # Cleanup all clients
        for client_id in list(self.client_manager.clients.keys()):
            await self.client_manager.unregister_client(client_id)
            
        # Stop Unity
        if self.unity_process:
            self.unity_process.terminate()
            await asyncio.sleep(5)
            if self.unity_process.returncode is None:
                self.unity_process.kill()
                
        logger.info("Graceful shutdown completed")
        
    async def run(self, host='0.0.0.0', port=8080):
        """Run the multi-client server"""
        logger.info(f"Starting Unity MCP Multi-Client Server on {host}:{port}")
        
        # Setup signal handlers
        await self.setup_signal_handlers()
        
        # Start Unity
        unity_started = await self.start_unity_headless()
        if not unity_started:
            logger.warning("Unity failed to start, running in degraded mode")
            
        # Start web server
        runner = web.AppRunner(self.app)
        await runner.setup()
        site = web.TCPSite(runner, host, port)
        await site.start()
        
        logger.info(f"Server running on http://{host}:{port}")
        logger.info("Health check: /health")
        logger.info("Status: /status")
        logger.info("Metrics: /metrics")
        
        # Start background tasks
        cleanup_task = asyncio.create_task(self.periodic_cleanup())
        
        try:
            # Wait for shutdown signal
            await self.shutdown_event.wait()
        finally:
            # Cleanup
            cleanup_task.cancel()
            await self.graceful_shutdown()
            await runner.cleanup()
            
    async def periodic_cleanup(self):
        """Periodic cleanup of idle clients"""
        while not self.shutdown_event.is_set():
            try:
                await asyncio.sleep(300)  # 5 minutes
                await self.client_manager.cleanup_idle_clients()
            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Periodic cleanup error: {str(e)}")

if __name__ == '__main__':
    # Configuration from environment
    max_clients = int(os.getenv('MAX_CLIENTS', 10))
    host = os.getenv('HOST', '0.0.0.0')
    port = int(os.getenv('PORT', 8080))
    unity_project = os.getenv('UNITY_PROJECT_PATH', '/opt/unity-mcp/projects/shared')
    
    server = MultiClientUnityServer(max_clients, unity_project)
    
    try:
        asyncio.run(server.run(host, port))
    except KeyboardInterrupt:
        logger.info("Server stopped by user")
    except Exception as e:
        logger.error(f"Server error: {str(e)}")
        sys.exit(1)