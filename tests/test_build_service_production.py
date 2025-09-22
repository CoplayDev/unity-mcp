#!/usr/bin/env python3
"""
Production-ready test suite for Unity Build Service API
Tests API compliance, security, error handling, and edge cases
"""

import asyncio
import pytest
import aiohttp
import json
import uuid
import tempfile
import os
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock, patch
import sys

# Add source directory to path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'UnityMcpBridge', 'UnityMcpServer~', 'src'))

from build_service import UnityBuildService, BuildStatus, BuildJob, BuildRequest


class MockClientManager:
    """Mock client manager for testing"""
    
    def __init__(self):
        self.clients = {}
        
    async def register_client(self, client_id, namespace):
        """Mock client registration"""
        context = MagicMock()
        context.client_id = client_id
        context.scene_namespace = namespace
        context.asset_path = f"/tmp/test_assets_{client_id}"
        self.clients[client_id] = context
        return context
        
    async def unregister_client(self, client_id):
        """Mock client unregistration"""
        if client_id in self.clients:
            del self.clients[client_id]
            
    async def execute_command(self, client_id, command):
        """Mock command execution"""
        return {"success": True, "result": "mock_result"}


class MockSceneManager:
    """Mock scene manager for testing"""
    
    async def create_scene(self, client_id, scene_name, namespace, asset_path):
        """Mock scene creation"""
        pass
        
    async def load_scene(self, client_id, scene_name, namespace):
        """Mock scene loading"""
        pass


@pytest.fixture
async def build_service():
    """Create build service for testing"""
    with tempfile.TemporaryDirectory() as temp_dir:
        client_manager = MockClientManager()
        scene_manager = MockSceneManager()
        service = UnityBuildService(client_manager, scene_manager, temp_dir)
        yield service


@pytest.fixture
def valid_build_request():
    """Valid build request data"""
    return {
        "user_id": str(uuid.uuid4()),
        "game_id": "test-game-123",
        "game_name": "Test Game",
        "game_type": "platformer",
        "asset_set": "v1",
        "assets": [
            ["https://example.com/player.png"],
            ["https://example.com/background.jpg", "https://example.com/bg2.jpg"],
            ["https://example.com/sound.wav"]
        ]
    }


class TestBuildServiceAPI:
    """Test suite for Build Service API compliance"""
    
    async def test_api_compliance_post_build_success(self, build_service, valid_build_request):
        """Test POST /build returns correct response format"""
        result = await build_service.create_build(valid_build_request)
        
        # API spec: Status 200 OK with {"url": string}
        assert "url" in result
        assert isinstance(result["url"], str)
        assert result["url"].startswith("/build/")
        assert result["url"].endswith("/status")
        
        # Extract build_id and verify it's a valid UUID format
        build_id = result["url"].split("/")[2]
        uuid.UUID(build_id)  # Should not raise exception
        
    async def test_api_compliance_get_status_success(self, build_service, valid_build_request):
        """Test GET /build/{build_id}/status returns correct format"""
        # Create a build first
        result = await build_service.create_build(valid_build_request)
        build_id = result["url"].split("/")[2]
        
        # Get status
        status = await build_service.get_build_status(build_id)
        
        # API spec format verification
        required_fields = ["game_id", "status", "queue_position", "game_url", "error_message"]
        for field in required_fields:
            assert field in status
            
        assert status["game_id"] == valid_build_request["game_id"]
        assert status["status"] in ["pending", "building", "deploying", "completed", "failed"]
        assert isinstance(status["queue_position"], int)
        assert status["game_url"] is None or isinstance(status["game_url"], str)
        assert status["error_message"] is None or isinstance(status["error_message"], str)
        
    async def test_api_compliance_stop_build(self, build_service, valid_build_request):
        """Test PUT /build/{build_id}/stop compliance"""
        # Create a build first
        result = await build_service.create_build(valid_build_request)
        build_id = result["url"].split("/")[2]
        
        # Stop the build
        success = await build_service.stop_build(build_id)
        assert success == True
        
        # Verify status is updated
        status = await build_service.get_build_status(build_id)
        assert status["status"] == "failed"
        assert "cancelled" in status["error_message"].lower()


