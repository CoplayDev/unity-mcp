class _DummyMeta(dict):
    def __getattr__(self, item):
        try:
            return self[item]
        except KeyError as exc:
            raise AttributeError(item) from exc

    model_extra = property(lambda self: self)

    def model_dump(self, exclude_none=True):
        if not exclude_none:
            return dict(self)
        return {k: v for k, v in self.items() if v is not None}


class DummyContext:
    """Mock context object for testing"""

    def __init__(self, **meta):
        import uuid
        self.log_info = []
        self.log_warning = []
        self.log_error = []
        self._meta = _DummyMeta(meta)
        # Give each context a unique session_id to avoid state leakage between tests
        self.session_id = str(uuid.uuid4())
        # Add state storage to mimic FastMCP context state
        self._state = {}

        class _RequestContext:
            def __init__(self, meta):
                self.meta = meta

        self.request_context = _RequestContext(self._meta)

    async def info(self, message):
        self.log_info.append(message)

    async def warning(self, message):
        self.log_warning.append(message)

    # Some code paths call warn(); treat it as an alias of warning()
    async def warn(self, message):
        await self.warning(message)

    async def error(self, message):
        self.log_error.append(message)

    def set_state(self, key, value):
        """Set state value (mimics FastMCP context.set_state)"""
        self._state[key] = value

    def get_state(self, key, default=None):
        """Get state value (mimics FastMCP context.get_state)"""
        return self._state.get(key, default)


class DummyMCP:
    """Mock MCP server for testing tool registration patterns."""

    def __init__(self):
        self.tools = {}

    def tool(self, *args, **kwargs):
        def deco(fn):
            self.tools[fn.__name__] = fn
            return fn
        return deco


def setup_script_tools():
    """
    Setup script-related tools for testing.

    Returns a dict mapping tool names to their async handler functions.
    Useful for testing parameter serialization and SDK behavior.
    """
    mcp = DummyMCP()
    # Import tools to trigger decorator-based registration
    import services.tools.manage_script
    from services.registry import get_registered_tools

    for tool_info in get_registered_tools():
        name = tool_info['name']
        if any(k in name for k in ['script', 'apply_text', 'create_script',
                                    'delete_script', 'validate_script', 'get_sha']):
            mcp.tools[name] = tool_info['func']
    return mcp.tools
