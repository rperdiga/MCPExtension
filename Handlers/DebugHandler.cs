using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels; // Add this for AssociationDirection
using MCPExtension.Core;

namespace MCPExtension.Handlers
{
    public class DebugHandler : BaseApiHandler
    {
        // Standardize path for MCP compatibility
        public override string Path => "/api/debug";
        public override string Method => "POST";

        public DebugHandler(IModel currentApp) : base(currentApp) { }

        public override async Task HandleAsync(HttpContext context)
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

            // Read the request body
            string requestBody;
            using (var reader = new StreamReader(context.Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            System.Diagnostics.Debug.WriteLine($"Debug API request: {requestBody}");

            // Get module and entity information
            var module = Utils.Utils.GetMyFirstModule(CurrentApp);
            var response = new Dictionary<string, object>();

            if (module?.DomainModel != null)
            {
                var entities = module.DomainModel.GetEntities().ToList();
                response["module"] = module.Name;
                response["entityCount"] = entities.Count;
                response["entities"] = entities.Select(e => new
                {
                    Name = e.Name,
                    QualifiedName = $"{module.Name}.{e.Name}",
                    AttributeCount = e.GetAttributes().Count(),
                    Attributes = e.GetAttributes().Select(a => a.Name).ToList(),
                    LocationX = e.Location != null ? e.Location.X : 0,
                    LocationY = e.Location != null ? e.Location.Y : 0
                }).ToList();

                // Collect association information with more details
                var allAssociations = new List<object>();
                foreach (var entity in entities)
                {
                    var associations = entity.GetAssociations(AssociationDirection.Both, null).ToList();
                    foreach (var association in associations)
                    {
                        allAssociations.Add(new
                        {
                            Name = association.Association.Name,
                            Parent = association.Parent.Name,
                            ParentQualifiedName = $"{module.Name}.{association.Parent.Name}",
                            Child = association.Child.Name,
                            ChildQualifiedName = $"{module.Name}.{association.Child.Name}",
                            Type = association.Association.Type.ToString(),
                            MappedType = association.Association.Type == AssociationType.Reference ? "one-to-many" : "many-to-many"
                        });
                    }
                }
                response["associations"] = allAssociations;
                response["associationCount"] = allAssociations.Count;
                
                // Add troubleshooting section
                response["troubleshooting"] = new
                {
                    entityNamesList = entities.Select(e => e.Name).ToList(),
                    associationTips = new[] {
                        "Make sure both entities exist before creating an association",
                        "Use simple names without module prefixes in API calls",
                        "Check that association names are unique"
                    }
                };
            }
            else
            {
                response["error"] = "No domain model found";
            }

            // Add examples for entity and association creation
            response["examples"] = new
            {
                entityCreation = new
                {
                    simple = new
                    {
                        entity_name = "Customer",
                        attributes = new[]
                        {
                            new { name = "firstName", type = "String" },
                            new { name = "lastName", type = "String" },
                            new { name = "birthDate", type = "DateTime" },
                            new { name = "isActive", type = "Boolean" }
                        }
                    },
                    withEnumeration = new
                    {
                        entity_name = "Product",
                        attributes = new object[]
                        {
                            new { name = "productName", type = "String" },
                            new { name = "price", type = "Decimal" },
                            new
                            {
                                name = "status",
                                type = "Enumeration",
                                enumerationValues = new[] { "Available", "OutOfStock", "Discontinued" }
                            }
                        }
                    }
                },
                associationCreation = new
                {
                    oneToMany = new
                    {
                        name = "Customer_Orders",
                        parent = "Customer",
                        child = "Order",
                        type = "one-to-many"
                    },
                    manyToMany = new
                    {
                        name = "Product_Category",
                        parent = "Product",
                        child = "Category",
                        type = "many-to-many"
                    }
                }
            };

            // Return response
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                success = true,
                message = "Debug information retrieved successfully",
                data = response
            }, options));
        }
    }
}