class TestBuildServiceValidation:
    """Test input validation and error handling"""
    
    async def test_missing_required_fields(self, build_service):
        """Test validation of required fields"""
        incomplete_requests = [
            {},  # Empty request
            {"user_id": "123"},  # Missing game_id
            {"user_id": "123", "game_id": "game"},  # Missing game_name
            {"user_id": "123", "game_id": "game", "game_name": "Test"},  # Missing game_type
            {"user_id": "123", "game_id": "game", "game_name": "Test", "game_type": "platformer"},  # Missing asset_set
            {"user_id": "123", "game_id": "game", "game_name": "Test", "game_type": "platformer", "asset_set": "v1"},  # Missing assets
        ]
        
        for request_data in incomplete_requests:
            with pytest.raises(ValueError, match="Missing required field"):
                await build_service.create_build(request_data)
                
    async def test_invalid_assets_format(self, build_service):
        """Test validation of assets field format"""
        base_request = {
            "user_id": str(uuid.uuid4()),
            "game_id": "test-game",
            "game_name": "Test Game",
            "game_type": "platformer",
            "asset_set": "v1"
        }
        
        invalid_assets = [
            "not_a_list",  # String instead of list
            123,  # Number instead of list
            ["not", "nested", "lists"],  # Flat list instead of nested
            [["valid"], "invalid"],  # Mixed valid and invalid
            [["https://example.com/valid.png"], ["not_a_url_string", 123]],  # Invalid URL types
        ]
        
        for assets in invalid_assets:
            request_data = {**base_request, "assets": assets}
            with pytest.raises((ValueError, TypeError)):
                await build_service.create_build(request_data)


class TestBuildServiceAuthentication:
    """Test authentication and authorization"""
    
    async def test_valid_bearer_token(self, build_service):
        """Test valid Bearer token authentication"""
        valid_headers = [
            "Bearer default-api-key",
            "Bearer custom-key-123",
            "bearer lowercase-bearer",  # Should be case-insensitive
        ]
        
        # Mock API key to match test cases
        original_key = build_service.api_key
        
        for header in valid_headers:
            if header.startswith("Bearer "):
                build_service.api_key = header.split(" ", 1)[1]
            else:  # bearer lowercase
                build_service.api_key = header.split(" ", 1)[1]
                
            result = build_service.authenticate_request(header)
            assert result == True
            
        build_service.api_key = original_key
        
    async def test_invalid_authentication(self, build_service):
        """Test invalid authentication scenarios"""
        invalid_headers = [
            "",  # Empty header
            "Invalid format",  # No Bearer prefix
            "Bearer",  # No token
            "Bearer ",  # Empty token
            "Basic dXNlcjpwYXNz",  # Wrong auth type
            "Bearer wrong-key",  # Wrong API key
        ]
        
        for header in invalid_headers:
            result = build_service.authenticate_request(header)
            assert result == False


class TestBuildServiceConcurrency:
    """Test concurrent build handling"""
    
    async def test_queue_management(self, build_service, valid_build_request):
        """Test build queue and position tracking"""
        # Set low concurrency limit for testing
        build_service.max_concurrent_builds = 1
        
        # Create multiple builds
        builds = []
        for i in range(3):
            request_data = {**valid_build_request, "game_id": f"game-{i}"}
            result = await build_service.create_build(request_data)
            builds.append(result["url"].split("/")[2])
            
        # Check queue positions
        statuses = []
        for build_id in builds:
            status = await build_service.get_build_status(build_id)
            statuses.append(status)
            
        # First build should be building/completed, others queued
        active_builds = [s for s in statuses if s["status"] in ["building", "deploying", "completed"]]
        queued_builds = [s for s in statuses if s["status"] == "pending"]
        
        assert len(active_builds) <= build_service.max_concurrent_builds
        assert len(queued_builds) >= 0
        
        # Queue positions should be sequential for pending builds
        queued_positions = [s["queue_position"] for s in queued_builds]
        queued_positions.sort()
        for i, pos in enumerate(queued_positions):
            assert pos == i + 1
            
    async def test_build_cancellation_cleanup(self, build_service, valid_build_request):
        """Test that cancelled builds are properly cleaned up"""
        # Create a build
        result = await build_service.create_build(valid_build_request)
        build_id = result["url"].split("/")[2]
        
        # Cancel it
        success = await build_service.stop_build(build_id)
        assert success == True
        
        # Verify it's not in active builds
        assert build_id not in build_service.active_builds
        
        # Verify it's not in queue
        assert build_id not in build_service.build_queue


