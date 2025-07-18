using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using MCPExtension.Core;
using Eto.Forms;

namespace MCPExtension.Handlers
{
    public class SaveDataHandler : BaseApiHandler
    {
        // Standardize path for MCP compatibility
        public override string Path => "/api/data/save";
        public override string Method => "POST";

        public class SaveDataRequest
        {
            public Dictionary<string, JsonElement> data { get; set; }
        }

        public SaveDataHandler(IModel currentApp) : base(currentApp)
        {
        }

        public override async Task HandleAsync(HttpContext context)
        {
            await ExecuteInTransactionAsync(
                context,
                "save data",
                async (model) =>
                {
                    var request = await DeserializeRequestBodyAsync<SaveDataRequest>(context);

                    if (request?.data == null)
                    {
                        return (
                            success: false,
                            message: "Invalid request format or empty data. Ensure the request body is a JSON object with a 'data' property containing a dictionary of entity names and their respective data arrays. Example: { \"data\": { \"MyFirstModule.Customer\": [ { \"VirtualId\": \"CUST001\", \"Name\": \"John Doe\", \"EmailAddress\": \"john.doe@example.com\", \"PhoneNumber\": \"+1234567890\" } ], \"MyFirstModule.Order\": [ { \"VirtualId\": \"ORD001\", \"OrderDate\": \"2023-11-01T10:30:00Z\", \"Status\": \"Pending\", \"TotalAmount\": 99.99, \"Customer\": { \"VirtualId\": \"CUST001\" } } ] } }",
                            data: null as object
                        );
                    }

                    var validationResult = ValidateData(request.data, model);
                    if (!validationResult.IsValid)
                    {
                        return (
                            success: false,
                            message: $"{validationResult.Message} Ensure the data follows the format: {{ \"MyFirstModule.Customer\": [ {{ \"VirtualId\": \"CUST001\", \"Name\": \"John Doe\", \"EmailAddress\": \"john.doe@example.com\", \"PhoneNumber\": \"+1234567890\" }} ], \"MyFirstModule.Order\": [ {{ \"VirtualId\": \"ORD001\", \"OrderDate\": \"2023-11-01T10:30:00Z\", \"Status\": \"Pending\", \"TotalAmount\": 99.99, \"Customer\": {{ \"VirtualId\": \"CUST001\" }} }} ] }}.",
                            data: null as object
                        );
                    }

                    // Save the original data without cleaning
                    await SaveSampleData(request);

                    return (
                        success: true,
                        message: "Data validated and saved successfully",
                        data: request
                    );
                });
        }

        private (bool IsValid, string Message) ValidateData(Dictionary<string, JsonElement> data, IModel model)
        {
            try
            {
                var module = Utils.Utils.GetMyFirstModule(model);
                if (module?.DomainModel == null)
                {
                    return (false, "No domain model found.");
                }

                foreach (var entityData in data)
                {
                    // Extract entity name without module prefix
                    var entityName = entityData.Key.Split('.').Last();
                    var entity = module.DomainModel.GetEntities()
                        .FirstOrDefault(e => e.Name == entityName);

                    if (entity == null)
                    {
                        return (false, $"Entity {entityName} not found in domain model.");
                    }

                    if (entityData.Value.ValueKind != JsonValueKind.Array)
                    {
                        return (false, $"Data for entity {entityName} must be an array.");
                    }

                    var records = entityData.Value.EnumerateArray();
                    foreach (var record in records)
                    {
                        if (record.ValueKind != JsonValueKind.Object)
                        {
                            return (false, $"Each record in {entityName} must be an object.");
                        }

                        // Validate attributes exist
                        foreach (var attribute in entity.GetAttributes())
                        {
                            if (!record.TryGetProperty(attribute.Name, out var _))
                            {
                                // Just log a warning instead of failing validation
                                Console.WriteLine($"Warning: Attribute {attribute.Name} missing in {entityName}.");
                            }
                        }

                        // Validate VirtualId if relationships exist
                        var associations = entity.GetAssociations(AssociationDirection.Both, null);
                        if (associations.Any())
                        {
                            if (!record.TryGetProperty("VirtualId", out var virtualIdProp) || virtualIdProp.ValueKind != JsonValueKind.String)
                            {
                                return (false, $"Entity {entityName} requires a 'VirtualId' property for relationships. Example: {{ \"VirtualId\": \"ID001\" }}");
                            }
                        }

                        // Validate relationships
                        foreach (var association in associations)
                        {
                            var assocName = association.Association.Name;
                            if (record.TryGetProperty(assocName, out var assocProp))
                            {
                                if (assocProp.ValueKind != JsonValueKind.Object || !assocProp.TryGetProperty("VirtualId", out var assocVirtualId) || assocVirtualId.ValueKind != JsonValueKind.String)
                                {
                                    return (false, $"Association {assocName} in {entityName} must be an object with a 'VirtualId'. Example: {{ \"{assocName}\": {{ \"VirtualId\": \"ID001\" }} }}");
                                }
                            }
                        }
                    }
                }

                return (true, "Validation successful");
            }
            catch (Exception ex)
            {
                return (false, $"Validation error: {ex.Message}");
            }
        }

        private Dictionary<string, JsonElement> CleanVirtualIds(Dictionary<string, JsonElement> data)
        {
            var cleanedData = new Dictionary<string, JsonElement>();

            foreach (var entity in data)
            {
                using (var jsonDoc = JsonDocument.Parse(entity.Value.GetRawText()))
                {
                    var arrayElements = jsonDoc.RootElement.EnumerateArray()
                        .Select(element =>
                        {
                            var propertyList = element.EnumerateObject()
                                .Where(prop => prop.Name != "VirtualId")
                                .Select(prop =>
                                {
                                    if (prop.Value.ValueKind == JsonValueKind.Object)
                                    {
                                        // Clean nested objects (relationships)
                                        var nestedObj = prop.Value.EnumerateObject()
                                            .Where(p => p.Name != "VirtualId");
                                        return new { prop.Name, Value = JsonSerializer.SerializeToElement(nestedObj) };
                                    }
                                    return new { prop.Name, Value = prop.Value };
                                });

                            return JsonSerializer.SerializeToElement(
                                propertyList.ToDictionary(x => x.Name, x => x.Value)
                            );
                        });

                    cleanedData[entity.Key] = JsonSerializer.SerializeToElement(arrayElements.ToArray());
                }
            }

            return cleanedData;
        }

        private async Task SaveSampleData(object sampleData)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string executingDirectory = System.IO.Path.GetDirectoryName(assembly.Location);
                DirectoryInfo directory = new DirectoryInfo(executingDirectory);
                string targetDirectory = directory?.Parent?.Parent?.Parent?.FullName 
                    ?? throw new InvalidOperationException("Could not determine target directory");

                string filePath = System.IO.Path.Combine(targetDirectory, "resources", "SampleData.json");
                
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };
                
                string jsonData = JsonSerializer.Serialize(sampleData, options);
                // No need to remove brackets - we want to keep the array structure
                
                await File.WriteAllTextAsync(filePath, jsonData);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving sample data: {ex.Message}");
                throw;
            }
        }

        private async Task<T> DeserializeRequestBodyAsync<T>(HttpContext context)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };

            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, options);
        }
    }
}
