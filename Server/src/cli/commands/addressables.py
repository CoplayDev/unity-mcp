"""Addressables configuration queries."""

from typing import Any
import json

import click

from cli.utils.config import get_config
from cli.utils.connection import handle_unity_errors, run_command
from cli.utils.output import format_output


def _query(action: str, **values: Any) -> None:
    config = get_config()
    params = {"action": action, **{key: value for key, value in values.items() if value is not None}}
    click.echo(format_output(run_command("manage_addressables", params, config), config.format))


def _paging(command):
    command = click.option("--cursor", help="Opaque next_cursor from a previous page.")(command)
    return click.option("--page-size", type=click.IntRange(1, 200), help="Items per page (default 50).")(command)


def _identity(command):
    command = click.option("--guid", help="Asset GUID; mutually exclusive with --path.")(command)
    return click.option("--path", help="Project asset path; mutually exclusive with --guid.")(command)


def _asset(guid: str | None, path: str | None) -> dict[str, str]:
    if (guid is None) == (path is None):
        raise click.UsageError("Provide exactly one of --guid or --path.")
    return {"guid": guid} if guid is not None else {"path": path}


def _json(value: str, expected: type) -> Any:
    try:
        parsed = json.loads(value)
    except json.JSONDecodeError as exc:
        raise click.BadParameter(f"Invalid JSON: {exc.msg}") from exc
    if not isinstance(parsed, expected):
        raise click.BadParameter(f"Expected a JSON {expected.__name__}.")
    return parsed


@click.group()
def addressables():
    """Query and edit Addressables configuration."""


@addressables.command("ping")
@handle_unity_errors
def ping():
    """Check package, API and settings availability without creating settings."""
    _query("ping")


@addressables.command("settings")
@handle_unity_errors
def settings():
    """Read basic settings."""
    _query("get_settings")


@addressables.command("groups")
@_paging
@handle_unity_errors
def groups(page_size: int | None, cursor: str | None):
    """List groups in GUID order."""
    _query("group_list", page_size=page_size, cursor=cursor)


@addressables.command("entries")
@click.option("--group", help="Group GUID or exact name; defaults to all groups.")
@_paging
@handle_unity_errors
def entries(group: str | None, page_size: int | None, cursor: str | None):
    """List explicitly registered entries, without expanding folders."""
    _query("entry_list", group=group, page_size=page_size, cursor=cursor)


@addressables.command("find")
@click.option("--guid", help="Asset GUID, mutually exclusive with --path.")
@click.option("--path", help="Project asset path, mutually exclusive with --guid.")
@handle_unity_errors
def find(guid: str | None, path: str | None):
    """Find an explicitly registered entry."""
    if (guid is None) == (path is None):
        raise click.UsageError("Provide exactly one of --guid or --path.")
    _query("entry_find", guid=guid, path=path)


@addressables.command("labels")
@_paging
@handle_unity_errors
def labels(page_size: int | None, cursor: str | None):
    """List label definitions."""
    _query("label_list", page_size=page_size, cursor=cursor)


@addressables.command("profiles")
@_paging
@handle_unity_errors
def profiles(page_size: int | None, cursor: str | None):
    """List profile IDs and names."""
    _query("profile_list", page_size=page_size, cursor=cursor)


@addressables.command("profile")
@click.option("--profile-id", help="Profile ID; defaults to the active profile.")
@_paging
@handle_unity_errors
def profile(profile_id: str | None, page_size: int | None, cursor: str | None):
    """Read raw profile variable values without evaluating tokens."""
    _query("profile_get", profile_id=profile_id, page_size=page_size, cursor=cursor)


@addressables.command("group-create")
@click.argument("name")
@click.option("--static-content/--dynamic-content", default=None, help="Content update restriction; omit to keep Unity's default.")
@handle_unity_errors
def group_create(name: str, static_content: bool | None):
    """Create a bundled group with content-update schema."""
    _query("group_create", name=name, static_content=static_content)


