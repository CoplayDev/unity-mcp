#!/usr/bin/env python3
"""
Production configuration and monitoring for Unity Build Service
Provides production-ready configuration, monitoring, and health checks
"""

import os
import logging
import logging.handlers
import json
from pathlib import Path
from typing import Dict, Any, Optional
from dataclasses import dataclass
from datetime import datetime, timedelta


@dataclass
class ProductionConfig:
    """Production configuration settings"""
    
    # API Configuration
    api_key: str = os.getenv('BUILD_SERVICE_API_KEY', 'CHANGE-THIS-IN-PRODUCTION')
    base_game_url: str = os.getenv('BASE_GAME_URL', 'https://localhost/games')
    max_concurrent_builds: int = int(os.getenv('MAX_CONCURRENT_BUILDS', '3'))
    
    # Resource Limits
    max_assets_per_build: int = int(os.getenv('MAX_ASSETS_PER_BUILD', '100'))
    max_assets_per_slot: int = int(os.getenv('MAX_ASSETS_PER_SLOT', '20'))
    max_asset_size_mb: int = int(os.getenv('MAX_ASSET_SIZE_MB', '100'))
    
    # Timeouts
    asset_download_timeout: int = int(os.getenv('ASSET_DOWNLOAD_TIMEOUT', '300'))
    build_timeout: int = int(os.getenv('BUILD_TIMEOUT', '1800'))  # 30 minutes
    
    # Directories
    base_build_dir: str = os.getenv('BUILD_BASE_DIR', '/opt/unity-mcp/builds')
    log_dir: str = os.getenv('LOG_DIR', '/opt/unity-mcp/logs')
    
    # Cleanup
    build_retention_hours: int = int(os.getenv('BUILD_RETENTION_HOURS', '24'))
    
    # Rate limiting
    rate_limit_requests: int = int(os.getenv('RATE_LIMIT_REQUESTS', '10'))
    rate_limit_window: int = int(os.getenv('RATE_LIMIT_WINDOW', '60'))
    
    # Security
    enable_security_audit: bool = os.getenv('ENABLE_SECURITY_AUDIT', 'true').lower() == 'true'
    allow_private_urls: bool = os.getenv('ALLOW_PRIVATE_URLS', 'false').lower() == 'true'
    
    def validate(self) -> None:
        """Validate configuration for production readiness"""
        issues = []
        
        # Check for default/insecure values
        if self.api_key == 'CHANGE-THIS-IN-PRODUCTION':
            issues.append("API key is set to default value - SECURITY RISK")
            
        if 'localhost' in self.base_game_url:
            issues.append("Game URL is set to localhost - not suitable for production")
            
        # Check directories exist
        for dir_path in [self.base_build_dir, self.log_dir]:
            if not Path(dir_path).exists():
                issues.append(f"Directory does not exist: {dir_path}")
                
        # Check resource limits are reasonable
        if self.max_concurrent_builds > 10:
            issues.append("Very high concurrent build limit - may cause resource issues")
            
        if self.max_asset_size_mb > 500:
            issues.append("Very high asset size limit - may cause storage issues")
            
        if issues:
            raise ValueError("Production configuration issues:\n" + "\n".join(f"- {issue}" for issue in issues))


class ProductionLogger:
    """Production-ready logging configuration"""
    
    @staticmethod
    def setup_logging(config: ProductionConfig) -> None:
        """Setup production logging"""
        # Create log directory
        log_dir = Path(config.log_dir)
        log_dir.mkdir(parents=True, exist_ok=True)
        
        # Main application logger
        app_logger = logging.getLogger('unity-mcp-build-service')
        app_logger.setLevel(logging.INFO)
        
        # File handler with rotation
        file_handler = logging.handlers.RotatingFileHandler(
            log_dir / 'build-service.log',
            maxBytes=50*1024*1024,  # 50MB
            backupCount=10
        )
        file_handler.setLevel(logging.INFO)
        
        # Console handler
        console_handler = logging.StreamHandler()
        console_handler.setLevel(logging.WARNING)
        
        # Formatter
        formatter = logging.Formatter(
            '%(asctime)s - %(name)s - %(levelname)s - %(message)s'
        )
        file_handler.setFormatter(formatter)
        console_handler.setFormatter(formatter)
        
        app_logger.addHandler(file_handler)
        app_logger.addHandler(console_handler)
        
        # Security audit logger (if enabled)
        if config.enable_security_audit:
            security_logger = logging.getLogger('security_audit')
            security_logger.setLevel(logging.INFO)
            
            security_handler = logging.handlers.RotatingFileHandler(
                log_dir / 'security-audit.log',
                maxBytes=10*1024*1024,  # 10MB
                backupCount=20
            )
            security_handler.setFormatter(formatter)
            security_logger.addHandler(security_handler)
        
        # Build process logger
        build_logger = logging.getLogger('build_process')
        build_logger.setLevel(logging.DEBUG)
        
        build_handler = logging.handlers.RotatingFileHandler(
            log_dir / 'build-process.log',
            maxBytes=20*1024*1024,  # 20MB
            backupCount=15
        )
        build_handler.setFormatter(formatter)
        build_logger.addHandler(build_handler)


