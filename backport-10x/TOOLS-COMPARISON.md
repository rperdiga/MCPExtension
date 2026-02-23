# MCPExtension Tools Comparison: Before vs After Backport

**Old Version** (commit f932d76e, Aug 13 2025): **17 tools**
**Current Version** (backported to Studio Pro 10.24.13): **83 tools** — a **4.9x increase**

---

## Original 17 Tools (available in both versions)

| # | Tool | Description |
|---|------|-------------|
| 1 | `read_domain_model` | Read domain model structure with generalizations, event handlers, attributes, associations |
| 2 | `create_entity` | Create a new entity in the domain model |
| 3 | `create_association` | Create association between entities, supports cross-module |
| 4 | `delete_model_element` | Delete entity, attribute, association, microflow, constant, or enumeration |
| 5 | `diagnose_associations` | Diagnose association creation issues |
| 6 | `create_multiple_entities` | Create multiple entities at once |
| 7 | `create_multiple_associations` | Create multiple associations at once |
| 8 | `create_domain_model_from_schema` | Create complete domain model from schema definition |
| 9 | `save_data` | Generate sample data for domain model entities |
| 10 | `generate_overview_pages` | Generate overview pages for entities |
| 11 | `list_microflows` | List all microflows in a module |
| 12 | `get_last_error` | Get details about the last error |
| 13 | `list_available_tools` | List all available tools |
| 14 | `debug_info` | Get comprehensive debug info about domain model |
| 15 | `read_microflow_details` | Get details about a microflow: params, return type, activities |
| 16 | `create_microflow` | Create a new microflow with parameters and return type |
| 17 | `create_microflow_activities` | Add activities to a microflow (retrieve, create, change, commit, delete, etc.) |

---

## 66 NEW Tools (added in Phases 1-26)

| # | Tool | Phase | Description |
|---|------|-------|-------------|
| 18 | `list_modules` | 1 | List all modules with metadata |
| 19 | `create_module` | 1 | Create a new module |
| 20 | `set_entity_generalization` | 2 | Set entity inheritance |
| 21 | `remove_entity_generalization` | 2 | Remove generalization from an entity |
| 22 | `add_event_handler` | 2 | Add before/after event handler to an entity |
| 23 | `add_attribute` | 3 | Add attribute to entity (all types incl. Enum, Binary, HashedString) |
| 24 | `set_calculated_attribute` | 3 | Make attribute calculated by microflow |
| 25 | `create_constant` | 5 | Create a new constant |
| 26 | `list_constants` | 5 | List all constants |
| 27 | `create_enumeration` | 5 | Create a new enumeration |
| 28 | `list_enumerations` | 5 | List all enumerations with values |
| 29 | `read_project_info` | 7 | Comprehensive project overview with counts |
| 30 | `check_model` | 8 | Validate model for broken refs, missing microflows |
| 31 | `get_studio_pro_logs` | 8 | Read Studio Pro and MCP error logs |
| 32 | `check_project_errors` | 8 | Run mx.exe consistency check on saved MPR |
| 33 | `configure_system_attributes` | 9 | Toggle CreatedDate, ChangedDate, Owner, ChangedBy |
| 34 | `manage_folders` | 9 | Create, list, or move documents between folders |
| 35 | `validate_name` | 9 | Validate candidate name for model elements |
| 36 | `copy_model_element` | 9 | Deep-copy entity, microflow, constant, or enumeration |
| 37 | `list_java_actions` | 9 | List Java actions with parameters |
| 38 | `read_runtime_settings` | 10 | Read After Startup, Before Shutdown, Health Check |
| 39 | `set_runtime_settings` | 10 | Assign microflows to runtime hooks |
| 40 | `read_configurations` | 10 | List run configurations with settings |
| 41 | `set_configuration` | 10 | Create/update a run configuration |
| 42 | `read_version_control` | 10 | Read VC status, branch, and head commit |
| 43 | `set_microflow_url` | 11 | Read/set microflow REST URL |
| 44 | `list_rules` | 11 | List validation rules across modules |
| 45 | `exclude_document` | 11 | Mark document as excluded from compilation |
| 46 | `read_security_info` | 12/23 | Read project & module security config |
| 47 | `read_entity_access_rules` | 23 | Read entity-level access rules and member rights |
| 48 | `read_microflow_security` | 23 | Read microflow execution roles |
| 49 | `audit_security` | 23 | Security gap analysis |
| 50 | `read_nanoflow_details` | 24 | Read nanoflow params, activities, security |
| 51 | `list_nanoflows` | 12/24 | List all nanoflows with details |
| 52 | `list_scheduled_events` | 12 | List scheduled events with intervals |
| 53 | `list_rest_services` | 12 | List published REST services |
| 54 | `query_model_elements` | 12 | Generic metamodel query escape-hatch |
| 55 | `rename_entity` | 13 | Rename entity (auto-updates references) |
| 56 | `rename_attribute` | 13 | Rename attribute (auto-updates references) |
| 57 | `rename_association` | 13 | Rename association (auto-updates references) |
| 58 | `rename_document` | 13 | Rename any document (auto-updates references) |
| 59 | `rename_module` | 13 | Rename module (auto-updates all qualified refs) |
| 60 | `rename_enumeration_value` | 13 | Rename an enumeration value |
| 61 | `update_attribute` | 14 | Modify attribute type, default value, length |
| 62 | `update_association` | 14 | Modify association owner, type, delete behaviors |
| 63 | `update_constant` | 14 | Modify constant value and visibility |
| 64 | `update_enumeration` | 14 | Add/remove enumeration values |
| 65 | `set_documentation` | 14 | Set documentation on entity/attribute/association |
| 66 | `query_associations` | 15 | Cross-module association queries with direction filter |
| 67 | `manage_navigation` | 15 | Add pages to responsive web navigation |
| 68 | `check_variable_name` | 16 | Check variable name availability in microflow |
| 69 | `modify_microflow_activity` | 16 | Modify existing activity properties by position |
| 70 | `insert_before_activity` | 16 | Insert new activity before a position |
| 71 | `list_pages` | 17/25 | List pages with widget count, layout, parameters |
| 72 | `read_page_details` | 25 | Deep page introspection: widget tree, data sources, bindings |
| 73 | `list_workflows` | 26 | List workflows with context entity and activity count |
| 74 | `read_workflow_details` | 26 | Deep workflow introspection: activities, outcomes, security |
| 75 | `delete_document` | 17 | Delete page, microflow, or any document |
| 76 | `sync_filesystem` | 17 | Synchronize model with file system |
| 77 | `update_microflow` | 18 | Update microflow return type, URL |
| 78 | `read_attribute_details` | 18 | Detailed single-attribute info |
| 79 | `configure_constant_values` | 18 | Set constant value per run configuration |
| 80 | `generate_sample_data` | 19 | Auto-generate sample data + import pipeline |
| 81 | `read_sample_data` | 19 | Read previously saved sample data |
| 82 | `setup_data_import` | 19b | Wire up data import pipeline (After Startup) |
| 83 | `arrange_domain_model` | 20 | Smart association-aware entity layout |

---

## Compatibility Notes

- **API Compatibility**: Mendix Extensions API is 99% identical between Studio Pro 10.24.13 and 11.5
- **All 83 tools verified working** on Studio Pro 10.24.13
- **NuGet Package**: `Mendix.StudioPro.ExtensionsAPI v10.21.1` (same for both versions)
- **Only known API difference**: `IAppService.TryImportModule()` parameter type — not used by MCPExtension
