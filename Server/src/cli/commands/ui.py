"""UI CLI commands - placeholder for future implementation."""

import sys
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, UnityConnectionError


@click.group()
def ui():
    """UI operations - create and modify UI elements."""
    pass


@ui.command("create-canvas")
@click.argument("name")
@click.option(
    "--render-mode",
    type=click.Choice(
        ["ScreenSpaceOverlay", "ScreenSpaceCamera", "WorldSpace"]),
    default="ScreenSpaceOverlay",
    help="Canvas render mode."
)
def create_canvas(name: str, render_mode: str):
    """Create a new Canvas.

    \b
    Examples:
        unity-mcp ui create-canvas "MainUI"
        unity-mcp ui create-canvas "WorldUI" --render-mode WorldSpace
    """
    config = get_config()

    try:
        # Step 1: Create empty GameObject
        result = run_command("manage_gameobject", {
            "action": "create",
            "name": name,
        }, config)

        if not (result.get("success") or result.get("data") or result.get("result")):
            click.echo(format_output(result, config.format))
            return

        # Step 2: Add Canvas components
        for component in ["Canvas", "CanvasScaler", "GraphicRaycaster"]:
            run_command("manage_components", {
                "action": "add",
                "target": name,
                "componentType": component,
            }, config)

        # Step 3: Set render mode
        render_mode_value = {"ScreenSpaceOverlay": 0,
                             "ScreenSpaceCamera": 1, "WorldSpace": 2}.get(render_mode, 0)
        run_command("manage_components", {
            "action": "set_property",
            "target": name,
            "componentType": "Canvas",
            "property": "renderMode",
            "value": render_mode_value,
        }, config)

        click.echo(format_output(result, config.format))
        print_success(f"Created Canvas: {name}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@ui.command("create-text")
@click.argument("name")
@click.option(
    "--parent", "-p",
    required=True,
    help="Parent Canvas or UI element."
)
@click.option(
    "--text", "-t",
    default="New Text",
    help="Initial text content."
)
@click.option(
    "--position",
    nargs=2,
    type=float,
    default=(0, 0),
    help="Anchored position X Y."
)
def create_text(name: str, parent: str, text: str, position: tuple):
    """Create a UI Text element (TextMeshPro).

    \b
    Examples:
        unity-mcp ui create-text "TitleText" --parent "MainUI" --text "Hello World"
    """
    config = get_config()

    try:
        # Step 1: Create empty GameObject with parent
        result = run_command("manage_gameobject", {
            "action": "create",
            "name": name,
            "parent": parent,
            "position": list(position),
        }, config)

        if not (result.get("success") or result.get("data") or result.get("result")):
            click.echo(format_output(result, config.format))
            return

        # Step 2: Add RectTransform and TextMeshProUGUI
        run_command("manage_components", {
            "action": "add",
            "target": name,
            "componentType": "TextMeshProUGUI",
        }, config)

        # Step 3: Set text content
        run_command("manage_components", {
            "action": "set_property",
            "target": name,
            "componentType": "TextMeshProUGUI",
            "property": "text",
            "value": text,
        }, config)

        click.echo(format_output(result, config.format))
        print_success(f"Created Text: {name}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@ui.command("create-button")
@click.argument("name")
@click.option(
    "--parent", "-p",
    required=True,
    help="Parent Canvas or UI element."
)
@click.option(
    "--text", "-t",
    default="Button",
    help="Button label text."
)
def create_button(name: str, parent: str, text: str):  # text current placeholder
    """Create a UI Button.

    \b
    Examples:
        unity-mcp ui create-button "StartButton" --parent "MainUI" --text "Start Game"
    """
    config = get_config()

    try:
        # Step 1: Create empty GameObject with parent
        result = run_command("manage_gameobject", {
            "action": "create",
            "name": name,
            "parent": parent,
        }, config)

        if not (result.get("success") or result.get("data") or result.get("result")):
            click.echo(format_output(result, config.format))
            return

        # Step 2: Add Button and Image components
        for component in ["Image", "Button"]:
            run_command("manage_components", {
                "action": "add",
                "target": name,
                "componentType": component,
            }, config)

        # Step 3: Create child label GameObject
        label_name = f"{name}_Label"
        label_result = run_command("manage_gameobject", {
            "action": "create",
            "name": label_name,
            "parent": name,
        }, config)

        # Step 4: Add TextMeshProUGUI to label and set text
        run_command("manage_components", {
            "action": "add",
            "target": label_name,
            "componentType": "TextMeshProUGUI",
        }, config)
        run_command("manage_components", {
            "action": "set_property",
            "target": label_name,
            "componentType": "TextMeshProUGUI",
            "property": "text",
            "value": text,
        }, config)

        click.echo(format_output(result, config.format))
        print_success(f"Created Button: {name} (with label '{text}')")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@ui.command("create-image")
@click.argument("name")
@click.option(
    "--parent", "-p",
    required=True,
    help="Parent Canvas or UI element."
)
@click.option(
    "--sprite", "-s",
    default=None,
    help="Sprite asset path."
)
def create_image(name: str, parent: str, sprite: Optional[str]):
    """Create a UI Image.

    \b
    Examples:
        unity-mcp ui create-image "Background" --parent "MainUI"
        unity-mcp ui create-image "Icon" --parent "MainUI" --sprite "Assets/Sprites/icon.png"
    """
    config = get_config()

    try:
        # Step 1: Create empty GameObject with parent
        result = run_command("manage_gameobject", {
            "action": "create",
            "name": name,
            "parent": parent,
        }, config)

        if not (result.get("success") or result.get("data") or result.get("result")):
            click.echo(format_output(result, config.format))
            return

        # Step 2: Add Image component
        run_command("manage_components", {
            "action": "add",
            "target": name,
            "componentType": "Image",
        }, config)

        # Step 3: Set sprite if provided
        if sprite:
            run_command("manage_components", {
                "action": "set_property",
                "target": name,
                "componentType": "Image",
                "property": "sprite",
                "value": sprite,
            }, config)

        click.echo(format_output(result, config.format))
        print_success(f"Created Image: {name}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)