class HealthChecker:
    """Health check system for monitoring"""
    
    def __init__(self, build_service):
        self.build_service = build_service
        self.start_time = datetime.now()
        
    def get_health_status(self) -> Dict[str, Any]:
        """Get comprehensive health status"""
        current_time = datetime.now()
        uptime = current_time - self.start_time
        
        # Basic stats
        stats = self.build_service.get_build_statistics()
        
        # Check system health
        health_issues = []
        
        # Check active builds vs capacity
        if stats['active_builds'] >= self.build_service.max_concurrent_builds:
            health_issues.append("At maximum build capacity")
            
        # Check queue length
        if stats['queued_builds'] > 20:
            health_issues.append(f"High queue length: {stats['queued_builds']}")
            
        # Check success rate
        if stats['total_builds'] > 10 and stats['success_rate'] < 70:
            health_issues.append(f"Low success rate: {stats['success_rate']:.1f}%")
            
        # Check disk space
        try:
            import shutil
            disk_usage = shutil.disk_usage(self.build_service.base_build_dir)
            free_gb = disk_usage.free / (1024**3)
            if free_gb < 5:  # Less than 5GB free
                health_issues.append(f"Low disk space: {free_gb:.1f}GB free")
        except Exception:
            health_issues.append("Could not check disk space")
            
        # Determine overall status
        if not health_issues:
            status = "healthy"
        elif len(health_issues) <= 2:
            status = "warning"
        else:
            status = "critical"
            
        return {
            "status": status,
            "uptime_seconds": int(uptime.total_seconds()),
            "uptime_human": str(uptime),
            "timestamp": current_time.isoformat(),
            "build_stats": stats,
            "issues": health_issues,
            "version": "1.0.0"  # Should be from actual version
        }
        
    def get_metrics(self) -> Dict[str, Any]:
        """Get detailed metrics for monitoring systems"""
        stats = self.build_service.get_build_statistics()
        
        # Calculate rates
        current_time = datetime.now()
        uptime_hours = (current_time - self.start_time).total_seconds() / 3600
        
        builds_per_hour = stats['total_builds'] / max(uptime_hours, 0.1)
        
        return {
            "timestamp": current_time.isoformat(),
            "uptime_seconds": int((current_time - self.start_time).total_seconds()),
            "builds_total": stats['total_builds'],
            "builds_completed": stats['completed_builds'],
            "builds_failed": stats['failed_builds'],
            "builds_active": stats['active_builds'],
            "builds_queued": stats['queued_builds'],
            "success_rate_percent": stats['success_rate'],
            "builds_per_hour": round(builds_per_hour, 2),
            "max_concurrent_builds": stats['max_concurrent_builds']
        }


