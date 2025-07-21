using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using MCPExtension.Utils;

namespace MCPExtension.Tools
{
    public class MendixAdditionalTools
    {
        private readonly IModel _model;
        private readonly ILogger<MendixAdditionalTools> _logger;
        private readonly IPageGenerationService _pageGenerationService;
        private readonly INavigationManagerService _navigationManagerService;
        private static string? _lastError;
        private static Exception? _lastException;

        public MendixAdditionalTools(
            IModel model, 
            ILogger<MendixAdditionalTools> logger,
            IPageGenerationService pageGenerationService,
            INavigationManagerService navigationManagerService)
        {
            _model = model;
            _logger = logger;
            _pageGenerationService = pageGenerationService;
            _navigationManagerService = navigationManagerService;
        }

        public static void SetLastError(string error, Exception? exception = null)
        {
            _lastError = error;
            _lastException = exception;
        }

    public async Task<object> SaveData(JsonObject arguments)
    {
        try
        {
            if (_model == null)
            {
                var error = "IModel instance is null in SaveData.";
                _logger.LogError(error);
                SetLastError(error);
                return JsonSerializer.Serialize(new { error, success = false });
            }

            var dataProperty = arguments["data"]?.AsObject();
            if (dataProperty == null)
            {
                var currentModule = Utils.Utils.GetMyFirstModule(_model);
                if (currentModule == null)
                {
                    var error = "No module found in SaveData.";
                    _logger.LogError(error);
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error, success = false });
                }
                var moduleName = currentModule?.Name ?? "MyFirstModule";
                    
                var emptyDataError = "Invalid request format or empty data. The save_data tool is used to generate sample data for Mendix domain models.";
                SetLastError(emptyDataError);
                return JsonSerializer.Serialize(new { 
                    error = emptyDataError,
                        message = "The save_data tool requires a 'data' property with entity data in the specified format.",
                        required_format = new {
                            data = new {
                                CustomerEntity = new[] {
                                    new {
                                        VirtualId = "CUST001",
                                        FirstName = "John",
                                        LastName = "Doe",
                                        Email = "john.doe@example.com"
                                    }
                                },
                                OrderEntity = new[] {
                                    new {
                                        VirtualId = "ORD001",
                                        OrderDate = "2023-11-01T10:30:00Z",
                                        TotalAmount = 99.99,
                                        Customer = new {
                                            VirtualId = "CUST001"
                                        }
                                    }
                                }
                            }
                        },
                        format_notes = new {
                            entity_naming = $"Use '{moduleName}.EntityName' format for entity keys (e.g., '{moduleName}.Customer')",
                            virtual_id = "Include a unique VirtualId for each record to establish relationships",
                            relationships = "Reference related entities using their VirtualId in nested objects",
                            dates = "Use ISO 8601 format for dates (YYYY-MM-DDTHH:MM:SSZ)"
                        },
                        purpose = "This tool generates realistic sample data for testing and development purposes.",
                        success = false
                    });
                }

