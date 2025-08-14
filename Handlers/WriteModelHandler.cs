using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using MCPExtension.Core;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MCPExtension.Utils;
using System;
using Mendix.StudioPro.ExtensionsAPI.Model.Enumerations;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Model.Texts;
using MCPExtension.Tools;
using Microsoft.Extensions.Logging;

namespace MCPExtension.Handlers.Schema
{
    public class DomainModelSchema
    {
        public List<EntitySchema> Entities { get; set; } = new List<EntitySchema>();
        public List<AssociationSchema> Associations { get; set; } = new List<AssociationSchema>();
    }

    public class EntitySchema
    {
        public string Name { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public List<AttributeSchema> Attributes { get; set; } = new List<AttributeSchema>();
    }

    public class AttributeSchema
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public List<string>? EnumerationValues { get; set; }
    }

    public class AssociationSchema
    {
        public string Name { get; set; } = string.Empty;
        public string Parent { get; set; } = string.Empty;
        public string Child { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    public class Association
    {
        public string Name { get; set; }
        public string Parent { get; set; }
        public string Child { get; set; }
        public string Type { get; set; }
    }
}

namespace MCPExtension.Handlers
{
    public class WriteModelHandler : BaseApiHandler
    {
        private static readonly HashSet<string> UsedNames = new HashSet<string>();

        // Standardize path for MCP compatibility
        public override string Path => "/api/model/write";
        public override string Method => "POST";

        public WriteModelHandler(IModel currentApp) : base(currentApp) { }

        private string StripModuleName(string entityName)
        {
            return entityName.Contains(".") ? entityName.Split('.').Last() : entityName;
        }

        private AssociationType MapAssociationType(string type)
        {
            System.Diagnostics.Debug.WriteLine($"Mapping association type: '{type}'");
            
            if (string.IsNullOrEmpty(type))
            {
                System.Diagnostics.Debug.WriteLine("WARNING: Association type is null or empty, defaulting to Reference");
                return AssociationType.Reference;
            }
            
            var normalizedType = type.ToLowerInvariant().Trim();
            System.Diagnostics.Debug.WriteLine($"Normalized type: '{normalizedType}'");
            
            switch (normalizedType)
            {
                case "one-to-many":
                case "reference":
                    return AssociationType.Reference;
                case "many-to-many":
                case "referenceset":  // FIXED: ReferenceSet should create many-to-many
                    return AssociationType.ReferenceSet;
                default:
                    System.Diagnostics.Debug.WriteLine($"WARNING: Unknown association type '{normalizedType}', defaulting to Reference");
                    return AssociationType.Reference;
            }
        }

        public override async Task HandleAsync(HttpContext context)
        {
            try
            {
                // Set CORS headers safely
                context.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
                context.Response.Headers.TryAdd("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
                context.Response.Headers.TryAdd("Access-Control-Allow-Headers", "Content-Type");

                // Handle OPTIONS requests
                if (context.Request.Method == "OPTIONS")
                {
                    context.Response.StatusCode = 200;
                    return;
                }

                // Check if method is allowed
                if (context.Request.Method != Method)
                {
                    context.Response.StatusCode = 405;
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        message = $"Method not allowed. Use {Method}."
                    }, JsonOptions));
                    return;
                }

