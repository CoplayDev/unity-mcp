# Unity NL/T Editing Suite — CI Agent Contract

You are running inside CI for the `unity-mcp` repo. Use only the tools allowed by the workflow. Work autonomously; do not prompt the user. Do NOT spawn subagents.

**Print this once, verbatim, early in the run:**
AllowedTools: Write,Bash(printf:*),Bash(echo:*),Bash(scripts/nlt-revert.sh:*),mcp__unity__manage_editor,mcp__unity__list_resources,mcp__unity__read_resource,mcp__unity__apply_text_edits,mcp__unity__script_apply_edits,mcp__unity__validate_script,mcp__unity__find_in_file,mcp__unity__read_console,mcp__unity__get_sha

---

## Mission
1) Pick target file (prefer):
   - `unity://path/Assets/Scripts/LongUnityScriptClaudeTest.cs`
2) Execute **all** NL/T tests in order using minimal, precise edits.
3) Validate each edit with `mcp__unity__validate_script(level:"standard")`.
4) **Report**: write one `<testcase>` XML fragment per test to `reports/<TESTID>_results.xml`. Do **not** read or edit `$JUNIT_OUT`.
5) **Restore** the file after each test using the OS‑level helper (fast), not a full‑file text write.

---

## Environment & Paths (CI)
- Always pass: `project_root: "TestProjects/UnityMCPTests"` and `ctx: {}` on list/read/edit/validate.
- **Canonical URIs only**:
  - Primary: `unity://path/Assets/...` (never embed `project_root` in the URI)
  - Relative (when supported): `Assets/...`
- File paths for the helper script are workspace‑relative:
  - `TestProjects/UnityMCPTests/Assets/...`

CI provides:
- `$JUNIT_OUT=reports/junit-nl-suite.xml` (pre‑created; leave alone)
- `$MD_OUT=reports/junit-nl-suite.md` (synthesized from JUnit)
- Helper script: `scripts/nlt-revert.sh` (snapshot/restore)

---

## Tool Mapping
- **Anchors/regex/structured**: `mcp__unity__script_apply_edits`
  - Allowed ops: `anchor_insert`, `replace_range`, `regex_replace` (no overlapping ranges within a single call)
- **Precise ranges / atomic batch**: `mcp__unity__apply_text_edits` (non‑overlapping ranges)
  - Multi‑span batches are computed from the same fresh read and sent atomically by default.
  - Prefer `options.applyMode:"atomic"` when passing options for multiple spans; for single‑span, sequential is fine.
- **Hash-only**: `mcp__unity__get_sha` — returns `{sha256,lengthBytes,lastModifiedUtc}` without file body
- **Validation**: `mcp__unity__validate_script(level:"standard")`
  - For edits, you may pass `options.validate`:
    - `standard` (default): full‑file delimiter balance checks.
    - `relaxed`: scoped checks for interior, non‑structural text edits; do not use for header/signature/brace‑touching changes.
- **Reporting**: `Write` small XML fragments to `reports/*_results.xml`
- **Editor state/flush**: `mcp__unity__manage_editor` (use sparingly; no project mutations)
- **Console readback**: `mcp__unity__read_console` (INFO capture only; do not assert in place of `validate_script`)
- **Snapshot/Restore**: `Bash(scripts/nlt-revert.sh:*)`
  - For `script_apply_edits`: use `name` + workspace‑relative `path` only (e.g., `name="LongUnityScriptClaudeTest"`, `path="Assets/Scripts"`). Do not pass `unity://...` URIs as `path`.
  - For `apply_text_edits` / `read_resource`: use the URI form only (e.g., `uri="unity://path/Assets/Scripts/LongUnityScriptClaudeTest.cs"`). Do not concatenate `Assets/` with a `unity://...` URI.
  - Never call generic Bash like `mkdir`; the revert helper creates needed directories. Use only `scripts/nlt-revert.sh` for snapshot/restore.
  - If you believe a directory is missing, you are mistaken: the workflow pre-creates it and the snapshot helper creates it if needed. Do not attempt any Bash other than scripts/nlt-revert.sh:*.

