using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using MCPExtension.Core;
using System.Linq;
using System.Collections.Generic;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Model.Enumerations;
using System.Text.Json;
using System;
using System.IO;
using System.Text;

namespace MCPExtension.Handlers
{
    public class ReadModelHandler : BaseApiHandler
    {
        // Standardize path for MCP compatibility
        public override string Path => "/api/model/read";
        public override string Method => "POST"; // Explicitly set to POST

        public ReadModelHandler(IModel currentApp) : base(currentApp) { }

        public override async Task HandleAsync(HttpContext context)
        {
            try
            {
                // Log the request details to help debugging
                System.Diagnostics.Debug.WriteLine($"Received {context.Request.Method} request to {context.Request.Path}");

                if (context.Request.Method != "POST")
                {
                    context.Response.StatusCode = 405;
                    await WriteJsonResponse(context, new { 
                        success = false, 
                        message = "Method not allowed. Use POST." 
                    });
                    return;
                }

                await ExecuteInTransactionAsync(
                    context,
                    "read domain model",
                    async (model) =>
                    {
                        var module = Utils.Utils.GetMyFirstModule(model);
                        if (module?.DomainModel == null)
                        {
                            return (
                                success: false,
                                message: "No domain model found.",
                                data: null as object
                            );
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

                        return (
                            success: true,
                            message: "Model retrieved successfully",
                            data: modelData as object
                        );
                    });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in ReadModelHandler: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                context.Response.StatusCode = 500;
                await WriteJsonResponse(context, new {
                    success = false,
                    message = $"Error reading domain model: {ex.Message}",
                    error = "InternalServerError"
                });
            }
        }

        private async Task WriteJsonResponse(HttpContext context, object value)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(value, options));
        }

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

                var associationModel = new Association
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

    }

    public class Association
    {
        public string Name { get; set; }
        public string Parent { get; set; }
        public string Child { get; set; }
        public string Type { get; set; }
    }

}