@addressables.command("group-remove")
@click.argument("group")
@click.option("--force", is_flag=True, help="Also remove registrations in a non-empty group.")
@handle_unity_errors
def group_remove(group: str, force: bool):
    """Remove group configuration, preserving source assets."""
    _query("group_remove", group=group, force=force)


@addressables.command("entry-add")
@click.argument("group")
@_identity
@handle_unity_errors
def entry_add(group: str, guid: str | None, path: str | None):
    """Register an asset in the given group."""
    _query("entry_add", group=group, **_asset(guid, path))


@addressables.command("entry-remove")
@_identity
@handle_unity_errors
def entry_remove(guid: str | None, path: str | None):
    """Remove a registration without deleting the source asset."""
    _query("entry_remove", **_asset(guid, path))


@addressables.command("entry-move")
@click.argument("group")
@_identity
@handle_unity_errors
def entry_move(group: str, guid: str | None, path: str | None):
    """Move an existing registration to a different group."""
    _query("entry_move", group=group, **_asset(guid, path))


@addressables.command("entry-address")
@click.argument("address")
@_identity
@handle_unity_errors
def entry_address(address: str, guid: str | None, path: str | None):
    """Change an existing entry's address."""
    _query("entry_set_address", address=address, **_asset(guid, path))


@addressables.command("label-add")
@click.argument("name")
@handle_unity_errors
def label_add(name: str):
    """Add a label definition."""
    _query("label_add", name=name)


@addressables.command("label-remove")
@click.argument("name")
@click.option("--force", is_flag=True, help="Detach the label from entries before removing it.")
@handle_unity_errors
def label_remove(name: str, force: bool):
    """Remove a label definition."""
    _query("label_remove", name=name, force=force)


@addressables.command("label-set")
@click.option("--labels", required=True, help="JSON list of existing labels; [] clears labels.")
@_identity
@handle_unity_errors
def label_set(labels: str, guid: str | None, path: str | None):
    """Replace an entry's assigned labels."""
    _query("label_set", labels=_json(labels, list), **_asset(guid, path))


@addressables.command("profile-set")
@click.argument("profile_id")
@click.argument("name")
@click.argument("value")
@handle_unity_errors
def profile_set(profile_id: str, name: str, value: str):
    """Set the raw value of an existing profile variable."""
    _query("profile_set", profile_id=profile_id, name=name, value=value)


@addressables.command("profile-activate")
@click.argument("profile_id")
@handle_unity_errors
def profile_activate(profile_id: str):
    """Select an existing profile."""
    _query("profile_set_active", profile_id=profile_id)


@addressables.command("settings-set")
@click.option("--values", default="{}", help="JSON object of supported settings updates.")
@click.option("--initialize", is_flag=True, help="Explicitly create default settings if absent.")
@handle_unity_errors
def settings_set(values: str, initialize: bool):
    """Change supported settings, optionally initializing the project."""
    _query("set_settings", settings=_json(values, dict), initialize=initialize)


@addressables.command("build-content")
@handle_unity_errors
def build_content():
    """Queue a full content build for the active platform and builder."""
    _query("build_content")


@addressables.command("build-update")
@click.argument("content_state_path", type=click.Path(exists=True, dir_okay=False, resolve_path=True))
@handle_unity_errors
def build_update(content_state_path: str):
    """Queue an update using the matching previous build's content-state file."""
    _query("build_update", content_state_path=content_state_path)


@addressables.command("build-clean")
@handle_unity_errors
def build_clean():
    """Queue cleanup through the active player data builder."""
    _query("build_clean")


@addressables.command("build-status")
@click.option("--job-id", help="Job ID; defaults to the latest content operation.")
@handle_unity_errors
def build_status(job_id: str | None):
    """Read the latest build's state and output paths."""
    _query("build_status", job_id=job_id)
