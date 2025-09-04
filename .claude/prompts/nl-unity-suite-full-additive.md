# Unity NL/T Editing Suite — Additive Test Design

You are running inside CI for the `unity-mcp` repo. Use only the tools allowed by the workflow. Work autonomously; do not prompt the user. Do NOT spawn subagents.

**Print this once, verbatim, early in the run:**
AllowedTools: Write,mcp__unity__manage_editor,mcp__unity__list_resources,mcp__unity__read_resource,mcp__unity__apply_text_edits,mcp__unity__script_apply_edits,mcp__unity__validate_script,mcp__unity__find_in_file,mcp__unity__read_console,mcp__unity__get_sha

---

## Mission
1) Pick target file (prefer):
   - `unity://path/Assets/Scripts/LongUnityScriptClaudeTest.cs`
2) Execute **all** NL/T tests in order using minimal, precise edits that **build on each other**.
3) Validate each edit with `mcp__unity__validate_script(level:"standard")`.
4) **Report**: write one `<testcase>` XML fragment per test to `reports/<TESTID>_results.xml`. Do **not** read or edit `$JUNIT_OUT`.
5) **NO RESTORATION** - tests build additively on previous state.

---

## Environment & Paths (CI)
- Always pass: `project_root: "TestProjects/UnityMCPTests"` and `ctx: {}` on list/read/edit/validate.
- **Canonical URIs only**:
  - Primary: `unity://path/Assets/...` (never embed `project_root` in the URI)
  - Relative (when supported): `Assets/...`

CI provides:
- `$JUNIT_OUT=reports/junit-nl-suite.xml` (pre‑created; leave alone)
- `$MD_OUT=reports/junit-nl-suite.md` (synthesized from JUnit)

---

## Tool Mapping
- **Anchors/regex/structured**: `mcp__unity__script_apply_edits`
  - Allowed ops: `anchor_insert`, `replace_method`, `insert_method`, `delete_method`, `regex_replace`
- **Precise ranges / atomic batch**: `mcp__unity__apply_text_edits` (non‑overlapping ranges)
- **Hash-only**: `mcp__unity__get_sha` — returns `{sha256,lengthBytes,lastModifiedUtc}` without file body
- **Validation**: `mcp__unity__validate_script(level:"standard")`
- **Dynamic targeting**: Use `mcp__unity__find_in_file` to locate current positions of methods/markers

---

## Additive Test Design Principles

**Key Changes from Reset-Based:**
1. **Dynamic Targeting**: Use `find_in_file` to locate methods/content, never hardcode line numbers
2. **State Awareness**: Each test expects the file state left by the previous test
3. **Content-Based Operations**: Target methods by signature, classes by name, not coordinates
4. **Cumulative Validation**: Ensure the file remains structurally sound throughout the sequence
5. **Composability**: Tests demonstrate how operations work together in real workflows

**State Tracking:**
- Track file SHA after each test to ensure operations succeeded
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
- Insert `PrintSeries()` method after `GetCurrentTarget()`: `public void PrintSeries() { Debug.Log("1,2,3"); }`
- Verify both methods exist and are properly formatted
- Delete `PrintSeries()` method (cleanup for next test)
- **Expected final state**: `HasTarget()` modified, file structure intact, no temporary methods

### NL-2. Anchor Comment Insertion (Additive State B) 
**Goal**: Demonstrate anchor-based insertions above methods
**Actions**:
- Use `find_in_file` to locate current position of `Update()` method
- Insert `// Build marker OK` comment line above `Update()` method
- Verify comment exists and `Update()` still functions
- **Expected final state**: State A + build marker comment above `Update()`

### NL-3. End-of-Class Content (Additive State C)
**Goal**: Demonstrate end-of-class insertions with smart brace matching
**Actions**:
- Use anchor pattern to find the class-ending brace (accounts for previous additions)
- Insert three comment lines before final class brace:
  ```
  // Tail test A
  // Tail test B  
  // Tail test C
  ```
- **Expected final state**: State B + tail comments before class closing brace

### NL-4. Console State Verification (No State Change)
**Goal**: Verify Unity console integration without file modification
**Actions**:
- Read Unity console messages (INFO level)
- Validate no compilation errors from previous operations
- **Expected final state**: State C (unchanged)

### T-A. Temporary Helper Lifecycle (Returns to State C)
**Goal**: Test insert → verify → delete cycle for temporary code
**Actions**:
- Find current position of `GetCurrentTarget()` method (may have shifted from NL-2 comment)
- Insert temporary helper: `private int __TempHelper(int a, int b) => a + b;`
- Verify helper method exists and compiles
- Delete helper method via structured delete operation
- **Expected final state**: Return to State C (helper removed, other changes intact)

### T-B. Method Body Interior Edit (Additive State D)
**Goal**: Edit method interior without affecting structure, on modified file
**Actions**:
- Use `find_in_file` to locate current `HasTarget()` method (modified in NL-1)
- Edit method body interior: change return statement to `return true; /* test modification */`
- Use `validate: "relaxed"` for interior-only edit
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
- **Expected final state**: State E + TestHelper() method before class end

### T-E. Method Evolution Lifecycle (Additive State G)
**Goal**: Insert → modify → finalize a method through multiple operations
**Actions**:
- Insert basic method: `private int Counter = 0;`
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

### T-G. Path Normalization Test (No State Change)
**Goal**: Verify URI forms work equivalently on modified file
**Actions**:
- Make identical edit using `unity://path/Assets/Scripts/LongUnityScriptClaudeTest.cs`
- Then using `Assets/Scripts/LongUnityScriptClaudeTest.cs` 
- Second should return `stale_file`, retry with updated SHA
- Verify both URI forms target same file
- **Expected final state**: State H (no content change, just path testing)

### T-H. Validation on Modified File (No State Change)
**Goal**: Ensure validation works correctly on heavily modified file
**Actions**:
- Run `validate_script(level:"standard")` on current state
- Verify no structural errors despite extensive modifications
- **Expected final state**: State H (validation only, no edits)

### T-I. Failure Surface Testing (No State Change)
**Goal**: Test error handling on real modified file
**Actions**:
- Attempt overlapping edits (should fail cleanly)
- Attempt edit with stale SHA (should fail cleanly) 
- Verify error responses are informative
- **Expected final state**: State H (failed operations don't modify file)

### T-J. Idempotency on Modified File (Additive State I)
**Goal**: Verify operations behave predictably when repeated
**Actions**:
- Add unique marker comment: `// idempotency test marker`
- Attempt to add same comment again (should detect no-op)
- Remove marker, attempt removal again (should handle gracefully)
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
4. Log cumulative changes in test evidence

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