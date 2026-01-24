"""Shader CLI commands for managing Unity shaders."""

import sys
import click
from typing import Optional

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, UnityConnectionError


@click.group()
def shader():
    """Shader operations - create, read, update, delete shaders."""
    pass


@shader.command("read")
@click.argument("path")
def read_shader(path: str):
    """Read a shader file.

    \\b
    Examples:
        unity-mcp shader read "Assets/Shaders/MyShader.shader"
    """
    config = get_config()

    # Extract name from path
    import os
    name = os.path.splitext(os.path.basename(path))[0]
    directory = os.path.dirname(path)

    try:
        result = run_command("manage_shader", {
            "action": "read",
            "name": name,
            "path": directory or "Assets/",
        }, config)

        # If successful, display the contents nicely
        if result.get("success") and result.get("data", {}).get("contents"):
            click.echo(result["data"]["contents"])
        else:
            click.echo(format_output(result, config.format))
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@shader.command("create")
@click.argument("name")
@click.option(
    "--path", "-p",
    default="Assets/Shaders",
    help="Directory to create shader in."
)
@click.option(
    "--contents", "-c",
    default=None,
    help="Shader code (reads from stdin if not provided)."
)
@click.option(
    "--file", "-f",
    "file_path",
    default=None,
    type=click.Path(exists=True),
    help="Read shader code from file."
)
def create_shader(name: str, path: str, contents: Optional[str], file_path: Optional[str]):
    """Create a new shader.

    \\b
    Examples:
        unity-mcp shader create "MyShader" --path "Assets/Shaders"
        unity-mcp shader create "MyShader" --file local_shader.shader
        echo "Shader code..." | unity-mcp shader create "MyShader"
    """
    config = get_config()

    # Get contents from file, option, or stdin
    if file_path:
        with open(file_path, 'r') as f:
            shader_contents = f.read()
    elif contents:
        shader_contents = contents
    else:
        # Read from stdin if available
        import sys
        if not sys.stdin.isatty():
            shader_contents = sys.stdin.read()
        else:
            # Provide default shader template
            shader_contents = f'''Shader "Custom/{name}"
{{
    Properties
    {{
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {{}}
    }}
    SubShader
    {{
        Tags {{ "RenderType"="Opaque" }}
        LOD 200
        
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        
        sampler2D _MainTex;
        fixed4 _Color;
        
        struct Input
        {{
            float2 uv_MainTex;
        }};
        
        void surf (Input IN, inout SurfaceOutputStandard o)
        {{
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
        }}
        ENDCG
    }}
    FallBack "Diffuse"
}}
'''

    try:
        result = run_command("manage_shader", {
            "action": "create",
            "name": name,
            "path": path,
            "contents": shader_contents,
        }, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Created shader: {path}/{name}.shader")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@shader.command("update")
@click.argument("path")
@click.option(
    "--contents", "-c",
    default=None,
    help="New shader code."
)
@click.option(
    "--file", "-f",
    "file_path",
    default=None,
    type=click.Path(exists=True),
    help="Read shader code from file."
)
def update_shader(path: str, contents: Optional[str], file_path: Optional[str]):
    """Update an existing shader.

    \\b
    Examples:
        unity-mcp shader update "Assets/Shaders/MyShader.shader" --file updated.shader
        echo "New shader code" | unity-mcp shader update "Assets/Shaders/MyShader.shader"
    """
    config = get_config()

    import os
    name = os.path.splitext(os.path.basename(path))[0]
    directory = os.path.dirname(path)

    # Get contents from file, option, or stdin
    if file_path:
        with open(file_path, 'r') as f:
            shader_contents = f.read()
    elif contents:
        shader_contents = contents
    else:
        import sys
        if not sys.stdin.isatty():
            shader_contents = sys.stdin.read()
        else:
            print_error(
                "No shader contents provided. Use --contents, --file, or pipe via stdin.")
            sys.exit(1)

    try:
        result = run_command("manage_shader", {
            "action": "update",
            "name": name,
            "path": directory or "Assets/",
            "contents": shader_contents,
        }, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Updated shader: {path}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@shader.command("delete")
@click.argument("path")
@click.option(
    "--force", "-f",
    is_flag=True,
    help="Skip confirmation prompt."
)
def delete_shader(path: str, force: bool):
    """Delete a shader.

    \\b
    Examples:
        unity-mcp shader delete "Assets/Shaders/OldShader.shader"
        unity-mcp shader delete "Assets/Shaders/OldShader.shader" --force
    """
    config = get_config()

    if not force:
        click.confirm(f"Delete shader '{path}'?", abort=True)

    import os
    name = os.path.splitext(os.path.basename(path))[0]
    directory = os.path.dirname(path)

    try:
        result = run_command("manage_shader", {
            "action": "delete",
            "name": name,
            "path": directory or "Assets/",
        }, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Deleted shader: {path}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)
