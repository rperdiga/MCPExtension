# Mendix Studio Pro MCP Extension

A C# extension for **Mendix Studio Pro 11.5** that exposes the full modeling API through a **Model Context Protocol (MCP) server** over HTTP/SSE. Enables AI tools (Claude, Cursor, Copilot, etc.) to read, create, modify, and manage Mendix application models programmatically.

**83 tools** across domain modeling, microflows, pages, security, workflows, and more.

## Mendix Marketplace Module

A pre-built version for **Mendix 10.24.2** is available in the `.github/Mendix Marketplace Module/` folder with installation instructions.

## Quick Start

### Prerequisites

- Mendix Studio Pro 11.5+
- .NET 8.0 SDK
- `--enable-extension-development` flag on Studio Pro

### Build & Deploy

```bash
dotnet build MCPExtension.csproj
```

Post-build automatically copies to `{YourProject}/extensions/MCP/`.

### Launch Studio Pro

All flags are required:

```bash
studiopro.exe "YourProject.mpr" \
  --enable-extension-development \
  --enable-universal-maia \
  --enable-microflow-generation \
  --enable-workflow-generation \
  --enable-maia-session-story-attachment
```

### Start the MCP Server

Open the **MCP dockable pane** in Studio Pro (the server starts when the pane opens).

### Connect

| Endpoint | URL |
|----------|-----|
| SSE | `http://localhost:3001/sse` |
| Messages | `http://localhost:3001/message` |
| Health | `http://localhost:3001/health` |
| MCP Metadata | `http://localhost:3001/.well-known/mcp` |

Port auto-increments from 3001 if occupied.

## Architecture

| Component | File | Purpose |
|-----------|------|---------|
| Entry Point | `AIAPIEngine.cs` | DockablePaneExtension, starts MCP server on pane open |
| MCP Server | `Mcp/McpServer.cs` | HTTP/SSE server, tool descriptions & JSON schemas |
| Tool Wiring | `Mcp/MendixMcpServer.cs` | Registers all 83 tools, routes calls to implementations |
| Domain Tools | `Tools/MendixDomainModelTools.cs` | Entities, associations, attributes, domain model ops |
| Additional Tools | `Tools/MendixAdditionalTools.cs` | Microflows, pages, security, workflows, settings |
| Utilities | `Utils/Utils.cs` | Module/entity resolution helpers |

### Dependency Injection (MEF)

Services injected via `[ImportingConstructor]`:
- `IMicroflowService`, `IMicroflowExpressionService`, `IMicroflowActivitiesService`
- `IPageGenerationService`, `INavigationManagerService`
- `INameValidationService`, `IVersionControlService` (optional)
- `IUntypedModelAccessService` (optional, for metamodel escape-hatch)

## Tool Reference (83 Tools)

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

### Constants & Enumerations (6 tools)

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

### Module & Folder Management (3 tools)

| Tool | Description |
|------|-------------|
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

### Meta & Discovery (4 tools)

| Tool | Description |
|------|-------------|
| `list_available_tools` | List all 83 tools with capabilities |
| `debug_info` | Comprehensive domain model debug info with usage examples |
| `list_scheduled_events` | List scheduled events with interval and status |
| `list_rest_services` | List published REST services with paths and authentication |
| `query_model_elements` | Generic metamodel escape-hatch: query any type by name |

## Usage Examples

### Create a Domain Model

```json
{
  "name": "tools/call",
  "params": {
    "name": "create_domain_model_from_schema",
    "arguments": {
      "module_name": "CRM",
      "entities": [
        {
          "entity_name": "Customer",
          "attributes": [
            {"name": "firstName", "type": "String"},
            {"name": "lastName", "type": "String"},
            {"name": "email", "type": "String"},
            {"name": "isActive", "type": "Boolean"}
          ]
        },
        {
          "entity_name": "Order",
          "entityType": "storecreateddate",
          "attributes": [
            {"name": "orderNumber", "type": "String"},
            {"name": "totalAmount", "type": "Decimal"}
          ]
        }
      ],
      "associations": [
        {
          "name": "Order_Customer",
          "parent": "Order",
          "child": "Customer",
          "type": "Reference"
        }
      ]
    }
  }
}
```

### Create a Microflow with Activities

```json
{
  "name": "create_microflow",
  "arguments": {
    "module_name": "CRM",
    "microflow_name": "CreateCustomer",
    "parameters": [
      {"name": "Name", "type": "String"},
      {"name": "Email", "type": "String"}
    ],
    "return_type": "Boolean"
  }
}
```

### Inspect a Page

```json
{
  "name": "read_page_details",
  "arguments": {
    "page_name": "Customer_NewEdit",
    "module_name": "CRM"
  }
}
```

### Security Audit

```json
{
  "name": "audit_security",
  "arguments": {}
}
```

## Known Limitations

### Write Capabilities

The Extensions API has limited write support for some model element types:

- **Pages**: Only overview page generation (`GenerateOverviewPages`). No widget-level creation/editing — use read tools for introspection
- **Nanoflows**: Read-only introspection via untyped model. No creation API
- **Workflows**: Read-only introspection via untyped model. No creation/editing API
- **Log Message Activity**: `CreateLogMessageActivity` does not exist in the Extensions API
- **IDomainModelService**: Not injectable via MEF (crashes on load). Use `entity.GetAssociations()` instead

### Microflow Activity Quirks

- `retrieve` and `change_object` activity types in `create_microflow_activities` may create the wrong action type — use `modify_microflow_activity` to fix after creation
- `output_variable` parameter is ignored during activity creation — set it via `modify_microflow_activity`
- `return_type` on `create_microflow` may not apply — use `update_microflow` after creation

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Server won't start | Open the MCP dockable pane in Studio Pro |
| Extension not loading | Verify all `--enable-*` flags on Studio Pro launch |
| Port conflict | Server auto-finds next available port from 3001 |
| Connection refused | Check `http://localhost:3001/health` and Windows Firewall |
| Tool errors | Use `get_last_error` for stack traces, `check_project_errors` for CE codes |
| Entity type errors | Ensure AIExtension module has required templates (NPE, FileDocument, Image, etc.) |

### Logs

- MCP debug log: `{MendixProjectPath}/resources/mcp_debug.log`
- Studio Pro logs: accessible via `get_studio_pro_logs` tool

## Development

### Adding a New Tool

1. Implement in `MendixDomainModelTools.cs` or `MendixAdditionalTools.cs`
2. Register in `MendixMcpServer.cs` via `_mcpServer.RegisterTool()`
3. Add description in `McpServer.GetToolDescription()`
4. Add JSON schema in `McpServer.GetToolInputSchema()`
5. Update `ListAvailableTools` arrays in both tool classes
6. Update `registeredTools` count in `MendixMcpServer.GetStatusAsync()`

### Dependencies

- .NET 8.0
- Mendix.StudioPro.ExtensionsAPI (11.5)
- Microsoft.AspNetCore (HTTP/SSE server)
- System.Text.Json

## License

Experimental — provided as-is for research and development purposes.
