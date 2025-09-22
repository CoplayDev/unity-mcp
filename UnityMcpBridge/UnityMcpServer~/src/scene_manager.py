"""
Scene Management System for Unity MCP VPS Deployment
Handles multi-client scene operations with isolation
"""

import asyncio
import logging
from typing import Dict, List, Optional, Set
from dataclasses import dataclass
import json
import os
from datetime import datetime

logger = logging.getLogger(__name__)

@dataclass
class SceneInfo:
    name: str
    namespace: str
    client_id: str
    path: str
    is_loaded: bool = False
    objects_count: int = 0
    last_modified: Optional[str] = None
    created_at: str = ""
    
    def __post_init__(self):
        if not self.created_at:
            self.created_at = datetime.now().isoformat()

class SceneManager:
    """Manages scenes across multiple clients with isolation"""
    
    def __init__(self):
        self.scenes: Dict[str, SceneInfo] = {}
        self.loaded_scenes: Set[str] = set()
        self.scene_queue = asyncio.Queue()
        self.max_loaded_scenes = 3  # Limit concurrent loaded scenes
        self.current_namespace = ""
        
    async def create_scene(self, client_id: str, scene_name: str, namespace: str, asset_path: str) -> SceneInfo:
        """Create new scene for client"""
        full_name = f"{namespace}_{scene_name}"
        
        if full_name in self.scenes:
            raise Exception(f"Scene {full_name} already exists")
            
        scene_path = os.path.join(asset_path, "Scenes", f"{full_name}.unity")
        
        scene_info = SceneInfo(
            name=scene_name,
            namespace=namespace,
            client_id=client_id,
            path=scene_path
        )
        
        # Create scene file
        await self._create_scene_file(scene_info)
        
        self.scenes[full_name] = scene_info
        logger.info(f"Created scene {full_name} for client {client_id}")
        
        return scene_info
        
    async def _create_scene_file(self, scene_info: SceneInfo):
        """Create Unity scene file with basic content"""
        # Ensure directory exists
        os.makedirs(os.path.dirname(scene_info.path), exist_ok=True)
        
        # Basic Unity scene content
        scene_content = f"""% YAML 1.1
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
  m_OcclusionCullingData: {{fileID: 0}}
--- !u!104 &2
RenderSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 9
  m_Fog: 0
  m_FogColor: {{r: 0.5, g: 0.5, b: 0.5, a: 1}}
  m_FogMode: 3
  m_FogDensity: 0.01
  m_LinearFogStart: 0
  m_LinearFogEnd: 300
  m_AmbientSkyColor: {{r: 0.212, g: 0.227, b: 0.259, a: 1}}
  m_AmbientEquatorColor: {{r: 0.114, g: 0.125, b: 0.133, a: 1}}
  m_AmbientGroundColor: {{r: 0.047, g: 0.043, b: 0.035, a: 1}}
  m_AmbientIntensity: 1
  m_AmbientMode: 3
  m_SubtractiveShadowColor: {{r: 0.42, g: 0.478, b: 0.627, a: 1}}
  m_SkyboxMaterial: {{fileID: 0}}
  m_HaloStrength: 0.5
  m_FlareStrength: 1
  m_FlareFadeSpeed: 3
  m_HaloTexture: {{fileID: 0}}
  m_SpotCookie: {{fileID: 10001, guid: 0000000000000000e000000000000000, type: 0}}
  m_DefaultReflectionMode: 0
  m_DefaultReflectionResolution: 128
  m_ReflectionBounces: 1
  m_ReflectionIntensity: 1
  m_CustomReflection: {{fileID: 0}}
  m_Sun: {{fileID: 0}}
  m_IndirectSpecularColor: {{r: 0, g: 0, b: 0, a: 1}}
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
    m_LightmapParameters: {{fileID: 0}}
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
  m_LightingDataAsset: {{fileID: 0}}
  m_LightingSettings: {{fileID: 0}}
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
  m_NavMeshData: {{fileID: 0}}
--- !u!1 &963194225
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInternal: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: 963194228}}
  - component: {{fileID: 963194227}}
  - component: {{fileID: 963194226}}
  m_Layer: 0
  m_Name: {scene_info.namespace}_Main_Camera
  m_TagString: MainCamera
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!81 &963194226
AudioListener:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInternal: {{fileID: 0}}
  m_GameObject: {{fileID: 963194225}}
  m_Enabled: 1
--- !u!20 &963194227
Camera:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInternal: {{fileID: 0}}
  m_GameObject: {{fileID: 963194225}}
  m_Enabled: 1
  serializedVersion: 2
  m_ClearFlags: 1
  m_BackGroundColor: {{r: 0.19215687, g: 0.3019608, b: 0.4745098, a: 0}}
  m_projectionMatrixMode: 1
  m_GateFitMode: 2
  m_FOVAxisMode: 0
  m_SensorSize: {{x: 36, y: 24}}
  m_LensShift: {{x: 0, y: 0}}
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
  m_targetTexture: {{fileID: 0}}
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
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInternal: {{fileID: 0}}
  m_GameObject: {{fileID: 963194225}}
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 1, z: -10}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_Children: []
  m_Father: {{fileID: 0}}
  m_RootOrder: 0
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
--- !u!1 &705507993
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInternal: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: 705507995}}
  - component: {{fileID: 705507994}}
  m_Layer: 0
  m_Name: {scene_info.namespace}_Directional_Light
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!108 &705507994
Light:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInternal: {{fileID: 0}}
  m_GameObject: {{fileID: 705507993}}
  m_Enabled: 1
  serializedVersion: 10
  m_Type: 1
  m_Shape: 0
  m_Color: {{r: 1, g: 0.95686275, b: 0.8392157, a: 1}}
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
  m_Cookie: {{fileID: 0}}
  m_DrawHalo: 0
  m_Flare: {{fileID: 0}}
  m_RenderMode: 0
  m_CullingMask:
    serializedVersion: 2
    m_Bits: 4294967295
  m_RenderingLayerMask: 1
  m_Lightmapping: 1
  m_LightShadowCasterMode: 0
  m_AreaSize: {{x: 1, y: 1}}
  m_BounceIntensity: 1
  m_ColorTemperature: 6570
  m_UseColorTemperature: 0
  m_BoundingSphereOverride: {{x: 0, y: 0, z: 0, w: 0}}
  m_UseBoundingSphereOverride: 0
  m_UseViewFrustumForShadowCasterCull: 1
  m_ShadowRadius: 0
  m_ShadowAngle: 0
--- !u!4 &705507995
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInternal: {{fileID: 0}}
  m_GameObject: {{fileID: 705507993}}
  m_LocalRotation: {{x: 0.40821788, y: -0.23456968, z: 0.10938163, w: 0.8754261}}
  m_LocalPosition: {{x: 0, y: 3, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_Children: []
  m_Father: {{fileID: 0}}
  m_RootOrder: 1
  m_LocalEulerAnglesHint: {{x: 50, y: -30, z: 0}}
"""
        
        # Write scene file
        with open(scene_info.path, 'w') as f:
            f.write(scene_content)
            
        scene_info.last_modified = datetime.now().isoformat()
        scene_info.objects_count = 2  # Camera and Light
        
    async def load_scene(self, client_id: str, scene_name: str, namespace: str) -> bool:
        """Load scene for specific client"""
        full_name = f"{namespace}_{scene_name}"
        
        if full_name not in self.scenes:
            raise Exception(f"Scene {full_name} not found")
            
        scene_info = self.scenes[full_name]
        
        # Verify ownership
        if scene_info.client_id != client_id:
            raise Exception(f"Scene {full_name} belongs to different client")
            
        # Check if we need to unload other scenes
        if len(self.loaded_scenes) >= self.max_loaded_scenes:
            await self._unload_oldest_scene()
            
        # Load scene (mock implementation)
        await self._unity_load_scene(scene_info)
        
        scene_info.is_loaded = True
        self.loaded_scenes.add(full_name)
        
        logger.info(f"Loaded scene {full_name} for client {client_id}")
        return True
        
    async def switch_client_context(self, client_id: str, namespace: str):
        """Switch Unity context to specific client's namespace"""
        # Unload all scenes from other namespaces
        other_scenes = [
            scene for name, scene in self.scenes.items()
            if scene.namespace != namespace and scene.is_loaded
        ]
        
        for scene in other_scenes:
            await self._unity_unload_scene(scene)
            scene.is_loaded = False
            scene_name = f"{scene.namespace}_{scene.name}"
            self.loaded_scenes.discard(scene_name)
            
        # Load client's main scene if it exists
        main_scene = f"{namespace}_Main"
        if main_scene in self.scenes and not self.scenes[main_scene].is_loaded:
            await self.load_scene(client_id, "Main", namespace)
            
        self.current_namespace = namespace
        logger.info(f"Switched context to client {client_id} namespace {namespace}")
        
    async def list_client_scenes(self, client_id: str, namespace: str) -> List[Dict]:
        """List all scenes for a specific client"""
        client_scenes = []
        
        for name, scene in self.scenes.items():
            if scene.client_id == client_id and scene.namespace == namespace:
                client_scenes.append({
                    "name": scene.name,
                    "full_name": name,
                    "path": scene.path,
                    "is_loaded": scene.is_loaded,
                    "objects_count": scene.objects_count,
                    "created_at": scene.created_at,
                    "last_modified": scene.last_modified
                })
                
        return client_scenes
        
    async def get_scene_info(self, full_scene_name: str) -> Optional[Dict]:
        """Get information about a specific scene"""
        if full_scene_name not in self.scenes:
            return None
            
        scene = self.scenes[full_scene_name]
        return {
            "name": scene.name,
            "namespace": scene.namespace,
            "client_id": scene.client_id,
            "path": scene.path,
            "is_loaded": scene.is_loaded,
            "objects_count": scene.objects_count,
            "created_at": scene.created_at,
            "last_modified": scene.last_modified
        }
        
    async def _unity_load_scene(self, scene_info: SceneInfo):
        """Load scene in Unity (mock implementation)"""
        # In production, this would send commands to Unity
        logger.debug(f"Mock: Loading scene {scene_info.namespace}_{scene_info.name}")
        await asyncio.sleep(0.1)  # Simulate load time
        
    async def _unity_unload_scene(self, scene_info: SceneInfo):
        """Unload scene in Unity (mock implementation)"""
        # In production, this would send commands to Unity
        logger.debug(f"Mock: Unloading scene {scene_info.namespace}_{scene_info.name}")
        await asyncio.sleep(0.05)  # Simulate unload time
        
    async def _unload_oldest_scene(self):
        """Unload least recently used scene"""
        if not self.loaded_scenes:
            return
            
        # Simple implementation - unload first loaded scene
        oldest = next(iter(self.loaded_scenes))
        if oldest in self.scenes:
            scene_info = self.scenes[oldest]
            await self._unity_unload_scene(scene_info)
            scene_info.is_loaded = False
            self.loaded_scenes.remove(oldest)
            logger.info(f"Unloaded oldest scene: {oldest}")
            
    async def cleanup_client_scenes(self, client_id: str, namespace: str):
        """Clean up all scenes for a client"""
        client_scenes = [
            (name, scene) for name, scene in self.scenes.items()
            if scene.client_id == client_id
        ]
        
        for name, scene in client_scenes:
            if scene.is_loaded:
                await self._unity_unload_scene(scene)
                self.loaded_scenes.discard(name)
                
            # Delete scene file
            try:
                if os.path.exists(scene.path):
                    os.remove(scene.path)
            except OSError as e:
                logger.warning(f"Could not delete scene file {scene.path}: {e}")
                
            del self.scenes[name]
            
        logger.info(f"Cleaned up {len(client_scenes)} scenes for client {client_id}")
        
    def get_stats(self) -> Dict:
        """Get scene manager statistics"""
        total_scenes = len(self.scenes)
        loaded_scenes = len(self.loaded_scenes)
        
        # Count scenes per namespace
        namespace_counts = {}
        for scene in self.scenes.values():
            namespace_counts[scene.namespace] = namespace_counts.get(scene.namespace, 0) + 1
            
        return {
            "total_scenes": total_scenes,
            "loaded_scenes": loaded_scenes,
            "max_loaded_scenes": self.max_loaded_scenes,
            "current_namespace": self.current_namespace,
            "namespace_counts": namespace_counts,
            "namespaces": list(namespace_counts.keys())
        }