# SPMCP — Mendix Studio Pro MCP Extension

A **Mendix Studio Pro extension** that exposes the full modeling API through a **Model Context Protocol (MCP) server** over HTTP/SSE. Enables AI tools (Claude, Cursor, Copilot, etc.) to read, create, modify, and manage Mendix application models programmatically.

**84 tools** across domain modeling, microflows, pages, security, workflows, and more.

---

## Installation (Studio Pro 10.24.13)

### Step 1 — Import the module

1. Download [`dist/SPMCP.mpk`](dist/SPMCP.mpk)
2. In Studio Pro, go to **App > Import Module Package** and select `SPMCP.mpk`
3. Accept the import — the **SPMCP** module will appear in your project

### Step 2 — Enable extension development

Extensions require the `--enable-extension-development` flag when launching Studio Pro.

**Option A — Shortcut (recommended):** Right-click your Studio Pro shortcut > **Properties** > append the flag to the **Target** field:

```
"C:\Users\...\studiopro.exe" --enable-extension-development
```

**Option B — Command line:**

```bash
studiopro.exe "YourProject.mpr" --enable-extension-development
```

### Step 3 — Start the MCP server

Open the **SPMCP dockable pane** in Studio Pro (View menu or toolbar). The MCP server starts automatically when the pane opens.

Verify it's running:

```
http://localhost:3001/health
```

### Step 4 — Connect your AI tool

| Transport | Endpoint | Description |
|-----------|----------|-------------|
| Streamable HTTP | `http://localhost:3001/mcp` | Primary (MCP spec 2025-03-26) |
| SSE (legacy) | `http://localhost:3001/sse` | Legacy SSE transport (MCP spec 2024-11-05) |
| Health | `http://localhost:3001/health` | Health check |
| Metadata | `http://localhost:3001/.well-known/mcp` | Server capabilities |

Default port is 3001. Change it via the settings button in the SPMCP pane.

---

## Tool Reference (84 Tools)

### Domain Model — CRUD (15 tools)

| Tool | Description |
|------|-------------|
| `read_domain_model` | Read full domain model: entities, attributes, associations, generalizations, event handlers |
| `create_entity` | Create entity with attributes. Supports 9 types: persistent, non-persistent, filedocument, image, audit trail variants |
| `create_association` | Create association between entities. Cross-module supported |
| `create_multiple_entities` | Bulk entity creation with per-entity module targeting |
| `create_multiple_associations` | Bulk association creation with cross-module support |
| `create_domain_model_from_schema` | Create complete domain model from JSON schema |
| `delete_model_element` | Delete entity, attribute, association, or microflow |
| `add_attribute` | Add attribute to existing entity (all types including Binary, HashedString, Long) |
| `set_calculated_attribute` | Make attribute calculated via microflow |
| `set_entity_generalization` | Set entity inheritance (cross-module) |
| `remove_entity_generalization` | Remove entity inheritance |
| `add_event_handler` | Add before/after event handler (create/commit/delete/rollback) |
| `configure_system_attributes` | Toggle system attrs: HasCreatedDate, HasChangedDate, HasOwner, HasChangedBy |
| `diagnose_associations` | Troubleshoot association creation issues |
| `arrange_domain_model` | Smart layout of entities on domain model canvas |

### Domain Model — Modify & Rename (11 tools)

| Tool | Description |
|------|-------------|
| `update_attribute` | Change type, length, default value, or enumeration of existing attribute |
| `update_association` | Change owner, type, delete behavior of existing association |
| `rename_entity` | Rename entity (auto-updates all references) |
| `rename_attribute` | Rename attribute (auto-updates all references) |
| `rename_association` | Rename association (auto-updates all references) |
| `rename_document` | Rename any document (microflow, page, constant, etc.) |
| `rename_module` | Rename a module (auto-updates qualified references) |
| `rename_enumeration_value` | Rename enumeration value |
| `set_documentation` | Set documentation on entity, attribute, association, or domain model |
| `read_attribute_details` | Detailed attribute info: type details, validation, access rights |
| `query_associations` | Query associations by module, entity pair, or direction |

### Microflows (14 tools)

| Tool | Description |
|------|-------------|
| `list_microflows` | List microflows with metadata |
| `create_microflow` | Create microflow with parameters and return type |
| `read_microflow_details` | Full microflow inspection: parameters, return type, all activities |
| `create_microflow_activities` | Create activity sequences (create_object, change_object, retrieve, commit, delete, show_message, close_page) |
| `update_microflow` | Update return type, return variable, URL |
| `modify_microflow_activity` | Modify existing activity properties by position |
| `insert_before_activity` | Insert activity before a specific position |
| `check_variable_name` | Check if variable name is available in a microflow |
| `set_microflow_url` | Expose microflow as REST endpoint |
| `list_rules` | List validation rules across modules |
| `exclude_document` | Mark document as excluded/included |
| `copy_model_element` | Deep-copy entity, microflow, constant, or enumeration |
| `list_java_actions` | List Java actions with parameters |
| `validate_name` | Validate candidate name for model elements |

### Constants & Enumerations (7 tools)

| Tool | Description |
|------|-------------|
| `create_constant` | Create constant (string/integer/boolean/decimal/datetime) |
| `list_constants` | List constants across modules |
| `update_constant` | Modify constant default value or exposed_to_client flag |
| `configure_constant_values` | Set constant value overrides per run configuration |
| `create_enumeration` | Create enumeration with values and captions |
| `list_enumerations` | List enumerations with all values |
| `update_enumeration` | Add/remove enumeration values |

### Pages (4 tools)

