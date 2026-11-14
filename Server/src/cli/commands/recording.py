"""Motion recording CLI, sharing validation with the MCP tool."""
import json
import click
from cli.utils.config import get_config
from cli.utils.connection import handle_unity_errors, run_command
from cli.utils.output import format_output
from services.tools.manage_recording import build_recording_params


@click.group()
def recording() -> None:
    """Record views and control motion sources with a JSON specification."""


@recording.command("run")
@click.argument("action", type=click.Choice(["capabilities", "discover", "prepare", "start", "status", "stop", "cancel"]))
@click.option("--spec", default="{}", help="JSON recording parameters, with the same keys as manage_recording.")
@click.option("--recording-id", default=None, help="Prepared/running recording ID.")
@handle_unity_errors
def run(action: str, spec: str, recording_id: str | None) -> None:
    """Execute an action. Start accepts a spec or a prepared recording ID."""
    try:
        values = json.loads(spec)
        if not isinstance(values, dict):
            raise ValueError("--spec must be a JSON object")
        values["action"] = action
        if recording_id is not None:
            values["recording_id"] = recording_id
        params = build_recording_params(values)
    except (ValueError, TypeError) as exc:
        raise click.BadParameter(str(exc), param_hint="--spec") from exc
    config = get_config()
    click.echo(format_output(run_command("manage_recording", params, config), config.format))
