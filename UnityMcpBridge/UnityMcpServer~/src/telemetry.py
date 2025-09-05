"""
Privacy-focused, anonymous telemetry system for Unity MCP
Inspired by Onyx's telemetry implementation with Unity-specific adaptations
"""

import uuid
import threading
import contextvars
import json
import time
import os
import logging
from enum import Enum
from dataclasses import dataclass, asdict
from typing import Optional, Dict, Any, List
from pathlib import Path

try:
    import httpx
    HAS_HTTPX = True
except ImportError:
    httpx = None  # type: ignore
    HAS_HTTPX = False

logger = logging.getLogger("unity-mcp-telemetry")

class RecordType(str, Enum):
    """Types of telemetry records we collect"""
    VERSION = "version"
    STARTUP = "startup" 
    USAGE = "usage"
    LATENCY = "latency"
    FAILURE = "failure"
    TOOL_EXECUTION = "tool_execution"
    UNITY_CONNECTION = "unity_connection"
    CLIENT_CONNECTION = "client_connection"

class MilestoneType(str, Enum):
    """Major user journey milestones"""
    FIRST_STARTUP = "first_startup"
    FIRST_TOOL_USAGE = "first_tool_usage"
    FIRST_SCRIPT_CREATION = "first_script_creation"
    FIRST_SCENE_MODIFICATION = "first_scene_modification"
    MULTIPLE_SESSIONS = "multiple_sessions"
    DAILY_ACTIVE_USER = "daily_active_user"
    WEEKLY_ACTIVE_USER = "weekly_active_user"

@dataclass
class TelemetryRecord:
    """Structure for telemetry data"""
    record_type: RecordType
    timestamp: float
    customer_uuid: str
    session_id: str
    data: Dict[str, Any]
    milestone: Optional[MilestoneType] = None

class TelemetryConfig:
    """Telemetry configuration"""
    def __init__(self):
        # Check environment variables for opt-out
        self.enabled = not self._is_disabled()
        
        # Telemetry endpoint - hardcoded to Coplay production API
        self.endpoint = "https://api-prod.coplay.dev/telemetry/events"
        
        # Local storage for UUID and milestones
        self.data_dir = self._get_data_directory()
        self.uuid_file = self.data_dir / "customer_uuid.txt"
        self.milestones_file = self.data_dir / "milestones.json"
        
        # Request timeout
        self.timeout = 10.0
        
        # Session tracking
        self.session_id = str(uuid.uuid4())
        
    def _is_disabled(self) -> bool:
        """Check if telemetry is disabled via environment variables"""
        disable_vars = [
            "DISABLE_TELEMETRY",
            "UNITY_MCP_DISABLE_TELEMETRY", 
            "MCP_DISABLE_TELEMETRY"
        ]
        
        for var in disable_vars:
            if os.environ.get(var, "").lower() in ("true", "1", "yes", "on"):
                return True
        return False
        
    def _get_data_directory(self) -> Path:
        """Get directory for storing telemetry data"""
        if os.name == 'nt':  # Windows
            base_dir = Path(os.environ.get('APPDATA', Path.home() / 'AppData' / 'Roaming'))
        elif os.name == 'posix':  # macOS/Linux
            if 'darwin' in os.uname().sysname.lower():  # macOS
                base_dir = Path.home() / 'Library' / 'Application Support'
            else:  # Linux
                base_dir = Path(os.environ.get('XDG_DATA_HOME', Path.home() / '.local' / 'share'))
        else:
            base_dir = Path.home() / '.unity-mcp'
            
        data_dir = base_dir / 'UnityMCP'
        data_dir.mkdir(parents=True, exist_ok=True)
        return data_dir

