#!/usr/bin/env python3
"""
Test script for Unity Build Service API
Tests the complete build service workflow
"""

import asyncio
import aiohttp
import json
import time
import sys
from typing import Dict, Any

class BuildServiceTester:
    """Test client for Unity Build Service API"""
    
    def __init__(self, base_url: str = "http://localhost:8080", api_key: str = "default-api-key"):
        self.base_url = base_url
        self.api_key = api_key
        self.headers = {
            "Authorization": f"Bearer {api_key}",
            "Content-Type": "application/json"
        }
        
    async def test_health_check(self) -> bool:
        """Test server health"""
        try:
            async with aiohttp.ClientSession() as session:
                async with session.get(f"{self.base_url}/health") as response:
                    if response.status == 200:
                        print("✅ Server health check passed")
                        return True
                    else:
                        print(f"❌ Server health check failed: {response.status}")
                        return False
        except Exception as e:
            print(f"❌ Server connection failed: {e}")
            return False
            
    async def create_test_build(self) -> str:
        """Create a test build and return build ID"""
        test_build_data = {
            "user_id": "test-user-123",
            "game_id": "test-game-456",
            "game_name": "Test Platformer Game",
            "game_type": "platformer",
            "asset_set": "test_assets_v1",
            "assets": [
                # Player sprites (slot 0)
                [
                    "https://via.placeholder.com/64x64.png?text=Player1",
                    "https://via.placeholder.com/64x64.png?text=Player2"
                ],
                # Backgrounds (slot 1)
                [
                    "https://via.placeholder.com/800x600.jpg?text=Background1",
                    "https://via.placeholder.com/800x600.jpg?text=Background2"
                ],
                # Sound effects (slot 2)
                [
                    "https://www.soundjay.com/misc/sounds/beep-07a.wav"
                ]
            ]
        }
        
        try:
            async with aiohttp.ClientSession() as session:
                async with session.post(
                    f"{self.base_url}/build",
                    headers=self.headers,
                    json=test_build_data
                ) as response:
                    
                    if response.status == 200:
                        result = await response.json()
                        status_url = result["url"]
                        build_id = status_url.split("/")[-2]  # Extract build ID from URL
                        print(f"✅ Build created successfully")
                        print(f"   Build ID: {build_id}")
                        print(f"   Status URL: {status_url}")
                        return build_id
                    elif response.status == 403:
                        print("❌ Build creation failed: Unauthorized (check API key)")
                        return None
                    else:
                        error = await response.json()
                        print(f"❌ Build creation failed: {response.status} - {error}")
                        return None
                        
        except Exception as e:
            print(f"❌ Build creation error: {e}")
            return None
            
    async def monitor_build_status(self, build_id: str, timeout: int = 600) -> Dict[str, Any]:
        """Monitor build status until completion or timeout"""
        start_time = time.time()
        last_status = None
        
        print(f"🔍 Monitoring build {build_id}...")
        
        async with aiohttp.ClientSession() as session:
            while time.time() - start_time < timeout:
                try:
                    async with session.get(
                        f"{self.base_url}/build/{build_id}/status",
                        headers=self.headers
                    ) as response:
                        
                        if response.status == 200:
                            status = await response.json()
                            
                            # Only print status changes
                            if status["status"] != last_status:
                                print(f"   Status: {status['status']}")
                                if status["queue_position"] > 0:
                                    print(f"   Queue position: {status['queue_position']}")
                                last_status = status["status"]
                                
                            # Check if build is complete
                            if status["status"] in ["completed", "failed"]:
                                if status["status"] == "completed":
                                    print(f"✅ Build completed successfully!")
                                    print(f"   Game URL: {status['game_url']}")
                                else:
                                    print(f"❌ Build failed: {status['error_message']}")
                                return status
                                
                        elif response.status == 404:
                            print(f"❌ Build not found: {build_id}")
                            return None
                        else:
                            print(f"❌ Status check failed: {response.status}")
                            
                except Exception as e:
                    print(f"❌ Status check error: {e}")
                    
                # Wait before next check
                await asyncio.sleep(5)
                
        print(f"⏰ Build monitoring timed out after {timeout} seconds")
        return None
        
    async def test_stop_build(self, build_id: str) -> bool:
        """Test stopping a build"""
        try:
            async with aiohttp.ClientSession() as session:
                async with session.put(
                    f"{self.base_url}/build/{build_id}/stop",
                    headers=self.headers
                ) as response:
                    
                    if response.status == 200:
                        result = await response.json()
                        print(f"✅ Build stopped: {result['status']}")
                        return True
                    elif response.status == 404:
                        print(f"❌ Build not found: {build_id}")
                        return False
                    else:
                        error = await response.json()
                        print(f"❌ Stop build failed: {response.status} - {error}")
                        return False
                        
        except Exception as e:
            print(f"❌ Stop build error: {e}")
            return False
            
    async def test_build_stats(self) -> Dict[str, Any]:
        """Test getting build statistics"""
        try:
            async with aiohttp.ClientSession() as session:
                async with session.get(
                    f"{self.base_url}/api/admin/build-stats"
                ) as response:
                    
                    if response.status == 200:
                        stats = await response.json()
                        print("✅ Build statistics retrieved:")
                        print(f"   Total builds: {stats['total_builds']}")
                        print(f"   Completed: {stats['completed_builds']}")
                        print(f"   Failed: {stats['failed_builds']}")
                        print(f"   Active: {stats['active_builds']}")
                        print(f"   Queued: {stats['queued_builds']}")
                        print(f"   Success rate: {stats['success_rate']:.1f}%")
                        return stats
                    else:
                        print(f"❌ Stats retrieval failed: {response.status}")
                        return None
                        
        except Exception as e:
            print(f"❌ Stats retrieval error: {e}")
            return None
            
    async def test_authentication(self) -> bool:
        """Test API authentication"""
        print("🔐 Testing authentication...")
        
        # Test with invalid API key
        invalid_headers = {
            "Authorization": "Bearer invalid-key",
            "Content-Type": "application/json"
        }
        
        test_data = {
            "user_id": "test",
            "game_id": "test",
            "game_name": "test",
            "game_type": "test",
            "asset_set": "test",
            "assets": []
        }
        
        try:
            async with aiohttp.ClientSession() as session:
                async with session.post(
                    f"{self.base_url}/build",
                    headers=invalid_headers,
                    json=test_data
                ) as response:
                    
                    if response.status == 403:
                        print("✅ Authentication properly rejects invalid API key")
                        return True
                    else:
                        print(f"❌ Authentication failed to reject invalid key: {response.status}")
                        return False
                        
        except Exception as e:
            print(f"❌ Authentication test error: {e}")
            return False
            
    async def run_full_test_suite(self):
        """Run the complete test suite"""
        print("🚀 Starting Unity Build Service API Test Suite")
        print("=" * 50)
        
        # Test 1: Health check
        print("\n1. Testing server health...")
        if not await self.test_health_check():
            print("❌ Server is not healthy. Aborting tests.")
            return False
            
        # Test 2: Authentication
        print("\n2. Testing authentication...")
        if not await self.test_authentication():
            print("❌ Authentication test failed")
            
        # Test 3: Build statistics (before builds)
        print("\n3. Getting initial build statistics...")
        await self.test_build_stats()
        
        # Test 4: Create build
        print("\n4. Creating test build...")
        build_id = await self.create_test_build()
        if not build_id:
            print("❌ Could not create test build. Aborting remaining tests.")
            return False
            
        # Test 5: Monitor build (short timeout for testing)
        print("\n5. Monitoring build progress...")
        final_status = await self.monitor_build_status(build_id, timeout=120)
        
        # Test 6: Build statistics (after build)
        print("\n6. Getting updated build statistics...")
        await self.test_build_stats()
        
        # Test 7: Stop build (if still running)
        if final_status and final_status["status"] in ["pending", "building", "deploying"]:
            print("\n7. Testing build stop functionality...")
            await self.test_stop_build(build_id)
        
        print("\n" + "=" * 50)
        print("🎉 Test suite completed!")
        
        return True
        
    async def run_concurrent_builds_test(self, num_builds: int = 3):
        """Test concurrent build processing"""
        print(f"\n🔄 Testing {num_builds} concurrent builds...")
        
        build_tasks = []
        for i in range(num_builds):
            # Create slightly different build data
            build_data = {
                "user_id": f"concurrent-user-{i}",
                "game_id": f"concurrent-game-{i}",
                "game_name": f"Concurrent Test Game {i}",
                "game_type": "platformer",
                "asset_set": "concurrent_test",
                "assets": [
                    [f"https://via.placeholder.com/64x64.png?text=Player{i}"],
                    [f"https://via.placeholder.com/800x600.jpg?text=BG{i}"]
                ]
            }
            
            task = asyncio.create_task(self._create_and_monitor_build(build_data, i))
            build_tasks.append(task)
            
        # Wait for all builds to complete
        results = await asyncio.gather(*build_tasks, return_exceptions=True)
        
        successful_builds = sum(1 for result in results if result and not isinstance(result, Exception))
        print(f"✅ Concurrent builds completed: {successful_builds}/{num_builds}")
        
    async def _create_and_monitor_build(self, build_data: Dict, build_num: int):
        """Helper to create and monitor a single build"""
        try:
            async with aiohttp.ClientSession() as session:
                # Create build
                async with session.post(
                    f"{self.base_url}/build",
                    headers=self.headers,
                    json=build_data
                ) as response:
                    
                    if response.status != 200:
                        print(f"❌ Concurrent build {build_num} creation failed")
                        return False
                        
                    result = await response.json()
                    build_id = result["url"].split("/")[-2]
                    print(f"✅ Concurrent build {build_num} created: {build_id}")
                    
                    # Monitor briefly
                    timeout = 60  # Short timeout for concurrent test
                    start_time = time.time()
                    
                    while time.time() - start_time < timeout:
                        async with session.get(
                            f"{self.base_url}/build/{build_id}/status",
                            headers=self.headers
                        ) as status_response:
                            
                            if status_response.status == 200:
                                status = await status_response.json()
                                if status["status"] in ["completed", "failed"]:
                                    print(f"✅ Concurrent build {build_num} {status['status']}")
                                    return True
                                    
                        await asyncio.sleep(5)
                        
                    print(f"⏰ Concurrent build {build_num} timeout")
                    return False
                    
        except Exception as e:
            print(f"❌ Concurrent build {build_num} error: {e}")
            return False

async def main():
    """Main test function"""
    if len(sys.argv) > 1:
        base_url = sys.argv[1]
    else:
        base_url = "http://localhost:8080"
        
    if len(sys.argv) > 2:
        api_key = sys.argv[2]
    else:
        api_key = "default-api-key"
        
    print(f"Testing Unity Build Service at: {base_url}")
    print(f"Using API key: {api_key[:10]}...")
    
    tester = BuildServiceTester(base_url, api_key)
    
    # Run basic test suite
    await tester.run_full_test_suite()
    
    # Optional: Run concurrent builds test
    print("\n" + "=" * 50)
    response = input("Run concurrent builds test? (y/N): ")
    if response.lower() == 'y':
        await tester.run_concurrent_builds_test(3)

if __name__ == "__main__":
    asyncio.run(main())