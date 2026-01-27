# Models & Data Structures - Complete Test Index

Generated: 2026-01-26

---

## Python Tests (79 total)
**File**: `/Users/davidsarno/unity-mcp/Server/tests/test_models_characterization.py`

### MCPResponse Model Tests (11 tests)
1. `test_mcp_response_minimal_required_fields` - Instantiate with only success field
2. `test_mcp_response_all_fields` - Instantiate with all fields specified
3. `test_mcp_response_success_false_with_error` - Success=False state
4. `test_mcp_response_serialization_to_json` - JSON serialization via model_dump_json()
5. `test_mcp_response_deserialization_from_json` - JSON deserialization via model_validate_json()
6. `test_mcp_response_hint_values` - Hint field with various values
7. `test_mcp_response_complex_data_structure` - Nested data structures
8-11. `test_mcp_response_various_combinations[*]` - Parametrized: 4 combinations of success/message/error

**Coverage**: Field requirements, defaults, serialization, complex data

### ToolParameterModel Tests (13 tests)
1. `test_tool_parameter_minimal` - Minimal required fields (name only)
2. `test_tool_parameter_full_specification` - All fields specified
3. `test_tool_parameter_type_defaults_to_string` - Type default
4. `test_tool_parameter_required_defaults_to_true` - Required default
5. `test_tool_parameter_various_types` - All type values (string, integer, float, boolean, array, object)
6. `test_tool_parameter_serialization` - JSON serialization
7. `test_tool_parameter_deserialization` - JSON deserialization
8. `test_tool_parameter_with_default_value` - Default value handling
9-13. `test_tool_parameter_combinations[*]` - Parametrized: 5 combinations of name/type/required

**Coverage**: Field requirements, defaults, type validation, serialization

### ToolDefinitionModel Tests (12 tests)
1. `test_tool_definition_minimal` - Minimal required fields (name only)
2. `test_tool_definition_full_specification` - All fields with parameters
3. `test_tool_definition_defaults` - Verify all defaults (structured_output=True, requires_polling=False, poll_action="status")
4. `test_tool_definition_with_polling` - Polling configuration
5. `test_tool_definition_with_many_parameters` - Multiple parameters
6. `test_tool_definition_serialization` - JSON serialization with parameters
7. `test_tool_definition_deserialization` - JSON deserialization
8-11. `test_tool_definition_polling_combinations[*]` - Parametrized: 4 polling combinations
12. (See parametrized tests above)

**Coverage**: Field requirements, defaults, parameter handling, polling config

### UnityInstanceInfo Tests (13 tests)
1. `test_unity_instance_info_minimal` - Required fields only
2. `test_unity_instance_info_full_fields` - All fields including optional ones
3. `test_unity_instance_info_status_values` - Various status values (running, reloading, offline)
4. `test_unity_instance_info_to_dict` - to_dict() method
5. `test_unity_instance_info_to_dict_with_heartbeat` - to_dict() with datetime conversion
6. `test_unity_instance_info_serialization_to_json` - JSON serialization
7. `test_unity_instance_info_deserialization_from_json` - JSON deserialization
8. `test_unity_instance_info_round_trip_json` - Full round-trip test
9-13. `test_unity_instance_info_port_status_combinations[*]` - Parametrized: 5 port/status combinations

**Coverage**: Field requirements, optional fields, to_dict() method, datetime handling, serialization

### normalize_unity_response() Tests (18 tests)
1. `test_normalize_empty_dict` - Empty input dict
2. `test_normalize_already_normalized_response` - Already MCPResponse-shaped
3. `test_normalize_status_success_response` - Status=success mapping
4. `test_normalize_status_error_response` - Status=error mapping
5. `test_normalize_with_data_payload` - Result contains data
6. `test_normalize_non_dict_response` - Non-dict input (passthrough)
7. `test_normalize_none_response` - None input
8. `test_normalize_list_response` - List input (passthrough)
9. `test_normalize_result_with_nested_dict` - Nested structures in result
10. `test_normalize_no_status_no_success_field` - Neither status nor success
11. `test_normalize_result_field_as_string` - Result is string not dict
12. `test_normalize_error_message_fallback` - Error message falls back to message field
13. `test_normalize_unknown_status` - Unknown status value
14. `test_normalize_result_none_value` - Result field is None
15. `test_normalize_nested_success_in_result` - Success nested in result
16-20. `test_normalize_status_to_success_mapping[*]` - Parametrized: 5 status values
21. `test_normalize_preserves_extra_fields_in_result` - Extra fields in result become data
22. `test_normalize_empty_result_dict` - Empty result dict
23. `test_normalize_status_code_excluded_from_data` - Fields filtered from data

**Coverage**: Status mapping, result extraction, field filtering, edge cases

### ModelValidation Tests (5 tests)
1. `test_mcp_response_missing_success_field_required` - Required field validation
2. `test_tool_parameter_missing_name_required` - Required field validation
3. `test_tool_definition_missing_name_required` - Required field validation
4. `test_unity_instance_info_missing_required_fields` - All required fields
5. `test_unity_instance_info_missing_single_field` - Single field missing

