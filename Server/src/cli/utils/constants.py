"""Common constants for CLI commands."""
import click

# Search method constants used across various CLI commands
# These define how GameObjects and other Unity objects can be located

# Full set of search methods (used by gameobject commands)
SEARCH_METHODS_FULL = ["by_name", "by_path", "by_id", "by_tag", "by_layer", "by_component"]

# Basic search methods (used by component, animation, audio commands)
SEARCH_METHODS_BASIC = ["by_id", "by_name", "by_path"]

# Extended search methods for renderer-based commands (material commands)
SEARCH_METHODS_RENDERER = ["by_id", "by_name", "by_path", "by_tag", "by_layer", "by_component"]

# Tagged search methods (used by VFX commands)
SEARCH_METHODS_TAGGED = ["by_name", "by_path", "by_id", "by_tag"]

# Click choice options for each set
SEARCH_METHOD_CHOICE_FULL = click.Choice(SEARCH_METHODS_FULL)
SEARCH_METHOD_CHOICE_BASIC = click.Choice(SEARCH_METHODS_BASIC)
SEARCH_METHOD_CHOICE_RENDERER = click.Choice(SEARCH_METHODS_RENDERER)
SEARCH_METHOD_CHOICE_TAGGED = click.Choice(SEARCH_METHODS_TAGGED)
