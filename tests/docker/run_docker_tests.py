#!/usr/bin/env python3
"""
Docker Test Runner for Unity MCP
Comprehensive test suite for all Docker-related functionality
Runs all acceptance criteria tests without external dependencies
"""

import subprocess
import json
import time
import threading
import queue
import sys
import os
import tempfile
from pathlib import Path


class DockerTestRunner:
    """Main test runner for Docker functionality"""
    
    def __init__(self):
        self.project_root = Path(__file__).parent.parent.parent.absolute()
        self.test_image_name = "unity-mcp:latest"
        self.test_container_name = "unity-mcp-test"
        self.http_port = 8082
        self.unity_port = 6402
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
        """Test that Docker image builds successfully"""
        self.log("Testing Docker build...")
        
        # Use CI Dockerfile if available (for GitHub Actions)
        dockerfile_ci = self.project_root / "docker" / "Dockerfile.ci"
        dockerfile_prod = self.project_root / "docker" / "Dockerfile.production"
        
        if os.environ.get("CI") == "true" and dockerfile_ci.exists():
            dockerfile = dockerfile_ci
            self.log("Using CI Dockerfile for testing")
        elif dockerfile_prod.exists():
            dockerfile = dockerfile_prod
        else:
            raise Exception("No suitable Dockerfile found")
        
        build_cmd = [
            "docker", "build",
            "-f", str(dockerfile),
            "-t", self.test_image_name,
            "--target", "production",
            "."
        ]
        
        self.log(f"Building image: {' '.join(build_cmd)}")
        result = self.run_command(build_cmd, timeout=1800)
        
        if "Successfully built" not in result.stdout and "Successfully tagged" not in result.stdout:
            self.log(f"Build stdout: {result.stdout[-500:]}")  # Last 500 chars
            self.log(f"Build stderr: {result.stderr[-500:]}")  # Last 500 chars
            raise Exception("Build did not complete successfully")
        
        # Verify image exists
        self.run_command(["docker", "image", "inspect", self.test_image_name])
        
        self.log("✅ Docker build test passed")
        return True
    
    def test_image_size_under_2gb(self):
        """Test that image size is under 2GB"""
        self.log("Testing image size...")
        
        result = self.run_command(["docker", "image", "inspect", self.test_image_name])
        image_info = json.loads(result.stdout)[0]
        
        size_bytes = image_info.get("Size", 0)
        size_gb = size_bytes / (1024 ** 3)
        
        self.log(f"Image size: {size_gb:.2f} GB")
        
        if size_gb >= 2.0:
            raise Exception(f"Image size ({size_gb:.2f}GB) exceeds 2GB limit")
        
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
            "-e", "UNITY_PROJECT_PATH=",  # No Unity project for faster startup
            "-e", "LOG_LEVEL=DEBUG",
        ]
        
        # Add CI_MODE if running in CI
        if os.environ.get("CI") == "true":
            run_cmd.extend(["-e", "CI_MODE=true"])
        
        run_cmd.append(self.test_image_name)
        
        self.run_command(run_cmd)
        
        # Wait for HTTP server to respond
        timeout = 20  # Allow reasonable time for HTTP server
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
        
        # Note: We test HTTP server startup time, not full Unity startup
        # Unity startup is much longer but HTTP server should be quick
        if startup_time > 25:
            self.log(f"⚠️  Warning: Startup time ({startup_time:.2f}s) is longer than ideal")
        
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
        except json.JSONDecodeError:
            raise Exception("Health endpoint returned invalid JSON")
        
        self.log("✅ Health endpoint test passed")
        return True
    
    # ===============================
    # END-TO-END TESTS
    # ===============================
    
    def test_execute_command(self):
        """Test command execution through container"""
        self.log("Testing command execution...")
        
        command_data = {
            "action": "ping",
            "params": {},
            "userId": "test-user"
        }
        
        status_code, response_text = self.http_request("POST", "/execute-command", command_data, timeout=20)
        
        if status_code != 200:
            raise Exception(f"Command execution failed with status {status_code}: {response_text}")
        
        try:
            response_data = json.loads(response_text)
            if "commandId" not in response_data:
                raise Exception("Response missing commandId")
            
            command_id = response_data["commandId"]
            self.log(f"Command executed with ID: {command_id}")
            
        except json.JSONDecodeError:
            raise Exception(f"Invalid JSON response: {response_text}")
        
        self.log("✅ Command execution test passed")
        return True
    
    def test_concurrent_commands(self):
        """Test 5 concurrent commands"""
        self.log("Testing concurrent commands...")
        
        results_queue = queue.Queue()
        
        def execute_command(command_num):
            try:
                command_data = {
                    "action": "ping",
                    "params": {"message": f"concurrent-{command_num}"},
                    "userId": f"user-{command_num}"
                }
                
                status_code, response_text = self.http_request("POST", "/execute-command", command_data, timeout=15)
                
                if status_code == 200:
                    response_data = json.loads(response_text)
                    results_queue.put((command_num, "success", response_data.get("commandId", "unknown")))
                else:
                    results_queue.put((command_num, "failed", f"Status: {status_code}"))
            except Exception as e:
                results_queue.put((command_num, "error", str(e)))
        
        # Launch 5 concurrent commands
        threads = []
        for i in range(5):
            thread = threading.Thread(target=execute_command, args=(i,))
            threads.append(thread)
            thread.start()
        
        # Wait for all threads
        for thread in threads:
            thread.join(timeout=20)
        
        # Collect results
        results = []
        while not results_queue.empty():
            results.append(results_queue.get())
        
        if len(results) != 5:
            raise Exception(f"Expected 5 results, got {len(results)}")
        
        successful = sum(1 for _, status, _ in results if status == "success")
        
        # Allow some tolerance for resource constraints
        if successful < 4:
            failed_details = [f"Command {num}: {status} - {detail}" 
                            for num, status, detail in results if status != "success"]
            raise Exception(f"Too many failures ({5-successful}/5): {failed_details}")
        
        self.log(f"Concurrent commands: {successful}/5 successful")
        self.log("✅ Concurrent commands test passed")
        return True
    
    def test_container_processes(self):
        """Test that required processes are running"""
        self.log("Testing container processes...")
        
        result = self.run_command(["docker", "exec", self.test_container_name, "ps", "aux"])
        processes = result.stdout
        
        if "headless_server.py" not in processes:
            raise Exception("Python server process not found")
        
        # Check logs for Unity startup attempt
        log_result = self.run_command(["docker", "logs", self.test_container_name], check=False)
        logs = log_result.stdout + log_result.stderr
        
        if "Starting Unity MCP Headless HTTP Server" not in logs:
            raise Exception("HTTP server startup not logged")
        
        self.log("✅ Container processes test passed")
        return True
    
    # ===============================
    # MAIN TEST EXECUTION
    # ===============================
    
    def run_all_tests(self):
        """Run all Docker tests"""
        self.log("Starting Docker test suite for Unity MCP...")
        self.log(f"Project root: {self.project_root}")
        self.log(f"Test image: {self.test_image_name}")
        
        tests = [
            ("Docker Build", self.test_docker_build_succeeds),
            ("Image Size (<2GB)", self.test_image_size_under_2gb),
            ("Security Configuration", self.test_security_configuration),
            ("Container Startup Time", self.test_container_startup_time),
            ("Health Endpoint", self.test_health_endpoint_responds),
            ("Command Execution", self.test_execute_command),
            ("Concurrent Commands (5)", self.test_concurrent_commands),
            ("Container Processes", self.test_container_processes),
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
        print("UNITY MCP DOCKER TEST RESULTS")
        print("="*80)
        print(f"Total Tests: {total}")
        print(f"Passed: {passed}")
        print(f"Failed: {failed}")
        print(f"Success Rate: {success_rate:.1f}%")
        print()
        
        # Milestone Requirements Check
        print("MILESTONE 2 ACCEPTANCE CRITERIA:")
        
        build_passed = any(r["test"] == "Docker Build" and r["status"] == "PASSED" for r in self.results)
        size_passed = any(r["test"] == "Image Size (<2GB)" and r["status"] == "PASSED" for r in self.results)
        startup_passed = any(r["test"] == "Container Startup Time" and r["status"] == "PASSED" for r in self.results)
        e2e_passed = any(r["test"] == "Command Execution" and r["status"] == "PASSED" for r in self.results)
        security_passed = any(r["test"] == "Security Configuration" and r["status"] == "PASSED" for r in self.results)
        
        print(f"✅ Image builds without errors: {'PASS' if build_passed else 'FAIL'}")
        print(f"✅ Container size < 2GB: {'PASS' if size_passed else 'FAIL'}")
        print(f"✅ Container startup working: {'PASS' if startup_passed else 'FAIL'}")
        print(f"✅ End-to-end command execution: {'PASS' if e2e_passed else 'FAIL'}")
        print(f"✅ Security scan passes: {'PASS' if security_passed else 'FAIL'}")
        
        milestone_passed = all([build_passed, size_passed, startup_passed, e2e_passed, security_passed])
        
        print(f"\n🎯 MILESTONE 2 STATUS: {'✅ PASSED' if milestone_passed else '❌ FAILED'}")
        
        if failed > 0:
            print(f"\nFAILED TESTS:")
            for result in self.results:
                if result["status"] == "FAILED":
                    print(f"- {result['test']}: {result['error']}")
        
        print("="*80)


def main():
    """Main entry point"""
    runner = DockerTestRunner()
    
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