from typing import Any
from datetime import datetime
from pydantic import BaseModel


class MCPResponse(BaseModel):
    success: bool
    message: str | None = None
    error: str | None = None
    data: Any | None = None


class UnityInstanceInfo(BaseModel):
    """Information about a Unity Editor instance"""
    id: str  # "ProjectName@hash" or fallback to hash
    name: str  # Project name extracted from path
    path: str  # Full project path (Assets folder)
    hash: str  # 8-char hash of project path
    port: int  # TCP port
    status: str  # "running", "reloading", "offline"
    last_heartbeat: datetime | None = None
    unity_version: str | None = None

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for JSON serialization"""
        return {
            "id": self.id,
            "name": self.name,
            "path": self.path,
            "hash": self.hash,
            "port": self.port,
            "status": self.status,
            "last_heartbeat": self.last_heartbeat.isoformat() if self.last_heartbeat else None,
            "unity_version": self.unity_version
        }
