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
        """
        Return a dictionary representation of the UnityInstanceInfo suitable for JSON serialization.
        
        The mapping includes keys "id", "name", "path", "hash", "port", "status", "last_heartbeat", and "unity_version". If last_heartbeat is present, it is converted to an ISO 8601 string via isoformat(); otherwise its value is None.
        
        Returns:
            dict[str, Any]: Serialized representation of the instance.
        """
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