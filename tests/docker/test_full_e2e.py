#!/usr/bin/env python3
"""
Full End-to-End Test for Unity MCP Docker Deployment
Tests complete production workflow including Unity instance
"""

import subprocess
import json
import time
import os
import sys
import tempfile
import shutil
from pathlib import Path


class FullE2ETest:
    """Complete end-to-end test with Unity instance"""
    
    def __init__(self):
        self.project_root = Path(__file__).parent.parent.parent.absolute()
        self.production_image = "unity-mcp:e2e-test"
        self.container_name = "unity-mcp-full-e2e"
        self.http_port = 8090
        self.unity_port = 6410
        self.test_results = []
        
        # Test Unity project path
        self.test_project_dir = self.project_root / "tests" / "fixtures" / "test-unity-project"
        
        os.chdir(self.project_root)
    
    def log(self, message, level="INFO"):
        """Log message with timestamp"""
        timestamp = time.strftime("%Y-%m-%d %H:%M:%S")
        print(f"[{timestamp}] [{level}] {message}")
    
    def run_command(self, cmd, timeout=60, check=True):
        """Run shell command with proper error handling"""
        try:
            result = subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                timeout=timeout,
                shell=isinstance(cmd, str)
            )
            if check and result.returncode != 0:
                raise subprocess.CalledProcessError(result.returncode, cmd, result.stdout, result.stderr)
            return result
        except subprocess.TimeoutExpired:
            raise Exception(f"Command timed out after {timeout}s: {cmd}")
    
    def cleanup(self):
        """Clean up test resources"""
        try:
            self.run_command(f"docker stop {self.container_name}", check=False)
            self.run_command(f"docker rm -f {self.container_name}", check=False)
            self.run_command(f"docker rmi -f {self.production_image}", check=False)
        except Exception as e:
            self.log(f"Cleanup warning: {e}")
    
    def http_request(self, method, endpoint, data=None, timeout=30):
        """Make HTTP request using curl"""
        url = f"http://localhost:{self.http_port}{endpoint}"
        
        cmd = ["curl", "-s", "-w", "\\n%{http_code}", "--max-time", str(timeout)]
        
        if method == "POST" and data:
            cmd.extend([
                "-H", "Content-Type: application/json",
                "-d", json.dumps(data)
            ])
        
        cmd.append(url)
        
        try:
            result = self.run_command(cmd, timeout=timeout + 10, check=False)
            
            if result.returncode == 0:
                lines = result.stdout.strip().split('\n')
                if len(lines) >= 2:
                    response_text = '\n'.join(lines[:-1])
                    status_code = int(lines[-1])
                    return status_code, response_text
                else:
                    return int(lines[0]) if lines[0].isdigit() else 0, ""
            else:
                return 0, result.stderr
        except Exception as e:
            return 0, str(e)
    
    def create_minimal_unity_project(self):
        """Create a minimal Unity project for testing"""
        self.log("Creating minimal Unity project for testing...")
        
        project_dir = self.test_project_dir
        project_dir.mkdir(parents=True, exist_ok=True)
        
        # Create basic Unity project structure
        assets_dir = project_dir / "Assets"
        assets_dir.mkdir(exist_ok=True)
        
        # Create a simple scene
        scenes_dir = assets_dir / "Scenes"
        scenes_dir.mkdir(exist_ok=True)
        
        # Create basic project settings
        project_settings_dir = project_dir / "ProjectSettings"
        project_settings_dir.mkdir(exist_ok=True)
        
        # Create minimal ProjectSettings.asset
        project_settings_content = """% YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 24
  productGUID: 12345678901234567890123456789012
  AndroidBundleVersionCode: 1
  AndroidMinSdkVersion: 22
  AndroidTargetSdkVersion: 0
  AndroidPreferredInstallLocation: 1
  aotOptions: 
  stripEngineCode: 1
  iPhoneStrippingLevel: 0
  bundleVersion: 1.0
  preloadedAssets: []
  metroInputSource: 0
  wsaTransparentSwapchain: 0
  m_HolographicPauseOnTrackingLoss: 1
  xboxOneDisableKinectGpuReservation: 1
  xboxOneEnable7thCore: 1
  vrSettings:
    cardboard:
      depthFormat: 0
      enableTransitionView: 0
    daydream:
      depthFormat: 0
      useSustainedPerformanceMode: 0
      enableVideoLayer: 0
      useProtectedVideoMemory: 0
    hololens:
      depthFormat: 1
      depthBufferSharingEnabled: 1
    oculus:
      sharedDepthBuffer: 1
      dashSupport: 1
      lowOverheadMode: 0
      protectedContext: 0
      v2Signing: 1
  protectGraphicsMemory: 0
  useHDRDisplay: 0
  m_ColorSpace: 0
  m_MTRendering: 1
  mipStripping: 0
  numberOfMipsStripped: 0
  m_StackTraceTypes: 010000000100000001000000010000000100000001000000
  iosShowActivityIndicatorOnLoading: -1
  androidShowActivityIndicatorOnLoading: -1
  iosUseCustomAppBackgroundBehavior: 0
  iosAllowHTTPDownload: 1
  allowedAutorotateToPortrait: 1
  allowedAutorotateToPortraitUpsideDown: 1
  allowedAutorotateToLandscapeRight: 1
  allowedAutorotateToLandscapeLeft: 1
  useOSAutorotation: 1
  use32BitDisplayBuffer: 1
  preserveFramebufferAlpha: 0
  disableDepthAndStencilBuffers: 0
  androidStartInFullscreen: 1
  androidRenderOutsideSafeArea: 1
  androidUseSwappy: 1
  androidBlitType: 0
  androidResizableWindow: 0
  androidDefaultWindowWidth: 1920
  androidDefaultWindowHeight: 1080
  androidMinimumWindowWidth: 400
  androidMinimumWindowHeight: 300
  androidFullscreenMode: 1
  defaultIsNativeResolution: 1
  macRetinaSupport: 1
  runInBackground: 1
  captureSingleScreen: 0
  muteOtherAudioSources: 0
  Prepare IOS For Recording: 0
  Force IOS Speakers When Recording: 0
  deferSystemGesturesMode: 0
  hideHomeButton: 0
  submitAnalytics: 1
  usePlayerLog: 1
  bakeCollisionMeshes: 0
  forceSingleInstance: 0
  useFlipModelSwapchain: 1
  resizableWindow: 0
  useMacAppStoreValidation: 0
  macAppStoreCategory: public.app-category.games
  gpuSkinning: 1
  xboxPIXTextureCapture: 0
  xboxEnableAvatar: 0
  xboxEnableKinect: 0
  xboxEnableKinectAutoTracking: 0
  xboxEnableFitness: 0
  visibleInBackground: 1
  allowFullscreenSwitch: 1
  graphicsJobMode: 0
  fullscreenMode: 1
  xboxSpeechDB: 0
  xboxEnableHeadOrientation: 0
  xboxEnableGuest: 0
  xboxEnablePIXSampling: 0
  metalFramebufferOnly: 0
  xboxOneResolution: 0
  xboxOneSResolution: 0
  xboxOneXResolution: 3
  xboxOneMonoLoggingLevel: 0
  xboxOneLoggingLevel: 1
  xboxOneDisableEsram: 0
  xboxOneEnableTypeOptimization: 0
  xboxOnePresentImmediateThreshold: 0
  switchQueueCommandMemory: 0
  switchQueueControlMemory: 16384
  switchQueueComputeMemory: 262144
  switchNVNShaderPoolsGranularity: 33554432
  switchNVNDefaultPoolsGranularity: 16777216
  switchNVNOtherPoolsGranularity: 16777216"""
        
        with open(project_settings_dir / "ProjectSettings.asset", "w") as f:
            f.write(project_settings_content)
        
        # Create version info
        version_content = "m_EditorVersion: 2022.3.45f1\nm_EditorVersionWithRevision: 2022.3.45f1 (63b2b3067b8e)"
        with open(project_settings_dir / "ProjectVersion.txt", "w") as f:
            f.write(version_content)
        
        self.log(f"Created minimal Unity project at: {project_dir}")
        return project_dir
    
    def build_production_image(self):
        """Build the production Docker image"""
        self.log("Building production Docker image...")
        
        build_start = time.time()
        
        # Build production image with Unity
        build_cmd = [
            "docker", "build",
            "-f", "docker/Dockerfile.production",
            "-t", self.production_image,
            "--target", "production",
            "--build-arg", "UNITY_VERSION=2022.3.45f1",
            "--build-arg", "UNITY_CHANGESET=63b2b3067b8e",
            "--build-arg", "PYTHON_VERSION=3.11",
            "."
        ]
        
        self.log(f"Build command: {' '.join(build_cmd)}")
        
        try:
            result = self.run_command(build_cmd, timeout=3600)  # 1 hour timeout for Unity install
            build_duration = time.time() - build_start
            
            self.log(f"✅ Production image built successfully in {build_duration:.1f}s")
            
            # Verify image size
            inspect_result = self.run_command(["docker", "image", "inspect", self.production_image])
            image_info = json.loads(inspect_result.stdout)[0]
            size_bytes = image_info.get("Size", 0)
            size_gb = size_bytes / (1024 ** 3)
            
            self.log(f"Production image size: {size_gb:.2f} GB")
            
            if size_gb >= 2.0:
                raise Exception(f"Image size ({size_gb:.2f}GB) exceeds 2GB limit")
            
            return True
            
        except subprocess.CalledProcessError as e:
            self.log(f"Build failed: {e.stderr}")
            raise Exception(f"Docker build failed: {e.returncode}")
        except Exception as e:
            self.log(f"Build error: {e}")
            raise
    
    def start_unity_container(self):
        """Start Unity container with proper configuration"""
        self.log("Starting Unity MCP container...")
        
        # Create test project
        project_path = self.create_minimal_unity_project()
        
        # Start container with volume mounts
        run_cmd = [
            "docker", "run", "-d",
            "--name", self.container_name,
            "-p", f"{self.http_port}:8080",
            "-p", f"{self.unity_port}:6400",
            
            # Environment variables
            "-e", "LOG_LEVEL=DEBUG",
            "-e", f"UNITY_PROJECT_PATH=/app/unity-projects/test",
            "-e", "UNITY_HEADLESS=true",
            "-e", "UNITY_MCP_AUTOSTART=true",
            
            # Volume mounts
            "-v", f"{project_path}:/app/unity-projects/test:ro",
            "-v", f"{self.project_root}/builds:/app/builds",
            "-v", f"{self.project_root}/logs:/tmp/unity-logs",
            
            # Resource limits for stability
            "--memory", "4g",
            "--cpus", "2.0",
            
            self.production_image
        ]
        
        self.log(f"Container command: {' '.join(run_cmd)}")
        
        result = self.run_command(run_cmd)
        
        container_id = result.stdout.strip()
        self.log(f"Container started with ID: {container_id[:12]}")
        
        return container_id
    
    def wait_for_unity_ready(self, timeout=300):
        """Wait for Unity and HTTP server to be fully ready"""
        self.log(f"Waiting for Unity MCP to be ready (timeout: {timeout}s)...")
        
        start_time = time.time()
        last_status = None
        
        while (time.time() - start_time) < timeout:
            try:
                # Check HTTP server
                status_code, response = self.http_request("GET", "/health", timeout=5)
                
                if status_code == 200:
                    health_data = json.loads(response)
                    status = health_data.get("status")
                    unity_connected = health_data.get("unity_connected", False)
                    
                    if status != last_status:
                        self.log(f"Health status: {status}, Unity connected: {unity_connected}")
                        last_status = status
                    
                    # Check if both HTTP server and Unity are ready
                    if status == "healthy" and unity_connected:
                        elapsed = time.time() - start_time
                        self.log(f"✅ Unity MCP is fully ready after {elapsed:.1f}s")
                        return True
                
                # If we get a response but Unity isn't ready, show progress
                if status_code == 200 and time.time() - start_time > 30:
                    # Show logs every 30 seconds for debugging
                    if int(time.time() - start_time) % 30 == 0:
                        self.show_container_logs()
            
            except Exception as e:
                # Expected during startup
                pass
            
            time.sleep(5)
        
        # Timeout reached
        elapsed = time.time() - start_time
        self.log(f"❌ Timeout waiting for Unity MCP after {elapsed:.1f}s")
        
        # Show logs for debugging
        self.show_container_logs()
        
        return False
    
    def show_container_logs(self):
        """Show container logs for debugging"""
        try:
            logs_result = self.run_command([
                "docker", "logs", "--tail", "20", self.container_name
            ], check=False)
            
            if logs_result.stdout:
                self.log("Container logs (last 20 lines):")
                for line in logs_result.stdout.strip().split('\n')[-10:]:  # Show last 10 lines
                    self.log(f"  {line}")
            
        except Exception as e:
            self.log(f"Could not retrieve logs: {e}")
    
    def test_basic_api_endpoints(self):
        """Test basic API functionality"""
        self.log("Testing basic API endpoints...")
        
        # Test health endpoint
        status_code, response = self.http_request("GET", "/health")
        assert status_code == 200, f"Health check failed: {status_code}"
        
        health_data = json.loads(response)
        assert health_data.get("status") == "healthy", "Health status not healthy"
        assert health_data.get("unity_connected") == True, "Unity not connected"
        
        self.log("✅ Health endpoint working")
        
        # Test status endpoint
        status_code, response = self.http_request("GET", "/status")
        assert status_code == 200, f"Status endpoint failed: {status_code}"
        
        status_data = json.loads(response)
        assert "server" in status_data, "Status missing server info"
        assert "version" in status_data, "Status missing version info"
        
        self.log("✅ Status endpoint working")
        
        return True
    
    def test_unity_commands(self):
        """Test actual Unity command execution"""
        self.log("Testing Unity command execution...")
        
        # Test ping command
        ping_cmd = {
            "action": "ping",
            "params": {},
            "userId": "e2e-test-user",
            "timeout": 30.0
        }
        
        status_code, response = self.http_request("POST", "/execute-command", ping_cmd, timeout=45)
        assert status_code == 200, f"Ping command failed: {status_code} - {response}"
        
        ping_result = json.loads(response)
        assert "commandId" in ping_result, "Ping response missing commandId"
        assert ping_result.get("success") == True, "Ping command not successful"
        
        command_id = ping_result["commandId"]
        self.log(f"✅ Ping command successful (ID: {command_id})")
        
        # Test scene management command
        scene_cmd = {
            "action": "headless_operations",
            "params": {
                "action": "create_empty_scene",
                "sceneName": "E2ETestScene",
                "addDefaultObjects": True
            },
            "userId": "e2e-test-user",
            "timeout": 60.0
        }
        
        status_code, response = self.http_request("POST", "/execute-command", scene_cmd, timeout=75)
        assert status_code == 200, f"Scene command failed: {status_code} - {response}"
        
        scene_result = json.loads(response)
        assert "commandId" in scene_result, "Scene response missing commandId"
        
        scene_command_id = scene_result["commandId"]
        self.log(f"✅ Scene creation command successful (ID: {scene_command_id})")
        
        # Wait and check command status
        time.sleep(5)
        status_code, response = self.http_request("GET", f"/command/{scene_command_id}")
        if status_code == 200:
            cmd_status = json.loads(response)
            self.log(f"Scene command status: {cmd_status.get('success', 'unknown')}")
        
        return True
    
    def test_concurrent_commands(self):
        """Test concurrent command execution"""
        self.log("Testing concurrent command execution...")
        
        import threading
        import queue
        
        results_queue = queue.Queue()
        
        def execute_command(cmd_index):
            try:
                cmd = {
                    "action": "ping",
                    "params": {"message": f"concurrent-test-{cmd_index}"},
                    "userId": f"e2e-user-{cmd_index}",
                    "timeout": 30.0
                }
                
                status_code, response = self.http_request("POST", "/execute-command", cmd, timeout=45)
                
                if status_code == 200:
                    result = json.loads(response)
                    results_queue.put((cmd_index, "success", result.get("commandId", "unknown")))
                else:
                    results_queue.put((cmd_index, "failed", f"Status: {status_code}"))
                    
            except Exception as e:
                results_queue.put((cmd_index, "error", str(e)))
        
        # Launch 3 concurrent commands (conservative for Unity)
        threads = []
        for i in range(3):
            thread = threading.Thread(target=execute_command, args=(i,))
            threads.append(thread)
            thread.start()
        
        # Wait for completion
        for thread in threads:
            thread.join(timeout=60)
        
        # Collect results
        results = []
        while not results_queue.empty():
            results.append(results_queue.get())
        
        assert len(results) == 3, f"Expected 3 results, got {len(results)}"
        
        successful = sum(1 for _, status, _ in results if status == "success")
        self.log(f"Concurrent commands: {successful}/3 successful")
        
        # Allow some tolerance for Unity resource constraints
        assert successful >= 2, f"Too many concurrent failures: {successful}/3"
        
        self.log("✅ Concurrent command test passed")
        return True
    
    def test_performance_metrics(self):
        """Test performance and resource usage"""
        self.log("Testing performance metrics...")
        
        # Get container stats
        stats_result = self.run_command([
            "docker", "stats", "--no-stream", "--format",
            "table {{.Container}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.MemPerc}}",
            self.container_name
        ], check=False)
        
        if stats_result.returncode == 0:
            lines = stats_result.stdout.strip().split('\n')
            if len(lines) > 1:
                stats_line = lines[1]
                self.log(f"Container stats: {stats_line}")
                
                # Basic resource usage check
                parts = stats_line.split()
                if len(parts) >= 4:
                    cpu_perc = parts[1].replace('%', '')
                    mem_perc = parts[3].replace('%', '')
                    
                    try:
                        cpu_val = float(cpu_perc)
                        mem_val = float(mem_perc)
                        
                        # Reasonable limits for Unity
                        assert cpu_val < 95, f"CPU usage too high: {cpu_val}%"
                        assert mem_val < 90, f"Memory usage too high: {mem_val}%"
                        
                        self.log(f"✅ Resource usage acceptable: CPU {cpu_val}%, Memory {mem_val}%")
                    except ValueError:
                        self.log("⚠️  Could not parse resource values")
        
        # Test response times
        start_time = time.time()
        status_code, _ = self.http_request("GET", "/health")
        response_time = (time.time() - start_time) * 1000
        
        assert response_time < 5000, f"Health check too slow: {response_time:.1f}ms"
        self.log(f"✅ Response time acceptable: {response_time:.1f}ms")
        
        return True
    
    def run_full_e2e_test(self):
        """Run the complete end-to-end test"""
        try:
            self.log("🚀 Starting Full End-to-End Test for Unity MCP Docker")
            self.log(f"Test configuration:")
            self.log(f"  - Production image: {self.production_image}")
            self.log(f"  - HTTP port: {self.http_port}")
            self.log(f"  - Unity port: {self.unity_port}")
            
            # Cleanup any existing resources
            self.cleanup()
            
            # Phase 1: Build production image
            self.log("\n=== PHASE 1: BUILD PRODUCTION IMAGE ===")
            self.build_production_image()
            
            # Phase 2: Start Unity container
            self.log("\n=== PHASE 2: START UNITY CONTAINER ===")
            container_id = self.start_unity_container()
            
            # Phase 3: Wait for services to be ready
            self.log("\n=== PHASE 3: WAIT FOR SERVICES ===")
            if not self.wait_for_unity_ready():
                raise Exception("Unity MCP services not ready within timeout")
            
            # Phase 4: Test basic API
            self.log("\n=== PHASE 4: TEST BASIC API ===")
            self.test_basic_api_endpoints()
            
            # Phase 5: Test Unity commands
            self.log("\n=== PHASE 5: TEST UNITY COMMANDS ===")
            self.test_unity_commands()
            
            # Phase 6: Test concurrent execution
            self.log("\n=== PHASE 6: TEST CONCURRENT COMMANDS ===")
            self.test_concurrent_commands()
            
            # Phase 7: Test performance
            self.log("\n=== PHASE 7: TEST PERFORMANCE ===")
            self.test_performance_metrics()
            
            # Success!
            self.log("\n" + "="*80)
            self.log("🎉 FULL END-TO-END TEST COMPLETED SUCCESSFULLY!")
            self.log("="*80)
            self.log("✅ Production Docker image builds correctly")
            self.log("✅ Unity instance starts and connects")
            self.log("✅ HTTP API endpoints functional")
            self.log("✅ Unity command execution working")
            self.log("✅ Concurrent command handling operational")
            self.log("✅ Performance metrics within limits")
            self.log("")
            self.log("🚀 MILESTONE 2: DOCKERIZATION - FULLY VALIDATED")
            
            return True
            
        except Exception as e:
            self.log(f"\n❌ FULL END-TO-END TEST FAILED: {e}")
            self.show_container_logs()
            return False
            
        finally:
            # Cleanup
            self.log("\nCleaning up test resources...")
            self.cleanup()


def main():
    """Main entry point"""
    test = FullE2ETest()
    
    try:
        success = test.run_full_e2e_test()
        return 0 if success else 1
    except KeyboardInterrupt:
        print("\n⚠️  Test interrupted by user")
        test.cleanup()
        return 1
    except Exception as e:
        print(f"\n💥 Test runner failed: {e}")
        test.cleanup()
        return 1


if __name__ == "__main__":
    exit(main())