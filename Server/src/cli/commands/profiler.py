import click
from cli.utils.connection import handle_unity_errors, run_command, get_config
from cli.utils.output import format_output


@click.group("profiler")
def profiler():
    """Read Unity Profiler counters: CPU timing, GC allocation, and animation."""
    pass


@profiler.command("frame-timing")
@handle_unity_errors
def frame_timing():
    """Get main thread, render thread, CPU and GPU frame timing (ms)."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "get_frame_timing"}, config)
    click.echo(format_output(result, config.format))


@profiler.command("script-timing")
@handle_unity_errors
def script_timing():
    """Get Update, FixedUpdate, and LateUpdate script execution time (ms)."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "get_script_timing"}, config)
    click.echo(format_output(result, config.format))


@profiler.command("physics-timing")
@handle_unity_errors
def physics_timing():
    """Get Physics.Processing and Physics.FetchResults time (ms)."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "get_physics_timing"}, config)
    click.echo(format_output(result, config.format))


@profiler.command("gc-alloc")
@handle_unity_errors
def gc_alloc():
    """Get GC allocation bytes and count per frame."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "get_gc_alloc"}, config)
    click.echo(format_output(result, config.format))


@profiler.command("animation-timing")
@handle_unity_errors
def animation_timing():
    """Get Animator.Update time (ms)."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "get_animation_timing"}, config)
    click.echo(format_output(result, config.format))
