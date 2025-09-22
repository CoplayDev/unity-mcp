#!/usr/bin/env python3
"""
Security utilities for Unity Build Service
Provides input validation, sanitization, and security controls
"""

import re
import os
import hashlib
import urllib.parse
from pathlib import Path
from typing import List, Dict, Any, Optional
import logging

logger = logging.getLogger(__name__)

class SecurityError(Exception):
    """Security-related error"""
    pass

class InputValidator:
    """Validates and sanitizes user inputs for security"""
    
    # URL validation patterns
    ALLOWED_URL_SCHEMES = {'http', 'https'}
    ALLOWED_DOMAINS_PATTERN = r'^[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$'
    
    # File extension whitelist
    ALLOWED_EXTENSIONS = {
        'image': {'.png', '.jpg', '.jpeg', '.gif', '.bmp', '.tga'},
        'audio': {'.wav', '.mp3', '.ogg', '.aac'},
        'model': {'.fbx', '.obj', '.dae', '.3ds'},
        'texture': {'.png', '.jpg', '.jpeg', '.tga', '.exr', '.hdr'}
    }
    
    # Resource limits
    MAX_ASSETS_PER_BUILD = int(os.getenv('MAX_ASSETS_PER_BUILD', '100'))
    MAX_ASSETS_PER_SLOT = int(os.getenv('MAX_ASSETS_PER_SLOT', '20'))
    MAX_ASSET_SIZE_MB = int(os.getenv('MAX_ASSET_SIZE_MB', '100'))
    MAX_GAME_NAME_LENGTH = 100
    MAX_USER_ID_LENGTH = 100
    
    @classmethod
    def validate_build_request(cls, request_data: Dict[str, Any]) -> Dict[str, Any]:
        """Validate and sanitize build request data"""
        # Check required fields
        required_fields = ['user_id', 'game_id', 'game_name', 'game_type', 'asset_set', 'assets']
        for field in required_fields:
            if field not in request_data:
                raise SecurityError(f"Missing required field: {field}")
        
        # Sanitize string fields
        sanitized = {}
        sanitized['user_id'] = cls._sanitize_id(request_data['user_id'], cls.MAX_USER_ID_LENGTH)
        sanitized['game_id'] = cls._sanitize_id(request_data['game_id'], cls.MAX_USER_ID_LENGTH)
        sanitized['game_name'] = cls._sanitize_string(request_data['game_name'], cls.MAX_GAME_NAME_LENGTH)
        sanitized['game_type'] = cls._sanitize_game_type(request_data['game_type'])
        sanitized['asset_set'] = cls._sanitize_string(request_data['asset_set'], 50)
        
        # Validate and sanitize assets
        sanitized['assets'] = cls._validate_assets(request_data['assets'])
        
        return sanitized
    
    @classmethod
    def _sanitize_id(cls, value: str, max_length: int) -> str:
        """Sanitize ID fields (user_id, game_id)"""
        if not isinstance(value, str):
            raise SecurityError("ID must be a string")
            
        if len(value) > max_length:
            raise SecurityError(f"ID too long (max {max_length} characters)")
            
        # Allow only alphanumeric, hyphens, and underscores
        if not re.match(r'^[a-zA-Z0-9_-]+$', value):
            raise SecurityError("ID contains invalid characters")
            
        return value
    
    @classmethod
    def _sanitize_string(cls, value: str, max_length: int) -> str:
        """Sanitize general string fields"""
        if not isinstance(value, str):
            raise SecurityError("Value must be a string")
            
        if len(value) > max_length:
            raise SecurityError(f"String too long (max {max_length} characters)")
            
        # Remove potentially dangerous characters
        sanitized = re.sub(r'[<>"\'\\\x00-\x1f\x7f-\x9f]', '', value)
        sanitized = sanitized.strip()
        
        if not sanitized:
            raise SecurityError("String cannot be empty after sanitization")
            
        return sanitized
    
    @classmethod
    def _sanitize_game_type(cls, value: str) -> str:
        """Sanitize game type field"""
        allowed_types = {
            'platformer', 'shooter', 'puzzle', 'runner', 'rpg', 'custom',
            'action', 'adventure', 'strategy', 'simulation', 'sports'
        }
        
        value = cls._sanitize_string(value, 50).lower()
        
        if value not in allowed_types:
            logger.warning(f"Unknown game type: {value}, using 'custom'")
            return 'custom'
            
        return value
    
    @classmethod
    def _validate_assets(cls, assets: List[List[str]]) -> List[List[str]]:
        """Validate and sanitize assets array"""
        if not isinstance(assets, list):
            raise SecurityError("Assets must be a list")
            
        if len(assets) > cls.MAX_ASSETS_PER_BUILD:
            raise SecurityError(f"Too many asset slots (max {cls.MAX_ASSETS_PER_BUILD})")
            
        sanitized_assets = []
        for slot_index, slot in enumerate(assets):
            if not isinstance(slot, list):
                raise SecurityError(f"Asset slot {slot_index} must be a list")
                
            if len(slot) > cls.MAX_ASSETS_PER_SLOT:
                raise SecurityError(f"Slot {slot_index} has too many assets (max {cls.MAX_ASSETS_PER_SLOT})")
                
            sanitized_slot = []
            for asset_index, asset_url in enumerate(slot):
                sanitized_url = cls._validate_asset_url(asset_url, slot_index, asset_index)
                sanitized_slot.append(sanitized_url)
                
            sanitized_assets.append(sanitized_slot)
            
        return sanitized_assets
    
    @classmethod
    def _validate_asset_url(cls, url: str, slot_index: int, asset_index: int) -> str:
        """Validate and sanitize asset URL"""
        if not isinstance(url, str):
            raise SecurityError(f"Asset URL at slot {slot_index}, asset {asset_index} must be a string")
            
        if len(url) > 2048:  # Reasonable URL length limit
            raise SecurityError(f"Asset URL too long at slot {slot_index}, asset {asset_index}")
            
        try:
            parsed = urllib.parse.urlparse(url)
        except Exception:
            raise SecurityError(f"Invalid URL format at slot {slot_index}, asset {asset_index}")
            
        # Validate scheme
        if parsed.scheme.lower() not in cls.ALLOWED_URL_SCHEMES:
            raise SecurityError(f"Invalid URL scheme at slot {slot_index}, asset {asset_index} (only HTTP/HTTPS allowed)")
            
        # Validate domain
        if not parsed.netloc:
            raise SecurityError(f"Missing domain in URL at slot {slot_index}, asset {asset_index}")
            
        # Check for localhost/private IP addresses (security risk)
        if cls._is_private_address(parsed.netloc):
            raise SecurityError(f"Private/local addresses not allowed at slot {slot_index}, asset {asset_index}")
            
        # Validate file extension if present
        path_lower = parsed.path.lower()
        if '.' in path_lower:
            ext = Path(parsed.path).suffix.lower()
            if ext and not cls._is_allowed_extension(ext):
                logger.warning(f"Unknown file extension {ext} at slot {slot_index}, asset {asset_index}")
                
        return url
    
    @classmethod
    def _is_private_address(cls, netloc: str) -> bool:
        """Check if address is private/localhost"""
        # Extract hostname (remove port if present)
        hostname = netloc.split(':')[0].lower()
        
        # Check for localhost variants
        localhost_patterns = {
            'localhost', '127.0.0.1', '::1', '0.0.0.0',
            '10.', '172.16.', '172.17.', '172.18.', '172.19.',
            '172.20.', '172.21.', '172.22.', '172.23.',
            '172.24.', '172.25.', '172.26.', '172.27.',
            '172.28.', '172.29.', '172.30.', '172.31.',
            '192.168.'
        }
        
        for pattern in localhost_patterns:
            if hostname.startswith(pattern):
                return True
                
        return False
    
    @classmethod
    def _is_allowed_extension(cls, ext: str) -> bool:
        """Check if file extension is allowed"""
        for category, extensions in cls.ALLOWED_EXTENSIONS.items():
            if ext in extensions:
                return True
        return False


