"""Read the serialized objects in a regular Unity text Prefab."""

from __future__ import annotations

import json
import math
import re
import struct
from collections import defaultdict
from dataclasses import dataclass, replace
from typing import Any, Iterator

import yaml


class PrefabDiffError(ValueError):
    """The input does not describe a supported text Prefab."""


_UNITY_HEADER = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:"]
_DOCUMENT_HEADER = re.compile(
    r"^---[ \t]+!u!(?P<class_id>[0-9]+)[ \t]+&(?P<file_id>-?[1-9][0-9]{0,18})"
    r"(?P<stripped>[ \t]+stripped)?[ \t]*\r?$",
    re.MULTILINE,
)
_DECIMAL_INT = re.compile(r"^[+-]?(?:0|[1-9][0-9]*)$")
_DECIMAL_FLOAT = re.compile(
    r"^[+-]?(?:(?:[0-9]+\.[0-9]*|\.[0-9]+)(?:[eE][+-]?[0-9]+)?|"
    r"[0-9]+[eE][+-]?[0-9]+)$"
)
_HEX_FLOAT = re.compile(r"^0[xX]([0-9a-fA-F]{8}(?:[0-9a-fA-F]{8})?)(?:\([^()\r\n]*\))?$")
_YAML_TAG = "tag:yaml.org,2002:"
_HEX_FLOAT_TAG = "!unity-hex-float"
_TEXT_FIELDS = {"m_Name", "m_TagString", "m_EditorClassIdentifier", "fileID", "guid"}
_MISSING = object()
_MAX_CHANGE_OUTPUT_BYTES = 256 * 1024
_TRANSFORM_VECTORS = {"m_LocalPosition", "m_LocalRotation", "m_LocalScale", "m_LocalEulerAnglesHint"}
_RECT_TRANSFORM_VECTORS = _TRANSFORM_VECTORS | {
    "m_AnchorMin", "m_AnchorMax", "m_AnchoredPosition", "m_SizeDelta", "m_Pivot",
}


@dataclass(frozen=True)
class _Number:
    value: int | float
    spelling: str
    hexadecimal: bool = False
    float32: bytes | None = None


class _PrefabLoader(yaml.SafeLoader):
    # Unity string fields such as m_Name can include plain words like "Off".
    # Preserve number spellings and keep words like "On" and "Off" as strings.

    yaml_implicit_resolvers = {
        key: [entry for entry in entries if entry[0] == _YAML_TAG + "null"]
        for key, entries in yaml.SafeLoader.yaml_implicit_resolvers.items()
    }
    _depth = 0

    def compose_node(self, parent: yaml.Node | None, index: Any) -> yaml.Node:
        event = self.peek_event()
        if event.anchor is not None or getattr(event, "tag", None) is not None:
            raise PrefabDiffError("YAML anchors, aliases, and custom tags inside Unity documents are unsupported.")
        if self._depth >= 100:
            raise PrefabDiffError("Unity YAML exceeds the supported field nesting depth.")
        self._depth += 1
        try:
            return super().compose_node(parent, index)
        finally:
            self._depth -= 1

    def construct_mapping(self, node: yaml.MappingNode, deep: bool = False) -> dict[str, Any]:
        fields: dict[str, Any] = {}
        for key, value in node.value:
            if not isinstance(key, yaml.ScalarNode):
                raise PrefabDiffError("Unity YAML field names must be scalars.")
            if key.value in fields:
                raise PrefabDiffError("Unity YAML contains a duplicate field.")
            # Preserve names, GUIDs, and 64-bit fileIDs even when they look numeric.
            fields[key.value] = (
                value.value
                if key.value in _TEXT_FIELDS and isinstance(value, yaml.ScalarNode)
                else self.construct_object(value, deep=deep)
            )
        return fields


def _decimal_int(loader: _PrefabLoader, node: yaml.ScalarNode) -> _Number:
    text = loader.construct_scalar(node)
    return _Number(int(text, 10), text)


