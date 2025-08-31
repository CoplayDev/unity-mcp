#!/usr/bin/env python3
"""
Container Size Tests for Unity MCP
Tests that Docker image meets size requirements (<2GB)
"""

import subprocess
import json
import sys
from pathlib import Path

class TestContainerSize:
    """Test container size requirements"""
    
    @classmethod
    def setup_class(cls):
        """Setup test environment"""
        cls.project_root = Path(__file__).parent.parent.parent.absolute()
        cls.test_image_name = "unity-mcp:latest"
        
    def test_final_image_under_2gb(self):
        """Test that final production image is under 2GB"""
        # Get image size
        inspect_result = subprocess.run([
            "docker", "image", "inspect", self.test_image_name
        ], capture_output=True, text=True)
        
        assert inspect_result.returncode == 0, f"Failed to inspect image {self.test_image_name}"
        
        image_info = json.loads(inspect_result.stdout)[0]
        size_bytes = image_info.get("Size", 0)
        size_gb = size_bytes / (1024 ** 3)
        size_mb = size_bytes / (1024 ** 2)
        
        print(f"Image size: {size_gb:.2f} GB ({size_mb:.1f} MB)")
        
        # Check 2GB requirement
        assert size_gb < 2.0, f"Image size ({size_gb:.2f}GB) exceeds 2GB limit"
        
        # Additional checks for optimization
        if size_gb > 1.5:
            print(f"⚠️  Warning: Image size ({size_gb:.2f}GB) is close to 2GB limit")
        elif size_gb < 1.0:
            print(f"✅ Excellent: Image size ({size_gb:.2f}GB) is well under limit")
        else:
            print(f"✅ Good: Image size ({size_gb:.2f}GB) is under 2GB limit")
    
    def test_layer_optimization(self):
        """Test that image layers are reasonably optimized"""
        inspect_result = subprocess.run([
            "docker", "image", "inspect", self.test_image_name
        ], capture_output=True, text=True)
        
        assert inspect_result.returncode == 0
        image_info = json.loads(inspect_result.stdout)[0]
        
        # Check layer count
        layers = image_info.get("RootFS", {}).get("Layers", [])
        layer_count = len(layers)
        
        print(f"Image layers: {layer_count}")
        
        # Reasonable layer count for multi-stage build
        assert layer_count < 30, f"Too many layers: {layer_count} (may indicate inefficient build)"
        assert layer_count > 5, f"Too few layers: {layer_count} (may indicate missing components)"
        
        if layer_count > 20:
            print(f"⚠️  Warning: High layer count ({layer_count}) - consider layer optimization")
        else:
            print(f"✅ Layer count ({layer_count}) is reasonable")
    
    def test_image_history_efficiency(self):
        """Test image build history for large layers"""
        history_result = subprocess.run([
            "docker", "image", "history", "--human", "--no-trunc", self.test_image_name
        ], capture_output=True, text=True)
        
        assert history_result.returncode == 0
        
        # Parse history to find large layers
        lines = history_result.stdout.strip().split('\n')[1:]  # Skip header
        large_layers = []
        
        for line in lines:
            parts = line.split()
            if len(parts) >= 2:
                size_str = parts[1]
                if 'MB' in size_str:
                    try:
                        size_mb = float(size_str.replace('MB', ''))
                        if size_mb > 200:  # Flag layers over 200MB
                            large_layers.append((size_str, line))
                    except ValueError:
                        pass
                elif 'GB' in size_str:
                    try:
                        size_gb = float(size_str.replace('GB', ''))
                        if size_gb > 0.1:  # Flag layers over 100MB
                            large_layers.append((size_str, line))
                    except ValueError:
                        pass
        
        if large_layers:
            print("⚠️  Large layers found:")
            for size, line in large_layers[:5]:  # Show top 5
                print(f"   {size}: {line[:100]}...")
        else:
            print("✅ No unusually large layers detected")
        
        # This is a warning, not a failure
        # assert len(large_layers) < 5, f"Too many large layers: {len(large_layers)}"
    
    def test_size_comparison_with_base(self):
        """Test size comparison with base Ubuntu image"""
        # Get Ubuntu base image size
        ubuntu_result = subprocess.run([
            "docker", "image", "inspect", "ubuntu:22.04"
        ], capture_output=True, text=True)
        
        if ubuntu_result.returncode != 0:
            print("⚠️  Warning: Ubuntu base image not available for comparison")
            return
        
        ubuntu_info = json.loads(ubuntu_result.stdout)[0]
        ubuntu_size_gb = ubuntu_info.get("Size", 0) / (1024 ** 3)
        
        # Get our image size
        inspect_result = subprocess.run([
            "docker", "image", "inspect", self.test_image_name
        ], capture_output=True, text=True)
        
        assert inspect_result.returncode == 0
        image_info = json.loads(inspect_result.stdout)[0]
        our_size_gb = image_info.get("Size", 0) / (1024 ** 3)
        
        overhead_gb = our_size_gb - ubuntu_size_gb
        
        print(f"Base Ubuntu size: {ubuntu_size_gb:.2f} GB")
        print(f"Our image size: {our_size_gb:.2f} GB")
        print(f"Added overhead: {overhead_gb:.2f} GB")
        
        # Unity + Python should add significant size, but not too much
        assert overhead_gb > 0.1, f"Suspiciously small overhead: {overhead_gb:.2f}GB"
        assert overhead_gb < 1.8, f"Very large overhead: {overhead_gb:.2f}GB - consider optimization"
    
    def test_compressed_vs_uncompressed_size(self):
        """Test difference between compressed and uncompressed image sizes"""
        # Get detailed size information
        inspect_result = subprocess.run([
            "docker", "image", "inspect", self.test_image_name
        ], capture_output=True, text=True)
        
        assert inspect_result.returncode == 0
        image_info = json.loads(inspect_result.stdout)[0]
        
        size = image_info.get("Size", 0)
        virtual_size = image_info.get("VirtualSize", size)
        
        size_gb = size / (1024 ** 3)
        virtual_size_gb = virtual_size / (1024 ** 3)
        
        print(f"Actual size: {size_gb:.2f} GB")
        print(f"Virtual size: {virtual_size_gb:.2f} GB")
        
        # Both should be under 2GB, but virtual size might be slightly different
        assert size_gb < 2.0, f"Actual size ({size_gb:.2f}GB) exceeds 2GB limit"
        assert virtual_size_gb < 2.5, f"Virtual size ({virtual_size_gb:.2f}GB) is excessive"
    
    def test_size_breakdown_analysis(self):
        """Analyze what contributes most to image size"""
        # Use docker system df to get space usage
        df_result = subprocess.run([
            "docker", "system", "df", "-v"
        ], capture_output=True, text=True)
        
        if df_result.returncode == 0:
            print("Docker system space usage:")
            lines = df_result.stdout.split('\n')
            for line in lines:
                if self.test_image_name in line or "unity-mcp" in line:
                    print(f"  {line}")
        
        # Get image layers information
        history_result = subprocess.run([
            "docker", "image", "history", "--human", self.test_image_name
        ], capture_output=True, text=True)
        
        if history_result.returncode == 0:
            print("\nTop layers by size:")
            lines = history_result.stdout.strip().split('\n')[1:6]  # Top 5 layers
            for line in lines:
                if line.strip():
                    print(f"  {line[:100]}...")


def run_tests():
    """Run all size tests"""
    test_class = TestContainerSize()
    test_class.setup_class()
    
    tests = [
        test_class.test_final_image_under_2gb,
        test_class.test_layer_optimization,
        test_class.test_image_history_efficiency,
        test_class.test_size_comparison_with_base,
        test_class.test_compressed_vs_uncompressed_size,
        test_class.test_size_breakdown_analysis
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
    print(f"CONTAINER SIZE TESTS COMPLETED")
    print(f"Passed: {passed}")
    print(f"Failed: {failed}")
    print(f"Success Rate: {(passed/(passed+failed)*100):.1f}%" if (passed+failed) > 0 else "No tests run")
    
    return failed == 0


if __name__ == "__main__":
    success = run_tests()
    exit(0 if success else 1)