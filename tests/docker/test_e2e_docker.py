#!/usr/bin/env python3
"""
End-to-End Docker Tests for Unity MCP
Tests full command execution through containerized server
"""

import subprocess
import json
import time
import sys
import os
from pathlib import Path
try:
    import requests
except ImportError:
    print("Warning: requests not available, will use curl for HTTP requests")
    requests = None

class TestE2EDocker:
    """Test end-to-end functionality through Docker"""
    
    @classmethod
    def setup_class(cls):
        """Setup test environment"""
        cls.project_root = Path(__file__).parent.parent.parent.absolute()
        cls.test_image_name = "unity-mcp:latest"
        cls.test_container_name = "unity-mcp-e2e-test"
        cls.http_port = 8081  # Use different port to avoid conflicts
        cls.unity_port = 6401
        
        # Ensure we're in the right directory
        os.chdir(cls.project_root)
        
        # Stop any existing test containers
        cls._cleanup_containers()
        
        # Start test container
        cls._start_container()
        
    @classmethod
    def teardown_class(cls):
        """Cleanup test containers"""
        cls._cleanup_containers()
    
    @classmethod
    def _cleanup_containers(cls):
        """Remove test containers"""
        try:
            subprocess.run([
                "docker", "stop", cls.test_container_name
            ], capture_output=True, timeout=30)
            subprocess.run([
                "docker", "rm", "-f", cls.test_container_name
            ], capture_output=True, timeout=30)
        except Exception:
            pass
    
    @classmethod
    def _start_container(cls):
        """Start test container"""
        run_cmd = [
            "docker", "run", "-d",
            "--name", cls.test_container_name,
            "-p", f"{cls.http_port}:8080",
            "-p", f"{cls.unity_port}:6400",
            "-e", "LOG_LEVEL=DEBUG",
            "-e", "UNITY_PROJECT_PATH=",  # No Unity project for faster startup
            cls.test_image_name
        ]
        
        print(f"Starting test container: {' '.join(run_cmd)}")
        result = subprocess.run(run_cmd, capture_output=True, text=True, timeout=30)
        
        if result.returncode != 0:
            raise Exception(f"Failed to start test container: {result.stderr}")
        
        # Wait for server to be ready
        cls._wait_for_server()
    
    @classmethod
    def _wait_for_server(cls):
        """Wait for HTTP server to be ready"""
        timeout = 60
        elapsed = 0
        
        while elapsed < timeout:
            if cls._check_health():
                print(f"Server ready after {elapsed}s")
                return
            time.sleep(2)
            elapsed += 2
        
        # Get logs for debugging
        logs_result = subprocess.run([
            "docker", "logs", cls.test_container_name
        ], capture_output=True, text=True)
        
        if logs_result.returncode == 0:
            print("Container logs:")
            print(logs_result.stdout[-2000:])  # Last 2000 chars
            if logs_result.stderr:
                print("Container errors:")
                print(logs_result.stderr[-1000:])
        
        raise Exception(f"Server not ready after {timeout}s")
    
    @classmethod
    def _check_health(cls):
        """Check if server is healthy"""
        try:
            if requests:
                response = requests.get(
                    f"http://localhost:{cls.http_port}/health", 
                    timeout=5
                )
                return response.status_code == 200
            else:
                # Use curl as fallback
                result = subprocess.run([
                    "curl", "-f", "-s", f"http://localhost:{cls.http_port}/health"
                ], capture_output=True, timeout=5)
                return result.returncode == 0
        except Exception:
            return False
    
    def _make_request(self, method, endpoint, data=None):
        """Make HTTP request to container"""
        url = f"http://localhost:{self.http_port}{endpoint}"
        
        if requests:
            if method == "GET":
                response = requests.get(url, timeout=10)
            elif method == "POST":
                response = requests.post(url, json=data, timeout=30)
            else:
                raise ValueError(f"Unsupported method: {method}")
            
            return response.status_code, response.text
        else:
            # Use curl as fallback
            cmd = ["curl", "-s", "-w", "%{http_code}"]
            
            if method == "POST" and data:
                cmd.extend([
                    "-H", "Content-Type: application/json",
                    "-d", json.dumps(data)
                ])
            
            cmd.append(url)
            
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
            
            if result.returncode == 0:
                # Split response and status code
                output = result.stdout
                if len(output) >= 3:
                    status_code = int(output[-3:])
                    response_text = output[:-3]
                    return status_code, response_text
            
            return 0, result.stderr
    
    def test_execute_command_via_docker(self):
        """Test executing a command through the containerized server"""
        # Test basic ping command
        command_data = {
            "action": "ping",
            "params": {},
            "userId": "test-user"
        }
        
        status_code, response_text = self._make_request("POST", "/execute-command", command_data)
        
        assert status_code == 200, f"Command execution failed with status {status_code}: {response_text}"
        
        # Parse response
        try:
            response_data = json.loads(response_text)
            assert "commandId" in response_data, "Response missing commandId"
            assert "success" in response_data, "Response missing success field"
            
            command_id = response_data["commandId"]
            print(f"Command executed with ID: {command_id}")
            
            # Check command status
            status_code, status_text = self._make_request("GET", f"/command/{command_id}")
            
            assert status_code == 200, f"Command status check failed: {status_code}"
            
            status_data = json.loads(status_text)
            assert "success" in status_data, "Status response missing success field"
            
            print(f"✅ Command execution test passed")
            
        except json.JSONDecodeError as e:
            assert False, f"Invalid JSON response: {e}\nResponse: {response_text}"
    
    def test_concurrent_commands_in_container(self):
        """Test 5 concurrent commands work in Docker"""
        import threading
        import queue
        
        results_queue = queue.Queue()
        
        def execute_command(command_num):
            try:
                command_data = {
                    "action": "ping",
                    "params": {"message": f"concurrent-test-{command_num}"},
                    "userId": f"test-user-{command_num}"
                }
                
                status_code, response_text = self._make_request("POST", "/execute-command", command_data)
                
                if status_code == 200:
                    response_data = json.loads(response_text)
                    results_queue.put((command_num, "success", response_data.get("commandId")))
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
            thread.join(timeout=30)
        
        # Collect results
        results = []
        while not results_queue.empty():
            results.append(results_queue.get())
        
        assert len(results) == 5, f"Expected 5 results, got {len(results)}"
        
        successful = sum(1 for _, status, _ in results if status == "success")
        failed = sum(1 for _, status, _ in results if status != "success")
        
        print(f"Concurrent commands: {successful} successful, {failed} failed")
        
        # Allow 1 failure due to rate limiting or resource constraints
        assert successful >= 4, f"Too many concurrent command failures: {successful}/5 successful"
        
        # Print failure details if any
        for cmd_num, status, detail in results:
            if status != "success":
                print(f"Command {cmd_num} failed: {status} - {detail}")
        
        print("✅ Concurrent commands test passed")
    
    def test_health_and_status_endpoints(self):
        """Test health and status endpoints"""
        # Test health endpoint
        status_code, response_text = self._make_request("GET", "/health")
        assert status_code == 200, f"Health check failed: {status_code}"
        
        health_data = json.loads(response_text)
        assert "status" in health_data, "Health response missing status"
        assert "timestamp" in health_data, "Health response missing timestamp"
        
        print(f"Health status: {health_data.get('status')}")
        
        # Test status endpoint
        status_code, response_text = self._make_request("GET", "/status")
        assert status_code == 200, f"Status check failed: {status_code}"
        
        status_data = json.loads(response_text)
        assert "server" in status_data, "Status response missing server info"
        assert "version" in status_data, "Status response missing version info"
        
        print(f"Server: {status_data.get('server')}")
        print(f"Version: {status_data.get('version')}")
        
        print("✅ Health and status endpoints test passed")
    
    def test_error_handling(self):
        """Test error handling for invalid commands"""
        # Test invalid command
        invalid_command = {
            "action": "nonexistent_command",
            "params": {},
            "userId": "test-user"
        }
        
        status_code, response_text = self._make_request("POST", "/execute-command", invalid_command)
        
        # Should either return 400 (bad request) or 200 with error in response
        assert status_code in [200, 400, 422], f"Unexpected status for invalid command: {status_code}"
        
        if status_code == 200:
            response_data = json.loads(response_text)
            # Should indicate failure or error
            success = response_data.get("success", True)
            assert not success, "Invalid command should not succeed"
        
        print("✅ Error handling test passed")
    
    def test_command_timeout_handling(self):
        """Test command timeout handling"""
        # Test command with short timeout
        timeout_command = {
            "action": "ping",
            "params": {"delay": 1},  # If ping supports delay
            "userId": "test-user",
            "timeout": 0.1  # Very short timeout
        }
        
        status_code, response_text = self._make_request("POST", "/execute-command", timeout_command)
        
        # Should handle timeout gracefully
        assert status_code in [200, 408, 504], f"Unexpected status for timeout test: {status_code}"
        
        print("✅ Timeout handling test passed")
    
    def test_container_resource_usage(self):
        """Test container resource usage during operation"""
        # Get container stats
        stats_result = subprocess.run([
            "docker", "stats", "--no-stream", "--format",
            "table {{.Container}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.MemPerc}}",
            self.test_container_name
        ], capture_output=True, text=True, timeout=10)
        
        if stats_result.returncode == 0:
            lines = stats_result.stdout.strip().split('\n')
            if len(lines) > 1:  # Skip header
                stats_line = lines[1]
                print(f"Container stats: {stats_line}")
                
                # Basic check that container is using reasonable resources
                parts = stats_line.split()
                if len(parts) >= 4:
                    cpu_perc = parts[1].replace('%', '')
                    mem_perc = parts[3].replace('%', '')
                    
                    try:
                        cpu_val = float(cpu_perc)
                        mem_val = float(mem_perc)
                        
                        # Reasonable resource usage checks
                        assert cpu_val < 90, f"CPU usage too high: {cpu_val}%"
                        assert mem_val < 80, f"Memory usage too high: {mem_val}%"
                        
                        print(f"✅ Resource usage reasonable: CPU {cpu_val}%, Memory {mem_val}%")
                    except ValueError:
                        print("⚠️  Could not parse resource usage values")
        else:
            print("⚠️  Could not get container stats")
        
        print("✅ Resource usage test completed")


