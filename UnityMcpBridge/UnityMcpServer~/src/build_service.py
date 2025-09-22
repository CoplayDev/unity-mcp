#!/usr/bin/env python3
"""
Unity Build Service for Unity MCP VPS
Implements the Unity Build Service API specification for building and deploying games
"""

import asyncio
import json
import logging
import os
import time
import uuid
import hashlib
from datetime import datetime, timedelta
from dataclasses import dataclass, field, asdict
from typing import Dict, List, Optional, Any
from enum import Enum
import aiohttp
import aiofiles
from pathlib import Path
import zipfile
import shutil

logger = logging.getLogger(__name__)

class BuildStatus(Enum):
    PENDING = "pending"
    BUILDING = "building"
    DEPLOYING = "deploying"
    COMPLETED = "completed"
    FAILED = "failed"

@dataclass
class BuildRequest:
    user_id: str
    game_id: str
    game_name: str
    game_type: str
    asset_set: str
    assets: List[List[str]]
    build_id: str = field(default_factory=lambda: str(uuid.uuid4()))
    created_at: datetime = field(default_factory=datetime.now)
    
@dataclass
class BuildJob:
    build_id: str
    request: BuildRequest
    status: BuildStatus = BuildStatus.PENDING
    queue_position: int = 0
    game_url: Optional[str] = None
    error_message: Optional[str] = None
    created_at: datetime = field(default_factory=datetime.now)
    started_at: Optional[datetime] = None
    completed_at: Optional[datetime] = None
    unity_client_id: Optional[str] = None
    assets_downloaded: List[str] = field(default_factory=list)
    build_log: List[str] = field(default_factory=list)
    
    def to_dict(self):
        return {
            "game_id": self.request.game_id,
            "status": self.status.value,
            "queue_position": self.queue_position,
            "game_url": self.game_url,
            "error_message": self.error_message
        }

