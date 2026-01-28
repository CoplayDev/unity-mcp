"""Tests for parameter normalization utility."""
import pytest
from transport.param_normalizer_middleware import camel_to_snake, normalize_arguments
from services.tools.param_normalizer import normalize_params


class TestNormalizeArguments:
    """Tests for normalize_arguments function (middleware version)."""

    def test_camel_case_to_snake_case(self):
        """camelCase arguments are normalized to snake_case."""
        result = normalize_arguments({
            "searchMethod": "by_name",
            "searchTerm": "Player",
            "pageSize": 50
        })
        assert result == {
            "search_method": "by_name",
            "search_term": "Player",
            "page_size": 50
        }

    def test_snake_case_unchanged(self):
        """snake_case arguments pass through unchanged."""
        result = normalize_arguments({
            "search_method": "by_name",
            "search_term": "Player"
        })
        assert result == {
            "search_method": "by_name",
            "search_term": "Player"
        }

    def test_mixed_conventions(self):
        """Mixed camelCase and snake_case are handled."""
        result = normalize_arguments({
            "searchMethod": "by_name",
            "search_term": "Player"
        })
        assert result == {
            "search_method": "by_name",
            "search_term": "Player"
        }

    def test_conflict_prefers_snake_case(self):
        """When both conventions provided, snake_case wins."""
        result = normalize_arguments({
            "searchMethod": "by_id",
            "search_method": "by_name"
        })
        assert result["search_method"] == "by_name"

    def test_none_returns_none(self):
        """None input returns None."""
        assert normalize_arguments(None) is None

    def test_empty_dict(self):
        """Empty dict returns empty dict."""
        assert normalize_arguments({}) == {}


class TestCamelToSnake:
    """Tests for camel_to_snake conversion function."""

    def test_standard_camel_case(self):
        """Standard camelCase converts correctly."""
        assert camel_to_snake("searchMethod") == "search_method"
        assert camel_to_snake("searchTerm") == "search_term"
        assert camel_to_snake("includeInactive") == "include_inactive"
        assert camel_to_snake("pageSize") == "page_size"

    def test_already_snake_case(self):
        """Already snake_case passes through unchanged."""
        assert camel_to_snake("search_method") == "search_method"
        assert camel_to_snake("page_size") == "page_size"
        assert camel_to_snake("include_inactive") == "include_inactive"

    def test_single_word(self):
        """Single lowercase word passes through unchanged."""
        assert camel_to_snake("action") == "action"
        assert camel_to_snake("path") == "path"
        assert camel_to_snake("name") == "name"

    def test_consecutive_capitals(self):
        """Consecutive capitals (acronyms) are handled."""
        assert camel_to_snake("HTMLParser") == "html_parser"
        assert camel_to_snake("parseHTML") == "parse_html"
        assert camel_to_snake("XMLHTTPRequest") == "xmlhttp_request"

    def test_numbers_in_name(self):
        """Numbers in parameter names are handled."""
        assert camel_to_snake("filter2D") == "filter2_d"
        assert camel_to_snake("vector3") == "vector3"
        assert camel_to_snake("point2d") == "point2d"

    def test_mixed_case_words(self):
        """Multiple camelCase words convert correctly."""
        assert camel_to_snake("componentProperties") == "component_properties"
        assert camel_to_snake("generatePreview") == "generate_preview"
        assert camel_to_snake("filterDateAfter") == "filter_date_after"


