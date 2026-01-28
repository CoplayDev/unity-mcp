"""Tests for parameter aliasing using Pydantic AliasChoices.

P1-1.5 uses Pydantic's AliasChoices with Field(validation_alias=...) to accept
both snake_case and camelCase parameter names at the FastMCP validation layer.
"""
import pytest
from pydantic import AliasChoices, BaseModel, Field
from typing import Annotated


class TestAliasChoicesPattern:
    """Tests demonstrating the AliasChoices pattern for parameter aliasing."""

    def test_alias_choices_accepts_snake_case(self):
        """AliasChoices accepts snake_case parameter names."""

        class TestModel(BaseModel):
            search_term: Annotated[
                str,
                Field(validation_alias=AliasChoices("search_term", "searchTerm"))
            ]

        m = TestModel.model_validate({"search_term": "test"})
        assert m.search_term == "test"

    def test_alias_choices_accepts_camel_case(self):
        """AliasChoices accepts camelCase parameter names."""

        class TestModel(BaseModel):
            search_term: Annotated[
                str,
                Field(validation_alias=AliasChoices("search_term", "searchTerm"))
            ]

        m = TestModel.model_validate({"searchTerm": "test"})
        assert m.search_term == "test"

    def test_snake_case_takes_precedence(self):
        """When both are provided, the first alias choice wins."""

        class TestModel(BaseModel):
            search_term: Annotated[
                str,
                Field(validation_alias=AliasChoices("search_term", "searchTerm"))
            ]

        # First matching alias wins
        m = TestModel.model_validate({"search_term": "snake", "searchTerm": "camel"})
        assert m.search_term == "snake"

    def test_alias_choices_with_default_value(self):
        """AliasChoices works with optional parameters that have defaults."""

        class TestModel(BaseModel):
            search_method: Annotated[
                str,
                Field(
                    default="by_name",
                    validation_alias=AliasChoices("search_method", "searchMethod")
                )
            ]

        # Default is used when not provided
        m1 = TestModel.model_validate({})
        assert m1.search_method == "by_name"

        # snake_case overrides default
        m2 = TestModel.model_validate({"search_method": "by_tag"})
        assert m2.search_method == "by_tag"

        # camelCase overrides default
        m3 = TestModel.model_validate({"searchMethod": "by_id"})
        assert m3.search_method == "by_id"

    def test_alias_choices_with_optional_none(self):
        """AliasChoices works with Optional parameters defaulting to None."""

        class TestModel(BaseModel):
            page_size: Annotated[
                int | None,
                Field(
                    default=None,
                    validation_alias=AliasChoices("page_size", "pageSize")
                )
            ]

        # None default
        m1 = TestModel.model_validate({})
        assert m1.page_size is None

        # snake_case
        m2 = TestModel.model_validate({"page_size": 50})
        assert m2.page_size == 50

        # camelCase
        m3 = TestModel.model_validate({"pageSize": 100})
        assert m3.page_size == 100

    def test_alias_choices_with_bool_coercion(self):
        """AliasChoices works with boolean parameters."""

        class TestModel(BaseModel):
            include_inactive: Annotated[
                bool | str | None,
                Field(
                    default=None,
                    validation_alias=AliasChoices("include_inactive", "includeInactive")
                )
            ]

        # camelCase with bool
        m1 = TestModel.model_validate({"includeInactive": True})
        assert m1.include_inactive is True

        # snake_case with string (common from JSON)
        m2 = TestModel.model_validate({"include_inactive": "true"})
        assert m2.include_inactive == "true"  # Note: string coercion happens in tool

    def test_alias_choices_multiple_params(self):
        """Multiple parameters can each have AliasChoices."""

        class TestModel(BaseModel):
            search_term: Annotated[
                str,
                Field(validation_alias=AliasChoices("search_term", "searchTerm"))
            ]
            search_method: Annotated[
                str,
                Field(
                    default="by_name",
                    validation_alias=AliasChoices("search_method", "searchMethod")
                )
            ]
            page_size: Annotated[
                int | None,
                Field(
                    default=None,
                    validation_alias=AliasChoices("page_size", "pageSize")
                )
            ]

        # Mix of snake_case and camelCase
        m = TestModel.model_validate({
            "searchTerm": "Player",
            "search_method": "by_tag",
            "pageSize": 25
        })

        assert m.search_term == "Player"
        assert m.search_method == "by_tag"
        assert m.page_size == 25
