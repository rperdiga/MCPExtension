using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using Mendix.StudioPro.ExtensionsAPI.Model.Enumerations;
using Mendix.StudioPro.ExtensionsAPI.Model.Texts;
using Microsoft.Extensions.Logging;
using MCPExtension.Utils;

namespace MCPExtension.Tools
{
    public class MendixDomainModelTools
    {
        private readonly IModel _model;
        private readonly ILogger<MendixDomainModelTools> _logger;

        public MendixDomainModelTools(IModel model, ILogger<MendixDomainModelTools> logger)
        {
            _model = model;
            _logger = logger;
        }

        public async Task<string> ReadDomainModel(JsonObject parameters)
        {
            try
            {
                var module = Utils.Utils.GetMyFirstModule(_model);
                if (module == null)
                {
                    return JsonSerializer.Serialize(new { error = "Module not found" });
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
                        Attributes = GetEntityAttributes(entity),
                        Associations = GetEntityAssociations(entity, module)
                    }).ToList()
                };

                var result = new
                {
                    success = true,
                    message = "Model retrieved successfully",
                    data = modelData,
                    status = "success"
                };

                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
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

                    if (string.IsNullOrEmpty(entityName))
                    {
                        return JsonSerializer.Serialize(new { error = "Entity name is required" });
                    }

                    var module = Utils.Utils.GetMyFirstModule(_model);
                    if (module?.DomainModel == null)
                    {
                        return JsonSerializer.Serialize(new { error = "No domain model found" });
                    }

                    // Check if entity already exists
                    var existingEntity = module.DomainModel.GetEntities()
                        .FirstOrDefault(e => e.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase));

                    if (existingEntity != null)
                    {
                        return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' already exists" });
                    }

