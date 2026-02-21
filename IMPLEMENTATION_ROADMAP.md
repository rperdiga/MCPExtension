# Phased Implementation Plan — Closing MCP API Gaps

## Context

After systematically mapping all 50 MCP tools against the full Mendix Studio Pro 11.5 Extensions API, we identified significant gaps. However, deep exploration revealed critical constraints:

- **Flow control** (exclusive splits, loops, merges, error handlers) — NOT exposed in the typed API
- **Security** (access rules, module roles, project security) — Explicitly EXCLUDED from the API by design
- **Show Page / REST Call / Close Page** — NOT in the typed API
- **Log Message** — Confirmed not in the API (documented as SDK limitation)

What IS fully available and not yet implemented:
- **Rename** all model elements (entities, attributes, associations, microflows, modules, constants, enumerations)
- **Modify** existing elements (constant values, enumeration values, attribute types/defaults/lengths, association properties)
- **Domain Model Service** (cross-module association queries)
- **Navigation** (add pages to web navigation)
- **Page list/delete** (via IDocument)
- **Microflow manipulation** (insert before activity, delete activity, variable checks)

---

## Current State

- **50 registered tools** + 7 microflow activity sub-types
- **Last commit**: `7f8c965` — Fix 15 stress test bugs + enhance read_microflow_details
- **All stress test bugs fixed and verified**
- **Studio Pro 11.5** with extension deployed to `C:\Mendix Projects\Sample\extensions\MCP\`

---

## Files Modified in Every Phase

| File | Role |
|------|------|
| `Tools/MendixDomainModelTools.cs` | Domain model tool implementations |
| `Tools/MendixAdditionalTools.cs` | Microflow/page/config tool implementations |
| `Mcp/MendixMcpServer.cs` | Tool registration + tool count |
| `Mcp/McpServer.cs` | Tool descriptions + JSON schemas |

---

## Phase 13: Rename & Refactor (6 new tools)

**Goal**: Enable renaming of all major model elements. The API auto-updates all by-name references when renaming.

### New Tools

| # | Tool Name | What it does | API Used |
|---|-----------|-------------|----------|
| 1 | `rename_entity` | Rename an entity | `IEntity.Name = newName` |
| 2 | `rename_attribute` | Rename an attribute on an entity | `IAttribute.Name = newName` |
| 3 | `rename_association` | Rename an association | `IAssociation.Name = newName` |
| 4 | `rename_document` | Rename microflow, page, constant, enumeration, or any document | `IDocument.Name = newName` |
| 5 | `rename_module` | Rename a module (updates all qualified refs) | `IModule.Name = newName` |
| 6 | `rename_enumeration_value` | Rename a value within an enumeration | `IEnumerationValue.Name = newName` |

### Implementation Notes
- Each tool: find element → validate new name via `INameValidationService` → set `.Name` inside transaction
- `rename_document` is generic: accepts `document_type` param (microflow/page/constant/enumeration) + `document_name` + `new_name`
- `rename_entity` / `rename_attribute` / `rename_association` need entity/module resolution via existing `Utils.FindEntityAcrossModules()`

### Stress Test Plan — Phase 13
1. Rename entity "Customer" → "Client" in ECommerce module, verify `read_domain_model` shows new name
2. Rename attribute "FirstName" → "GivenName" on Client entity, verify
3. Rename association "Order_Customer" → "Order_Client", verify
4. Rename microflow "ACT_Order_Create" → "ACT_Order_New", verify `list_microflows`
5. Rename enumeration value "Draft" → "New" in OrderStatus, verify `list_enumerations`
6. Rename module "HRManagement" → "HR", verify all qualified names update
7. **Error cases**: Rename to empty string, rename to duplicate name, rename non-existent element
8. **Rollback**: Rename everything back to original names
9. `check_model` + `check_project_errors` after all renames

**Expected tool count after Phase 13**: 56

---

## Phase 14: Modify Existing Elements (5 new tools)

**Goal**: Enable modification of properties on existing model elements that currently can only be set at creation time.

### New Tools

| # | Tool Name | What it does | Key APIs |
|---|-----------|-------------|----------|
| 1 | `update_attribute` | Change attribute type, default value, string length, documentation | `IAttribute.Type`, `IStoredValue.DefaultValue`, `IStringAttributeType.Length` |
| 2 | `update_association` | Change owner, type (ref/refset), delete behaviors, documentation | `IAssociation.Owner`, `.Type`, `.ParentDeleteBehavior`, `.ChildDeleteBehavior` |
| 3 | `update_constant` | Change constant default value, exposure | `IConstant.DefaultValue`, `.ExposedToClient` |
| 4 | `update_enumeration` | Add/remove values from existing enumeration | `IEnumeration.AddValue()`, `.RemoveValue()` |
| 5 | `set_documentation` | Set documentation on entity, attribute, or association | `IEntity.Documentation`, `IAttribute.Documentation`, `IAssociation.Documentation` |

### Implementation Notes
- `update_attribute`: Must handle type changes carefully. Changing from String→Integer may lose data. Include `type`, `default_value`, `max_length` (for strings), `localize_date` (for DateTime), `enumeration` (for enum attrs) as optional params — only supplied params are changed.
- `update_association`: Optional params for `owner` (Parent/Child/Both), `type` (Reference/ReferenceSet), `parent_delete_behavior`, `child_delete_behavior`
- `update_enumeration`: Action param: `add_values` (array of new value names), `remove_values` (array of value names to remove)
- `update_constant`: Simple set operations

### Stress Test Plan — Phase 14
1. Change attribute type: String → Integer, verify `read_domain_model`
2. Set default value on String attribute, verify
3. Set max_length on String attribute to 200, verify
4. Change association type: Reference → ReferenceSet, verify
5. Change association owner, verify
6. Update constant value "50" → "100", toggle ExposedToClient
7. Add 2 new values to existing enumeration, remove 1, verify `list_enumerations`
8. Set documentation on entity, attribute, and association
9. **Error cases**: Invalid type change, remove non-existent enum value, update non-existent constant
10. `check_model` + `check_project_errors`

**Expected tool count after Phase 14**: 61

---

## Phase 15: Domain Model Service & Advanced Queries (2 new tools)

**Goal**: Expose the `IDomainModelService` for cross-module association queries and add navigation management.

### New Tools

| # | Tool Name | What it does | API Used |
|---|-----------|-------------|----------|
| 1 | `query_associations` | Find associations between entities, for an entity, or across modules | `IDomainModelService.GetAllAssociations()`, `.GetAssociationsBetweenEntities()`, `.GetAssociationsOfEntity()` |
| 2 | `manage_navigation` | Add pages to responsive web navigation profile | `INavigationManagerService.PopulateWebNavigationWith()` |

### Implementation Notes
- `query_associations`: Params — `entity_name` (optional), `second_entity` (optional), `module_name` (optional), `direction` (parent/child/both). Returns rich association details including parent/child entities, owner, type, delete behaviors.
- `manage_navigation`: Params — `pages` array of `{caption, page_name, module_name}`. Resolves pages, calls `PopulateWebNavigationWith`.
- Need to inject `IDomainModelService` into `MendixDomainModelTools` constructor (not currently injected)

### Stress Test Plan — Phase 15
1. Query all associations in ECommerce module
2. Query associations between Customer and Order
3. Query all associations of a specific entity (both directions)
4. Query across all modules
5. Add 2 pages to web navigation, verify via `query_model_elements` with Navigation type
6. **Error cases**: Query for non-existent entity, navigate with non-existent page
7. `check_model`

**Expected tool count after Phase 15**: 63

---

## Phase 16: Microflow Manipulation (3 new tools)

**Goal**: Enable modification and manipulation of existing microflow activities.

### New Tools

| # | Tool Name | What it does | API Used |
|---|-----------|-------------|----------|
| 1 | `delete_microflow_activity` | Remove an activity from a microflow by position | `GetAllMicroflowActivities()` + remove from object collection |
| 2 | `modify_microflow_activity` | Change properties on an existing activity (variable names, entity, expressions) | Cast to specific action type + set properties |
| 3 | `check_variable_name` | Check if a variable name is already in use in a microflow | `IMicroflowService.IsVariableNameInUse()` |

### Implementation Notes
- `delete_microflow_activity`: Find activity by position (1-based), remove it. May need untyped model access if typed API doesn't support removal.
- `modify_microflow_activity`: Identify activity by position, cast to specific type, update supplied properties. Complex — needs to handle each action type.
- `check_variable_name`: Simple utility — returns true/false + suggests alternative name
- These require careful investigation of whether the API supports activity removal (not just creation)

### Stress Test Plan — Phase 16
1. Check variable name "AllOrders" in DS_GetActiveOrders (should return in-use)
2. Check variable name "NewVar" (should return available)
3. Modify retrieve activity: change output variable name
4. Delete last activity from a microflow, verify with `read_microflow_details`
5. **Error cases**: Delete from empty microflow, modify non-existent position, invalid variable name
6. `check_model` + `check_project_errors`

**Expected tool count after Phase 16**: 66

---

## Phase 17: Page & Document Management (3 new tools)

**Goal**: Basic page listing, deletion, and module import capabilities.

### New Tools

| # | Tool Name | What it does | API Used |
|---|-----------|-------------|----------|
| 1 | `list_pages` | List all pages in a module with names and layout info | `module.GetDocuments().OfType<IPage>()` |
| 2 | `delete_document` | Delete any document (page, microflow, constant, enum) by name and type | `IFolderBase.RemoveDocument()` |
| 3 | `sync_filesystem` | Synchronize model with filesystem (JS actions, widgets) | `IAppService.SynchronizeWithFileSystem()` |

### Implementation Notes
- `list_pages`: Simple introspection — page name, excluded status. IPage has minimal properties beyond IDocument.
- `delete_document`: More general than existing `delete_model_element` which is entity/microflow focused. Handles pages, snippets, any IDocument.
- `sync_filesystem`: Useful after adding custom widgets or JS actions outside Studio Pro
- Need to inject `IAppService` (UI service) — check if available via DI in extension context

### Stress Test Plan — Phase 17
1. List all pages in ECommerce module
2. List pages across all modules
3. Delete a generated overview page (from stress test orphans)
4. Verify deletion via `list_pages` and `check_project_errors`
5. **Error cases**: Delete non-existent page, delete page in use by navigation
6. `check_model`

**Expected tool count after Phase 17**: 69

---

## Phase 18: Quality of Life Improvements (3 new tools)

**Goal**: Utility tools that improve the developer experience and close remaining typed API gaps.

### New Tools

| # | Tool Name | What it does | API Used |
|---|-----------|-------------|----------|
| 1 | `update_microflow` | Change microflow return type, add/remove parameters | `IMicroflow.ReturnType`, `IMicroflowService.Initialize()` |
| 2 | `read_attribute_details` | Deep read of a single attribute: type details, default value, string length, calculated microflow | `IAttribute.Type`, `IStoredValue`, `ICalculatedValue` |
| 3 | `configure_constant_values` | Set per-configuration constant value overrides | `IConfiguration.AddConstantValue()` |

### Implementation Notes
- `update_microflow`: Changing return type on existing microflow. Adding parameters may be complex (need to check if `Initialize()` can be called again or if manual parameter manipulation is needed).
- `read_attribute_details`: Complements `read_domain_model` with deep per-attribute info
- `configure_constant_values`: Set constant overrides per run configuration (e.g., different DB URL for Development vs Production)

### Stress Test Plan — Phase 18
1. Change microflow return type from Boolean to Void, verify
2. Read detailed attribute info for String attribute (check max length), DateTime (check localization)
3. Set constant value override in Development configuration
4. **Error cases**: Invalid return type, read non-existent attribute
5. `check_model` + `check_project_errors`

**Expected tool count after Phase 18**: 72

---

## Summary — Phase Roadmap

| Phase | Focus | New Tools | Running Total | Risk |
|-------|-------|-----------|---------------|------|
| **13** | Rename & Refactor | 6 | 56 | Low — simple property setters |
| **14** | Modify Existing Elements | 5 | 61 | Medium — type changes need care |
| **15** | Association Queries & Navigation | 2 | 63 | Low — read + simple service calls |
| **16** | Microflow Manipulation | 3 | 66 | High — activity deletion may need untyped API |
| **17** | Page & Document Management | 3 | 69 | Medium — UI service injection |
| **18** | Quality of Life | 3 | 72 | Medium — parameter manipulation |

---

## What CANNOT Be Implemented (API Limitations)

| Capability | Reason |
|-----------|--------|
| Exclusive Split / Decision | Not in typed API — no `IExclusiveSplit` interface |
| Loop / Iterator | Not in typed API — no `ILoopedActivity` interface |
| Merge node | Not in typed API |
| Error Handler / Try-Catch | Not in typed API |
| Show Page activity | Not in typed API — no `IShowPageAction` |
| Close Page activity | Not in typed API |
| REST Call activity | Not in typed API — no `IRestCallAction` |
| Web Service Call | Not in typed API |
| Cast Object | Not in typed API |
| Log Message activity | Confirmed not in API (documented) |
| Security roles & access rules | Explicitly excluded from API by design |
| Entity access rules | Explicitly excluded (`[CodeGeneration(ExcludeProperties)]`) |
| Validation rules | Explicitly excluded |
| Entity indexes | Explicitly excluded |
| Nanoflow creation | Not in typed API (read-only via untyped) |

---

## Execution Rules

- **One phase at a time** — complete implementation + stress test before starting next
- **Each phase**: implement → build → deploy → stress test → fix bugs → verify → move on
- **No phase skipping** — dependencies may exist between phases
- **Bug threshold**: Fix all Critical/Medium bugs before advancing; Low bugs can be deferred
- **Commit after each phase** — clean git history per phase
