# Unity T Editing Suite — Additive Test Design
You are running inside CI for the `unity-mcp` repo. Use only the tools allowed by the workflow. Work autonomously; do not prompt the user. Do NOT spawn subagents.

**Print this once, verbatim, early in the run:**
AllowedTools: Write,mcp__UnityMCP__manage_editor,mcp__UnityMCP__list_resources,mcp__UnityMCP__read_resource,mcp__UnityMCP__apply_text_edits,mcp__UnityMCP__script_apply_edits,mcp__UnityMCP__validate_script,mcp__UnityMCP__find_in_file,mcp__UnityMCP__read_console,mcp__UnityMCP__get_sha

---

## Mission
1) Pick target file (prefer):
   - `mcpforunity://path/Assets/Scripts/LongUnityScriptClaudeTest.cs`
2) Execute T tests T-A..T-J in order using minimal, precise edits that build on the NL pass state.
3) Validate each edit with `mcp__UnityMCP__validate_script(level:"standard")`.
4) **Report**: write one `<testcase>` XML fragment per test to `reports/<TESTID>_results.xml`. Do **not** read or edit `$JUNIT_OUT`.

**CRITICAL XML FORMAT REQUIREMENTS:**
- Each file must contain EXACTLY one `<testcase>` root element
- NO prologue, epilogue, code fences, or extra characters
- NO markdown formatting or explanations outside the XML
- Use this exact format:

```xml
<testcase name="T-D — End-of-Class Helper" classname="UnityMCP.NL-T">
  <system-out><![CDATA[
(evidence of what was accomplished)
  ]]></system-out>
</testcase>
```

- If test fails, include: `<failure message="reason"/>`
- TESTID must be one of: T-A, T-B, T-C, T-D, T-E, T-F, T-G, T-H, T-I, T-J
5) **NO RESTORATION** - tests build additively on previous state.
6) **STRICT FRAGMENT EMISSION** - After each test, immediately emit a clean XML file under `reports/<TESTID>_results.xml` with exactly one `<testcase>` whose `name` begins with the exact test id. No prologue/epilogue or fences. If the test fails, include a `<failure message="..."/>` and still emit.

---

## Environment & Paths (CI)
- Always pass: `project_root: "TestProjects/UnityMCPTests"` and `ctx: {}` on list/read/edit/validate.
- **Canonical URIs only**:
  - Primary: `mcpforunity://path/Assets/...` (never embed `project_root` in the URI)
  - Relative (when supported): `Assets/...`

CI provides:
- `$JUNIT_OUT=reports/junit-nl-suite.xml` (pre‑created; leave alone)
- `$MD_OUT=reports/junit-nl-suite.md` (synthesized from JUnit)

---

## Transcript Minimization Rules
- Do not restate tool JSON; summarize in ≤ 2 short lines.
- Never paste full file contents. For matches, include only the matched line and ±1 line.
- Prefer `mcp__UnityMCP__find_in_file` for targeting; avoid `mcp__UnityMCP__read_resource` unless strictly necessary. If needed, limit to `head_bytes ≤ 256` or `tail_lines ≤ 10`.
- Per‑test `system-out` ≤ 400 chars: brief status only (no SHA).
- Console evidence: fetch the last 10 lines with `include_stacktrace:false` and include ≤ 3 lines in the fragment.
- Avoid quoting multi‑line diffs; reference markers instead.
— Console scans: perform two reads — last 10 `log/info` lines and up to 3 `error` entries (use `include_stacktrace:false`); include ≤ 3 lines total in the fragment; if no errors, state "no errors".
— Final check is folded into T‑J: perform an errors‑only scan (with `include_stacktrace:false`) and include a single "no errors" line or up to 3 error lines within the T‑J fragment.

---

## Tool Mapping
- **Anchors/regex/structured**: `mcp__UnityMCP__script_apply_edits`
  - Allowed ops: `anchor_insert`, `replace_method`, `insert_method`, `delete_method`, `regex_replace`
  - For `anchor_insert`, always set `"position": "before"` or `"after"`.
- **Precise ranges / atomic batch**: `mcp__UnityMCP__apply_text_edits` (non‑overlapping ranges)
STRICT OP GUARDRAILS
- Do not use `anchor_replace`. Structured edits must be one of: `anchor_insert`, `replace_method`, `insert_method`, `delete_method`, `regex_replace`.
- For multi‑spot textual tweaks in one operation, compute non‑overlapping ranges with `mcp__UnityMCP__find_in_file` and use `mcp__UnityMCP__apply_text_edits`.

- **Hash-only**: `mcp__UnityMCP__get_sha` — returns `{sha256,lengthBytes,lastModifiedUtc}` without file body
- **Validation**: `mcp__UnityMCP__validate_script(level:"standard")`
- **Dynamic targeting**: Use `mcp__UnityMCP__find_in_file` to locate current positions of methods/markers

---

## Additive Test Design Principles