def _decimal_float(loader: _PrefabLoader, node: yaml.ScalarNode) -> _Number:
    text = loader.construct_scalar(node)
    value = float(text)
    if not math.isfinite(value):
        raise PrefabDiffError("Unity YAML contains an out-of-range number.")
    return _Number(value, text)


def _hex_float(loader: _PrefabLoader, node: yaml.ScalarNode) -> _Number | str:
    text = loader.construct_scalar(node)
    match = _HEX_FLOAT.fullmatch(text)
    assert match is not None  # matched by the implicit YAML resolver
    bits = bytes.fromhex(match[1])
    # Unity uses the IEEE bits and treats the decimal in parentheses as a note.
    value, = struct.unpack(">f" if len(bits) == 4 else ">d", bits)
    return _Number(value, text, hexadecimal=True) if math.isfinite(value) else text


_PrefabLoader.add_implicit_resolver(_YAML_TAG + "int", _DECIMAL_INT, "+-0123456789")
_PrefabLoader.add_implicit_resolver(_YAML_TAG + "float", _DECIMAL_FLOAT, "+-.0123456789")
_PrefabLoader.add_implicit_resolver(_YAML_TAG + "bool", re.compile(r"^(?:true|false)$"), "tf")
_PrefabLoader.add_implicit_resolver(_HEX_FLOAT_TAG, _HEX_FLOAT, "0")
_PrefabLoader.add_constructor(_YAML_TAG + "int", _decimal_int)
_PrefabLoader.add_constructor(_YAML_TAG + "float", _decimal_float)
_PrefabLoader.add_constructor(_HEX_FLOAT_TAG, _hex_float)


def _mark_transform_vectors(fields: dict[str, Any], vector_fields: set[str]) -> None:
    # Only these built-in fields give us a reliable float32 type. A script can
    # use the same field names for strings and must keep its saved spellings.
    for name in vector_fields:
        vector = fields.get(name)
        if not isinstance(vector, dict):
            continue
        for axis, number in vector.items():
            if isinstance(number, _Number) and axis in {"x", "y", "z", "w"}:
                try:
                    bits = struct.pack(">f", number.value)
                except (OverflowError, struct.error):
                    continue
                vector[axis] = replace(number, float32=bits)


@dataclass(frozen=True)
class _Document:
    file_id: str
    class_id: str
    component: str
    fields: dict[str, Any]


@dataclass(frozen=True)
class _Prefab:
    documents: dict[str, _Document]
    owners: dict[str, str]
    parents: dict[str, str]
    names: dict[str, str]

    def path_to(self, owner: str) -> str:
        names = []
        while owner != "0":
            names.append(self.names[owner])
            owner = self.parents[owner]
        return "/".join(reversed(names))


def _parse_documents(text: str) -> dict[str, _Document]:
    text = text.removeprefix("\ufeff")
    headers = list(_DOCUMENT_HEADER.finditer(text))
    preamble = (
        [line.strip() for line in text[:headers[0].start()].splitlines()
         if line.strip() and not line.lstrip().startswith("#")]
        if headers else []
    )
    if preamble != _UNITY_HEADER or "\x00" in text:
        raise PrefabDiffError("Expected a Unity text Prefab with standard YAML headers.")

    documents: dict[str, _Document] = {}
    for position, header in enumerate(headers):
        file_id = header["file_id"]
        class_id = header["class_id"]
        if header["stripped"] or class_id in {"1001", "1001480554"}:
            raise PrefabDiffError("Nested Prefabs and Prefab Variants are unsupported.")
        if file_id in documents:
            raise PrefabDiffError(f"Unity YAML contains duplicate document fileID {file_id}.")

        end = headers[position + 1].start() if position + 1 < len(headers) else len(text)
        try:
            body = yaml.load(text[header.end():end], Loader=_PrefabLoader)
        except (yaml.YAMLError, ValueError) as exc:
            if isinstance(exc, PrefabDiffError):
                raise
            raise PrefabDiffError(f"Invalid Unity YAML in document {file_id}.") from exc
        if not isinstance(body, dict) or len(body) != 1:
            raise PrefabDiffError(f"Document {file_id} must contain one Unity object mapping.")
        component, fields = next(iter(body.items()))
        if not component or not isinstance(fields, dict):
            raise PrefabDiffError(f"Document {file_id} must contain a Unity object with fields.")
        if component == "PrefabInstance":
            raise PrefabDiffError("Nested Prefabs and Prefab Variants are unsupported.")
        if (class_id, component) == ("4", "Transform"):
            _mark_transform_vectors(fields, _TRANSFORM_VECTORS)
        elif (class_id, component) == ("224", "RectTransform"):
            _mark_transform_vectors(fields, _RECT_TRANSFORM_VECTORS)
        documents[file_id] = _Document(file_id, class_id, component, fields)
    return documents


