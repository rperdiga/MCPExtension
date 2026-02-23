using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using Mendix.StudioPro.ExtensionsAPI.Model.Constants;
using Mendix.StudioPro.ExtensionsAPI.Model.DataTypes;
using Mendix.StudioPro.ExtensionsAPI.Model.Enumerations;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows;
using Mendix.StudioPro.ExtensionsAPI.Model.Settings;
using Mendix.StudioPro.ExtensionsAPI.Model.Texts;
using Microsoft.Extensions.Logging;
using MCPExtension.Utils;

namespace MCPExtension.Tools
{
    public class MendixDomainModelTools
    {
        private readonly IModel _model;
        private readonly ILogger<MendixDomainModelTools> _logger;
        private readonly Mendix.StudioPro.ExtensionsAPI.Services.INameValidationService? _nameValidationService;

        public MendixDomainModelTools(IModel model, ILogger<MendixDomainModelTools> logger, Mendix.StudioPro.ExtensionsAPI.Services.INameValidationService? nameValidationService = null)
        {
            _model = model;
            _logger = logger;
            _nameValidationService = nameValidationService;
        }

        public async Task<string> ListModules(JsonObject parameters)
        {
            try
            {
                var allModules = _model.Root.GetModules();
                var moduleList = allModules
                    .Where(m => m != null)
                    .Select(m => new
                    {
                        name = m.Name,
                        fromAppStore = m.FromAppStore,
                        entityCount = m.DomainModel?.GetEntities().Count() ?? 0
                    })
                    .OrderBy(m => m.fromAppStore)
                    .ThenBy(m => m.name)
                    .ToList();

                var result = new
                {
                    success = true,
                    message = $"Found {moduleList.Count} modules ({moduleList.Count(m => !m.fromAppStore)} user modules, {moduleList.Count(m => m.fromAppStore)} Marketplace modules)",
                    modules = moduleList,
                    userModules = moduleList.Where(m => !m.fromAppStore).Select(m => m.name).ToList()
                };

                return JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing modules");
                return JsonSerializer.Serialize(new { error = "Failed to list modules", details = ex.Message });
            }
        }

        public async Task<string> CreateModule(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters["module_name"]?.ToString();
                if (string.IsNullOrEmpty(moduleName))
                {
                    return JsonSerializer.Serialize(new { error = "module_name is required" });
                }

                // Check for duplicate
                var existing = Utils.Utils.GetModuleByName(_model, moduleName);
                if (existing != null)
                {
                    return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' already exists" });
                }

                using (var transaction = _model.StartTransaction("create module"))
                {
                    var module = _model.Create<IModule>();
                    module.Name = moduleName;
                    _model.Root.AddModule(module);
                    transaction.Commit();
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Module '{moduleName}' created successfully",
                    module = new { name = moduleName, fromAppStore = false, entityCount = 0 }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating module");
                MendixAdditionalTools.SetLastError($"Failed to create module: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to create module: {ex.Message}" });
            }
        }

        public async Task<string> SetEntityGeneralization(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var parentEntityName = parameters["parent_entity"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();
                var parentModuleName = parameters["parent_module"]?.ToString();

                if (string.IsNullOrEmpty(entityName) || string.IsNullOrEmpty(parentEntityName))
                {
                    return JsonSerializer.Serialize(new { error = "entity_name and parent_entity are required" });
                }

                // BUG-015 fix: Prevent self-referencing generalization
                if (entityName.Equals(parentEntityName, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(moduleName) && string.IsNullOrEmpty(parentModuleName) ||
                     (!string.IsNullOrEmpty(moduleName) && moduleName.Equals(parentModuleName, StringComparison.OrdinalIgnoreCase))))
                {
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' cannot inherit from itself (self-referencing generalization)" });
                }

                var (entity, entityModule) = Utils.Utils.FindEntityAcrossModules(_model, entityName, moduleName);
                if (entity == null)
                {
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" + (moduleName != null ? $" in module '{moduleName}'" : "") });
                }

                var (parentEntity, _) = Utils.Utils.FindEntityAcrossModules(_model, parentEntityName, parentModuleName);
                if (parentEntity == null)
                {
                    return JsonSerializer.Serialize(new { error = $"Parent entity '{parentEntityName}' not found" + (parentModuleName != null ? $" in module '{parentModuleName}'" : "") });
                }

                // BUG-015 fix: Also check resolved entity identity
                if (ReferenceEquals(entity, parentEntity))
                {
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' cannot inherit from itself (self-referencing generalization)" });
                }

                using (var transaction = _model.StartTransaction("set entity generalization"))
                {
                    var generalization = _model.Create<IGeneralization>();
                    generalization.Generalization = parentEntity.QualifiedName;
                    entity.Generalization = generalization;
                    transaction.Commit();
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Entity '{entityName}' now inherits from '{parentEntityName}'",
                    entity = entityName,
                    parent = parentEntityName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting entity generalization");
                MendixAdditionalTools.SetLastError($"Failed to set generalization: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to set generalization: {ex.Message}" });
            }
        }

        public async Task<string> RemoveEntityGeneralization(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(entityName))
                {
                    return JsonSerializer.Serialize(new { error = "entity_name is required" });
                }