| Tool | Description |
|------|-------------|
| `list_pages` | List pages with widget count, layout, parameters, documentation |
| `read_page_details` | Full page inspection: widget tree, data sources, parameters, actions |
| `generate_overview_pages` | Generate CRUD overview pages for entities |
| `delete_document` | Delete page, microflow, or any document from module |

### Nanoflows (2 tools)

| Tool | Description |
|------|-------------|
| `list_nanoflows` | List nanoflows with return type, activity count, parameters |
| `read_nanoflow_details` | Full nanoflow inspection: parameters, activities, actions |

### Workflows (2 tools)

| Tool | Description |
|------|-------------|
| `list_workflows` | List workflows with context entity, activity count, documentation |
| `read_workflow_details` | Full workflow inspection: activities (UserTasks, Decisions, SystemActivities), flows, security |

### Security (4 tools)

| Tool | Description |
|------|-------------|
| `read_security_info` | Project/module security: user roles, module roles, password policy |
| `read_entity_access_rules` | Entity access rules: CRUD permissions, XPath constraints, member rights |
| `read_microflow_security` | Microflow execution permissions by role |
| `audit_security` | Gap analysis: entities without access rules, overly permissive rules |

### Project & Settings (7 tools)

| Tool | Description |
|------|-------------|
| `read_project_info` | Project overview: all modules with entity/microflow/page counts |
| `read_runtime_settings` | Read after-startup, before-shutdown, health-check microflows |
| `set_runtime_settings` | Assign/clear runtime hook microflows |
| `read_configurations` | List run configurations with settings and constant overrides |
| `set_configuration` | Create/update run configuration |
| `read_version_control` | Version control status: branch, commit, VC type |
| `manage_navigation` | Add pages to responsive web navigation |

### Module & Folder Management (4 tools)

| Tool | Description |
|------|-------------|
| `list_modules` | List all modules in the project with metadata (name, source, entity count) |
| `create_module` | Create new module |
| `manage_folders` | Create, list, or move documents between folders |
| `sync_filesystem` | Import changes from JavaScript actions, widgets, external files |

### Data & Diagnostics (8 tools)

| Tool | Description |
|------|-------------|
| `save_data` | Generate sample data with entity relationships |
| `generate_sample_data` | Auto-generate realistic sample data from domain model schema |
| `read_sample_data` | Read previously saved sample data |
| `setup_data_import` | Wire up sample data import pipeline with Java action |
| `check_model` | Validate model for broken generalizations, missing handlers, etc. |
| `check_project_errors` | Run mx.exe consistency check (CE error codes) |
| `get_studio_pro_logs` | Read Studio Pro and MCP extension logs |
| `get_last_error` | Get last error details with stack trace |

### Meta & Discovery (6 tools)

| Tool | Description |
|------|-------------|
| `list_available_tools` | List all 84 tools with capabilities |
| `debug_info` | Comprehensive domain model debug info with usage examples |
| `list_scheduled_events` | List scheduled events with interval and status |
| `list_rest_services` | List published REST services with paths and authentication |
| `query_model_elements` | Generic metamodel escape-hatch: query any type by name |
| `analyze_project_patterns` | Analyze naming conventions, structural patterns, and best practices across modules. Optionally writes a skill file to `.claude/skills/` so future AI sessions follow the project's established conventions |

---

## MCP Endpoints

```
http://localhost:3001/sse       SSE stream (connect here from Claude/Cursor)
http://localhost:3001/message   POST endpoint for tool calls
http://localhost:3001/health    Server health check
```

---

## Setting Up Sample Data Import (After Startup)

The SPMCP module includes a Java action (`SPMCP.InsertDataFromJSON`) that loads sample data into your app on startup. After generating sample data via the `generate_sample_data` MCP tool, wire it up as follows:

### Using the MCP tool (recommended)

Call `generate_sample_data` — it auto-creates the `ASu_LoadSampleData` microflow and wires it to **After Startup** in one call:

```json
{
  "name": "generate_sample_data",
  "arguments": {
    "module_names": ["MyFirstModule"],
    "auto_setup": true
  }
}
```

If an **After Startup** microflow already exists, use `force_after_startup`:

```json
{
  "name": "setup_data_import",
  "arguments": {
    "force_after_startup": true
  }
}
```

### Manual wiring

1. In Studio Pro, open **App Settings** > **Runtime** tab
2. Set **After startup** to `SPMCP.ASu_LoadSampleData`
3. Run the app — sample data loads on first startup and the JSON file is deleted on success

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Extension not visible | Ensure `--enable-extension-development` flag is set on Studio Pro launch |
| Server won't start | Open the SPMCP dockable pane in Studio Pro (View menu) |
| Port conflict | Server auto-finds next available port from 3001 |
| Connection refused | Check `http://localhost:3001/health` and Windows Firewall |
| Tool errors | Use `get_last_error` for stack traces, `check_project_errors` for CE codes |
| Sample data not loading | Verify `SPMCP.ASu_LoadSampleData` is set as After Startup in App Settings |

### Logs

- MCP debug log: `{MendixProjectPath}/resources/mcp_debug.log`
- Studio Pro logs: accessible via `get_studio_pro_logs` tool

---

## Known Limitations

- **Pages**: Only overview page generation. No widget-level creation/editing via Extensions API
- **Nanoflows**: Read-only introspection. No creation API
- **Workflows**: Read-only introspection. No creation/editing API
- **Log Message Activity**: `CreateLogMessageActivity` does not exist in the Extensions API
- `retrieve` and `change_object` in `create_microflow_activities` may produce wrong action type — use `modify_microflow_activity` to correct after creation

---

## License

Experimental — provided as-is for research and development purposes.
