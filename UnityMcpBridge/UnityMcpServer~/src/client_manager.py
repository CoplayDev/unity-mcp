"""
Client Isolation Manager for Unity MCP VPS Deployment
Manages client isolation and resource allocation
"""

import asyncio
import logging
import os
import time
from typing import Dict, Optional, List, Set
from dataclasses import dataclass, field
from datetime import datetime, timedelta
import json
import shutil

logger = logging.getLogger(__name__)

@dataclass
class ClientContext:
    """Maintains client-specific context and isolation"""
    client_id: str
    project_name: str
    scene_namespace: str
    asset_path: str
    max_memory_mb: int = 2048  # 2GB per client
    max_assets: int = 1000
    created_at: datetime = field(default_factory=datetime.now)
    last_activity: datetime = field(default_factory=datetime.now)
    active_scenes: Set[str] = field(default_factory=set)
    loaded_assets: Set[str] = field(default_factory=set)
    command_history: List[Dict] = field(default_factory=list)
    command_count: int = 0
    
    def update_activity(self):
        self.last_activity = datetime.now()
        
    def is_idle(self, idle_minutes: int = 30) -> bool:
        return (datetime.now() - self.last_activity) > timedelta(minutes=idle_minutes)

class ResourceMonitor:
    """Monitor resource usage per client"""
    
    def __init__(self):
        self.client_memory_usage = {}
        
    async def get_client_memory(self, client_id: str) -> float:
        """Get memory usage for specific client"""
        # In production, this would interface with Unity's profiler
        # For now, return mock value based on activity
        return self.client_memory_usage.get(client_id, 512.0)
        
    async def update_client_memory(self, client_id: str, memory_mb: float):
        """Update memory usage for client"""
        self.client_memory_usage[client_id] = memory_mb
        
    async def get_total_memory(self) -> float:
        """Get total Unity process memory"""
        try:
            import psutil
            process = psutil.Process()
            return process.memory_info().rss / 1024 / 1024  # Convert to MB
        except ImportError:
            # Fallback if psutil not available
            return sum(self.client_memory_usage.values())