                var (entity, _) = Utils.Utils.FindEntityAcrossModules(_model, entityName, moduleName);
                if (entity == null)
                {
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });
                }

                if (entity.Generalization is not IGeneralization)
                {
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' does not have a generalization to remove" });
                }

                using (var transaction = _model.StartTransaction("remove entity generalization"))
                {
                    var noGeneralization = _model.Create<INoGeneralization>();
                    noGeneralization.Persistable = true;
                    entity.Generalization = noGeneralization;
                    transaction.Commit();
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Generalization removed from entity '{entityName}'. It is now a root entity.",
                    entity = entityName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing entity generalization");
                MendixAdditionalTools.SetLastError($"Failed to remove generalization: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to remove generalization: {ex.Message}" });
            }
        }

        public async Task<string> AddEventHandler(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var eventStr = parameters["event"]?.ToString();
                var momentStr = parameters["moment"]?.ToString();
                var microflowName = parameters["microflow"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(entityName) || string.IsNullOrEmpty(eventStr) || string.IsNullOrEmpty(momentStr) || string.IsNullOrEmpty(microflowName))
                {
                    return JsonSerializer.Serialize(new { error = "entity_name, event, moment, and microflow are required" });
                }

                var (entity, _) = Utils.Utils.FindEntityAcrossModules(_model, entityName, moduleName);
                if (entity == null)
                {
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });
                }

                // BUG-011 fix: Accept both qualified ("Module.MicroflowName") and unqualified names
                var mfSearchName = microflowName;
                string? mfModuleHint = null;
                if (microflowName.Contains('.'))
                {
                    var parts = microflowName.Split('.', 2);
                    mfModuleHint = parts[0];
                    mfSearchName = parts[1];
                }

                // Find the microflow across all non-AppStore modules
                IMicroflow? microflow = null;
                foreach (var mod in Utils.Utils.GetAllNonAppStoreModules(_model))
                {
                    // If module hint provided, prefer matching module first
                    if (mfModuleHint != null && !mod.Name.Equals(mfModuleHint, StringComparison.OrdinalIgnoreCase))
                        continue;

                    microflow = mod.GetDocuments().OfType<IMicroflow>()
                        .FirstOrDefault(mf => mf.Name.Equals(mfSearchName, StringComparison.OrdinalIgnoreCase));
                    if (microflow != null) break;
                }

                // If not found with module hint, try without
                if (microflow == null && mfModuleHint != null)
                {
                    foreach (var mod in Utils.Utils.GetAllNonAppStoreModules(_model))
                    {
                        microflow = mod.GetDocuments().OfType<IMicroflow>()
                            .FirstOrDefault(mf => mf.Name.Equals(mfSearchName, StringComparison.OrdinalIgnoreCase));
                        if (microflow != null) break;
                    }
                }

                if (microflow == null)
                {
                    return JsonSerializer.Serialize(new { error = $"Microflow '{microflowName}' not found in any module" });
                }

                var eventType = MapEventType(eventStr);
                var moment = momentStr.ToLowerInvariant().Trim() == "before" ? ActionMoment.Before : ActionMoment.After;

                bool raiseErrorOnFalse = true;
                if (parameters.ContainsKey("raise_error_on_false"))
                {
                    if (parameters["raise_error_on_false"]?.AsValue().TryGetValue<bool>(out var val) == true)
                        raiseErrorOnFalse = val;
                }

                using (var transaction = _model.StartTransaction("add event handler"))
                {
                    var eventHandler = _model.Create<IEventHandler>();
                    eventHandler.Moment = moment;
                    eventHandler.Event = eventType;
                    eventHandler.Microflow = microflow.QualifiedName;
                    eventHandler.RaiseErrorOnFalse = raiseErrorOnFalse;
                    eventHandler.PassEventObject = true;
                    entity.AddEventHandler(eventHandler);
                    transaction.Commit();
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Event handler added to '{entityName}': {momentStr} {eventStr} → {microflowName}",
                    entity = entityName,
                    moment = momentStr,
                    @event = eventStr,
                    microflow = microflowName,
                    raiseErrorOnFalse = raiseErrorOnFalse
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding event handler");
                MendixAdditionalTools.SetLastError($"Failed to add event handler: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to add event handler: {ex.Message}" });
            }
        }

        private EventType MapEventType(string eventStr)
        {
            return eventStr.ToLowerInvariant().Trim() switch
            {
                "create" => EventType.Create,
                "commit" => EventType.Commit,
                "delete" => EventType.Delete,
                "rollback" => EventType.RollBack,
                _ => EventType.Commit
            };
        }

        public async Task<string> AddAttribute(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var attributeName = parameters["attribute_name"]?.ToString();
                var attributeType = parameters["attribute_type"]?.ToString();
                var defaultValue = parameters["default_value"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(entityName))
                    return JsonSerializer.Serialize(new { error = "entity_name is required" });
                if (string.IsNullOrEmpty(attributeName))
                    return JsonSerializer.Serialize(new { error = "attribute_name is required" });
                if (string.IsNullOrEmpty(attributeType))
                    return JsonSerializer.Serialize(new { error = "attribute_type is required" });

                var (entity, entityModule) = Utils.Utils.FindEntityAcrossModules(_model, entityName, moduleName);
                if (entity == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" + (moduleName != null ? $" in module '{moduleName}'" : "") });

                // Check if attribute already exists
                var existingAttr = entity.GetAttributes().FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                if (existingAttr != null)
                    return JsonSerializer.Serialize(new { error = $"Attribute '{attributeName}' already exists on entity '{entityName}'" });

                using (var transaction = _model.StartTransaction("add attribute"))
                {
                    var mxAttribute = _model.Create<IAttribute>();
                    mxAttribute.Name = attributeName;

                    if (attributeType.Equals("Enumeration", StringComparison.OrdinalIgnoreCase))
                    {
                        // Check if enumeration_name parameter is provided to link to existing enum
                        var explicitEnumName = parameters["enumeration_name"]?.ToString();
                        if (!string.IsNullOrEmpty(explicitEnumName))
                        {
                            // Link to existing enumeration by name
                            var foundEnum = FindExistingEnumeration(explicitEnumName);
                            if (foundEnum == null)
                                return JsonSerializer.Serialize(new { error = $"Enumeration '{explicitEnumName}' not found in any module" });

                            var enumTypeInstance = _model.Create<IEnumerationAttributeType>();
                            enumTypeInstance.Enumeration = foundEnum.QualifiedName;
                            mxAttribute.Type = enumTypeInstance;
                        }
                        else
                        {
                            // Create new enumeration from values array
                            var enumValues = parameters["enumeration_values"]?.AsArray()
                                ?.Select(v => v?.ToString())
                                ?.Where(v => !string.IsNullOrEmpty(v))
                                ?.ToList();

                            if (enumValues != null && enumValues.Any())
                            {
                                var enumTypeInstance = CreateEnumerationType(_model, attributeName, enumValues, entityModule);
                                mxAttribute.Type = enumTypeInstance;
                            }
                            else
                            {
                                return JsonSerializer.Serialize(new { error = "Enumeration type requires 'enumeration_values' array or 'enumeration_name' to reference an existing enumeration" });
                            }
                        }
                    }
                    else if (attributeType.StartsWith("Enumeration:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Link to existing enumeration via "Enumeration:EnumName" syntax
                        var enumName = attributeType.Substring("Enumeration:".Length).Trim();

                        // Allow explicit enumeration_name parameter to override
                        var explicitEnumName = parameters["enumeration_name"]?.ToString();
                        if (!string.IsNullOrEmpty(explicitEnumName))
                            enumName = explicitEnumName;

                        if (string.IsNullOrEmpty(enumName))
                            return JsonSerializer.Serialize(new { error = "Enumeration name must be specified after the colon, e.g. 'Enumeration:OrderStatus'" });

                        var foundEnum = FindExistingEnumeration(enumName);
                        if (foundEnum == null)
                            return JsonSerializer.Serialize(new { error = $"Enumeration '{enumName}' not found in any module" });

                        var enumTypeInstance = _model.Create<IEnumerationAttributeType>();
                        enumTypeInstance.Enumeration = foundEnum.QualifiedName;
                        mxAttribute.Type = enumTypeInstance;
                    }
                    else
                    {
                        mxAttribute.Type = CreateAttributeType(_model, attributeType);
                    }

                    // Set default value if provided
                    if (!string.IsNullOrEmpty(defaultValue))
                    {
                        var storedValue = _model.Create<IStoredValue>();
                        storedValue.DefaultValue = defaultValue;
                        mxAttribute.Value = storedValue;
                    }

                    entity.AddAttribute(mxAttribute);
                    transaction.Commit();
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Attribute '{attributeName}' ({attributeType}) added to entity '{entityName}'",
                    entity = entityName,
                    attribute = new
                    {
                        name = attributeName,
                        type = attributeType,
                        defaultValue = defaultValue
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding attribute");
                MendixAdditionalTools.SetLastError($"Failed to add attribute: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to add attribute: {ex.Message}" });
            }
        }

        public async Task<string> SetCalculatedAttribute(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var attributeName = parameters["attribute_name"]?.ToString();
                var microflowName = parameters["microflow"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(entityName))
                    return JsonSerializer.Serialize(new { error = "entity_name is required" });
                if (string.IsNullOrEmpty(attributeName))
                    return JsonSerializer.Serialize(new { error = "attribute_name is required" });
                if (string.IsNullOrEmpty(microflowName))
                    return JsonSerializer.Serialize(new { error = "microflow is required" });

                var (entity, entityModule) = Utils.Utils.FindEntityAcrossModules(_model, entityName, moduleName);
                if (entity == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" + (moduleName != null ? $" in module '{moduleName}'" : "") });

                var attribute = entity.GetAttributes().FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                if (attribute == null)
                    return JsonSerializer.Serialize(new { error = $"Attribute '{attributeName}' not found on entity '{entityName}'" });

                // Find the microflow across all non-AppStore modules
                IMicroflow? microflow = null;
                foreach (var mod in Utils.Utils.GetAllNonAppStoreModules(_model))
                {
                    microflow = mod.GetDocuments().OfType<IMicroflow>()
                        .FirstOrDefault(mf => mf.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));
                    if (microflow != null) break;
                }
                if (microflow == null)
                    return JsonSerializer.Serialize(new { error = $"Microflow '{microflowName}' not found" });

                using (var transaction = _model.StartTransaction("set calculated attribute"))
                {
                    var calculatedValue = _model.Create<ICalculatedValue>();
                    calculatedValue.Microflow = microflow.QualifiedName;
                    calculatedValue.PassEntity = true;
                    attribute.Value = calculatedValue;
                    transaction.Commit();
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Attribute '{attributeName}' on '{entityName}' is now calculated by microflow '{microflowName}'",
                    entity = entityName,
                    attribute = attributeName,
                    microflow = microflowName,
                    passEntity = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting calculated attribute");
                MendixAdditionalTools.SetLastError($"Failed to set calculated attribute: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to set calculated attribute: {ex.Message}" });
            }
        }

        public async Task<string> CheckModel(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters?["module_name"]?.ToString();
                var errors = new List<object>();
                var warnings = new List<object>();
                var info = new List<object>();

                var modules = string.IsNullOrWhiteSpace(moduleName)
                    ? Utils.Utils.GetAllNonAppStoreModules(_model).ToList()
                    : new List<IModule> { Utils.Utils.GetModuleByName(_model, moduleName) }.Where(m => m != null).ToList()!;

                if (!modules.Any())
                {
                    return JsonSerializer.Serialize(new { error = moduleName != null ? $"Module '{moduleName}' not found" : "No modules found" });
                }

                foreach (var module in modules)
                {
                    var entities = module.DomainModel?.GetEntities()?.ToList() ?? new List<IEntity>();

                    foreach (var entity in entities)
                    {
                        // Check: Entity has no attributes (suspicious)
                        var attrs = entity.GetAttributes();
                        if (attrs == null || attrs.Count == 0)
                        {
                            warnings.Add(new { module = module.Name, entity = entity.Name, type = "no_attributes", message = $"Entity '{entity.Name}' has no attributes defined." });
                        }

                        // Check: Generalization points to a valid entity
                        if (entity.Generalization is IGeneralization gen)
                        {
                            try
                            {
                                var parentEntity = gen.Generalization?.Resolve();
                                if (parentEntity == null)
                                {
                                    errors.Add(new { module = module.Name, entity = entity.Name, type = "broken_generalization", message = $"Entity '{entity.Name}' has a generalization to '{gen.Generalization}' which cannot be resolved." });
                                }
                            }
                            catch
                            {
                                errors.Add(new { module = module.Name, entity = entity.Name, type = "broken_generalization", message = $"Entity '{entity.Name}' has a generalization that cannot be resolved." });
                            }
                        }

                        // Check: Event handlers point to valid microflows
                        var handlers = entity.GetEventHandlers();
                        if (handlers != null)
                        {
                            foreach (var handler in handlers)
                            {
                                if (handler.Microflow == null)
                                {
                                    errors.Add(new { module = module.Name, entity = entity.Name, type = "missing_event_microflow", message = $"Event handler on '{entity.Name}' ({handler.Moment} {handler.Event}) has no microflow assigned." });
                                }
                                else
                                {
                                    try
                                    {
                                        var mf = handler.Microflow.Resolve();
                                        if (mf == null)
                                        {
                                            errors.Add(new { module = module.Name, entity = entity.Name, type = "broken_event_microflow", message = $"Event handler on '{entity.Name}' ({handler.Moment} {handler.Event}) references microflow '{handler.Microflow}' which cannot be resolved." });
                                        }
                                    }
                                    catch
                                    {
                                        errors.Add(new { module = module.Name, entity = entity.Name, type = "broken_event_microflow", message = $"Event handler on '{entity.Name}' ({handler.Moment} {handler.Event}) references a microflow that cannot be resolved." });
                                    }
                                }
                            }
                        }

                        // Check: Calculated attributes point to valid microflows
                        foreach (var attr in attrs)
                        {
                            if (attr.Value is ICalculatedValue calcVal)
                            {
                                if (calcVal.Microflow == null)
                                {
                                    errors.Add(new { module = module.Name, entity = entity.Name, attribute = attr.Name, type = "missing_calc_microflow", message = $"Calculated attribute '{attr.Name}' on '{entity.Name}' has no microflow assigned." });
                                }
                                else
                                {
                                    try
                                    {
                                        var mf = calcVal.Microflow.Resolve();
                                        if (mf == null)
                                        {
                                            errors.Add(new { module = module.Name, entity = entity.Name, attribute = attr.Name, type = "broken_calc_microflow", message = $"Calculated attribute '{attr.Name}' on '{entity.Name}' references microflow '{calcVal.Microflow}' which cannot be resolved." });
                                        }
                                    }
                                    catch
                                    {
                                        errors.Add(new { module = module.Name, entity = entity.Name, attribute = attr.Name, type = "broken_calc_microflow", message = $"Calculated attribute '{attr.Name}' on '{entity.Name}' references a microflow that cannot be resolved." });
                                    }
                                }
                            }
                        }
                    }

                    // Check: Associations are valid
                    var associationCount = 0;
                    var checkedAssociations = new HashSet<string>();
                    foreach (var entity in entities)
                    {
                        try
                        {
                            var assocs = entity.GetAssociations(AssociationDirection.Both, null);
                            foreach (var assocResult in assocs)
                            {
                                var assoc = assocResult.Association;
                                if (checkedAssociations.Contains(assoc.Name)) continue;
                                checkedAssociations.Add(assoc.Name);
                                associationCount++;

                                if (string.IsNullOrEmpty(assoc.Name))
                                {
                                    errors.Add(new { module = module.Name, entity = entity.Name, type = "unnamed_association", message = $"Entity '{entity.Name}' has an association with no name." });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            warnings.Add(new { module = module.Name, entity = entity.Name, type = "association_read_error", message = $"Could not read associations for entity '{entity.Name}': {ex.Message}" });
                        }
                    }

                    // Info: Module statistics
                    var microflows = module.GetDocuments().OfType<IMicroflow>().Count();
                    info.Add(new { module = module.Name, entities = entities.Count, associations = associationCount, microflows = microflows });
                }

                var hasIssues = errors.Any() || warnings.Any();
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    healthy = !errors.Any(),
                    summary = new
                    {
                        modulesChecked = modules.Count,
                        errorCount = errors.Count,
                        warningCount = warnings.Count
                    },
                    errors = errors.Any() ? errors : null,
                    warnings = warnings.Any() ? warnings : null,
                    moduleStats = info,
                    message = !hasIssues ? "Model is healthy. No issues found." : $"Found {errors.Count} error(s) and {warnings.Count} warning(s)."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking model");
                return JsonSerializer.Serialize(new { error = $"Failed to check model: {ex.Message}" });
            }
        }

        #region Phase 5: Constants + Enumerations

        public async Task<string> CreateConstant(JsonObject parameters)
        {
            try
            {
                var name = parameters?["name"]?.ToString();
                var type = parameters?["type"]?.ToString()?.ToLowerInvariant() ?? "string";
                var defaultValue = parameters?["default_value"]?.ToString() ?? "";
                var exposedToClient = bool.Parse(parameters?["exposed_to_client"]?.ToString() ?? "false");
                var moduleName = parameters?["module_name"]?.ToString();

                if (string.IsNullOrEmpty(name))
                    return JsonSerializer.Serialize(new { error = "Constant name is required" });

                var module = Utils.Utils.ResolveModule(_model, moduleName);
                if (module == null)
                    return JsonSerializer.Serialize(new { error = $"Module '{moduleName ?? "default"}' not found" });

                // Check for duplicate
                var existingConstants = _model.Root.GetModuleDocuments<IConstant>(module);
                if (existingConstants.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    return JsonSerializer.Serialize(new { error = $"Constant '{name}' already exists in module '{module.Name}'" });

                using var transaction = _model.StartTransaction($"Create constant {name}");

                var constant = _model.Create<IConstant>();
                constant.Name = name;
                constant.DefaultValue = defaultValue;
                constant.ExposedToClient = exposedToClient;

                // Set data type
                constant.DataType = type switch
                {
                    "string" => DataType.String,
                    "integer" or "int" => DataType.Integer,
                    "boolean" or "bool" => DataType.Boolean,
                    "decimal" => DataType.Decimal,
                    "datetime" => DataType.DateTime,
                    "float" => DataType.Float,
                    _ => DataType.String
                };

                module.AddDocument(constant);
                transaction.Commit();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Constant '{name}' created in module '{module.Name}'",
                    constant = new
                    {
                        name = constant.Name,
                        qualifiedName = constant.QualifiedName?.ToString(),
                        type,
                        defaultValue,
                        exposedToClient,
                        module = module.Name
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating constant");
                return JsonSerializer.Serialize(new { error = $"Failed to create constant: {ex.Message}" });
            }
        }

        public async Task<string> ListConstants(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters?["module_name"]?.ToString();

                var modules = string.IsNullOrEmpty(moduleName)
                    ? Utils.Utils.GetAllNonAppStoreModules(_model).ToList()
                    : new List<IModule> { Utils.Utils.ResolveModule(_model, moduleName) }.Where(m => m != null).ToList();

                var result = new List<object>();
                foreach (var module in modules)
                {
                    var constants = _model.Root.GetModuleDocuments<IConstant>(module);
                    foreach (var c in constants)
                    {
                        result.Add(new
                        {
                            name = c.Name,
                            qualifiedName = c.QualifiedName?.ToString(),
                            module = module.Name,
                            defaultValue = c.DefaultValue,
                            exposedToClient = c.ExposedToClient,
                            dataType = c.DataType?.ToString()
                        });
                    }
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    count = result.Count,
                    constants = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing constants");
                return JsonSerializer.Serialize(new { error = $"Failed to list constants: {ex.Message}" });
            }
        }

        public async Task<string> CreateEnumeration(JsonObject parameters)
        {
            try
            {
                var name = parameters?["name"]?.ToString();
                var moduleName = parameters?["module_name"]?.ToString();
                var valuesArray = parameters?["values"]?.AsArray();

                if (string.IsNullOrEmpty(name))
                    return JsonSerializer.Serialize(new { error = "Enumeration name is required" });

                if (valuesArray == null || valuesArray.Count == 0)
                    return JsonSerializer.Serialize(new { error = "At least one value is required for enumeration" });

                var module = Utils.Utils.ResolveModule(_model, moduleName);
                if (module == null)
                    return JsonSerializer.Serialize(new { error = $"Module '{moduleName ?? "default"}' not found" });

                // Check for duplicate
                var existingEnums = _model.Root.GetModuleDocuments<IEnumeration>(module);
                if (existingEnums.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    return JsonSerializer.Serialize(new { error = $"Enumeration '{name}' already exists in module '{module.Name}'" });

                using var transaction = _model.StartTransaction($"Create enumeration {name}");

                var enumeration = _model.Create<IEnumeration>();
                enumeration.Name = name;

                var createdValues = new List<object>();
                foreach (var valueNode in valuesArray)
                {
                    string? valueName = null;
                    string? caption = null;

                    if (valueNode is JsonObject valueObj)
                    {
                        valueName = valueObj["name"]?.ToString();
                        caption = valueObj["caption"]?.ToString();
                    }
                    else
                    {
                        valueName = valueNode?.ToString();
                    }

                    if (string.IsNullOrEmpty(valueName)) continue;

                    var enumValue = _model.Create<IEnumerationValue>();
                    enumValue.Name = valueName;

                    var captionText = _model.Create<IText>();
                    captionText.AddOrUpdateTranslation("en_US", caption ?? valueName);
                    enumValue.Caption = captionText;

                    enumeration.AddValue(enumValue);
                    createdValues.Add(new { name = valueName, caption = caption ?? valueName });
                }

                module.AddDocument(enumeration);
                transaction.Commit();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Enumeration '{name}' created with {createdValues.Count} values in module '{module.Name}'",
                    enumeration = new
                    {
                        name = enumeration.Name,
                        qualifiedName = enumeration.QualifiedName?.ToString(),
                        module = module.Name,
                        values = createdValues
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating enumeration");
                return JsonSerializer.Serialize(new { error = $"Failed to create enumeration: {ex.Message}" });
            }
        }

        public async Task<string> ListEnumerations(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters?["module_name"]?.ToString();

                var modules = string.IsNullOrEmpty(moduleName)
                    ? Utils.Utils.GetAllNonAppStoreModules(_model).ToList()
                    : new List<IModule> { Utils.Utils.ResolveModule(_model, moduleName) }.Where(m => m != null).ToList();

                var result = new List<object>();
                foreach (var module in modules)
                {
                    var enumerations = _model.Root.GetModuleDocuments<IEnumeration>(module);
                    foreach (var e in enumerations)
                    {
                        var values = e.GetValues().Select(v => new
                        {
                            name = v.Name,
                            caption = v.Caption?.GetTranslations()?.FirstOrDefault()?.Text ?? v.Name
                        }).ToList();

                        result.Add(new
                        {
                            name = e.Name,
                            qualifiedName = e.QualifiedName?.ToString(),
                            module = module.Name,
                            valueCount = values.Count,
                            values
                        });
                    }
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    count = result.Count,
                    enumerations = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing enumerations");
                return JsonSerializer.Serialize(new { error = $"Failed to list enumerations: {ex.Message}" });
            }
        }

        #endregion

        public async Task<string> ReadProjectInfo(JsonObject parameters)
        {
            try
            {
                var allModules = Utils.Utils.GetAllNonAppStoreModules(_model).ToList();
                if (!allModules.Any())
                {
                    return JsonSerializer.Serialize(new { error = "No modules found in the project" });
                }

                var moduleInfos = allModules.Select(mod =>
                {
                    var entities = mod.DomainModel?.GetEntities()?.ToList() ?? new List<IEntity>();
                    var microflows = mod.GetDocuments().OfType<IMicroflow>().Count();
                    var constants = _model.Root.GetModuleDocuments<IConstant>(mod).Count;
                    var enumerations = _model.Root.GetModuleDocuments<IEnumeration>(mod).Count;

                    // Count associations (deduplicated)
                    var assocNames = new HashSet<string>();
                    foreach (var entity in entities)
                    {
                        try
                        {
                            foreach (var ea in entity.GetAssociations(AssociationDirection.Both, null))
                                assocNames.Add(ea.Association.Name);
                        }
                        catch { }
                    }

                    return new
                    {
                        name = mod.Name,
                        fromAppStore = mod.FromAppStore,
                        entityCount = entities.Count,
                        associationCount = assocNames.Count,
                        microflowCount = microflows,
                        constantCount = constants,
                        enumerationCount = enumerations,
                        entities = entities.Select(e => e.Name).ToList()
                    };
                }).ToList();

                var totals = new
                {
                    modules = moduleInfos.Count,
                    entities = moduleInfos.Sum(m => m.entityCount),
                    associations = moduleInfos.Sum(m => m.associationCount),
                    microflows = moduleInfos.Sum(m => m.microflowCount),
                    constants = moduleInfos.Sum(m => m.constantCount),
                    enumerations = moduleInfos.Sum(m => m.enumerationCount)
                };

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    projectDirectory = _model.Root?.GetModules()?.FirstOrDefault()?.Name != null ? "Available" : "Unknown",
                    totals = totals,
                    modules = moduleInfos
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading project info");
                return JsonSerializer.Serialize(new { error = $"Failed to read project info: {ex.Message}" });
            }
        }

        public async Task<string> ReadDomainModel(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters?["module_name"]?.ToString();

                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };

                if (string.IsNullOrWhiteSpace(moduleName))
                {
                    // Return domain models from ALL non-AppStore modules
                    var allModules = Utils.Utils.GetAllNonAppStoreModules(_model).ToList();
                    if (!allModules.Any())
                    {
                        return JsonSerializer.Serialize(new { error = "No modules found" });
                    }

                    var allModuleData = allModules.Select(mod => new
                    {
                        ModuleName = mod.Name,
                        Entities = mod.DomainModel?.GetEntities().Select(entity => new
                        {
                            Name = entity.Name,
                            QualifiedName = $"{mod.Name}.{entity.Name}",
                            Generalization = GetGeneralizationInfo(entity),
                            Attributes = GetEntityAttributes(entity),
                            Associations = GetEntityAssociations(entity, mod),
                            EventHandlers = GetEventHandlerInfo(entity)
                        }).ToList()
                    }).ToList();

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        message = $"Domain models retrieved from {allModuleData.Count} modules",
                        data = allModuleData,
                        status = "success"
                    }, options);
                }

                var module = Utils.Utils.GetModuleByName(_model, moduleName);
                if (module == null)
                {
                    return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });
                }

                var domainModel = module.DomainModel;
                var entities = domainModel.GetEntities().ToList();

                var modelData = new
                {
                    ModuleName = module.Name,
                    Entities = entities.Select(entity => new
                    {
                        Name = entity.Name,
                        QualifiedName = $"{module.Name}.{entity.Name}",
                        Generalization = GetGeneralizationInfo(entity),
                        Attributes = GetEntityAttributes(entity),
                        Associations = GetEntityAssociations(entity, module),
                        EventHandlers = GetEventHandlerInfo(entity)
                    }).ToList()
                };

                var result = new
                {
                    success = true,
                    message = "Model retrieved successfully",
                    data = modelData,
                    status = "success"
                };

                return JsonSerializer.Serialize(result, options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading domain model");
                return JsonSerializer.Serialize(new { error = "Failed to read domain model", details = ex.Message });
            }
        }

        public async Task<string> CreateEntity(JsonObject parameters)
        {
            try
            {
                using (var transaction = _model.StartTransaction("create entity"))
                {
                    var entityName = parameters["entity_name"]?.ToString();
                    var attributesArray = parameters["attributes"]?.AsArray();

                    // Extract persistable parameter (default to true for backward compatibility)
                    bool persistable = true;
                    if (parameters.ContainsKey("persistable"))
                    {
                        if (parameters["persistable"]?.AsValue().TryGetValue<bool>(out var persistableValue) == true)
                        {
                            persistable = persistableValue;
                        }
                    }

                    // Extract entityType parameter (default to "persistent")
                    string entityType = "persistent";
                    if (parameters.ContainsKey("entityType"))
                    {
                        entityType = parameters["entityType"]?.ToString() ?? "persistent";
                    }
                    // Handle backward compatibility: if persistable is false, use non-persistent
                    else if (!persistable)
                    {
                        entityType = "non-persistent";
                    }                    if (string.IsNullOrEmpty(entityName))
                    {
                        return JsonSerializer.Serialize(new { error = "Entity name is required" });
                    }

                    var moduleName = parameters["module_name"]?.ToString();
                    var module = Utils.Utils.ResolveModule(_model, moduleName);
                    if (module?.DomainModel == null)
                    {
                        return JsonSerializer.Serialize(new { error = string.IsNullOrWhiteSpace(moduleName) ? "No domain model found" : $"Module '{moduleName}' not found" });
                    }

                    // Check if entity already exists
                    var existingEntity = module.DomainModel.GetEntities()
                        .FirstOrDefault(e => e.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase));

                    if (existingEntity != null)
                    {
                        return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' already exists" });
                    }

                    IEntity mxEntity;
                    string displayEntityType = entityType;

                    if (entityType != "persistent")
                    {
                        // Use template-based approach for special entity types
                        mxEntity = CreateEntityFromTemplate(module, entityName, attributesArray, entityType);
                        if (mxEntity == null)
                        {
                            return JsonSerializer.Serialize(new 
                            { 
                                error = $"Failed to create {entityType} entity. AIExtension.{GetTemplateName(entityType)} template not found or invalid.",
                                details = $"Make sure the AIExtension module exists with a {GetTemplateName(entityType)} entity properly configured."
                            });
                        }
                    }
                    else
                    {
                        // Create regular persistent entity
                        mxEntity = CreateEntityFromTemplate(module, entityName, attributesArray, "persistent");
                        if (mxEntity == null)
                        {
                            return JsonSerializer.Serialize(new 
                            { 
                                error = "Failed to create persistent entity.",
                                details = "Error occurred while creating the entity."
                            });
                        }
                    }

                    transaction.Commit();

                    return JsonSerializer.Serialize(new 
                    { 
                        success = true, 
                        message = $"Entity '{entityName}' created successfully as {displayEntityType}",
                        entity = new
                        {
                            name = mxEntity.Name,
                            persistable = persistable,
                            entityType = entityType,
                            attributes = mxEntity.GetAttributes().Select(a => new
                            {
                                name = a.Name,
                                type = a.Type?.GetType().Name ?? "Unknown"
                            }).ToArray()
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating entity");
                MendixAdditionalTools.SetLastError($"Failed to create entity: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to create entity: {ex.Message}" });
            }
        }

        public async Task<string> CreateAssociation(JsonObject parameters)
        {
            try
            {
                using (var transaction = _model.StartTransaction("create association"))
                {
                    var name = parameters["name"]?.ToString();
                    var parent = parameters["parent"]?.ToString();
                    var child = parameters["child"]?.ToString();
                    var type = parameters["type"]?.ToString() ?? "one-to-many";

                    // Add debugging to understand what parameters are being passed
                    _logger.LogInformation($"CreateAssociation called with: name='{name}', parent='{parent}', child='{child}', type='{type}'");
                    _logger.LogInformation($"IMPORTANT: In typical business terms, parent='{parent}' should be the 'one' side, child='{child}' should be the 'many' side");
                    _logger.LogInformation($"For example: Customer (parent) has many Orders (child) -> 1 Customer : N Orders");

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(child))
                    {
                        return JsonSerializer.Serialize(new { 
                            error = "Missing required parameters for association creation",
                            message = "To create an association, you must provide: name, parent, and child parameters",
                            required_parameters = new {
                                name = new { type = "string", description = "Name of the association (e.g., 'Customer_Orders')", required = true },
                                parent = new { type = "string", description = "Name of the parent entity (e.g., 'Customer')", required = true },
                                child = new { type = "string", description = "Name of the child entity (e.g., 'Order')", required = true },
                                type = new { type = "string", description = "Type of association ('one-to-many' or 'many-to-many')", required = false, @default = "one-to-many" }
                            },
                            example_usage = new {
                                tool_name = "create_association",
                                parameters = new {
                                    name = "Customer_Orders",
                                    parent = "Customer", 
                                    child = "Order",
                                    type = "one-to-many"
                                }
                            },
                            available_entities = new string[] { "Customer", "Order" },
                            guidance = "Make sure both parent and child entities exist before creating an association. Use the entity names exactly as they appear in the domain model."
                        });
                    }

                    // Cross-module support: resolve entities from specified or any module
                    var parentModuleName = parameters["parent_module"]?.ToString();
                    var childModuleName = parameters["child_module"]?.ToString();
                    var defaultModuleName = parameters["module_name"]?.ToString();
                    parentModuleName ??= defaultModuleName;
                    childModuleName ??= defaultModuleName;

                    var (parentEntity, parentModuleResolved) = Utils.Utils.FindEntityAcrossModules(_model, parent, parentModuleName);
                    var (childEntity, childModuleResolved) = Utils.Utils.FindEntityAcrossModules(_model, child, childModuleName);

                    if (parentEntity == null)
                    {
                        return JsonSerializer.Serialize(new { error = $"Parent entity '{parent}' not found" + (parentModuleName != null ? $" in module '{parentModuleName}'" : " in any module") });
                    }

                    if (childEntity == null)
                    {
                        return JsonSerializer.Serialize(new { error = $"Child entity '{child}' not found" + (childModuleName != null ? $" in module '{childModuleName}'" : " in any module") });
                    }

                    // Create association - FIXED: For "1 Customer has many Orders",
                    // we need to call childEntity.AddAssociation(parentEntity) because in Mendix:
                    // - entity.AddAssociation(otherEntity) means "entity references otherEntity"
                    // - For one-to-many, the "many" side should reference the "one" side
                    // So Order (child/many) should reference Customer (parent/one)
                    var mxAssociation = childEntity.AddAssociation(parentEntity);
                    mxAssociation.Name = name;
                    mxAssociation.Type = MapAssociationType(type);

                    // Configure delete behavior if specified
                    var parentDeleteBehavior = parameters["parent_delete_behavior"]?.ToString();
                    var childDeleteBehavior = parameters["child_delete_behavior"]?.ToString();
                    if (!string.IsNullOrEmpty(parentDeleteBehavior))
                        mxAssociation.ParentDeleteBehavior = MapDeletingBehavior(parentDeleteBehavior);
                    if (!string.IsNullOrEmpty(childDeleteBehavior))
                        mxAssociation.ChildDeleteBehavior = MapDeletingBehavior(childDeleteBehavior);

                    // Configure owner if specified
                    var owner = parameters["owner"]?.ToString();
                    if (!string.IsNullOrEmpty(owner) && owner.ToLowerInvariant().Trim() == "both")
                        mxAssociation.Owner = AssociationOwner.Both;

                    _logger.LogInformation($"FIXED: Created association {mxAssociation.Name} by calling {childEntity.Name}.AddAssociation({parentEntity.Name})");
                    _logger.LogInformation($"This creates: 1 {parentEntity.Name} has many {childEntity.Name} (correct direction)");

                    transaction.Commit();

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        message = $"Association '{name}' created successfully",
                        association = new
                        {
                            name = mxAssociation.Name,
                            parent = parentEntity.Name,
                            child = childEntity.Name,
                            type = mxAssociation.Type.ToString(),
                            parentDeleteBehavior = FormatDeletingBehavior(mxAssociation.ParentDeleteBehavior),
                            childDeleteBehavior = FormatDeletingBehavior(mxAssociation.ChildDeleteBehavior),
                            owner = mxAssociation.Owner.ToString()
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating association");
                MendixAdditionalTools.SetLastError($"Failed to create association: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to create association: {ex.Message}" });
            }
        }

        public async Task<string> CreateMultipleEntities(JsonObject parameters)
        {
            try
            {
                using (var transaction = _model.StartTransaction("create multiple entities"))
                {
                    var entitiesArray = parameters["entities"]?.AsArray();
                    
                    // Extract persistable parameter (default to true for backward compatibility)
                    bool persistable = true;
                    if (parameters.ContainsKey("persistable"))
                    {
                        if (parameters["persistable"]?.AsValue().TryGetValue<bool>(out var persistableValue) == true)
                        {
                            persistable = persistableValue;
                        }
                    }

                    if (entitiesArray == null)
                    {
                        return JsonSerializer.Serialize(new { error = "Entities array is required" });
                    }

                    var globalModuleName = parameters["module_name"]?.ToString();
                    var defaultModule = Utils.Utils.ResolveModule(_model, globalModuleName);
                    if (defaultModule?.DomainModel == null)
                    {
                        return JsonSerializer.Serialize(new { error = string.IsNullOrWhiteSpace(globalModuleName) ? "No domain model found" : $"Module '{globalModuleName}' not found" });
                    }

                    var createdEntities = new List<object>();
                    string entityType = persistable ? "persistent" : "non-persistent";

                    foreach (var entityNode in entitiesArray)
                    {
                        var entityObj = entityNode?.AsObject();
                        if (entityObj == null) continue;

                        // BUG-010 fix: Accept both 'entity_name' and 'name' fields
                        var entityName = entityObj["entity_name"]?.ToString() ?? entityObj["name"]?.ToString();
                        var attributesArray = entityObj["attributes"]?.AsArray();

                        if (string.IsNullOrEmpty(entityName)) continue;

                        // Per-entity module override
                        var entityModuleName = entityObj["module_name"]?.ToString();
                        var module = !string.IsNullOrWhiteSpace(entityModuleName)
                            ? Utils.Utils.GetModuleByName(_model, entityModuleName) ?? defaultModule
                            : defaultModule;

                        // Check if entity already exists
                        var existingEntity = module.DomainModel.GetEntities()
                            .FirstOrDefault(e => e.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase));

                        if (existingEntity != null)
                        {
                            continue; // Skip existing entities
                        }

                        IEntity mxEntity;
                        var entityAttributes = new List<object>();

                        if (!persistable)
                        {
                            // Use template-based approach for non-persistent entities
                            mxEntity = CreateEntityFromTemplate(module, entityName, attributesArray);
                            if (mxEntity == null)
                            {
                                // Skip this entity and continue with others
                                continue;
                            }

                            // Collect attributes for response
                            foreach (var attr in mxEntity.GetAttributes())
                            {
                                entityAttributes.Add(new { name = attr.Name, type = attr.Type?.GetType().Name ?? "Unknown" });
                            }
                        }
                        else
                        {
                            // Create regular persistent entity
                            mxEntity = _model.Create<IEntity>();
                            mxEntity.Name = entityName;
                            module.DomainModel.AddEntity(mxEntity);

                            // Add attributes if provided
                            if (attributesArray != null)
                            {
                                foreach (var attrNode in attributesArray)
                                {
                                    var attrObj = attrNode?.AsObject();
                                    if (attrObj == null) continue;

                                    var attrName = attrObj["name"]?.ToString();
                                    var attrType = attrObj["type"]?.ToString();

                                    if (string.IsNullOrEmpty(attrName) || string.IsNullOrEmpty(attrType)) continue;

                                    var mxAttribute = _model.Create<IAttribute>();
                                    mxAttribute.Name = attrName;

                                    if (attrType.Equals("Enumeration", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var enumValues = attrObj["enumerationValues"]?.AsArray()
                                            ?.Select(v => v?.ToString())
                                            ?.Where(v => !string.IsNullOrEmpty(v))
                                            ?.ToList();

                                        if (enumValues != null && enumValues.Any())
                                        {
                                            var enumTypeInstance = CreateEnumerationType(_model, attrName, enumValues, module);
                                            mxAttribute.Type = enumTypeInstance;
                                        }
                                        else
                                        {
                                            continue; // Skip invalid enumerations
                                        }
                                    }
                                    else
                                    {
                                        var attributeType = CreateAttributeType(_model, attrType);
                                        mxAttribute.Type = attributeType;
                                    }

                                    // Set default value if provided
                                    var defaultVal = attrObj["default_value"]?.ToString();
                                    if (!string.IsNullOrEmpty(defaultVal))
                                    {
                                        var storedValue = _model.Create<IStoredValue>();
                                        storedValue.DefaultValue = defaultVal;
                                        mxAttribute.Value = storedValue;
                                    }

                                    mxEntity.AddAttribute(mxAttribute);
                                    entityAttributes.Add(new { name = attrName, type = attrType, defaultValue = defaultVal });
                                }
                            }

                            // Position entity
                            PositionEntity(mxEntity, module.DomainModel.GetEntities().Count());
                        }

                        createdEntities.Add(new
                        {
                            name = entityName,
                            persistable = persistable,
                            entityType = entityType,
                            attributes = entityAttributes
                        });
                    }

                    transaction.Commit();

                    // Auto-arrange the domain model after bulk creation
                    object? layoutResult = null;
                    try
                    {
                        using (var layoutTx = _model.StartTransaction("arrange domain model after bulk creation"))
                        {
                            layoutResult = ArrangeDomainModelInternal(defaultModule);
                            layoutTx.Commit();
                        }
                    }
                    catch (Exception layoutEx)
                    {
                        _logger.LogWarning(layoutEx, "Auto-arrange after bulk creation failed (non-fatal)");
                    }

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        message = $"Successfully created {createdEntities.Count} {entityType} entities",
                        entities = createdEntities,
                        persistable = persistable,
                        entityType = entityType,
                        auto_arranged = layoutResult != null
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating multiple entities");
                return JsonSerializer.Serialize(new { error = $"Failed to create entities: {ex.Message}" });
            }
        }

        public async Task<string> CreateMultipleAssociations(JsonObject parameters)
        {
            try
            {
                using (var transaction = _model.StartTransaction("create multiple associations"))
                {
                    var associationsArray = parameters["associations"]?.AsArray();

                    if (associationsArray == null)
                    {
                        return JsonSerializer.Serialize(new { 
                            error = "Missing required 'associations' array parameter",
                            message = "To create multiple associations, you must provide an 'associations' array containing association objects",
                            required_parameters = new {
                                associations = new {
                                    type = "array",
                                    description = "Array of association objects to create",
                                    required = true,
                                    item_schema = new {
                                        name = new { type = "string", description = "Name of the association", required = true },
                                        parent = new { type = "string", description = "Name of the parent entity", required = true },
                                        child = new { type = "string", description = "Name of the child entity", required = true },
                                        type = new { type = "string", description = "Type of association", required = false, @default = "one-to-many" }
                                    }
                                }
                            },
                            example_usage = new {
                                tool_name = "create_multiple_associations",
                                parameters = new {
                                    associations = new[] {
                                        new {
                                            name = "Customer_Orders",
                                            parent = "Customer",
                                            child = "Order", 
                                            type = "one-to-many"
                                        }
                                    }
                                }
                            },
                            available_entities = new string[] { "Customer", "Order" },
                            guidance = "Each association object must have name, parent, and child properties. Ensure all referenced entities exist before creating associations."
                        });
                    }

                    var createdAssociations = new List<object>();

                    foreach (var assocNode in associationsArray)
                    {
                        var assocObj = assocNode?.AsObject();
                        if (assocObj == null) continue;

                        var name = assocObj["name"]?.ToString();
                        var parent = assocObj["parent"]?.ToString();
                        var child = assocObj["child"]?.ToString();
                        var type = assocObj["type"]?.ToString() ?? "one-to-many";

                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(child))
                        {
                            continue; // Skip invalid associations
                        }

                        // Cross-module entity resolution per association
                        var parentModuleName = assocObj["parent_module"]?.ToString();
                        var childModuleName = assocObj["child_module"]?.ToString();

                        var (parentEntity, _) = Utils.Utils.FindEntityAcrossModules(_model, parent, parentModuleName);
                        var (childEntity, _) = Utils.Utils.FindEntityAcrossModules(_model, child, childModuleName);

                        if (parentEntity == null || childEntity == null)
                        {
                            continue; // Skip if entities don't exist
                        }

                        // Create association - FIXED: Use child.AddAssociation(parent) for correct direction
                        var mxAssociation = childEntity.AddAssociation(parentEntity);
                        mxAssociation.Name = name;
                        mxAssociation.Type = MapAssociationType(type);

                        // Configure delete behavior if specified
                        var parentDeleteBehavior = assocObj["parent_delete_behavior"]?.ToString();
                        var childDeleteBehavior = assocObj["child_delete_behavior"]?.ToString();
                        if (!string.IsNullOrEmpty(parentDeleteBehavior))
                            mxAssociation.ParentDeleteBehavior = MapDeletingBehavior(parentDeleteBehavior);
                        if (!string.IsNullOrEmpty(childDeleteBehavior))
                            mxAssociation.ChildDeleteBehavior = MapDeletingBehavior(childDeleteBehavior);

                        // Configure owner if specified
                        var assocOwner = assocObj["owner"]?.ToString();
                        if (!string.IsNullOrEmpty(assocOwner) && assocOwner.ToLowerInvariant().Trim() == "both")
                            mxAssociation.Owner = AssociationOwner.Both;

                        createdAssociations.Add(new
                        {
                            name = mxAssociation.Name,
                            parent = parentEntity.Name,
                            child = childEntity.Name,
                            type = mxAssociation.Type.ToString(),
                            parentDeleteBehavior = FormatDeletingBehavior(mxAssociation.ParentDeleteBehavior),
                            childDeleteBehavior = FormatDeletingBehavior(mxAssociation.ChildDeleteBehavior),
                            owner = mxAssociation.Owner.ToString()
                        });
                    }

                    transaction.Commit();

                    return JsonSerializer.Serialize(new 
                    { 
                        success = true, 
                        message = $"Successfully created {createdAssociations.Count} associations",
                        associations = createdAssociations
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating multiple associations");
                return JsonSerializer.Serialize(new { error = $"Failed to create associations: {ex.Message}" });
            }
        }

        public async Task<string> CreateDomainModelFromSchema(JsonObject parameters)
        {
            try
            {
                using (var transaction = _model.StartTransaction("create domain model from schema"))
                {
                    var schema = parameters["schema"]?.AsObject();

                    if (schema == null)
                    {
                        return JsonSerializer.Serialize(new { error = "Schema object is required" });
                    }

                    // Extract persistable parameter (default to true for backward compatibility)
                    bool persistable = true;
                    if (parameters.ContainsKey("persistable"))
                    {
                        if (parameters["persistable"]?.AsValue().TryGetValue<bool>(out var persistableValue) == true)
                        {
                            persistable = persistableValue;
                        }
                    }

                    var moduleName = parameters["module_name"]?.ToString();
                    var module = Utils.Utils.ResolveModule(_model, moduleName);
                    if (module?.DomainModel == null)
                    {
                        return JsonSerializer.Serialize(new { error = string.IsNullOrWhiteSpace(moduleName) ? "No domain model found" : $"Module '{moduleName}' not found" });
                    }

                    var entitiesArray = schema["entities"]?.AsArray();
                    var associationsArray = schema["associations"]?.AsArray();

                    var createdEntities = new List<object>();
                    var createdAssociations = new List<object>();
                    string entityType = persistable ? "persistent" : "non-persistent";

                    // Create entities first
                    if (entitiesArray != null)
                    {
                        foreach (var entityNode in entitiesArray)
                        {
                            var entityObj = entityNode?.AsObject();
                            if (entityObj == null) continue;

                            // BUG-012 fix: Accept both 'entity_name' and 'name' fields
                            var entityName = entityObj["entity_name"]?.ToString() ?? entityObj["name"]?.ToString();
                            var attributesArray = entityObj["attributes"]?.AsArray();

                            if (string.IsNullOrEmpty(entityName)) continue;

                            // Extract entityType for this specific entity
                            string currentEntityType = "persistent";
                            if (entityObj.ContainsKey("entityType"))
                            {
                                currentEntityType = entityObj["entityType"]?.ToString() ?? "persistent";
                            }
                            // Handle backward compatibility: if global persistable is false, use non-persistent
                            else if (!persistable)
                            {
                                currentEntityType = "non-persistent";
                            }

                            // Check if entity already exists
                            var existingEntity = module.DomainModel.GetEntities()
                                .FirstOrDefault(e => e.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase));

                            if (existingEntity != null)
                            {
                                continue; // Skip existing entities
                            }

                            IEntity mxEntity;
                            var entityAttributes = new List<object>();

                            // Use template-based approach for all entity types
                            mxEntity = CreateEntityFromTemplate(module, entityName, attributesArray, currentEntityType);
                            if (mxEntity == null)
                            {
                                // Skip this entity and continue with others
                                continue;
                            }

                            // Collect attributes for response
                            foreach (var attr in mxEntity.GetAttributes())
                            {
                                entityAttributes.Add(new { name = attr.Name, type = attr.Type?.GetType().Name ?? "Unknown" });
                            }

                            createdEntities.Add(new 
                            { 
                                name = entityName, 
                                persistable = currentEntityType == "persistent",
                                entityType = currentEntityType,
                                attributes = entityAttributes 
                            });
                        }
                    }

                    // Create associations after entities
                    if (associationsArray != null)
                    {
                        foreach (var assocNode in associationsArray)
                        {
                            var assocObj = assocNode?.AsObject();
                            if (assocObj == null) continue;

                            var name = assocObj["name"]?.ToString();
                            var parent = assocObj["parent"]?.ToString();
                            var child = assocObj["child"]?.ToString();
                            var type = assocObj["type"]?.ToString() ?? "one-to-many";

                            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(child))
                            {
                                continue; // Skip invalid associations
                            }

                            // Cross-module entity resolution for schema associations
                            var parentModuleName = assocObj["parent_module"]?.ToString();
                            var childModuleName = assocObj["child_module"]?.ToString();

                            var (parentEntity, _) = Utils.Utils.FindEntityAcrossModules(_model, parent, parentModuleName);
                            var (childEntity, _) = Utils.Utils.FindEntityAcrossModules(_model, child, childModuleName);

                            if (parentEntity == null || childEntity == null)
                            {
                                continue; // Skip if entities don't exist
                            }

                            // Create association - Use child.AddAssociation(parent) for correct direction
                            var mxAssociation = childEntity.AddAssociation(parentEntity);
                            mxAssociation.Name = name;
                            mxAssociation.Type = MapAssociationType(type);

                            createdAssociations.Add(new
                            {
                                name = mxAssociation.Name,
                                parent = parentEntity.Name,
                                child = childEntity.Name,
                                type = mxAssociation.Type.ToString()
                            });
                        }
                    }

                    transaction.Commit();

                    // Auto-arrange the domain model after schema creation (entities + associations now exist)
                    object? layoutResult = null;
                    try
                    {
                        using (var layoutTx = _model.StartTransaction("arrange domain model after schema creation"))
                        {
                            layoutResult = ArrangeDomainModelInternal(module);
                            layoutTx.Commit();
                        }
                    }
                    catch (Exception layoutEx)
                    {
                        _logger.LogWarning(layoutEx, "Auto-arrange after schema creation failed (non-fatal)");
                    }

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        message = $"Successfully created domain model with {createdEntities.Count} entities and {createdAssociations.Count} associations",
                        entities = createdEntities,
                        associations = createdAssociations,
                        persistable = persistable,
                        auto_arranged = layoutResult != null
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating domain model from schema");
                return JsonSerializer.Serialize(new { error = $"Failed to create domain model: {ex.Message}" });
            }
        }

        public async Task<string> DeleteModelElement(JsonObject parameters)
        {
            try
            {
                var elementType = parameters["element_type"]?.ToString();
                var elementName = parameters["element_name"]?.ToString();
                var entityName = parameters["entity_name"]?.ToString() ?? elementName;
                var attributeName = parameters["attribute_name"]?.ToString();
                var associationName = parameters["association_name"]?.ToString();
                var documentName = parameters["document_name"]?.ToString() ?? elementName ?? entityName;

                if (string.IsNullOrEmpty(elementType))
                {
                    return JsonSerializer.Serialize(new { error = "Element type is required" });
                }

                var moduleName = parameters["module_name"]?.ToString();
                var module = Utils.Utils.ResolveModule(_model, moduleName);
                if (module == null)
                {
                    return JsonSerializer.Serialize(new { error = string.IsNullOrWhiteSpace(moduleName) ? "No module found" : $"Module '{moduleName}' not found" });
                }

                switch (elementType.ToLower())
                {
                    case "entity":
                        if (string.IsNullOrEmpty(entityName))
                            return JsonSerializer.Serialize(new { error = "entity_name is required for entity deletion" });
                        if (module.DomainModel == null)
                            return JsonSerializer.Serialize(new { error = "No domain model found" });
                        return DeleteEntity(module.DomainModel, entityName);

                    case "attribute":
                        if (string.IsNullOrEmpty(entityName) || string.IsNullOrEmpty(attributeName))
                            return JsonSerializer.Serialize(new { error = "entity_name and attribute_name are required for attribute deletion" });
                        if (module.DomainModel == null)
                            return JsonSerializer.Serialize(new { error = "No domain model found" });
                        return DeleteAttribute(module.DomainModel, entityName, attributeName);

                    case "association":
                        if (string.IsNullOrEmpty(entityName) || string.IsNullOrEmpty(associationName))
                            return JsonSerializer.Serialize(new { error = "entity_name and association_name are required for association deletion" });
                        if (module.DomainModel == null)
                            return JsonSerializer.Serialize(new { error = "No domain model found" });
                        return DeleteAssociation(module.DomainModel, entityName, associationName);

                    case "microflow":
                        if (string.IsNullOrEmpty(documentName))
                            return JsonSerializer.Serialize(new { error = "document_name (or entity_name) is required for microflow deletion" });
                        return DeleteDocument<IMicroflow>(module, documentName, "Microflow");

                    case "constant":
                        if (string.IsNullOrEmpty(documentName))
                            return JsonSerializer.Serialize(new { error = "document_name (or entity_name) is required for constant deletion" });
                        return DeleteConstant(module, documentName);

                    case "enumeration":
                        if (string.IsNullOrEmpty(documentName))
                            return JsonSerializer.Serialize(new { error = "document_name (or entity_name) is required for enumeration deletion" });
                        return DeleteDocument<IEnumeration>(module, documentName, "Enumeration");

                    default:
                        return JsonSerializer.Serialize(new { error = $"Unknown deletion type: {elementType}. Supported: entity, attribute, association, microflow, constant, enumeration" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting model element");
                MendixAdditionalTools.SetLastError($"Failed to delete element: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to delete element: {ex.Message}" });
            }
        }

        public async Task<string> DiagnoseAssociations(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters?["module_name"]?.ToString();
                var module = Utils.Utils.ResolveModule(_model, moduleName);
                if (module == null)
                {
                    return JsonSerializer.Serialize(new { error = string.IsNullOrWhiteSpace(moduleName) ? "Module not found" : $"Module '{moduleName}' not found" });
                }

                var domainModel = module.DomainModel;
                var entities = domainModel.GetEntities().ToList();
                var allAssociations = new List<object>();

                // Collect associations with detailed information
                foreach (var entity in entities)
                {
                    var associations = entity.GetAssociations(AssociationDirection.Both, null).ToList();
                    foreach (var association in associations)
                    {
                        allAssociations.Add(new
                        {
                            Name = association.Association.Name,
                            Parent = association.Parent.Name,
                            Child = association.Child.Name,
                            Type = association.Association.Type.ToString(),
                            MappedType = association.Association.Type == AssociationType.Reference ? "one-to-many" : "many-to-many"
                        });
                    }
                }

                var result = new
                {
                    entities = entities.Select(e => e.Name).ToList(),
                    entityCount = entities.Count,
                    associations = allAssociations,
                    associationCount = allAssociations.Count,
                    status = "Domain model diagnosed successfully",
                    guidance = new
                    {
                        commonIssues = new[]
                        {
                            "Entities must exist before creating associations",
                            "Entity names are case sensitive",
                            "Don't use module prefixes in entity names",
                            "Association names must be unique",
                            "For one-to-many associations, parent is the 'one' side, child is the 'many' side"
                        },
                        properFormat = new
                        {
                            Name = "Customer_Orders",
                            Parent = "Customer",
                            Child = "Order",
                            Type = "one-to-many"
                        }
                    }
                };

                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error diagnosing associations");
                return JsonSerializer.Serialize(new { error = "Failed to diagnose associations", details = ex.Message });
            }
        }

        public async Task<string> GetLastError(JsonObject parameters)
        {
            return JsonSerializer.Serialize(new { error = "GetLastError not implemented yet" });
        }

        public async Task<string> ListAvailableTools(JsonObject parameters)
        {
            var tools = new[]
            {
                "list_modules",
                "create_module",
                "read_domain_model",
                "read_project_info",
                "create_entity",
                "create_multiple_entities",
                "create_association",
                "create_multiple_associations",
                "create_domain_model_from_schema",
                "delete_model_element",
                "diagnose_associations",
                "set_entity_generalization",
                "remove_entity_generalization",
                "add_event_handler",
                "add_attribute",
                "set_calculated_attribute",
                "create_constant",
                "list_constants",
                "create_enumeration",
                "list_enumerations",
                "save_data",
                "generate_overview_pages",
                "list_microflows",
                "read_microflow_details",
                "create_microflow",
                "create_microflow_activities",
                "check_model",
                "check_project_errors",
                "get_studio_pro_logs",
                "get_last_error",
                "list_available_tools",
                "debug_info",
                "configure_system_attributes",
                "manage_folders",
                "validate_name",
                "copy_model_element",
                "list_java_actions",
                "read_runtime_settings",
                "set_runtime_settings",
                "read_configurations",
                "set_configuration",
                "read_version_control",
                "set_microflow_url",
                "list_rules",
                "exclude_document",
                "read_security_info",
                "read_entity_access_rules",
                "read_microflow_security",
                "audit_security",
                "read_nanoflow_details",
                "list_nanoflows",
                "list_scheduled_events",
                "list_rest_services",
                "query_model_elements",
                "rename_entity",
                "rename_attribute",
                "rename_association",
                "rename_document",
                "rename_module",
                "rename_enumeration_value",
                "update_attribute",
                "update_association",
                "update_constant",
                "update_enumeration",
                "set_documentation",
                "query_associations",
                "manage_navigation",
                "check_variable_name",
                "modify_microflow_activity",
                "insert_before_activity",
                "list_pages",
                "read_page_details",
                "list_workflows",
                "read_workflow_details",
                "delete_document",
                "sync_filesystem",
                "update_microflow",
                "read_attribute_details",
                "configure_constant_values",
                "generate_sample_data",
                "read_sample_data",
                "setup_data_import",
                "arrange_domain_model"
            };

            return JsonSerializer.Serialize(new { tools = tools, status = "success" });
        }

        /// <summary>
        /// Get dynamically available entity types based on template availability
        /// </summary>
        /// <returns>List of supported entity types in current project</returns>
        public List<string> GetAvailableEntityTypes()
        {
            var availableTypes = new List<string>
            {
                "persistent" // Always available
            };

            try
            {
                // Check for each template and add to available types if found
                if (FindNonPersistentTemplate() != null)
                {
                    availableTypes.Add("non-persistent");
                }

                if (FindFileDocumentTemplate() != null)
                {
                    availableTypes.Add("filedocument");
                }

                if (FindImageTemplate() != null)
                {
                    availableTypes.Add("image");
                }

                if (FindStoreCreatedDateTemplate() != null)
                {
                    availableTypes.Add("storecreateddate");
                }

                if (FindStoreChangeDateTemplate() != null)
                {
                    availableTypes.Add("storechangedate");
                }

                if (FindStoreCreatedChangeDateTemplate() != null)
                {
                    availableTypes.Add("storecreatedchangedate");
                }

                if (FindStoreOwnerTemplate() != null)
                {
                    availableTypes.Add("storeowner");
                }

                if (FindStoreChangeByTemplate() != null)
                {
                    availableTypes.Add("storechangeby");
                }

                _logger.LogInformation($"Available entity types: {string.Join(", ", availableTypes)}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking template availability, falling back to basic types");
                // If there's an error checking templates, provide basic types
                if (!availableTypes.Contains("non-persistent"))
                {
                    availableTypes.Add("non-persistent"); // Usually available
                }
            }

            return availableTypes;
        }

        /// <summary>
        /// Get detailed information about available entity types including descriptions
        /// </summary>
        /// <returns>Dictionary with entity type details</returns>
        public Dictionary<string, object> GetEntityTypeInfo()
        {
            var availableTypes = GetAvailableEntityTypes();
            var allDescriptions = new Dictionary<string, string>
            {
                { "persistent", "Standard entity stored in database (always available)" },
                { "non-persistent", "Session entity not stored in database (uses NPE template)" },
                { "filedocument", "Entity inheriting from System.FileDocument for file storage" },
                { "image", "Entity inheriting from System.Image for image storage" },
                { "storecreateddate", "Entity with automatic creation date tracking" },
                { "storechangedate", "Entity with automatic modification date tracking" },
                { "storecreatedchangedate", "Entity with both creation and modification date tracking" },
                { "storeowner", "Entity with automatic owner (creator) tracking" },
                { "storechangeby", "Entity with automatic last modifier tracking" }
            };

            var result = new Dictionary<string, object>
            {
                { "availableTypes", availableTypes },
                { "descriptions", availableTypes.ToDictionary(type => type, type => allDescriptions[type]) },
                { "unavailableTypes", allDescriptions.Keys.Except(availableTypes).ToList() },
                { "templateInstructions", "Unavailable types require corresponding templates in AIExtension module" }
            };

            return result;
        }

        #region Helper Methods

        private List<object> GetEntityAttributes(IEntity entity)
        {
            return entity.GetAttributes()
                .Where(attr => attr != null)
                .Select(attr =>
                {
                    var typeName = attr.Type?.GetType().Name ?? "Unknown";
                    typeName = typeName.Replace("AttributeTypeProxy", "");

                    // Handle Enumerations specially
                    if (attr.Type is IEnumerationAttributeType enumType)
                    {
                        var enumeration = enumType.Enumeration.Resolve();
                        var enumValues = enumeration.GetValues()
                            .Select(v => v.Name)
                            .ToList();
                        typeName = $"Enumeration ({string.Join("/", enumValues)})";
                    }

                    // Determine value type and default
                    string? valueType = null;
                    string? defaultValue = null;
                    string? calculatedMicroflow = null;

                    if (attr.Value is IStoredValue storedValue)
                    {
                        valueType = "stored";
                        defaultValue = string.IsNullOrEmpty(storedValue.DefaultValue) ? null : storedValue.DefaultValue;
                    }
                    else if (attr.Value is ICalculatedValue calcValue)
                    {
                        valueType = "calculated";
                        calculatedMicroflow = calcValue.Microflow?.ToString();
                    }

                    return (object)new
                    {
                        name = attr.Name,
                        type = typeName,
                        valueType = valueType,
                        defaultValue = defaultValue,
                        calculatedMicroflow = calculatedMicroflow
                    };
                })
                .ToList();
        }

        private object? GetGeneralizationInfo(IEntity entity)
        {
            if (entity.Generalization is IGeneralization gen)
            {
                return new
                {
                    hasGeneralization = true,
                    parent = gen.Generalization?.ToString()
                };
            }
            return null;
        }

        private List<object>? GetEventHandlerInfo(IEntity entity)
        {
            var handlers = entity.GetEventHandlers();
            if (handlers == null || handlers.Count == 0)
                return null;

            return handlers.Select(h => (object)new
            {
                moment = h.Moment.ToString().ToLowerInvariant(),
                @event = h.Event.ToString().ToLowerInvariant(),
                microflow = h.Microflow?.ToString(),
                raiseErrorOnFalse = h.RaiseErrorOnFalse,
                passEventObject = h.PassEventObject
            }).ToList();
        }

        private List<Association> GetEntityAssociations(IEntity entity, IModule module)
        {
            var entityAssociations = new List<Association>();
            var associations = entity.GetAssociations(AssociationDirection.Both, null);

            foreach (var association in associations)
            {
                var associationType = association.Association.Type.ToString();
                var mappedType = associationType switch
                {
                    "Reference" => "one-to-many",
                    "ReferenceSet" => "many-to-many",
                    _ => "one-to-many"
                };

                // FIXED: For Reference associations, we need to swap parent/child to match business semantics
                // In Mendix: association.Parent is the entity that owns the reference (the "many" side)
                //           association.Child is the entity being referenced (the "one" side)
                // In business terms: we want "one" side as parent, "many" side as child
                string parentName, childName;
                
                if (associationType == "Reference")
                {
                    // Swap: Mendix parent becomes our child, Mendix child becomes our parent
                    parentName = association.Child.Name;  // The "one" side (being referenced)
                    childName = association.Parent.Name;  // The "many" side (owns the reference)
                }
                else
                {
                    // For many-to-many, keep original direction
                    parentName = association.Parent.Name;
                    childName = association.Child.Name;
                }

                var associationModel = new Association
                {
                    Name = association.Association.Name,
                    Parent = parentName,
                    Child = childName,
                    Type = mappedType,
                    ParentDeleteBehavior = FormatDeletingBehavior(association.Association.ParentDeleteBehavior),
                    ChildDeleteBehavior = FormatDeletingBehavior(association.Association.ChildDeleteBehavior),
                    Owner = association.Association.Owner.ToString()
                };

                entityAssociations.Add(associationModel);
            }

            return entityAssociations;
        }

        private IAttributeType CreateAttributeType(IModel model, string attributeType)
        {
            switch (attributeType.ToLowerInvariant())
            {
                case "decimal":
                    return model.Create<IDecimalAttributeType>();
                case "integer":
                    return model.Create<IIntegerAttributeType>();
                case "long":
                    return model.Create<ILongAttributeType>();
                case "string":
                    return model.Create<IStringAttributeType>();
                case "boolean":
                    return model.Create<IBooleanAttributeType>();
                case "datetime":
                    return model.Create<IDateTimeAttributeType>();
                case "autonumber":
                    return model.Create<IAutoNumberAttributeType>();
                case "binary":
                    return model.Create<IBinaryAttributeType>();
                case "hashedstring":
                    return model.Create<IHashedStringAttributeType>();
                default:
                    return model.Create<IStringAttributeType>();
            }
        }

        private IEnumerationAttributeType CreateEnumerationType(IModel model, string attributeName, List<string> enumValues, IModule module)
        {
            var attributeEnum = model.Create<IEnumerationAttributeType>();
            var enumDoc = model.Create<IEnumeration>();
            enumDoc.Name = GetUniqueName(attributeName + "Enum");

            foreach (var value in enumValues)
            {
                var enumValue = model.Create<IEnumerationValue>();
                enumValue.Name = value;
                
                var captionText = model.Create<IText>();
                captionText.AddOrUpdateTranslation("en_US", value);
                enumValue.Caption = captionText;
                
                enumDoc.AddValue(enumValue);
            }

            module.AddDocument(enumDoc);
            attributeEnum.Enumeration = enumDoc.QualifiedName;
            return attributeEnum;
        }

        private IEnumeration? FindExistingEnumeration(string enumName)
        {
            foreach (var mod in Utils.Utils.GetAllNonAppStoreModules(_model))
            {
                var candidate = _model.Root.GetModuleDocuments<IEnumeration>(mod)
                    .FirstOrDefault(e =>
                        e.Name.Equals(enumName, StringComparison.OrdinalIgnoreCase) ||
                        (e.QualifiedName?.ToString() ?? "").Equals(enumName, StringComparison.OrdinalIgnoreCase));
                if (candidate != null)
                    return candidate;
            }
            return null;
        }

        private AssociationType MapAssociationType(string type)
        {
            if (string.IsNullOrEmpty(type))
            {
                return AssociationType.Reference;
            }

            var normalizedType = type.ToLowerInvariant().Trim();

            switch (normalizedType)
            {
                case "one-to-many":
                case "reference":
                    return AssociationType.Reference;
                case "many-to-many":
                case "referenceset":  // FIXED: ReferenceSet should create many-to-many
                    return AssociationType.ReferenceSet;
                default:
                    return AssociationType.Reference;
            }
        }

        private DeletingBehavior MapDeletingBehavior(string behavior)
        {
            if (string.IsNullOrEmpty(behavior))
                return DeletingBehavior.DeleteMeButKeepReferences;

            // BUG-001 fix: Support all common aliases for delete behaviors
            return behavior.ToLowerInvariant().Trim() switch
            {
                "delete_me_and_references" or "cascade" or "delete_me_too" or "delete_referencing" => DeletingBehavior.DeleteMeAndReferences,
                "delete_me_if_no_references" or "prevent" or "keep_if_referenced" => DeletingBehavior.DeleteMeIfNoReferences,
                "delete_me_but_keep_references" or "default" or "keep_references" => DeletingBehavior.DeleteMeButKeepReferences,
                _ => DeletingBehavior.DeleteMeButKeepReferences
            };
        }

        private string FormatDeletingBehavior(DeletingBehavior behavior)
        {
            return behavior switch
            {
                DeletingBehavior.DeleteMeAndReferences => "delete_me_and_references",
                DeletingBehavior.DeleteMeIfNoReferences => "delete_me_if_no_references",
                DeletingBehavior.DeleteMeButKeepReferences => "delete_me_but_keep_references",
                _ => "delete_me_but_keep_references"
            };
        }

        private void PositionEntity(IEntity entity, int entityCount)
        {
            const int EntityWidth = 150;
            const int EntityHeight = 75;
            const int SpacingX = 200;
            const int SpacingY = 150;
            const int StartX = 20;
            const int StartY = 20;
            const int MaxColumns = 5;

            int column = entityCount % MaxColumns;
            int row = entityCount / MaxColumns;

            int x = StartX + (column * SpacingX);
            int y = StartY + (row * SpacingY);

            entity.Location = new Location(x, y);
        }

        #region Smart Domain Model Layout

        public async Task<string> ArrangeDomainModel(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters["module_name"]?.ToString();
                var rootEntity = parameters["root_entity"]?.ToString();
                if (string.IsNullOrEmpty(moduleName))
                    return JsonSerializer.Serialize(new { success = false, error = "module_name is required" });

                var module = Utils.Utils.ResolveModule(_model, moduleName);
                if (module == null)
                    return JsonSerializer.Serialize(new { success = false, error = $"Module '{moduleName}' not found" });

                using (var transaction = _model.StartTransaction("arrange domain model"))
                {
                    var result = ArrangeDomainModelInternal(module, rootEntity);
                    transaction.Commit();
                    return JsonSerializer.Serialize(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error arranging domain model");
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Core layout algorithm — Sugiyama-style layered graph layout with crossing minimization.
        /// Handles 20+ entity models efficiently. Can be called internally after bulk entity creation.
        /// </summary>
        internal object ArrangeDomainModelInternal(IModule module, string? rootEntity = null)
        {
            var entities = module.DomainModel.GetEntities().ToList();
            if (entities.Count == 0)
                return new { success = true, message = "No entities to arrange", entities_arranged = 0 };

            // Layout constants (tuned for large models)
            const int ENTITY_WIDTH = 200;
            const int H_GAP = 50;             // minimum horizontal gap between entities in same layer
            const int V_SPACING = 120;        // vertical spacing between layers
            const int START_X = 50;
            const int START_Y = 50;
            const int ATTR_LINE_HEIGHT = 15;
            const int ATTR_PADDING = 10;
            const int MIN_ATTRS = 4;
            const int BARYCENTER_ITERATIONS = 4;

            // ── Entity lookup ──
            var entityByName = new Dictionary<string, IEntity>();
            foreach (var e in entities)
                entityByName[e.Name] = e;

            // ── Phase A: Build UNDIRECTED graph from associations ──
            // Mendix assoc.Parent/Child direction is inconsistent between UI-created and
            // tool-created associations, so we treat all edges as undirected and use
            // degree-based root selection for a consistent hierarchical layout.
            var neighbors = new Dictionary<string, HashSet<string>>();
            var edgeSet = new HashSet<(string, string)>();

            foreach (var e in entities)
                neighbors[e.Name] = new HashSet<string>();

            foreach (var entity in entities)
            {
                var associations = entity.GetAssociations(AssociationDirection.Both, null);
                if (associations == null) continue;

                foreach (var assoc in associations)
                {
                    string? nameA = null, nameB = null;
                    try
                    {
                        var parentEntity = assoc.Parent;
                        var childEntity = assoc.Child;
                        if (parentEntity != null && childEntity != null)
                        {
                            nameA = parentEntity.Name;
                            nameB = childEntity.Name;
                        }
                    }
                    catch { continue; }

                    if (nameA == null || nameB == null) continue;
                    if (!entityByName.ContainsKey(nameA) || !entityByName.ContainsKey(nameB)) continue;
                    if (nameA == nameB) continue;

                    // Undirected: add both directions to neighbor sets
                    neighbors[nameA].Add(nameB);
                    neighbors[nameB].Add(nameA);

                    // Canonical edge for crossing minimization (alphabetical order for dedup)
                    var canonical = string.Compare(nameA, nameB, StringComparison.Ordinal) < 0
                        ? (nameA, nameB) : (nameB, nameA);
                    edgeSet.Add(canonical);
                }
            }

            // ── Phase B: Layer assignment (BFS from highest-degree roots per component) ──
            // Find connected components, pick root = highest degree entity in each
            var visited = new HashSet<string>();
            var rootNames = new List<string>();

            // For each connected component: find hub (highest degree), then pick the node
            // farthest from hub as root. In hierarchical domain models, the farthest node
            // from the hub is typically a top-level entity (e.g., Company) or a leaf entity.
            // Picking the lower-degree endpoint of the graph diameter produces the best hierarchy.
            var byDegree = entities.OrderByDescending(e => neighbors[e.Name].Count).Select(e => e.Name).ToList();
            foreach (var name in byDegree)
            {
                if (visited.Contains(name)) continue;
                if (neighbors[name].Count == 0) continue; // orphan — skip

                // Flood-fill to collect entire component
                var component = new List<string>();
                var flood = new Queue<string>();
                flood.Enqueue(name);
                visited.Add(name);
                while (flood.Count > 0)
                {
                    var n = flood.Dequeue();
                    component.Add(n);
                    foreach (var nb in neighbors[n])
                    {
                        if (!visited.Contains(nb))
                        {
                            visited.Add(nb);
                            flood.Enqueue(nb);
                        }
                    }
                }

                // name = hub (highest degree, first entry). BFS from hub to find farthest node A.
                string farthestA = name;
                {
                    var dist = new Dictionary<string, int> { [name] = 0 };
                    var q = new Queue<string>();
                    q.Enqueue(name);
                    int maxDist = 0;
                    while (q.Count > 0)
                    {
                        var cur = q.Dequeue();
                        foreach (var nb in neighbors[cur])
                        {
                            if (!dist.ContainsKey(nb))
                            {
                                dist[nb] = dist[cur] + 1;
                                q.Enqueue(nb);
                                if (dist[nb] > maxDist)
                                {
                                    maxDist = dist[nb];
                                    farthestA = nb;
                                }
                            }
                        }
                    }
                }

                // BFS from A to find farthest node B (diameter endpoint)
                string farthestB = farthestA;
                {
                    var dist = new Dictionary<string, int> { [farthestA] = 0 };
                    var q = new Queue<string>();
                    q.Enqueue(farthestA);
                    int maxDist = 0;
                    while (q.Count > 0)
                    {
                        var cur = q.Dequeue();
                        foreach (var nb in neighbors[cur])
                        {
                            if (!dist.ContainsKey(nb))
                            {
                                dist[nb] = dist[cur] + 1;
                                q.Enqueue(nb);
                                if (dist[nb] > maxDist)
                                {
                                    maxDist = dist[nb];
                                    farthestB = nb;
                                }
                            }
                        }
                    }
                }

                // If user specified a root entity and it's in this component, use it
                if (!string.IsNullOrEmpty(rootEntity) && component.Contains(rootEntity))
                {
                    rootNames.Add(rootEntity);
                }
                else
                {
                    // Pick the diameter endpoint with lower degree as root (peripheral/top-level entity)
                    var root = neighbors[farthestA].Count <= neighbors[farthestB].Count ? farthestA : farthestB;
                    rootNames.Add(root);
                }
            }

            // BFS layer assignment from roots (undirected — visited set prevents backtracking)
            var layerOf = new Dictionary<string, int>();
            var bfsChildren = new Dictionary<string, List<string>>();
            foreach (var e in entities)
                bfsChildren[e.Name] = new List<string>();

            var bfsQueue = new Queue<string>();
            foreach (var r in rootNames)
            {
                layerOf[r] = 0;
                bfsQueue.Enqueue(r);
            }

            while (bfsQueue.Count > 0)
            {
                var node = bfsQueue.Dequeue();
                var nodeLayer = layerOf[node];
                foreach (var nb in neighbors[node])
                {
                    if (!layerOf.ContainsKey(nb))
                    {
                        layerOf[nb] = nodeLayer + 1;
                        bfsChildren[node].Add(nb);
                        bfsQueue.Enqueue(nb);
                    }
                }
            }

            // Orphans: entities with no associations at all
            var orphanNames = new List<string>();
            foreach (var e in entities)
            {
                if (!layerOf.ContainsKey(e.Name))
                    orphanNames.Add(e.Name);
            }

            // Build layers dictionary: layer number → ordered list of entity names
            var maxLayer = layerOf.Values.Any() ? layerOf.Values.Max() : 0;
            var layers = new Dictionary<int, List<string>>();
            for (int i = 0; i <= maxLayer; i++)
                layers[i] = new List<string>();
            foreach (var kvp in layerOf)
                layers[kvp.Value].Add(kvp.Key);

            // ── Phase C: Crossing minimization (Barycenter heuristic) ──
            // Edge set uses canonical (alphabetical) ordering, so check both permutations
            double Barycenter(string node, List<string> referenceLayer, HashSet<(string, string)> edges)
            {
                var refIndices = new List<int>();
                for (int i = 0; i < referenceLayer.Count; i++)
                {
                    var refNode = referenceLayer[i];
                    if (edges.Contains((node, refNode)) || edges.Contains((refNode, node)))
                        refIndices.Add(i);
                }
                return refIndices.Count > 0 ? refIndices.Average() : double.MaxValue;
            }

            for (int iter = 0; iter < BARYCENTER_ITERATIONS; iter++)
            {
                // Top-down sweep: fix layer i, reorder layer i+1
                for (int i = 0; i < maxLayer; i++)
                {
                    var fixedLayer = layers[i];
                    var freeLayer = layers[i + 1];
                    if (freeLayer.Count <= 1) continue;

                    var sorted = freeLayer
                        .Select(n => (name: n, bc: Barycenter(n, fixedLayer, edgeSet)))
                        .OrderBy(x => x.bc)
                        .Select(x => x.name)
                        .ToList();
                    layers[i + 1] = sorted;
                }

                // Bottom-up sweep: fix layer i+1, reorder layer i
                for (int i = maxLayer; i > 0; i--)
                {
                    var fixedLayer = layers[i];
                    var freeLayer = layers[i - 1];
                    if (freeLayer.Count <= 1) continue;

                    var sorted = freeLayer
                        .Select(n => (name: n, bc: Barycenter(n, fixedLayer, edgeSet)))
                        .OrderBy(x => x.bc)
                        .Select(x => x.name)
                        .ToList();
                    layers[i - 1] = sorted;
                }
            }

            // ── Phase D: Coordinate assignment ──
            int EstimateHeight(IEntity e)
            {
                var attrCount = Math.Max(MIN_ATTRS, e.GetAttributes().Count());
                return (int)(attrCount * ATTR_LINE_HEIGHT + ATTR_PADDING);
            }

            // Find the widest layer to determine total canvas width
            int maxLayerCount = layers.Values.Any() ? layers.Values.Max(l => l.Count) : 1;
            int layerTotalWidth = maxLayerCount * ENTITY_WIDTH + (maxLayerCount - 1) * H_GAP;

            var positions = new Dictionary<string, (int x, int y)>();
            int currentY = START_Y;

            for (int layer = 0; layer <= maxLayer; layer++)
            {
                var layerNodes = layers[layer];
                if (layerNodes.Count == 0) continue;

                // Calculate this layer's width and center it relative to the widest layer
                int thisLayerWidth = layerNodes.Count * ENTITY_WIDTH + (layerNodes.Count - 1) * H_GAP;
                int offsetX = START_X + (layerTotalWidth - thisLayerWidth) / 2;

                // Find max entity height in this layer for uniform vertical spacing
                int maxHeight = 0;
                for (int i = 0; i < layerNodes.Count; i++)
                {
                    var x = offsetX + i * (ENTITY_WIDTH + H_GAP);
                    positions[layerNodes[i]] = (x, currentY);

                    if (entityByName.TryGetValue(layerNodes[i], out var ent))
                    {
                        var h = EstimateHeight(ent);
                        if (h > maxHeight) maxHeight = h;
                    }
                }

                currentY += (maxHeight > 0 ? maxHeight : 80) + V_SPACING;
            }

            // ── Phase D2: Parent centering post-pass ──
            // Shift each parent toward the center of its children (within layer bounds)
            for (int layer = 0; layer <= maxLayer - 1; layer++)
            {
                var layerNodes = layers[layer];
                foreach (var node in layerNodes)
                {
                    var nodeChildren = bfsChildren[node].Where(c => layerOf.ContainsKey(c) && layerOf[c] == layer + 1).ToList();
                    if (nodeChildren.Count < 2) continue;

                    var childPositions = nodeChildren.Where(c => positions.ContainsKey(c)).Select(c => positions[c].x).ToList();
                    if (childPositions.Count < 2) continue;

                    int childCenter = (childPositions.Min() + childPositions.Max() + ENTITY_WIDTH) / 2 - ENTITY_WIDTH / 2;
                    var currentPos = positions[node];

                    // Only shift if it doesn't overlap siblings
                    var siblings = layerNodes.Where(n => n != node && positions.ContainsKey(n)).ToList();
                    bool canShift = true;
                    foreach (var sib in siblings)
                    {
                        var sibX = positions[sib].x;
                        if (Math.Abs(childCenter - sibX) < ENTITY_WIDTH + H_GAP / 2)
                        {
                            canShift = false;
                            break;
                        }
                    }

                    if (canShift)
                        positions[node] = (childCenter, currentPos.y);
                }
            }

            // ── Phase E: Orphan placement (adaptive grid) ──
            int orphanCount = 0;
            if (orphanNames.Count > 0)
            {
                int orphanCols = Math.Min(6, (int)Math.Ceiling(Math.Sqrt(orphanNames.Count)));
                int orphanY = positions.Values.Any()
                    ? positions.Values.Max(p => p.y) + 150 + V_SPACING
                    : START_Y;
                int orphanX = START_X;
                int col = 0;

                foreach (var orphanName in orphanNames)
                {
                    positions[orphanName] = (orphanX, orphanY);
                    orphanCount++;
                    col++;

                    if (col >= orphanCols)
                    {
                        col = 0;
                        orphanX = START_X;
                        orphanY += (entityByName.ContainsKey(orphanName) ? EstimateHeight(entityByName[orphanName]) : 80) + V_SPACING;
                    }
                    else
                    {
                        orphanX += ENTITY_WIDTH + H_GAP;
                    }
                }
            }

            // ── Apply positions to entities ──
            foreach (var kvp in positions)
            {
                if (entityByName.TryGetValue(kvp.Key, out var entity))
                    entity.Location = new Location(kvp.Value.x, kvp.Value.y);
            }

            // ── Calculate bounding box ──
            int minX = positions.Values.Min(p => p.x);
            int minY = positions.Values.Min(p => p.y);
            int maxXFinal = positions.Values.Max(p => p.x) + ENTITY_WIDTH;
            int maxYFinal = positions.Values.Max(p => p.y) + 150;

            int treeCount = rootNames.Count;

            return new
            {
                success = true,
                entities_arranged = positions.Count,
                trees = treeCount,
                orphans = orphanCount,
                bounding_box = new { x = minX, y = minY, width = maxXFinal - minX, height = maxYFinal - minY },
                layout = positions.Select(p => new { entity = p.Key, x = p.Value.x, y = p.Value.y }).ToList()
            };
        }

        #endregion

        private static readonly HashSet<string> UsedNames = new HashSet<string>();

        private string GetUniqueName(string baseName)
        {
            if (!UsedNames.Contains(baseName))
            {
                UsedNames.Add(baseName);
                return baseName;
            }

            int counter = 1;
            string uniqueName;
            do
            {
                uniqueName = $"{baseName}{counter}";
                counter++;
            } while (UsedNames.Contains(uniqueName));

            UsedNames.Add(uniqueName);
            return uniqueName;
        }

        private string DeleteDocument<T>(IModule module, string documentName, string typeName) where T : class, IDocument
        {
            using (var transaction = _model.StartTransaction($"Delete {typeName}"))
            {
                var document = module.GetDocuments().OfType<T>()
                    .FirstOrDefault(d => d.Name.Equals(documentName, StringComparison.OrdinalIgnoreCase));

                if (document == null)
                {
                    return JsonSerializer.Serialize(new { error = $"{typeName} '{documentName}' not found in module '{module.Name}'" });
                }

                module.RemoveDocument(document);
                transaction.Commit();

                _logger.LogInformation($"Deleted {typeName} '{documentName}' from module '{module.Name}'");
                return JsonSerializer.Serialize(new { success = true, message = $"{typeName} '{documentName}' deleted successfully from module '{module.Name}'" });
            }
        }

        private string DeleteConstant(IModule module, string constantName)
        {
            using (var transaction = _model.StartTransaction($"Delete Constant '{constantName}'"))
            {
                var constant = module.GetDocuments().OfType<IConstant>()
                    .FirstOrDefault(d => d.Name.Equals(constantName, StringComparison.OrdinalIgnoreCase));

                if (constant == null)
                {
                    return JsonSerializer.Serialize(new { error = $"Constant '{constantName}' not found in module '{module.Name}'" });
                }

                // Clean up configuration constant value references before deleting
                var constantQualifiedName = constant.QualifiedName?.FullName;
                if (!string.IsNullOrEmpty(constantQualifiedName))
                {
                    try
                    {
                        var project = _model.Root as IProject;
                        var settings = project?.GetProjectDocuments().OfType<IProjectSettings>().FirstOrDefault();
                        var configSettings = settings?.GetSettingsParts().OfType<IConfigurationSettings>().FirstOrDefault();
                        if (configSettings != null)
                        {
                            foreach (var config in configSettings.GetConfigurations())
                            {
                                var orphanedValues = config.GetConstantValues()
                                    .Where(cv => cv.Constant?.FullName == constantQualifiedName)
                                    .ToList();
                                foreach (var orphan in orphanedValues)
                                {
                                    config.RemoveConstantValue(orphan);
                                    _logger.LogInformation($"Removed constant value reference for '{constantQualifiedName}' from configuration '{config.Name}'");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not clean up configuration references for constant '{constantName}': {ex.Message}");
                    }
                }

                module.RemoveDocument(constant);
                transaction.Commit();

                _logger.LogInformation($"Deleted Constant '{constantName}' from module '{module.Name}'");
                return JsonSerializer.Serialize(new { success = true, message = $"Constant '{constantName}' deleted successfully from module '{module.Name}'" });
            }
        }

        private string DeleteEntity(IDomainModel domainModel, string entityName)
        {
            using (var transaction = _model.StartTransaction("Delete Entity"))
            {
                var entity = domainModel.GetEntities().FirstOrDefault(e => e.Name == entityName);
                if (entity == null)
                {
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });
                }

                // Delete all associations first
                var entityAssociations = entity.GetAssociations(AssociationDirection.Both, null).ToList();
                foreach (var entityAssociation in entityAssociations)
                {
                    var association = entityAssociation.Association;
                    entity.DeleteAssociation(association);
                }

                domainModel.RemoveEntity(entity);
                transaction.Commit();

                return JsonSerializer.Serialize(new 
                { 
                    success = true, 
                    message = $"Entity '{entityName}' and its associations deleted successfully" 
                });
            }
        }

        private string DeleteAttribute(IDomainModel domainModel, string entityName, string attributeName)
        {
            using (var transaction = _model.StartTransaction("Delete Attribute"))
            {
                var entity = domainModel.GetEntities().FirstOrDefault(e => e.Name == entityName);
                if (entity == null)
                {
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });
                }

                var attribute = entity.GetAttributes().FirstOrDefault(a => a.Name == attributeName);
                if (attribute == null)
                {
                    return JsonSerializer.Serialize(new { error = $"Attribute '{attributeName}' not found in entity '{entityName}'" });
                }

                entity.RemoveAttribute(attribute);
                transaction.Commit();

                return JsonSerializer.Serialize(new 
                { 
                    success = true, 
                    message = $"Attribute '{attributeName}' deleted successfully from entity '{entityName}'" 
                });
            }
        }

        private string DeleteAssociation(IDomainModel domainModel, string entityName, string associationName)
        {
            using (var transaction = _model.StartTransaction("Delete Association"))
            {
                var entity = domainModel.GetEntities().FirstOrDefault(e => e.Name == entityName);
                if (entity == null)
                {
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });
                }

                var entityAssociation = entity.GetAssociations(AssociationDirection.Both, null)
                    .FirstOrDefault(a => a.Association.Name == associationName);
                if (entityAssociation == null)
                {
                    return JsonSerializer.Serialize(new { error = $"Association '{associationName}' not found" });
                }

                var association = entityAssociation.Association;
                entity.DeleteAssociation(association);
                transaction.Commit();

                return JsonSerializer.Serialize(new 
                { 
                    success = true, 
                    message = $"Association '{associationName}' deleted successfully" 
                });
            }
        }

        #region Entity Template Methods

        /// <summary>
        /// Finds the template non-persistent entity (AIExtension.NPE) for copying
        /// </summary>
        /// <returns>The template entity if found, null otherwise</returns>
        private IEntity? FindNonPersistentTemplate()
        {
            return FindTemplateEntity("NPE", "non-persistent");
        }

        /// <summary>
        /// Finds the template FileDocument entity (AIExtension.FileDocument) for copying
        /// </summary>
        /// <returns>The template entity if found, null otherwise</returns>
        private IEntity? FindFileDocumentTemplate()
        {
            return FindTemplateEntity("FileDocument", "FileDocument");
        }

        /// <summary>
        /// Finds the template Image entity (AIExtension.Image) for copying
        /// </summary>
        /// <returns>The template entity if found, null otherwise</returns>
        private IEntity? FindImageTemplate()
        {
            return FindTemplateEntity("Image", "Image");
        }

        /// <summary>
        /// Finds the template StoreCreatedDate entity (AIExtension.StoreCreatedDate) for copying
        /// </summary>
        /// <returns>The template entity if found, null otherwise</returns>
        private IEntity? FindStoreCreatedDateTemplate()
        {
            return FindTemplateEntity("StoreCreatedDate", "StoreCreatedDate");
        }

        /// <summary>
        /// Finds the template StoreChangeDate entity (AIExtension.StoreChangeDate) for copying
        /// </summary>
        /// <returns>The template entity if found, null otherwise</returns>
        private IEntity? FindStoreChangeDateTemplate()
        {
            return FindTemplateEntity("StoreChangeDate", "StoreChangeDate");
        }

        /// <summary>
        /// Finds the template StoreCreatedChangeDate entity (AIExtension.StoreCreatedChangeDate) for copying
        /// </summary>
        /// <returns>The template entity if found, null otherwise</returns>
        private IEntity? FindStoreCreatedChangeDateTemplate()
        {
            return FindTemplateEntity("StoreCreatedChangeDate", "StoreCreatedChangeDate");
        }

        /// <summary>
        /// Finds the template StoreOwner entity (AIExtension.StoreOwner) for copying
        /// </summary>
        /// <returns>The template entity if found, null otherwise</returns>
        private IEntity? FindStoreOwnerTemplate()
        {
            return FindTemplateEntity("StoreOwner", "StoreOwner");
        }

        /// <summary>
        /// Finds the template StoreChangeBy entity (AIExtension.StoreChangeBy) for copying
        /// </summary>
        /// <returns>The template entity if found, null otherwise</returns>
        private IEntity? FindStoreChangeByTemplate()
        {
            return FindTemplateEntity("StoreChangeBy", "StoreChangeBy");
        }

        /// <summary>
        /// Generic method to find template entities in the AIExtension module
        /// </summary>
        /// <param name="templateName">Name of the template entity to find</param>
        /// <param name="templateType">Type description for logging purposes</param>
        /// <returns>The template entity if found, null otherwise</returns>
        private IEntity? FindTemplateEntity(string templateName, string templateType)
        {
            try
            {
                // Use the same module access pattern as the rest of the codebase
                // Find all modules the safe way
                var modules = _model.Root.GetModules();
                
                // Look specifically for AIExtension module
                var aiExtensionModule = modules.FirstOrDefault(m => m?.Name == "AIExtension");

                if (aiExtensionModule?.DomainModel == null)
                {
                    _logger.LogWarning($"AIExtension module or its domain model not found for {templateType} template");
                    return null;
                }

                // Find the specified entity in AIExtension
                var templateEntity = aiExtensionModule.DomainModel.GetEntities()
                    .FirstOrDefault(e => e?.Name == templateName);

                if (templateEntity == null)
                {
                    _logger.LogWarning($"{templateName} template entity not found in AIExtension module");
                    return null;
                }

                _logger.LogInformation($"Found {templateType} template entity: AIExtension.{templateName}");
                return templateEntity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error finding {templateType} template entity");
                return null;
            }
        }        /// <summary>
        /// Creates an entity by copying from a template based on entity type
        /// </summary>
        /// <param name="targetModule">Module where the new entity will be created</param>
        /// <param name="entityName">Name for the new entity</param>
        /// <param name="attributesArray">Attributes to add to the entity</param>
        /// <param name="entityType">Type of entity: "persistent", "non-persistent", "filedocument", "image"</param>
        /// <returns>The created entity if successful, null otherwise</returns>
        private IEntity? CreateEntityFromTemplate(IModule targetModule, string entityName, JsonArray? attributesArray, string entityType = "persistent")
        {
            try
            {
                IEntity? templateEntity = null;
                string templateDescription = "";

                // Find the appropriate template based on entity type
                switch (entityType.ToLower())
                {
                    case "non-persistent":
                        templateEntity = FindNonPersistentTemplate();
                        templateDescription = "non-persistent";
                        break;
                    case "filedocument":
                        templateEntity = FindFileDocumentTemplate();
                        templateDescription = "FileDocument";
                        break;
                    case "image":
                        templateEntity = FindImageTemplate();
                        templateDescription = "Image";
                        break;
                    case "storecreateddate":
                        templateEntity = FindStoreCreatedDateTemplate();
                        templateDescription = "StoreCreatedDate";
                        break;
                    case "storechangedate":
                        templateEntity = FindStoreChangeDateTemplate();
                        templateDescription = "StoreChangeDate";
                        break;
                    case "storecreatedchangedate":
                        templateEntity = FindStoreCreatedChangeDateTemplate();
                        templateDescription = "StoreCreatedChangeDate";
                        break;
                    case "storeowner":
                        templateEntity = FindStoreOwnerTemplate();
                        templateDescription = "StoreOwner";
                        break;
                    case "storechangeby":
                        templateEntity = FindStoreChangeByTemplate();
                        templateDescription = "StoreChangeBy";
                        break;
                    case "persistent":
                    default:
                        // For persistent entities, create normally without template
                        return CreatePersistentEntity(targetModule, entityName, attributesArray);
                }

                if (templateEntity == null)
                {
                    _logger.LogError($"Cannot create {templateDescription} entity: template not found");
                    return null;
                }

                // Copy the template entity (this preserves the special properties)
                var newEntity = _model.Copy(templateEntity);

                // Rename the entity
                newEntity.Name = entityName;

                // Add the desired attributes
                if (attributesArray != null)
                {
                    foreach (var attrNode in attributesArray)
                    {
                        var attrObj = attrNode?.AsObject();
                        if (attrObj == null) continue;

                        var attrName = attrObj["name"]?.ToString();
                        var attrType = attrObj["type"]?.ToString();

                        if (string.IsNullOrEmpty(attrName) || string.IsNullOrEmpty(attrType)) continue;

                        var mxAttribute = _model.Create<IAttribute>();
                        mxAttribute.Name = attrName;

                        if (attrType.StartsWith("Enumeration:", StringComparison.OrdinalIgnoreCase))
                        {
                            // "Enumeration:EnumName" syntax — link to existing enumeration
                            var enumName = attrType.Substring("Enumeration:".Length).Trim();
                            var explicitEnumName = attrObj["enumeration_name"]?.ToString();
                            if (!string.IsNullOrEmpty(explicitEnumName))
                                enumName = explicitEnumName;

                            var foundEnum = FindExistingEnumeration(enumName);
                            if (foundEnum != null)
                            {
                                var enumTypeInstance = _model.Create<IEnumerationAttributeType>();
                                enumTypeInstance.Enumeration = foundEnum.QualifiedName;
                                mxAttribute.Type = enumTypeInstance;
                            }
                            else
                            {
                                _logger.LogWarning($"Enumeration '{enumName}' not found — skipping attribute '{attrName}'");
                                continue;
                            }
                        }
                        else if (attrType.Equals("Enumeration", StringComparison.OrdinalIgnoreCase))
                        {
                            // Plain "Enumeration" type — check enumeration_name or enumerationValues
                            var explicitEnumName = attrObj["enumeration_name"]?.ToString();
                            if (!string.IsNullOrEmpty(explicitEnumName))
                            {
                                var foundEnum = FindExistingEnumeration(explicitEnumName);
                                if (foundEnum != null)
                                {
                                    var enumTypeInstance = _model.Create<IEnumerationAttributeType>();
                                    enumTypeInstance.Enumeration = foundEnum.QualifiedName;
                                    mxAttribute.Type = enumTypeInstance;
                                }
                                else
                                {
                                    _logger.LogWarning($"Enumeration '{explicitEnumName}' not found — skipping attribute '{attrName}'");
                                    continue;
                                }
                            }
                            else
                            {
                                var enumValues = attrObj["enumerationValues"]?.AsArray()
                                    ?.Select(v => v?.ToString())
                                    ?.Where(v => !string.IsNullOrEmpty(v))
                                    ?.ToList();

                                if (enumValues != null && enumValues.Any())
                                {
                                    var enumTypeInstance = CreateEnumerationType(_model, attrName, enumValues, targetModule);
                                    mxAttribute.Type = enumTypeInstance;
                                }
                                else
                                {
                                    _logger.LogWarning($"Enumeration attribute '{attrName}' requires 'enumeration_name' or 'enumerationValues' — skipping");
                                    continue;
                                }
                            }
                        }
                        else
                        {
                            var attributeType = CreateAttributeType(_model, attrType);
                            mxAttribute.Type = attributeType;
                        }

                        // Set default value if provided
                        var defaultVal = attrObj["default_value"]?.ToString();
                        if (!string.IsNullOrEmpty(defaultVal))
                        {
                            var storedValue = _model.Create<IStoredValue>();
                            storedValue.DefaultValue = defaultVal;
                            mxAttribute.Value = storedValue;
                        }

                        newEntity.AddAttribute(mxAttribute);
                    }
                }

                // Add the entity to the target module
                targetModule.DomainModel.AddEntity(newEntity);

                // Position the entity
                PositionEntity(newEntity, targetModule.DomainModel.GetEntities().Count());

                _logger.LogInformation($"Successfully created {templateDescription} entity '{entityName}' from template");
                return newEntity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating entity '{entityName}' from template");
                return null;
            }
        }

        /// <summary>
        /// Creates a regular persistent entity without using templates
        /// </summary>
        /// <param name="targetModule">Module where the new entity will be created</param>
        /// <param name="entityName">Name for the new entity</param>
        /// <param name="attributesArray">Attributes to add to the entity</param>
        /// <returns>The created entity if successful, null otherwise</returns>
        private IEntity? CreatePersistentEntity(IModule targetModule, string entityName, JsonArray? attributesArray)
        {
            try
            {
                // Create regular persistent entity
                var mxEntity = _model.Create<IEntity>();
                mxEntity.Name = entityName;
                targetModule.DomainModel.AddEntity(mxEntity);

                // Add attributes if provided
                if (attributesArray != null)
                {
                    foreach (var attrNode in attributesArray)
                    {
                        var attrObj = attrNode?.AsObject();
                        if (attrObj == null) continue;

                        var attrName = attrObj["name"]?.ToString();
                        var attrType = attrObj["type"]?.ToString();

                        if (string.IsNullOrEmpty(attrName) || string.IsNullOrEmpty(attrType)) continue;

                        var mxAttribute = _model.Create<IAttribute>();
                        mxAttribute.Name = attrName;

                        if (attrType.StartsWith("Enumeration:", StringComparison.OrdinalIgnoreCase))
                        {
                            // "Enumeration:EnumName" syntax — link to existing enumeration
                            var enumName = attrType.Substring("Enumeration:".Length).Trim();
                            var explicitEnumName = attrObj["enumeration_name"]?.ToString();
                            if (!string.IsNullOrEmpty(explicitEnumName))
                                enumName = explicitEnumName;

                            var foundEnum = FindExistingEnumeration(enumName);
                            if (foundEnum != null)
                            {
                                var enumTypeInstance = _model.Create<IEnumerationAttributeType>();
                                enumTypeInstance.Enumeration = foundEnum.QualifiedName;
                                mxAttribute.Type = enumTypeInstance;
                            }
                            else
                            {
                                _logger.LogWarning($"Enumeration '{enumName}' not found — skipping attribute '{attrName}'");
                                continue;
                            }
                        }
                        else if (attrType.Equals("Enumeration", StringComparison.OrdinalIgnoreCase))
                        {
                            // Plain "Enumeration" type — check enumeration_name or enumerationValues
                            var explicitEnumName = attrObj["enumeration_name"]?.ToString();
                            if (!string.IsNullOrEmpty(explicitEnumName))
                            {
                                var foundEnum = FindExistingEnumeration(explicitEnumName);
                                if (foundEnum != null)
                                {
                                    var enumTypeInstance = _model.Create<IEnumerationAttributeType>();
                                    enumTypeInstance.Enumeration = foundEnum.QualifiedName;
                                    mxAttribute.Type = enumTypeInstance;
                                }
                                else
                                {
                                    _logger.LogWarning($"Enumeration '{explicitEnumName}' not found — skipping attribute '{attrName}'");
                                    continue;
                                }
                            }
                            else
                            {
                                var enumValues = attrObj["enumerationValues"]?.AsArray()
                                    ?.Select(v => v?.ToString())
                                    ?.Where(v => !string.IsNullOrEmpty(v))
                                    ?.ToList();

                                if (enumValues != null && enumValues.Any())
                                {
                                    var enumTypeInstance = CreateEnumerationType(_model, attrName, enumValues, targetModule);
                                    mxAttribute.Type = enumTypeInstance;
                                }
                                else
                                {
                                    _logger.LogWarning($"Enumeration attribute '{attrName}' requires 'enumeration_name' or 'enumerationValues' — skipping");
                                    continue;
                                }
                            }
                        }
                        else
                        {
                            var attributeType = CreateAttributeType(_model, attrType);
                            mxAttribute.Type = attributeType;
                        }

                        // Set default value if provided
                        var defaultVal = attrObj["default_value"]?.ToString();
                        if (!string.IsNullOrEmpty(defaultVal))
                        {
                            var storedValue = _model.Create<IStoredValue>();
                            storedValue.DefaultValue = defaultVal;
                            mxAttribute.Value = storedValue;
                        }

                        mxEntity.AddAttribute(mxAttribute);
                    }
                }

                // Position entity
                PositionEntity(mxEntity, targetModule.DomainModel.GetEntities().Count());

                return mxEntity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating persistent entity '{entityName}'");
                return null;
            }
        }

        /// <summary>
        /// Gets the template name for a given entity type
        /// </summary>
        /// <param name="entityType">The entity type</param>
        /// <returns>The template name</returns>
        private static string GetTemplateName(string entityType)
        {
            return entityType.ToLower() switch
            {
                "non-persistent" => "NPE",
                "filedocument" => "FileDocument",
                "image" => "Image",
                "storecreateddate" => "StoreCreatedDate",
                "storechangedate" => "StoreChangeDate",
                "storecreatedchangedate" => "StoreCreatedChangeDate",
                "storeowner" => "StoreOwner",
                "storechangeby" => "StoreChangeBy",
                _ => "Unknown"
            };
        }

        #endregion

        #region Phase 9: Entity Configuration & Module Organization

        public async Task<string> ConfigureSystemAttributes(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                if (string.IsNullOrEmpty(entityName))
                    return JsonSerializer.Serialize(new { error = "entity_name is required" });

                var moduleName = parameters["module_name"]?.ToString();
                var (entity, _) = Utils.Utils.FindEntityAcrossModules(_model, entityName, moduleName);
                if (entity == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });

                if (entity.Generalization is not INoGeneralization noGen)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' has a generalization (inherits from another entity). System attributes can only be configured on root entities." });

                using var transaction = _model.StartTransaction("Configure system attributes");

                bool changed = false;
                if (parameters.ContainsKey("has_created_date"))
                {
                    noGen.HasCreatedDate = parameters["has_created_date"]?.GetValue<bool>() ?? false;
                    changed = true;
                }
                if (parameters.ContainsKey("has_changed_date"))
                {
                    noGen.HasChangedDate = parameters["has_changed_date"]?.GetValue<bool>() ?? false;
                    changed = true;
                }
                if (parameters.ContainsKey("has_owner"))
                {
                    noGen.HasOwner = parameters["has_owner"]?.GetValue<bool>() ?? false;
                    changed = true;
                }
                if (parameters.ContainsKey("has_changed_by"))
                {
                    noGen.HasChangedBy = parameters["has_changed_by"]?.GetValue<bool>() ?? false;
                    changed = true;
                }
                if (parameters.ContainsKey("persistable"))
                {
                    noGen.Persistable = parameters["persistable"]?.GetValue<bool>() ?? true;
                    changed = true;
                }

                if (!changed)
                    return JsonSerializer.Serialize(new { error = "No system attribute parameters provided. Use has_created_date, has_changed_date, has_owner, has_changed_by, or persistable." });

                transaction.Commit();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    entity = entityName,
                    hasCreatedDate = noGen.HasCreatedDate,
                    hasChangedDate = noGen.HasChangedDate,
                    hasOwner = noGen.HasOwner,
                    hasChangedBy = noGen.HasChangedBy,
                    persistable = noGen.Persistable
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error configuring system attributes");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ManageFolders(JsonObject parameters)
        {
            try
            {
                var action = parameters["action"]?.ToString()?.ToLowerInvariant();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(action))
                    return JsonSerializer.Serialize(new { error = "action is required: 'list', 'create', or 'move_document'" });

                var module = Utils.Utils.ResolveModule(_model, moduleName);
                if (module == null)
                    return JsonSerializer.Serialize(new { error = $"Module '{moduleName ?? "(default)"}' not found" });

                switch (action)
                {
                    case "list":
                        return ListFoldersRecursive(module);

                    case "create":
                    {
                        var folderName = parameters["folder_name"]?.ToString();
                        if (string.IsNullOrEmpty(folderName))
                            return JsonSerializer.Serialize(new { error = "folder_name is required for 'create' action" });

                        var parentFolderName = parameters["parent_folder"]?.ToString();

                        using var transaction = _model.StartTransaction("Create folder");
                        var newFolder = _model.Create<IFolder>();
                        newFolder.Name = folderName;

                        if (!string.IsNullOrEmpty(parentFolderName))
                        {
                            var parentFolder = FindFolderRecursive(module, parentFolderName);
                            if (parentFolder == null)
                                return JsonSerializer.Serialize(new { error = $"Parent folder '{parentFolderName}' not found in module '{module.Name}'" });
                            parentFolder.AddFolder(newFolder);
                        }
                        else
                        {
                            module.AddFolder(newFolder);
                        }

                        transaction.Commit();
                        return JsonSerializer.Serialize(new { success = true, folder = folderName, module = module.Name, parent = parentFolderName ?? "(root)" });
                    }

                    case "move_document":
                    {
                        var documentName = parameters["document_name"]?.ToString();
                        var targetFolderName = parameters["target_folder"]?.ToString();
                        if (string.IsNullOrEmpty(documentName))
                            return JsonSerializer.Serialize(new { error = "document_name is required for 'move_document' action" });
                        if (string.IsNullOrEmpty(targetFolderName))
                            return JsonSerializer.Serialize(new { error = "target_folder is required for 'move_document' action" });

                        IDocument? doc = null;
                        IFolderBase? sourceParent = null;

                        doc = module.GetDocuments().FirstOrDefault(d => d.Name.Equals(documentName, StringComparison.OrdinalIgnoreCase));
                        if (doc != null)
                        {
                            sourceParent = module;
                        }
                        else
                        {
                            (doc, sourceParent) = FindDocumentWithParent(module, documentName);
                        }

                        if (doc == null)
                            return JsonSerializer.Serialize(new { error = $"Document '{documentName}' not found in module '{module.Name}'" });

                        var targetFolder = FindFolderRecursive(module, targetFolderName);
                        if (targetFolder == null)
                            return JsonSerializer.Serialize(new { error = $"Target folder '{targetFolderName}' not found in module '{module.Name}'" });

                        using var transaction = _model.StartTransaction("Move document to folder");
                        sourceParent!.RemoveDocument(doc);
                        targetFolder.AddDocument(doc);
                        transaction.Commit();

                        return JsonSerializer.Serialize(new { success = true, document = documentName, movedTo = targetFolderName });
                    }

                    default:
                        return JsonSerializer.Serialize(new { error = $"Unknown action '{action}'. Use 'list', 'create', or 'move_document'." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error managing folders");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private string ListFoldersRecursive(IModule module)
        {
            var result = new List<object>();
            CollectFolders(module, "", result);
            return JsonSerializer.Serialize(new { success = true, module = module.Name, folders = result, rootDocuments = module.GetDocuments().Select(d => d.Name).ToList() });
        }

        private void CollectFolders(IFolderBase parent, string path, List<object> result)
        {
            foreach (var folder in parent.GetFolders())
            {
                var folderPath = string.IsNullOrEmpty(path) ? folder.Name : $"{path}/{folder.Name}";
                result.Add(new
                {
                    name = folder.Name,
                    path = folderPath,
                    documentCount = folder.GetDocuments().Count,
                    documents = folder.GetDocuments().Select(d => d.Name).ToList(),
                    subfolderCount = folder.GetFolders().Count
                });
                CollectFolders(folder, folderPath, result);
            }
        }

        private IFolder? FindFolderRecursive(IFolderBase parent, string folderName)
        {
            foreach (var folder in parent.GetFolders())
            {
                if (folder.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase))
                    return folder;
                var found = FindFolderRecursive(folder, folderName);
                if (found != null)
                    return found;
            }
            return null;
        }

        private IDocument? FindDocumentRecursive(IFolderBase parent, string documentName)
        {
            foreach (var folder in parent.GetFolders())
            {
                var doc = folder.GetDocuments().FirstOrDefault(d => d.Name.Equals(documentName, StringComparison.OrdinalIgnoreCase));
                if (doc != null) return doc;
                var found = FindDocumentRecursive(folder, documentName);
                if (found != null) return found;
            }
            return null;
        }

        private (IDocument? doc, IFolderBase? parent) FindDocumentWithParent(IFolderBase parent, string documentName)
        {
            foreach (var folder in parent.GetFolders())
            {
                var doc = folder.GetDocuments().FirstOrDefault(d => d.Name.Equals(documentName, StringComparison.OrdinalIgnoreCase));
                if (doc != null) return (doc, folder);
                var result = FindDocumentWithParent(folder, documentName);
                if (result.doc != null) return result;
            }
            return (null, null);
        }

        public async Task<string> ValidateName(JsonObject parameters)
        {
            try
            {
                var name = parameters["name"]?.ToString();
                if (string.IsNullOrEmpty(name))
                    return JsonSerializer.Serialize(new { error = "name is required" });

                if (_nameValidationService == null)
                    return JsonSerializer.Serialize(new { error = "INameValidationService is not available" });

                var validationResult = _nameValidationService.IsNameValid(name);
                var autoFix = parameters["auto_fix"]?.GetValue<bool>() ?? false;

                string? fixedName = null;
                if (!validationResult.IsValid && autoFix)
                {
                    fixedName = _nameValidationService.GetValidName(name);
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    name,
                    isValid = validationResult.IsValid,
                    errorMessage = validationResult.IsValid ? null : validationResult.ErrorMessage,
                    fixedName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating name");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> CopyModelElement(JsonObject parameters)
        {
            try
            {
                var elementType = parameters["element_type"]?.ToString()?.ToLowerInvariant();
                var sourceName = parameters["source_name"]?.ToString();
                var newName = parameters["new_name"]?.ToString();
                var sourceModuleName = parameters["source_module"]?.ToString();
                var targetModuleName = parameters["target_module"]?.ToString();

                if (string.IsNullOrEmpty(elementType))
                    return JsonSerializer.Serialize(new { error = "element_type is required: 'entity', 'microflow', 'constant', 'enumeration'" });
                if (string.IsNullOrEmpty(sourceName))
                    return JsonSerializer.Serialize(new { error = "source_name is required" });
                if (string.IsNullOrEmpty(newName))
                    return JsonSerializer.Serialize(new { error = "new_name is required" });

                var sourceModule = Utils.Utils.ResolveModule(_model, sourceModuleName);
                // BUG-013 fix: If no explicit target_module, default to source module (not MyFirstModule)
                var targetModule = !string.IsNullOrWhiteSpace(targetModuleName)
                    ? Utils.Utils.ResolveModule(_model, targetModuleName)
                    : sourceModule;
                if (sourceModule == null)
                    return JsonSerializer.Serialize(new { error = $"Source module '{sourceModuleName ?? "(default)"}' not found" });
                if (targetModule == null)
                    return JsonSerializer.Serialize(new { error = $"Target module '{targetModuleName ?? "(default)"}' not found" });

                using var transaction = _model.StartTransaction($"Copy {elementType} '{sourceName}' as '{newName}'");

                switch (elementType)
                {
                    case "entity":
                    {
                        var sourceEntity = sourceModule.DomainModel.GetEntities()
                            .FirstOrDefault(e => e.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
                        if (sourceEntity == null)
                            return JsonSerializer.Serialize(new { error = $"Entity '{sourceName}' not found in module '{sourceModule.Name}'" });

                        var copy = _model.Copy(sourceEntity);
                        copy.Name = newName;
                        targetModule.DomainModel.AddEntity(copy);
                        transaction.Commit();
                        return JsonSerializer.Serialize(new { success = true, elementType, source = sourceName, copy = newName, targetModule = targetModule.Name });
                    }
                    case "microflow":
                    {
                        var sourceMf = sourceModule.GetDocuments().OfType<IMicroflow>()
                            .FirstOrDefault(m => m.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
                        if (sourceMf == null)
                            return JsonSerializer.Serialize(new { error = $"Microflow '{sourceName}' not found in module '{sourceModule.Name}'" });

                        var copy = _model.Copy(sourceMf);
                        copy.Name = newName;
                        targetModule.AddDocument(copy);
                        transaction.Commit();
                        return JsonSerializer.Serialize(new { success = true, elementType, source = sourceName, copy = newName, targetModule = targetModule.Name });
                    }
                    case "constant":
                    {
                        var sourceConst = _model.Root.GetModuleDocuments<IConstant>(sourceModule)
                            .FirstOrDefault(c => c.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
                        if (sourceConst == null)
                            return JsonSerializer.Serialize(new { error = $"Constant '{sourceName}' not found in module '{sourceModule.Name}'" });

                        var copy = _model.Copy(sourceConst);
                        copy.Name = newName;
                        targetModule.AddDocument(copy);
                        transaction.Commit();
                        return JsonSerializer.Serialize(new { success = true, elementType, source = sourceName, copy = newName, targetModule = targetModule.Name });
                    }
                    case "enumeration":
                    {
                        var sourceEnum = _model.Root.GetModuleDocuments<IEnumeration>(sourceModule)
                            .FirstOrDefault(e => e.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
                        if (sourceEnum == null)
                            return JsonSerializer.Serialize(new { error = $"Enumeration '{sourceName}' not found in module '{sourceModule.Name}'" });

                        var copy = _model.Copy(sourceEnum);
                        copy.Name = newName;
                        targetModule.AddDocument(copy);
                        transaction.Commit();
                        return JsonSerializer.Serialize(new { success = true, elementType, source = sourceName, copy = newName, targetModule = targetModule.Name });
                    }
                    default:
                        return JsonSerializer.Serialize(new { error = $"Unknown element_type '{elementType}'. Supported: entity, microflow, constant, enumeration." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying model element");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 13: Rename & Refactor

        public async Task<string> RenameEntity(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var newName = parameters["new_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(entityName))
                    return JsonSerializer.Serialize(new { error = "entity_name is required" });
                if (string.IsNullOrEmpty(newName))
                    return JsonSerializer.Serialize(new { error = "new_name is required" });

                if (_nameValidationService != null)
                {
                    var validation = _nameValidationService.IsNameValid(newName);
                    if (!validation.IsValid)
                        return JsonSerializer.Serialize(new { error = $"Invalid name '{newName}': {validation.ErrorMessage}" });
                }

                var (entity, module) = Utils.Utils.FindEntityAcrossModules(_model, entityName, moduleName);
                if (entity == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                var oldName = entity.Name;
                using var transaction = _model.StartTransaction($"Rename entity '{oldName}' to '{newName}'");
                entity.Name = newName;
                transaction.Commit();

                _logger.LogInformation($"Renamed entity '{oldName}' to '{newName}' in module '{module!.Name}'");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Entity renamed from '{oldName}' to '{newName}'",
                    module = module.Name,
                    oldName,
                    newName,
                    qualifiedName = $"{module.Name}.{newName}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming entity");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> RenameAttribute(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var attributeName = parameters["attribute_name"]?.ToString();
                var newName = parameters["new_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(entityName))
                    return JsonSerializer.Serialize(new { error = "entity_name is required" });
                if (string.IsNullOrEmpty(attributeName))
                    return JsonSerializer.Serialize(new { error = "attribute_name is required" });
                if (string.IsNullOrEmpty(newName))
                    return JsonSerializer.Serialize(new { error = "new_name is required" });

                if (_nameValidationService != null)
                {
                    var validation = _nameValidationService.IsNameValid(newName);
                    if (!validation.IsValid)
                        return JsonSerializer.Serialize(new { error = $"Invalid name '{newName}': {validation.ErrorMessage}" });
                }

                var (entity, module) = Utils.Utils.FindEntityAcrossModules(_model, entityName, moduleName);
                if (entity == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                var attribute = entity.GetAttributes()
                    .FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                if (attribute == null)
                    return JsonSerializer.Serialize(new { error = $"Attribute '{attributeName}' not found on entity '{entity.Name}'" });

                var oldName = attribute.Name;
                using var transaction = _model.StartTransaction($"Rename attribute '{oldName}' to '{newName}' on '{entity.Name}'");
                attribute.Name = newName;
                transaction.Commit();

                _logger.LogInformation($"Renamed attribute '{oldName}' to '{newName}' on entity '{entity.Name}'");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Attribute renamed from '{oldName}' to '{newName}' on entity '{entity.Name}'",
                    entity = entity.Name,
                    module = module!.Name,
                    oldName,
                    newName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming attribute");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> RenameAssociation(JsonObject parameters)
        {
            try
            {
                var associationName = parameters["association_name"]?.ToString();
                var newName = parameters["new_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(associationName))
                    return JsonSerializer.Serialize(new { error = "association_name is required" });
                if (string.IsNullOrEmpty(newName))
                    return JsonSerializer.Serialize(new { error = "new_name is required" });

                if (_nameValidationService != null)
                {
                    var validation = _nameValidationService.IsNameValid(newName);
                    if (!validation.IsValid)
                        return JsonSerializer.Serialize(new { error = $"Invalid name '{newName}': {validation.ErrorMessage}" });
                }

                // Search for the association across all entities in the target module(s)
                IAssociation? foundAssociation = null;
                IModule? foundModule = null;

                var modules = moduleName != null
                    ? new[] { Utils.Utils.ResolveModule(_model, moduleName) }.Where(m => m != null)
                    : _model.Root.GetModules().Where(m => !m.FromAppStore);

                foreach (var mod in modules)
                {
                    foreach (var entity in mod!.DomainModel.GetEntities())
                    {
                        var assoc = entity.GetAssociations(AssociationDirection.Both, null)
                            .FirstOrDefault(a => a.Association.Name.Equals(associationName, StringComparison.OrdinalIgnoreCase));
                        if (assoc != null)
                        {
                            foundAssociation = assoc.Association;
                            foundModule = mod;
                            break;
                        }
                    }
                    if (foundAssociation != null) break;
                }

                if (foundAssociation == null)
                    return JsonSerializer.Serialize(new { error = $"Association '{associationName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                var oldName = foundAssociation.Name;
                using var transaction = _model.StartTransaction($"Rename association '{oldName}' to '{newName}'");
                foundAssociation.Name = newName;
                transaction.Commit();

                _logger.LogInformation($"Renamed association '{oldName}' to '{newName}' in module '{foundModule!.Name}'");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Association renamed from '{oldName}' to '{newName}'",
                    module = foundModule.Name,
                    oldName,
                    newName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming association");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> RenameDocument(JsonObject parameters)
        {
            try
            {
                var documentName = parameters["document_name"]?.ToString();
                var newName = parameters["new_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();
                var documentType = parameters["document_type"]?.ToString()?.ToLowerInvariant();

                if (string.IsNullOrEmpty(documentName))
                    return JsonSerializer.Serialize(new { error = "document_name is required" });
                if (string.IsNullOrEmpty(newName))
                    return JsonSerializer.Serialize(new { error = "new_name is required" });

                if (_nameValidationService != null)
                {
                    var validation = _nameValidationService.IsNameValid(newName);
                    if (!validation.IsValid)
                        return JsonSerializer.Serialize(new { error = $"Invalid name '{newName}': {validation.ErrorMessage}" });
                }

                // Handle qualified name (Module.DocumentName)
                if (documentName.Contains('.') && moduleName == null)
                {
                    var parts = documentName.Split('.', 2);
                    moduleName = parts[0];
                    documentName = parts[1];
                }

                var modules = moduleName != null
                    ? new[] { Utils.Utils.ResolveModule(_model, moduleName) }.Where(m => m != null)
                    : _model.Root.GetModules().Where(m => !m.FromAppStore);

                IDocument? foundDoc = null;
                IModule? foundModule = null;

                foreach (var mod in modules)
                {
                    var docs = mod!.GetDocuments();

                    // Filter by type if specified
                    IEnumerable<IDocument> filtered = documentType switch
                    {
                        "microflow" => docs.OfType<IMicroflow>(),
                        "constant" => docs.OfType<IConstant>(),
                        "enumeration" => docs.OfType<IEnumeration>(),
                        _ => docs
                    };

                    foundDoc = filtered.FirstOrDefault(d => d.Name.Equals(documentName, StringComparison.OrdinalIgnoreCase));

                    // If not found at root, search subfolders
                    if (foundDoc == null)
                    {
                        foundDoc = FindDocumentRecursive(mod, documentName);
                    }

                    if (foundDoc != null)
                    {
                        foundModule = mod;
                        break;
                    }
                }

                if (foundDoc == null)
                    return JsonSerializer.Serialize(new { error = $"Document '{documentName}'{(documentType != null ? $" (type: {documentType})" : "")} not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                var oldName = foundDoc.Name;
                var detectedType = foundDoc.GetType().Name;
                using var transaction = _model.StartTransaction($"Rename document '{oldName}' to '{newName}'");
                foundDoc.Name = newName;
                transaction.Commit();

                _logger.LogInformation($"Renamed document '{oldName}' to '{newName}' in module '{foundModule!.Name}'");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Document renamed from '{oldName}' to '{newName}' (all by-name references updated)",
                    module = foundModule.Name,
                    documentType = detectedType,
                    oldName,
                    newName,
                    qualifiedName = $"{foundModule.Name}.{newName}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming document");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> RenameModule(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters["module_name"]?.ToString();
                var newName = parameters["new_name"]?.ToString();

                if (string.IsNullOrEmpty(moduleName))
                    return JsonSerializer.Serialize(new { error = "module_name is required" });
                if (string.IsNullOrEmpty(newName))
                    return JsonSerializer.Serialize(new { error = "new_name is required" });

                if (_nameValidationService != null)
                {
                    var validation = _nameValidationService.IsNameValid(newName);
                    if (!validation.IsValid)
                        return JsonSerializer.Serialize(new { error = $"Invalid name '{newName}': {validation.ErrorMessage}" });
                }

                var module = _model.Root.GetModules()
                    .FirstOrDefault(m => m.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
                if (module == null)
                    return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });

                var oldName = module.Name;
                using var transaction = _model.StartTransaction($"Rename module '{oldName}' to '{newName}'");
                module.Name = newName;
                transaction.Commit();

                _logger.LogInformation($"Renamed module '{oldName}' to '{newName}' (all qualified references updated)");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Module renamed from '{oldName}' to '{newName}' (all qualified references updated)",
                    oldName,
                    newName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming module");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> RenameEnumerationValue(JsonObject parameters)
        {
            try
            {
                var enumerationName = parameters["enumeration_name"]?.ToString();
                var valueName = parameters["value_name"]?.ToString();
                var newName = parameters["new_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(enumerationName))
                    return JsonSerializer.Serialize(new { error = "enumeration_name is required" });
                if (string.IsNullOrEmpty(valueName))
                    return JsonSerializer.Serialize(new { error = "value_name is required" });
                if (string.IsNullOrEmpty(newName))
                    return JsonSerializer.Serialize(new { error = "new_name is required" });

                if (_nameValidationService != null)
                {
                    var validation = _nameValidationService.IsNameValid(newName);
                    if (!validation.IsValid)
                        return JsonSerializer.Serialize(new { error = $"Invalid name '{newName}': {validation.ErrorMessage}" });
                }

                // Handle qualified name (Module.EnumName)
                if (enumerationName.Contains('.') && moduleName == null)
                {
                    var parts = enumerationName.Split('.', 2);
                    moduleName = parts[0];
                    enumerationName = parts[1];
                }

                IEnumeration? foundEnum = null;
                IModule? foundModule = null;

                var modules = moduleName != null
                    ? new[] { Utils.Utils.ResolveModule(_model, moduleName) }.Where(m => m != null)
                    : _model.Root.GetModules().Where(m => !m.FromAppStore);

                foreach (var mod in modules)
                {
                    var en = _model.Root.GetModuleDocuments<IEnumeration>(mod!)
                        .FirstOrDefault(e => e.Name.Equals(enumerationName, StringComparison.OrdinalIgnoreCase));
                    if (en != null)
                    {
                        foundEnum = en;
                        foundModule = mod;
                        break;
                    }
                }

                if (foundEnum == null)
                    return JsonSerializer.Serialize(new { error = $"Enumeration '{enumerationName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                var value = foundEnum.GetValues()
                    .FirstOrDefault(v => v.Name.Equals(valueName, StringComparison.OrdinalIgnoreCase));
                if (value == null)
                    return JsonSerializer.Serialize(new { error = $"Value '{valueName}' not found in enumeration '{foundEnum.Name}'" });

                var oldValueName = value.Name;
                using var transaction = _model.StartTransaction($"Rename enumeration value '{oldValueName}' to '{newName}' in '{foundEnum.Name}'");
                value.Name = newName;
                transaction.Commit();

                _logger.LogInformation($"Renamed enumeration value '{oldValueName}' to '{newName}' in '{foundEnum.Name}'");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Enumeration value renamed from '{oldValueName}' to '{newName}' in '{foundEnum.Name}'",
                    enumeration = foundEnum.Name,
                    module = foundModule!.Name,
                    oldName = oldValueName,
                    newName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming enumeration value");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 14: Modify Existing Elements

        public async Task<string> UpdateAttribute(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var attributeName = parameters["attribute_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(entityName))
                    return JsonSerializer.Serialize(new { error = "entity_name is required" });
                if (string.IsNullOrEmpty(attributeName))
                    return JsonSerializer.Serialize(new { error = "attribute_name is required" });

                var (entity, module) = Utils.Utils.FindEntityAcrossModules(_model, entityName, moduleName);
                if (entity == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                var attribute = entity.GetAttributes()
                    .FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                if (attribute == null)
                    return JsonSerializer.Serialize(new { error = $"Attribute '{attributeName}' not found on entity '{entity.Name}'" });

                var changes = new List<string>();
                using var transaction = _model.StartTransaction($"Update attribute '{attributeName}' on '{entity.Name}'");

                // Change type if specified
                var newType = parameters["type"]?.ToString();
                if (!string.IsNullOrEmpty(newType))
                {
                    var typeLower = newType.ToLowerInvariant();
                    if (typeLower.StartsWith("enumeration:"))
                    {
                        // Reference existing enumeration
                        var enumName = newType.Substring("enumeration:".Length);
                        var enumDoc = FindEnumerationByName(enumName, moduleName);
                        if (enumDoc == null)
                            return JsonSerializer.Serialize(new { error = $"Enumeration '{enumName}' not found" });
                        var enumAttrType = _model.Create<IEnumerationAttributeType>();
                        enumAttrType.Enumeration = enumDoc.QualifiedName;
                        attribute.Type = enumAttrType;
                        changes.Add($"type → Enumeration:{enumName}");
                    }
                    else
                    {
                        var attrType = CreateAttributeType(_model, typeLower);
                        attribute.Type = attrType;
                        changes.Add($"type → {newType}");
                    }
                }

                // Set string length if specified (only applicable to String type)
                var maxLengthNode = parameters["max_length"];
                if (maxLengthNode != null)
                {
                    var maxLength = maxLengthNode.GetValue<int>();
                    if (attribute.Type is IStringAttributeType stringType)
                    {
                        stringType.Length = maxLength;
                        changes.Add($"max_length → {maxLength}");
                    }
                    else
                    {
                        return JsonSerializer.Serialize(new { error = "max_length can only be set on String attributes" });
                    }
                }

                // Set localize_date if specified (only applicable to DateTime type)
                var localizeDateNode = parameters["localize_date"];
                if (localizeDateNode != null)
                {
                    var localizeDate = localizeDateNode.GetValue<bool>();
                    if (attribute.Type is IDateTimeAttributeType dateType)
                    {
                        dateType.LocalizeDate = localizeDate;
                        changes.Add($"localize_date → {localizeDate}");
                    }
                    else
                    {
                        return JsonSerializer.Serialize(new { error = "localize_date can only be set on DateTime attributes" });
                    }
                }

                // Set default value if specified
                var defaultValue = parameters["default_value"]?.ToString();
                if (defaultValue != null)
                {
                    if (attribute.Value is IStoredValue storedValue)
                    {
                        storedValue.DefaultValue = defaultValue;
                        changes.Add($"default_value → '{defaultValue}'");
                    }
                    else
                    {
                        // Convert to stored value first
                        var newStored = _model.Create<IStoredValue>();
                        newStored.DefaultValue = defaultValue;
                        attribute.Value = newStored;
                        changes.Add($"default_value → '{defaultValue}' (converted to stored)");
                    }
                }

                if (changes.Count == 0)
                    return JsonSerializer.Serialize(new { error = "No changes specified. Provide at least one of: type, max_length, localize_date, default_value" });

                transaction.Commit();

                _logger.LogInformation($"Updated attribute '{attributeName}' on '{entity.Name}': {string.Join(", ", changes)}");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Attribute '{attributeName}' updated on entity '{entity.Name}'",
                    entity = entity.Name,
                    attribute = attributeName,
                    module = module!.Name,
                    changes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating attribute");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private IEnumeration? FindEnumerationByName(string enumName, string? moduleName = null)
        {
            // Handle qualified name
            if (enumName.Contains('.'))
            {
                var parts = enumName.Split('.', 2);
                moduleName = parts[0];
                enumName = parts[1];
            }

            var modules = moduleName != null
                ? new[] { Utils.Utils.ResolveModule(_model, moduleName) }.Where(m => m != null)
                : _model.Root.GetModules().Where(m => !m.FromAppStore);

            foreach (var mod in modules)
            {
                var en = _model.Root.GetModuleDocuments<IEnumeration>(mod!)
                    .FirstOrDefault(e => e.Name.Equals(enumName, StringComparison.OrdinalIgnoreCase));
                if (en != null) return en;
            }
            return null;
        }

        public async Task<string> UpdateAssociation(JsonObject parameters)
        {
            try
            {
                var associationName = parameters["association_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(associationName))
                    return JsonSerializer.Serialize(new { error = "association_name is required" });

                // Find association
                IAssociation? foundAssociation = null;
                IModule? foundModule = null;

                var modules = moduleName != null
                    ? new[] { Utils.Utils.ResolveModule(_model, moduleName) }.Where(m => m != null)
                    : _model.Root.GetModules().Where(m => !m.FromAppStore);

                foreach (var mod in modules)
                {
                    foreach (var entity in mod!.DomainModel.GetEntities())
                    {
                        var assoc = entity.GetAssociations(AssociationDirection.Both, null)
                            .FirstOrDefault(a => a.Association.Name.Equals(associationName, StringComparison.OrdinalIgnoreCase));
                        if (assoc != null)
                        {
                            foundAssociation = assoc.Association;
                            foundModule = mod;
                            break;
                        }
                    }
                    if (foundAssociation != null) break;
                }

                if (foundAssociation == null)
                    return JsonSerializer.Serialize(new { error = $"Association '{associationName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                var changes = new List<string>();
                using var transaction = _model.StartTransaction($"Update association '{associationName}'");

                // Change owner
                var ownerStr = parameters["owner"]?.ToString()?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(ownerStr))
                {
                    var owner = ownerStr switch
                    {
                        "default" or "parent" or "one" => AssociationOwner.Default,
                        "both" => AssociationOwner.Both,
                        _ => (AssociationOwner?)null
                    };
                    if (owner == null)
                        return JsonSerializer.Serialize(new { error = $"Invalid owner '{ownerStr}'. Use 'default' (one owner) or 'both'." });
                    foundAssociation.Owner = owner.Value;
                    changes.Add($"owner → {ownerStr}");
                }

                // Change type
                var typeStr = parameters["type"]?.ToString()?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(typeStr))
                {
                    var assocType = typeStr switch
                    {
                        "reference" or "one-to-many" or "1:n" => AssociationType.Reference,
                        "referenceset" or "reference_set" or "many-to-many" or "n:m" => AssociationType.ReferenceSet,
                        _ => (AssociationType?)null
                    };
                    if (assocType == null)
                        return JsonSerializer.Serialize(new { error = $"Invalid type '{typeStr}'. Use 'reference' or 'referenceset'." });
                    foundAssociation.Type = assocType.Value;
                    changes.Add($"type → {typeStr}");
                }

                // Change delete behaviors
                var parentDeleteBehavior = parameters["parent_delete_behavior"]?.ToString();
                if (!string.IsNullOrEmpty(parentDeleteBehavior))
                {
                    foundAssociation.ParentDeleteBehavior = MapDeletingBehavior(parentDeleteBehavior);
                    changes.Add($"parent_delete_behavior → {parentDeleteBehavior}");
                }

                var childDeleteBehavior = parameters["child_delete_behavior"]?.ToString();
                if (!string.IsNullOrEmpty(childDeleteBehavior))
                {
                    foundAssociation.ChildDeleteBehavior = MapDeletingBehavior(childDeleteBehavior);
                    changes.Add($"child_delete_behavior → {childDeleteBehavior}");
                }

                if (changes.Count == 0)
                    return JsonSerializer.Serialize(new { error = "No changes specified. Provide at least one of: owner, type, parent_delete_behavior, child_delete_behavior" });

                transaction.Commit();

                _logger.LogInformation($"Updated association '{associationName}': {string.Join(", ", changes)}");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Association '{associationName}' updated",
                    association = associationName,
                    module = foundModule!.Name,
                    changes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating association");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> UpdateConstant(JsonObject parameters)
        {
            try
            {
                var constantName = parameters["constant_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(constantName))
                    return JsonSerializer.Serialize(new { error = "constant_name is required" });

                // Handle qualified name
                if (constantName.Contains('.') && moduleName == null)
                {
                    var parts = constantName.Split('.', 2);
                    moduleName = parts[0];
                    constantName = parts[1];
                }

                IConstant? foundConstant = null;
                IModule? foundModule = null;

                var modules = moduleName != null
                    ? new[] { Utils.Utils.ResolveModule(_model, moduleName) }.Where(m => m != null)
                    : _model.Root.GetModules().Where(m => !m.FromAppStore);

                foreach (var mod in modules)
                {
                    var c = _model.Root.GetModuleDocuments<IConstant>(mod!)
                        .FirstOrDefault(c => c.Name.Equals(constantName, StringComparison.OrdinalIgnoreCase));
                    if (c != null)
                    {
                        foundConstant = c;
                        foundModule = mod;
                        break;
                    }
                }

                if (foundConstant == null)
                    return JsonSerializer.Serialize(new { error = $"Constant '{constantName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                var changes = new List<string>();
                using var transaction = _model.StartTransaction($"Update constant '{constantName}'");

                // Change default value
                var defaultValue = parameters["default_value"]?.ToString();
                if (defaultValue != null)
                {
                    foundConstant.DefaultValue = defaultValue;
                    changes.Add($"default_value → '{defaultValue}'");
                }

                // Change exposed to client
                var exposedNode = parameters["exposed_to_client"];
                if (exposedNode != null)
                {
                    var exposed = exposedNode.GetValue<bool>();
                    foundConstant.ExposedToClient = exposed;
                    changes.Add($"exposed_to_client → {exposed}");
                }

                if (changes.Count == 0)
                    return JsonSerializer.Serialize(new { error = "No changes specified. Provide at least one of: default_value, exposed_to_client" });

                transaction.Commit();

                _logger.LogInformation($"Updated constant '{constantName}': {string.Join(", ", changes)}");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Constant '{constantName}' updated",
                    constant = constantName,
                    module = foundModule!.Name,
                    qualifiedName = $"{foundModule.Name}.{constantName}",
                    changes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating constant");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> UpdateEnumeration(JsonObject parameters)
        {
            try
            {
                var enumerationName = parameters["enumeration_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(enumerationName))
                    return JsonSerializer.Serialize(new { error = "enumeration_name is required" });

                // Handle qualified name
                if (enumerationName.Contains('.') && moduleName == null)
                {
                    var parts = enumerationName.Split('.', 2);
                    moduleName = parts[0];
                    enumerationName = parts[1];
                }

                var foundEnum = FindEnumerationByName(enumerationName, moduleName);
                if (foundEnum == null)
                    return JsonSerializer.Serialize(new { error = $"Enumeration '{enumerationName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                var changes = new List<string>();
                using var transaction = _model.StartTransaction($"Update enumeration '{enumerationName}'");

                // Add values
                var addValuesNode = parameters["add_values"];
                if (addValuesNode is JsonArray addArray && addArray.Count > 0)
                {
                    foreach (var item in addArray)
                    {
                        var valName = item?.ToString();
                        if (string.IsNullOrEmpty(valName)) continue;

                        // Check for duplicates
                        if (foundEnum.GetValues().Any(v => v.Name.Equals(valName, StringComparison.OrdinalIgnoreCase)))
                        {
                            changes.Add($"skipped '{valName}' (already exists)");
                            continue;
                        }

                        if (_nameValidationService != null)
                        {
                            var validation = _nameValidationService.IsNameValid(valName);
                            if (!validation.IsValid)
                                return JsonSerializer.Serialize(new { error = $"Invalid value name '{valName}': {validation.ErrorMessage}" });
                        }

                        var enumValue = _model.Create<IEnumerationValue>();
                        enumValue.Name = valName;
                        var captionText = _model.Create<IText>();
                        captionText.AddOrUpdateTranslation("en_US", valName);
                        enumValue.Caption = captionText;
                        foundEnum.AddValue(enumValue);
                        changes.Add($"added '{valName}'");
                    }
                }

                // Remove values
                var removeValuesNode = parameters["remove_values"];
                if (removeValuesNode is JsonArray removeArray && removeArray.Count > 0)
                {
                    foreach (var item in removeArray)
                    {
                        var valName = item?.ToString();
                        if (string.IsNullOrEmpty(valName)) continue;

                        var existingValue = foundEnum.GetValues()
                            .FirstOrDefault(v => v.Name.Equals(valName, StringComparison.OrdinalIgnoreCase));
                        if (existingValue == null)
                        {
                            changes.Add($"skipped removing '{valName}' (not found)");
                            continue;
                        }

                        foundEnum.RemoveValue(existingValue);
                        changes.Add($"removed '{valName}'");
                    }
                }

                if (changes.Count == 0)
                    return JsonSerializer.Serialize(new { error = "No changes specified. Provide at least one of: add_values (array), remove_values (array)" });

                transaction.Commit();

                var remainingValues = foundEnum.GetValues().Select(v => v.Name).ToList();
                _logger.LogInformation($"Updated enumeration '{enumerationName}': {string.Join(", ", changes)}");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Enumeration '{enumerationName}' updated",
                    enumeration = enumerationName,
                    changes,
                    currentValues = remainingValues,
                    valueCount = remainingValues.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating enumeration");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> SetDocumentation(JsonObject parameters)
        {
            try
            {
                var elementType = parameters["element_type"]?.ToString()?.ToLowerInvariant();
                var elementName = parameters["element_name"]?.ToString();
                var documentation = parameters["documentation"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(elementType))
                    return JsonSerializer.Serialize(new { error = "element_type is required: 'entity', 'attribute', 'association', 'domain_model'" });
                if (string.IsNullOrEmpty(elementName) && elementType != "domain_model")
                    return JsonSerializer.Serialize(new { error = "element_name is required" });
                if (documentation == null)
                    return JsonSerializer.Serialize(new { error = "documentation is required (use empty string to clear)" });

                using var transaction = _model.StartTransaction($"Set documentation on {elementType} '{elementName}'");

                switch (elementType)
                {
                    case "entity":
                    {
                        var (entity, module) = Utils.Utils.FindEntityAcrossModules(_model, elementName!, moduleName);
                        if (entity == null)
                            return JsonSerializer.Serialize(new { error = $"Entity '{elementName}' not found" });
                        entity.Documentation = documentation;
                        transaction.Commit();
                        return JsonSerializer.Serialize(new { success = true, message = $"Documentation set on entity '{entity.Name}'", elementType, elementName = entity.Name, module = module!.Name });
                    }
                    case "attribute":
                    {
                        var entityName = parameters["entity_name"]?.ToString() ?? elementName!;
                        var attrName = parameters["attribute_name"]?.ToString();
                        if (string.IsNullOrEmpty(attrName))
                        {
                            // If element_name contains ".", treat as "Entity.Attribute"
                            if (elementName!.Contains('.'))
                            {
                                var parts = elementName.Split('.', 2);
                                entityName = parts[0];
                                attrName = parts[1];
                            }
                            else
                            {
                                return JsonSerializer.Serialize(new { error = "attribute_name is required for attribute documentation (or use element_name as 'Entity.Attribute')" });
                            }
                        }
                        var (entity, module) = Utils.Utils.FindEntityAcrossModules(_model, entityName, moduleName);
                        if (entity == null)
                            return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });
                        var attr = entity.GetAttributes().FirstOrDefault(a => a.Name.Equals(attrName, StringComparison.OrdinalIgnoreCase));
                        if (attr == null)
                            return JsonSerializer.Serialize(new { error = $"Attribute '{attrName}' not found on entity '{entity.Name}'" });
                        attr.Documentation = documentation;
                        transaction.Commit();
                        return JsonSerializer.Serialize(new { success = true, message = $"Documentation set on attribute '{attrName}' of entity '{entity.Name}'", elementType, entity = entity.Name, attribute = attrName, module = module!.Name });
                    }
                    case "association":
                    {
                        IAssociation? foundAssociation = null;
                        IModule? foundModule = null;
                        var modules = moduleName != null
                            ? new[] { Utils.Utils.ResolveModule(_model, moduleName) }.Where(m => m != null)
                            : _model.Root.GetModules().Where(m => !m.FromAppStore);
                        foreach (var mod in modules)
                        {
                            foreach (var entity in mod!.DomainModel.GetEntities())
                            {
                                var assoc = entity.GetAssociations(AssociationDirection.Both, null)
                                    .FirstOrDefault(a => a.Association.Name.Equals(elementName, StringComparison.OrdinalIgnoreCase));
                                if (assoc != null)
                                {
                                    foundAssociation = assoc.Association;
                                    foundModule = mod;
                                    break;
                                }
                            }
                            if (foundAssociation != null) break;
                        }
                        if (foundAssociation == null)
                            return JsonSerializer.Serialize(new { error = $"Association '{elementName}' not found" });
                        foundAssociation.Documentation = documentation;
                        transaction.Commit();
                        return JsonSerializer.Serialize(new { success = true, message = $"Documentation set on association '{elementName}'", elementType, association = elementName, module = foundModule!.Name });
                    }
                    case "domain_model":
                    {
                        var module = Utils.Utils.ResolveModule(_model, moduleName);
                        if (module == null)
                            return JsonSerializer.Serialize(new { error = $"Module '{moduleName ?? "(default)"}' not found" });
                        module.DomainModel.Documentation = documentation;
                        transaction.Commit();
                        return JsonSerializer.Serialize(new { success = true, message = $"Documentation set on domain model of module '{module.Name}'", elementType, module = module.Name });
                    }
                    default:
                        return JsonSerializer.Serialize(new { error = $"Unknown element_type '{elementType}'. Supported: entity, attribute, association, domain_model" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting documentation");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 15: Cross-Module Association Queries

        public async Task<string> QueryAssociations(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var secondEntity = parameters["second_entity"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();
                var direction = parameters["direction"]?.ToString()?.ToLowerInvariant() ?? "both";

                // Build entity-to-module lookup and collect all associations using entity-level API
                var entityModuleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var seenAssociations = new HashSet<string>(); // Deduplicate by association name
                var allAssociations = new List<(IAssociation assoc, string parentEntity, string parentModule, string childEntity, string childModule)>();

                var modules = _model.Root.GetModules();
                foreach (var mod in modules)
                {
                    foreach (var entity in mod.DomainModel.GetEntities())
                        entityModuleMap[entity.Name] = mod.Name;
                }

                // Collect all associations via entity.GetAssociations(Both)
                foreach (var mod in modules)
                {
                    foreach (var entity in mod.DomainModel.GetEntities())
                    {
                        foreach (var ea in entity.GetAssociations(AssociationDirection.Both))
                        {
                            var assocName = ea.Association.Name;
                            if (seenAssociations.Contains(assocName)) continue;
                            seenAssociations.Add(assocName);

                            allAssociations.Add((
                                ea.Association,
                                ea.Parent?.Name ?? "",
                                entityModuleMap.GetValueOrDefault(ea.Parent?.Name ?? "", ""),
                                ea.Child?.Name ?? "",
                                entityModuleMap.GetValueOrDefault(ea.Child?.Name ?? "", "")
                            ));
                        }
                    }
                }

                // Apply filters
                IEnumerable<(IAssociation assoc, string parentEntity, string parentModule, string childEntity, string childModule)> filtered = allAssociations;

                if (!string.IsNullOrEmpty(entityName) && !string.IsNullOrEmpty(secondEntity))
                {
                    // Find associations between two specific entities
                    var (e1, _) = Utils.Utils.FindEntityAcrossModules(_model, entityName, moduleName);
                    if (e1 == null)
                        return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });
                    var (e2, _) = Utils.Utils.FindEntityAcrossModules(_model, secondEntity, null);
                    if (e2 == null)
                        return JsonSerializer.Serialize(new { error = $"Entity '{secondEntity}' not found" });

                    filtered = filtered.Where(a =>
                        (a.parentEntity.Equals(entityName, StringComparison.OrdinalIgnoreCase) && a.childEntity.Equals(secondEntity, StringComparison.OrdinalIgnoreCase)) ||
                        (a.parentEntity.Equals(secondEntity, StringComparison.OrdinalIgnoreCase) && a.childEntity.Equals(entityName, StringComparison.OrdinalIgnoreCase)));
                }
                else if (!string.IsNullOrEmpty(entityName))
                {
                    // Find associations of a specific entity
                    var (e, _) = Utils.Utils.FindEntityAcrossModules(_model, entityName, moduleName);
                    if (e == null)
                        return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });

                    filtered = direction switch
                    {
                        "parent" => filtered.Where(a => a.parentEntity.Equals(entityName, StringComparison.OrdinalIgnoreCase)),
                        "child" => filtered.Where(a => a.childEntity.Equals(entityName, StringComparison.OrdinalIgnoreCase)),
                        _ => filtered.Where(a =>
                            a.parentEntity.Equals(entityName, StringComparison.OrdinalIgnoreCase) ||
                            a.childEntity.Equals(entityName, StringComparison.OrdinalIgnoreCase))
                    };
                }
                else if (!string.IsNullOrEmpty(moduleName))
                {
                    // Filter by module
                    var module = modules.FirstOrDefault(m => m.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
                    if (module == null)
                        return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });

                    filtered = filtered.Where(a =>
                        a.parentModule.Equals(moduleName, StringComparison.OrdinalIgnoreCase) ||
                        a.childModule.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
                }

                var associations = filtered.Select(a => new
                {
                    name = a.assoc.Name,
                    parent = a.parentEntity,
                    parentModule = a.parentModule,
                    child = a.childEntity,
                    childModule = a.childModule,
                    type = a.assoc.Type.ToString(),
                    owner = a.assoc.Owner.ToString(),
                    parentDeleteBehavior = FormatDeletingBehavior(a.assoc.ParentDeleteBehavior),
                    childDeleteBehavior = FormatDeletingBehavior(a.assoc.ChildDeleteBehavior)
                }).ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    count = associations.Count,
                    query = new
                    {
                        entityName,
                        secondEntity,
                        moduleName,
                        direction
                    },
                    associations
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying associations");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #endregion
    }

    public class Association
    {
        public string Name { get; set; }
        public string Parent { get; set; }
        public string Child { get; set; }
        public string Type { get; set; }
        public string ParentDeleteBehavior { get; set; }
        public string ChildDeleteBehavior { get; set; }
        public string Owner { get; set; }
    }
}
