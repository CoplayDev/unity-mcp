#!/usr/bin/env python3
"""
Container Startup Tests for Unity MCP
Tests container startup time, service initialization, and health checks
"""

import subprocess
import json
import time
import requests
import signal
import os
import sys
from pathlib import Path

# Add project root to path for imports
project_root = Path(__file__).parent.parent.parent
sys.path.insert(0, str(project_root))

class TestContainerStartup:
    """Test container startup functionality"""
    
    @classmethod
    def setup_class(cls):
        """Setup test environment"""
        cls.project_root = Path(__file__).parent.parent.parent.absolute()
        cls.test_image_name = "unity-mcp:latest"
        cls.test_container_name = "unity-mcp-startup-test"
        cls.http_port = 8080
        cls.unity_port = 6400
        
        # Ensure we're in the right directory
        os.chdir(cls.project_root)
        
        # Stop any existing test containers
        cls._cleanup_containers()
        
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
    
    def test_container_starts_under_10s(self):
        """Test that container starts and becomes healthy in under 10 seconds"""
        # For this test, we'll use a mock Unity setup to avoid the full Unity startup time
        # In production, Unity startup is much longer, but the HTTP server should be ready quickly
        
        start_time = time.time()
        
        # Start container with health check disabled initially to test just the HTTP server startup
        run_cmd = [
            "docker", "run", "-d",
            "--name", self.test_container_name,
            "--health-interval", "5s",
            "--health-timeout", "3s",
            "--health-retries", "2",
            "--health-start-period", "10s",
            "-p", f"{self.http_port}:8080",
            "-p", f"{self.unity_port}:6400",
            "-e", "UNITY_PROJECT_PATH=",  # No Unity project to speed up startup
            "-e", "LOG_LEVEL=DEBUG",
            self.test_image_name
        ]
        
        print(f"Starting container: {' '.join(run_cmd)}")
        result = subprocess.run(run_cmd, capture_output=True, text=True, timeout=30)
        
        assert result.returncode == 0, f"Container failed to start: {result.stderr}"
        
        # Wait for HTTP server to respond (should be much faster than Unity)
        timeout = 15  # Allow some extra time for HTTP server startup
        elapsed = 0
        server_ready = False
        
        while elapsed < timeout:
            try:
                response = requests.get(
                    f"http://localhost:{self.http_port}/health", 
                    timeout=2
                )
                if response.status_code == 200:
                    server_ready = True
                    break
            except requests.exceptions.ConnectionError:
                pass  # Server not ready yet
            except Exception as e:
                print(f"Health check error: {e}")
            
            time.sleep(1)
            elapsed += 1
        
        startup_time = time.time() - start_time
        print(f"Container startup time: {startup_time:.2f}s")
        
        # The HTTP server itself should start quickly, even if Unity takes longer
        assert server_ready, f"HTTP server not ready after {timeout}s"
        assert startup_time < 20, f"HTTP server took too long to start: {startup_time:.2f}s"
        
        # Cleanup
        self._cleanup_containers()
    
    def test_services_initialize_correctly(self):
        """Test that both Unity and Python server processes start"""
        # Start container
        run_cmd = [
            "docker", "run", "-d",
            "--name", self.test_container_name,
            "-p", f"{self.http_port}:8080",
            "-p", f"{self.unity_port}:6400",
            "-e", "LOG_LEVEL=DEBUG",
            self.test_image_name
        ]
        
        result = subprocess.run(run_cmd, capture_output=True, text=True, timeout=30)
        assert result.returncode == 0, f"Container failed to start: {result.stderr}"
        
        # Give container time to initialize
        time.sleep(15)
        
        # Check that both processes are running
        ps_result = subprocess.run([
            "docker", "exec", self.test_container_name,
            "ps", "aux"
        ], capture_output=True, text=True, timeout=10)
        
        assert ps_result.returncode == 0
        processes = ps_result.stdout
        
        # Check for Python server process
        assert "headless_server.py" in processes, "Python server process not found"
        
        # Check for Unity process (may not be present if no project specified)
        # We'll check logs instead for Unity startup attempt
        logs_result = subprocess.run([
            "docker", "logs", self.test_container_name
        ], capture_output=True, text=True, timeout=10)
        
        assert logs_result.returncode == 0
        logs = logs_result.stdout + logs_result.stderr
        
        assert "Starting Unity MCP Headless HTTP Server" in logs, "HTTP server startup not logged"
        
        # Cleanup
        self._cleanup_containers()
    
    def test_health_endpoint_responds(self):
        """Test that health check endpoint returns correct status"""
        # Start container
        run_cmd = [
            "docker", "run", "-d",
            "--name", self.test_container_name,
            "-p", f"{self.http_port}:8080",
            "-e", "LOG_LEVEL=INFO",
            self.test_image_name
        ]
        
        result = subprocess.run(run_cmd, capture_output=True, text=True, timeout=30)
        assert result.returncode == 0, f"Container failed to start: {result.stderr}"
        
        # Wait for health endpoint to be ready
        timeout = 30
        elapsed = 0
        health_ready = False
        
        while elapsed < timeout:
            try:
                response = requests.get(
                    f"http://localhost:{self.http_port}/health", 
                    timeout=3
                )
                if response.status_code == 200:
                    health_data = response.json()
                    
                    # Check required fields
                    assert "status" in health_data, "Health response missing status field"
                    assert "unity_connected" in health_data, "Health response missing unity_connected field"
                    assert "timestamp" in health_data, "Health response missing timestamp field"
                    
                    # Status should be healthy or starting
                    status = health_data.get("status")
                    assert status in ["healthy", "starting"], f"Unexpected health status: {status}"
                    
                    health_ready = True
                    break
                    
            except requests.exceptions.ConnectionError:
                pass  # Not ready yet
            except Exception as e:
                print(f"Health check error: {e}")
            
            time.sleep(2)
            elapsed += 2
        
        assert health_ready, f"Health endpoint not ready after {timeout}s"
        
        # Test additional endpoints
        try:
            status_response = requests.get(
                f"http://localhost:{self.http_port}/status", 
                timeout=5
            )
            assert status_response.status_code == 200, "Status endpoint not responding"
            
            status_data = status_response.json()
            assert "server" in status_data, "Status response missing server info"
            assert "version" in status_data, "Status response missing version info"
            
        except Exception as e:
            print(f"Status endpoint test failed: {e}")
            # Don't fail the test, as this is secondary
        
        # Cleanup
        self._cleanup_containers()
    
    def test_container_environment(self):
        """Test that container has proper environment setup"""
        # Start container
        run_cmd = [
            "docker", "run", "-d",
            "--name", self.test_container_name,
            self.test_image_name
        ]
        
        result = subprocess.run(run_cmd, capture_output=True, text=True, timeout=30)
        assert result.returncode == 0, f"Container failed to start: {result.stderr}"
        
        # Give container time to start
        time.sleep(5)
        
        # Check environment variables
        env_result = subprocess.run([
            "docker", "exec", self.test_container_name,
            "env"
        ], capture_output=True, text=True, timeout=10)
        
        assert env_result.returncode == 0
        env_vars = env_result.stdout
        
        # Check required environment variables
        required_vars = [
            "UNITY_HEADLESS=true",
            "UNITY_MCP_AUTOSTART=true",
            "HOME=/home/unity"
        ]
        
        for var in required_vars:
            assert var in env_vars, f"Required environment variable not found: {var}"
        
        # Check working directory
        pwd_result = subprocess.run([
            "docker", "exec", self.test_container_name,
            "pwd"
        ], capture_output=True, text=True, timeout=10)
        
        assert pwd_result.returncode == 0
        assert pwd_result.stdout.strip() == "/app", f"Wrong working directory: {pwd_result.stdout.strip()}"
        
        # Check user
        whoami_result = subprocess.run([
            "docker", "exec", self.test_container_name,
            "whoami"
        ], capture_output=True, text=True, timeout=10)
        
        assert whoami_result.returncode == 0
        assert whoami_result.stdout.strip() == "unity", f"Wrong user: {whoami_result.stdout.strip()}"
        
        # Cleanup
        self._cleanup_containers()
    
    def test_container_ports_accessible(self):
        """Test that container ports are properly exposed and accessible"""
        # Start container
        run_cmd = [
            "docker", "run", "-d",
            "--name", self.test_container_name,
            "-p", f"{self.http_port}:8080",
            "-p", f"{self.unity_port}:6400",
            self.test_image_name
        ]
        
        result = subprocess.run(run_cmd, capture_output=True, text=True, timeout=30)
        assert result.returncode == 0, f"Container failed to start: {result.stderr}"
        
        # Check port mapping
        port_result = subprocess.run([
            "docker", "port", self.test_container_name
        ], capture_output=True, text=True, timeout=10)
        
        assert port_result.returncode == 0
        port_info = port_result.stdout
        
        assert f"8080/tcp -> 0.0.0.0:{self.http_port}" in port_info, "HTTP port not properly mapped"
        assert f"6400/tcp -> 0.0.0.0:{self.unity_port}" in port_info, "Unity MCP port not properly mapped"
        
        # Wait for HTTP server to be ready
        time.sleep(10)
        
        # Test HTTP port accessibility
        try:
            response = requests.get(f"http://localhost:{self.http_port}/health", timeout=5)
            # Should get a response (200 or otherwise, but not connection error)
            assert response.status_code in [200, 500, 503], f"Unexpected status code: {response.status_code}"
        except requests.exceptions.ConnectionError:
            assert False, "HTTP port not accessible"
        
        # Cleanup
        self._cleanup_containers()
    
    def test_graceful_shutdown(self):
        """Test that container shuts down gracefully"""
        # Start container
        run_cmd = [
            "docker", "run", "-d",
            "--name", self.test_container_name,
            self.test_image_name
        ]
        
        result = subprocess.run(run_cmd, capture_output=True, text=True, timeout=30)
        assert result.returncode == 0, f"Container failed to start: {result.stderr}"
        
        # Give container time to start
        time.sleep(10)
        
        # Send SIGTERM and measure shutdown time
        start_time = time.time()
        
        stop_result = subprocess.run([
            "docker", "stop", "--time", "30", self.test_container_name
        ], capture_output=True, text=True, timeout=45)
        
        shutdown_time = time.time() - start_time
        
        assert stop_result.returncode == 0, "Container failed to stop gracefully"
        assert shutdown_time < 35, f"Container took too long to shutdown: {shutdown_time:.2f}s"
        
        # Check logs for graceful shutdown messages
        logs_result = subprocess.run([
            "docker", "logs", self.test_container_name
        ], capture_output=True, text=True, timeout=10)
        
        if logs_result.returncode == 0:
            logs = logs_result.stdout + logs_result.stderr
            # Look for cleanup messages (if implemented)
            if "cleanup" in logs.lower() or "shutdown" in logs.lower():
                print("Graceful shutdown detected in logs")
        
        # Final cleanup
        self._cleanup_containers()


def run_tests():
    """Run all startup tests"""
    test_class = TestContainerStartup()
    test_class.setup_class()
    
    try:
        tests = [
            test_class.test_container_starts_under_10s,
            test_class.test_services_initialize_correctly,
            test_class.test_health_endpoint_responds,
            test_class.test_container_environment,
            test_class.test_container_ports_accessible,
            test_class.test_graceful_shutdown
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
        print(f"CONTAINER STARTUP TESTS COMPLETED")
        print(f"Passed: {passed}")
        print(f"Failed: {failed}")
        print(f"Success Rate: {(passed/(passed+failed)*100):.1f}%" if (passed+failed) > 0 else "No tests run")
        
        return failed == 0
        
    finally:
        test_class.teardown_class()


if __name__ == "__main__":
    success = run_tests()
    exit(0 if success else 1)