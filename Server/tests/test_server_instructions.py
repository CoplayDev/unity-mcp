"""Custom startup context is appended to the MCP initialization instructions."""

from unittest.mock import MagicMock

import pytest


@pytest.fixture
def server_module():
    import main
    return main


@pytest.fixture
def startup(server_module, monkeypatch):
    for name in (
        "UNITY_MCP_INSTRUCTIONS_FILE",
        "UNITY_MCP_HTTP_REMOTE_HOSTED",
        "UNITY_MCP_API_KEY_VALIDATION_URL",
    ):
        monkeypatch.delenv(name, raising=False)
    monkeypatch.setenv("UNITY_MCP_PROJECT_SCOPED_TOOLS", "true")
    monkeypatch.setenv("UNITY_MCP_HTTP_HOST", "127.0.0.1")
    monkeypatch.setenv("UNITY_MCP_HTTP_PORT", "8080")
    server = MagicMock()
    factory = MagicMock(return_value=server)
    monkeypatch.setattr(server_module, "create_mcp_server", factory)
    return server_module, factory, server


@pytest.mark.parametrize("project_scoped_tools", [False, True])
def test_custom_instructions_keep_builtin_workflows(server_module, project_scoped_tools):
    custom = "Check the project's test scene before changing gameplay."
    builtin = server_module._build_instructions(project_scoped_tools)

    instructions = server_module._build_instructions(project_scoped_tools, custom)

    assert instructions.startswith(builtin.rstrip())
    assert instructions.count(custom) == 1
    assert "mcpforunity://editor/state" in instructions
    if project_scoped_tools:
        assert "mcpforunity://custom-tools" in instructions
    else:
        assert "No project-scoped custom tools resource is available." in instructions


@pytest.mark.parametrize("custom", [None, "", " \n\t"])
@pytest.mark.parametrize("project_scoped_tools", [False, True])
def test_empty_custom_instructions_preserve_defaults(server_module, custom, project_scoped_tools):
    assert server_module._build_instructions(project_scoped_tools, custom) == (
        server_module._build_instructions(project_scoped_tools)
    )


@pytest.mark.parametrize("source", ["cli", "env"])
@pytest.mark.parametrize("transport", ["stdio", "http"])
def test_startup_loads_utf8_file_for_both_transports(startup, monkeypatch, tmp_path, source, transport):
    module, factory, server = startup
    path = tmp_path / "project-context.md"
    custom = "ゲームを変更する前にテストを実行。\nKeep generated assets under Assets/Generated/."
    path.write_bytes(("\ufeff" + custom + "\n").encode("utf-8"))
    argv = ["mcp-for-unity", "--transport", transport]
    if source == "cli":
        argv += ["--instructions-file", str(path)]
    else:
        monkeypatch.setenv("UNITY_MCP_INSTRUCTIONS_FILE", str(path))
    monkeypatch.setattr(module.sys, "argv", argv)

    module.main()

    factory.assert_called_once_with(True, custom_instructions=custom)
    assert server.run.call_args.kwargs["transport"] == transport


def test_cli_instructions_file_overrides_environment(startup, monkeypatch, tmp_path):
    module, factory, _ = startup
    path = tmp_path / "context.md"
    path.write_text("Use the supplied scene.", encoding="utf-8")
    monkeypatch.setenv("UNITY_MCP_INSTRUCTIONS_FILE", str(tmp_path / "missing-env.md"))
    monkeypatch.setattr(module.sys, "argv", ["mcp-for-unity", "--instructions-file", str(path)])

    module.main()

    factory.assert_called_once_with(True, custom_instructions="Use the supplied scene.")


def test_no_instructions_file_keeps_default_startup(startup, monkeypatch):
    module, factory, server = startup
    monkeypatch.setattr(module.sys, "argv", ["mcp-for-unity"])

    module.main()

    factory.assert_called_once_with(True, custom_instructions=None)
    server.run.assert_called_once_with(transport="stdio")


@pytest.mark.parametrize("contents", ["", " \n\t"])
def test_blank_file_keeps_builtin_instructions(startup, monkeypatch, tmp_path, contents):
    module, factory, _ = startup
    path = tmp_path / "blank.md"
    path.write_text(contents, encoding="utf-8")
    monkeypatch.setattr(module.sys, "argv", ["mcp-for-unity", "--instructions-file", str(path)])

    module.main()

    custom = factory.call_args.kwargs["custom_instructions"]
    assert module._build_instructions(True, custom) == module._build_instructions(True)


@pytest.mark.parametrize("source", ["cli", "env"])
@pytest.mark.parametrize("problem", ["missing", "directory", "invalid-utf8"])
def test_unreadable_instructions_stop_startup_without_leaking_context(
    startup, monkeypatch, tmp_path, capsys, source, problem
):
    module, factory, server = startup
    path = tmp_path / "private-context.md"
    if problem == "directory":
        path.mkdir()
    elif problem == "invalid-utf8":
        path.write_bytes(b"private-context-\xff")
    argv = ["mcp-for-unity"]
    if source == "cli":
        argv += ["--instructions-file", str(path)]
    else:
        monkeypatch.setenv("UNITY_MCP_INSTRUCTIONS_FILE", str(path))
    monkeypatch.setattr(module.sys, "argv", argv)

    with pytest.raises(SystemExit) as exc:
        module.main()

    assert exc.value.code == 2
    factory.assert_not_called()
    server.run.assert_not_called()
    error = capsys.readouterr().err
    assert "UTF-8" in error
    assert str(path) not in error
    assert "private-context" not in error


def test_help_documents_instruction_option_and_environment(server_module, monkeypatch, capsys):
    monkeypatch.setattr(server_module.sys, "argv", ["mcp-for-unity", "--help"])

    with pytest.raises(SystemExit) as exc:
        server_module.main()

    assert exc.value.code == 0
    help_text = capsys.readouterr().out
    assert "--instructions-file" in help_text
    assert "UNITY_MCP_INSTRUCTIONS_FILE" in help_text