class ProductionErrorHandler:
    """Enhanced error handling for production"""
    
    def __init__(self, config: ProductionConfig):
        self.config = config
        self.logger = logging.getLogger('unity-mcp-build-service')
        
    def handle_build_error(self, build_id: str, error: Exception, context: str = "") -> Dict[str, Any]:
        """Handle build errors with appropriate logging and response"""
        error_msg = str(error)
        error_type = type(error).__name__
        
        # Log error with context
        self.logger.error(f"Build {build_id} failed in {context}: {error_type}: {error_msg}")
        
        # Classify error for user-friendly message
        if "download" in error_msg.lower():
            user_message = "Failed to download one or more assets. Please check asset URLs."
        elif "timeout" in error_msg.lower():
            user_message = "Build timed out. Please try with fewer or smaller assets."
        elif "unity" in error_msg.lower():
            user_message = "Unity compilation failed. Please check asset compatibility."
        elif "storage" in error_msg.lower() or "disk" in error_msg.lower():
            user_message = "Insufficient storage space. Please try again later."
        else:
            user_message = "Build failed due to an internal error. Please try again."
            
        return {
            "error_message": user_message,
            "error_code": error_type,
            "timestamp": datetime.now().isoformat(),
            "build_id": build_id
        }
        
    def handle_api_error(self, error: Exception, endpoint: str, client_ip: str = "unknown") -> tuple[Dict[str, Any], int]:
        """Handle API errors with appropriate HTTP status and response"""
        error_msg = str(error)
        error_type = type(error).__name__
        
        # Log API error
        self.logger.error(f"API error at {endpoint} from {client_ip}: {error_type}: {error_msg}")
        
        # Determine appropriate HTTP status and response
        if "unauthorized" in error_msg.lower() or "authentication" in error_msg.lower():
            return {"error": "Unauthorized"}, 403
        elif "missing" in error_msg.lower() or "required" in error_msg.lower():
            return {"error": f"Bad request: {error_msg}"}, 400
        elif "not found" in error_msg.lower():
            return {"error": "Resource not found"}, 502  # Per API spec
        elif "timeout" in error_msg.lower():
            return {"error": "Request timeout"}, 502
        elif "rate limit" in error_msg.lower():
            return {"error": "Rate limit exceeded"}, 429
        else:
            return {"error": "Internal server error"}, 502
            
    def log_performance_warning(self, operation: str, duration_seconds: float, threshold: float = 30.0):
        """Log performance warnings for slow operations"""
        if duration_seconds > threshold:
            self.logger.warning(f"Slow operation: {operation} took {duration_seconds:.1f}s (threshold: {threshold}s)")


class MaintenanceManager:
    """System maintenance and cleanup"""
    
    def __init__(self, build_service, config: ProductionConfig):
        self.build_service = build_service
        self.config = config
        self.logger = logging.getLogger('maintenance')
        
    async def cleanup_old_builds(self) -> Dict[str, int]:
        """Clean up old builds and return cleanup statistics"""
        cleaned_builds = 0
        freed_bytes = 0
        errors = 0
        
        cutoff_time = datetime.now() - timedelta(hours=self.config.build_retention_hours)
        
        for build_id, build_job in list(self.build_service.builds.items()):
            if (build_job.completed_at and 
                build_job.completed_at < cutoff_time and
                build_job.status.value in ['completed', 'failed']):
                
                try:
                    # Calculate freed space before deletion
                    build_assets = Path(self.build_service.base_build_dir) / "assets" / build_id
                    build_output = Path(self.build_service.base_build_dir) / "games" / build_id
                    
                    # Calculate size
                    size_before = 0
                    for path in [build_assets, build_output]:
                        if path.exists():
                            for file_path in path.rglob('*'):
                                if file_path.is_file():
                                    size_before += file_path.stat().st_size
                    
                    # Remove files
                    import shutil
                    if build_assets.exists():
                        shutil.rmtree(build_assets)
                    if build_output.exists():
                        shutil.rmtree(build_output)
                        
                    # Remove from memory
                    del self.build_service.builds[build_id]
                    
                    cleaned_builds += 1
                    freed_bytes += size_before
                    
                    self.logger.info(f"Cleaned up build {build_id} (freed {size_before} bytes)")
                    
                except Exception as e:
                    errors += 1
                    self.logger.error(f"Failed to cleanup build {build_id}: {e}")
                    
        freed_mb = freed_bytes / (1024 * 1024)
        self.logger.info(f"Cleanup complete: {cleaned_builds} builds, {freed_mb:.1f}MB freed, {errors} errors")
        
        return {
            "cleaned_builds": cleaned_builds,
            "freed_mb": int(round(freed_mb, 1)),
            "errors": errors
        }
        
    def optimize_storage(self) -> Dict[str, Any]:
        """Optimize storage usage"""
        # This is a placeholder for storage optimization
        # Could include duplicate file detection, compression, etc.
        return {"status": "Storage optimization not implemented yet"}


# Export configuration singleton
production_config = ProductionConfig()