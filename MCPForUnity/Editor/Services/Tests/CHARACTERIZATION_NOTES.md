# Services Characterization Tests

## Overview

This document describes the characterization tests written for the Unity Editor Integration domain (`MCPForUnity/Editor/Services/`). These tests capture **CURRENT BEHAVIOR** without refactoring, serving as a baseline for understanding and potentially refactoring this complex 34-file domain.

**Key insight:** The domain exhibits conflicting architectural patterns that need refactoring:
- Stateless services + EditorPrefs state persistence
- Lazy-loaded singletons without thread synchronization
- Static utility method proliferation
- Event-driven invalidation vs. request-driven state reads

## Test File Location

`MCPForUnity/Editor/Services/Tests/Services_Characterization.cs`

**Metrics:**
- **Total test methods:** 26
- **Total lines:** 1,158
- **Assertion format:** NUnit with extensive docstrings
- **Coverage approach:** Behavior documentation via docstrings, not code execution

## Services Analyzed

### 1. ServerManagementService (1,487 lines)
**Role:** Bridge control, state caching, communication with local HTTP server

**Patterns Captured:**
1. **Stateless service architecture**
   - No instance fields tracking state (only static diagnostic set)
   - All public methods are either static or instance methods that don't maintain per-service state
   - State flows through: EditorPrefs (persistent) + method parameters (transient) + process inspection (dynamic)

2. **Multi-detection strategy for server status**
   - Handshake validation: pidfile + instance token (deterministic)
   - Stored PID matching: EditorPrefs with 6-hour validity window
   - Heuristic process matching: "uvx", "python", "mcp-for-unity" detection
   - Fallback to network probe (50ms TCP connection attempt)

3. **Complex process termination logic**
   - Unix: Graceful SIGTERM (kill -15) with 8-second grace period, then SIGKILL (kill -9)
   - Windows: taskkill with /T flag (process tree), optional /F flag (forced)
   - Multiple safeguards: Never kills Unity Editor, validates process identity before termination

4. **State persistence patterns**
   - EditorPrefKeys: `LastLocalHttpServerPid`, `LastLocalHttpServerPort`, `LastLocalHttpServerStartedUtc`
   - Additional tracking: `LastLocalHttpServerPidArgsHash`, `LastLocalHttpServerPidFilePath`, `LastLocalHttpServerInstanceToken`
   - Purpose: Deterministic server identification across domain reloads

5. **Platform-specific command execution**
   - Windows: cmd.exe shells for netstat/tasklist/wmic
   - macOS: /bin/bash for ps command
   - Linux: Fallback terminal detection (gnome-terminal, xfce4-terminal, xterm)

**Test Coverage:**
- `ServerManagementService_IsStateless_NoInstanceFieldsTrackingState()`: Verifies no instance state
- `ServerManagementService_StoresLocalHttpServerMetadata_InEditorPrefs()`: EditorPrefs persistence
- `ServerManagementService_IsLocalHttpServerRunning_UsesMultiDetectionStrategy()`: Detection logic
- `ServerManagementService_IsLocalHttpServerReachable_UsesNetworkProbe()`: Fast reachability
- `ServerManagementService_TryGetLocalHttpServerCommand_BuildsUvxCommand()`: Command building
- `ServerManagementService_IsLocalUrl_MatchesLoopbackAddresses()`: URL validation
- `ServerManagementService_TerminateProcess_UsesGracefulThenForced_OnUnix()`: Process termination
- `ServerManagementService_LooksLikeMcpServerProcess_UsesMultiStrategyValidation()`: Process identity
- `ServerManagementService_StopLocalHttpServer_PrefersPidfileBasedApproach()`: Stop sequence
- `ServerManagementService_StoreLocalServerPidTracking_UsesArgHash()`: Command hash validation

### 2. EditorStateCache (static class)
**Role:** Maintains thread-safe cached snapshot of editor readiness state

**Patterns Captured:**
1. **InitializeOnLoad automatic initialization**
   - Static constructor runs when domain loads
   - Subscribes to EditorApplication.update (throttled 1s interval)
   - Subscribes to EditorApplication.playModeStateChanged (forced refresh)
   - Subscribes to AssemblyReloadEvents (tracks domain reload lifecycle)