def _local_file_id(value: Any) -> str | None:
    if isinstance(value, dict) and set(value) == {"fileID"}:
        file_id = value["fileID"]
        if isinstance(file_id, str) and _DECIMAL_INT.fullmatch(file_id):
            return file_id
    return None


def _read_prefab(text: str) -> _Prefab:
    documents = _parse_documents(text)
    game_objects = {
        file_id: document for file_id, document in documents.items()
        if document.class_id == "1" and document.component == "GameObject"
    }
    owners: dict[str, str] = {}
    transforms: dict[str, str] = {}
    parent_transforms: dict[str, str] = {}
    for file_id, document in documents.items():
        if file_id in game_objects:
            owners[file_id] = file_id
            continue

        owner = _local_file_id(document.fields.get("m_GameObject"))
        if owner is None or owner not in game_objects:
            raise PrefabDiffError(f"Document {file_id} does not reference a local GameObject.")
        owners[file_id] = owner
        if (document.class_id, document.component) not in {("4", "Transform"), ("224", "RectTransform")}:
            continue

        parent = _local_file_id(document.fields.get("m_Father"))
        if parent is None or owner in parent_transforms:
            raise PrefabDiffError(f"Invalid or duplicate Transform for GameObject {owner}.")
        transforms[file_id] = owner
        parent_transforms[owner] = parent

    if game_objects.keys() != parent_transforms.keys():
        raise PrefabDiffError("Every GameObject in a Prefab must have a Transform.")

    children: dict[str, list[str]] = defaultdict(list)
    parents: dict[str, str] = {}
    for owner, parent in parent_transforms.items():
        if parent != "0" and parent not in transforms:
            raise PrefabDiffError(f"GameObject {owner} has a missing parent Transform.")
        # No Unity object has fileID 0, so use it as the parent of the root.
        parent_owner = transforms[parent] if parent != "0" else "0"
        parents[owner] = parent_owner
        children[parent_owner].append(owner)

    roots = children["0"]
    if len(roots) != 1:
        raise PrefabDiffError("Expected one root GameObject in a regular Prefab.")
    names: dict[str, str] = {}
    pending = roots.copy()
    while pending:
        owner = pending.pop()
        name = game_objects[owner].fields.get("m_Name")
        if not isinstance(name, str):
            raise PrefabDiffError(f"GameObject {owner} has no serialized name.")
        names[owner] = name
        pending.extend(children[owner])
    if len(names) != len(game_objects):
        raise PrefabDiffError("Prefab GameObject hierarchy contains a cycle.")
    return _Prefab(documents, owners, parents, names)


def _is_reference(value: dict[str, Any]) -> bool:
    return "fileID" in value and value.keys() <= {"fileID", "guid", "type"}


