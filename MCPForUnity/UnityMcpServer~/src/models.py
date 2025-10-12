from typing import Any
from pydantic import BaseModel


class MCPResponse(BaseModel):
    success: bool
    message: str
    error: str | None = None
    data: Any | None = None