### Structured edit ops (required usage)

# Insert a helper RIGHT BEFORE the final class brace (NL‑3, T‑D)
1) Prefer `script_apply_edits` with a regex capture on the final closing brace:
```json
{"op":"regex_replace",
 "pattern":"(?s)(\\r?\\n\\s*\\})\\s*$",
 "replacement":"\\n    // Tail test A\\n    // Tail test B\\n    // Tail test C\\1"}

2) If the server returns `unsupported` (op not available) or `missing_field` (op‑specific), FALL BACK to
   `apply_text_edits`:
   - Find the last `}` in the file (class closing brace) by scanning from end.
   - Insert the three comment lines immediately before that index with one non‑overlapping range.

# Insert after GetCurrentTarget (T‑A/T‑E)
- Use `script_apply_edits` with:
```json
{"op":"anchor_insert","afterMethodName":"GetCurrentTarget","text":"private int __TempHelper(int a,int b)=>a+b;\\n"}
```

# Delete the temporary helper (T‑A/T‑E)
- Prefer structured delete:
  - Use `script_apply_edits` with `{ "op":"delete_method", "className":"LongUnityScriptClaudeTest", "methodName":"PrintSeries" }` (or `__TempHelper` for T‑A).
- If structured delete is unavailable, fall back to `apply_text_edits` with a single `replace_range` spanning the exact method block (bounds computed from a fresh read); avoid whole‑file regex deletes.

# T‑B (replace method body)
- Use `mcp__unity__apply_text_edits` with a single `replace_range` strictly inside the `HasTarget` braces.
- Compute start/end from a fresh `read_resource` at test start. Do not edit signature or header.
- On `{status:"stale_file"}` retry once with the server-provided hash; if absent, re-read once and retry.
- On `bad_request`: write the testcase with `<failure>…</failure>`, restore, and continue to next test.
- On `missing_field`: FALL BACK per above; if the fallback also returns `unsupported` or `bad_request`, then fail as above.
> Don’t use `mcp__unity__create_script`. Avoid the header/`using` region entirely.

Span formats for `apply_text_edits`:
- Prefer LSP ranges (0‑based): `{ "range": { "start": {"line": L, "character": C}, "end": {…} }, "newText": "…" }`
- Explicit fields are 1‑based: `{ "startLine": L1, "startCol": C1, "endLine": L2, "endCol": C2, "newText": "…" }`
- SDK preflights overlap after normalization; overlapping non‑zero spans → `{status:"overlap"}` with conflicts and no file mutation.
- Optional debug: pass `strict:true` to reject explicit 0‑based fields (else they are normalized and a warning is emitted).
- Apply mode guidance: router defaults to atomic for multi‑span; you can explicitly set `options.applyMode` if needed.

---

## Output Rules (JUnit fragments only)
- For each test, create **one** file: `reports/<TESTID>_results.xml` containing exactly a single `<testcase ...> ... </testcase>`.
 Put human-readable lines (PLAN/PROGRESS/evidence) **inside** `<system-out><![CDATA[ ... ]]></system-out>`.
   - If content contains `]]>`, split CDATA: replace `]]>` with `]]]]><![CDATA[>`.
- Evidence windows only (±20–40 lines). If showing a unified diff, cap at 100 lines and note truncation.
- **Never** open/patch `$JUNIT_OUT` or `$MD_OUT`; CI merges fragments and synthesizes Markdown.
  - Write destinations must match: `^reports/[A-Za-z0-9._-]+_results\.xml$`
  - Snapshot files must live under `reports/_snapshots/`
  - Reject absolute paths and any path containing `..`
  - Reject control characters and line breaks in filenames; enforce UTF‑8
  - Cap basename length to ≤64 chars; cap any path segment to ≤100 and total path length to ≤255
  - Bash(printf|echo) must write to stdout only. Do not use shell redirection, here‑docs, or `tee` to create/modify files. The only allowed FS mutation is via `scripts/nlt-revert.sh`.

**Example fragment**
```xml
<testcase classname="UnityMCP.NL-T" name="NL-1. Method replace/insert/delete">
  <system-out><![CDATA[
PLAN: NL-0,NL-1,NL-2,NL-3,NL-4,T-A,T-B,T-C,T-D,T-E,T-F,T-G,T-H,T-I,T-J (len=15)
PROGRESS: 2/15 completed
pre_sha=<...>
... evidence windows ...
VERDICT: PASS
]]></system-out>
</testcase>

```

Note: Emit the PLAN line only in NL‑0 (do not repeat it for later tests).


### Fast Restore Strategy (OS‑level)

- Snapshot once at NL‑0, then restore after each test via the helper.
- Snapshot (once after confirming the target):
  ```bash
  scripts/nlt-revert.sh snapshot "TestProjects/UnityMCPTests/Assets/Scripts/LongUnityScriptClaudeTest.cs" "reports/_snapshots/LongUnityScriptClaudeTest.cs.baseline"
  ```
- Log `snapshot_sha=...` printed by the script.
- Restore (after each mutating test):
  ```bash
  scripts/nlt-revert.sh restore "TestProjects/UnityMCPTests/Assets/Scripts/LongUnityScriptClaudeTest.cs" "reports/_snapshots/LongUnityScriptClaudeTest.cs.baseline"
  ```
- Then `read_resource` to confirm and (optionally) `validate_script(level:"standard")`.
- If the helper fails: fall back once to a guarded full‑file restore using the baseline bytes; then continue.

### Guarded Write Pattern (for edits, not restores)

- Before any mutation: `res = mcp__unity__read_resource(uri)`; `pre_sha = sha256(res.bytes)`.
- Write with `precondition_sha256 = pre_sha` on `apply_text_edits`/`script_apply_edits`.
- To compute `pre_sha` without reading file contents, you may instead call `mcp__unity__get_sha(uri).sha256`.
- On `{status:"stale_file"}`:
  - Retry once using the server-provided hash (e.g., `data.current_sha256` or `data.expected_sha256`, per API schema).
  - If absent, one re-read then a final retry. No loops.
- After success: immediately re-read via `res2 = mcp__unity__read_resource(uri)` and set `pre_sha = sha256(res2.bytes)` before any further edits in the same test.
- Prefer anchors (`script_apply_edits`) for end-of-class / above-method insertions. Keep edits inside method bodies. Avoid header/using.
 
**On non‑JSON/transport errors (timeout, EOF, connection closed):**
- Write `reports/<TESTID>_results.xml` with a `<testcase>` that includes a `<failure>` or `<error>` node capturing the error text.
- Run the OS restore via `scripts/nlt-revert.sh restore …`.
- Continue to the next test (do not abort).

**If any write returns `bad_request`, or `unsupported` after a fallback attempt:**
- Write `reports/<TESTID>_results.xml` with a `<testcase>` that includes a `<failure>` node capturing the server error, include evidence, and end with `VERDICT: FAIL`.
- Run `scripts/nlt-revert.sh restore ...` and continue to the next test.
### Execution Order (fixed)

- Run exactly: NL-0, NL-1, NL-2, NL-3, NL-4, T-A, T-B, T-C, T-D, T-E, T-F, T-G, T-H, T-I, T-J (15 total).
- Before NL-1..T-J: Bash(scripts/nlt-revert.sh:restore "<target>" "reports/_snapshots/LongUnityScriptClaudeTest.cs.baseline") IF the baseline exists; skip for NL-0.
- NL-0 must include the PLAN line (len=15).
- After each testcase, include `PROGRESS: <k>/15 completed`.


### Test Specs (concise)

- NL‑0. Sanity reads — Tail ~120; ±40 around `Update()`. Then snapshot via helper.
- NL‑1. Replace/insert/delete — `HasTarget → return currentTarget != null;`; insert `PrintSeries()` after `GetCurrentTarget` logging "1,2,3"; verify; delete `PrintSeries()`; restore.
- NL‑2. Anchor comment — Insert `// Build marker OK` above `public void Update(...)`; restore.
- NL‑3. End‑of‑class — Insert `// Tail test A/B/C` (3 lines) before final brace; restore.
- NL‑4. Compile trigger — Record INFO only.

### T‑A. Anchor insert (text path) — Insert helper after `GetCurrentTarget`; verify; delete via `regex_replace`; restore.
### T‑B. Replace body — Single `replace_range` inside `HasTarget`; restore.
- Options: pass {"validate":"relaxed"} for interior one-line edits.
### T‑C. Header/region preservation — Edit interior of `ApplyBlend`; preserve signature/docs/regions; restore.
- Options: pass {"validate":"relaxed"} for interior one-line edits.
### T‑D. End‑of‑class (anchor) — Insert helper before final brace; remove; restore.
### T‑E. Lifecycle — Insert → update → delete via regex; restore.
### T‑F. Atomic batch — One `mcp__unity__apply_text_edits` call (text ranges only)
  - Compute all three edits from the **same fresh read**:
    1) Two small interior `replace_range` tweaks.
    2) One **end‑of‑class insertion**: find the **index of the final `}`** for the class; create a zero‑width range `[idx, idx)` and set `replacement` to the 3‑line comment block.
  - Send all three ranges in **one call**, sorted **descending by start index** to avoid offset drift.
  - Expect all‑or‑nothing semantics; on `{status:"overlap"}` or `{status:"bad_request"}`, write the testcase fragment with `<failure>…</failure>`, **restore**, and continue.
  - Options: pass {"applyMode":"atomic"} to enforce all‑or‑nothing.
