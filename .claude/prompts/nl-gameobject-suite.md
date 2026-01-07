# Unity GameObject API Test Suite — Tool/Resource Separation

You are running inside CI for the `unity-mcp` repo. Use only the tools allowed by the workflow. Work autonomously; do not prompt the user. Do NOT spawn subagents.

**Print this once, verbatim, early in the run:**
AllowedTools: Write,mcp__UnityMCP__manage_editor,mcp__UnityMCP__manage_gameobject,mcp__UnityMCP__find_gameobjects,mcp__UnityMCP__manage_components,mcp__UnityMCP__manage_scene,mcp__UnityMCP__read_console

---

## Mission
1) Test the new Tool/Resource separation for GameObject management
2) Execute GO tests GO-0..GO-10 in order
3) Verify deprecation warnings appear for legacy actions
4) **Report**: write one `<testcase>` XML fragment per test to `reports/<TESTID>_results.xml`

**CRITICAL XML FORMAT REQUIREMENTS:**
- Each file must contain EXACTLY one `<testcase>` root element
- NO prologue, epilogue, code fences, or extra characters
- Use this exact shape:

<testcase name="GO-0 — Hierarchy with ComponentTypes" classname="UnityMCP.GO-T">
  <system-out><![CDATA[
(evidence of what was accomplished)
  ]]></system-out>
</testcase>

- If test fails, include: `<failure message="reason"/>`
- TESTID must be one of: GO-0, GO-1, GO-2, GO-3, GO-4, GO-5, GO-6, GO-7, GO-8, GO-9, GO-10

---

## Test Specs

### GO-0. Hierarchy with ComponentTypes
**Goal**: Verify get_hierarchy now includes componentTypes list
**Actions**:
- Call `mcp__UnityMCP__manage_scene(action="get_hierarchy", page_size=10)`
- Verify response includes `componentTypes` array for each item in `data.items`
- Check that Main Camera (or similar) has component types like `["Transform", "Camera", "AudioListener"]`
- **Pass criteria**: componentTypes present and non-empty for at least one item

### GO-1. Find GameObjects Tool
**Goal**: Test the new find_gameobjects tool
**Actions**:
- Call `mcp__UnityMCP__find_gameobjects(search_term="Camera", search_method="by_component")`
- Verify response contains `instanceIDs` array in `data`
- Verify response contains pagination info (`pageSize`, `cursor`, `totalCount`)
- **Pass criteria**: Returns at least one instance ID

### GO-2. GameObject Resource Read
**Goal**: Test reading a single GameObject via resource
**Actions**:
- Use the instance ID from GO-1
- Call `mcp__UnityMCP__read_resource(uri="mcpforunity://scene/gameobject/{instanceID}")` replacing {instanceID} with the actual ID
- Verify response includes: instanceID, name, tag, layer, transform, path
- **Pass criteria**: All expected fields present

### GO-3. Components Resource Read  
**Goal**: Test reading components via resource
**Actions**:
- Use the instance ID from GO-1
- Call `mcp__UnityMCP__read_resource(uri="mcpforunity://scene/gameobject/{instanceID}/components")` replacing {instanceID} with the actual ID
- Verify response includes paginated component list in `data.items`
- Verify at least one component has typeName and instanceID
- **Pass criteria**: Components list returned with proper pagination

### GO-4. Manage Components Tool - Add and Set Property
**Goal**: Test the new manage_components tool (add component, set property)
**Actions**:
- Create a test GameObject: `mcp__UnityMCP__manage_gameobject(action="create", name="GO_Test_Object")`
- Add a component: `mcp__UnityMCP__manage_components(action="add", target="GO_Test_Object", component_type="Rigidbody")`
- Set a property: `mcp__UnityMCP__manage_components(action="set_property", target="GO_Test_Object", component_type="Rigidbody", properties={"mass": 5.0})`
- Verify the component was added and property was set
- **Pass criteria**: Component added, property set successfully
- **Note**: Keep GO_Test_Object for GO-5 through GO-8

### GO-5. Find GameObjects by Name
**Goal**: Test find_gameobjects with by_name search method
**Actions**:
- Call `mcp__UnityMCP__find_gameobjects(search_term="GO_Test_Object", search_method="by_name")`
- Verify response contains the GameObject created in GO-4
- Verify pagination info is present
- **Pass criteria**: Returns at least one instance ID matching GO_Test_Object

### GO-6. Find GameObjects by Tag
**Goal**: Test find_gameobjects with by_tag search method
**Actions**:
- Set a tag on GO_Test_Object: `mcp__UnityMCP__manage_gameobject(action="modify", target="GO_Test_Object", tag="TestTag")`
- Call `mcp__UnityMCP__find_gameobjects(search_term="TestTag", search_method="by_tag")`
- Verify response contains the tagged GameObject
- **Pass criteria**: Returns at least one instance ID

### GO-7. Single Component Resource Read
**Goal**: Test reading a single component via resource
**Actions**:
- Get instance ID of GO_Test_Object from GO-5
- Call `mcp__UnityMCP__read_resource(uri="mcpforunity://scene/gameobject/{instanceID}/component/Rigidbody")` replacing {instanceID}
- Verify response includes component data with typeName="Rigidbody"
- Verify mass property is 5.0 (set in GO-4)
- **Pass criteria**: Component data returned with correct properties

### GO-8. Remove Component
**Goal**: Test manage_components remove action
**Actions**:
- Remove the Rigidbody from GO_Test_Object: `mcp__UnityMCP__manage_components(action="remove", target="GO_Test_Object", component_type="Rigidbody")`
- Verify the component was removed by attempting to read it again
- **Pass criteria**: Component successfully removed

### GO-9. Find with Pagination
**Goal**: Test find_gameobjects pagination
**Actions**:
- Call `mcp__UnityMCP__find_gameobjects(search_term="", search_method="by_name", page_size=2)`
- Verify response includes cursor for next page
- If cursor is present, call again with the cursor to get next page
- Clean up: `mcp__UnityMCP__manage_gameobject(action="delete", target="GO_Test_Object")`
- **Pass criteria**: Pagination works (cursor present when more results available)

### GO-10. Removed Actions Return Error
**Goal**: Verify legacy actions (find, get_components, etc.) return clear errors directing to new tools
**Actions**:
- Call removed action: `mcp__UnityMCP__manage_gameobject(action="find", search_term="Camera", search_method="by_component")`
- Verify response contains error indicating action is unknown/removed
- **Pass criteria**: Error response received (legacy actions were removed, not deprecated)

---

## Tool Reference

### New Tools
- `find_gameobjects(search_term, search_method, page_size?, cursor?, search_inactive?)` - Returns instance IDs only
- `manage_components(action, target, component_type?, properties?)` - Add/remove/set_property/get_all/get_single

### New Resources  
- `mcpforunity://scene/gameobject/{instanceID}` - Single GameObject data
- `mcpforunity://scene/gameobject/{instanceID}/components` - All components (paginated)
- `mcpforunity://scene/gameobject/{instanceID}/component/{componentName}` - Single component

### Updated Resources
- `manage_scene(action="get_hierarchy")` - Now includes `componentTypes` array in each item

---

## Transcript Minimization Rules
- Do not restate tool JSON; summarize in ≤ 2 short lines
- Per-test `system-out` ≤ 400 chars
- Console evidence: include ≤ 3 lines in the fragment

---

