"""
Shared module discovery utilities for auto-registering tools and resources.
"""
import importlib
import logging
from pathlib import Path
import pkgutil
from typing import Generator

logger = logging.getLogger("mcp-for-unity-server")


def discover_modules(base_dir: Path, package_name: str) -> Generator[str, None, None]:
    """
    Discover and import all Python modules in a directory and its subdirectories.

    Args:
        base_dir: The base directory to search for modules
        package_name: The package name to use for relative imports (e.g., 'tools' or 'resources')

    Yields:
        Full module names that were successfully imported
    """
    # Discover modules in the top level
    for _, module_name, _ in pkgutil.iter_modules([str(base_dir)]):
        # Skip private modules and __init__
        if module_name.startswith('_'):
            continue

        try:
            full_module_name = f'.{module_name}'
            importlib.import_module(full_module_name, package_name)
            yield full_module_name
        except Exception as e:
            logger.warning(f"Failed to import module {module_name}: {e}")

    # Discover modules in subdirectories (one level deep)
    for subdir in base_dir.iterdir():
        if not subdir.is_dir() or subdir.name.startswith('_') or subdir.name.startswith('.'):
            continue

        # Check if subdirectory contains Python modules
        for _, module_name, _ in pkgutil.iter_modules([str(subdir)]):
            # Skip private modules and __init__
            if module_name.startswith('_'):
                continue

            try:
                # Import as package.subdirname.modulename
                full_module_name = f'.{subdir.name}.{module_name}'
                importlib.import_module(full_module_name, package_name)
                yield full_module_name
            except Exception as e:
                logger.warning(
                    f"Failed to import module {subdir.name}.{module_name}: {e}")