class ClientIsolationManager:
    """Manages client isolation and resource allocation"""
    
    def __init__(self, max_clients: int = 10):
        # max_clients = 0 means unlimited clients
        self.max_clients = max_clients
        self.clients: Dict[str, ClientContext] = {}
        self.scene_locks: Dict[str, str] = {}  # scene_name -> client_id
        self.resource_monitor = ResourceMonitor()
        self.base_path = "/opt/unity-mcp/projects"
        
    async def register_client(self, client_id: str, project_name: str) -> ClientContext:
        """Register new client with isolated resources"""
        if self.max_clients > 0 and len(self.clients) >= self.max_clients:
            raise Exception(f"Maximum client limit reached ({self.max_clients} clients)")
            
        # Create isolated directories
        project_path = os.path.join(self.base_path, project_name)
        asset_path = os.path.join(project_path, "Assets")
        
        # Create directory structure
        directories = [
            os.path.join(asset_path, "Scenes"),
            os.path.join(asset_path, "Scripts"),
            os.path.join(asset_path, "Prefabs"),
            os.path.join(asset_path, "Materials"),
            os.path.join(asset_path, "Textures"),
            os.path.join(project_path, "ProjectSettings"),
            os.path.join(project_path, "Packages")
        ]
        
        for directory in directories:
            os.makedirs(directory, exist_ok=True)
            
        # Create basic project files
        await self._create_project_files(project_path, project_name)
        
        # Create client context
        context = ClientContext(
            client_id=client_id,
            project_name=project_name,
            scene_namespace=f"Client_{client_id[:8]}",
            asset_path=asset_path
        )
        
        self.clients[client_id] = context
        logger.info(f"Registered client {client_id} with namespace {context.scene_namespace}")
        
        # Initialize client's default scene
        await self._create_default_scene(context)
        
        return context
        
    async def _create_project_files(self, project_path: str, project_name: str):
        """Create basic Unity project files"""
        # ProjectSettings/ProjectVersion.txt
        project_version_path = os.path.join(project_path, "ProjectSettings", "ProjectVersion.txt")
        with open(project_version_path, 'w') as f:
            f.write("m_EditorVersion: 6000.0.3f1\n")
            f.write("m_EditorVersionWithRevision: 6000.0.3f1 (6cd387ce4ddd)\n")
            
        # Packages/manifest.json
        manifest_path = os.path.join(project_path, "Packages", "manifest.json")
        manifest_content = {
            "dependencies": {
                "com.unity.collab-proxy": "2.4.4",
                "com.unity.feature.development": "1.0.3",
                "com.unity.textmeshpro": "3.2.0-pre.4",
                "com.unity.timeline": "1.8.7",
                "com.unity.ugui": "2.0.0",
                "com.unity.visualscripting": "1.9.4",
                "com.unity.modules.ai": "1.0.0",
                "com.unity.modules.androidjni": "1.0.0",
                "com.unity.modules.animation": "1.0.0",
                "com.unity.modules.assetbundle": "1.0.0",
                "com.unity.modules.audio": "1.0.0",
                "com.unity.modules.cloth": "1.0.0",
                "com.unity.modules.director": "1.0.0",
                "com.unity.modules.imageconversion": "1.0.0",
                "com.unity.modules.imgui": "1.0.0",
                "com.unity.modules.jsonserialize": "1.0.0",
                "com.unity.modules.particlesystem": "1.0.0",
                "com.unity.modules.physics": "1.0.0",
                "com.unity.modules.physics2d": "1.0.0",
                "com.unity.modules.screencapture": "1.0.0",
                "com.unity.modules.terrain": "1.0.0",
                "com.unity.modules.terrainphysics": "1.0.0",
                "com.unity.modules.tilemap": "1.0.0",
                "com.unity.modules.ui": "1.0.0",
                "com.unity.modules.uielements": "1.0.0",
                "com.unity.modules.umbra": "1.0.0",
                "com.unity.modules.unityanalytics": "1.0.0",
                "com.unity.modules.unitywebrequest": "1.0.0",
                "com.unity.modules.unitywebrequestassetbundle": "1.0.0",
                "com.unity.modules.unitywebrequestaudio": "1.0.0",
                "com.unity.modules.unitywebrequesttexture": "1.0.0",
                "com.unity.modules.unitywebrequestwww": "1.0.0",
                "com.unity.modules.vehicles": "1.0.0",
                "com.unity.modules.video": "1.0.0",
                "com.unity.modules.vr": "1.0.0",
                "com.unity.modules.wind": "1.0.0",
                "com.unity.modules.xr": "1.0.0"
            }
        }
        
        with open(manifest_path, 'w') as f:
            json.dump(manifest_content, f, indent=2)
            
    async def _create_default_scene(self, context: ClientContext):
        """Create default scene for client"""
        scene_name = f"{context.scene_namespace}_Main"
        scene_path = os.path.join(context.asset_path, "Scenes", f"{scene_name}.unity")
        
        # Create basic scene content (this would normally be done through Unity)
        scene_content = """% YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!29 &1
OcclusionCullingSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_OcclusionBakeSettings:
    smallestOccluder: 5
    smallestHole: 0.25
    backfaceThreshold: 100
  m_SceneGUID: 00000000000000000000000000000000
  m_OcclusionCullingData: {fileID: 0}
--- !u!104 &2
RenderSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 9
  m_Fog: 0
  m_FogColor: {r: 0.5, g: 0.5, b: 0.5, a: 1}
  m_FogMode: 3
  m_FogDensity: 0.01
  m_LinearFogStart: 0
  m_LinearFogEnd: 300
  m_AmbientSkyColor: {r: 0.212, g: 0.227, b: 0.259, a: 1}
  m_AmbientEquatorColor: {r: 0.114, g: 0.125, b: 0.133, a: 1}
  m_AmbientGroundColor: {r: 0.047, g: 0.043, b: 0.035, a: 1}
  m_AmbientIntensity: 1
  m_AmbientMode: 3
  m_SubtractiveShadowColor: {r: 0.42, g: 0.478, b: 0.627, a: 1}
  m_SkyboxMaterial: {fileID: 0}
  m_HaloStrength: 0.5
  m_FlareStrength: 1
  m_FlareFadeSpeed: 3
  m_HaloTexture: {fileID: 0}
  m_SpotCookie: {fileID: 10001, guid: 0000000000000000e000000000000000, type: 0}
  m_DefaultReflectionMode: 0
  m_DefaultReflectionResolution: 128
  m_ReflectionBounces: 1
  m_ReflectionIntensity: 1
  m_CustomReflection: {fileID: 0}
  m_Sun: {fileID: 0}
  m_IndirectSpecularColor: {r: 0, g: 0, b: 0, a: 1}
  m_UseRadianceAmbientProbe: 0
--- !u!157 &3
LightmapSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 12
  m_GIWorkflowMode: 1
  m_GISettings:
    serializedVersion: 2
    m_BounceScale: 1
    m_IndirectOutputScale: 1
    m_AlbedoBoost: 1
    m_EnvironmentLightingMode: 0
    m_EnableBakedLightmaps: 1
    m_EnableRealtimeLightmaps: 0
  m_LightmapEditorSettings:
    serializedVersion: 12
    m_Resolution: 2
    m_BakeResolution: 40
    m_AtlasSize: 1024
    m_AO: 0
    m_AOMaxDistance: 1
    m_CompAOExponent: 1
    m_CompAOExponentDirect: 0
    m_ExtractAmbientOcclusion: 0
    m_Padding: 2
    m_LightmapParameters: {fileID: 0}
    m_LightmapsBakeMode: 1
    m_TextureCompression: 1
    m_FinalGather: 0
    m_FinalGatherFiltering: 1
    m_FinalGatherRayCount: 256
    m_ReflectionCompression: 2
    m_MixedBakeMode: 2
    m_BakeBackend: 1
    m_PVRSampling: 1
    m_PVRDirectSampleCount: 32
    m_PVRSampleCount: 500
    m_PVRBounces: 2
    m_PVREnvironmentSampleCount: 500
    m_PVREnvironmentReferencePointCount: 2048
    m_PVRFilteringMode: 2
    m_PVRDenoiserTypeDirect: 0
    m_PVRDenoiserTypeIndirect: 0
    m_PVRDenoiserTypeAO: 0
    m_PVRFilterTypeDirect: 0
    m_PVRFilterTypeIndirect: 0
    m_PVRFilterTypeAO: 0
    m_PVREnvironmentMIS: 0
    m_PVRCulling: 1
    m_PVRFilteringGaussRadiusDirect: 1
    m_PVRFilteringGaussRadiusIndirect: 5
    m_PVRFilteringGaussRadiusAO: 2
    m_PVRFilteringAtrousPositionSigmaDirect: 0.5
    m_PVRFilteringAtrousPositionSigmaIndirect: 2
    m_PVRFilteringAtrousPositionSigmaAO: 1
    m_ExportTrainingData: 0
    m_TrainingDataDestination: TrainingData
    m_LightProbeSampleCountMultiplier: 4
  m_LightingDataAsset: {fileID: 0}
  m_LightingSettings: {fileID: 0}
--- !u!196 &4
NavMeshSettings:
  serializedVersion: 2
  m_ObjectHideFlags: 0
  m_BuildSettings:
    serializedVersion: 2
    agentTypeID: 0
    agentRadius: 0.5
    agentHeight: 2
    agentSlope: 45
    agentClimb: 0.4
    ledgeDropHeight: 0
    maxJumpAcrossDistance: 0
    minRegionArea: 2
    manualCellSize: 0
    cellSize: 0.16666667
    manualTileSize: 0
    tileSize: 256
    accuratePlacement: 0
    maxJobWorkers: 0
    preserveTilesOutsideBounds: 0
    debug:
      m_Flags: 0
  m_NavMeshData: {fileID: 0}
--- !u!1 &705507993
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInternal: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 705507995}
  - component: {fileID: 705507994}
  m_Layer: 0
  m_Name: Directional Light
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!108 &705507994
Light:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInternal: {fileID: 0}
  m_GameObject: {fileID: 705507993}
  m_Enabled: 1
  serializedVersion: 10
  m_Type: 1
  m_Shape: 0
  m_Color: {r: 1, g: 0.95686275, b: 0.8392157, a: 1}
  m_Intensity: 1
  m_Range: 10
  m_SpotAngle: 30
  m_InnerSpotAngle: 21.80208
  m_CookieSize: 10
  m_Shadows:
    m_Type: 2
    m_Resolution: -1
    m_CustomResolution: -1
    m_Strength: 1
    m_Bias: 0.05
    m_NormalBias: 0.4
    m_NearPlane: 2
    m_CullingMatrixOverride:
      e00: 1
      e01: 0
      e02: 0
      e03: 0
      e10: 0
      e11: 1
      e12: 0
      e13: 0
      e20: 0
      e21: 0
      e22: 1
      e23: 0
      e30: 0
      e31: 0
      e32: 0
      e33: 1
    m_UseCullingMatrixOverride: 0
  m_Cookie: {fileID: 0}
  m_DrawHalo: 0
  m_Flare: {fileID: 0}
  m_RenderMode: 0
  m_CullingMask:
    serializedVersion: 2
    m_Bits: 4294967295
  m_RenderingLayerMask: 1
  m_Lightmapping: 1
  m_LightShadowCasterMode: 0
  m_AreaSize: {x: 1, y: 1}
  m_BounceIntensity: 1
  m_ColorTemperature: 6570
  m_UseColorTemperature: 0
  m_BoundingSphereOverride: {x: 0, y: 0, z: 0, w: 0}
  m_UseBoundingSphereOverride: 0
  m_UseViewFrustumForShadowCasterCull: 1
  m_ShadowRadius: 0
  m_ShadowAngle: 0
--- !u!4 &705507995
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInternal: {fileID: 0}
  m_GameObject: {fileID: 705507993}
  m_LocalRotation: {x: 0.40821788, y: -0.23456968, z: 0.10938163, w: 0.8754261}
  m_LocalPosition: {x: 0, y: 3, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_Children: []
  m_Father: {fileID: 0}
  m_RootOrder: 1
  m_LocalEulerAnglesHint: {x: 50, y: -30, z: 0}
--- !u!1 &963194225
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInternal: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 963194228}
  - component: {fileID: 963194227}
  - component: {fileID: 963194226}
  m_Layer: 0
  m_Name: Main Camera
  m_TagString: MainCamera
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!81 &963194226
AudioListener:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInternal: {fileID: 0}
  m_GameObject: {fileID: 963194225}
  m_Enabled: 1
--- !u!20 &963194227
Camera:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInternal: {fileID: 0}
  m_GameObject: {fileID: 963194225}
  m_Enabled: 1
  serializedVersion: 2
  m_ClearFlags: 1
  m_BackGroundColor: {r: 0.19215687, g: 0.3019608, b: 0.4745098, a: 0}
  m_projectionMatrixMode: 1
  m_GateFitMode: 2
  m_FOVAxisMode: 0
  m_SensorSize: {x: 36, y: 24}
  m_LensShift: {x: 0, y: 0}
  m_FocalLength: 50
  m_NormalizedViewPortRect:
    serializedVersion: 2
    x: 0
    y: 0
    width: 1
    height: 1
  m_near: 0.3
  m_far: 1000
  m_orthographic: 0
  m_orthographicSize: 5
  m_depth: -1
  m_cullingMask:
    serializedVersion: 2
    m_Bits: 4294967295
  m_renderingPath: -1
  m_targetTexture: {fileID: 0}
  m_targetDisplay: 0
  m_targetEye: 3
  m_HDR: 1
  m_AllowMSAA: 1
  m_AllowDynamicResolution: 0
  m_ForceIntoRT: 0
  m_OcclusionCulling: 1
  m_StereoConvergence: 10
  m_StereoSeparation: 0.022
--- !u!4 &963194228
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInternal: {fileID: 0}
  m_GameObject: {fileID: 963194225}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 1, z: -10}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_Children: []
  m_Father: {fileID: 0}
  m_RootOrder: 0
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
"""
        
        # Write scene file
        with open(scene_path, 'w') as f:
            f.write(scene_content)
            
        # Add to active scenes
        context.active_scenes.add(scene_name)
        self.scene_locks[scene_name] = context.client_id
        
        logger.info(f"Created default scene {scene_name} for client {context.client_id}")
        
    async def execute_command(self, client_id: str, command: Dict) -> Dict:
        """Execute command in client's isolated context"""
        if client_id not in self.clients:
            raise Exception("Invalid client ID")
            
        context = self.clients[client_id]
        context.update_activity()
        context.command_count += 1
        
        # Validate resource limits
        if not await self._check_resource_limits(context, command):
            raise Exception("Resource limit exceeded")
            
        # Add isolation context to command
        isolated_command = self._isolate_command(context, command)
        
        # Execute command (mock implementation for now)
        result = await self._execute_in_context(context, isolated_command)
        
        # Track command in history
        context.command_history.append({
            "command": command,
            "result": result,
            "timestamp": datetime.now().isoformat()
        })
        
        # Keep only last 100 commands in history
        if len(context.command_history) > 100:
            context.command_history = context.command_history[-100:]
            
        return result
        
    def _isolate_command(self, context: ClientContext, command: Dict) -> Dict:
        """Add isolation parameters to command"""
        isolated = command.copy()
        
        # Add namespace to all commands
        if "params" not in isolated:
            isolated["params"] = {}
            
        isolated["params"]["__namespace__"] = context.scene_namespace
        isolated["params"]["__client_id__"] = context.client_id
        isolated["params"]["__asset_path__"] = context.asset_path
        
        # Modify paths to be relative to client's project
        if "path" in isolated["params"]:
            original_path = isolated["params"]["path"]
            if not original_path.startswith(context.asset_path):
                isolated["params"]["path"] = os.path.join(
                    context.asset_path, 
                    original_path.lstrip("/")
                )
                
        # Handle scene operations
        if isolated["action"] == "manage_scene":
            scene_name = isolated["params"].get("scene_name", "")
            if scene_name and not scene_name.startswith(context.scene_namespace):
                isolated["params"]["scene_name"] = f"{context.scene_namespace}_{scene_name}"
                
        # Handle GameObject operations
        if isolated["action"] == "manage_gameobject":
            # Prefix GameObject names with namespace
            if "name" in isolated["params"]:
                obj_name = isolated["params"]["name"]
                if not obj_name.startswith(context.scene_namespace):
                    isolated["params"]["name"] = f"{context.scene_namespace}_{obj_name}"
                    
        return isolated
        
    async def _check_resource_limits(self, context: ClientContext, command: Dict) -> bool:
        """Check if command would exceed resource limits"""
        # Check memory usage
        client_memory = await self.resource_monitor.get_client_memory(context.client_id)
        if client_memory > context.max_memory_mb:
            logger.warning(f"Client {context.client_id} exceeds memory limit")
            return False
            
        # Check asset count
        if command["action"] == "manage_asset" and command["params"].get("action") == "create":
            if len(context.loaded_assets) >= context.max_assets:
                logger.warning(f"Client {context.client_id} exceeds asset limit")
                return False
                
        return True
        
    async def _execute_in_context(self, context: ClientContext, command: Dict) -> Dict:
        """Execute command in Unity with client isolation"""
        # Mock implementation for now
        # In production, this would interface with Unity through the MCP bridge
        
        action = command.get("action", "unknown")
        params = command.get("params", {})
        
        # Simulate different command types
        if action == "manage_scene":
            return await self._mock_scene_command(context, params)
        elif action == "manage_gameobject":
            return await self._mock_gameobject_command(context, params)
        elif action == "manage_asset":
            return await self._mock_asset_command(context, params)
        else:
            return {
                "success": True,
                "message": f"Mock execution of {action} in namespace {context.scene_namespace}",
                "namespace": context.scene_namespace,
                "client_id": context.client_id
            }
            
    async def _mock_scene_command(self, context: ClientContext, params: Dict) -> Dict:
        """Mock scene management command"""
        sub_action = params.get("action", "list")
        scene_name = params.get("scene_name", "")
        
        if sub_action == "create":
            full_scene_name = f"{context.scene_namespace}_{scene_name}"
            context.active_scenes.add(full_scene_name)
            return {
                "success": True,
                "message": f"Created scene {full_scene_name}",
                "scene_name": full_scene_name
            }
        elif sub_action == "load":
            return {
                "success": True,
                "message": f"Loaded scene {scene_name} in namespace {context.scene_namespace}"
            }
        elif sub_action == "list":
            return {
                "success": True,
                "scenes": list(context.active_scenes),
                "namespace": context.scene_namespace
            }
        else:
            return {
                "success": True,
                "message": f"Scene {sub_action} completed for {scene_name}"
            }
            
    async def _mock_gameobject_command(self, context: ClientContext, params: Dict) -> Dict:
        """Mock GameObject management command"""
        sub_action = params.get("action", "list")
        obj_name = params.get("name", "GameObject")
        
        if sub_action == "create":
            full_name = f"{context.scene_namespace}_{obj_name}"
            return {
                "success": True,
                "message": f"Created GameObject {full_name}",
                "gameobject_name": full_name,
                "namespace": context.scene_namespace
            }
        else:
            return {
                "success": True,
                "message": f"GameObject {sub_action} completed for {obj_name} in namespace {context.scene_namespace}"
            }
            
    async def _mock_asset_command(self, context: ClientContext, params: Dict) -> Dict:
        """Mock asset management command"""
        sub_action = params.get("action", "list")
        asset_name = params.get("name", "Asset")
        
        if sub_action == "create":
            asset_path = os.path.join(context.asset_path, asset_name)
            context.loaded_assets.add(asset_path)
            return {
                "success": True,
                "message": f"Created asset {asset_name}",
                "asset_path": asset_path,
                "namespace": context.scene_namespace
            }
        else:
            return {
                "success": True,
                "message": f"Asset {sub_action} completed for {asset_name}"
            }
            
    async def cleanup_idle_clients(self) -> List[str]:
        """Clean up resources for idle clients"""
        idle_clients = []
        
        for client_id, context in list(self.clients.items()):
            if context.is_idle(30):  # 30 minutes idle
                idle_clients.append(client_id)
                
        for client_id in idle_clients:
            await self.unregister_client(client_id)
            
        logger.info(f"Cleaned up {len(idle_clients)} idle clients")
        return idle_clients
        
    async def unregister_client(self, client_id: str):
        """Unregister client and clean up resources"""
        if client_id not in self.clients:
            logger.warning(f"Attempted to unregister non-existent client {client_id}")
            return
            
        context = self.clients[client_id]
        
        # Clean up scenes
        for scene in context.active_scenes:
            if scene in self.scene_locks:
                del self.scene_locks[scene]
                
        # Clean up project directory (optional - comment out for data persistence)
        # project_path = os.path.dirname(context.asset_path)
        # if os.path.exists(project_path):
        #     shutil.rmtree(project_path)
        
        # Remove client
        del self.clients[client_id]
        logger.info(f"Unregistered client {client_id}")
        
    def get_client_info(self, client_id: str) -> Optional[Dict]:
        """Get client information"""
        if client_id not in self.clients:
            return None
            
        context = self.clients[client_id]
        return {
            "client_id": context.client_id,
            "project_name": context.project_name,
            "scene_namespace": context.scene_namespace,
            "asset_path": context.asset_path,
            "created_at": context.created_at.isoformat(),
            "last_activity": context.last_activity.isoformat(),
            "command_count": context.command_count,
            "active_scenes": list(context.active_scenes),
            "loaded_assets": list(context.loaded_assets),
            "idle_minutes": (datetime.now() - context.last_activity).total_seconds() / 60
        }
        
    def list_all_clients(self) -> List[Dict]:
        """List all registered clients"""
        result = []
        for client_id in self.clients.keys():
            info = self.get_client_info(client_id)
            if info is not None:
                result.append(info)
        return result