class TestNormalizeParamsSync:
    """Tests for normalize_params decorator with sync functions."""

    def test_sync_function_camel_case_params(self):
        """Sync function receives normalized snake_case params."""
        received_kwargs = {}

        @normalize_params
        def sync_tool(**kwargs):
            received_kwargs.update(kwargs)
            return "ok"

        result = sync_tool(searchMethod="by_name", searchTerm="Player")

        assert result == "ok"
        assert received_kwargs == {
            "search_method": "by_name",
            "search_term": "Player"
        }

    def test_sync_function_snake_case_params(self):
        """Sync function passes through snake_case params unchanged."""
        received_kwargs = {}

        @normalize_params
        def sync_tool(**kwargs):
            received_kwargs.update(kwargs)
            return "ok"

        result = sync_tool(search_method="by_name", search_term="Player")

        assert result == "ok"
        assert received_kwargs == {
            "search_method": "by_name",
            "search_term": "Player"
        }

    def test_sync_function_mixed_params(self):
        """Sync function handles mixed camelCase and snake_case."""
        received_kwargs = {}

        @normalize_params
        def sync_tool(**kwargs):
            received_kwargs.update(kwargs)
            return "ok"

        result = sync_tool(searchMethod="by_name", search_term="Player", pageSize=50)

        assert result == "ok"
        assert received_kwargs == {
            "search_method": "by_name",
            "search_term": "Player",
            "page_size": 50
        }

    def test_sync_function_conflict_prefers_snake_case(self):
        """When both conventions provided, snake_case wins."""
        received_kwargs = {}

        @normalize_params
        def sync_tool(**kwargs):
            received_kwargs.update(kwargs)
            return "ok"

        # Both searchMethod and search_method provided
        result = sync_tool(searchMethod="by_id", search_method="by_name")

        assert result == "ok"
        # snake_case value should win
        assert received_kwargs["search_method"] == "by_name"


class TestNormalizeParamsAsync:
    """Tests for normalize_params decorator with async functions."""

    @pytest.mark.asyncio
    async def test_async_function_camel_case_params(self):
        """Async function receives normalized snake_case params."""
        received_kwargs = {}

        @normalize_params
        async def async_tool(**kwargs):
            received_kwargs.update(kwargs)
            return "ok"

        result = await async_tool(searchMethod="by_name", searchTerm="Player")

        assert result == "ok"
        assert received_kwargs == {
            "search_method": "by_name",
            "search_term": "Player"
        }

    @pytest.mark.asyncio
    async def test_async_function_snake_case_params(self):
        """Async function passes through snake_case params unchanged."""
        received_kwargs = {}

        @normalize_params
        async def async_tool(**kwargs):
            received_kwargs.update(kwargs)
            return "ok"

        result = await async_tool(search_method="by_name", search_term="Player")

        assert result == "ok"
        assert received_kwargs == {
            "search_method": "by_name",
            "search_term": "Player"
        }

    @pytest.mark.asyncio
    async def test_async_function_mixed_params(self):
        """Async function handles mixed camelCase and snake_case."""
        received_kwargs = {}

        @normalize_params
        async def async_tool(**kwargs):
            received_kwargs.update(kwargs)
            return "ok"

        result = await async_tool(searchMethod="by_name", search_term="Player", pageSize=50)

        assert result == "ok"
        assert received_kwargs == {
            "search_method": "by_name",
            "search_term": "Player",
            "page_size": 50
        }

    @pytest.mark.asyncio
    async def test_async_function_conflict_prefers_snake_case(self):
        """When both conventions provided, snake_case wins."""
        received_kwargs = {}

        @normalize_params
        async def async_tool(**kwargs):
            received_kwargs.update(kwargs)
            return "ok"

        # Both searchMethod and search_method provided
        result = await async_tool(searchMethod="by_id", search_method="by_name")

        assert result == "ok"
        # snake_case value should win
        assert received_kwargs["search_method"] == "by_name"


class TestNormalizeParamsPreservesFunction:
    """Tests that normalize_params preserves function metadata."""

    def test_preserves_function_name(self):
        """Decorated function keeps its original name."""
        @normalize_params
        def my_tool(**kwargs):
            pass

        assert my_tool.__name__ == "my_tool"

    def test_preserves_docstring(self):
        """Decorated function keeps its docstring."""
        @normalize_params
        def my_tool(**kwargs):
            """This is my docstring."""
            pass

        assert my_tool.__doc__ == "This is my docstring."

    def test_preserves_positional_args(self):
        """Positional args are passed through unchanged."""
        received_args = []
        received_kwargs = {}

        @normalize_params
        def my_tool(ctx, *args, **kwargs):
            received_args.extend(args)
            received_kwargs.update(kwargs)
            return ctx

        result = my_tool("context_value", "arg1", "arg2", searchMethod="by_name")

        assert result == "context_value"
        assert received_args == ["arg1", "arg2"]
        assert received_kwargs == {"search_method": "by_name"}
