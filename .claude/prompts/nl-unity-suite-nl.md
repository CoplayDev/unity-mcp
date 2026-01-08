# Unity NL Editing Suite — Additive Test Design

You are running inside CI for the `unity-mcp` repo. Use only the tools allowed by the workflow. Work autonomously; do not prompt the user. Do NOT spawn subagents.

**Print this once, verbatim, early in the run:**
AllowedTools: Write,mcp__UnityMCP__apply_text_edits,mcp__UnityMCP__script_apply_edits,mcp__UnityMCP__validate_script,mcp__UnityMCP__find_in_file,mcp__UnityMCP__read_console,mcp__UnityMCP__get_sha

---

## Mission
1) Pick target file (prefer):
   - `mcpforunity://path/Assets/Scripts/LongUnityScriptClaudeTest.cs`
2) Execute NL tests NL-0..NL-4 in order using minimal, precise edits that build on each other.
3) Validate each edit with `mcp__UnityMCP__validate_script(level:"standard")`.
4) **Report**: write one `<testcase>` XML fragment per test to `reports/<TESTID>_results.xml`. Do **not** read or edit `$JUNIT_OUT`.

**CRITICAL XML FORMAT REQUIREMENTS:**
- Each file must contain EXACTLY one `<testcase>` root element
- NO prologue, epilogue, code fences, or extra characters
- NO markdown formatting or explanations outside the XML
- Use this exact shape (write the XML directly into the file; do not wrap it in ``` fences):

<testcase name="NL-0 — Baseline State Capture" classname="UnityMCP.NL-T">
  <system-out><![CDATA[
(evidence of what was accomplished)
  ]]></system-out>
</testcase>

- Must end with the closing tag `</testcase>` (well‑formed XML only).

- If test fails, include: `<failure message="reason"/>`
- TESTID must be one of: NL-0, NL-1, NL-2, NL-3, NL-4
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
- Prefer `mcp__UnityMCP__find_in_file` for targeting to minimize transcript size.
- Per‑test `system-out` ≤ 400 chars: brief status only (no SHA).
- Console evidence: fetch the last 10 lines with `include_stacktrace:false` and include ≤ 3 lines in the fragment.
- Avoid quoting multi‑line diffs; reference markers instead.
— Console scans: perform two reads — last 10 `log/info` lines and up to 3 `error` entries (use `include_stacktrace:false`); include ≤ 3 lines total in the fragment; if no errors, state "no errors".

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
- Track file SHA after each test (`mcp__UnityMCP__get_sha`) for potential preconditions in later passes. Do not include SHA values in report fragments.
- Use content signatures (method names, comment markers) to verify expected state
- Validate structural integrity after each major change

---

## Execution Order & Additive Test Specs

### NL-0. Baseline State Capture
**Goal**: Establish initial file state and verify accessibility
**Actions**:
- Read file head and tail to confirm structure
- Locate key methods: `HasTarget()`, `GetCurrentTarget()`, `Update()`, `ApplyBlend()`
- Record initial SHA for tracking
- **Expected final state**: Unchanged baseline file

### NL-1. Core Method Operations (Additive State A)
**Goal**: Demonstrate method replacement operations
**Actions**: 
- Replace `HasTarget()` method body: `public bool HasTarget() { return currentTarget != null; }`
- Validate.
- Insert `PrintSeries()` method after a unique anchor method. Prefer `GetCurrentTarget()` if unique; otherwise use another unique method such as `ApplyBlend`. Insert: `public void PrintSeries() { Debug.Log("1,2,3"); }`
- Validate that both methods exist and are properly formatted.
- Delete `PrintSeries()` method (cleanup for next test)
- **Expected final state**: `HasTarget()` modified, file structure intact, no temporary methods

### NL-2. Anchor Comment Insertion (Additive State B) 
**Goal**: Demonstrate anchor-based insertions above methods
**Actions**:
- Use `find_in_file` with a tolerant anchor to locate the `Update()` method, e.g. `(?m)^\\s*(?:public|private|protected|internal)?\\s*void\\s+Update\\s*\\(\\s*\\)`
- Expect exactly one match; if multiple, fail clearly rather than guessing.
- Insert `// Build marker OK` comment line above `Update()` method
- Verify comment exists and `Update()` still functions
- **Expected final state**: State A + build marker comment above `Update()`

### NL-3. End-of-Class Content (Additive State C)
**Goal**: Demonstrate end-of-class insertions without ambiguous anchors
**Actions**:
- Use `find_in_file` to locate brace-only lines (e.g., `(?m)^\\s*}\\s*$`). Select the **last** such line (preferably indentation 0 if multiples).
- Compute an exact insertion point immediately before that last brace using `apply_text_edits` (do not use `anchor_insert` for this step).
- Insert three comment lines before the final class brace:
  ```
  // Tail test A
  // Tail test B  
  // Tail test C
  ```
- **Expected final state**: State B + tail comments before class closing brace

### NL-4. Console State Verification (No State Change)
**Goal**: Verify Unity console integration without file modification
**Actions**:
- Read last 10 Unity console lines (log/info)
- Perform a targeted scan for errors/exceptions (type: errors), up to 3 entries
- Validate no compilation errors from previous operations
- **Expected final state**: State C (unchanged)
- **IMMEDIATELY** write clean XML fragment to `reports/NL-4_results.xml` (no extra text). The `<testcase name>` must start with `NL-4`. Include at most 3 lines total across both reads, or simply state "no errors; console OK" (≤ 400 chars).

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
{"op": "anchor_insert", "anchor": "(?m)^\\s*(?:public|private|protected|internal)?\\s*void\\s+Update\\s*\\(\\s*\\)", "position": "before", "text": "// comment"}
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