2. **Thread-safe state with lock object**
   - `private static readonly object LockObj = new();`
   - Protects: `_cached` snapshot, `_sequence` counter, state fields
   - Used for: Concurrent access from UI thread + network thread + test runner thread

3. **Two-stage change detection**
   - **Stage 1 (fast):** Check compilation edge + throttle window
   - **Stage 2 (cheap):** Capture current scene, focus, play mode, asset state
   - **Stage 3 (comparison):** String/bool comparisons with last tracked state
   - **Stage 4 (expensive):** Call BuildSnapshot() only if state changed
   - **Optimization result:** Avoids JSON serialization 9 out of 10 updates

4. **Snapshot schema**
   - Sections: unity, editor, activity, compilation, assets, tests, transport
   - Fields: 40+ tracked state properties
   - Purpose: Enable bridge to detect editor readiness without polling Unity API

5. **Event-driven invalidation**
   - Play mode change: Force rebuild
   - Compilation edge: Bypass throttle
   - Domain reload: Track before/after timestamps
   - Default: Throttled to 1 second for performance

**Test Coverage:**
- `EditorStateCache_IsInitializedOnLoad_AndThreadSafe()`: Initialization pattern
- `EditorStateCache_BuildSnapshot_OnlyCalledWhenStateChanges()`: Change detection
- `EditorStateCache_SnapshotSchema_CoversEditorState()`: Schema documentation
- `EditorStateCache_UsesLockObjPattern_ForThreadSafety()`: Synchronization

### 3. BridgeControlService
**Role:** Facades TransportManager, resolves transport mode from config

**Patterns Captured:**
1. **Mode resolution from EditorPrefs**
   - Re-reads `EditorPrefKeys.UseHttpTransport` on each method call
   - No caching of mode decision (allows live config changes)
   - Fallback: Legacy `StdioBridgeHost.GetCurrentPort()` if state unavailable

2. **Mutual exclusion transport pattern**
   - StartAsync(): Stops opposing transport FIRST, then starts preferred
   - Prevents dual-bridge sessions (confusing state)
   - Legacy cleanup: Explicit StdioBridgeHost.Stop() call for Stdio

3. **Verification with ping + handshake**
   - VerifyAsync(): Async ping + state check
   - Verify(port): Sync variant with explicit port
   - Mode-specific validation: Stdio checks port match, HTTP assumes valid

**Test Coverage:**
- `BridgeControlService_ResolvesPreferredMode_FromEditorPrefs()`: Mode resolution
- `BridgeControlService_StartAsync_StopsOtherTransport_First()`: Mutual exclusion
- `BridgeControlService_VerifyAsync_ChecksBothPingAndHandshake()`: Verification

### 4. ClientConfigurationService
**Role:** Applies configuration to registered MCP clients

