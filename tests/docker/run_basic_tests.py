#!/usr/bin/env python3
"""
Basic Docker Test Runner for Unity MCP
Runs core Docker functionality tests that don't require full Unity installation
"""

import subprocess
import json
import time
import threading
import queue
import sys
import os
from pathlib import Path


class BasicDockerTestRunner:
    """Basic test runner for Docker functionality without Unity"""
    
    def __init__(self):
        self.project_root = Path(__file__).parent.parent.parent.absolute()
        self.test_image_name = "unity-mcp:test"
        self.test_container_name = "unity-mcp-basic-test"
        self.http_port = 8083
        self.unity_port = 6403
        self.results = []
        
        # Ensure we're in the right directory
        os.chdir(self.project_root)
    
    def log(self, message, level="INFO"):
        """Log message with timestamp"""
        timestamp = time.strftime("%Y-%m-%d %H:%M:%S")
        print(f"[{timestamp}] [{level}] {message}")
    
    def run_command(self, cmd, timeout=30, check=True):
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
    
    def cleanup_containers(self):
        """Clean up test containers"""
        try:
            self.run_command(f"docker stop {self.test_container_name}", check=False)
            self.run_command(f"docker rm -f {self.test_container_name}", check=False)
        except Exception:
            pass
    
    def http_request(self, method, endpoint, data=None, timeout=10):
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
            result = self.run_command(cmd, timeout=timeout + 5, check=False)
            
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
    
    # ===============================
    # BUILD TESTS
    # ===============================
    
    def test_docker_build_succeeds(self):
        """Test that Docker test image exists and was built"""
        self.log("Testing Docker build...")
        
        # Check if test image exists
        result = self.run_command(["docker", "image", "inspect", self.test_image_name])
        
        image_info = json.loads(result.stdout)[0]
        
        # Basic validation
        config = image_info.get("Config", {})
        if not config:
            raise Exception("Image config not found")
        
        # Check user
        user = config.get("User", "")
        if "unity" not in user:
            raise Exception("Image should run as unity user")
        
        self.log("✅ Docker build test passed")
        return True
    
    def test_image_size_reasonable(self):
        """Test that image size is reasonable (should be much smaller than 2GB without Unity)"""
        self.log("Testing image size...")
        
        result = self.run_command(["docker", "image", "inspect", self.test_image_name])
        image_info = json.loads(result.stdout)[0]
        
        size_bytes = image_info.get("Size", 0)
        size_gb = size_bytes / (1024 ** 3)
        size_mb = size_bytes / (1024 ** 2)
        
        self.log(f"Image size: {size_mb:.1f} MB ({size_gb:.3f} GB)")
        
        # Test image should be much smaller than 2GB since it doesn't have Unity
        if size_gb > 0.5:  # 500MB limit for test image
            raise Exception(f"Test image size ({size_gb:.3f}GB) is too large for non-Unity image")
        
        self.log("✅ Image size test passed")
        return True
    
    def test_security_configuration(self):
        """Test image security configuration"""
        self.log("Testing security configuration...")
        
        result = self.run_command(["docker", "image", "inspect", self.test_image_name])
        image_info = json.loads(result.stdout)[0]
        config = image_info.get("Config", {})
        
        # Check non-root user
        user = config.get("User", "")
        if user == "root" or user == "":
            raise Exception("Image should run as non-root user")
        
        if "unity" not in user:
            raise Exception("Should run as unity user")
        
        # Check health check
        health_check = config.get("Healthcheck")
        if not health_check or "Test" not in health_check:
            raise Exception("Health check not properly configured")
        
        self.log("✅ Security configuration test passed")
        return True
    
    # ===============================
    # STARTUP TESTS
    # ===============================
    
    def test_container_startup_time(self):
        """Test container startup time"""
        self.log("Testing container startup time...")
        
        self.cleanup_containers()
        
        start_time = time.time()
        
        run_cmd = [
            "docker", "run", "-d",
            "--name", self.test_container_name,
            "-p", f"{self.http_port}:8080",
            "-p", f"{self.unity_port}:6400",
            "-e", "LOG_LEVEL=DEBUG",
            self.test_image_name
        ]
        
        self.run_command(run_cmd)
        
        # Wait for HTTP server to respond
        timeout = 15  # Should be fast without Unity
        elapsed = 0
        server_ready = False
        
        while elapsed < timeout:
            status_code, _ = self.http_request("GET", "/health", timeout=3)
            if status_code == 200:
                server_ready = True
                break
            time.sleep(1)
            elapsed += 1
        
        startup_time = time.time() - start_time
        self.log(f"Container startup time: {startup_time:.2f}s")
        
        if not server_ready:
            # Get logs for debugging
            log_result = self.run_command(["docker", "logs", self.test_container_name], check=False)
            self.log(f"Container logs: {log_result.stdout}")
            raise Exception(f"HTTP server not ready after {timeout}s")
        
        # Should be quite fast without Unity
        if startup_time > 20:
            self.log(f"⚠️  Warning: Startup time ({startup_time:.2f}s) is longer than expected")
        
        self.log("✅ Container startup test passed")
        return True
    
    def test_health_endpoint_responds(self):
        """Test health endpoint functionality"""
        self.log("Testing health endpoint...")
        
        status_code, response_text = self.http_request("GET", "/health")
        
        if status_code != 200:
            raise Exception(f"Health endpoint returned {status_code}")
        
        try:
            health_data = json.loads(response_text)
            required_fields = ["status", "timestamp"]
            for field in required_fields:
                if field not in health_data:
                    raise Exception(f"Health response missing {field}")
            
            # Should be degraded since Unity isn't connected
            status = health_data.get("status")
            if status not in ["healthy", "degraded", "starting"]:
                raise Exception(f"Unexpected health status: {status}")
                
        except json.JSONDecodeError:
            raise Exception("Health endpoint returned invalid JSON")
        
        self.log("✅ Health endpoint test passed")
        return True
    
    # ===============================
    # API TESTS
    # ===============================
    
    def test_status_endpoint(self):
        """Test status endpoint"""
        self.log("Testing status endpoint...")
        
        status_code, response_text = self.http_request("GET", "/status")
        
        if status_code != 200:
            raise Exception(f"Status endpoint returned {status_code}")
        
        try:
            status_data = json.loads(response_text)
            required_fields = ["server", "version"]
            for field in required_fields:
                if field not in status_data:
                    raise Exception(f"Status response missing {field}")
        except json.JSONDecodeError:
            raise Exception("Status endpoint returned invalid JSON")
        
        self.log("✅ Status endpoint test passed")
        return True
    
    def test_basic_command_handling(self):
        """Test basic command handling (should work without Unity for ping)"""
        self.log("Testing basic command handling...")
        
        command_data = {
            "action": "ping",
            "params": {},
            "userId": "test-user"
        }
        
        status_code, response_text = self.http_request("POST", "/execute-command", command_data, timeout=10)
        
        if status_code != 200:
            # May fail without Unity, but should not crash the server
            self.log(f"⚠️  Command returned {status_code} (expected without Unity)")
        else:
            try:
                response_data = json.loads(response_text)
                if "commandId" not in response_data:
                    self.log("⚠️  Response missing commandId (may be expected without Unity)")
            except json.JSONDecodeError:
                self.log("⚠️  Invalid JSON response (may be expected without Unity)")
        
        # The important thing is that the server didn't crash
        # Check that health is still working
        health_status, _ = self.http_request("GET", "/health", timeout=5)
        if health_status != 200:
            raise Exception("Server crashed after command execution")
        
        self.log("✅ Basic command handling test passed")
        return True
    
    def test_container_processes(self):
        """Test that required processes are running"""
        self.log("Testing container processes...")
        
        result = self.run_command(["docker", "exec", self.test_container_name, "ps", "aux"])
        processes = result.stdout
        
        if "headless_server.py" not in processes:
            raise Exception("Python server process not found")
        
        self.log("✅ Container processes test passed")
        return True
    
    def test_container_environment(self):
        """Test container environment"""
        self.log("Testing container environment...")
        
        # Check user
        whoami_result = self.run_command([
            "docker", "exec", self.test_container_name, "whoami"
        ])
        
        if whoami_result.stdout.strip() != "unity":
            raise Exception(f"Wrong user: {whoami_result.stdout.strip()}")
        
        # Check working directory
        pwd_result = self.run_command([
            "docker", "exec", self.test_container_name, "pwd"
        ])
        
        if pwd_result.stdout.strip() != "/app":
            raise Exception(f"Wrong working directory: {pwd_result.stdout.strip()}")
        
        self.log("✅ Container environment test passed")
        return True
    
    # ===============================
    # MAIN TEST EXECUTION
    # ===============================
    
    def run_all_tests(self):
        """Run all basic Docker tests"""
        self.log("Starting basic Docker test suite for Unity MCP...")
        self.log(f"Project root: {self.project_root}")
        self.log(f"Test image: {self.test_image_name}")
        
        tests = [
            ("Docker Build", self.test_docker_build_succeeds),
            ("Image Size (Reasonable)", self.test_image_size_reasonable),
            ("Security Configuration", self.test_security_configuration),
            ("Container Startup Time", self.test_container_startup_time),
            ("Health Endpoint", self.test_health_endpoint_responds),
            ("Status Endpoint", self.test_status_endpoint),
            ("Basic Command Handling", self.test_basic_command_handling),
            ("Container Processes", self.test_container_processes),
            ("Container Environment", self.test_container_environment),
        ]
        
        passed = 0
        failed = 0
        
        for test_name, test_func in tests:
            start_time = time.time()
            try:
                self.log(f"\n--- Running {test_name} ---")
                
                result = test_func()
                
                duration = time.time() - start_time
                self.results.append({
                    "test": test_name,
                    "status": "PASSED",
                    "duration": duration,
                    "error": None
                })
                
                passed += 1
                self.log(f"✅ {test_name} PASSED ({duration:.2f}s)")
                
            except Exception as e:
                duration = time.time() - start_time
                error_msg = str(e)
                
                self.results.append({
                    "test": test_name,
                    "status": "FAILED", 
                    "duration": duration,
                    "error": error_msg
                })
                
                failed += 1
                self.log(f"❌ {test_name} FAILED ({duration:.2f}s): {error_msg}")
        
        # Final cleanup
        self.cleanup_containers()
        
        # Print summary
        self.print_summary(passed, failed)
        
        return failed == 0
    
    def print_summary(self, passed, failed):
        """Print test summary"""
        total = passed + failed
        success_rate = (passed / total * 100) if total > 0 else 0
        
        print("\n" + "="*80)
        print("UNITY MCP BASIC DOCKER TEST RESULTS")
        print("="*80)
        print(f"Total Tests: {total}")
        print(f"Passed: {passed}")
        print(f"Failed: {failed}")
        print(f"Success Rate: {success_rate:.1f}%")
        print()
        
        # Docker Core Requirements Check
        print("DOCKER CORE FUNCTIONALITY:")
        
        build_passed = any(r["test"] == "Docker Build" and r["status"] == "PASSED" for r in self.results)
        size_passed = any(r["test"] == "Image Size (Reasonable)" and r["status"] == "PASSED" for r in self.results)
        startup_passed = any(r["test"] == "Container Startup Time" and r["status"] == "PASSED" for r in self.results)
        api_passed = any(r["test"] == "Health Endpoint" and r["status"] == "PASSED" for r in self.results)
        security_passed = any(r["test"] == "Security Configuration" and r["status"] == "PASSED" for r in self.results)
        
        print(f"✅ Image builds correctly: {'PASS' if build_passed else 'FAIL'}")
        print(f"✅ Container size reasonable: {'PASS' if size_passed else 'FAIL'}")
        print(f"✅ Container startup working: {'PASS' if startup_passed else 'FAIL'}")
        print(f"✅ HTTP API responding: {'PASS' if api_passed else 'FAIL'}")
        print(f"✅ Security hardening: {'PASS' if security_passed else 'FAIL'}")
        
        core_passed = all([build_passed, size_passed, startup_passed, api_passed, security_passed])
        
        print(f"\n🎯 DOCKER CORE STATUS: {'✅ PASSED' if core_passed else '❌ FAILED'}")
        print(f"\n📝 Note: This test suite validates Docker containerization without Unity installation.")
        print(f"    Full Unity integration requires Unity license and longer build times.")
        
        if failed > 0:
            print(f"\nFAILED TESTS:")
            for result in self.results:
                if result["status"] == "FAILED":
                    print(f"- {result['test']}: {result['error']}")
        
        print("="*80)


def main():
    """Main entry point"""
    runner = BasicDockerTestRunner()
    
    try:
        success = runner.run_all_tests()
        return 0 if success else 1
    except KeyboardInterrupt:
        print("\n⚠️  Test execution interrupted by user")
        runner.cleanup_containers()
        return 1
    except Exception as e:
        print(f"\n💥 Test runner failed: {e}")
        runner.cleanup_containers()
        return 1


if __name__ == "__main__":
    exit(main())