                    // Create entity
                    var mxEntity = _model.Create<IEntity>();
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
                                    return JsonSerializer.Serialize(new { error = $"Enumeration attribute '{attrName}' must have values defined" });
                                }
                            }
                            else
                            {
                                var attributeType = CreateAttributeType(_model, attrType);
                                mxAttribute.Type = attributeType;
                            }

                            mxEntity.AddAttribute(mxAttribute);
                        }
                    }

                    // Position entity
                    PositionEntity(mxEntity, module.DomainModel.GetEntities().Count());

                    transaction.Commit();

                    return JsonSerializer.Serialize(new 
                    { 
                        success = true, 
                        message = $"Entity '{entityName}' created successfully",
                        entity = new
                        {
                            name = mxEntity.Name,
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

                    var module = Utils.Utils.GetMyFirstModule(_model);
                    if (module?.DomainModel == null)
                    {
                        return JsonSerializer.Serialize(new { error = "No domain model found" });
                    }

                    // Find parent and child entities
                    var parentEntity = module.DomainModel.GetEntities()
                        .FirstOrDefault(e => e.Name.Equals(parent, StringComparison.OrdinalIgnoreCase));
                    var childEntity = module.DomainModel.GetEntities()
                        .FirstOrDefault(e => e.Name.Equals(child, StringComparison.OrdinalIgnoreCase));

                    if (parentEntity == null)
                    {
                        return JsonSerializer.Serialize(new { error = $"Parent entity '{parent}' not found" });
                    }

                    if (childEntity == null)
                    {
                        return JsonSerializer.Serialize(new { error = $"Child entity '{child}' not found" });
                    }

                    // Create association - FIXED: For "1 Customer has many Orders", 
                    // we need to call childEntity.AddAssociation(parentEntity) because in Mendix:
                    // - entity.AddAssociation(otherEntity) means "entity references otherEntity"
                    // - For one-to-many, the "many" side should reference the "one" side
                    // So Order (child/many) should reference Customer (parent/one)
                    var mxAssociation = childEntity.AddAssociation(parentEntity);
                    mxAssociation.Name = name;
                    mxAssociation.Type = MapAssociationType(type);

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
                            type = mxAssociation.Type.ToString()
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

                    if (entitiesArray == null)
                    {
                        return JsonSerializer.Serialize(new { error = "Entities array is required" });
                    }

                    var module = Utils.Utils.GetMyFirstModule(_model);
                    if (module?.DomainModel == null)
                    {
                        return JsonSerializer.Serialize(new { error = "No domain model found" });
                    }

                    var createdEntities = new List<object>();

                    foreach (var entityNode in entitiesArray)
                    {
                        var entityObj = entityNode?.AsObject();
                        if (entityObj == null) continue;

                        var entityName = entityObj["entity_name"]?.ToString();
                        var attributesArray = entityObj["attributes"]?.AsArray();

                        if (string.IsNullOrEmpty(entityName)) continue;

                        // Check if entity already exists
                        var existingEntity = module.DomainModel.GetEntities()
                            .FirstOrDefault(e => e.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase));

                        if (existingEntity != null)
                        {
                            continue; // Skip existing entities
                        }

                        // Create entity
                        var mxEntity = _model.Create<IEntity>();
                        mxEntity.Name = entityName;
                        module.DomainModel.AddEntity(mxEntity);

                        // Add attributes if provided
                        var entityAttributes = new List<object>();
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

                                mxEntity.AddAttribute(mxAttribute);
                                entityAttributes.Add(new { name = attrName, type = attrType });
                            }
                        }

                        // Position entity
                        PositionEntity(mxEntity, module.DomainModel.GetEntities().Count());

                        createdEntities.Add(new 
                        { 
                            name = entityName, 
                            attributes = entityAttributes 
                        });
                    }

                    transaction.Commit();

                    return JsonSerializer.Serialize(new 
                    { 
                        success = true, 
                        message = $"Successfully created {createdEntities.Count} entities",
                        entities = createdEntities
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

                    var module = Utils.Utils.GetMyFirstModule(_model);
                    if (module?.DomainModel == null)
                    {
                        return JsonSerializer.Serialize(new { error = "No domain model found" });
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

                        // Find parent and child entities
                        var parentEntity = module.DomainModel.GetEntities()
                            .FirstOrDefault(e => e.Name.Equals(parent, StringComparison.OrdinalIgnoreCase));
                        var childEntity = module.DomainModel.GetEntities()
                            .FirstOrDefault(e => e.Name.Equals(child, StringComparison.OrdinalIgnoreCase));

                        if (parentEntity == null || childEntity == null)
                        {
                            continue; // Skip if entities don't exist
                        }

                        // Create association - FIXED: Use child.AddAssociation(parent) for correct direction
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

                    var module = Utils.Utils.GetMyFirstModule(_model);
                    if (module?.DomainModel == null)
                    {
                        return JsonSerializer.Serialize(new { error = "No domain model found" });
                    }

                    var entitiesArray = schema["entities"]?.AsArray();
                    var associationsArray = schema["associations"]?.AsArray();

                    var createdEntities = new List<object>();
                    var createdAssociations = new List<object>();

                    // Create entities first
                    if (entitiesArray != null)
                    {
                        foreach (var entityNode in entitiesArray)
                        {
                            var entityObj = entityNode?.AsObject();
                            if (entityObj == null) continue;

                            var entityName = entityObj["entity_name"]?.ToString();
                            var attributesArray = entityObj["attributes"]?.AsArray();

                            if (string.IsNullOrEmpty(entityName)) continue;

                            // Check if entity already exists
                            var existingEntity = module.DomainModel.GetEntities()
                                .FirstOrDefault(e => e.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase));

                            if (existingEntity != null)
                            {
                                continue; // Skip existing entities
                            }

                            // Create entity
                            var mxEntity = _model.Create<IEntity>();
                            mxEntity.Name = entityName;
                            module.DomainModel.AddEntity(mxEntity);

                            // Add attributes if provided
                            var entityAttributes = new List<object>();
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

                                    mxEntity.AddAttribute(mxAttribute);
                                    entityAttributes.Add(new { name = attrName, type = attrType });
                                }
                            }

                            // Position entity
                            PositionEntity(mxEntity, module.DomainModel.GetEntities().Count());

                            createdEntities.Add(new 
                            { 
                                name = entityName, 
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

                            // Find parent and child entities
                            var parentEntity = module.DomainModel.GetEntities()
                                .FirstOrDefault(e => e.Name.Equals(parent, StringComparison.OrdinalIgnoreCase));
                            var childEntity = module.DomainModel.GetEntities()
                                .FirstOrDefault(e => e.Name.Equals(child, StringComparison.OrdinalIgnoreCase));

                            if (parentEntity == null || childEntity == null)
                            {
                                continue; // Skip if entities don't exist
                            }

                            // Create association - FIXED: Use child.AddAssociation(parent) for correct direction
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

                    return JsonSerializer.Serialize(new 
                    { 
                        success = true, 
                        message = $"Successfully created domain model with {createdEntities.Count} entities and {createdAssociations.Count} associations",
                        entities = createdEntities,
                        associations = createdAssociations
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
                var entityName = parameters["entity_name"]?.ToString();
                var attributeName = parameters["attribute_name"]?.ToString();
                var associationName = parameters["association_name"]?.ToString();

                if (string.IsNullOrEmpty(elementType) || string.IsNullOrEmpty(entityName))
                {
                    return JsonSerializer.Serialize(new { error = "Element type and entity name are required" });
                }

                var module = Utils.Utils.GetMyFirstModule(_model);
                if (module?.DomainModel == null)
                {
                    return JsonSerializer.Serialize(new { error = "No domain model found" });
                }

                switch (elementType.ToLower())
                {
                    case "entity":
                        return DeleteEntity(module.DomainModel, entityName);
                    
                    case "attribute":
                        if (string.IsNullOrEmpty(attributeName))
                        {
                            return JsonSerializer.Serialize(new { error = "Attribute name is required for attribute deletion" });
                        }
                        return DeleteAttribute(module.DomainModel, entityName, attributeName);
                    
                    case "association":
                        if (string.IsNullOrEmpty(associationName))
                        {
                            return JsonSerializer.Serialize(new { error = "Association name is required for association deletion" });
                        }
                        return DeleteAssociation(module.DomainModel, entityName, associationName);
                    
                    default:
                        return JsonSerializer.Serialize(new { error = $"Unknown deletion type: {elementType}" });
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
                var module = Utils.Utils.GetMyFirstModule(_model);
                if (module == null)
                {
                    return JsonSerializer.Serialize(new { error = "Module not found" });
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
                "read_domain_model",
                "create_entity",
                "create_association",
                "create_multiple_entities",
                "create_multiple_associations",
                "create_domain_model_from_schema",
                "delete_model_element",
                "diagnose_associations",
                "get_last_error",
                "list_available_tools"
            };

            return JsonSerializer.Serialize(new { tools = tools, status = "success" });
        }

        #region Helper Methods

        private Dictionary<string, string> GetEntityAttributes(IEntity entity)
        {
            return entity.GetAttributes()
                .Where(attr => attr != null)
                .ToDictionary(
                    attr => attr.Name,
                    attr => {
                        var typeName = attr.Type?.GetType().Name ?? "Unknown";
                        
                        // Remove "AttributeTypeProxy" suffix
                        typeName = typeName.Replace("AttributeTypeProxy", "");
                        
                        // Handle Enumerations specially
                        if (attr.Type is IEnumerationAttributeType enumType)
                        {
                            var enumeration = enumType.Enumeration.Resolve();
                            var enumValues = enumeration.GetValues()
                                .Select(v => v.Name)
                                .ToList();
                            return $"Enumeration ({string.Join("/", enumValues)})";
                        }
                        
                        return typeName;
                    }
                );
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
                    Type = mappedType
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
                case "string":
                    return model.Create<IStringAttributeType>();
                case "boolean":
                    return model.Create<IBooleanAttributeType>();
                case "datetime":
                    return model.Create<IDateTimeAttributeType>();
                case "autonumber":
                    return model.Create<IAutoNumberAttributeType>();
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
                case "referenceset":
                    return AssociationType.ReferenceSet;
                default:
                    return AssociationType.Reference;
            }
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

        #endregion
    }

    public class Association
    {
        public string Name { get; set; }
        public string Parent { get; set; }
        public string Child { get; set; }
        public string Type { get; set; }
    }
}
