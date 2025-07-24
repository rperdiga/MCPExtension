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
        private readonly IServiceProvider _serviceProvider;
        private static string? _lastError;
        private static Exception? _lastException;

        public MendixAdditionalTools(
            IModel model, 
            ILogger<MendixAdditionalTools> logger,
            IPageGenerationService pageGenerationService,
            INavigationManagerService navigationManagerService,
            IServiceProvider serviceProvider)
        {
            _model = model;
            _logger = logger;
            _pageGenerationService = pageGenerationService;
            _navigationManagerService = navigationManagerService;
            _serviceProvider = serviceProvider;
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
                return JsonSerializer.Serialize(new { error });
            }

            var moduleName = arguments["module_name"]?.ToString();
            
            var module = Utils.Utils.GetMyFirstModule(_model);
            if (module == null)
            {
                var error = "No module found in ListMicroflows.";
                _logger.LogError(error);
                SetLastError(error);
                return JsonSerializer.Serialize(new { error });
            }

            if (!string.IsNullOrEmpty(moduleName) && module.Name != moduleName)
            {
                return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });
            }

                var microflows = module.GetDocuments()
                    .OfType<IMicroflow>()
                    .Select(mf => new
                    {
                        name = mf.Name,
                        module = module.Name
                    }).ToArray();

                return JsonSerializer.Serialize(new { microflows = microflows });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing microflows");
                SetLastError("Error listing microflows", ex);
                return JsonSerializer.Serialize(new { error = ex.Message });
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
                return JsonSerializer.Serialize(new { error });
            }

            var microflowName = arguments["microflow_name"]?.ToString();
            
            if (string.IsNullOrEmpty(microflowName))
            {
                var error = "Microflow name is required";
                SetLastError(error);
                return JsonSerializer.Serialize(new { error = error });
            }

            var module = Utils.Utils.GetMyFirstModule(_model);
            if (module == null)
            {
                var error = "No module found in ReadMicroflowDetails.";
                _logger.LogError(error);
                SetLastError(error);
                return JsonSerializer.Serialize(new { error });
            }

            // Find the microflow
                var microflow = module.GetDocuments()
                    .OfType<IMicroflow>()
                    .FirstOrDefault(mf => mf.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));

                if (microflow == null)
                {
                    var error = $"Microflow '{microflowName}' not found in module '{module.Name}'";
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error = error });
                }

                // Get microflow service to analyze activities
                var microflowService = _serviceProvider?.GetService<IMicroflowService>();
                var activitiesInfo = new List<object>();
                
                if (microflowService != null)
                {
                    try
                    {
                        var activities = microflowService.GetAllMicroflowActivities(microflow);
                        for (int i = 0; i < activities.Count; i++)
                        {
                            var activity = activities[i];
                            activitiesInfo.Add(new
                            {
                                position = i + 1, // 1-based position
                                index = i, // 0-based index
                                type = activity.GetType().Name,
                                activityId = activity.GetHashCode()
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not retrieve activity details for microflow analysis");
                    }
                }

                // Extract basic microflow information
                var microflowInfo = new
                {
                    name = microflow.Name,
                    qualifiedName = microflow.QualifiedName?.FullName ?? "Unknown",
                    module = module.Name,
                    returnType = microflow.ReturnType?.GetType().Name ?? "Void",
                    returnTypeFullName = microflow.ReturnType?.GetType().FullName ?? "Void",
                    activityCount = activitiesInfo.Count,
                    activities = activitiesInfo,
                    // Note: Advanced activity analysis requires IMicroflowService which is not available
                    limitations = activitiesInfo.Any() 
                        ? "Basic activity information available. Use read_microflow_activities API for detailed analysis."
                        : "Detailed activity analysis requires additional Mendix services not currently available in this MCP implementation"
                };

                return JsonSerializer.Serialize(new 
                { 
                    success = true,
                    microflow = microflowInfo
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading microflow details");
                SetLastError("Error reading microflow details", ex);
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<object> GetLastError(JsonObject arguments)
        {
            try
            {
                if (string.IsNullOrEmpty(_lastError))
                {
                    return JsonSerializer.Serialize(new { 
                        message = "No errors recorded",
                        last_error = (string?)null
                    });
                }

                return JsonSerializer.Serialize(new { 
                    message = "Last error retrieved",
                    last_error = _lastError,
                    details = _lastException?.Message,
                    stack_trace = _lastException?.StackTrace,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting last error");
                return JsonSerializer.Serialize(new { error = ex.Message });
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
                    "create_microflow",
                    "create_microflow_activity",
                    "create_microflow_activities_sequence"
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
                
                // Only set a non-void return type if explicitly specified and not empty/whitespace
                if (!string.IsNullOrWhiteSpace(returnTypeStr))
                {
                    returnType = Utils.Utils.DataTypeFromString(returnTypeStr);
                }

                _logger.LogInformation($"[create_microflow] Return type string: '{returnTypeStr ?? "null"}', resolved to: {returnType}");

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

        public async Task<object> CreateMicroflowActivity(JsonObject arguments)
        {
            try
            {
                // Add detailed logging to debug parameter reception
                _logger.LogInformation("=== CreateMicroflowActivity Debug ===");
                _logger.LogInformation($"Raw arguments received: {arguments?.ToJsonString()}");
                _logger.LogInformation($"Arguments type: {arguments?.GetType().FullName}");
                _logger.LogInformation($"Arguments count: {arguments?.Count ?? 0}");
                
                // Log each key-value pair
                if (arguments != null)
                {
                    foreach (var kvp in arguments)
                    {
                        _logger.LogInformation($"Key: '{kvp.Key}', Value: '{kvp.Value}', Value Type: {kvp.Value?.GetType().FullName}");
                    }
                }

                var microflowName = arguments["microflow_name"]?.ToString();
                var activityType = arguments["activity_type"]?.ToString();
                var activityData = arguments["activity_config"]?.AsObject();
                
                // Parse positioning parameters
                int? insertPosition = null;
                if (arguments.TryGetPropertyValue("insert_position", out var positionValue))
                {
                    if (positionValue != null && int.TryParse(positionValue.ToString(), out int pos))
                    {
                        insertPosition = pos;
                    }
                }
                
                // Alternative parameter name for backward compatibility
                if (!insertPosition.HasValue && arguments.TryGetPropertyValue("insert_after_activity_index", out var indexValue))
                {
                    if (indexValue != null && int.TryParse(indexValue.ToString(), out int idx))
                    {
                        insertPosition = idx + 1; // Convert from "after index" to position
                    }
                }

                _logger.LogInformation($"Extracted microflowName: '{microflowName}'");
                _logger.LogInformation($"Extracted activityType: '{activityType}'");
                _logger.LogInformation($"Extracted activityData: {activityData?.ToJsonString()}");
                _logger.LogInformation($"Extracted insertPosition: {insertPosition?.ToString() ?? "null (insert at start)"}");

                if (string.IsNullOrWhiteSpace(microflowName))
                {
                    var error = "Microflow name is required.";
                    _logger.LogError($"ERROR: {error} - microflowName was null/empty/whitespace");
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                if (string.IsNullOrWhiteSpace(activityType))
                {
                    var error = "Activity type is required.";
                    _logger.LogError($"ERROR: {error} - activityType was null/empty/whitespace");
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                var module = Utils.Utils.GetMyFirstModule(_model);
                if (module == null)
                {
                    var error = "No module found.";
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                // Find the microflow
                var microflow = module.GetDocuments().OfType<IMicroflow>()
                    .FirstOrDefault(mf => mf.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));

                if (microflow == null)
                {
                    var error = $"Microflow '{microflowName}' not found in module '{module.Name}'.";
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                // Create activity based on type
                IActionActivity? activity = null;
                using (var transaction = _model.StartTransaction("Create microflow activity"))
                {
                    switch (activityType.ToLowerInvariant())
                    {
                        case "log":
                        case "log_message":
                            activity = CreateLogActivity(activityData);
                            break;

                        case "change_variable":
                        case "change_value":
                            activity = CreateChangeVariableActivity(activityData);
                            break;

                        case "create_variable":
                        case "create_object":
                        case "create":
                            activity = CreateCreateVariableActivity(activityData);
                            break;

                        case "microflow_call":
                        case "call_microflow":
                            activity = CreateMicroflowCallActivity(activityData);
                            break;

                        // Database Operations
                        case "retrieve_from_database":
                        case "retrieve_database":
                        case "database_retrieve":
                            activity = CreateDatabaseRetrieveActivity(activityData);
                            break;

                        case "retrieve_by_association":
                        case "association_retrieve":
                            activity = CreateAssociationRetrieveActivity(activityData);
                            break;

                        case "commit_object":
                        case "commit":
                            activity = CreateCommitActivity(activityData);
                            break;

                        case "rollback_object":
                        case "rollback":
                            activity = CreateRollbackActivity(activityData);
                            break;

                        case "delete_object":
                        case "delete":
                            activity = CreateDeleteActivity(activityData);
                            break;

                        // List Operations
                        case "create_list":
                        case "new_list":
                            activity = CreateListActivity(activityData);
                            break;

                        case "change_list":
                        case "modify_list":
                            activity = CreateChangeListActivity(activityData);
                            break;

                        case "sort_list":
                            activity = CreateSortListActivity(activityData);
                            break;

                        case "filter_list":
                            activity = CreateFilterListActivity(activityData);
                            break;

                        case "find_in_list":
                        case "find_list_item":
                            activity = CreateFindInListActivity(activityData);
                            break;

                        // Advanced Operations
                        case "aggregate_list":
                        case "list_aggregate":
                            activity = CreateAggregateListActivity(activityData);
                            break;

                        case "java_action_call":
                        case "call_java_action":
                            activity = CreateJavaActionCallActivity(activityData);
                            break;

                        case "change_attribute":
                            activity = CreateChangeAttributeActivity(activityData);
                            break;

                        case "change_association":
                            activity = CreateChangeAssociationActivity(activityData);
                            break;

                        default:
                            var error = $"Unsupported activity type: {activityType}. Currently supported types: create_object, microflow_call, change_variable, retrieve_from_database, retrieve_by_association, commit_object, rollback_object, delete_object, create_list, change_list, sort_list, filter_list, find_in_list, aggregate_list, java_action_call, change_attribute, change_association. Note: log activities are not supported by the Extensions API.";
                            SetLastError(error);
                            return JsonSerializer.Serialize(new { error });
                    }

                    if (activity == null)
                    {
                        var error = $"Failed to create activity of type '{activityType}'. ";
                        if (activityType == "log")
                        {
                            error += "Log activities are not supported by the current Mendix Extensions API. Consider using change_variable or create_variable instead.";
                        }
                        else
                        {
                            error += "Please check the activity configuration and try again.";
                        }
                        SetLastError(error);
                        return JsonSerializer.Serialize(new { error });
                    }

                    // Insert the activity into the microflow
                    // Using a generic approach to insert at the start
                    try
                    {
                        // Get the IMicroflowService from service provider
                        var microflowService = _serviceProvider?.GetService<IMicroflowService>();
                        if (microflowService == null)
                        {
                            var error = "IMicroflowService not available.";
                            SetLastError(error);
                            return JsonSerializer.Serialize(new { error });
                        }

                        bool insertResult = false;
                        string insertMessage = "";

                        // Handle activity positioning
                        if (insertPosition.HasValue && insertPosition.Value > 1)
                        {
                            // Try to find existing activities to understand the current state
                            var orderedActivities = GetOrderedMicroflowActivities(microflow, microflowService);
                            
                            _logger.LogDebug($"Attempting to insert at position {insertPosition.Value}, found {orderedActivities.Count} existing activities");
                            
                            // Check if we have any existing activities to work with
                            if (orderedActivities.Count > 0)
                            {
                                // Position semantics:
                                // Position 1 = after start (before 1st activity)
                                // Position 2 = after 1st activity (before 2nd activity, or at end if only 1 activity exists)
                                // Position 3 = after 2nd activity (before 3rd activity, or at end if only 2 activities exist)
                                // etc.
                                
                                int targetActivityIndex = insertPosition.Value - 2; // Position 2 targets activity at index 0
                                
                                if (targetActivityIndex >= 0 && targetActivityIndex < orderedActivities.Count - 1)
                                {
                                    // We want to insert before a specific existing activity (not the last one)
                                    int insertBeforeIndex = targetActivityIndex + 1; // Insert before the next activity
                                    var targetActivity = orderedActivities[insertBeforeIndex];
                                    
                                    _logger.LogDebug($"Attempting to insert before activity at index {insertBeforeIndex}: {targetActivity.GetType().Name}");
                                    
                                    insertResult = microflowService.TryInsertBeforeActivity(targetActivity, activity);
                                    
                                    if (insertResult)
                                    {
                                        insertMessage = $"Activity inserted at position {insertPosition.Value} (before activity at index {insertBeforeIndex})";
                                        _logger.LogDebug($"Successfully inserted before activity: {targetActivity.GetType().Name}");
                                    }
                                    else
                                    {
                                        // Fallback: Insert after start
                                        _logger.LogWarning($"TryInsertBeforeActivity failed, falling back to inserting after start");
                                        insertResult = microflowService.TryInsertAfterStart(microflow, activity);
                                        insertMessage = insertResult 
                                            ? $"Activity inserted after start (fallback from position {insertPosition.Value})"
                                            : "Failed to insert activity at specified position";
                                    }
                                }
                                else
                                {
                                    // Position points to after the last activity, or beyond existing activities
                                    // API Limitation: We cannot insert "after" an activity, only "before" an activity or "after start"
                                    // The best we can do is insert after start, which will put it at the beginning
                                    
                                    _logger.LogWarning($"Position {insertPosition.Value} would place activity after the last existing activity. " +
                                                      $"API limitation: Cannot insert after activities, only before them or after start. " +
                                                      $"Inserting after start instead (will appear at beginning of microflow).");
                                    
                                    insertResult = microflowService.TryInsertAfterStart(microflow, activity);
                                    insertMessage = insertResult 
                                        ? $"Activity inserted after start (API limitation: position {insertPosition.Value} would be after last activity, which is not supported)"
                                        : "Failed to insert activity";
                                    
                                    // Add additional context to help user understand the limitation
                                    if (insertResult)
                                    {
                                        insertMessage += $". Note: The Mendix Extensions API only supports inserting activities 'after start' or 'before existing activities'. " +
                                                        $"To achieve the desired position, you may need to manually rearrange activities in Studio Pro after creation.";
                                    }
                                }
                            }
                            else
                            {
                                // No existing activities, position > 1 doesn't make sense
                                _logger.LogInformation($"No existing activities found, inserting at start regardless of requested position {insertPosition.Value}");
                                insertResult = microflowService.TryInsertAfterStart(microflow, activity);
                                insertMessage = $"Activity inserted after start (first activity in microflow)";
                            }
                        }
                        else
                        {
                            // Position 1 or default: insert after start
                            insertResult = microflowService.TryInsertAfterStart(microflow, activity);
                            insertMessage = insertPosition.HasValue && insertPosition.Value == 1 
                                ? "Activity inserted at position 1 (after start)"
                                : "Activity inserted after start (default position)";
                        }

                        if (!insertResult)
                        {
                            var error = "Failed to insert activity into microflow.";
                            SetLastError(error);
                            return JsonSerializer.Serialize(new { error });
                        }

                        transaction.Commit();

                        return JsonSerializer.Serialize(new {
                            success = true,
                            message = $"Activity of type '{activityType}' added to microflow '{microflowName}' successfully. {insertMessage}",
                            activity = new {
                                type = activityType,
                                microflow = microflowName,
                                module = module.Name,
                                insertPosition = insertPosition,
                                insertMethod = insertPosition.HasValue && insertPosition.Value > 0 ? "TryInsertBeforeActivity" : "TryInsertAfterStart"
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error inserting activity into microflow: {ex.Message}");
                        var error = $"Error inserting activity: {ex.Message}";
                        SetLastError(error, ex);
                        return JsonSerializer.Serialize(new { error });
                    }
                }
            }
            catch (Exception ex)
            {
                SetLastError($"Error creating microflow activity: {ex.Message}", ex);
                _logger.LogError(ex, "Error in CreateMicroflowActivity");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private IActionActivity? CreateLogActivity(JsonObject? activityData)
        {
            try
            {
                // Log activities are not supported by the current Mendix Extensions API
                // Instead, we'll create a simple comment activity or recommend using a different approach
                
                var message = activityData?["message"]?.ToString() ?? "Log message not available in Extensions API";
                var level = activityData?["level"]?.ToString() ?? "info";
                
                _logger.LogWarning($"Log activities are not supported by the Extensions API. Requested log: [{level}] {message}");
                
                // Return null to indicate this activity type is not supported
                // This will cause the method to return an error message
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating log activity");
                SetLastError($"Error creating log activity: {ex.Message}", ex);
                return null;
            }
        }

        private IActionActivity? CreateChangeVariableActivity(JsonObject? activityData)
        {
            try
            {
                var variableName = activityData?["variable_name"]?.ToString() ?? "newVariable";
                var newValue = activityData?["new_value"]?.ToString() ?? "''";

                // Create a change variable action (this is typically a ChangeObjectAction)
                var changeAction = _model.Create<IChangeObjectAction>();
                
                // Set properties for the change action
                // Note: This is a simplified example - real implementation would need proper entity and member configuration
                
                // Create the action activity
                var activity = _model.Create<IActionActivity>();
                activity.Action = changeAction;
                
                _logger.LogInformation($"Created change variable activity with variable '{variableName}' and value '{newValue}'");
                
                return activity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating change variable activity");
                SetLastError($"Error creating change variable activity: {ex.Message}", ex);
                return null;
            }
        }

        private IActionActivity? CreateCreateVariableActivity(JsonObject? activityData)
        {
            try
            {
                // Support multiple naming conventions - including "entity", "entityType", "entityName" parameters
                var variableName = activityData?["variableName"]?.ToString() ?? 
                                  activityData?["variable_name"]?.ToString() ?? "newVariable";
                var entityType = activityData?["entity"]?.ToString() ?? 
                                activityData?["entityType"]?.ToString() ?? 
                                activityData?["entityName"]?.ToString() ?? 
                                activityData?["variable_type"]?.ToString() ?? "String";
                var initialValue = activityData?["initial_value"]?.ToString() ?? "''";

                _logger.LogInformation($"Creating create object activity with variable '{variableName}', entityType '{entityType}'");

                // Create a create object action
                var createAction = _model.Create<ICreateObjectAction>();
                
                // Set the output variable name
                createAction.OutputVariableName = variableName;
                
                // Try to find and set the entity if entityType is provided
                if (!string.IsNullOrEmpty(entityType) && entityType != "String")
                {
                    try
                    {
                        var module = Utils.Utils.GetMyFirstModule(_model);
                        if (module?.DomainModel != null)
                        {
                            // Look for the entity by qualified name (e.g., "MyFirstModule.Customer")
                            var entity = module.DomainModel.GetEntities()
                                .FirstOrDefault(e => e.QualifiedName.ToString() == entityType || e.Name == entityType);
                            
                            if (entity != null)
                            {
                                createAction.Entity = entity.QualifiedName;
                                _logger.LogInformation($"Successfully set entity '{entity.QualifiedName}' for create action");
                            }
                            else
                            {
                                _logger.LogWarning($"Entity '{entityType}' not found in domain model. Available entities: {string.Join(", ", module.DomainModel.GetEntities().Select(e => e.QualifiedName.ToString()))}");
                            }
                        }
                    }
                    catch (Exception entityEx)
                    {
                        _logger.LogError(entityEx, $"Error setting entity '{entityType}' for create action");
                    }
                }
                
                // Create the action activity
                var activity = _model.Create<IActionActivity>();
                activity.Action = createAction;
                
                _logger.LogInformation($"Created create object activity with variable '{variableName}' for entity type '{entityType}'");
                
                return activity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating create variable activity");
                SetLastError($"Error creating create variable activity: {ex.Message}", ex);
                return null;
            }
        }

        private IActionActivity? CreateMicroflowCallActivity(JsonObject? activityData)
        {
            try
            {
                var microflowName = activityData?["microflow_name"]?.ToString();
                var returnVariable = activityData?["return_variable"]?.ToString();

                if (string.IsNullOrEmpty(microflowName))
                {
                    _logger.LogError("Microflow name is required for microflow call activity");
                    SetLastError("Microflow name is required for microflow call activity");
                    return null;
                }

                // Create microflow call action
                var microflowCallAction = _model.Create<IMicroflowCallAction>();
                var microflowCall = _model.Create<IMicroflowCall>();
                
                // Set the microflow call action properties
                microflowCallAction.MicroflowCall = microflowCall;
                
                // Set return variable if provided
                if (!string.IsNullOrEmpty(returnVariable))
                {
                    microflowCallAction.UseReturnVariable = true;
                    microflowCallAction.OutputVariableName = returnVariable;
                }
                else
                {
                    microflowCallAction.UseReturnVariable = false;
                }

                // Create the action activity
                var activity = _model.Create<IActionActivity>();
                activity.Action = microflowCallAction;
                
                _logger.LogInformation($"Created microflow call activity for microflow '{microflowName}' with return variable '{returnVariable ?? "none"}'");
                
                return activity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating microflow call activity");
                SetLastError($"Error creating microflow call activity: {ex.Message}", ex);
                return null;
            }
        }

        private LogLevel GetLogLevel(string logLevel)
        {
            return logLevel.ToLowerInvariant() switch
            {
                "trace" => LogLevel.Trace,
                "debug" => LogLevel.Debug,
                "info" or "information" => LogLevel.Information,
                "warn" or "warning" => LogLevel.Warning,
                "error" => LogLevel.Error,
                "critical" => LogLevel.Critical,
                _ => LogLevel.Information
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

        #region Database Operations

        /// <summary>
        /// Creates a database retrieve activity with custom range support.
        /// </summary>
        /// <param name="activityData">Activity configuration data</param>
        /// <returns>IActionActivity for database retrieval</returns>
        private IActionActivity? CreateDatabaseRetrieveActivity(JsonObject activityData)
        {
            try
            {
                _logger.LogInformation("Starting CreateDatabaseRetrieveActivity");

                // Enhanced parameter extraction with multiple naming conventions
                string entityName = activityData["entityName"]?.ToString() ??
                                   activityData["entity"]?.ToString() ??
                                   activityData["Entity"]?.ToString() ?? "";

                string? xpath = activityData["xpath"]?.ToString() ??
                               activityData["xPath"]?.ToString() ??
                               activityData["XPath"]?.ToString();

                string outputVariable = activityData["outputVariable"]?.ToString() ??
                                       activityData["output"]?.ToString() ??
                                       activityData["output_variable"]?.ToString() ??
                                       activityData["variableName"]?.ToString() ?? "RetrievedObjects";

                string range = activityData["range"]?.ToString()?.ToLowerInvariant() ?? "all";
                
                // Only extract limit and offset if range is custom or if they are explicitly provided
                int? limit = null;
                int? offset = null;
                
                if (range == "custom" || activityData.ContainsKey("limit") || activityData.ContainsKey("offset"))
                {
                    limit = int.Parse(activityData["limit"]?.ToString() ?? "10");
                    offset = int.Parse(activityData["offset"]?.ToString() ?? "0");
                    range = "custom"; // Force to custom if limit/offset are provided
                }
                
                _logger.LogInformation($"Parameters - entityName: '{entityName}', xpath: '{xpath}', outputVariable: '{outputVariable}', range: '{range}', limit: {limit?.ToString() ?? "N/A"}, offset: {offset?.ToString() ?? "N/A"}");

                // Enhanced entity name validation
                if (string.IsNullOrEmpty(entityName))
                {
                    // Get all available entities for diagnostics
                    var allEntities = Utils.Utils.GetMyFirstModule(_model).DomainModel.GetEntities()
                        .Select(e => e.Name).ToList();
                    
                    string availableEntities = allEntities.Any() ? 
                        string.Join(", ", allEntities) : "No entities found";
                    
                    string error = $"Entity name is required for database retrieve activity. Available entities: {availableEntities}";
                    _logger.LogError(error);
                    SetLastError(error, new ArgumentException("Missing entity name"));
                    return null;
                }

                // Find the entity in the domain model
                var module = Utils.Utils.GetMyFirstModule(_model);
                var entity = module.DomainModel.GetEntities()
                    .FirstOrDefault(e => e.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase));

                if (entity == null)
                {
                    string error = $"Entity '{entityName}' not found in domain model";
                    _logger.LogError(error);
                    SetLastError(error, new ArgumentException($"Entity not found: {entityName}"));
                    return null;
                }

                _logger.LogInformation($"Found entity '{entityName}' in domain model");

                // Get required services
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                var microflowExpressionService = _serviceProvider?.GetService<IMicroflowExpressionService>();
                
                if (microflowActivitiesService == null)
                {
                    string error = "IMicroflowActivitiesService not available in service provider";
                    _logger.LogError(error);
                    SetLastError(error, new InvalidOperationException("Required service not available"));
                    return null;
                }

                if (microflowExpressionService == null)
                {
                    string error = "IMicroflowExpressionService not available in service provider";
                    _logger.LogError(error);
                    SetLastError(error, new InvalidOperationException("Required service not available"));
                    return null;
                }

                IActionActivity retrieveActivity;
                
                // Handle different range types
                if (range == "first" || range == "1" || range == "single")
                {
                    // Use the boolean overload for "first item only"
                    retrieveActivity = microflowActivitiesService.CreateDatabaseRetrieveSourceActivity(
                        _model,
                        outputVariable,
                        entity,
                        xpath ?? "", // Empty string if no XPath constraint
                        true, // retrieveJustFirstItem = true
                        new Mendix.StudioPro.ExtensionsAPI.Model.Microflows.AttributeSorting[0] // No sorting for now
                    );
                    
                    _logger.LogInformation($"Created database retrieve activity for first item only");
                }
                else if (range == "all")
                {
                    // Use the boolean overload for "all items"
                    retrieveActivity = microflowActivitiesService.CreateDatabaseRetrieveSourceActivity(
                        _model,
                        outputVariable,
                        entity,
                        xpath ?? "", // Empty string if no XPath constraint
                        false, // retrieveJustFirstItem = false (get all)
                        new Mendix.StudioPro.ExtensionsAPI.Model.Microflows.AttributeSorting[0] // No sorting for now
                    );
                    
                    _logger.LogInformation($"Created database retrieve activity for all items");
                }
                else
                {
                    // Only use custom range if limit/offset were actually provided
                    if (limit.HasValue && offset.HasValue)
                    {
                        // Use custom range with limit and offset
                        // Create expressions for offset and limit
                        var offsetExpression = microflowExpressionService.CreateFromString(offset.Value.ToString());
                        var limitExpression = microflowExpressionService.CreateFromString(limit.Value.ToString());
                        
                        // Create the range tuple for the overload that accepts (IMicroflowExpression startingIndex, IMicroflowExpression amount)
                        var customRange = (offsetExpression, limitExpression);
                        
                        // Use the complex overload for custom range
                        retrieveActivity = microflowActivitiesService.CreateDatabaseRetrieveSourceActivity(
                            _model,
                            outputVariable,
                            entity,
                            xpath ?? "", // Empty string if no XPath constraint
                            customRange, // (startingIndex, amount) tuple
                            new Mendix.StudioPro.ExtensionsAPI.Model.Microflows.AttributeSorting[0] // No sorting for now
                        );
                        
                        _logger.LogInformation($"Created database retrieve activity with custom range (offset: {offset.Value}, limit: {limit.Value})");
                    }
                    else
                    {
                        // This shouldn't happen with the new logic, but fallback to "all"
                        retrieveActivity = microflowActivitiesService.CreateDatabaseRetrieveSourceActivity(
                            _model,
                            outputVariable,
                            entity,
                            xpath ?? "", // Empty string if no XPath constraint
                            false, // retrieveJustFirstItem = false (get all)
                            new Mendix.StudioPro.ExtensionsAPI.Model.Microflows.AttributeSorting[0] // No sorting for now
                        );
                        
                        _logger.LogInformation($"Created database retrieve activity for all items (fallback)");
                    }
                }

                _logger.LogInformation($"Successfully created database retrieve activity for entity '{entityName}' with output variable '{outputVariable}'");
                
                return retrieveActivity;
            }
            catch (Exception ex)
            {
                string error = $"Error creating database retrieve activity: {ex.Message}";
                _logger.LogError(ex, error);
                SetLastError(error, ex);
                return null;
            }
        }

        /// <summary>
        /// Creates an association retrieve activity.
        /// </summary>
        /// <param name="activityData">Activity configuration data</param>
        /// <returns>IActionActivity for association retrieval</returns>
        private IActionActivity? CreateAssociationRetrieveActivity(JsonObject activityData)
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                if (microflowActivitiesService == null)
                {
                    SetLastError("IMicroflowActivitiesService not available");
                    return null;
                }

                string outputVariable = activityData["output_variable"]?.ToString() ?? 
                                       activityData["outputVariable"]?.ToString() ?? 
                                       activityData["variable_name"]?.ToString() ?? 
                                       activityData["variableName"]?.ToString() ?? "AssociatedObjects";

                string associationName = activityData["association"]?.ToString() ?? 
                                        activityData["associationName"]?.ToString() ?? 
                                        activityData["association_name"]?.ToString() ??
                                        throw new ArgumentException("Association name is required");

                string inputVariable = activityData["input_variable"]?.ToString() ?? 
                                      activityData["inputVariable"]?.ToString() ?? 
                                      throw new ArgumentException("Input variable is required");

                var module = Utils.Utils.GetMyFirstModule(_model);
                // TODO: Find the correct way to access associations from IDomainModel
                // For now, return null until we find the proper API
                SetLastError($"Association retrieve not yet implemented - association API access needs to be researched");
                return null;
                // TODO: Find the correct way to access associations from IDomainModel
                // For now, return null until we find the proper API
                SetLastError($"Association retrieve not yet implemented - association API access needs to be researched");
                return null;

                // var association = associations.FirstOrDefault(a => a.Name == associationName);
                // if (association == null)
                // {
                //     SetLastError($"Association '{associationName}' not found in module");
                //     return null;
                // }

                // return microflowActivitiesService.CreateAssociationRetrieveSourceActivity(
                //     _model, association, inputVariable, outputVariable);
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create association retrieve activity: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Creates a commit object activity.
        /// </summary>
        /// <param name="activityData">Activity configuration data</param>
        /// <returns>IActionActivity for committing objects</returns>
        private IActionActivity? CreateCommitActivity(JsonObject activityData)
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                if (microflowActivitiesService == null)
                {
                    SetLastError("IMicroflowActivitiesService not available");
                    return null;
                }

                string variableName = activityData["variable_name"]?.ToString() ?? 
                                     activityData["variableName"]?.ToString() ?? 
                                     activityData["variable"]?.ToString() ??
                                     activityData["objectVariable"]?.ToString() ??
                                     activityData["object"]?.ToString() ??
                                     throw new ArgumentException("Variable name is required for commit. Please specify one of: variable_name, variableName, variable, objectVariable, or object in the activity_config.");

                bool refreshInClient = bool.Parse(activityData["refresh_in_client"]?.ToString() ?? 
                                                 activityData["refreshInClient"]?.ToString() ?? "true");

                bool withEvents = bool.Parse(activityData["with_events"]?.ToString() ?? 
                                           activityData["withEvents"]?.ToString() ?? "true");

                return microflowActivitiesService.CreateCommitObjectActivity(
                    _model, variableName, refreshInClient, withEvents);
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create commit activity: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Creates a rollback object activity.
        /// </summary>
        /// <param name="activityData">Activity configuration data</param>
        /// <returns>IActionActivity for rolling back objects</returns>
        private IActionActivity? CreateRollbackActivity(JsonObject activityData)
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                if (microflowActivitiesService == null)
                {
                    SetLastError("IMicroflowActivitiesService not available");
                    return null;
                }

                string variableName = activityData["variable_name"]?.ToString() ?? 
                                     activityData["variableName"]?.ToString() ?? 
                                     activityData["variable"]?.ToString() ??
                                     activityData["objectVariable"]?.ToString() ??
                                     activityData["object"]?.ToString() ??
                                     throw new ArgumentException("Variable name is required for rollback. Please specify one of: variable_name, variableName, variable, objectVariable, or object in the activity_config.");

                bool refreshInClient = bool.Parse(activityData["refresh_in_client"]?.ToString() ?? 
                                                 activityData["refreshInClient"]?.ToString() ?? "true");

                return microflowActivitiesService.CreateRollbackObjectActivity(
                    _model, variableName, refreshInClient);
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create rollback activity: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Creates a delete object activity.
        /// </summary>
        /// <param name="activityData">Activity configuration data</param>
        /// <returns>IActionActivity for deleting objects</returns>
        private IActionActivity? CreateDeleteActivity(JsonObject activityData)
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                if (microflowActivitiesService == null)
                {
                    SetLastError("IMicroflowActivitiesService not available");
                    return null;
                }

                string variableName = activityData["variable_name"]?.ToString() ?? 
                                     activityData["variableName"]?.ToString() ?? 
                                     activityData["variable"]?.ToString() ??
                                     activityData["objectVariable"]?.ToString() ??
                                     activityData["object"]?.ToString() ??
                                     throw new ArgumentException("Variable name is required for delete. Please specify one of: variable_name, variableName, variable, objectVariable, or object in the activity_config.");

                return microflowActivitiesService.CreateDeleteObjectActivity(_model, variableName);
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create delete activity: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region List Operations - Placeholder Methods

        // TODO: Implement these methods using IMicroflowActivitiesService
        private IActionActivity? CreateListActivity(JsonObject activityData) => null;
        private IActionActivity? CreateChangeListActivity(JsonObject activityData) => null;
        private IActionActivity? CreateSortListActivity(JsonObject activityData) => null;
        private IActionActivity? CreateFilterListActivity(JsonObject activityData) => null;
        private IActionActivity? CreateFindInListActivity(JsonObject activityData) => null;
        private IActionActivity? CreateAggregateListActivity(JsonObject activityData) => null;
        private IActionActivity? CreateJavaActionCallActivity(JsonObject activityData) => null;
        private IActionActivity? CreateChangeAttributeActivity(JsonObject activityData) => null;
        private IActionActivity? CreateChangeAssociationActivity(JsonObject activityData) => null;

        #endregion

        #region Sequential Activity Creation

        public async Task<object> CreateMicroflowActivitiesSequence(JsonObject arguments)
        {
            try
            {
                _logger.LogInformation("=== CreateMicroflowActivitiesSequence Debug ===");
                _logger.LogInformation($"Raw arguments received: {arguments?.ToJsonString()}");

                var microflowName = arguments["microflow_name"]?.ToString();
                var activitiesArray = arguments["activities"]?.AsArray();

                _logger.LogInformation($"Extracted microflowName: '{microflowName}'");
                _logger.LogInformation($"Extracted activities count: {activitiesArray?.Count ?? 0}");

                if (string.IsNullOrWhiteSpace(microflowName))
                {
                    var error = "Microflow name is required.";
                    _logger.LogError($"ERROR: {error}");
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                if (activitiesArray == null || activitiesArray.Count == 0)
                {
                    var error = "Activities array is required and must contain at least one activity.";
                    _logger.LogError($"ERROR: {error}");
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                var module = Utils.Utils.GetMyFirstModule(_model);
                if (module == null)
                {
                    var error = "No module found.";
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                // Find the microflow
                var microflow = module.GetDocuments().OfType<IMicroflow>()
                    .FirstOrDefault(mf => mf.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));

                if (microflow == null)
                {
                    var error = $"Microflow '{microflowName}' not found in module '{module.Name}'.";
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                // Get the microflow service
                var microflowService = _serviceProvider?.GetService<IMicroflowService>();
                if (microflowService == null)
                {
                    var error = "IMicroflowService not available.";
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                // Create all activities first
                var createdActivities = new List<IActionActivity>();
                var activityResults = new List<object>();

                using (var transaction = _model.StartTransaction("Create microflow activities sequence"))
                {
                    try
                    {
                        // Process each activity definition
                        for (int i = 0; i < activitiesArray.Count; i++)
                        {
                            var activityDef = activitiesArray[i]?.AsObject();
                            if (activityDef == null)
                            {
                                _logger.LogWarning($"Skipping null activity at index {i}");
                                continue;
                            }

                            var activityType = activityDef["activity_type"]?.ToString();
                            var activityConfig = activityDef["activity_config"]?.AsObject();

                            _logger.LogInformation($"Processing activity {i + 1}: type='{activityType}'");

                            if (string.IsNullOrWhiteSpace(activityType))
                            {
                                _logger.LogWarning($"Skipping activity at index {i} - no activity type specified");
                                continue;
                            }

                            // Create the activity (reuse existing logic)
                            IActionActivity? activity = CreateActivityByType(activityType, activityConfig);

                            if (activity != null)
                            {
                                createdActivities.Add(activity);
                                activityResults.Add(new
                                {
                                    index = i + 1,
                                    type = activityType,
                                    status = "created"
                                });
                                _logger.LogInformation($"Successfully created activity {i + 1} of type '{activityType}'");
                            }
                            else
                            {
                                var errorMsg = $"Failed to create activity {i + 1} of type '{activityType}'";
                                _logger.LogError(errorMsg);
                                activityResults.Add(new
                                {
                                    index = i + 1,
                                    type = activityType,
                                    status = "failed",
                                    error = errorMsg
                                });
                            }
                        }

                        if (createdActivities.Count == 0)
                        {
                            var error = "No activities were successfully created.";
                            SetLastError(error);
                            return JsonSerializer.Serialize(new { error, activityResults });
                        }

                        // Insert activities in reverse order (like TeamcenterExtension does)
                        // This ensures they appear in the correct sequence in the microflow
                        _logger.LogInformation($"Inserting {createdActivities.Count} activities in reverse order");
                        
                        var reversedActivities = new List<IActionActivity>(createdActivities);
                        reversedActivities.Reverse();

                        foreach (var activity in reversedActivities)
                        {
                            var insertResult = microflowService.TryInsertAfterStart(microflow, activity);
                            if (!insertResult)
                            {
                                var error = $"Failed to insert activity of type {activity.GetType().Name} into microflow.";
                                _logger.LogError(error);
                                SetLastError(error);
                                return JsonSerializer.Serialize(new { error, activityResults });
                            }
                        }

                        transaction.Commit();

                        return JsonSerializer.Serialize(new
                        {
                            success = true,
                            message = $"Successfully created and inserted {createdActivities.Count} activities in sequence to microflow '{microflowName}'",
                            microflow = microflowName,
                            module = module.Name,
                            activitiesCreated = createdActivities.Count,
                            activities = activityResults
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error during sequential activity creation: {ex.Message}");
                        var error = $"Error during sequential activity creation: {ex.Message}";
                        SetLastError(error, ex);
                        return JsonSerializer.Serialize(new { error, activityResults });
                    }
                }
            }
            catch (Exception ex)
            {
                SetLastError($"Error creating microflow activities sequence: {ex.Message}", ex);
                _logger.LogError(ex, "Error in CreateMicroflowActivitiesSequence");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private IActionActivity? CreateActivityByType(string activityType, JsonObject? activityConfig)
        {
            switch (activityType.ToLowerInvariant())
            {
                case "log":
                case "log_message":
                    return CreateLogActivity(activityConfig);

                case "change_variable":
                case "change_value":
                    return CreateChangeVariableActivity(activityConfig);

                case "create_variable":
                case "create_object":
                case "create":
                    return CreateCreateVariableActivity(activityConfig);

                case "microflow_call":
                case "call_microflow":
                    return CreateMicroflowCallActivity(activityConfig);

                // Database Operations
                case "retrieve_from_database":
                case "retrieve_database":
                case "database_retrieve":
                    return CreateDatabaseRetrieveActivity(activityConfig);

                case "retrieve_by_association":
                case "association_retrieve":
                    return CreateAssociationRetrieveActivity(activityConfig);

                case "commit_object":
                case "commit":
                    return CreateCommitActivity(activityConfig);

                case "rollback_object":
                case "rollback":
                    return CreateRollbackActivity(activityConfig);

                case "delete_object":
                case "delete":
                    return CreateDeleteActivity(activityConfig);

                // List Operations
                case "create_list":
                case "new_list":
                    return CreateListActivity(activityConfig);

                case "change_list":
                case "modify_list":
                    return CreateChangeListActivity(activityConfig);

                case "sort_list":
                    return CreateSortListActivity(activityConfig);

                case "filter_list":
                    return CreateFilterListActivity(activityConfig);

                case "find_in_list":
                case "find_list_item":
                    return CreateFindInListActivity(activityConfig);

                // Advanced Operations
                case "aggregate_list":
                case "list_aggregate":
                    return CreateAggregateListActivity(activityConfig);

                case "java_action_call":
                case "call_java_action":
                    return CreateJavaActionCallActivity(activityConfig);

                case "change_attribute":
                    return CreateChangeAttributeActivity(activityConfig);

                case "change_association":
                    return CreateChangeAssociationActivity(activityConfig);

                default:
                    _logger.LogError($"Unsupported activity type: {activityType}");
                    return null;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets microflow activities in their actual execution order by traversing the flow from start event.
        /// This is a simplified approach that works for linear microflows.
        /// </summary>
        /// <param name="microflow">The microflow to analyze</param>
        /// <param name="microflowService">The microflow service</param>
        /// <returns>List of activities in execution order</returns>
        private List<IActivity> GetOrderedMicroflowActivities(IMicroflow microflow, IMicroflowService microflowService)
        {
            try
            {
                // Get all activities from the microflow
                var allActivities = microflowService.GetAllMicroflowActivities(microflow);
                
                _logger.LogDebug($"Found {allActivities.Count()} total activities in microflow '{microflow.Name}'");
                
                // Filter out start and end events, only get action activities
                var actionActivities = allActivities
                    .Where(activity => 
                    {
                        var typeName = activity.GetType().Name;
                        var isStartOrEnd = typeName.Contains("Start") || typeName.Contains("End");
                        _logger.LogDebug($"Activity type: {typeName}, IsStartOrEnd: {isStartOrEnd}");
                        return !isStartOrEnd;
                    })
                    .ToList();

                _logger.LogDebug($"Filtered to {actionActivities.Count} action activities for microflow '{microflow.Name}'");
                
                // For now, return activities in the order they were retrieved
                // A more sophisticated implementation could traverse sequence flows to get true order
                return actionActivities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting ordered activities for microflow '{microflow.Name}'");
                // Fallback: return empty list to be safe
                return new List<IActivity>();
            }
        }

        #endregion

        #endregion
    }
}
