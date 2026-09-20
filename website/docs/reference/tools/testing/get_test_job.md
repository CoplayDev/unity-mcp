---
title: get_test_job
sidebar_label: get_test_job
description: "Polls an async Unity test job by job_id."
---

# `get_test_job`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `testing` &nbsp;·&nbsp; **Module:** `services.tools.run_tests`

## Description

Polls an async Unity test job by job_id.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `job_id` | `str` | yes | Job id returned by run_tests |
| `include_failed_tests` | `bool` | — | Include details for failed/skipped tests only (default: false) |
| `include_details` | `bool` | — | Include details for all tests (default: false) |
| `wait_timeout` | `int \| None` | — | If set, wait up to this many seconds for tests to complete before returning. Reduces polling frequency and avoids client-side loop detection. Recommended: 30-60 seconds. Returns immediately if tests complete sooner. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
### Wait for a run to finish

> Wait for the test job I just started and show the failures.

```json
{
  "job_id": "<job_id from run_tests>",
  "wait_timeout": 60,
  "include_failed_tests": true
}
```

With `wait_timeout`, the server polls Unity every 2 seconds and returns as soon as `status` is `succeeded`, `failed` or `cancelled` — or after 60 s with the current progress, in which case call it again. This avoids a tight client-side polling loop.

### Check progress without waiting

> How far along is the test run?

```json
{
  "job_id": "<job_id from run_tests>"
}
```

Returns straight away. `data.progress` has `completed` / `total`, the test currently running, and `failures_so_far`; `data.result.summary` appears once the job has finished.

### Get details for every test

> Show me the result of every test, not just the failures.

```json
{
  "job_id": "<job_id from run_tests>",
  "include_details": true
}
```

`include_details` returns all tests; `include_failed_tests` returns only failed and skipped ones, which keeps the response small on large suites.
<!-- examples:end -->

