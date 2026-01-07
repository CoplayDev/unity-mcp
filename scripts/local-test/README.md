# Unity MCP Local Test Suite

Fast local iteration for the Unity MCP NL/T test suite, without pushing to GitHub.

## Overview

This local test harness replicates your CI suite but runs against a locally running Unity Editor instead of Docker. This gives you:

- **10-100x faster iteration** (no Docker startup, no GitHub push/wait)
- **Real-time debugging** (see Unity console, attach debuggers)
- **Rapid experimentation** (edit prompts, re-run instantly)

## Quick Start

### 1. One-Time Setup

```bash
# Run setup (installs dependencies, makes scripts executable)
./scripts/local-test/setup.sh
```

### 2. Start Unity

Open Unity Editor and load the test project:
```
TestProjects/UnityMCPTests
```

The MCP bridge should auto-start when Unity opens.

### 3. Run Tests

**Full suite (NL + T passes):**
```bash
./scripts/local-test/run-nl-suite-local.sh
```

**Just NL tests (NL-0 to NL-4):**
```bash
./scripts/local-test/run-nl-suite-local.sh --nl-only
```

**Just T tests (T-A to T-J):**
```bash
./scripts/local-test/run-nl-suite-local.sh --t-only
```

**Just GameObject tests (GO-0 to GO-10):**
```bash
./scripts/local-test/run-nl-suite-local.sh --go-only
```

**Full suite including GameObject tests:**
```bash
./scripts/local-test/run-nl-suite-local.sh --with-go
```

**Single test (fastest for iteration):**
```bash
./scripts/local-test/quick-test.sh NL-0
./scripts/local-test/quick-test.sh T-F
./scripts/local-test/quick-test.sh GO-2
```

## Scripts

### `setup.sh`
One-time setup script. Checks dependencies, installs MCP server, makes scripts executable.

### `run-nl-suite-local.sh`
Main test runner. Runs the full NL/T suite locally.

**Options:**
- `--nl-only` - Run only NL pass (5 tests)
- `--t-only` - Run only T pass (10 tests)
- `--go-only` - Run only GameObject pass (11 tests)
- `--with-go` - Include GameObject tests with NL+T suite
- `--skip-setup` - Skip Unity check and config generation
- `--keep-reports` - Don't clean reports directory before run
- `--help` - Show usage information

**Examples:**
```bash
# Full NL+T suite (default)
./scripts/local-test/run-nl-suite-local.sh

# Full suite including GameObject tests
./scripts/local-test/run-nl-suite-local.sh --with-go

# Quick iteration (skip checks)
./scripts/local-test/run-nl-suite-local.sh --t-only --skip-setup

# Test only GameObject API
./scripts/local-test/run-nl-suite-local.sh --go-only

# Keep previous results for comparison
./scripts/local-test/run-nl-suite-local.sh --keep-reports
```

### `quick-test.sh`
Single test runner for rapid iteration. Much faster than running the full suite.

**Usage:**
```bash
./scripts/local-test/quick-test.sh TEST_ID [CUSTOM_PROMPT]
```

**Examples:**
```bash
# Run baseline test
./scripts/local-test/quick-test.sh NL-0

# Run atomic multi-edit test
./scripts/local-test/quick-test.sh T-F

# Run with custom prompt
./scripts/local-test/quick-test.sh NL-1 "Test method replacement on HasTarget()"
```

**Available test IDs:**

**NL Pass (Script Editing):**
- `NL-0` - Baseline State Capture
- `NL-1` - Core Method Operations
- `NL-2` - Anchor Comment Insertion
- `NL-3` - End-of-Class Content
- `NL-4` - Console State Verification

**T Pass (Advanced Script Editing):**
- `T-A` - Temporary Helper Lifecycle
- `T-B` - Method Body Interior Edit
- `T-C` - Different Method Interior Edit
- `T-D` - End-of-Class Helper
- `T-E` - Method Evolution Lifecycle
- `T-F` - Atomic Multi-Edit
- `T-G` - Path Normalization Test
- `T-H` - Validation on Modified File
- `T-I` - Failure Surface Testing
- `T-J` - Idempotency on Modified File

**GO Pass (GameObject API):**
- `GO-0` - Hierarchy with ComponentTypes
- `GO-1` - Find GameObjects Tool (by component)
- `GO-2` - GameObject Resource Read
- `GO-3` - Components Resource Read
- `GO-4` - Manage Components Tool - Add/Set
- `GO-5` - Find GameObjects by Name
- `GO-6` - Find GameObjects by Tag
- `GO-7` - Single Component Resource Read
- `GO-8` - Remove Component
- `GO-9` - Find with Pagination
- `GO-10` - Deprecation Warnings

## Test Results

Results are written to the `reports/` directory:

```
reports/
├── NL-0_results.xml      # Individual test results
├── NL-1_results.xml
├── ...
├── T-A_results.xml
├── ...
├── nl-pass.log           # Full NL pass log
├── t-pass.log            # Full T pass log
└── junit-nl-suite.xml    # Merged JUnit report (CI only)
```

Each test produces an XML fragment with:
- Test status (pass/fail)
- Evidence in `<system-out>` section
- Failure details if applicable

## Typical Workflow

### Development Iteration
```bash
# 1. Edit test prompts
vim .claude/prompts/nl-unity-suite-nl.md

# 2. Test your changes quickly
./scripts/local-test/quick-test.sh NL-1

# 3. Check the result
cat reports/NL-1_results.xml

# 4. Iterate until working
```

### Pre-Push Validation
```bash
# Run full suite before pushing to GitHub
./scripts/local-test/run-nl-suite-local.sh

# Check all tests pass
ls -l reports/*_results.xml

# If all good, push to GitHub
git push
```

## Troubleshooting

### "Unity MCP status file not found"

**Cause:** Unity Editor is not running or MCP bridge didn't start.

**Solution:**
1. Open Unity Editor
2. Load `TestProjects/UnityMCPTests`
3. Check Unity console for MCP bridge startup messages
4. Look for `.unity-mcp/unity-mcp-status-*.json` file

### "MCP server failed to start"

**Cause:** Python dependencies not installed.

**Solution:**
```bash
cd Server
uv venv
uv pip install -e .
```

### "claude-code CLI not found"

**Cause:** Claude Code CLI not installed.

**Solution:**
Install from: https://github.com/anthropics/claude-code

### "Test didn't produce result file"

**Cause:** Test failed or Claude didn't write the XML fragment.

**Solution:**
1. Check the log file: `reports/{TEST_ID}_quick.log`
2. Look for error messages
3. Check Unity console for compilation errors
4. Try running with more verbose output

### Unity Bridge Not Responding

**Cause:** Unity might be busy or crashed.

**Solution:**
1. Check Unity is not frozen
2. Look at Unity console for errors
3. Restart Unity Editor
4. Re-run setup: `./scripts/local-test/setup.sh`

## Comparison: Local vs CI

| Aspect | Local | CI (GitHub Actions) |
|--------|-------|---------------------|
| Unity | GUI Editor | Headless Docker |
| Startup time | ~5s | ~2-5min |
| Iteration speed | Instant | Push + wait ~10min |
| Debugging | Real-time console | Log files only |
| Cost | Free | GitHub minutes |
| Use case | Development | Pre-merge validation |

## Advanced Usage

### Custom Test Variations

Create temporary test prompts for experimentation:

```bash
# Create a custom test
cat > /tmp/my-test.md <<'EOF'
Test only the HasTarget() method replacement.
Write result to reports/CUSTOM_results.xml.
EOF

# Run it
claude-code \
  --mcp-config .claude/local/mcp.json \
  --settings .claude/settings.json \
  --prompt-file /tmp/my-test.md \
  --model claude-haiku-4-5-20251001
```

### Comparing Results

Keep multiple result sets:

```bash
# Baseline run
./scripts/local-test/run-nl-suite-local.sh
mv reports reports-baseline

# After changes
./scripts/local-test/run-nl-suite-local.sh
mv reports reports-new

# Compare
diff -ur reports-baseline reports-new
```

### Debugging MCP Communication

Enable detailed MCP logging:

```bash
export MCP_LOG_LEVEL=trace

# Run test
./scripts/local-test/quick-test.sh NL-0 2>&1 | tee mcp-debug.log
```

## Files Created

The local test runner creates these files:

```
.claude/local/
└── mcp.json              # Local MCP configuration

.unity-mcp/
└── unity-mcp-status-*.json   # Unity instance status (auto-created)

reports/
├── *_results.xml         # Test result fragments
├── nl-pass.log           # NL pass execution log
└── t-pass.log            # T pass execution log
```

## Next Steps

Once your tests are working locally:

1. **Commit your changes:**
   ```bash
   git add .claude/prompts/
   git commit -m "Update test prompts"
   ```

2. **Push and let CI validate:**
   ```bash
   git push
   ```

3. **Monitor CI run:**
   - GitHub Actions will run the full suite in Docker
   - Should match your local results
   - Any differences indicate environment-specific issues

## Tips

- **Keep Unity open:** The editor startup is the slowest part
- **Use quick-test.sh for iteration:** Much faster than full suite
- **Check Unity console:** Real-time feedback is invaluable
- **Edit prompts freely:** Local testing is cheap, experiment!
- **Run full suite before pushing:** Catches integration issues

## Support

If you encounter issues:

1. Check this README's troubleshooting section
2. Review the test logs in `reports/`
3. Check Unity console for errors
4. Verify MCP server is running: `uv run --directory Server mcp-for-unity --help`