                var module = Utils.Utils.GetMyFirstModule(_model);
                if (module == null)
                {
                    var error = "No module found in SaveData.";
                    _logger.LogError(error);
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error, success = false });
                }
                if (module?.DomainModel == null)
                {
                    var error = "No domain model found.";
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { 
                        error = error,
                        success = false
                    });
                }

                // Validate the data structure
                var validationResult = ValidateDataStructure(dataProperty, module);
                if (!validationResult.IsValid)
                {
                    SetLastError(validationResult.Message);
                    return JsonSerializer.Serialize(new { 
                        error = validationResult.Message,
                        details = validationResult.Details,
                        success = false
                    });
                }

                // Save the data to a JSON file
                var saveResult = await SaveDataToFile(dataProperty);
                if (!saveResult.Success)
                {
                    SetLastError(saveResult.ErrorMessage ?? "Unknown error occurred while saving data");
                    return JsonSerializer.Serialize(new { 
                        error = saveResult.ErrorMessage,
                        success = false
                    });
                }

                return JsonSerializer.Serialize(new { 
                    success = true, 
                    message = "Data validated and saved successfully",
                    file_path = saveResult.FilePath,
                    entities_processed = validationResult.EntitiesProcessed
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving data");
                SetLastError("Error saving data", ex);
                return JsonSerializer.Serialize(new { 
                    error = ex.Message,
                    success = false
                });
            }
        }

    public async Task<object> GenerateOverviewPages(JsonObject arguments)
    {
        try
        {
            if (_model == null)
            {
                var error = "IModel instance is null in GenerateOverviewPages.";
                _logger.LogError(error);
                SetLastError(error);
                return JsonSerializer.Serialize(new { error, success = false });
            }

            var entityNamesArray = arguments["entity_names"]?.AsArray();
                var generateIndexSnippet = arguments["generate_index_snippet"]?.GetValue<bool>() ?? true;

                if (entityNamesArray == null || !entityNamesArray.Any())
                {
                    return JsonSerializer.Serialize(new { 
                        error = "Invalid request format or no entity names provided",
                        success = false
                    });
                }

                var entityNames = entityNamesArray
                    .Select(node => node?.ToString())
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();

                if (!entityNames.Any())
                {
                    return JsonSerializer.Serialize(new { 
                        error = "No valid entity names provided",
                        success = false
                    });
                }

                var module = Utils.Utils.GetMyFirstModule(_model);
                if (module == null)
                {
                    var error = "No module found in GenerateOverviewPages.";
                    _logger.LogError(error);
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error, success = false });
                }
                if (module?.DomainModel == null)
                {
                    return JsonSerializer.Serialize(new { 
                        error = "No domain model found",
                        success = false
                    });
                }

                // Get all entities from the domain model
                var allEntities = module.DomainModel.GetEntities().ToList();
                
                // Filter entities based on the requested names
                var entitiesToGenerate = allEntities
                    .Where(e => entityNames.Contains(e.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (!entitiesToGenerate.Any())
                {
                    return JsonSerializer.Serialize(new { 
                        error = "None of the requested entities were found in the domain model",
                        success = false,
                        available_entities = allEntities.Select(e => e.Name).ToArray()
                    });
                }

                // Generate overview pages using the injected service
                var generatedOverviewPages = _pageGenerationService.GenerateOverviewPages(
                    module,
                    entitiesToGenerate,
                    generateIndexSnippet
                );

                // Add pages to navigation using the injected service
                var overviewPages = generatedOverviewPages
                    .Where(page => page.Name.Contains("overview", StringComparison.InvariantCultureIgnoreCase))
                    .Select(page => (page.Name, page))
                    .ToArray();

                _navigationManagerService.PopulateWebNavigationWith(
                    _model,
                    overviewPages
                );

                return JsonSerializer.Serialize(new { 
                    success = true,
                    message = $"Successfully generated {overviewPages.Length} overview pages",
                    generated_pages = overviewPages.Select(p => p.Name).ToArray(),
                    entities_processed = entitiesToGenerate.Select(e => e.Name).ToArray()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating overview pages");
                SetLastError("Error generating overview pages", ex);
                return JsonSerializer.Serialize(new { 
                    error = ex.Message,
                    success = false
                });
            }
        }

    public async Task<object> ListMicroflows(JsonObject arguments)
    {
        try
        {
            if (_model == null)
            {
                var error = "IModel instance is null in ListMicroflows.";
                _logger.LogError(error);
                SetLastError(error);
                return new { error };
            }

            var moduleName = arguments["module_name"]?.ToString();
            
            var module = Utils.Utils.GetMyFirstModule(_model);
            if (module == null)
            {
                var error = "No module found in ListMicroflows.";
                _logger.LogError(error);
                SetLastError(error);
                return new { error };
            }

            if (!string.IsNullOrEmpty(moduleName) && module.Name != moduleName)
            {
                return new { error = $"Module '{moduleName}' not found" };
            }

                var microflows = module.GetDocuments()
                    .OfType<IMicroflow>()
                    .Select(mf => new
                    {
                        name = mf.Name,
                        module = module.Name
                    }).ToArray();

                return new { microflows = microflows };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing microflows");
                SetLastError("Error listing microflows", ex);
                return new { error = ex.Message };
            }
        }

    public async Task<object> ReadMicroflowDetails(JsonObject arguments)
    {
        try
        {
            if (_model == null)
            {
                var error = "IModel instance is null in ReadMicroflowDetails.";
                _logger.LogError(error);
                SetLastError(error);
                return new { error };
            }

            var microflowName = arguments["microflow_name"]?.ToString();
            
            if (string.IsNullOrEmpty(microflowName))
            {
                var error = "Microflow name is required";
                SetLastError(error);
                return new { error = error };
            }

            var module = Utils.Utils.GetMyFirstModule(_model);
            if (module == null)
            {
                var error = "No module found in ReadMicroflowDetails.";
                _logger.LogError(error);
                SetLastError(error);
                return new { error };
            }

            // Find the microflow
                var microflow = module.GetDocuments()
                    .OfType<IMicroflow>()
                    .FirstOrDefault(mf => mf.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));

                if (microflow == null)
                {
                    var error = $"Microflow '{microflowName}' not found in module '{module.Name}'";
                    SetLastError(error);
                    return new { error = error };
                }

                // Extract basic microflow information
                var microflowInfo = new
                {
                    name = microflow.Name,
                    qualifiedName = microflow.QualifiedName?.FullName ?? "Unknown",
                    module = module.Name,
                    returnType = microflow.ReturnType?.GetType().Name ?? "Void",
                    returnTypeFullName = microflow.ReturnType?.GetType().FullName ?? "Void",
                    // Note: Advanced activity analysis requires IMicroflowService which is not available
                    limitations = "Detailed activity analysis requires additional Mendix services not currently available in this MCP implementation"
                };

                return new 
                { 
                    success = true,
                    microflow = microflowInfo
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading microflow details");
                SetLastError("Error reading microflow details", ex);
                return new { error = ex.Message };
            }
        }

        public async Task<object> GetLastError(JsonObject arguments)
        {
            try
            {
                if (string.IsNullOrEmpty(_lastError))
                {
                    return new { 
                        message = "No errors recorded",
                        last_error = (string?)null
                    };
                }

                return new { 
                    message = "Last error retrieved",
                    last_error = _lastError,
                    details = _lastException?.Message,
                    stack_trace = _lastException?.StackTrace,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting last error");
                return new { error = ex.Message };
            }
        }

        public async Task<object> ListAvailableTools(JsonObject arguments)
        {
            try
            {
                var tools = new[]
                {
                    "read_domain_model",
                    "create_entity",
                    "create_association",
                    "delete_model_element",
                    "diagnose_associations",
                    "create_multiple_entities",
                    "create_multiple_associations",
                    "create_domain_model_from_schema",
                    "save_data",
                    "generate_overview_pages",
                    "list_microflows",
                    "get_last_error",
                    "list_available_tools",
                    "debug_info",
                    "read_microflow_details",
                    "create_microflow"
                };

                return JsonSerializer.Serialize(new { available_tools = tools });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing available tools");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

    public async Task<object> DebugInfo(JsonObject arguments)
    {
        try
        {
            if (_model == null)
            {
                var error = "IModel instance is null in DebugInfo.";
                _logger.LogError(error);
                SetLastError(error);
                return JsonSerializer.Serialize(new { error });
            }

            var module = Utils.Utils.GetMyFirstModule(_model);
            if (module == null)
            {
                var error = "No module found in DebugInfo.";
                _logger.LogError(error);
                SetLastError(error);
                return JsonSerializer.Serialize(new { error });
            }
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
                        Attributes = e.GetAttributes().Select(a => new
                        {
                            Name = a.Name,
                            Type = a.Type?.GetType().Name ?? "Unknown",
                            TypeDetails = a.Type?.ToString() ?? "Unknown"
                        }).ToList(),
                        LocationX = e.Location.X,
                        LocationY = e.Location.Y
                    }).ToList();

                    // Collect association information with detailed mapping
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
                    
                    // Add comprehensive examples
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
                        },
                        dataFormat = new
                        {
                            data = new
                            {
                                MyFirstModule_Customer = new[]
                                {
                                    new
                                    {
                                        VirtualId = "CUST001",
                                        firstName = "John",
                                        lastName = "Doe",
                                        birthDate = "1990-01-01T00:00:00Z",
                                        isActive = true
                                    }
                                }
                            }
                        }
                    };

                    // Add troubleshooting tips
                    response["troubleshooting"] = new
                    {
                        entityNamesList = entities.Select(e => e.Name).ToList(),
                        associationTips = new[] {
                            "Make sure both entities exist before creating an association",
                            "Use simple names without module prefixes in API calls",
                            "Check that association names are unique",
                            "For data operations, use VirtualId for relationship references"
                        },
                        commonIssues = new[] {
                            "Entity names are case sensitive",
                            "Enumeration attributes must have values defined",
                            "Associations require both parent and child entities to exist",
                            "Data validation requires proper JSON structure"
                        }
                    };
                }
                else
                {
                    response["error"] = "No domain model found";
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = "Debug information retrieved successfully",
                    data = response,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving debug info");
                SetLastError("Error retrieving debug info", ex);
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }


        // ...existing code...
        public async Task<object> CreateMicroflow(JsonObject arguments)
        {
            // This method now just redirects to indicate that service injection is needed
            var error = "CreateMicroflow requires service provider context. Use CreateMicroflowWithService instead.";
            SetLastError(error);
            _logger.LogError("[create_microflow] Method called without service context.");
            return JsonSerializer.Serialize(new { error });
        }

        public async Task<object> CreateMicroflowWithService(JsonObject arguments, IMicroflowService microflowService, IServiceProvider serviceProvider)
        {
            try
            {
                var microflowName = arguments["name"]?.ToString();
                if (string.IsNullOrWhiteSpace(microflowName))
                {
                    var error = "Microflow name is required.";
                    SetLastError(error);
                    _logger.LogError("[create_microflow] Microflow name is missing in arguments.");
                    return JsonSerializer.Serialize(new { error });
                }

                var module = Utils.Utils.GetMyFirstModule(_model);
                if (module == null)
                {
                    var error = "No module found.";
                    SetLastError(error);
                    _logger.LogError($"[create_microflow] No module found for model: {_model}");
                    return JsonSerializer.Serialize(new { error });
                }

                // Check for duplicate
                var existing = module.GetDocuments().OfType<IMicroflow>()
                    .FirstOrDefault(mf => mf.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    var error = $"Microflow '{microflowName}' already exists in module '{module.Name}'.";
                    SetLastError(error);
                    _logger.LogError($"[create_microflow] Microflow '{microflowName}' already exists in module '{module.Name}'.");
                    return JsonSerializer.Serialize(new { error });
                }

                if (microflowService == null)
                {
                    var error = "IMicroflowService is not available in the current environment.";
                    SetLastError(error);
                    _logger.LogError("[create_microflow] IMicroflowService is null.");
                    return JsonSerializer.Serialize(new { error });
                }

                // Prepare parameters
                var parameters = arguments["parameters"]?.AsArray();
                var paramList = new List<(string, Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType)>();
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        var paramObj = param?.AsObject();
                        if (paramObj == null)
                        {
                            _logger.LogError("[create_microflow] Parameter object is null in parameters array.");
                            continue;
                        }
                        var paramName = paramObj["name"]?.ToString();
                        var paramTypeStr = paramObj["type"]?.ToString();
                        if (string.IsNullOrWhiteSpace(paramName) || string.IsNullOrWhiteSpace(paramTypeStr))
                        {
                            _logger.LogError($"[create_microflow] Parameter missing name or type: {paramObj}");
                            continue;
                        }
                        var dataType = Utils.Utils.DataTypeFromString(paramTypeStr);
                        paramList.Add((paramName, dataType));
                    }
                }

                // Prepare return value with proper expressions
                var returnTypeStr = arguments["returnType"]?.ToString();
                Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType returnType = Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.Void;
                if (!string.IsNullOrWhiteSpace(returnTypeStr))
                {
                    returnType = Utils.Utils.DataTypeFromString(returnTypeStr);
                }

                Mendix.StudioPro.ExtensionsAPI.Model.Microflows.MicroflowReturnValue? returnValue = null;
                
                // For non-void return types, create proper return value with expression
                if (returnType != Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.Void)
                {
                    try
                    {
                        var microflowExpressionService = serviceProvider.GetRequiredService<IMicroflowExpressionService>();
                        var defaultExpression = GetDefaultExpressionForDataType(returnType);
                        var expression = microflowExpressionService.CreateFromString(defaultExpression);
                        returnValue = new Mendix.StudioPro.ExtensionsAPI.Model.Microflows.MicroflowReturnValue(returnType, expression);
                        _logger.LogInformation($"[create_microflow] Created return value for {returnType} with expression: {defaultExpression}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[create_microflow] Failed to create return value for {returnType}");
                        var error = $"Failed to create return value for type {returnType}: {ex.Message}";
                        SetLastError(error, ex);
                        return JsonSerializer.Serialize(new { error });
                    }
                }

                // Wrap model changes in a transaction
                using (var transaction = _model.StartTransaction("Create microflow"))
                {
                    // Cast module to IFolderBase as required by the API
                    var folderBase = (Mendix.StudioPro.ExtensionsAPI.Model.Projects.IFolderBase)module;
                    
                    // Add debug logging
                    _logger.LogInformation($"[create_microflow] About to call CreateMicroflow with: model={_model != null}, folderBase={folderBase != null}, name={microflowName}, returnValue={returnValue != null}, paramCount={paramList.Count}");
                    
                    var microflow = microflowService.CreateMicroflow(_model, folderBase, microflowName, returnValue, paramList.ToArray());
                    if (microflow == null)
                    {
                        var error = "Failed to create microflow.";
                        SetLastError(error);
                        _logger.LogError("[create_microflow] IMicroflowService.CreateMicroflow returned null.");
                        return JsonSerializer.Serialize(new { error });
                    }
                    
                    transaction.Commit();
                    
                    string qualifiedName = "";
                    try
                    {
                        qualifiedName = microflow.QualifiedName != null ? (microflow.QualifiedName.FullName ?? "") : "";
                    }
                    catch (Exception qnEx)
                    {
                        _logger.LogError(qnEx, "[create_microflow] Exception accessing microflow.QualifiedName.FullName");
                        qualifiedName = "";
                    }
                    
                    return JsonSerializer.Serialize(new {
                        success = true,
                        message = $"Microflow '{microflowName}' created successfully in module '{module.Name}'.",
                        microflow = new {
                            name = microflow.Name,
                            qualifiedName = qualifiedName,
                            module = module.Name,
                            returnType = returnType.ToString(),
                            parameterCount = paramList.Count
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                SetLastError($"Error in create_microflow: {ex.Message}", ex);
                _logger.LogError(ex, "[create_microflow] Unhandled exception");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Gets default expression strings for different data types
        /// </summary>
        private string GetDefaultExpressionForDataType(Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType dataType)
        {
            return dataType switch
            {
                var dt when dt == Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.String => "''",
                var dt when dt == Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.Integer => "0",
                var dt when dt == Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.Decimal => "0.0",
                var dt when dt == Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.Boolean => "false",
                var dt when dt == Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.DateTime => "dateTime(1900)",
                _ => "empty"
            };
        }

        #region Helper Methods

        private (bool IsValid, string Message, string? Details, int EntitiesProcessed) ValidateDataStructure(JsonObject data, IModule module)
        {
            try
            {
                int entitiesProcessed = 0;
                var validationIssues = new List<string>();

                foreach (var entityData in data)
                {
                    // Extract entity name (handle both "ModuleName.EntityName" and "ModuleName_EntityName" formats)
                    var entityKey = entityData.Key;
                    var entityName = entityKey.Contains(".") ? entityKey.Split('.').Last() : 
                                    entityKey.Contains("_") ? entityKey.Split('_').Last() : entityKey;
                    
                    var entity = module.DomainModel.GetEntities()
                        .FirstOrDefault(e => e.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase));

                    if (entity == null)
                    {
                        validationIssues.Add($"Entity '{entityName}' not found in domain model");
                        continue;
                    }

                    if (entityData.Value?.GetValueKind() != JsonValueKind.Array)
                    {
                        validationIssues.Add($"Data for entity '{entityName}' must be an array");
                        continue;
                    }

                    var records = entityData.Value.AsArray();
                    var recordIndex = 0;

                    foreach (var recordNode in records)
                    {
                        recordIndex++;
                        if (recordNode?.GetValueKind() != JsonValueKind.Object)
                        {
                            validationIssues.Add($"Record {recordIndex} in '{entityName}' must be an object");
                            continue;
                        }

                        var record = recordNode.AsObject();

                        // Check for required VirtualId if entity has associations
                        var associations = entity.GetAssociations(AssociationDirection.Both, null);
                        if (associations.Any())
                        {
                            if (!record.ContainsKey("VirtualId") || record["VirtualId"]?.GetValueKind() != JsonValueKind.String)
                            {
                                validationIssues.Add($"Record {recordIndex} in '{entityName}' requires a 'VirtualId' property for relationships");
                                continue;
                            }
                        }

                        // Validate association references - look for both association names and entity names as relationship attributes
                        foreach (var association in associations)
                        {
                            var assocName = association.Association.Name;
                            var relatedEntityName = association.Parent.Name == entity.Name ? 
                                association.Child.Name : association.Parent.Name;
                            
                            // Check for relationship attribute (could be named after association or related entity)
                            var relationshipKey = record.ContainsKey(relatedEntityName) ? relatedEntityName : 
                                                 record.ContainsKey(assocName) ? assocName : null;
                            
                            if (relationshipKey != null)
                            {
                                var assocValue = record[relationshipKey];
                                if (assocValue?.GetValueKind() == JsonValueKind.Object)
                                {
                                    var assocObj = assocValue.AsObject();
                                    if (!assocObj.ContainsKey("VirtualId") || assocObj["VirtualId"]?.GetValueKind() != JsonValueKind.String)
                                    {
                                        validationIssues.Add($"Relationship '{relationshipKey}' in record {recordIndex} of '{entityName}' must have a 'VirtualId' property. Format: {{ \"VirtualId\": \"UNIQUE_ID\" }}");
                                    }
                                }
                                else if (assocValue?.GetValueKind() != JsonValueKind.Null)
                                {
                                    validationIssues.Add($"Relationship '{relationshipKey}' in record {recordIndex} of '{entityName}' must be an object with VirtualId or null");
                                }
                            }
                        }
                    }

                    entitiesProcessed++;
                }

                if (validationIssues.Any())
                {
                    return (false, "Data validation failed", string.Join("; ", validationIssues), entitiesProcessed);
                }

                return (true, "Validation successful", null, entitiesProcessed);
            }
            catch (Exception ex)
            {
                return (false, $"Validation error: {ex.Message}", ex.StackTrace, 0);
            }
        }

        private async Task<(bool Success, string? ErrorMessage, string? FilePath)> SaveDataToFile(JsonObject data)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var executingDirectory = Path.GetDirectoryName(assembly.Location);
                
                if (string.IsNullOrEmpty(executingDirectory))
                {
                    return (false, "Could not determine assembly location", null);
                }

                var directory = new DirectoryInfo(executingDirectory);
                var targetDirectory = directory?.Parent?.Parent?.Parent?.FullName;

                if (string.IsNullOrEmpty(targetDirectory))
                {
                    return (false, "Could not determine target directory", null);
                }

                var resourcesDir = Path.Combine(targetDirectory, "resources");
                if (!Directory.Exists(resourcesDir))
                {
                    Directory.CreateDirectory(resourcesDir);
                }

                var filePath = Path.Combine(resourcesDir, "SampleData.json");
                
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };
                
                var jsonData = JsonSerializer.Serialize(new { data = data }, options);
                
                await File.WriteAllTextAsync(filePath, jsonData);
                
                return (true, null, filePath);
            }
            catch (Exception ex)
            {
                return (false, $"Error saving data to file: {ex.Message}", null);
            }
        }

        #endregion
    }
}
