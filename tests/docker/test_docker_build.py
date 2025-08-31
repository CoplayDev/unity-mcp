#!/usr/bin/env python3
"""
Docker Build Tests for Unity MCP
Tests Docker image building, caching, and build arguments
"""

import pytest
import subprocess
import json
import time
import tempfile
import os
from pathlib import Path

class TestDockerBuild:
    """Test Docker build functionality"""
    
    @classmethod
    def setup_class(cls):
        """Setup test environment"""
        cls.project_root = Path(__file__).parent.parent.parent.absolute()
        cls.dockerfile_prod = cls.project_root / "docker" / "Dockerfile.production"
        cls.dockerfile_dev = cls.project_root / "docker" / "Dockerfile.dev"
        cls.test_image_name = "unity-mcp-test"
        cls.test_image_tag = f"{cls.test_image_name}:build-test"
        
        # Ensure we're in the right directory
        os.chdir(cls.project_root)
        
    @classmethod
    def teardown_class(cls):
        """Cleanup test images"""
        try:
            # Remove test images
            subprocess.run([
                "docker", "rmi", "-f", cls.test_image_tag
            ], capture_output=True)
        except Exception:
            pass
    
    def test_dockerfile_production_exists(self):
        """Test that production Dockerfile exists and is valid"""
        assert self.dockerfile_prod.exists(), "Production Dockerfile not found"
        
        # Check basic Dockerfile syntax
        with open(self.dockerfile_prod, 'r') as f:
            content = f.read()
            assert "FROM ubuntu:22.04" in content, "Base image not found"
            assert "COPY --from=" in content, "Multi-stage build not detected"
            assert "USER unity" in content, "Non-root user not set"
            assert "HEALTHCHECK" in content, "Health check not configured"
    
    def test_dockerfile_dev_exists(self):
        """Test that development Dockerfile exists"""
        assert self.dockerfile_dev.exists(), "Development Dockerfile not found"
    
    def test_production_build_succeeds(self):
        """Test that production Docker image builds successfully"""
        build_cmd = [
            "docker", "build",
            "-f", str(self.dockerfile_prod),
            "-t", self.test_image_tag,
            "--target", "production",
            "."
        ]
        
        print(f"Running: {' '.join(build_cmd)}")
        result = subprocess.run(build_cmd, capture_output=True, text=True, timeout=1800)
        
        assert result.returncode == 0, f"Docker build failed: {result.stderr}"
        assert "Successfully built" in result.stdout or "Successfully tagged" in result.stdout
        
        # Verify image exists
        inspect_result = subprocess.run([
            "docker", "image", "inspect", self.test_image_tag
        ], capture_output=True, text=True)
        
        assert inspect_result.returncode == 0, "Built image not found"
    
    def test_build_cache_efficiency(self):
        """Test that subsequent builds use cache effectively"""
        # First build (already done in previous test)
        start_time = time.time()
        
        # Second build should be much faster due to caching
        build_cmd = [
            "docker", "build",
            "-f", str(self.dockerfile_prod),
            "-t", f"{self.test_image_name}:cache-test",
            "--target", "production",
            "."
        ]
        
        result = subprocess.run(build_cmd, capture_output=True, text=True, timeout=600)
        build_time = time.time() - start_time
        
        assert result.returncode == 0, f"Cached build failed: {result.stderr}"
        assert build_time < 300, f"Cached build too slow: {build_time}s > 300s"
        
        # Cleanup
        subprocess.run([
            "docker", "rmi", "-f", f"{self.test_image_name}:cache-test"
        ], capture_output=True)
    
    def test_build_args_respected(self):
        """Test that build arguments are properly used"""
        custom_unity_version = "2022.3.40f1"
        custom_python_version = "3.10"
        
        build_cmd = [
            "docker", "build",
            "-f", str(self.dockerfile_prod),
            "-t", f"{self.test_image_name}:args-test",
            "--build-arg", f"UNITY_VERSION={custom_unity_version}",
            "--build-arg", f"PYTHON_VERSION={custom_python_version}",
            "--target", "unity-base",  # Build only to base to speed up test
            "."
        ]
        
        result = subprocess.run(build_cmd, capture_output=True, text=True, timeout=900)
        
        assert result.returncode == 0, f"Build with custom args failed: {result.stderr}"
        
        # Cleanup
        subprocess.run([
            "docker", "rmi", "-f", f"{self.test_image_name}:args-test"
        ], capture_output=True)
    
    def test_multi_stage_optimization(self):
        """Test that multi-stage build produces optimized final image"""
        # Inspect the image layers
        inspect_result = subprocess.run([
            "docker", "image", "inspect", self.test_image_tag
        ], capture_output=True, text=True)
        
        assert inspect_result.returncode == 0
        image_info = json.loads(inspect_result.stdout)[0]
        
        # Check that we have a reasonable number of layers (not too many)
        layers = image_info.get("RootFS", {}).get("Layers", [])
        assert len(layers) < 20, f"Too many layers: {len(layers)} (may indicate inefficient build)"
        
        # Check image size is reasonable (basic check before detailed size test)
        size_bytes = image_info.get("Size", 0)
        size_gb = size_bytes / (1024 ** 3)
        assert size_gb < 5.0, f"Image suspiciously large: {size_gb:.2f}GB"
    
    def test_security_configuration(self):
        """Test that image has proper security configuration"""
        inspect_result = subprocess.run([
            "docker", "image", "inspect", self.test_image_tag
        ], capture_output=True, text=True)
        
        assert inspect_result.returncode == 0
        image_info = json.loads(inspect_result.stdout)[0]
        config = image_info.get("Config", {})
        
        # Check non-root user
        user = config.get("User", "")
        assert user != "root" and user != "", f"Image should run as non-root user, got: {user}"
        assert "unity" in user, "Should run as unity user"
        
        # Check exposed ports are expected
        exposed_ports = list(config.get("ExposedPorts", {}).keys())
        expected_ports = ["8080/tcp", "6400/tcp"]
        for port in expected_ports:
            assert port in exposed_ports, f"Expected port {port} not exposed"
        
        # Check for health check
        health_check = config.get("Healthcheck")
        assert health_check is not None, "Health check not configured"
        assert "Test" in health_check, "Health check test not defined"
    
    def test_environment_variables(self):
        """Test that required environment variables are set"""
        inspect_result = subprocess.run([
            "docker", "image", "inspect", self.test_image_tag
        ], capture_output=True, text=True)
        
        assert inspect_result.returncode == 0
        image_info = json.loads(inspect_result.stdout)[0]
        config = image_info.get("Config", {})
        env_vars = {
            var.split("=")[0]: var.split("=", 1)[1] if "=" in var else ""
            for var in config.get("Env", [])
        }
        
        # Check required environment variables
        required_vars = [
            "UNITY_HEADLESS",
            "UNITY_MCP_AUTOSTART", 
            "UNITY_MCP_PORT",
            "LOG_LEVEL",
            "HOME"
        ]
        
        for var in required_vars:
            assert var in env_vars, f"Required environment variable {var} not set"
        
        # Check specific values
        assert env_vars.get("UNITY_HEADLESS") == "true"
        assert env_vars.get("UNITY_MCP_AUTOSTART") == "true"
        assert env_vars.get("HOME") == "/home/unity"
    
    def test_labels_and_metadata(self):
        """Test that image has proper labels and metadata"""
        inspect_result = subprocess.run([
            "docker", "image", "inspect", self.test_image_tag
        ], capture_output=True, text=True)
        
        assert inspect_result.returncode == 0
        image_info = json.loads(inspect_result.stdout)[0]
        config = image_info.get("Config", {})
        labels = config.get("Labels", {})
        
        # Check required labels
        required_labels = [
            "maintainer",
            "version", 
            "description"
        ]
        
        for label in required_labels:
            assert label in labels, f"Required label {label} not found"
        
        # Check OpenContainer labels if present
        if "org.opencontainers.image.source" in labels:
            source = labels["org.opencontainers.image.source"]
            assert "github.com" in source.lower(), "Source should point to GitHub repository"


if __name__ == "__main__":
    # Run tests with verbose output
    pytest.main([__file__, "-v", "--tb=short"])