**Patterns Captured:**
1. **Single-pass configuration loop**
   - Clean build artifacts once (before any client config)
   - Iterate all registered clients
   - Catch exceptions per client (don't stop iteration)
   - Return summary with success/failure counts

2. **Entry points**
   - ConfigureAllDetectedClients(): Batch configuration
   - ConfigureClient(): Single client
   - CheckClientStatus(): Status query with optional auto-rewrite

**Test Coverage:**
- `ClientConfigurationService_ConfigureAllDetectedClients_RunsOnce()`: Configuration loop

### 5. MCPServiceLocator
**Role:** Lazy-initialized service locator without DI framework

**Patterns Captured:**
1. **Lazy initialization with null-coalescing operator**
   ```csharp
   private static IBridgeControlService _bridgeService;
   public static IBridgeControlService Bridge => _bridgeService ??= new BridgeControlService();
   ```
   - Simple and readable
   - **Race condition risk:** Not atomic, double-initialization possible
   - **Acceptable because:** Services are stateless/idempotent, editor is single-threaded

2. **Manual service registration for testing**
   - Register<T>(implementation): Type dispatch via if-else chain
   - Enables: `MCPServiceLocator.Register<IBridgeControlService>(mock);`
   - Design: Enforces interface types (no concrete type routing)

3. **Reset pattern for cleanup**
   - Calls Dispose() on each service (if IDisposable)
   - Sets all fields to null
   - Used in test teardown

**Test Coverage:**
- `MCPServiceLocator_UsesLazyInitializationPattern_WithoutLocking()`: Lazy pattern + race condition
- `MCPServiceLocator_Reset_DisposesAndClears_AllServices()`: Cleanup
- `MCPServiceLocator_Register_DispatchesByInterface_Type()`: Registration

## State Management Patterns

### EditorPrefs as Source of Truth
**Usage:** All configuration stored in EditorPrefs, read on every method call
- No service-level caching
- Automatic invalidation (next method call sees new value)
- Trade-off: Slight inefficiency vs. simplicity

**Keys used:**
- `UseHttpTransport`: HTTP vs Stdio transport selection
- `LastLocalHttpServerPid`, `LastLocalHttpServerPort`: Server tracking
- `LastLocalHttpServerStartedUtc`: PID validity window (6 hours)
- `LastLocalHttpServerPidArgsHash`: Process identity validation
- `LastLocalHttpServerPidFilePath`: Pidfile path for deterministic stop
- `LastLocalHttpServerInstanceToken`: Token for instance validation
- `ProjectScopedToolsLocalHttp`: Flag for scoped uv tools

### Snapshot-Based State (EditorStateCache)
**Purpose:** Fast reads without polling Unity APIs
- Rebuilt only on state changes
- Thread-safe with lock
- Available as JObject (JSON)

### Transient State (Process Inspection)
**Methods:** ps, netstat, lsof, tasklist, wmic
- Real-time process status (not cached)
- Platform-dependent implementations
- Fallback strategies for missing tools

## Race Condition Scenarios

### 1. MCPServiceLocator Double-Initialization
**Window:** Between null check and assignment in null-coalescing operator
**Timeline:**
1. T1 accesses Bridge property, finds null
2. T2 accesses Bridge property, finds null (before T1 assignment)
3. Both create instances, last assignment wins
4. First instance discarded (no resource leak in editor context)

**Mitigation:** Services are stateless/idempotent, acceptable risk for simplicity

### 2. EditorPrefs Read-Modify-Write in Stop Path
**Scenario:** Stop method updates EditorPrefs while domain reload in progress
**Risk:** Stale EditorPrefs state after reload
**Mitigation:** Stop clears all related keys atomically (multiple DeleteKey calls)

### 3. Process Termination Race
**Scenario:** Process exits between identity validation and kill command
**Risk:** Kill command fails (harmless)
**Mitigation:** All errors caught, logged, handled gracefully

## Configuration Application Flow

1. **User changes config in UI** → EditorPrefs.SetBool(key, value)
2. **Service method called** → Reads EditorPrefs.GetBool(key, default)
3. **Behavior reflects new config** → No cache invalidation needed
4. **Result visible immediately** → No event system overhead

**Pattern:** Implicit propagation via EditorPrefs reads

## Edge Cases Tested

1. **Pidfile doesn't exist yet** (fast server start before pidfile written)
   - Falls back to port-based detection
   - Retries with portOverride flag

2. **PID reuse after process exit** (OS reuses PID number)
   - Mitigated by 6-hour validity window
   - Verified by command-line hash matching

3. **Process identity validation failures** (CIM permission issues on Windows)
   - Falls back to heuristic matching
   - Stricter heuristic if token validation unavailable

4. **Port already in use by unrelated process**
   - Refuses to start server (asks user to manually free port)
   - Refuses to kill unrelated process

5. **Config changes during operation**
   - No cache invalidation (handled by stateless methods)
   - Next method call sees new config

## Issues and Blocking Problems Found

### 1. No Thread Synchronization in MCPServiceLocator
**Severity:** Low (acceptable for editor)
**Fix:** Use Lazy<T> pattern (requires minimal change)
**Note:** Documented as acceptable risk

### 2. Static Method Proliferation
**Severity:** Medium (refactoring target)
**Symptom:** ServerManagementService has 30+ static utility methods
**Examples:** NormalizeForMatch, QuoteIfNeeded, ComputeShortHash, etc.
**Fix:** Extract to separate UtilityService class

### 3. EditorPrefs as State Persistence Without Versioning
**Severity:** Medium (potential for stale state)
**Risk:** Config format changes could leave stale EditorPrefs keys
**Mitigation:** ClearLocalServerPidTracking() call at strategic points
**Fix:** Implement EditorPrefs schema versioning

### 4. No Service Lifecycle Management
**Severity:** Medium (shutdown cleanup)
**Symptom:** Services don't cleanup resources (open files, network connections)
**Risk:** Orphaned resources on domain reload
**Fix:** Implement IDisposable and call Reset() in shutdown handlers

### 5. Platform-Specific Code Without Tests
**Severity:** Medium (regressive behavior)
**Note:** Windows/macOS/Linux code paths largely untested
**Fix:** Add integration tests for each platform

### 6. Handshake State Can Get Stale
**Severity:** Low (graceful fallback)
**Scenario:** Server starts but pidfile never written (race condition)
**Fallback:** Port-based detection works but less deterministic
**Fix:** Implement timeout-based cleanup of stale handshake state

## Recommendations for Future Refactoring

1. **Extract utility methods** from ServerManagementService
   - Create UtilityService with: NormalizeForMatch, QuoteIfNeeded, ComputeShortHash, etc.
   - Reduces ServerManagementService from 1,487 to ~800 lines

2. **Implement proper DI** instead of service locator
   - Use Zenject or manual constructor injection
   - Eliminates race conditions and enables better testing

3. **Add service lifecycle** hooks
   - Implement IDisposable on services
   - Call Reset() during shutdown
   - Cleanup resources properly

4. **Cache InvalidationService**
   - Centralized invalidation for EditorPrefs changes
   - Services subscribe to invalidation events
   - Reduces per-method EditorPrefs reads

5. **Process management service**
   - Extract ProcessUtility into dedicated service
   - Testable without OS process access
   - Mockable for unit tests

6. **Event-driven architecture**
   - Services communicate via events not direct calls
   - Reduces coupling between BridgeControlService and ServerManagementService
   - Enables reactive updates

## Test Execution Notes

These tests are **characterization tests**, not unit tests:
- Most assertions use `Assert.Pass()` with documentation
- No actual test execution required initially
- Serve as reference for understanding current behavior
- Should be converted to proper unit tests during refactoring

To run: `dotnet test` or Unity Test Runner in Editor

## References

- **ServerManagementService:** `/MCPForUnity/Editor/Services/ServerManagementService.cs` (1,487 lines)
- **EditorStateCache:** `/MCPForUnity/Editor/Services/EditorStateCache.cs` (500+ lines)
- **BridgeControlService:** `/MCPForUnity/Editor/Services/BridgeControlService.cs` (157 lines)
- **ClientConfigurationService:** `/MCPForUnity/Editor/Services/ClientConfigurationService.cs` (73 lines)
- **MCPServiceLocator:** `/MCPForUnity/Editor/Services/MCPServiceLocator.cs` (93 lines)

## Appendix: Test Index

| Test # | Service | Pattern | Focus |
|--------|---------|---------|-------|
| 1 | SMS | Stateless | Instance fields |
| 2 | SMS | State persistence | EditorPrefs storage |
| 3 | SMS | Detection | Multi-strategy approach |
| 4 | SMS | Reachability | Network probe |
| 5 | SMS | Command building | Platform-specific |
| 6 | SMS | URL validation | Loopback matching |
| 7 | SMS | Process termination | Platform-specific behavior |
| 8 | SMS | Identity validation | Multi-layer checks |
| 9 | SMS | Stop sequence | Deterministic path |
| 10 | SMS | Hash validation | PID reuse prevention |
| 11 | ESC | Initialization | InitializeOnLoad |
| 12 | ESC | Change detection | Two-stage approach |
| 13 | ESC | Schema | Coverage |
| 14 | ESC | Thread safety | Lock pattern |
| 15 | BCS | Mode resolution | EditorPrefs read |
| 16 | BCS | Transport exclusion | Mutual exclusion |
| 17 | BCS | Verification | Ping + handshake |
| 18 | CCS | Configuration | Single-pass loop |
| 19 | MSL | Lazy initialization | Null-coalescing |
| 20 | MSL | Cleanup | Reset pattern |
| 21 | MSL | Registration | Type dispatch |
| 22 | Consistency | State | EditorStateCache + BCS |
| 23 | Race | Locator | Double-initialization |
| 24 | Invalidation | Config | EditorPrefs changes |
| 25 | Init | Domain | Load sequence |
| 26 | Config | Flow | EditorPrefs to behavior |

**Legend:** SMS=ServerManagementService, ESC=EditorStateCache, BCS=BridgeControlService, CCS=ClientConfigurationService, MSL=MCPServiceLocator