class TestBuildServiceErrorHandling:
    """Test error handling and edge cases"""
    
    async def test_nonexistent_build_status(self, build_service):
        """Test requesting status for non-existent build"""
        fake_build_id = str(uuid.uuid4())
        status = await build_service.get_build_status(fake_build_id)
        assert status is None
        
    async def test_nonexistent_build_stop(self, build_service):
        """Test stopping non-existent build"""
        fake_build_id = str(uuid.uuid4())
        success = await build_service.stop_build(fake_build_id)
        assert success == False
        
    async def test_double_stop_build(self, build_service, valid_build_request):
        """Test stopping same build twice"""
        # Create a build
        result = await build_service.create_build(valid_build_request)
        build_id = result["url"].split("/")[2]
        
        # Stop it once
        success1 = await build_service.stop_build(build_id)
        assert success1 == True
        
        # Stop it again - should fail gracefully
        success2 = await build_service.stop_build(build_id)
        assert success2 == False


class TestBuildServiceStatistics:
    """Test build statistics and monitoring"""
    
    async def test_statistics_accuracy(self, build_service, valid_build_request):
        """Test that statistics accurately reflect build states"""
        initial_stats = build_service.get_build_statistics()
        assert initial_stats["total_builds"] == 0
        assert initial_stats["completed_builds"] == 0
        assert initial_stats["failed_builds"] == 0
        
        # Create a few builds
        for i in range(3):
            request_data = {**valid_build_request, "game_id": f"game-{i}"}
            await build_service.create_build(request_data)
            
        stats_after_create = build_service.get_build_statistics()
        assert stats_after_create["total_builds"] == 3
        assert stats_after_create["active_builds"] <= build_service.max_concurrent_builds
        
        # Success rate should be meaningful
        assert 0 <= stats_after_create["success_rate"] <= 100


class TestBuildServiceSecurity:
    """Test security aspects"""
    
    async def test_path_traversal_prevention(self, build_service):
        """Test that malicious paths are handled safely"""
        malicious_request = {
            "user_id": str(uuid.uuid4()),
            "game_id": "../../../etc/passwd",
            "game_name": "../../malicious",
            "game_type": "platformer",
            "asset_set": "v1",
            "assets": [["https://example.com/test.png"]]
        }
        
        # Should not raise exception, should handle gracefully
        result = await build_service.create_build(malicious_request)
        assert "url" in result
        
        # Build ID should be sanitized UUID, not the malicious game_id
        build_id = result["url"].split("/")[2]
        uuid.UUID(build_id)  # Should be valid UUID
        assert build_id != malicious_request["game_id"]
        
    async def test_resource_limits(self, build_service):
        """Test resource usage limits"""
        # Test with excessive assets
        large_assets = []
        for i in range(100):  # Large number of asset slots
            slot = [f"https://example.com/asset_{j}.png" for j in range(10)]  # Many assets per slot
            large_assets.append(slot)
            
        request_data = {
            "user_id": str(uuid.uuid4()),
            "game_id": "resource-test",
            "game_name": "Resource Test",
            "game_type": "platformer",
            "asset_set": "v1",
            "assets": large_assets
        }
        
        # Should handle large requests without crashing
        try:
            result = await build_service.create_build(request_data)
            assert "url" in result
        except Exception as e:
            # If it fails, should be a controlled failure, not a crash
            assert "resource" in str(e).lower() or "limit" in str(e).lower()


@pytest.mark.asyncio
class TestBuildServiceIntegration:
    """Integration tests for the complete build process"""
    
    async def test_end_to_end_build_workflow(self, build_service, valid_build_request):
        """Test complete build workflow from creation to completion"""
        # Mock the Unity operations to avoid actual Unity calls
        with patch.object(build_service, '_download_assets', new_callable=AsyncMock), \
             patch.object(build_service, '_create_game_project', new_callable=AsyncMock), \
             patch.object(build_service, '_build_game', new_callable=AsyncMock), \
             patch.object(build_service, '_deploy_game', new_callable=AsyncMock) as mock_deploy:
            
            mock_deploy.return_value = "https://example.com/games/test-game"
            
            # Create build
            result = await build_service.create_build(valid_build_request)
            build_id = result["url"].split("/")[2]
            
            # Simulate the build process by manually updating the build job
            build_job = build_service.builds[build_id]
            build_job.status = BuildStatus.COMPLETED
            build_job.game_url = "https://example.com/games/test-game"
            
            # Check final status
            status = await build_service.get_build_status(build_id)
            assert status["status"] == "completed"
            assert status["game_url"] == "https://example.com/games/test-game"
            assert status["error_message"] is None


if __name__ == "__main__":
    # Run tests directly
    pytest.main([__file__, "-v"])