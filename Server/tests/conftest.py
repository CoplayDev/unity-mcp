"""Pytest configuration for unity-mcp tests."""
import sys
from pathlib import Path

# Add src directory to Python path so tests can import cli, transport, etc.
src_path = Path(__file__).parent.parent / "src"
if str(src_path) not in sys.path:
    sys.path.insert(0, str(src_path))