def _same_value(before: Any, after: Any) -> bool:
    # Python considers True equal to 1. Keep distinct YAML scalar types visible.
    if isinstance(before, bool) != isinstance(after, bool):
        return False
    if isinstance(before, _Number) and isinstance(after, _Number):
        if before.float32 is not None and after.float32 is not None:
            return before.float32 == after.float32
        return before.spelling == after.spelling
    if isinstance(before, list) and isinstance(after, list):
        return len(before) == len(after) and all(_same_value(a, b) for a, b in zip(before, after))
    if isinstance(before, dict) and isinstance(after, dict):
        return before.keys() == after.keys() and all(_same_value(before[key], after[key]) for key in before)
    return before == after


def _report_value(value: Any, other: Any) -> Any:
    if isinstance(value, _Number):
        if value.float32 is None and (
            value.hexadecimal or (
                isinstance(other, _Number) and value.value == other.value and value.spelling != other.spelling
            )
        ):
            return value.spelling
        return value.value
    if isinstance(value, dict):
        matching = other if isinstance(other, dict) else {}
        return {key: _report_value(item, matching.get(key)) for key, item in value.items()}
    if isinstance(value, list):
        matching = other if isinstance(other, list) else []
        return [_report_value(item, matching[index] if index < len(matching) else None)
                for index, item in enumerate(value)]
    return None if value is _MISSING else value


def _field_changes(
    before: dict[str, Any], after: dict[str, Any], prefix: str = ""
) -> Iterator[tuple[str, Any, Any]]:
    for key in sorted(before.keys() | after.keys()):
        old_value = before.get(key, _MISSING)
        new_value = after.get(key, _MISSING)
        path = f"{prefix}{key}"
        if (isinstance(old_value, dict) and isinstance(new_value, dict)
                and not (_is_reference(old_value) or _is_reference(new_value))):
            yield from _field_changes(old_value, new_value, f"{path}.")
        elif not _same_value(old_value, new_value):
            yield path, old_value, new_value


def compare_prefabs(before: str, after: str, *, max_changes: int = 200) -> dict[str, Any]:
    """Compare two saved copies of the same regular Prefab by Unity fileID."""
    if max_changes < 1:
        raise ValueError("max_changes must be at least 1.")
    try:
        old = _read_prefab(before)
    except PrefabDiffError as exc:
        raise PrefabDiffError(f"HEAD: {exc}") from exc
    try:
        new = _read_prefab(after)
    except PrefabDiffError as exc:
        raise PrefabDiffError(f"Working tree: {exc}") from exc

    changes: list[dict[str, Any]] = []
    total = 0
    remaining_bytes = _MAX_CHANGE_OUTPUT_BYTES
    for file_id in sorted(old.documents.keys() | new.documents.keys(), key=int):
        previous = old.documents.get(file_id)
        current = new.documents.get(file_id)
        if previous and current and (previous.class_id, previous.component) != (current.class_id, current.component):
            pairs = [(previous, None), (None, current)]
        else:
            pairs = [(previous, current)]

        for left, right in pairs:
            prefab, document = (new, right) if right else (old, left)
            assert document is not None
            owner = prefab.owners[document.file_id]
            fields = _field_changes(left.fields if left else {}, right.fields if right else {})
            for field, old_value, new_value in fields:
                total += 1
                if total > max_changes or remaining_bytes <= 0:
                    continue
                kind = "added" if old_value is _MISSING else "removed" if new_value is _MISSING else "modified"
                change = {
                    "object": prefab.path_to(owner),
                    "gameObjectFileId": owner,
                    "component": document.component,
                    "componentFileId": document.file_id,
                    "field": field,
                    "oldValue": _report_value(old_value, new_value),
                    "newValue": _report_value(new_value, old_value),
                    "kind": kind,
                }
                # Count JSON escaping as well as the values themselves.
                remaining_bytes -= len(json.dumps(change)) + 2
                if remaining_bytes >= 0:
                    changes.append(change)
    return {"changes": changes, "totalChanges": total, "truncated": len(changes) < total}