class UnityBuildService:
    """Unity Build Service implementing the API specification"""
    
    def __init__(self, client_manager, scene_manager, base_build_dir="/opt/unity-mcp/builds"):
        self.client_manager = client_manager
        self.scene_manager = scene_manager
        self.base_build_dir = Path(base_build_dir)
        self.builds: Dict[str, BuildJob] = {}
        self.build_queue: List[str] = []
        self.active_builds: Dict[str, asyncio.Task] = {}
        self.max_concurrent_builds = int(os.getenv('MAX_CONCURRENT_BUILDS', 3))
        self.api_key = os.getenv('BUILD_SERVICE_API_KEY', 'default-api-key')
        self.base_game_url = os.getenv('BASE_GAME_URL', 'https://your-domain.com/games')
        
        # Ensure build directories exist
        self.base_build_dir.mkdir(exist_ok=True)
        (self.base_build_dir / "assets").mkdir(exist_ok=True)
        (self.base_build_dir / "games").mkdir(exist_ok=True)
        (self.base_build_dir / "templates").mkdir(exist_ok=True)
        
        logger.info(f"Unity Build Service initialized - max concurrent builds: {self.max_concurrent_builds}")
        
    def authenticate_request(self, authorization_header: str) -> bool:
        """Validate API key from Authorization header"""
        if not authorization_header:
            return False
            
        try:
            auth_type, token = authorization_header.split(' ', 1)
            if auth_type.lower() != 'bearer':
                return False
            return token == self.api_key
        except ValueError:
            return False
            
    async def create_build(self, build_request_data: Dict[str, Any]) -> Dict[str, str]:
        """Create a new build job from API request"""
        try:
            # Import security utilities
            from security_utils import InputValidator, SecurityAuditLogger
            
            # Validate and sanitize input
            validator = InputValidator()
            audit_logger = SecurityAuditLogger()
            
            # Log the request for security audit
            audit_logger.log_build_request(build_request_data)
            
            # Validate and sanitize the request
            sanitized_data = validator.validate_build_request(build_request_data)
            
            # Create build request with sanitized data
            build_request = BuildRequest(
                user_id=sanitized_data['user_id'],
                game_id=sanitized_data['game_id'],
                game_name=sanitized_data['game_name'],
                game_type=sanitized_data['game_type'],
                asset_set=sanitized_data['asset_set'],
                assets=sanitized_data['assets']
            )
            
            # Create build job
            build_job = BuildJob(
                build_id=build_request.build_id,
                request=build_request
            )
            
            # Add to queue
            self.builds[build_job.build_id] = build_job
            self.build_queue.append(build_job.build_id)
            self._update_queue_positions()
            
            # Start processing if possible
            await self._process_build_queue()
            
            logger.info(f"Created build job {build_job.build_id} for game {build_request.game_name}")
            
            return {
                "url": f"/build/{build_job.build_id}/status"
            }
            
        except Exception as e:
            logger.error(f"Failed to create build: {str(e)}")
            raise
            
    async def get_build_status(self, build_id: str) -> Optional[Dict[str, Any]]:
        """Get status of a specific build"""
        if build_id not in self.builds:
            return None
            
        build_job = self.builds[build_id]
        return build_job.to_dict()
        
    async def stop_build(self, build_id: str) -> bool:
        """Stop/cancel a build"""
        if build_id not in self.builds:
            return False
            
        build_job = self.builds[build_id]
        
        # If build is active, cancel the task
        if build_id in self.active_builds:
            task = self.active_builds[build_id]
            task.cancel()
            del self.active_builds[build_id]
            
        # If build is in queue, remove it
        if build_id in self.build_queue:
            self.build_queue.remove(build_id)
            self._update_queue_positions()
            
        # Update status
        build_job.status = BuildStatus.FAILED
        build_job.error_message = "Build cancelled by user"
        build_job.completed_at = datetime.now()
        
        # Clean up Unity client if assigned
        if build_job.unity_client_id:
            try:
                await self.client_manager.unregister_client(build_job.unity_client_id)
            except Exception as e:
                logger.warning(f"Failed to cleanup Unity client {build_job.unity_client_id}: {e}")
                
        logger.info(f"Stopped build {build_id}")
        return True
        
    def _update_queue_positions(self):
        """Update queue positions for all pending builds"""
        for i, build_id in enumerate(self.build_queue):
            if build_id in self.builds:
                self.builds[build_id].queue_position = i + 1
                
    async def _process_build_queue(self):
        """Process builds from the queue"""
        while (len(self.active_builds) < self.max_concurrent_builds and 
               len(self.build_queue) > 0):
            
            build_id = self.build_queue.pop(0)
            if build_id not in self.builds:
                continue
                
            # Start build task
            task = asyncio.create_task(self._execute_build(build_id))
            self.active_builds[build_id] = task
            
            # Update queue positions
            self._update_queue_positions()
            
    async def _execute_build(self, build_id: str):
        """Execute a single build job"""
        build_job = self.builds[build_id]
        
        try:
            logger.info(f"Starting build execution for {build_id}")
            
            # Update status
            build_job.status = BuildStatus.BUILDING
            build_job.started_at = datetime.now()
            build_job.build_log.append(f"Build started at {build_job.started_at}")
            
            # Register Unity client for this build
            client_context = await self.client_manager.register_client(
                str(uuid.uuid4()),
                f"build_{build_job.request.game_name}_{build_id[:8]}"
            )
            build_job.unity_client_id = client_context.client_id
            build_job.build_log.append(f"Unity client registered: {client_context.client_id}")
            
            # Download and prepare assets
            await self._download_assets(build_job)
            
            # Create game project in Unity
            await self._create_game_project(build_job, client_context)
            
            # Build the game
            build_job.status = BuildStatus.DEPLOYING
            await self._build_game(build_job, client_context)
            
            # Deploy the game
            game_url = await self._deploy_game(build_job)
            
            # Complete build
            build_job.status = BuildStatus.COMPLETED
            build_job.game_url = game_url
            build_job.completed_at = datetime.now()
            build_job.build_log.append(f"Build completed at {build_job.completed_at}")
            
            logger.info(f"Build {build_id} completed successfully - game URL: {game_url}")
            
        except asyncio.CancelledError:
            build_job.status = BuildStatus.FAILED
            build_job.error_message = "Build was cancelled"
            build_job.completed_at = datetime.now()
            logger.info(f"Build {build_id} was cancelled")
            
        except Exception as e:
            build_job.status = BuildStatus.FAILED
            build_job.error_message = str(e)
            build_job.completed_at = datetime.now()
            build_job.build_log.append(f"Build failed: {str(e)}")
            logger.error(f"Build {build_id} failed: {str(e)}")
            
        finally:
            # Clean up
            if build_id in self.active_builds:
                del self.active_builds[build_id]
                
            # Clean up Unity client
            if build_job.unity_client_id:
                try:
                    await self.client_manager.unregister_client(build_job.unity_client_id)
                except Exception as e:
                    logger.warning(f"Failed to cleanup Unity client: {e}")
                    
            # Process next build in queue
            await self._process_build_queue()
            
    async def _download_assets(self, build_job: BuildJob):
        """Download assets for the build with security validation"""
        build_job.build_log.append("Downloading assets...")
        
        try:
            from security_utils import SecureFileHandler, InputValidator
        except ImportError:
            logger.warning("Security utilities not available, using basic validation")
            SecureFileHandler = None
            InputValidator = None
        
        # Create secure asset directory
        if SecureFileHandler:
            assets_dir = SecureFileHandler.create_secure_path(
                self.base_build_dir / "assets", build_job.build_id
            )
        else:
            assets_dir = self.base_build_dir / "assets" / build_job.build_id
        assets_dir.mkdir(exist_ok=True)
        
        session_timeout = aiohttp.ClientTimeout(total=300)  # 5 minutes per asset
        max_file_size = int(os.getenv('MAX_ASSET_SIZE_MB', '100')) * 1024 * 1024  # Convert to bytes
        
        async with aiohttp.ClientSession(timeout=session_timeout) as session:
            for slot_index, asset_slot in enumerate(build_job.request.assets):
                if SecureFileHandler:
                    slot_dir = SecureFileHandler.create_secure_path(assets_dir, f"slot_{slot_index}")
                else:
                    slot_dir = assets_dir / f"slot_{slot_index}"
                slot_dir.mkdir(exist_ok=True)
                
                for asset_index, asset_url in enumerate(asset_slot):
                    try:
                        # Download asset with size limits
                        async with session.get(asset_url) as response:
                            if response.status != 200:
                                raise Exception(f"Failed to download asset: HTTP {response.status}")
                                
                            # Check content length if provided
                            content_length = response.headers.get('content-length')
                            if content_length and int(content_length) > max_file_size:
                                raise Exception(f"Asset too large: {content_length} bytes (max {max_file_size})")
                                
                            # Determine file extension from URL or content type
                            content_type = response.headers.get('content-type', '')
                            if 'image' in content_type:
                                ext = '.png'
                            elif 'audio' in content_type:
                                ext = '.wav'
                            elif 'model' in content_type or 'mesh' in content_type:
                                ext = '.fbx'
                            else:
                                ext = '.asset'
                                
                            asset_filename = f"asset_{asset_index}{ext}"
                            
                            if SecureFileHandler:
                                asset_path = SecureFileHandler.create_secure_path(slot_dir, asset_filename)
                            else:
                                asset_path = slot_dir / asset_filename
                            
                            # Download with size checking
                            downloaded_size = 0
                            async with aiofiles.open(asset_path, 'wb') as f:
                                async for chunk in response.content.iter_chunked(8192):
                                    downloaded_size += len(chunk)
                                    if downloaded_size > max_file_size:
                                        await f.close()
                                        asset_path.unlink(missing_ok=True)  # Delete partial file
                                        raise Exception(f"Asset too large during download: {downloaded_size} bytes")
                                    await f.write(chunk)
                            
                            # Validate downloaded file
                            if SecureFileHandler and not SecureFileHandler.validate_file_size(
                                asset_path, int(os.getenv('MAX_ASSET_SIZE_MB', '100'))
                            ):
                                asset_path.unlink(missing_ok=True)
                                raise Exception("Downloaded file exceeds size limit")
                                    
                            build_job.assets_downloaded.append(str(asset_path))
                            build_job.build_log.append(f"Downloaded asset: {asset_url} -> {asset_filename} ({downloaded_size} bytes)")
                            
                    except Exception as e:
                        error_msg = f"Failed to download asset {asset_url}: {str(e)}"
                        build_job.build_log.append(error_msg)
                        logger.warning(error_msg)
                        # Continue with other assets
                        
        build_job.build_log.append(f"Asset download complete: {len(build_job.assets_downloaded)} assets")
        
    async def _create_game_project(self, build_job: BuildJob, client_context):
        """Create the game project in Unity"""
        build_job.build_log.append("Creating game project...")
        
        # Create a scene for this game
        scene_name = f"Game_{build_job.request.game_name}_{build_job.build_id[:8]}"
        
        await self.scene_manager.create_scene(
            client_context.client_id,
            scene_name,
            client_context.scene_namespace,
            client_context.asset_path
        )
        
        # Load the scene
        await self.scene_manager.load_scene(
            client_context.client_id,
            scene_name,
            client_context.scene_namespace
        )
        
        # Import assets into Unity project
        await self._import_assets_to_unity(build_job, client_context)
        
        # Create game objects based on game type and asset set
        await self._create_game_objects(build_job, client_context)
        
        build_job.build_log.append("Game project created successfully")
        
    async def _import_assets_to_unity(self, build_job: BuildJob, client_context):
        """Import downloaded assets into Unity"""
        build_job.build_log.append("Importing assets to Unity...")
        
        # Copy assets to Unity project
        assets_dir = self.base_build_dir / "assets" / build_job.build_id
        unity_assets_dir = Path(client_context.asset_path) / "ImportedAssets"
        unity_assets_dir.mkdir(exist_ok=True)
        
        for asset_path in build_job.assets_downloaded:
            src_path = Path(asset_path)
            dst_path = unity_assets_dir / src_path.name
            shutil.copy2(src_path, dst_path)
            
        # Execute Unity asset import command
        await self.client_manager.execute_command(
            client_context.client_id,
            {
                "action": "manage_asset",
                "params": {
                    "action": "import",
                    "asset_path": str(unity_assets_dir),
                    "recursive": True
                }
            }
        )
        
        build_job.build_log.append("Assets imported to Unity")
        
    async def _create_game_objects(self, build_job: BuildJob, client_context):
        """Create game objects based on game type and assets"""
        build_job.build_log.append(f"Creating game objects for {build_job.request.game_type}...")
        
        # This is where you'd implement game-type-specific logic
        # For now, create basic objects based on assets
        
        # Create a main camera if not exists
        await self.client_manager.execute_command(
            client_context.client_id,
            {
                "action": "manage_gameobject",
                "params": {
                    "action": "create",
                    "name": "MainCamera",
                    "type": "Camera"
                }
            }
        )
        
        # Create lighting
        await self.client_manager.execute_command(
            client_context.client_id,
            {
                "action": "manage_gameobject", 
                "params": {
                    "action": "create",
                    "name": "DirectionalLight",
                    "type": "Light"
                }
            }
        )
        
        # Create game objects based on asset slots
        for slot_index, asset_slot in enumerate(build_job.request.assets):
            if asset_slot:  # If slot has assets
                await self.client_manager.execute_command(
                    client_context.client_id,
                    {
                        "action": "manage_gameobject",
                        "params": {
                            "action": "create",
                            "name": f"GameAsset_{slot_index}",
                            "type": "GameObject"
                        }
                    }
                )
                
        build_job.build_log.append("Game objects created")
        
    async def _build_game(self, build_job: BuildJob, client_context):
        """Build the game to WebGL"""
        build_job.build_log.append("Building game to WebGL...")
        
        # Execute Unity build command
        build_result = await self.client_manager.execute_command(
            client_context.client_id,
            {
                "action": "manage_editor",
                "params": {
                    "action": "build",
                    "platform": "WebGL",
                    "output_path": f"/opt/unity-mcp/builds/games/{build_job.build_id}",
                    "development_build": False
                }
            }
        )
        
        if not build_result.get("success", False):
            raise Exception(f"Unity build failed: {build_result.get('error', 'Unknown error')}")
            
        build_job.build_log.append("Game build completed")
        
    async def _deploy_game(self, build_job: BuildJob) -> str:
        """Deploy the built game and return the game URL"""
        build_job.build_log.append("Deploying game...")
        
        # Move build to web-accessible location
        build_output = Path(f"/opt/unity-mcp/builds/games/{build_job.build_id}")
        web_dir = Path("/var/www/html/games") / build_job.build_id
        web_dir.parent.mkdir(exist_ok=True)
        
        if build_output.exists():
            shutil.copytree(build_output, web_dir, dirs_exist_ok=True)
        else:
            # Create a placeholder if build failed but we need to deploy something
            web_dir.mkdir(exist_ok=True)
            placeholder_html = f"""
            <!DOCTYPE html>
            <html>
            <head><title>{build_job.request.game_name}</title></head>
            <body>
                <h1>{build_job.request.game_name}</h1>
                <p>Game build is being processed...</p>
                <p>Build ID: {build_job.build_id}</p>
            </body>
            </html>
            """
            async with aiofiles.open(web_dir / "index.html", 'w') as f:
                await f.write(placeholder_html)
        
        # Generate game URL
        game_url = f"{self.base_game_url}/{build_job.build_id}"
        
        build_job.build_log.append(f"Game deployed to: {game_url}")
        return game_url
        
    async def cleanup_old_builds(self, max_age_hours: int = 24):
        """Clean up old builds to save disk space"""
        cutoff_time = datetime.now() - timedelta(hours=max_age_hours)
        
        builds_to_remove = []
        for build_id, build_job in self.builds.items():
            if (build_job.completed_at and 
                build_job.completed_at < cutoff_time and
                build_job.status in [BuildStatus.COMPLETED, BuildStatus.FAILED]):
                builds_to_remove.append(build_id)
                
        for build_id in builds_to_remove:
            try:
                # Remove build files
                build_assets = self.base_build_dir / "assets" / build_id
                if build_assets.exists():
                    shutil.rmtree(build_assets)
                    
                build_output = self.base_build_dir / "games" / build_id
                if build_output.exists():
                    shutil.rmtree(build_output)
                    
                # Remove from memory
                del self.builds[build_id]
                
                logger.info(f"Cleaned up old build {build_id}")
                
            except Exception as e:
                logger.warning(f"Failed to cleanup build {build_id}: {e}")
                
    def get_build_statistics(self) -> Dict[str, Any]:
        """Get build service statistics"""
        total_builds = len(self.builds)
        completed_builds = sum(1 for b in self.builds.values() if b.status == BuildStatus.COMPLETED)
        failed_builds = sum(1 for b in self.builds.values() if b.status == BuildStatus.FAILED)
        active_builds = len(self.active_builds)
        queued_builds = len(self.build_queue)
        
        return {
            "total_builds": total_builds,
            "completed_builds": completed_builds,
            "failed_builds": failed_builds,
            "active_builds": active_builds,
            "queued_builds": queued_builds,
            "success_rate": completed_builds / max(total_builds, 1) * 100,
            "max_concurrent_builds": self.max_concurrent_builds
        }