class RateLimiter:
    """Simple rate limiter for API endpoints"""
    
    def __init__(self, max_requests: int = 10, window_seconds: int = 60):
        self.max_requests = max_requests
        self.window_seconds = window_seconds
        self.requests = {}  # {api_key: [(timestamp, ...)]}
        
    def is_allowed(self, api_key: str) -> bool:
        """Check if request is allowed for this API key"""
        import time
        current_time = time.time()
        
        # Clean old requests
        if api_key in self.requests:
            self.requests[api_key] = [
                req_time for req_time in self.requests[api_key]
                if current_time - req_time < self.window_seconds
            ]
        else:
            self.requests[api_key] = []
            
        # Check limit
        if len(self.requests[api_key]) >= self.max_requests:
            return False
            
        # Record this request
        self.requests[api_key].append(current_time)
        return True


class SecureFileHandler:
    """Secure file handling utilities"""
    
    @staticmethod
    def create_secure_path(base_dir: Path, *path_components: str) -> Path:
        """Create a secure path that prevents directory traversal"""
        # Sanitize each component
        safe_components = []
        for component in path_components:
            # Remove dangerous characters and path traversal attempts
            safe_component = re.sub(r'[<>:"|?*\x00-\x1f]', '', str(component))
            safe_component = safe_component.replace('..', '').replace('/', '').replace('\\', '')
            if safe_component:
                safe_components.append(safe_component)
                
        # Build path
        secure_path = base_dir
        for component in safe_components:
            secure_path = secure_path / component
            
        # Ensure the path is within base_dir
        try:
            secure_path.resolve().relative_to(base_dir.resolve())
        except ValueError:
            raise SecurityError("Path traversal attempt detected")
            
        return secure_path
    
    @staticmethod
    def validate_file_size(file_path: Path, max_size_mb: int = 100) -> bool:
        """Validate file size"""
        if file_path.exists():
            size_mb = file_path.stat().st_size / (1024 * 1024)
            return size_mb <= max_size_mb
        return True
    
    @staticmethod
    def compute_file_hash(file_path: Path) -> str:
        """Compute SHA256 hash of file"""
        hash_sha256 = hashlib.sha256()
        with open(file_path, "rb") as f:
            for chunk in iter(lambda: f.read(4096), b""):
                hash_sha256.update(chunk)
        return hash_sha256.hexdigest()


class SecurityAuditLogger:
    """Security audit logging"""
    
    def __init__(self):
        self.logger = logging.getLogger('security_audit')
        
    def log_build_request(self, request_data: Dict[str, Any], client_ip: str = "unknown"):
        """Log build request for security audit"""
        self.logger.info(f"Build request from {client_ip}: user_id={request_data.get('user_id')}, "
                        f"game_id={request_data.get('game_id')}, "
                        f"asset_count={sum(len(slot) for slot in request_data.get('assets', []))}")
    
    def log_security_violation(self, violation: str, details: str, client_ip: str = "unknown"):
        """Log security violation"""
        self.logger.warning(f"Security violation from {client_ip}: {violation} - {details}")
    
    def log_authentication_failure(self, client_ip: str = "unknown"):
        """Log authentication failure"""
        self.logger.warning(f"Authentication failure from {client_ip}")