**Coverage**: Pydantic validation, error conditions

### SchemaConsistency Tests (3 tests)
1. `test_mcp_response_with_tool_definition_as_data` - MCPResponse containing ToolDefinitionModel
2. `test_tool_definition_with_all_parameter_types` - All parameter types in one tool
3. `test_unity_instance_info_to_dict_json_roundtrip` - to_dict() → JSON → back conversion

**Coverage**: Schema integration, format consistency

---

## C# Tests (50+ tests)
**File**: `/Users/davidsarno/unity-mcp/MCPForUnity/Editor/Models/Tests/Models_Characterization.cs`

### McpStatus Enum Tests (3 tests)
1. `McpStatus_HasAllExpectedValues` - All 10 enum values exist
2. `McpStatus_CanBeCompared` - Enum comparison works

**Coverage**: Enum existence, comparison

### ConfiguredTransport Enum Tests (2 tests)
1. `ConfiguredTransport_HasAllExpectedValues` - All 3 enum values
2. `ConfiguredTransport_DefaultIsUnknown` - Default value

**Coverage**: Enum values, defaults

### McpClient Tests (20 tests)
1. `McpClient_DefaultValues` - Initial state (all null/NotConfigured)
2. `McpClient_CapabilityFlagsDefaults` - Flags initial state
3. `McpClient_CanSetBasicProperties` - Set name, paths, status
4. `McpClient_CanSetCapabilityFlags` - Set all 6 flags
5. `McpClient_DefaultUnityFieldsIsEmptyDictionary` - DefaultUnityFields initialized
6. `McpClient_CanPopulateDefaultUnityFields` - Add entries to fields dict
7. `McpClient_GetStatusDisplayString_NotConfigured` - Display string for each status (9 individual tests)
16. `McpClient_GetStatusDisplayString_Error_WithoutDetails` - Error display
17. `McpClient_SetStatus_UpdatesEnumAndString` - SetStatus method
18. `McpClient_SetStatus_ErrorWithDetails` - SetStatus with error details
19. `McpClient_SetStatus_ErrorWithoutDetails` - SetStatus without details
20. `McpClient_SetStatus_TransitionsMultipleTimes` - Multiple status transitions
21. `McpClient_CapabilityFlagsOverconfigurations` - Documents P2-3 issue

**Coverage**: All properties, status transitions, capability flags, display strings

### McpConfigServer Tests (10 tests)
1. `McpConfigServer_DefaultValues` - All null
2. `McpConfigServer_CanSetCommand` - command field
3. `McpConfigServer_CanSetArgs` - args array field
4. `McpConfigServer_CanSetType` - type field
5. `McpConfigServer_CanSetUrl` - url field
6. `McpConfigServer_CanSetAllFields` - Set all fields
7. `McpConfigServer_JsonSerialization_OnlyPopulatedFields` - Null handling in JSON
8. `McpConfigServer_JsonSerialization_AllFields` - Full serialization/deserialization
9. Round-trip serialization through JSON.NET

**Coverage**: Property setting, JSON serialization, null value handling

### McpConfigServers Tests (4 tests)
1. `McpConfigServers_DefaultValues` - unityMCP is null initially
2. `McpConfigServers_CanSetUnityMcpServer` - Set unityMCP server
3. `McpConfigServers_JsonPropertyName` - JsonProperty("unityMCP") serialization
4. `McpConfigServers_Deserialization` - Deserialize from JSON string

**Coverage**: Property handling, JSON property names

### McpConfig Tests (5 tests)
1. `McpConfig_DefaultValues` - mcpServers is null
2. `McpConfig_CanSetMcpServers` - Set servers
3. `McpConfig_JsonPropertyName` - JsonProperty("mcpServers") handling
4. `McpConfig_FullSerialization` - Complete serialization round-trip
5. `McpConfig_FullDeserialization` - Deserialize complete config
6. `McpConfig_IsSerializable` - [Serializable] attribute

**Coverage**: Configuration hierarchy, JSON property names, full structure

### Command Tests (8 tests)
1. `Command_DefaultValues` - type and @params null
2. `Command_CanSetType` - type field
3. `Command_CanSetParams` - @params field
4. `Command_CanSetBothTypeAndParams` - Set both
5. `Command_ParamsWithComplexStructure` - Nested JSON
6. `Command_ParamsCanAccessNestedValues` - Extract nested values
7. `Command_ParamsEmptyObject` - Empty JObject
8. `Command_ParamsWithArrays` - Array in params
9. `Command_RoundTripJsonSerialization` - Full round-trip

**Coverage**: Command structure, JObject handling, JSON serialization

### SchemaConsistency Tests (3 tests)
1. `ConfigurationHierarchy_IntegrationTest` - McpConfig → McpConfigServers → McpConfigServer
2. `ClientStatusDisplayConsistency` - All statuses have display strings
3. `CommandTypeAndParamsContract` - Various command/params combinations