                // Deserialize request body
                var schema = await DeserializeRequestBodyAsync<Schema.DomainModelSchema>(context);
                if (schema == null)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        message = "Failed to deserialize the request body or request body is empty"
                    }, JsonOptions));
                    return;
                }

                if ((schema.Entities == null || !schema.Entities.Any()) &&
                    (schema.Associations == null || !schema.Associations.Any()))
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        message = "No entities or associations provided in the schema"
                    }, JsonOptions));
                    return;
                }

                // Execute the model operations inside an explicit transaction
                using (var transaction = CurrentApp.StartTransaction("write domain model"))
                {
                    try
                    {
                        var schemaJson = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });

                        // Validate duplicates with stripped names
                        var duplicatedEntityNames = schema.Entities
                            .GroupBy(e => StripModuleName(e.Name))
                            .Where(g => g.Count() > 1)
                            .Select(g => g.Key)
                            .ToList();

                        if (duplicatedEntityNames.Any())
                        {
                            throw new InvalidOperationException(
                                $"Duplicated entity names detected: {string.Join(", ", duplicatedEntityNames)}");
                        }

                        var duplicatedAssociationNames = schema.Associations?
                            .GroupBy(a => StripModuleName(a.Name))
                            .Where(g => g.Count() > 1)
                            .Select(g => g.Key)
                            .ToList() ?? new List<string>();

                        if (duplicatedAssociationNames.Any())
                        {
                            throw new InvalidOperationException(
                                $"Duplicated association names detected: {string.Join(", ", duplicatedAssociationNames)}");
                        }

                        var module = Utils.Utils.GetMyFirstModule(CurrentApp);
                        if (module?.DomainModel == null)
                        {
                            throw new InvalidOperationException("No domain model found.");
                        }

                        var entities = new Dictionary<string, IEntity>();

                        // Create or update entities and attributes
                        foreach (var entity in schema.Entities)
                        {
                            IEntity mxEntity;
                            var entityName = StripModuleName(entity.Name);

                            // Check if entity already exists
                            var existingEntity = module.DomainModel.GetEntities()
                                .FirstOrDefault(e => e.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase));

                            if (existingEntity != null)
                            {
                                mxEntity = existingEntity;
                            }
                            else
                            {
                                // Create entity with proper entity type
                                var entityType = entity.EntityType ?? "persistent";
                                mxEntity = CreateEntityWithType(CurrentApp, entityName, entityType, entity.Attributes, module);
                                if (mxEntity != null)
                                {
                                    module.DomainModel.AddEntity(mxEntity);
                                }
                                else
                                {
                                    throw new InvalidOperationException($"Failed to create entity {entityName} of type {entityType}");
                                }
                            }

                            entities[entityName] = mxEntity;

                            // Note: Attributes are already handled by MendixDomainModelTools.HandleCreateEntityAsync
                            // No need to add attributes manually here
                        }

                        // Simple positioning for entities (no complex arrangement)
                        PositionEntities(module.DomainModel.GetEntities().ToList());

                        // Create associations
                        if (schema.Associations != null)
                        {
                            foreach (var association in schema.Associations)
                            {
                                var parentName = StripModuleName(association.Parent);
                                var childName = StripModuleName(association.Child);
                                // Always generate the association name from parent and child
                                var associationName = $"{parentName}_{childName}";
                                
                                System.Diagnostics.Debug.WriteLine($"Creating association '{associationName}' between parent '{parentName}' and child '{childName}'");

                                // Look up entities in both new and existing entities
                                var parentEntity = entities.ContainsKey(parentName)
                                    ? entities[parentName]
                                    : module.DomainModel.GetEntities()
                                        .FirstOrDefault(e => e.Name.Equals(parentName, StringComparison.OrdinalIgnoreCase));

                                var childEntity = entities.ContainsKey(childName)
                                    ? entities[childName]
                                    : module.DomainModel.GetEntities()
                                        .FirstOrDefault(e => e.Name.Equals(childName, StringComparison.OrdinalIgnoreCase));

                                if (parentEntity == null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"ERROR: Parent entity '{parentName}' not found");
                                    System.Diagnostics.Debug.WriteLine($"Available entities: {string.Join(", ", module.DomainModel.GetEntities().Select(e => e.Name))}");
                                    throw new InvalidOperationException(
                                        $"Invalid association {associationName}: Parent entity '{parentName}' not found. Create it before creating the association.");
                                }

                                if (childEntity == null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"ERROR: Child entity '{childName}' not found");
                                    System.Diagnostics.Debug.WriteLine($"Available entities: {string.Join(", ", module.DomainModel.GetEntities().Select(e => e.Name))}");
                                    throw new InvalidOperationException(
                                        $"Invalid association {associationName}: Child entity '{childName}' not found. Create it before creating the association.");
                                }

                                try
                                {
                                    System.Diagnostics.Debug.WriteLine($"Adding association from {parentName} to {childName}");
                                    var mxAssociation = parentEntity.AddAssociation(childEntity);
                                    mxAssociation.Name = associationName;
                                    mxAssociation.Type = MapAssociationType(association.Type);
                                    System.Diagnostics.Debug.WriteLine($"Successfully created association: {associationName} from {parentName} to {childName} of type {mxAssociation.Type}");
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error creating association {associationName}: {ex.Message}");
                                    System.Diagnostics.Debug.WriteLine($"Exception details: {ex}");
                                    throw new InvalidOperationException($"Failed to create association {associationName}: {ex.Message}");
                                }
                            }
                        }

                        // Commit the transaction
                        transaction.Commit();

                        // Return updated model data
                        var domainModel = module.DomainModel;
                        var updatedEntities = domainModel.GetEntities().ToList();

                        var modelData = updatedEntities.Select(entity => new
                        {
                            EntityName = $"{module.Name}.{entity.Name}",
                            Attributes = GetEntityAttributes(entity),
                            Associations = GetEntityAssociations(entity, module)
                        }).ToList();

                        context.Response.StatusCode = 200;
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new
                        {
                            success = true,
                            message = "Model created successfully",
                            data = modelData
                        }, JsonOptions));
                    }
                    catch (Exception ex)
                    {
                        // If any error occurs, rollback the transaction
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    message = $"Error creating model: {ex.Message}",
                    data = (object)null
                }, JsonOptions));
            }
        }

        private Dictionary<string, string> GetEntityAttributes(IEntity entity)
        {
            return entity.GetAttributes()
                .Where(attr => attr != null)
                .ToDictionary(
                    attr => attr.Name,
                    attr => attr.Type?.GetType().Name ?? "Unknown"
                );
        }

        private List<Schema.Association> GetEntityAssociations(IEntity entity, IModule module)
        {
            var entityAssociations = new List<Schema.Association>();
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

                var associationModel = new Schema.Association
                {
                    Name = association.Association.Name,
                    Parent = association.Parent.Name,
                    Child = association.Child.Name,
                    Type = mappedType
                };

                entityAssociations.Add(associationModel);
            }

            return entityAssociations;
        }

        private IEntity CreateEntity(IModel model, string name)
        {
            var mxEntity = model.Create<IEntity>();
            mxEntity.Name = GetUniqueName(name);
            return mxEntity;
        }

        private IEntity? CreateEntityWithType(IModel model, string name, string entityType, List<MCPExtension.Handlers.Schema.AttributeSchema>? attributes, IModule module)
        {
            IEntity? entity = null;
            
            // Create entity based on type
            switch (entityType.ToLower())
            {
                case "non-persistent":
                    entity = CreateNonPersistentEntity(model, name);
                    break;
                case "filedocument":
                    entity = CreateFileDocumentEntity(model, name);
                    break;
                case "image":
                    entity = CreateImageEntity(model, name);
                    break;
                case "storecreateddate":
                    entity = CreateStoreCreatedDateEntity(model, name);
                    break;
                case "storechangedate":
                    entity = CreateStoreChangeDateEntity(model, name);
                    break;
                case "storecreatedchangedate":
                    entity = CreateStoreCreatedChangeDateEntity(model, name);
                    break;
                case "storeowner":
                    entity = CreateStoreOwnerEntity(model, name);
                    break;
                case "storechangeby":
                    entity = CreateStoreChangeByEntity(model, name);
                    break;
                case "persistent":
                default:
                    entity = CreateEntity(model, name);
                    break;
            }

            // Add attributes if provided and entity was created
            if (entity != null && attributes != null)
            {
                foreach (var attribute in attributes)
                {
                    var mxAttribute = model.Create<IAttribute>();
                    mxAttribute.Name = attribute.Name;
                    
                    if (attribute.Type.Equals("Enumeration", StringComparison.OrdinalIgnoreCase))
                    {
                        var enumTypeInstance = GetEnumerationTypeInstance(model, attribute, module);
                        mxAttribute.Type = enumTypeInstance;
                    }
                    else
                    {
                        var attributeType = GetAttributeType(model, attribute.Type);
                        mxAttribute.Type = attributeType;
                    }
                    
                    entity.AddAttribute(mxAttribute);
                }
            }

            return entity;
        }

        private IEntity? CreateNonPersistentEntity(IModel model, string name)
        {
            var entity = model.Create<IEntity>();
            entity.Name = GetUniqueName(name);
            // Note: Set persistability through the domain model settings
            // entity.IsPersistable = false;  // This property might not be available directly
            return entity;
        }

        private IEntity? CreateFileDocumentEntity(IModel model, string name)
        {
            // For FileDocument entities, we need to inherit from the System.FileDocument template
            var entity = model.Create<IEntity>();
            entity.Name = GetUniqueName(name);
            // Note: In a real implementation, this would inherit from System.FileDocument
            // For now, we'll create a basic entity and mark it appropriately
            return entity;
        }

        private IEntity? CreateImageEntity(IModel model, string name)
        {
            // For Image entities, we need to inherit from the System.Image template
            var entity = model.Create<IEntity>();
            entity.Name = GetUniqueName(name);
            // Note: In a real implementation, this would inherit from System.Image
            // For now, we'll create a basic entity and mark it appropriately
            return entity;
        }

        private IEntity? CreateStoreCreatedDateEntity(IModel model, string name)
        {
            var entity = model.Create<IEntity>();
            entity.Name = GetUniqueName(name);
            // Add automatic CreatedDate attribute
            var createdDateAttr = model.Create<IAttribute>();
            createdDateAttr.Name = "CreatedDate";
            createdDateAttr.Type = model.Create<IDateTimeAttributeType>();
            entity.AddAttribute(createdDateAttr);
            return entity;
        }

        private IEntity? CreateStoreChangeDateEntity(IModel model, string name)
        {
            var entity = model.Create<IEntity>();
            entity.Name = GetUniqueName(name);
            // Add automatic ChangedDate attribute
            var changedDateAttr = model.Create<IAttribute>();
            changedDateAttr.Name = "ChangedDate";
            changedDateAttr.Type = model.Create<IDateTimeAttributeType>();
            entity.AddAttribute(changedDateAttr);
            return entity;
        }

        private IEntity? CreateStoreCreatedChangeDateEntity(IModel model, string name)
        {
            var entity = model.Create<IEntity>();
            entity.Name = GetUniqueName(name);
            // Add both CreatedDate and ChangedDate attributes
            var createdDateAttr = model.Create<IAttribute>();
            createdDateAttr.Name = "CreatedDate";
            createdDateAttr.Type = model.Create<IDateTimeAttributeType>();
            entity.AddAttribute(createdDateAttr);
            
            var changedDateAttr = model.Create<IAttribute>();
            changedDateAttr.Name = "ChangedDate";
            changedDateAttr.Type = model.Create<IDateTimeAttributeType>();
            entity.AddAttribute(changedDateAttr);
            return entity;
        }

        private IEntity? CreateStoreOwnerEntity(IModel model, string name)
        {
            var entity = model.Create<IEntity>();
            entity.Name = GetUniqueName(name);
            // Add automatic Owner attribute (would be reference to System.User in real implementation)
            var ownerAttr = model.Create<IAttribute>();
            ownerAttr.Name = "Owner";
            ownerAttr.Type = model.Create<IStringAttributeType>();
            entity.AddAttribute(ownerAttr);
            return entity;
        }

        private IEntity? CreateStoreChangeByEntity(IModel model, string name)
        {
            var entity = model.Create<IEntity>();
            entity.Name = GetUniqueName(name);
            // Add automatic ChangeBy attribute (would be reference to System.User in real implementation)
            var changeByAttr = model.Create<IAttribute>();
            changeByAttr.Name = "ChangeBy";
            changeByAttr.Type = model.Create<IStringAttributeType>();
            entity.AddAttribute(changeByAttr);
            return entity;
        }

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

        private void PositionEntities(List<IEntity> entities)
        {
            const int EntityWidth = 150;
            const int EntityHeight = 75;
            const int SpacingX = 200;
            const int SpacingY = 150;
            const int StartX = 20;
            const int StartY = 20;
            const int MaxColumns = 5;

            for (int i = 0; i < entities.Count; i++)
            {
                int column = i % MaxColumns;
                int row = i / MaxColumns;
                
                int x = StartX + (column * SpacingX);
                int y = StartY + (row * SpacingY);
                
                entities[i].Location = new Location(x, y);
            }
        }

        private IAttributeType GetAttributeType(IModel model, string attributeType)
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
                    // Default to string for unknown types
                    return model.Create<IStringAttributeType>();
            }
        }

        private IEnumerationAttributeType GetEnumerationTypeInstance(IModel model, Schema.AttributeSchema attribute, IModule module)
        {
            if (attribute.EnumerationValues == null || !attribute.EnumerationValues.Any())
            {
                throw new InvalidOperationException($"Enumeration attribute {attribute.Name} must have values defined.");
            }

            var attributeEnum = model.Create<IEnumerationAttributeType>();
            var enumDoc = model.Create<IEnumeration>();
            enumDoc.Name = GetUniqueName(attribute.Name + "Enum");

            foreach (var value in attribute.EnumerationValues)
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

        private async Task<T?> DeserializeRequestBodyAsync<T>(HttpContext context) where T : class
        {
            string requestBody;
            using (var reader = new StreamReader(context.Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            System.Diagnostics.Debug.WriteLine($"Received JSON: {requestBody}");

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                return null;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<T>(requestBody, options);
        }
    }
}