class TelemetryCollector:
    """Main telemetry collection class"""
    
    def __init__(self):
        self.config = TelemetryConfig()
        self._customer_uuid: Optional[str] = None
        self._milestones: Dict[str, Dict[str, Any]] = {}
        self._load_persistent_data()
        
    def _load_persistent_data(self):
        """Load UUID and milestones from disk"""
        try:
            # Load customer UUID
            if self.config.uuid_file.exists():
                self._customer_uuid = self.config.uuid_file.read_text().strip()
            else:
                self._customer_uuid = str(uuid.uuid4())
                self.config.uuid_file.write_text(self._customer_uuid)
                
            # Load milestones
            if self.config.milestones_file.exists():
                self._milestones = json.loads(self.config.milestones_file.read_text())
        except Exception as e:
            logger.warning(f"Failed to load telemetry data: {e}")
            self._customer_uuid = str(uuid.uuid4())
            self._milestones = {}
    
    def _save_milestones(self):
        """Save milestones to disk"""
        try:
            self.config.milestones_file.write_text(json.dumps(self._milestones, indent=2))
        except Exception as e:
            logger.warning(f"Failed to save milestones: {e}")
    
    def record_milestone(self, milestone: MilestoneType, data: Optional[Dict[str, Any]] = None) -> bool:
        """Record a milestone event, returns True if this is the first occurrence"""
        if not self.config.enabled:
            return False
            
        milestone_key = milestone.value
        if milestone_key in self._milestones:
            return False  # Already recorded
            
        milestone_data = {
            "timestamp": time.time(),
            "data": data or {}
        }
        
        self._milestones[milestone_key] = milestone_data
        self._save_milestones()
        
        # Also send as telemetry record
        self.record(
            record_type=RecordType.USAGE,
            data={"milestone": milestone_key, **(data or {})},
            milestone=milestone
        )
        
        return True
    
    def record(self, 
               record_type: RecordType, 
               data: Dict[str, Any], 
               milestone: Optional[MilestoneType] = None):
        """Record a telemetry event (async, non-blocking)"""
        if not self.config.enabled:
            return
            
        if not HAS_HTTPX:
            logger.debug("Telemetry disabled: httpx not available")
            return
            
        record = TelemetryRecord(
            record_type=record_type,
            timestamp=time.time(),
            customer_uuid=self._customer_uuid or "unknown",
            session_id=self.config.session_id,
            data=data,
            milestone=milestone
        )
        
        # Send in background thread to avoid blocking
        current_context = contextvars.copy_context()
        thread = threading.Thread(
            target=lambda: current_context.run(self._send_telemetry, record),
            daemon=True
        )
        thread.start()
    
    def _send_telemetry(self, record: TelemetryRecord):
        """Send telemetry data to endpoint"""
        try:
            payload = {
                "record": record.record_type.value,
                "timestamp": record.timestamp,
                "customer_uuid": record.customer_uuid,
                "session_id": record.session_id,
                "data": record.data,
                "version": "3.0.2",  # Unity MCP version
                "platform": os.name
            }
            
            if record.milestone:
                payload["milestone"] = record.milestone.value
                
            if not httpx:
                return
                
            with httpx.Client(timeout=self.config.timeout) as client:
                response = client.post(self.config.endpoint, json=payload)
                
                if response.status_code == 200:
                    logger.debug(f"Telemetry sent: {record.record_type}")
                else:
                    logger.debug(f"Telemetry failed: HTTP {response.status_code}")
                    
        except Exception as e:
            # Never let telemetry errors interfere with app functionality
            logger.debug(f"Telemetry send failed: {e}")

# Global telemetry instance
_telemetry_collector: Optional[TelemetryCollector] = None

def get_telemetry() -> TelemetryCollector:
    """Get the global telemetry collector instance"""
    global _telemetry_collector
    if _telemetry_collector is None:
        _telemetry_collector = TelemetryCollector()
    return _telemetry_collector

def record_telemetry(record_type: RecordType, 
                    data: Dict[str, Any], 
                    milestone: Optional[MilestoneType] = None):
    """Convenience function to record telemetry"""
    get_telemetry().record(record_type, data, milestone)

def record_milestone(milestone: MilestoneType, data: Optional[Dict[str, Any]] = None) -> bool:
    """Convenience function to record a milestone"""
    return get_telemetry().record_milestone(milestone, data)

def record_tool_usage(tool_name: str, success: bool, duration_ms: float, error: Optional[str] = None):
    """Record tool usage telemetry"""
    data = {
        "tool_name": tool_name,
        "success": success,
        "duration_ms": round(duration_ms, 2)
    }
    
    if error:
        data["error"] = str(error)[:200]  # Limit error message length
        
    record_telemetry(RecordType.TOOL_EXECUTION, data)

def record_latency(operation: str, duration_ms: float, metadata: Optional[Dict[str, Any]] = None):
    """Record latency telemetry"""
    data = {
        "operation": operation,
        "duration_ms": round(duration_ms, 2)
    }
    
    if metadata:
        data.update(metadata)
        
    record_telemetry(RecordType.LATENCY, data)

def record_failure(component: str, error: str, metadata: Optional[Dict[str, Any]] = None):
    """Record failure telemetry"""
    data = {
        "component": component,
        "error": str(error)[:500]  # Limit error message length
    }
    
    if metadata:
        data.update(metadata)
        
    record_telemetry(RecordType.FAILURE, data)

def is_telemetry_enabled() -> bool:
    """Check if telemetry is enabled"""
    return get_telemetry().config.enabled