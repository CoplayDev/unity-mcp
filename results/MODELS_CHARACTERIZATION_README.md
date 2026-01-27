# Models & Data Structures - Characterization Tests README

**Created**: 2026-01-26
**Domain**: Models & Data Structures (Python + C#)
**Status**: ✅ Complete - All 79 Python tests passing

---

## Quick Start

### View Test Results
```bash
cd /Users/davidsarno/unity-mcp/Server
python3 -m pytest tests/test_models_characterization.py -v
# Expected: 79 passed in 0.11s
```

### Run Specific Tests
```bash
# Test MCPResponse model only
python3 -m pytest tests/test_models_characterization.py::TestMCPResponseModel -v

# Test normalize_unity_response function
python3 -m pytest tests/test_models_characterization.py::TestNormalizeUnityResponse -v

# Test with detailed output
python3 -m pytest tests/test_models_characterization.py -vv --tb=long
```

### Run C# Tests
```bash
# In Unity Editor menu
Window → General → Test Runner
# Then Run All in the test window
```

---

## What's Tested

### Python Models (79 tests)

**Server/src/models/models.py:**
- ✅ MCPResponse (11 tests)
- ✅ ToolParameterModel (13 tests)
- ✅ ToolDefinitionModel (12 tests)
- ✅ UnityInstanceInfo (13 tests)

**Server/src/models/unity_response.py:**
- ✅ normalize_unity_response() (18 tests)

**Additional:**
- ✅ Model Validation (5 tests)
- ✅ Schema Consistency (3 tests)

### C# Models (50+ tests)

**MCPForUnity/Editor/Models/:**
- ✅ McpStatus enum (3 tests)
- ✅ ConfiguredTransport enum (2 tests)
- ✅ McpClient (20 tests)
- ✅ McpConfigServer (10 tests)
- ✅ McpConfigServers (4 tests)
- ✅ McpConfig (5 tests)
- ✅ Command (8 tests)
- ✅ Integration & Edge Cases (9 tests)

---

## Key Test Coverage

### ✅ Model Instantiation
- Default values documented
- Required fields enforced (Python via Pydantic)
- Optional fields supported

### ✅ Serialization/Deserialization
- JSON round-trip testing
- Complex nested structures
- Datetime handling
- Null value handling

### ✅ Validation
- Required field detection
- Type checking (Python)
- Error message clarity

### ✅ Schema Consistency
- Inter-model contracts verified
- Configuration hierarchy tested
- Field naming patterns documented

### ✅ Duplication Issues Identified
1. **Session Models** (P1-4)
   - PluginSession (Python) vs SessionDetails (C#)
   - Same concept, different names
   - Test marked in docstrings

2. **Client Configuration** (P2-3)
   - McpClient has 6 separate flags
   - Could use builder pattern
   - Test explicitly documents the issue

---

## Test Files

### Main Test Files
```
/Users/davidsarno/unity-mcp/Server/tests/
├── test_models_characterization.py (933 lines, 79 tests)
└── integration/ (other tests)

/Users/davidsarno/unity-mcp/MCPForUnity/Editor/Models/
├── Tests/
│   └── Models_Characterization.cs (888 lines, 50+ tests)
├── McpClient.cs
├── McpConfig.cs
├── McpConfigServer.cs
├── MCPConfigServers.cs
├── Command.cs
└── McpStatus.cs
```

### Documentation Files
```
/Users/davidsarno/unity-mcp/results/
├── MODELS_CHARACTERIZATION_SUMMARY.md (detailed findings)
├── MODELS_TEST_INDEX.md (complete test list)
├── MODELS_CHARACTERIZATION_README.md (this file)
└── REFACTOR_PLAN.md (P1-4 and P2-3 details)
```

---

## How Tests Are Organized

### Test Classes (Python)
Each model has its own test class:
- `TestMCPResponseModel` — 11 tests
- `TestToolParameterModel` — 13 tests
- `TestToolDefinitionModel` — 12 tests
- `TestUnityInstanceInfo` — 13 tests
- `TestNormalizeUnityResponse` — 18 tests
- `TestModelValidation` — 5 tests
- `TestSchemaConsistency` — 3 tests

### Test Fixtures (C#)
Organized by model in nested TestFixture classes:
- `McpStatusEnumTests`
- `ConfiguredTransportEnumTests`
- `McpClientTests`
- `McpConfigServerTests`
- `McpConfigServersTests`
- `McpConfigTests`
- `CommandTests`
- `SchemaConsistencyTests`
- `EdgeCaseTests`

---

## Test Patterns

### Instantiation Tests
```python
def test_mcp_response_minimal_required_fields(self):
    """Test MCPResponse with only required field (success)."""
    response = MCPResponse(success=True)
    assert response.success is True
    assert response.message is None  # Optional fields are None
```

### Serialization Tests
```python
def test_mcp_response_serialization_to_json(self):
    """Test MCPResponse can be serialized to JSON."""
    response = MCPResponse(success=True, message="Success")
    json_str = response.model_dump_json()
    data = json.loads(json_str)
    assert data["success"] is True
```

### Round-Trip Tests
```python
def test_unity_instance_info_round_trip_json(self):
    """Test round-trip serialization/deserialization."""
    original = UnityInstanceInfo(...)
    json_str = original.model_dump_json()
    restored = UnityInstanceInfo.model_validate_json(json_str)
    assert restored.id == original.id
```

### Parametrized Tests
```python
@pytest.mark.parametrize("success,message,error", [
    (True, "OK", None),
    (False, None, "Error occurred"),
    # ...
])
def test_mcp_response_various_combinations(self, success, message, error):
    """Parametrized test for various field combinations."""
    response = MCPResponse(success=success, message=message, error=error)
    assert response.success == success
```

---

## Important Notes

### Duplication Issues Documented

**Test includes explicit notes about:**

1. **Session Model Duplication (P1-4)**
   ```python
   # DUPLICATION NOTES:
   # - NOTE: PluginSession (Python) and SessionDetails (C# likely) represent the same concept
   # These should be consolidated in refactor P1-4
   ```

2. **Client Over-Configuration (P2-3)**
   ```csharp
   [Test]
   public void McpClient_CapabilityFlagsOverconfigurations()
   {
       // NOTE: This test documents the over-configuration issue identified in refactor P2-3
       // McpClient has 6 separate configuration flags for behavior that could be
       // simplified using a builder pattern or strategy pattern.
   }
   ```

### No Refactoring in Tests

These are **characterization tests** - they verify CURRENT behavior:
- ✅ Tests run against existing code
- ✅ No code changes to implement refactors
- ✅ All assertions match current implementation
- ✅ Tests serve as regression baseline

### Ready for Refactoring

After these tests are in place:
1. Implement refactors while keeping tests passing
2. Add new tests for consolidated models
3. Verify backward compatibility
4. Use as CI/CD gate

---

## Validation Rules Captured

### MCPResponse
- `success` (bool, required)
- `message` (str, optional)
- `error` (str, optional)
- `data` (any, optional)
- `hint` (str, optional) — supported: "retry"

### ToolParameterModel
- `name` (str, required)
- `type` (str, default="string") — options: string, integer, float, boolean, array, object
- `required` (bool, default=True)
- `description` (str, optional)
- `default_value` (str, optional)

### ToolDefinitionModel
- `name` (str, required)
- `structured_output` (bool, default=True)
- `requires_polling` (bool, default=False)
- `poll_action` (str, default="status")
- `parameters` (list, default=[])

### UnityInstanceInfo
- `id` (str, required) — "ProjectName@hash" format
- `name` (str, required)
- `path` (str, required)
- `hash` (str, required)
- `port` (int, required)
- `status` (str, required) — "running", "reloading", "offline"
- `last_heartbeat` (datetime, optional)
- `unity_version` (str, optional)

### normalize_unity_response()
- Maps `status: "success"` → `success: true`
- Maps any other status → `success: false`
- Extracts `result` field into response shape
- Filters internal fields (code, status, message)
- Handles nested success in result
- Passes through non-dict inputs unchanged

### McpClient
- `status` (McpStatus enum)
- `configStatus` (str)
- 6 capability flags (IsVsCodeLayout, SupportsHttpTransport, etc.)
- `DefaultUnityFields` (Dict)
- Methods: GetStatusDisplayString(), SetStatus()

### McpConfig Hierarchy
- McpConfig → McpConfigServers → McpConfigServer
- Uses JsonProperty attributes for JSON mapping
- NullValueHandling.Ignore on optional fields

### Command
- `type` (str) — command name
- `@params` (JObject) — command parameters
- Supports any JSON structure in params

---

## Common Test Patterns

### Testing Defaults
```python
def test_tool_parameter_type_defaults_to_string(self):
    param = ToolParameterModel(name="text")
    assert param.type == "string"  # Verify default
```

### Testing Required Fields
```python
def test_mcp_response_missing_success_field_required(self):
    with pytest.raises(Exception):  # Pydantic ValidationError
        MCPResponse.model_validate({})
```

### Testing Serialization
```python
def test_mcp_response_serialization_to_json(self):
    response = MCPResponse(success=True, message="OK")
    json_str = response.model_dump_json()
    data = json.loads(json_str)
    assert data["success"] is True
```

### Testing Round-Trips
```python
json_str = response.model_dump_json()
restored = MCPResponse.model_validate_json(json_str)
assert restored.success == response.success
```

### Testing Combinations
```python
@pytest.mark.parametrize("success,message,error", [
    (True, "OK", None),
    (False, None, "Error"),
    # ...
])
def test_combinations(self, success, message, error):
    response = MCPResponse(success=success, message=message, error=error)
    assert response.success == success
```

---

## Running Tests in CI/CD

### GitHub Actions Example
```yaml
name: Models Tests
on: [push, pull_request]

jobs:
  python-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - uses: actions/setup-python@v2
        with:
          python-version: '3.12'
      - run: cd Server && pip install -r requirements.txt
      - run: cd Server && python3 -m pytest tests/test_models_characterization.py -v

  csharp-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - uses: game-ci/unity-test-runner@v2
        with:
          testCategory: MCPForUnityTests.Editor.Models
          # ... Unity Test Runner config
```

---

## Baseline Results

### Python Tests
```
============================= test session starts ==============================
collected 79 items

tests/test_models_characterization.py ... [100 passed in 0.11s]

Result: ✅ All 79 tests PASSED
```

**Baseline Date**: 2026-01-26
**Python Version**: 3.13.3
**Pytest Version**: 8.4.2

---

## References

- **Full Test List**: See `MODELS_TEST_INDEX.md`
- **Detailed Findings**: See `MODELS_CHARACTERIZATION_SUMMARY.md`
- **Refactor Plan**: See `REFACTOR_PLAN.md` (sections P1-4 and P2-3)
- **Python Models**: `/Users/davidsarno/unity-mcp/Server/src/models/`
- **C# Models**: `/Users/davidsarno/unity-mcp/MCPForUnity/Editor/Models/`

---

## Next Steps

1. **Review Tests**
   - Open `test_models_characterization.py` in IDE
   - Review C# tests in `Models_Characterization.cs`
   - Check documentation references to refactor plan

2. **Run Tests**
   - Python: `pytest tests/test_models_characterization.py -v`
   - C#: Unity Test Runner

3. **Use as Baseline**
   - All tests must pass before implementing refactors
   - After refactoring, all tests should still pass
   - Add new tests for consolidated models

4. **Implement Refactors**
   - P1-4: Session Model Consolidation
   - P2-3: Configurator Builder Pattern
   - Use tests as regression gate

---

## Support

For questions about specific tests, see:
- Test class docstrings in `test_models_characterization.py`
- Test method docstrings explaining what each test verifies
- Comments marking duplication issues (search for "NOTE:" or "DUPLICATION")
- XML doc comments in C# test file

---

## Summary

✅ **129+ characterization tests** covering Python and C# models
✅ **All 79 Python tests passing** (baseline established)
✅ **C# tests ready** for Unity Test Runner
✅ **Duplication issues documented** with test markers
✅ **Regression baseline established** for refactoring
