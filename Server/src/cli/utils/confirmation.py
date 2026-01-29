"""Confirmation dialog utilities for CLI commands."""

import click


def confirm_destructive_action(
    action: str,
    item_type: str,
    item_name: str,
    force: bool,
    extra_context: str = ""
) -> None:
    """Prompt user to confirm destructive action unless --force flag is set.

    Args:
        action: The action being performed (e.g., "Delete", "Remove")
        item_type: The type of item (e.g., "script", "GameObject", "asset")
        item_name: The name/path of the item
        force: If True, skip confirmation prompt
        extra_context: Optional additional context (e.g., "from 'Player'")

    Raises:
        click.Abort: If user declines confirmation

    Examples:
        confirm_destructive_action("Delete", "script", "MyScript.cs", force=False)
        # Prompts: "Delete script 'MyScript.cs'?"

        confirm_destructive_action("Remove", "Rigidbody", "Player", force=False, extra_context="from")
        # Prompts: "Remove Rigidbody from 'Player'?"
    """
    if not force:
        if extra_context:
            message = f"{action} {item_type} {extra_context} '{item_name}'?"
        else:
            message = f"{action} {item_type} '{item_name}'?"
        click.confirm(message, abort=True)