**Key Changes from Reset-Based:**
1. **Dynamic Targeting**: Use `find_in_file` to locate methods/content, never hardcode line numbers
2. **State Awareness**: Each test expects the file state left by the previous test
3. **Content-Based Operations**: Target methods by signature, classes by name, not coordinates
4. **Cumulative Validation**: Ensure the file remains structurally sound throughout the sequence
5. **Composability**: Tests demonstrate how operations work together in real workflows

**State Tracking:**
- Track file SHA after each test (`mcp__UnityMCP__get_sha`) and use it as a precondition
  for `apply_text_edits` in T‑F/T‑G/T‑I to exercise `stale_file` semantics. Do not include SHA values in report fragments.
- Use content signatures (method names, comment markers) to verify expected state
- Validate structural integrity after each major change

---

### T-A. Temporary Helper Lifecycle (Returns to State C)
**Goal**: Test insert → verify → delete cycle for temporary code
**Actions**:
- Find current position of `GetCurrentTarget()` method (may have shifted from NL-2 comment)
- Insert temporary helper: `private int __TempHelper(int a, int b) => a + b;`
- Verify helper method exists and compiles
- Delete helper method via structured delete operation
- **Expected final state**: Return to State C (helper removed, other changes intact)

### Late-Test Editing Rule
- When modifying a method body, use `mcp__UnityMCP__script_apply_edits`. If the method is expression-bodied (`=>`), convert it to a block or replace the whole method definition. After the edit, run `mcp__UnityMCP__validate_script` and rollback on error. Use `//` comments in inserted code.

### T-B. Method Body Interior Edit (Additive State D)
**Goal**: Edit method interior without affecting structure, on modified file
**Actions**:
- Use `find_in_file` to locate current `HasTarget()` method (modified in NL-1)
- Edit method body interior: change return statement to `return true; /* test modification */`
- Validate with `mcp__UnityMCP__validate_script(level:"standard")` for consistency
- Verify edit succeeded and file remains balanced
- **Expected final state**: State C + modified HasTarget() body

### T-C. Different Method Interior Edit (Additive State E)
**Goal**: Edit a different method to show operations don't interfere
**Actions**:
- Locate `ApplyBlend()` method using content search
- Edit interior line to add null check: `if (animator == null) return; // safety check`
- Preserve method signature and structure  
- **Expected final state**: State D + modified ApplyBlend() method

### T-D. End-of-Class Helper (Additive State F)
**Goal**: Add permanent helper method at class end
**Actions**:
- Use smart anchor matching to find current class-ending brace (after NL-3 tail comments)
- Insert permanent helper before class brace: `private void TestHelper() { /* placeholder */ }`
- Validate with `mcp__UnityMCP__validate_script(level:"standard")`
- **IMMEDIATELY** write clean XML fragment to `reports/T-D_results.xml` (no extra text). The `<testcase name>` must start with `T-D`. Include brief evidence in `system-out`.
- **Expected final state**: State E + TestHelper() method before class end

### T-E. Method Evolution Lifecycle (Additive State G)
**Goal**: Insert → modify → finalize a field + companion method
**Actions**:
- Insert field: `private int Counter = 0;`
- Update it: find and replace with `private int Counter = 42; // initialized`
- Add companion method: `private void IncrementCounter() { Counter++; }`
- **Expected final state**: State F + Counter field + IncrementCounter() method

### T-F. Atomic Multi-Edit (Additive State H)
**Goal**: Multiple coordinated edits in single atomic operation
**Actions**:
- Read current file state to compute precise ranges
- Atomic edit combining:
  1. Add comment in `HasTarget()`: `// validated access`  
  2. Add comment in `ApplyBlend()`: `// safe animation`
  3. Add final class comment: `// end of test modifications`
- All edits computed from same file snapshot, applied atomically
- **Expected final state**: State G + three coordinated comments
- After applying the atomic edits, run `validate_script(level:"standard")` and emit a clean fragment to `reports/T-F_results.xml` with a short summary.

### T-G. Path Normalization Test (No State Change)
**Goal**: Verify URI forms work equivalently on modified file
**Actions**:
- Make identical edit using `mcpforunity://path/Assets/Scripts/LongUnityScriptClaudeTest.cs`
- Then using `Assets/Scripts/LongUnityScriptClaudeTest.cs` 
- Second should return `stale_file`, retry with updated SHA
- Verify both URI forms target same file
- **Expected final state**: State H (no content change, just path testing)
- Emit `reports/T-G_results.xml` showing evidence of stale SHA handling.

### T-H. Validation on Modified File (No State Change)
**Goal**: Ensure validation works correctly on heavily modified file
**Actions**:
- Run `validate_script(level:"standard")` on current state
- Verify no structural errors despite extensive modifications
- **Expected final state**: State H (validation only, no edits)
- Emit `reports/T-H_results.xml` confirming validation OK.

