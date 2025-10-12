from typing import Any
from pydantic import BaseModel


class MCPResponse(BaseModel):
    success: bool
    message: str
    data: Any | None = None