- T‑G. Path normalization — Make the same edit with `unity://path/Assets/...` then `Assets/...`. Without refreshing `precondition_sha256`, the second attempt returns `{stale_file}`; retry with the server-provided hash to confirm both forms resolve to the same file.

### T-H. Validation (standard)
- Restore baseline (helper call above).
- Perform a harmless interior tweak (or none), then MUST call:
  mcp__unity__validate_script(level:"standard")
- Write the validator output to system-out; VERDICT: PASS if standard is clean, else include <failure> with the validator message and continue.

### T-I. Failure surfaces (expected)
- Restore baseline.
- (1) OVERLAP:
  * Fresh read of file; compute two interior ranges that overlap inside HasTarget.
  * Prefer LSP ranges (0‑based) or explicit 1‑based fields; ensure both spans come from the same snapshot.
  * Single mcp__unity__apply_text_edits call with both ranges.
  * Expect `{status:"overlap"}` (SDK preflight) → record as PASS; else FAIL. Restore.
- (2) STALE_FILE:
  * Fresh read → pre_sha.
  * Make a tiny legit edit with pre_sha; success.
  * Attempt another edit reusing the OLD pre_sha.
  * Expect {status:"stale_file"} → record as PASS; else FAIL. Re-read to refresh, restore.

### Per‑test error handling and recovery
- For each test (NL‑0..T‑J), use a try/finally pattern:
  - Always write a testcase fragment and perform restore in finally, even when tools return error payloads.
  - try: run the test steps; always write `reports/<ID>_results.xml` with PASS/FAIL/ERROR
  - finally: run Bash(scripts/nlt-revert.sh:restore …baseline) to restore the target file
- On any transport/JSON/tool exception:
  - catch and write a `<testcase>` fragment with an `<error>` node (include the message), then proceed to the next test.
- After NL‑4 completes, proceed directly to T‑A regardless of any earlier validator warnings (do not abort the run).
- (3) USING_GUARD (optional):
  * Attempt a 1-line insert above the first 'using'.
  * Expect {status:"using_guard"} → record as PASS; else note 'not emitted'. Restore.

### T-J. Idempotency
- Restore baseline.
- Repeat a replace_range twice (second call may be noop). Validate standard after each.
- Insert or ensure a tiny comment, then delete it twice (second delete may be noop).
- Restore and PASS unless an error/structural break occurred.


### Status & Reporting

- Safeguard statuses are non‑fatal; record and continue.
- End each testcase `<system-out>` with `VERDICT: PASS` or `VERDICT: FAIL`.