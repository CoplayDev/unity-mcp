---
title: answer_dialog
sidebar_label: answer_dialog
description: "Reads or answers a modal dialog blocking the Unity Editor."
---

# `answer_dialog`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.answer_dialog`

## Description

Reads or answers a modal dialog blocking the Unity Editor. A modal blocks every other tool until answered. Omit button to read the dialog; pass button to press it.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `button` | `str \| None` | — | Exact button label from the dialog's buttons list. Omit to read without answering. |
| `expect_title` | `str \| None` | — | Expected dialog title; the press is refused if a different dialog is open. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

