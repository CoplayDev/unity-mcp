"""
Provides a utility to strip carriage return characters from output streams.

This module implements the `CRStripper` class, which wraps a file-like object
to filter out carriage return (\r) characters during write operations.

Usage of this wrapper is essential for Model Context Protocol (MCP) communication
over stdio, as it ensures consistent line endings and safeguards against 
protocol errors, particularly in Windows environments.
"""

from typing import Any, BinaryIO

class CRStripper:
    """
    A file-like wrapper that strips carriage return (\r) characters from data before writing.
    
    This class intercepts write calls to the underlying stream and removes all 
    instances of '\r', ensuring that output is clean and consistent across 
    different platforms.
    """
    def __init__(self, stream: BinaryIO) -> None:
        """
        Initialize the stripper with an underlying stream.

        Args:
            stream (BinaryIO): The underlying file-like object or buffer to wrap (e.g., sys.stdout.buffer).
        """
        self._stream = stream
    
    def write(self, data: bytes | bytearray | str) -> int:
        """
        Write data to the underlying stream after stripping all carriage return characters.

        Args:
            data (bytes | bytearray | str): The data to be written.

        Returns:
            int: The number of bytes or characters processed (matches input length if successful).
        """
        if isinstance(data, (bytes, bytearray)):
            stripped = data.replace(b'\r', b'')
            written = self._stream.write(stripped)
        elif isinstance(data, str):
            stripped = data.replace('\r', '')
            written = self._stream.write(stripped)
        else:
            return self._stream.write(data)

        # If the underlying stream wrote all the stripped data, we report
        # that we wrote all the ORIGINAL data.
        # This prevents callers (like TextIOWrapper) from seeing a "partial write"
        # mismatch when we intentionally removed characters.
        if written == len(stripped):
            return len(data)
        
        return written
        
    def flush(self) -> None:
        """
        Flush the underlying stream.
        """
        return self._stream.flush()
        
    def __getattr__(self, name: str) -> Any:
        """
        Delegate any attribute or method access to the underlying stream.

        Args:
            name (str): The name of the attribute to access.

        Returns:
            Any: The attribute or method from the wrapped stream.
        """
        return getattr(self._stream, name)