**Coverage**: Model hierarchy, consistency across all types

### EdgeCase Tests (6 tests)
1. `McpClient_MultipleStatusTransitionsPreservesOtherFields` - Status change doesn't affect other fields
2. `McpConfigServer_EmptyArgsArray` - Empty string array
3. `Command_ParamsNullIsValid` - null params is valid
4. `McpClient_SetStatusErrorWithEmptyString` - Empty error string handling
5. `McpClient_CapabilityFlagsCanBeToggledMultipleTimes` - Toggle flags repeatedly
6. Integration tests with various field combinations

**Coverage**: Edge cases, unusual but valid states

---

## Test Execution Instructions

### Running Python Tests
```bash
cd /Users/davidsarno/unity-mcp/Server
python3 -m pytest tests/test_models_characterization.py -v

# Run specific test class
python3 -m pytest tests/test_models_characterization.py::TestMCPResponseModel -v

# Run specific test
python3 -m pytest tests/test_models_characterization.py::TestMCPResponseModel::test_mcp_response_minimal_required_fields -v

# Run with coverage
python3 -m pytest tests/test_models_characterization.py --cov=models --cov-report=html
```

### Running C# Tests
```bash
# In Unity Editor
Window → General → Test Runner → Run All

# Or from command line
Unity -runTests -testCategory "MCPForUnityTests.Editor.Models" -batchmode

# Run specific test class
# In Test Runner UI: Search for "ModelsCharacterizationTests"
```

---

## Quick Statistics

| Metric | Count |
|--------|-------|
| Total Python Tests | 79 |
| Total C# Tests | 50+ |
| **Total Tests** | **129+** |
| Python Model Classes Covered | 4 |
| Python Functions Covered | 1 |
| C# Model Classes Covered | 6 |
| C# Enum Types Covered | 2 |
| Parametrized Test Cases | 19 |
| Edge Case Tests | 6 |
| Schema Consistency Tests | 6 |
| Validation Tests | 5 |

---

## Coverage by Concern

| Concern | Python Tests | C# Tests | Notes |
|---------|--------------|---------|-------|
| Instantiation | 8 | 15 | Default values, all fields |
| Serialization | 18 | 12 | JSON round-trips, null handling |
| Validation | 5 | 3 | Required fields, error conditions |
| Default Values | 6 | 6 | Documented via tests |
| Schema Consistency | 3 | 8 | Inter-model contracts |
| Edge Cases | 0 | 6 | Unusual but valid states |
| **Total** | **79** | **50+** | **Coverage complete** |

---

## Duplication Issues Documented

### In Tests

All tests include docstring comments that reference specific issues from REFACTOR_PLAN.md:

1. **P1-4 Session Model Consolidation** (Lines marked in Python tests)
   - `# NOTE: PluginSession (Python) and SessionDetails (C# likely) represent the same concept`
   - `# These should be consolidated in refactor P1-4`

2. **P2-3 Configurator Builder Pattern** (Lines marked in C# tests)
   - `McpClient_CapabilityFlagsOverconfigurations()` test explicitly documents the issue
   - `# NOTE: This test documents the over-configuration issue identified in refactor P2-3`
   - `# McpClient has 6 separate configuration fields for behavior that could be simplified`

### Test Output

When running tests, look for:
- **Python**: Comments in test class docstrings
- **C#**: XML doc comments and [Test] method documentation

---

## Next Steps

1. **Run Tests Baseline**
   ```bash
   # Record current test results as baseline
   cd /Users/davidsarno/unity-mcp/Server
   python3 -m pytest tests/test_models_characterization.py -v > baseline_python.txt

   # In Unity, run C# tests and save results
   ```

2. **Implement P1-4 Refactor**
   - Keep baseline tests passing
   - Add new tests for PluginSession/SessionDetails consolidation
   - Ensure to_api_response() method tested

3. **Implement P2-3 Refactor**
   - Keep baseline tests passing
   - Add builder pattern tests
   - Verify backward compatibility

4. **CI/CD Integration**
   - Add Python tests to CI pipeline
   - Add C# tests to Unity Test Runner in CI
   - Block merges if tests fail

---

## Files Reference

- **Python Tests**: `/Users/davidsarno/unity-mcp/Server/tests/test_models_characterization.py` (933 lines)
- **C# Tests**: `/Users/davidsarno/unity-mcp/MCPForUnity/Editor/Models/Tests/Models_Characterization.cs` (888 lines)
- **Summary**: `/Users/davidsarno/unity-mcp/results/MODELS_CHARACTERIZATION_SUMMARY.md`
- **Index**: `/Users/davidsarno/unity-mcp/results/MODELS_TEST_INDEX.md` (this file)
- **Refactor Plan**: `/Users/davidsarno/unity-mcp/results/REFACTOR_PLAN.md` (P1-4, P2-3 items)