### T-I. Failure Surface Testing (No State Change)
**Goal**: Test error handling on real modified file
**Actions**:
- Attempt overlapping edits (should fail cleanly)
- Attempt edit with stale SHA (should fail cleanly) 
- Verify error responses are informative
- **Expected final state**: State H (failed operations don't modify file)
- Emit `reports/T-I_results.xml` capturing error evidence; file must contain one `<testcase>`.

### T-J. Idempotency on Modified File (Additive State I)
**Goal**: Verify operations behave predictably when repeated
**Actions**:
- **Insert (structured)**: `mcp__UnityMCP__script_apply_edits` with:
  `{"op":"anchor_insert","anchor":"// Tail test C","position":"after","text":"\n    // idempotency test marker"}`
- **Insert again** (same op) → expect `no_op: true`.
- **Remove (structured)**: `{"op":"regex_replace","pattern":"(?m)^\\s*// idempotency test marker\\r?\\n?","text":""}`
- **Remove again** (same `regex_replace`) → expect `no_op: true`.
- `mcp__UnityMCP__validate_script(level:"standard")`
- Perform a final console scan for errors/exceptions (errors only, up to 3); include "no errors" if none
- **IMMEDIATELY** write clean XML fragment to `reports/T-J_results.xml` with evidence of both `no_op: true` outcomes and the console result. The `<testcase name>` must start with `T-J`.
- **Expected final state**: State H + verified idempotent behavior

---

## Dynamic Targeting Examples

**Instead of hardcoded coordinates:**
```json
{"startLine": 31, "startCol": 26, "endLine": 31, "endCol": 58}
```

**Use content-aware targeting:**
```json
# Find current method location
find_in_file(pattern: "public bool HasTarget\\(\\)")
# Then compute edit ranges from found position
```

**Method targeting by signature:**
```json
{"op": "replace_method", "className": "LongUnityScriptClaudeTest", "methodName": "HasTarget"}
```

**Anchor-based insertions:**
```json  
{"op": "anchor_insert", "anchor": "private void Update\\(\\)", "position": "before", "text": "// comment"}
```

---

## State Verification Patterns

**After each test:**
1. Verify expected content exists: `find_in_file` for key markers
2. Check structural integrity: `validate_script(level:"standard")`  
3. Update SHA tracking for next test's preconditions
4. Emit a per‑test fragment to `reports/<TESTID>_results.xml` immediately. If the test failed, still write a single `<testcase>` with a `<failure message="..."/>` and evidence in `system-out`.
5. Log cumulative changes in test evidence (keep concise per Transcript Minimization Rules; never paste raw tool JSON)

**Error Recovery:**
- If test fails, log current state but continue (don't restore)
- Next test adapts to actual current state, not expected state
- Demonstrates resilience of operations on varied file conditions

---

## Benefits of Additive Design

1. **Realistic Workflows**: Tests mirror actual development patterns
2. **Robust Operations**: Proves edits work on evolving files, not just pristine baselines  
3. **Composability Validation**: Shows operations coordinate well together
4. **Simplified Infrastructure**: No restore scripts or snapshots needed
5. **Better Failure Analysis**: Failures don't cascade - each test adapts to current reality
6. **State Evolution Testing**: Validates SDK handles cumulative file modifications correctly

This additive approach produces a more realistic and maintainable test suite that better represents actual SDK usage patterns.

---

BAN ON EXTRA TOOLS AND DIRS
- Do not use any tools outside `AllowedTools`. Do not create directories; assume `reports/` exists.

---

## XML Fragment Templates (T-F .. T-J)

Use these skeletons verbatim as a starting point. Replace the bracketed placeholders with your evidence. Ensure each file contains exactly one `<testcase>` element and that the `name` begins with the exact test id.

```xml
<testcase name="T-F — Atomic Multi-Edit" classname="UnityMCP.NL-T">
  <system-out><![CDATA[
Applied 3 non-overlapping edits in one atomic call:
- HasTarget(): added "// validated access"
- ApplyBlend(): added "// safe animation"
- End-of-class: added "// end of test modifications"
validate_script: OK
  ]]></system-out>
</testcase>
```

```xml
<testcase name="T-G — Path Normalization Test" classname="UnityMCP.NL-T">
  <system-out><![CDATA[
Edit via unity://path/... succeeded.
Same edit via Assets/... returned stale_file, retried with updated hash: OK.
  ]]></system-out>
</testcase>
```

```xml
<testcase name="T-H — Validation on Modified File" classname="UnityMCP.NL-T">
  <system-out><![CDATA[
validate_script(level:"standard"): OK on the modified file.
  ]]></system-out>
</testcase>
```

```xml
<testcase name="T-I — Failure Surface Testing" classname="UnityMCP.NL-T">
  <system-out><![CDATA[
Overlapping edit: failed cleanly (error captured).
Stale hash edit: failed cleanly (error captured).
File unchanged.
  ]]></system-out>
</testcase>
```

```xml
<testcase name="T-J — Idempotency on Modified File" classname="UnityMCP.NL-T">
  <system-out><![CDATA[
Insert marker after "// Tail test C": OK.
Insert same marker again: no_op: true.
regex_remove marker: OK.
regex_remove again: no_op: true.
validate_script: OK.
  ]]></system-out>
</testcase>
