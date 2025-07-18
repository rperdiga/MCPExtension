using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using MCPExtension.Core;

namespace MCPExtension.Handlers
{
    public class AssociationDiagnosticHandler : BaseApiHandler
    {
        // Standardize path for MCP compatibility
        public override string Path => "/api/diagnostics/association";
        public override string Method => "POST";

        public AssociationDiagnosticHandler(IModel currentApp) : base(currentApp) { }

        public override async Task HandleAsync(HttpContext context)
        {
            try
            {
                // Set CORS headers safely
                context.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
                context.Response.Headers.TryAdd("Access-Control-Allow-Methods", "POST, OPTIONS");
                context.Response.Headers.TryAdd("Access-Control-Allow-Headers", "Content-Type");

                // Handle OPTIONS requests
                if (context.Request.Method == "OPTIONS")
                {
                    context.Response.StatusCode = 200;
                    return;
                }

                var module = Utils.Utils.GetMyFirstModule(CurrentApp);
                if (module?.DomainModel == null)
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new 
                    { 
                        success = false, 
                        message = "No domain model found" 
                    }));
                    return;
                }

                var entities = module.DomainModel.GetEntities().ToList();
                var allAssociations = new List<object>();

                // Get all entity names for easier debugging
                var entityNames = entities.Select(e => e.Name).ToList();

                // Collect associations data
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

                // Add association creation guidance
                var associationGuidance = new
                {
                    CommonIssues = new[]
                    {
                        "Entities must exist before creating associations",
                        "Entity names are case sensitive",
                        "Don't use module prefixes in entity names",
                        "Association names must be unique",
                        "For one-to-many associations, parent is the 'one' side, child is the 'many' side"
                    },
                    ProperFormat = new
                    {
                        Name = "Customer_Orders",
                        Parent = "Customer",
                        Child = "Order",
                        Type = "one-to-many"
                    },
                    AvailableEntities = entityNames
                };

                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                
                var responseOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    message = "Association diagnostic information retrieved",
                    data = new 
                    {
                        Entities = entityNames,
                        EntityCount = entityNames.Count,
                        Associations = allAssociations,
                        AssociationCount = allAssociations.Count,
                        Guidance = associationGuidance
                    }
                }, responseOptions));
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    message = $"Error in association diagnostic: {ex.Message}"
                }));
            }
        }
    }
}