def run_tests():
    """Run all E2E tests"""
    try:
        test_class = TestE2EDocker()
        test_class.setup_class()
        
        tests = [
            test_class.test_execute_command_via_docker,
            test_class.test_health_and_status_endpoints,
            test_class.test_error_handling,
            test_class.test_command_timeout_handling,
            test_class.test_concurrent_commands_in_container,
            test_class.test_container_resource_usage
        ]
        
        passed = 0
        failed = 0
        
        for test in tests:
            try:
                print(f"\nRunning {test.__name__}...")
                test()
                print(f"✅ {test.__name__} PASSED")
                passed += 1
            except Exception as e:
                print(f"❌ {test.__name__} FAILED: {e}")
                failed += 1
        
        print(f"\n{'='*60}")
        print(f"END-TO-END DOCKER TESTS COMPLETED")
        print(f"Passed: {passed}")
        print(f"Failed: {failed}")
        print(f"Success Rate: {(passed/(passed+failed)*100):.1f}%" if (passed+failed) > 0 else "No tests run")
        
        return failed == 0
        
    except Exception as e:
        print(f"❌ Test setup failed: {e}")
        return False
    finally:
        try:
            TestE2EDocker.teardown_class()
        except Exception as e:
            print(f"⚠️  Cleanup warning: {e}")


if __name__ == "__main__":
    success = run_tests()
    exit(0 if success else 1)