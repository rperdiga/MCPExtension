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
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using Mendix.StudioPro.ExtensionsAPI.Model.JavaActions;
using Mendix.StudioPro.ExtensionsAPI.Model.Settings;
using Mendix.StudioPro.ExtensionsAPI.Model.Constants;
using Mendix.StudioPro.ExtensionsAPI.Model.DataTypes;
using Mendix.StudioPro.ExtensionsAPI.Model.UntypedModel;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
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
        private readonly string? _projectDirectory;
        private readonly IVersionControlService? _versionControlService;
        private readonly IUntypedModelAccessService? _untypedModelService;
        private static string? _lastError;
        private static Exception? _lastException;

        public MendixAdditionalTools(
            IModel model,
            ILogger<MendixAdditionalTools> logger,
            IPageGenerationService pageGenerationService,
            INavigationManagerService navigationManagerService,
            IServiceProvider serviceProvider,
            string? projectDirectory = null)
        {
            _model = model;
            _logger = logger;
            _pageGenerationService = pageGenerationService;
            _navigationManagerService = navigationManagerService;
            _serviceProvider = serviceProvider;
            _projectDirectory = projectDirectory;
            _versionControlService = serviceProvider.GetService<IVersionControlService>();
            _untypedModelService = serviceProvider.GetService<IUntypedModelAccessService>();
        }

        private string GetDebugLogPath()
        {
            try
            {
                // Use the project directory if available
                if (!string.IsNullOrEmpty(_projectDirectory))
                {
                    string resourcesDir = System.IO.Path.Combine(_projectDirectory, "resources");
                    
                    if (!System.IO.Directory.Exists(resourcesDir))
                    {
                        System.IO.Directory.CreateDirectory(resourcesDir);
                    }
                    
                    return System.IO.Path.Combine(resourcesDir, "mcp_debug.log");
                }
                
                // Fallback to current directory if no project found
                return System.IO.Path.Combine(Environment.CurrentDirectory, "mcp_debug.log");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting debug log path, using fallback");
                return System.IO.Path.Combine(Environment.CurrentDirectory, "mcp_debug.log");
            }
        }

        private string GetAISampleImportLogPath()
        {
            try
            {
                // Use the project directory if available
                if (!string.IsNullOrEmpty(_projectDirectory))
                {
                    string resourcesDir = System.IO.Path.Combine(_projectDirectory, "resources");
                    
                    if (!System.IO.Directory.Exists(resourcesDir))
                    {
                        System.IO.Directory.CreateDirectory(resourcesDir);
                    }
                    
                    return System.IO.Path.Combine(resourcesDir, "AI_Sample_Import.log");
                }
                
                // Fallback to current directory if no project found
                return System.IO.Path.Combine(Environment.CurrentDirectory, "AI_Sample_Import.log");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting AI sample import log path, using fallback");
                return System.IO.Path.Combine(Environment.CurrentDirectory, "AI_Sample_Import.log");
            }
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
                var requestedModuleName = arguments["module_name"]?.ToString();
                var currentModule = Utils.Utils.ResolveModule(_model, requestedModuleName);
                if (currentModule == null)
                {
                    var error = string.IsNullOrWhiteSpace(requestedModuleName) ? "No module found in SaveData." : $"Module '{requestedModuleName}' not found.";
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

                var saveModuleName = arguments["module_name"]?.ToString();
                var module = Utils.Utils.ResolveModule(_model, saveModuleName);
                if (module == null)
                {
                    var error = string.IsNullOrWhiteSpace(saveModuleName) ? "No module found in SaveData." : $"Module '{saveModuleName}' not found.";
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

                var overviewModuleName = arguments["module_name"]?.ToString();
                var module = Utils.Utils.ResolveModule(_model, overviewModuleName);
                if (module == null)
                {
                    var error = string.IsNullOrWhiteSpace(overviewModuleName) ? "No module found in GenerateOverviewPages." : $"Module '{overviewModuleName}' not found.";
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

                // BUG-009 note: The Mendix IPageGenerationService may generate broken widget bindings
                // for enumeration-typed attributes and association references (CE1613 errors).
                // Warn the user so they know to check and fix in Studio Pro.
                var hasEnumAttrs = entitiesToGenerate.Any(e =>
                    e.GetAttributes().Any(a => a.Type?.GetType().Name?.Contains("Enumeration") == true));
                var hasAssocs = entitiesToGenerate.Any(e => e.GetAssociations(AssociationDirection.Both, null).Any());
                var warnings = new List<string>();
                if (hasEnumAttrs)
                    warnings.Add("Some entities have enumeration-typed attributes which may generate broken widget bindings (CE1613). Please verify in Studio Pro.");
                if (hasAssocs)
                    warnings.Add("Some entities have associations which may generate broken reference widget bindings. Please verify in Studio Pro.");

                return JsonSerializer.Serialize(new {
                    success = true,
                    message = $"Successfully generated {overviewPages.Length} overview pages",
                    generated_pages = overviewPages.Select(p => p.Name).ToArray(),
                    entities_processed = entitiesToGenerate.Select(e => e.Name).ToArray(),
                    warnings = warnings.Any() ? warnings.ToArray() : null
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

            var module = Utils.Utils.ResolveModule(_model, moduleName);
            if (module == null)
            {
                var error = string.IsNullOrWhiteSpace(moduleName) ? "No module found in ListMicroflows." : $"Module '{moduleName}' not found.";
                _logger.LogError(error);
                SetLastError(error);
                return JsonSerializer.Serialize(new { error });
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

    private string FormatDataType(DataType? dt)
    {
        if (dt == null) return "Void";
        return dt switch
        {
            ListType lt => $"List of {lt.EntityName?.FullName ?? "Unknown"}",
            ObjectType ot => $"Object of {ot.EntityName?.FullName ?? "Unknown"}",
            EnumerationType et => $"Enumeration {et.EnumerationName?.FullName ?? "Unknown"}",
            _ => dt.ToString()
        };
    }

    private List<object> SerializeMemberChanges(IChangeMembersAction changeMembersAction)
    {
        var changes = new List<object>();
        try
        {
            foreach (var item in changeMembersAction.GetItems())
            {
                string memberName = "unknown";
                string memberKind = "unknown";
                try
                {
                    if (item.MemberType is AttributeMemberChangeType attrChange)
                    {
                        memberName = attrChange.Attribute?.Name ?? "unknown";
                        memberKind = "attribute";
                    }
                    else if (item.MemberType is AssociationMemberChangeType assocChange)
                    {
                        memberName = assocChange.Association?.Name ?? "unknown";
                        memberKind = "association";
                    }
                }
                catch { /* resilient to API quirks */ }

                changes.Add(new
                {
                    type = item.Type.ToString(),
                    member = memberName,
                    memberKind = memberKind,
                    value = item.Value?.Text
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read member changes");
        }
        return changes;
    }

    private Dictionary<string, object?> SerializeActivityAction(IActionActivity activity)
    {
        var result = new Dictionary<string, object?>();
        result["caption"] = activity.Caption;
        result["disabled"] = activity.Disabled;

        try
        {
            switch (activity.Action)
            {
                case ICreateObjectAction createObj:
                    result["actionType"] = "create_object";
                    result["entity"] = createObj.Entity?.FullName;
                    result["outputVariable"] = createObj.OutputVariableName;
                    result["commit"] = createObj.Commit.ToString();
                    result["refreshInClient"] = createObj.RefreshInClient;
                    result["memberChanges"] = SerializeMemberChanges(createObj);
                    break;

                case IChangeObjectAction changeObj:
                    result["actionType"] = "change_object";
                    result["changeVariable"] = changeObj.ChangeVariableName;
                    result["commit"] = changeObj.Commit.ToString();
                    result["refreshInClient"] = changeObj.RefreshInClient;
                    result["memberChanges"] = SerializeMemberChanges(changeObj);
                    break;

                case IRetrieveAction retrieve:
                    result["actionType"] = "retrieve";
                    result["outputVariable"] = retrieve.OutputVariableName;
                    if (retrieve.RetrieveSource is IDatabaseRetrieveSource dbSource)
                    {
                        result["source"] = "database";
                        result["entity"] = dbSource.Entity?.FullName;
                        result["xpathConstraint"] = dbSource.XPathConstraint;
                        result["firstOnly"] = dbSource.RetrieveJustFirstItem;
                        if (dbSource.Range != null)
                        {
                            result["rangeOffset"] = dbSource.Range.OffsetExpression?.Text;
                            result["rangeAmount"] = dbSource.Range.AmountExpression?.Text;
                        }
                        try
                        {
                            var sorting = dbSource.AttributesToSortBy;
                            if (sorting != null && sorting.Length > 0)
                            {
                                result["sortBy"] = sorting.Select(s => new
                                {
                                    attribute = s.Attribute?.Name,
                                    descending = s.SortByDescending
                                }).ToList();
                            }
                        }
                        catch { /* sorting may not be available */ }
                    }
                    else if (retrieve.RetrieveSource is IAssociationRetrieveSource assocSource)
                    {
                        result["source"] = "association";
                        result["association"] = assocSource.Association?.Name;
                        result["startVariable"] = assocSource.StartVariableName;
                    }
                    break;

                case ICreateListAction createList:
                    result["actionType"] = "create_list";
                    result["entity"] = createList.Entity?.FullName;
                    result["outputVariable"] = createList.OutputVariableName;
                    break;

                case ICommitAction commit:
                    result["actionType"] = "commit";
                    result["commitVariable"] = commit.CommitVariableName;
                    result["withEvents"] = commit.WithEvents;
                    result["refreshInClient"] = commit.RefreshInClient;
                    break;

                case IRollbackAction rollback:
                    result["actionType"] = "rollback";
                    result["rollbackVariable"] = rollback.RollbackVariableName;
                    result["refreshInClient"] = rollback.RefreshInClient;
                    break;

                case IDeleteAction delete:
                    result["actionType"] = "delete";
                    result["deleteVariable"] = delete.DeleteVariableName;
                    break;

                case IChangeListAction changeList:
                    result["actionType"] = "change_list";
                    result["changeVariable"] = changeList.ChangeVariableName;
                    result["operation"] = changeList.Type.ToString();
                    try { result["value"] = changeList.Value?.Text; } catch { }
                    break;

                case IListOperationAction listOp:
                    result["actionType"] = "list_operation";
                    result["outputVariable"] = listOp.OutputVariableName;
                    var op = listOp.Operation;
                    result["listVariable"] = op?.ListVariableName;
                    if (op is ISort sort)
                    {
                        result["operationType"] = "sort";
                        try
                        {
                            var sortAttrs = sort.AttributesToSortBy;
                            if (sortAttrs != null && sortAttrs.Length > 0)
                            {
                                result["sortBy"] = sortAttrs.Select(s => new
                                {
                                    attribute = s.Attribute?.Name,
                                    descending = s.SortByDescending
                                }).ToList();
                            }
                        }
                        catch { }
                    }
                    else if (op is IFilter filter)
                    {
                        result["operationType"] = "filter";
                        result["expression"] = filter.Expression?.Text;
                    }
                    else if (op is IFindByExpression findByExpr)
                    {
                        result["operationType"] = "find_by_expression";
                        result["expression"] = findByExpr.Expression?.Text;
                    }
                    else if (op is IFind find)
                    {
                        result["operationType"] = "find";
                        result["expression"] = find.Expression?.Text;
                    }
                    else if (op is IBinaryListOperation binaryOp)
                    {
                        var rawName = op.GetType().Name.ToLowerInvariant().Replace("proxy", "");
                        result["operationType"] = rawName;
                        result["secondListVariable"] = binaryOp.SecondListOrObjectVariableName;
                    }
                    else
                    {
                        var rawName = op?.GetType().Name ?? "unknown";
                        result["operationType"] = rawName.ToLowerInvariant().Replace("proxy", "");
                    }
                    break;

                case IAggregateListAction aggregate:
                    result["actionType"] = "aggregate_list";
                    result["inputListVariable"] = aggregate.InputListVariableName;
                    result["outputVariable"] = aggregate.OutputVariableName;
                    result["function"] = aggregate.AggregateFunction.ToString();
                    try
                    {
                        var aggFunc = aggregate.AggregateListFunction;
                        if (aggFunc?.Expression != null)
                            result["expression"] = aggFunc.Expression.Text;
                        if (aggFunc?.Attribute != null)
                            result["attribute"] = aggFunc.Attribute.Name;
                        if (aggFunc?.ReduceListFunction != null)
                        {
                            result["reduceInitialValue"] = aggFunc.ReduceListFunction.InitialValueExpression?.Text;
                            result["reduceDataType"] = FormatDataType(aggFunc.ReduceListFunction.DataType);
                        }
                    }
                    catch { }
                    break;

                case IMicroflowCallAction mfCall:
                    result["actionType"] = "microflow_call";
                    result["calledMicroflow"] = mfCall.MicroflowCall?.Microflow?.FullName;
                    result["outputVariable"] = mfCall.OutputVariableName;
                    result["useReturnVariable"] = mfCall.UseReturnVariable;
                    try
                    {
                        var mappings = mfCall.MicroflowCall?.GetParameterMappings();
                        if (mappings != null && mappings.Count > 0)
                        {
                            result["parameterMappings"] = mappings.Select(m => new
                            {
                                parameter = m.Parameter?.FullName,
                                argument = m.Argument?.Text
                            }).ToList();
                        }
                    }
                    catch { }
                    break;

                case IJavaActionCallAction javaCall:
                    result["actionType"] = "java_action_call";
                    result["javaAction"] = javaCall.JavaAction?.FullName;
                    result["outputVariable"] = javaCall.OutputVariableName;
                    result["useReturnVariable"] = javaCall.UseReturnVariable;
                    try
                    {
                        var mappings = javaCall.GetParameterMappings();
                        if (mappings != null && mappings.Count > 0)
                        {
                            result["parameterMappings"] = mappings.Select(m => new
                            {
                                parameter = m.ToString()
                            }).ToList();
                        }
                    }
                    catch { }
                    break;

                default:
                    result["actionType"] = "unknown";
                    result["typeName"] = activity.Action?.GetType().Name ?? "null";
                    break;
            }
        }
        catch (Exception ex)
        {
            result["actionType"] = "error";
            result["error"] = ex.Message;
            result["typeName"] = activity.Action?.GetType().Name ?? "null";
        }

        return result;
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
                return JsonSerializer.Serialize(new { error });
            }

            // Handle qualified names like "Module.MicroflowName"
            var moduleName = arguments["module_name"]?.ToString();
            if (microflowName.Contains('.') && string.IsNullOrWhiteSpace(moduleName))
            {
                var parts = microflowName.Split('.', 2);
                moduleName = parts[0];
                microflowName = parts[1];
            }

            var module = Utils.Utils.ResolveModule(_model, moduleName);
            if (module == null)
            {
                var error = string.IsNullOrWhiteSpace(moduleName)
                    ? "No module found in ReadMicroflowDetails."
                    : $"Module '{moduleName}' not found.";
                _logger.LogError(error);
                SetLastError(error);
                return JsonSerializer.Serialize(new { error });
            }

            var microflow = module.GetDocuments()
                .OfType<IMicroflow>()
                .FirstOrDefault(mf => mf.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));

            if (microflow == null)
            {
                var error = $"Microflow '{microflowName}' not found in module '{module.Name}'";
                SetLastError(error);
                return JsonSerializer.Serialize(new { error });
            }

            var microflowService = _serviceProvider?.GetService<IMicroflowService>();

            // --- Parameters ---
            var parametersInfo = new List<object>();
            if (microflowService != null)
            {
                try
                {
                    var parameters = microflowService.GetParameters(microflow);
                    foreach (var param in parameters)
                    {
                        parametersInfo.Add(new
                        {
                            name = param.Name,
                            type = FormatDataType(param.Type),
                            documentation = string.IsNullOrEmpty(param.Documentation) ? null : param.Documentation
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not retrieve parameters for microflow '{MicroflowName}'", microflowName);
                }
            }

            // --- Activities ---
            var activitiesInfo = new List<object>();
            if (microflowService != null)
            {
                try
                {
                    var activities = microflowService.GetAllMicroflowActivities(microflow);
                    for (int i = 0; i < activities.Count; i++)
                    {
                        var activity = activities[i];
                        if (activity is IActionActivity actionActivity)
                        {
                            var details = SerializeActivityAction(actionActivity);
                            details["position"] = i + 1;
                            activitiesInfo.Add(details);
                        }
                        else
                        {
                            activitiesInfo.Add(new
                            {
                                position = i + 1,
                                actionType = "non_action",
                                typeName = activity.GetType().Name
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not retrieve activities for microflow '{MicroflowName}'", microflowName);
                }
            }

            // --- Return type ---
            var returnType = FormatDataType(microflow.ReturnType);

            var microflowInfo = new
            {
                name = microflow.Name,
                qualifiedName = microflow.QualifiedName?.FullName ?? "Unknown",
                module = module.Name,
                returnType = returnType,
                returnVariableName = microflow.ReturnVariableName,
                url = string.IsNullOrEmpty(microflow.Url) ? null : microflow.Url,
                parameterCount = parametersInfo.Count,
                parameters = parametersInfo,
                activityCount = activitiesInfo.Count,
                activities = activitiesInfo
            };

            return JsonSerializer.Serialize(new { success = true, microflow = microflowInfo });
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

        public async Task<object> GetStudioProLogs(JsonObject arguments)
        {
            try
            {
                var level = arguments?["level"]?.ToString()?.ToUpperInvariant() ?? "ERROR";
                var lastNMinutes = 30;
                if (arguments?["last_minutes"] != null && int.TryParse(arguments["last_minutes"]?.ToString(), out var mins))
                    lastNMinutes = mins;

                var logEntries = new List<object>();

                // Read Studio Pro log file
                var studioProLogPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Mendix", "log", "11.5.0", "log.txt");

                if (System.IO.File.Exists(studioProLogPath))
                {
                    var cutoff = DateTime.Now.AddMinutes(-lastNMinutes);
                    // Read with sharing since Studio Pro has this file open
                    using var fs = new System.IO.FileStream(studioProLogPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                    using var reader = new System.IO.StreamReader(fs);
                    string? line;
                    var multiLineBuffer = new System.Text.StringBuilder();
                    string? currentTimestamp = null;
                    string? currentLevel = null;
                    string? currentSource = null;

                    while ((line = reader.ReadLine()) != null)
                    {
                        // Parse log line: "2026-02-20 01:18:03.0029 INFO Mendix.Something Message here"
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)\s+(INFO|WARN|ERROR|DEBUG)\s+(\S+)\s+(.*)$");
                        if (match.Success)
                        {
                            // Flush previous entry
                            if (currentTimestamp != null && ShouldIncludeLogEntry(currentLevel, level))
                            {
                                if (DateTime.TryParse(currentTimestamp, out var ts) && ts >= cutoff)
                                {
                                    logEntries.Add(new { timestamp = currentTimestamp, level = currentLevel, source = currentSource, message = multiLineBuffer.ToString().TrimEnd() });
                                }
                            }

                            currentTimestamp = match.Groups[1].Value;
                            currentLevel = match.Groups[2].Value;
                            currentSource = match.Groups[3].Value;
                            multiLineBuffer.Clear();
                            multiLineBuffer.AppendLine(match.Groups[4].Value);
                        }
                        else if (currentTimestamp != null)
                        {
                            // Continuation line (stack trace, etc.)
                            multiLineBuffer.AppendLine(line);
                        }
                    }

                    // Flush last entry
                    if (currentTimestamp != null && ShouldIncludeLogEntry(currentLevel, level))
                    {
                        if (DateTime.TryParse(currentTimestamp, out var ts) && ts >= cutoff)
                        {
                            logEntries.Add(new { timestamp = currentTimestamp, level = currentLevel, source = currentSource, message = multiLineBuffer.ToString().TrimEnd() });
                        }
                    }
                }

                // Also read our MCP debug log for extension-specific errors
                var mcpErrors = new List<object>();
                var mcpLogPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "..", "..", "Mendix Projects", "Sample", "resources", "mcp_debug.log");

                // Try project directory path first
                try
                {
                    var project = _model?.Root as Mendix.StudioPro.ExtensionsAPI.Model.Projects.IProject;
                    if (project?.DirectoryPath != null)
                    {
                        mcpLogPath = System.IO.Path.Combine(project.DirectoryPath, "resources", "mcp_debug.log");
                    }
                }
                catch { /* ignore */ }

                if (System.IO.File.Exists(mcpLogPath))
                {
                    var cutoff = DateTime.Now.AddMinutes(-lastNMinutes);
                    using var fs = new System.IO.FileStream(mcpLogPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                    using var reader = new System.IO.StreamReader(fs);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("exception", StringComparison.OrdinalIgnoreCase) || line.Contains("fail", StringComparison.OrdinalIgnoreCase))
                        {
                            var tsMatch = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)\]");
                            if (tsMatch.Success && DateTime.TryParse(tsMatch.Groups[1].Value, out var ts) && ts >= cutoff)
                            {
                                mcpErrors.Add(new { timestamp = tsMatch.Groups[1].Value, source = "MCP Extension", message = line });
                            }
                        }
                    }
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    studioProLogPath = studioProLogPath,
                    filter = new { level = level, lastMinutes = lastNMinutes },
                    studioProEntries = logEntries.Count > 0 ? logEntries : null,
                    mcpExtensionErrors = mcpErrors.Count > 0 ? mcpErrors : null,
                    summary = new
                    {
                        studioProLogCount = logEntries.Count,
                        mcpErrorCount = mcpErrors.Count,
                        totalIssues = logEntries.Count + mcpErrors.Count
                    },
                    message = (logEntries.Count + mcpErrors.Count) == 0
                        ? $"No {level} entries found in the last {lastNMinutes} minutes."
                        : $"Found {logEntries.Count} Studio Pro log entries and {mcpErrors.Count} MCP extension errors."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Studio Pro logs");
                return JsonSerializer.Serialize(new { error = $"Failed to read logs: {ex.Message}" });
            }
        }

        private static bool ShouldIncludeLogEntry(string? entryLevel, string filterLevel)
        {
            if (string.IsNullOrEmpty(entryLevel)) return false;
            return filterLevel switch
            {
                "ERROR" => entryLevel == "ERROR",
                "WARN" => entryLevel == "ERROR" || entryLevel == "WARN",
                "INFO" => entryLevel == "ERROR" || entryLevel == "WARN" || entryLevel == "INFO",
                "ALL" => true,
                _ => entryLevel == "ERROR"
            };
        }

        /// <summary>
        /// Runs mx.exe check against the project MPR file to get real Studio Pro consistency errors.
        /// This provides structured error/warning output with error codes and locations.
        /// </summary>
        public async Task<object> CheckProjectErrors(JsonObject arguments)
        {
            try
            {
                var studioProVersion = arguments?["studio_pro_version"]?.ToString();

                // Find the MPR file path
                string? mprPath = null;
                if (!string.IsNullOrEmpty(_projectDirectory))
                {
                    // Search for .mpr files in the project directory
                    var mprFiles = Directory.GetFiles(_projectDirectory, "*.mpr", SearchOption.TopDirectoryOnly);
                    if (mprFiles.Length > 0)
                    {
                        mprPath = mprFiles[0];
                    }
                }

                if (string.IsNullOrEmpty(mprPath))
                {
                    return new { success = false, message = "Could not find .mpr file in project directory" };
                }

                if (!File.Exists(mprPath))
                {
                    return new { success = false, message = $"MPR file not found: {mprPath}" };
                }

                // Find mx.exe
                string? mxPath = null;
                string mendixDir = @"C:\Program Files\Mendix";

                if (!string.IsNullOrEmpty(studioProVersion))
                {
                    mxPath = Path.Combine(mendixDir, studioProVersion, "modeler", "mx.exe");
                    if (!File.Exists(mxPath))
                    {
                        return new { success = false, message = $"mx.exe not found for version {studioProVersion} at {mxPath}" };
                    }
                }
                else
                {
                    // Auto-detect: first try to match the running Studio Pro process version
                    try
                    {
                        var studioProProcesses = System.Diagnostics.Process.GetProcessesByName("studiopro");
                        foreach (var proc in studioProProcesses)
                        {
                            try
                            {
                                var procPath = proc.MainModule?.FileName;
                                if (!string.IsNullOrEmpty(procPath))
                                {
                                    // Extract version from path like C:\Program Files\Mendix\11.5.0\modeler\studiopro.exe
                                    var modelerDir = Path.GetDirectoryName(procPath);
                                    var versionDir = Path.GetDirectoryName(modelerDir);
                                    var version = Path.GetFileName(versionDir);
                                    if (!string.IsNullOrEmpty(version) && System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+"))
                                    {
                                        var candidate = Path.Combine(mendixDir, version, "modeler", "mx.exe");
                                        if (File.Exists(candidate))
                                        {
                                            mxPath = candidate;
                                            _logger.LogInformation($"Auto-detected mx.exe from running Studio Pro process: {version}");
                                            break;
                                        }
                                    }
                                }
                            }
                            catch { /* ignore per-process errors */ }
                        }
                    }
                    catch { /* ignore process enumeration errors */ }

                    // Fallback: find latest installed version
                    if (string.IsNullOrEmpty(mxPath))
                    {
                        try
                        {
                            if (Directory.Exists(mendixDir))
                            {
                                var dirs = Directory.GetDirectories(mendixDir)
                                    .Select(d => Path.GetFileName(d))
                                    .Where(d => System.Text.RegularExpressions.Regex.IsMatch(d, @"^\d+\.\d+"))
                                    .OrderByDescending(d =>
                                    {
                                        var parts = d.Split('.').Select(p => { int.TryParse(p, out int v); return v; }).ToArray();
                                        long val = 0;
                                        if (parts.Length >= 1) val += parts[0] * 10000000L;
                                        if (parts.Length >= 2) val += parts[1] * 100000L;
                                        if (parts.Length >= 3) val += parts[2] * 1000L;
                                        if (parts.Length >= 4) val += parts[3];
                                        return val;
                                    })
                                    .ToList();

                                foreach (var dir in dirs)
                                {
                                    var candidate = Path.Combine(mendixDir, dir, "modeler", "mx.exe");
                                    if (File.Exists(candidate))
                                    {
                                        mxPath = candidate;
                                        break;
                                    }
                                }
                            }
                        }
                        catch { /* ignore directory access errors */ }
                    }

                    if (string.IsNullOrEmpty(mxPath))
                    {
                        return new { success = false, message = "Could not find mx.exe. Please specify studio_pro_version (e.g., '11.5.0')." };
                    }
                }

                _logger.LogInformation($"Running mx check: {mxPath} check \"{mprPath}\"");

                // Run mx.exe check
                string output;
                try
                {
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = mxPath,
                        Arguments = $"check \"{mprPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = System.Diagnostics.Process.Start(processInfo);
                    if (process == null)
                    {
                        return new { success = false, message = "Failed to start mx.exe process" };
                    }

                    var stdoutTask = process.StandardOutput.ReadToEndAsync();
                    var stderrTask = process.StandardError.ReadToEndAsync();

                    // Wait up to 120 seconds
                    var completed = process.WaitForExit(120000);
                    if (!completed)
                    {
                        process.Kill();
                        return new { success = false, message = "mx.exe check timed out after 120 seconds" };
                    }

                    output = await stdoutTask + await stderrTask;
                }
                catch (Exception ex)
                {
                    return new { success = false, message = $"Failed to run mx.exe check: {ex.Message}" };
                }

                // Parse the output
                var lines = output.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();
                var errors = new List<object>();
                var warnings = new List<object>();
                string? mprVersion = null;

                var errorPattern = new System.Text.RegularExpressions.Regex(
                    @"^\[(error|warning)\]\s*\[([^\]]+)\]\s*""([^""]+)""(?:\s*at\s*(.+))?$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                var versionPattern = new System.Text.RegularExpressions.Regex(
                    @"The mpr file version is '([^']+)'");

                foreach (var line in lines)
                {
                    var versionMatch = versionPattern.Match(line);
                    if (versionMatch.Success)
                    {
                        mprVersion = versionMatch.Groups[1].Value;
                        continue;
                    }

                    var errorMatch = errorPattern.Match(line);
                    if (errorMatch.Success)
                    {
                        var entry = new
                        {
                            type = errorMatch.Groups[1].Value.ToLowerInvariant(),
                            code = errorMatch.Groups[2].Value,
                            message = errorMatch.Groups[3].Value,
                            location = errorMatch.Groups[4].Success ? errorMatch.Groups[4].Value : "Unknown"
                        };

                        if (entry.type == "error")
                            errors.Add(entry);
                        else
                            warnings.Add(entry);
                    }
                }

                _logger.LogInformation($"mx check completed: {errors.Count} errors, {warnings.Count} warnings");

                return new
                {
                    success = errors.Count == 0,
                    mprPath,
                    mprVersion,
                    mxVersion = mxPath != null ? Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(mxPath))) : null,
                    errorCount = errors.Count,
                    warningCount = warnings.Count,
                    errors,
                    warnings,
                    rawOutput = output.Length > 5000 ? output.Substring(0, 5000) + "... (truncated)" : output
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check project errors");
                return new { success = false, message = $"Error checking project: {ex.Message}" };
            }
        }

        public async Task<object> ListAvailableTools(JsonObject arguments)
        {
            try
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
                    "list_nanoflows",
                    "list_scheduled_events",
                    "list_rest_services",
                    "query_model_elements"
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

            var debugModuleName = arguments?["module_name"]?.ToString();
            var module = Utils.Utils.ResolveModule(_model, debugModuleName);
            if (module == null)
            {
                var error = string.IsNullOrWhiteSpace(debugModuleName) ? "No module found in DebugInfo." : $"Module '{debugModuleName}' not found.";
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

                    // Add microflow, constant, and enumeration counts
                    var microflows = module.GetDocuments().OfType<IMicroflow>().ToList();
                    response["microflowCount"] = microflows.Count;
                    response["microflows"] = microflows.Select(mf => mf.Name).ToList();

                    try
                    {
                        var constants = _model.Root.GetModuleDocuments<Mendix.StudioPro.ExtensionsAPI.Model.Constants.IConstant>(module);
                        response["constantCount"] = constants.Count;
                        response["constants"] = constants.Select(c => new { name = c.Name, defaultValue = c.DefaultValue }).ToList();
                    }
                    catch { response["constantCount"] = "N/A"; }

                    try
                    {
                        var enumerations = _model.Root.GetModuleDocuments<Mendix.StudioPro.ExtensionsAPI.Model.Enumerations.IEnumeration>(module);
                        response["enumerationCount"] = enumerations.Count;
                        response["enumerations"] = enumerations.Select(e => new
                        {
                            name = e.Name,
                            values = e.GetValues().Select(v => v.Name).ToList()
                        }).ToList();
                    }
                    catch { response["enumerationCount"] = "N/A"; }

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

                var mfModuleName = arguments["module_name"]?.ToString();
                var module = Utils.Utils.ResolveModule(_model, mfModuleName);
                if (module == null)
                {
                    var error = string.IsNullOrWhiteSpace(mfModuleName) ? "No module found." : $"Module '{mfModuleName}' not found.";
                    SetLastError(error);
                    _logger.LogError($"[create_microflow] {error}");
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
                        var paramEntityStr = paramObj["entity"]?.ToString();
                        if (string.IsNullOrWhiteSpace(paramName) || string.IsNullOrWhiteSpace(paramTypeStr))
                        {
                            _logger.LogError($"[create_microflow] Parameter missing name or type: {paramObj}");
                            continue;
                        }

                        // Handle Object and List parameter types with entity reference
                        Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType dataType;
                        var normalizedParamType = paramTypeStr.Trim().ToLowerInvariant();
                        if ((normalizedParamType == "object" || normalizedParamType == "list") && !string.IsNullOrWhiteSpace(paramEntityStr))
                        {
                            var (paramEntity, _) = Utils.Utils.FindEntityAcrossModules(_model, paramEntityStr);
                            if (paramEntity != null)
                            {
                                dataType = normalizedParamType == "object"
                                    ? Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.Object(paramEntity.QualifiedName)
                                    : Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.List(paramEntity.QualifiedName);
                            }
                            else
                            {
                                _logger.LogWarning($"[create_microflow] Entity '{paramEntityStr}' not found for param '{paramName}', defaulting to String");
                                dataType = Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.String;
                            }
                        }
                        else
                        {
                            dataType = Utils.Utils.DataTypeFromString(paramTypeStr);
                        }
                        paramList.Add((paramName, dataType));
                    }
                }

                // Prepare return value with proper expressions
                var returnTypeStr = arguments["returnType"]?.ToString();
                var returnEntityStr = arguments["returnEntity"]?.ToString();
                Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType returnType = Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.Void;

                // Only set a non-void return type if explicitly specified and meaningful
                if (!string.IsNullOrWhiteSpace(returnTypeStr) &&
                    !returnTypeStr.Trim().Equals("void", StringComparison.OrdinalIgnoreCase) &&
                    !returnTypeStr.Trim().Equals("", StringComparison.OrdinalIgnoreCase))
                {
                    // BUG-002 fix: Handle List and Object return types that require entity reference
                    var normalizedReturnType = returnTypeStr.Trim().ToLowerInvariant();
                    if ((normalizedReturnType == "list" || normalizedReturnType == "object") && !string.IsNullOrWhiteSpace(returnEntityStr))
                    {
                        var (returnEntity, _) = Utils.Utils.FindEntityAcrossModules(_model, returnEntityStr);
                        if (returnEntity != null)
                        {
                            returnType = normalizedReturnType == "list"
                                ? Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.List(returnEntity.QualifiedName)
                                : Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.Object(returnEntity.QualifiedName);
                            _logger.LogInformation($"[create_microflow] Created {normalizedReturnType} return type for entity '{returnEntityStr}'");
                        }
                        else
                        {
                            _logger.LogWarning($"[create_microflow] Entity '{returnEntityStr}' not found for {normalizedReturnType} return type, falling back to String");
                            returnType = Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.String;
                        }
                    }
                    else
                    {
                        returnType = Utils.Utils.DataTypeFromString(returnTypeStr);
                    }
                }

                _logger.LogInformation($"[create_microflow] Return type string: '{returnTypeStr ?? "null"}', entity: '{returnEntityStr ?? "null"}', resolved to: {returnType}");

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
                        // BUG-002 fix: For List/Object types, try without expression (use empty)
                        _logger.LogWarning(ex, $"[create_microflow] Failed to create return value with expression, trying with 'empty': {ex.Message}");
                        try
                        {
                            var microflowExpressionService = serviceProvider.GetRequiredService<IMicroflowExpressionService>();
                            var expression = microflowExpressionService.CreateFromString("empty");
                            returnValue = new Mendix.StudioPro.ExtensionsAPI.Model.Microflows.MicroflowReturnValue(returnType, expression);
                            _logger.LogInformation($"[create_microflow] Created return value for {returnType} with 'empty' expression");
                        }
                        catch (Exception ex2)
                        {
                            _logger.LogError(ex2, $"[create_microflow] Failed to create return value for {returnType}");
                            var error = $"Failed to create return value for type {returnType}: {ex2.Message}";
                            SetLastError(error, ex2);
                            return JsonSerializer.Serialize(new { error });
                        }
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

        /// <summary>
        /// Normalizes Mendix expression strings by replacing double quotes with single quotes.
        /// Mendix expressions use single-quoted string literals ('Hello'). AI agents frequently
        /// pass double-quoted strings ("Hello") which cause CE0117 parse errors.
        /// Double quotes are never valid in Mendix expression syntax, so this replacement is safe.
        /// </summary>
        private static string NormalizeMendixExpression(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                return expression;
            return expression.Replace('"', '\'');
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

                // BUG-005 fix: If no nested activity_config, use arguments itself as config (flat format)
                if (activityData == null && activityType != null)
                {
                    activityData = new JsonObject();
                    foreach (var prop in arguments)
                    {
                        if (prop.Key != "activity_type" && prop.Key != "microflow_name" &&
                            prop.Key != "module_name" && prop.Key != "insert_position" &&
                            prop.Key != "insert_after_activity_index")
                        {
                            activityData[prop.Key] = prop.Value?.DeepClone();
                        }
                    }
                }

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
                    // BUG-004 fix: Check if user used 'type' instead of 'activity_type'
                    var possibleType = arguments["type"]?.ToString();
                    var error = !string.IsNullOrWhiteSpace(possibleType)
                        ? $"Activity type is required. Did you mean 'activity_type' instead of 'type'? Found type='{possibleType}'. Use 'activity_type' as the field name."
                        : "Activity type is required. Use the 'activity_type' field to specify the type (e.g., 'create_object', 'retrieve_from_database', 'microflow_call').";
                    _logger.LogError($"ERROR: {error} - activityType was null/empty/whitespace");
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                var actModuleName = arguments["module_name"]?.ToString();
                var module = Utils.Utils.ResolveModule(_model, actModuleName);
                if (module == null)
                {
                    var error = string.IsNullOrWhiteSpace(actModuleName) ? "No module found." : $"Module '{actModuleName}' not found.";
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
                        case "commit_objects":
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

                        // Phase 11: Advanced list operations
                        case "union_lists":
                        case "union":
                            activity = CreateBinaryListOperationActivity<IUnion>(activityData);
                            break;

                        case "subtract_lists":
                        case "subtract":
                            activity = CreateBinaryListOperationActivity<ISubtract>(activityData);
                            break;

                        case "intersect_lists":
                        case "intersect":
                            activity = CreateBinaryListOperationActivity<IIntersect>(activityData);
                            break;

                        case "contains_in_list":
                        case "contains":
                            activity = CreateBinaryListOperationActivity<IContains>(activityData);
                            break;

                        case "head_of_list":
                        case "head":
                            activity = CreateUnaryListOperationActivity<IHead>(activityData);
                            break;

                        case "tail_of_list":
                        case "tail":
                            activity = CreateUnaryListOperationActivity<ITail>(activityData);
                            break;

                        case "reduce_list":
                        case "reduce":
                            activity = CreateReduceListActivity(activityData);
                            break;

                        default:
                            var supportedTypes = new[]
                            {
                                "create_object/create_variable", "microflow_call/call_microflow", "change_variable/change_value",
                                "retrieve_from_database", "retrieve_by_association", "commit_object/commit_objects/commit", "rollback_object/rollback",
                                "delete_object/delete", "create_list/new_list", "change_list/modify_list", "sort_list", "filter_list",
                                "find_in_list", "aggregate_list", "java_action_call", "change_attribute", "change_association", "change_object",
                                "union_lists/union", "subtract_lists/subtract", "intersect_lists/intersect",
                                "contains_in_list/contains", "head_of_list/head", "tail_of_list/tail", "reduce_list/reduce"
                            };
                            
                            var error = $"Unsupported activity type: '{activityType}'. " +
                                       $"Supported types: {string.Join(", ", supportedTypes)}. " +
                                       $"Note: For object changes, use 'change_object' (auto-detects), 'change_attribute' (for attributes), or 'change_association' (for references).";
                            
                            SetLastError(error);
                            return JsonSerializer.Serialize(new { error, supportedTypes });
                    }

                    if (activity == null)
                    {
                        // BUG-008 fix: Preserve specific error from the activity handler
                        var specificError = _lastError;
                        var availableParams = activityData?.AsObject()?.Select(kv => $"{kv.Key}={kv.Value}") ?? new string[0];
                        var paramsString = availableParams.Any() ? $" Available parameters: {string.Join(", ", availableParams)}" : " No parameters provided.";

                        var error = $"Failed to create activity of type '{activityType}'.";
                        if (!string.IsNullOrEmpty(specificError))
                        {
                            error += $" Detail: {specificError}";
                        }
                        else
                        {
                            error += paramsString;
                            if (activityType == "log" || activityType == "log_message")
                            {
                                error += " Log activities are not supported by the current Mendix Extensions API. Consider using change_variable or create_variable instead.";
                            }
                            else if (activityType == "delete" || activityType == "delete_object")
                            {
                                error += " For delete activities, ensure you specify the object variable using one of: variable_name, variableName, variable, objectVariable, object_variable, or object.";
                            }
                            else
                            {
                                error += " Please check the activity configuration and try again.";
                            }
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
                var message = activityData?["message"]?.ToString() ?? "'Log message'";
                var level = activityData?["level"]?.ToString() ?? "Info";

                // The Mendix Extensions API does not expose a CreateLogMessageActivity method.
                // As a workaround, create a change_variable activity that stores the log message,
                // or inform the caller that log_message is not supported.
                _logger.LogWarning($"Log activities are not directly supported by the Extensions API. Requested log: [{level}] {message}");
                SetLastError($"log_message activity is not supported by the Mendix Studio Pro Extensions API. " +
                    $"The API does not expose LogMessageAction creation. " +
                    $"Workaround: Use a Java action call to write logs, or add log messages manually in Studio Pro. " +
                    $"Requested: [{level}] {message}");
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
                _logger.LogInformation("CreateChangeVariableActivity called - analyzing parameters to determine if this is attribute or association change");

                // Check if this looks like an association change
                var associationName = activityData?["association_name"]?.ToString() ?? 
                                     activityData?["associationName"]?.ToString() ?? 
                                     activityData?["association"]?.ToString();

                var attributeName = activityData?["attribute_name"]?.ToString() ?? 
                                   activityData?["attributeName"]?.ToString() ?? 
                                   activityData?["attribute"]?.ToString();

                if (!string.IsNullOrEmpty(associationName))
                {
                    _logger.LogInformation($"Detected association change operation for association '{associationName}' - delegating to CreateChangeAssociationActivity");
                    return CreateChangeAssociationActivity(activityData);
                }
                else if (!string.IsNullOrEmpty(attributeName))
                {
                    _logger.LogInformation($"Detected attribute change operation for attribute '{attributeName}' - delegating to CreateChangeAttributeActivity");
                    return CreateChangeAttributeActivity(activityData);
                }
                else
                {
                    // Legacy fallback: assume attribute change and try to infer from variable name
                    var variableName = activityData?["variable_name"]?.ToString() ?? "newVariable";
                    var newValue = activityData?["new_value"]?.ToString() ?? "''";

                    _logger.LogWarning($"No explicit attribute or association specified in change_variable activity. This is a legacy usage pattern. " +
                                      $"For proper Change Object functionality, please use 'change_attribute' or 'change_association' activity types instead. " +
                                      $"Attempting to create a generic change activity for variable '{variableName}' with value '{newValue}'.");

                    // Try to create a basic change attribute activity with inferred parameters
                    var inferredActivityData = new JsonObject
                    {
                        ["object_variable"] = variableName,
                        ["attribute"] = "Name", // Default attribute - this is a guess
                        ["new_value"] = newValue,
                        ["change_type"] = "set",
                        ["commit"] = "no"
                    };

                    _logger.LogInformation("Attempting to create change attribute activity with inferred parameters. This may fail if the attribute doesn't exist.");
                    
                    // This may fail, but that's expected for legacy usage without proper configuration
                    try
                    {
                        return CreateChangeAttributeActivity(inferredActivityData);
                    }
                    catch (Exception inferEx)
                    {
                        var error = $"Failed to create change variable activity. For Change Object operations, please use 'change_attribute' or 'change_association' activity types with proper configuration. " +
                                   $"Legacy change_variable usage failed: {inferEx.Message}";
                        _logger.LogError(error);
                        SetLastError(error, inferEx);
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreateChangeVariableActivity");
                SetLastError($"Error creating change variable activity: {ex.Message}", ex);
                return null;
            }
        }

        private IActionActivity? CreateCreateVariableActivity(JsonObject? activityData)
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                var microflowExpressionService = _serviceProvider?.GetService<IMicroflowExpressionService>();

                var variableName = activityData?["variableName"]?.ToString() ??
                                  activityData?["variable_name"]?.ToString() ?? "newVariable";
                var entityType = activityData?["entity"]?.ToString() ??
                                activityData?["entityType"]?.ToString() ??
                                activityData?["entityName"]?.ToString() ??
                                activityData?["variable_type"]?.ToString() ?? "String";

                // Parse commit and refresh options
                var commitStr = activityData?["commit"]?.ToString()?.ToLowerInvariant() ?? "no";
                var refreshInClient = bool.Parse(activityData?["refresh_in_client"]?.ToString() ?? "false");

                var commit = commitStr switch
                {
                    "yes" => Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.CommitEnum.Yes,
                    "yes_without_events" => Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.CommitEnum.YesWithoutEvents,
                    _ => Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.CommitEnum.No
                };

                _logger.LogInformation($"Creating create object activity: variable='{variableName}', entity='{entityType}'");

                // Find entity
                var (entity, _) = Utils.Utils.FindEntityAcrossModules(_model, entityType);
                if (entity == null)
                {
                    // Fallback to old approach if entity not found
                    _logger.LogWarning($"Entity '{entityType}' not found. Creating basic create action.");
                    var createAction = _model.Create<ICreateObjectAction>();
                    createAction.OutputVariableName = variableName;
                    var activity = _model.Create<IActionActivity>();
                    activity.Action = createAction;
                    return activity;
                }

                // Use the service method if available (supports initial values)
                if (microflowActivitiesService != null && microflowExpressionService != null)
                {
                    // Parse initial values from activity config
                    var initialValues = new List<(string attribute, Mendix.StudioPro.ExtensionsAPI.Model.MicroflowExpressions.IMicroflowExpression valueExpression)>();
                    var initValuesNode = activityData?["initial_values"]?.AsArray() ??
                                        activityData?["initialValues"]?.AsArray() ??
                                        activityData?["values"]?.AsArray();

                    if (initValuesNode != null)
                    {
                        foreach (var val in initValuesNode)
                        {
                            if (val is JsonObject valObj)
                            {
                                var attrName = valObj["attribute"]?.ToString() ?? valObj["name"]?.ToString();
                                var valueExpr = valObj["value"]?.ToString() ?? valObj["expression"]?.ToString();
                                if (!string.IsNullOrEmpty(attrName) && !string.IsNullOrEmpty(valueExpr))
                                {
                                    var expr = microflowExpressionService.CreateFromString(NormalizeMendixExpression(valueExpr));
                                    initialValues.Add((attrName, expr));
                                }
                            }
                        }
                    }

                    _logger.LogInformation($"Using service CreateCreateObjectActivity with {initialValues.Count} initial values");
                    return microflowActivitiesService.CreateCreateObjectActivity(
                        _model, entity, variableName, commit, refreshInClient,
                        initialValues.ToArray());
                }
                else
                {
                    // Fallback: direct creation without service
                    var createAction = _model.Create<ICreateObjectAction>();
                    createAction.OutputVariableName = variableName;
                    createAction.Entity = entity.QualifiedName;
                    var activity = _model.Create<IActionActivity>();
                    activity.Action = createAction;
                    return activity;
                }
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
                var moduleName = activityData?["module_name"]?.ToString();

                if (string.IsNullOrEmpty(microflowName))
                {
                    _logger.LogError("Microflow name is required for microflow call activity");
                    SetLastError("Microflow name is required for microflow call activity");
                    return null;
                }

                // Find the target microflow across all modules
                IMicroflow? targetMicroflow = null;

                // Handle qualified name format "Module.MicroflowName"
                if (microflowName.Contains('.'))
                {
                    var parts = microflowName.Split('.', 2);
                    moduleName = parts[0];
                    microflowName = parts[1];
                }

                if (!string.IsNullOrEmpty(moduleName))
                {
                    var module = Utils.Utils.GetModuleByName(_model, moduleName);
                    if (module != null)
                    {
                        targetMicroflow = module.GetDocuments()
                            .OfType<IMicroflow>()
                            .FirstOrDefault(mf => mf.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));
                    }
                }
                else
                {
                    // Search all non-AppStore modules
                    foreach (var module in Utils.Utils.GetAllNonAppStoreModules(_model))
                    {
                        targetMicroflow = module.GetDocuments()
                            .OfType<IMicroflow>()
                            .FirstOrDefault(mf => mf.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));
                        if (targetMicroflow != null) break;
                    }
                }

                if (targetMicroflow == null)
                {
                    var error = $"Target microflow '{microflowName}' not found" +
                        (!string.IsNullOrEmpty(moduleName) ? $" in module '{moduleName}'" : " in any module");
                    _logger.LogError(error);
                    SetLastError(error);
                    return null;
                }

                // Create microflow call action
                var microflowCallAction = _model.Create<IMicroflowCallAction>();
                var microflowCall = _model.Create<IMicroflowCall>();

                // Set the target microflow via QualifiedName
                microflowCall.Microflow = targetMicroflow.QualifiedName;
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

                // Handle parameter mappings if provided
                var parametersArray = activityData?["parameters"]?.AsArray();
                if (parametersArray != null && parametersArray.Count > 0)
                {
                    var microflowService = _serviceProvider?.GetService<IMicroflowService>();
                    var microflowExpressionService = _serviceProvider?.GetService<IMicroflowExpressionService>();

                    if (microflowService != null && microflowExpressionService != null)
                    {
                        var targetParams = microflowService.GetParameters(targetMicroflow);

                        foreach (var paramNode in parametersArray)
                        {
                            var paramName = paramNode?["name"]?.ToString();
                            var paramValue = paramNode?["value"]?.ToString();

                            if (string.IsNullOrEmpty(paramName) || string.IsNullOrEmpty(paramValue))
                                continue;

                            var targetParam = targetParams.FirstOrDefault(p =>
                                p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));

                            if (targetParam != null)
                            {
                                var paramMapping = _model.Create<IMicroflowCallParameterMapping>();
                                paramMapping.Parameter = targetParam.QualifiedName;
                                paramMapping.Argument = microflowExpressionService.CreateFromString(NormalizeMendixExpression(paramValue));
                                microflowCall.AddParameterMapping(paramMapping);
                                _logger.LogInformation($"Mapped parameter '{paramName}' = '{paramValue}'");
                            }
                            else
                            {
                                _logger.LogWarning($"Parameter '{paramName}' not found in target microflow '{targetMicroflow.Name}'");
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("IMicroflowService or IMicroflowExpressionService not available - skipping parameter mappings");
                    }
                }

                // Create the action activity
                var activity = _model.Create<IActionActivity>();
                activity.Action = microflowCallAction;

                _logger.LogInformation($"Created microflow call activity for microflow '{targetMicroflow.Name}' (qualified: {targetMicroflow.QualifiedName}) with return variable '{returnVariable ?? "none"}'");

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

        /// <summary>
        /// Determines the optimal range (first/all) based on XPath constraint and variable naming patterns.
        /// </summary>
        /// <param name="xpath">XPath constraint string</param>
        /// <param name="outputVariable">Output variable name</param>
        /// <returns>Recommended range: "first" or "all"</returns>
        private string DetermineOptimalRange(string? xpath, string outputVariable)
        {
            try
            {
                // Default to "all" for safety
                string recommendedRange = "all";

                // Analyze XPath patterns that typically indicate single record lookup
                if (!string.IsNullOrEmpty(xpath))
                {
                    var xpathLower = xpath.ToLowerInvariant();
                    
                    // Look for ID-based constraints which typically return single records
                    if (xpathLower.Contains("id =") || 
                        xpathLower.Contains("id=") ||
                        xpathLower.Contains("[id =") ||
                        xpathLower.Contains("[id="))
                    {
                        recommendedRange = "first";
                        _logger.LogInformation($"Detected ID-based XPath constraint: '{xpath}' - recommending 'first' range");
                    }
                    // Look for unique key constraints
                    else if (xpathLower.Contains("email =") || 
                             xpathLower.Contains("email=") ||
                             xpathLower.Contains("username =") ||
                             xpathLower.Contains("username=") ||
                             xpathLower.Contains("code =") ||
                             xpathLower.Contains("code="))
                    {
                        recommendedRange = "first";
                        _logger.LogInformation($"Detected unique key constraint: '{xpath}' - recommending 'first' range");
                    }
                }

                // Analyze variable naming patterns
                var variableLower = outputVariable.ToLowerInvariant();
                if (variableLower.StartsWith("retrieved") && !variableLower.Contains("list") && !variableLower.Contains("collection"))
                {
                    // Variable names like "RetrievedCustomer" suggest single object
                    if (!variableLower.EndsWith("s") && !variableLower.Contains("objects"))
                    {
                        recommendedRange = "first";
                        _logger.LogInformation($"Variable name '{outputVariable}' suggests single object - recommending 'first' range");
                    }
                }

                _logger.LogInformation($"Determined optimal range: '{recommendedRange}' (XPath: '{xpath}', Variable: '{outputVariable}')");
                return recommendedRange;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error in DetermineOptimalRange, defaulting to 'all': {ex.Message}");
                return "all";
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
                               activityData["XPath"]?.ToString() ??
                               activityData["xpath_constraint"]?.ToString() ??
                               activityData["xpathConstraint"]?.ToString() ??
                               activityData["constraint"]?.ToString();

                string outputVariable;
                
                // If no explicit output variable specified, create one based on entity name
                if (activityData.ContainsKey("outputVariable") || activityData.ContainsKey("output") || 
                    activityData.ContainsKey("output_variable") || activityData.ContainsKey("variableName"))
                {
                    outputVariable = activityData["outputVariable"]?.ToString() ??
                                   activityData["output"]?.ToString() ??
                                   activityData["output_variable"]?.ToString() ??
                                   activityData["variableName"]?.ToString() ?? "RetrievedObjects";
                }
                else
                {
                    // Create intelligent variable name based on entity
                    var autoEntityName = entityName.Contains('.') ? entityName.Split('.').Last() : entityName;
                    outputVariable = $"Retrieved{autoEntityName}";
                    _logger.LogInformation($"Auto-generated output variable name: '{outputVariable}' for entity '{entityName}'");
                }

                // Smart range detection with explicit override capability
                string range = activityData["range"]?.ToString()?.ToLowerInvariant() ?? DetermineOptimalRange(xpath, outputVariable);
                
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
                    var allEntities = Utils.Utils.GetAllNonAppStoreModules(_model)
                        .SelectMany(m => m.DomainModel?.GetEntities() ?? Enumerable.Empty<IEntity>())
                        .Select(e => e.Name).ToList();
                    
                    string availableEntities = allEntities.Any() ? 
                        string.Join(", ", allEntities) : "No entities found";
                    
                    string error = $"Entity name is required for database retrieve activity. Available entities: {availableEntities}";
                    _logger.LogError(error);
                    SetLastError(error, new ArgumentException("Missing entity name"));
                    return null;
                }

                // Find the entity across all modules
                // If entityName contains a dot, extract the simple name (e.g., "MyFirstModule.Customer" -> "Customer")
                var simpleEntityName = entityName.Contains('.') ? entityName.Split('.').Last() : entityName;

                // Try to find by simple name across all modules
                var (entity, foundModule) = Utils.Utils.FindEntityAcrossModules(_model, simpleEntityName);

                // If not found and original entityName contained a dot, try qualified name match
                if (entity == null && entityName.Contains('.'))
                {
                    foreach (var mod in Utils.Utils.GetAllNonAppStoreModules(_model))
                    {
                        entity = mod.DomainModel?.GetEntities()
                            .FirstOrDefault(e => e.QualifiedName.ToString().Equals(entityName, StringComparison.OrdinalIgnoreCase));
                        if (entity != null) break;
                    }
                }

                if (entity == null)
                {
                    var availableEntities = Utils.Utils.GetAllNonAppStoreModules(_model)
                        .SelectMany(m => m.DomainModel?.GetEntities() ?? Enumerable.Empty<IEntity>())
                        .Select(e => $"{e.Name} (qualified: {e.QualifiedName})")
                        .ToList();

                    string availableEntitiesStr = availableEntities.Any() ?
                        string.Join(", ", availableEntities) : "No entities found";

                    string error = $"Entity '{entityName}' not found in any module. Tried simple name '{simpleEntityName}' and qualified name '{entityName}'. Available entities: {availableEntitiesStr}";
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
                                      activityData["entity_variable"]?.ToString() ??
                                      throw new ArgumentException("Input variable (entity_variable) is required");

                // Find the association by searching all entities across all modules
                IAssociation? association = null;
                foreach (var module in Utils.Utils.GetAllNonAppStoreModules(_model))
                {
                    if (module.DomainModel == null) continue;
                    foreach (var entity in module.DomainModel.GetEntities())
                    {
                        var entityAssociations = entity.GetAssociations(AssociationDirection.Both, null);
                        var match = entityAssociations.FirstOrDefault(ea =>
                            ea.Association.Name.Equals(associationName, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            association = match.Association;
                            _logger.LogInformation($"Found association '{associationName}' between '{match.Parent.Name}' and '{match.Child.Name}'");
                            break;
                        }
                    }
                    if (association != null) break;
                }

                if (association == null)
                {
                    SetLastError($"Association '{associationName}' not found in any module");
                    return null;
                }

                _logger.LogInformation($"Creating association retrieve: association='{associationName}', input='{inputVariable}', output='{outputVariable}'");
                return microflowActivitiesService.CreateAssociationRetrieveSourceActivity(
                    _model, association, outputVariable, inputVariable);
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

                // Try multiple parameter name variations for better UX
                string? variableName = activityData["variable_name"]?.ToString() ?? 
                                      activityData["variableName"]?.ToString() ?? 
                                      activityData["variable"]?.ToString() ??
                                      activityData["objectVariable"]?.ToString() ??
                                      activityData["object_variable"]?.ToString() ??
                                      activityData["object"]?.ToString();

                // Support for multiple objects (array format)
                var objectsArray = activityData["objects"]?.AsArray() ?? 
                                  activityData["commit_objects"]?.AsArray() ??
                                  activityData["variables"]?.AsArray();

                // If no single variable but we have an objects array, use the first one
                if (string.IsNullOrEmpty(variableName) && objectsArray?.Count > 0)
                {
                    variableName = objectsArray[0]?.ToString();
                    if (objectsArray.Count > 1)
                    {
                        _logger?.LogWarning($"Multiple objects specified for commit [{string.Join(", ", objectsArray.Select(o => o?.ToString()))}], using first one: {variableName}. Consider creating separate commit activities for each object.");
                    }
                }

                if (string.IsNullOrEmpty(variableName))
                {
                    var supportedParams = new[] { "variable_name", "variable", "object", "object_variable", "objects (array)", "commit_objects (array)" };
                    SetLastError($"Variable name is required for commit activity. Supported parameter names: {string.Join(", ", supportedParams)}.\n\nExample usage:\n{{\n  \"activity_type\": \"commit\",\n  \"activity_config\": {{\n    \"variable\": \"Customer\",\n    \"with_events\": true\n  }}\n}}");
                    return null;
                }

                bool refreshInClient = bool.Parse(activityData["refresh_in_client"]?.ToString() ?? 
                                                 activityData["refreshInClient"]?.ToString() ?? 
                                                 activityData["refresh"]?.ToString() ?? "true");

                bool withEvents = bool.Parse(activityData["with_events"]?.ToString() ?? 
                                           activityData["withEvents"]?.ToString() ?? 
                                           activityData["events"]?.ToString() ?? "true");

                _logger?.LogInformation($"Creating commit activity: variable='{variableName}', withEvents={withEvents}, refreshInClient={refreshInClient}");

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

                // Log all available parameters for debugging
                var availableParams = activityData?.AsObject()?.Select(kv => $"{kv.Key}={kv.Value}") ?? new string[0];
                _logger.LogInformation($"[CreateDeleteActivity] Available parameters: {string.Join(", ", availableParams)}");

                string variableName = activityData["variable_name"]?.ToString() ?? 
                                     activityData["variableName"]?.ToString() ?? 
                                     activityData["variable"]?.ToString() ??
                                     activityData["objectVariable"]?.ToString() ??
                                     activityData["object_variable"]?.ToString() ??
                                     activityData["object"]?.ToString() ??
                                     throw new ArgumentException("Variable name is required for delete. Please specify one of: variable_name, variableName, variable, objectVariable, object_variable, or object in the activity_config.");

                _logger.LogInformation($"[CreateDeleteActivity] Using variable name: '{variableName}'");

                return microflowActivitiesService.CreateDeleteObjectActivity(_model, variableName);
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create delete activity: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region List Operations - Full Implementation

        /// <summary>
        /// Creates a create list activity (empty list of a given entity type).
        /// </summary>
        private IActionActivity? CreateListActivity(JsonObject? activityData)
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                if (microflowActivitiesService == null)
                {
                    SetLastError("IMicroflowActivitiesService not available");
                    return null;
                }

                string entityName = activityData?["entity_name"]?.ToString() ??
                                   activityData?["entityName"]?.ToString() ??
                                   activityData?["entity"]?.ToString() ??
                                   throw new ArgumentException("Entity name is required for create list activity. Use 'entity' or 'entity_name'.");

                string outputVariable = activityData?["output_variable"]?.ToString() ??
                                       activityData?["outputVariable"]?.ToString() ??
                                       activityData?["variable_name"]?.ToString() ??
                                       $"{entityName}List";

                var (entity, _) = Utils.Utils.FindEntityAcrossModules(_model, entityName);
                if (entity == null)
                {
                    SetLastError($"Entity '{entityName}' not found in any module for create list activity");
                    return null;
                }

                _logger.LogInformation($"Creating create list activity: entity='{entityName}', output='{outputVariable}', entityQN='{entity.QualifiedName}'");
                var activity = microflowActivitiesService.CreateCreateListActivity(_model, entity, outputVariable);
                if (activity == null)
                {
                    SetLastError($"CreateCreateListActivity returned null for entity '{entityName}', output '{outputVariable}'");
                }
                return activity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to create list activity: entity='{activityData?["entity"]?.ToString()}', error={ex.Message}");
                SetLastError($"Failed to create list activity: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Creates a sort list activity. Sorts an existing list variable by one or more attributes.
        /// </summary>
        private IActionActivity? CreateSortListActivity(JsonObject? activityData)
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                if (microflowActivitiesService == null)
                {
                    SetLastError("IMicroflowActivitiesService not available");
                    return null;
                }

                string listVariable = activityData?["list_variable"]?.ToString() ??
                                     activityData?["listVariable"]?.ToString() ??
                                     activityData?["variable_name"]?.ToString() ??
                                     throw new ArgumentException("List variable name is required for sort list activity");

                string outputVariable = activityData?["output_variable"]?.ToString() ??
                                       activityData?["outputVariable"]?.ToString() ??
                                       $"Sorted{listVariable}";

                string entityName = activityData?["entity_name"]?.ToString() ??
                                   activityData?["entityName"]?.ToString() ??
                                   activityData?["entity"]?.ToString() ??
                                   throw new ArgumentException("Entity name is required to resolve sort attributes");

                var (entity, _) = Utils.Utils.FindEntityAcrossModules(_model, entityName);
                if (entity == null)
                {
                    SetLastError($"Entity '{entityName}' not found for sort list activity");
                    return null;
                }

                // Parse sort_by: can be array of {attribute, descending} or a single attribute name
                var sortings = new List<Mendix.StudioPro.ExtensionsAPI.Model.Microflows.AttributeSorting>();
                var sortByArray = activityData?["sort_by"]?.AsArray();

                if (sortByArray != null && sortByArray.Count > 0)
                {
                    foreach (var sortItem in sortByArray)
                    {
                        string? attrName = null;
                        bool descending = false;

                        if (sortItem is JsonObject sortObj)
                        {
                            attrName = sortObj["attribute"]?.ToString() ??
                                      sortObj["attribute_name"]?.ToString();
                            descending = bool.Parse(sortObj["descending"]?.ToString() ?? "false") ||
                                        (sortObj["direction"]?.ToString()?.ToLowerInvariant() == "desc");
                        }
                        else
                        {
                            attrName = sortItem?.ToString();
                        }

                        if (string.IsNullOrEmpty(attrName)) continue;

                        var attr = entity.GetAttributes()
                            .FirstOrDefault(a => a.Name.Equals(attrName, StringComparison.OrdinalIgnoreCase));
                        if (attr == null)
                        {
                            SetLastError($"Attribute '{attrName}' not found on entity '{entityName}' for sorting");
                            return null;
                        }
                        sortings.Add(new Mendix.StudioPro.ExtensionsAPI.Model.Microflows.AttributeSorting(attr, descending));
                    }
                }
                else
                {
                    // Single attribute sort
                    string? attrName = activityData?["attribute"]?.ToString() ??
                                     activityData?["attribute_name"]?.ToString();
                    bool descending = bool.Parse(activityData?["descending"]?.ToString() ?? "false") ||
                                    (activityData?["direction"]?.ToString()?.ToLowerInvariant() == "desc");

                    if (string.IsNullOrEmpty(attrName))
                    {
                        SetLastError("At least one attribute is required for sort list. Use 'attribute' or 'sort_by' array.");
                        return null;
                    }

                    var attr = entity.GetAttributes()
                        .FirstOrDefault(a => a.Name.Equals(attrName, StringComparison.OrdinalIgnoreCase));
                    if (attr == null)
                    {
                        SetLastError($"Attribute '{attrName}' not found on entity '{entityName}' for sorting");
                        return null;
                    }
                    sortings.Add(new Mendix.StudioPro.ExtensionsAPI.Model.Microflows.AttributeSorting(attr, descending));
                }

                _logger.LogInformation($"Creating sort list activity: list='{listVariable}', output='{outputVariable}', sortBy=[{string.Join(", ", sortings.Select(s => $"{s.Attribute.Name} {(s.SortByDescending ? "DESC" : "ASC")}"))}]");
                return microflowActivitiesService.CreateSortListActivity(
                    _model, listVariable, outputVariable, sortings.ToArray());
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create sort list activity: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Creates a filter list activity. Filters a list by attribute value using an expression.
        /// </summary>
        private IActionActivity? CreateFilterListActivity(JsonObject? activityData)
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                var microflowExpressionService = _serviceProvider?.GetService<IMicroflowExpressionService>();
                if (microflowActivitiesService == null || microflowExpressionService == null)
                {
                    SetLastError("IMicroflowActivitiesService or IMicroflowExpressionService not available");
                    return null;
                }

                string listVariable = activityData?["list_variable"]?.ToString() ??
                                     activityData?["listVariable"]?.ToString() ??
                                     activityData?["variable_name"]?.ToString() ??
                                     throw new ArgumentException("List variable name is required for filter list activity");

                string outputVariable = activityData?["output_variable"]?.ToString() ??
                                       activityData?["outputVariable"]?.ToString() ??
                                       $"Filtered{listVariable}";

                string filterExpr = activityData?["filter_expression"]?.ToString() ??
                                   activityData?["filterExpression"]?.ToString() ??
                                   activityData?["expression"]?.ToString() ??
                                   throw new ArgumentException("Filter expression is required for filter list activity");

                string entityName = activityData?["entity_name"]?.ToString() ??
                                   activityData?["entityName"]?.ToString() ??
                                   activityData?["entity"]?.ToString() ??
                                   throw new ArgumentException("Entity name is required to resolve filter attribute");

                string attributeName = activityData?["attribute_name"]?.ToString() ??
                                     activityData?["attributeName"]?.ToString() ??
                                     activityData?["attribute"]?.ToString() ??
                                     throw new ArgumentException("Attribute name is required for filter list by attribute");

                var (entity, _) = Utils.Utils.FindEntityAcrossModules(_model, entityName);
                if (entity == null)
                {
                    SetLastError($"Entity '{entityName}' not found for filter list activity");
                    return null;
                }

                var attribute = entity.GetAttributes()
                    .FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                if (attribute == null)
                {
                    SetLastError($"Attribute '{attributeName}' not found on entity '{entityName}' for filtering");
                    return null;
                }

                var expression = microflowExpressionService.CreateFromString(NormalizeMendixExpression(filterExpr));

                _logger.LogInformation($"Creating filter list activity: list='{listVariable}', output='{outputVariable}', attr='{attributeName}', expr='{filterExpr}'");
                return microflowActivitiesService.CreateFilterListByAttributeActivity(
                    _model, attribute, listVariable, outputVariable, expression);
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create filter list activity: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Creates a find in list activity. Finds a single item by attribute or expression.
        /// </summary>
        private IActionActivity? CreateFindInListActivity(JsonObject? activityData)
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                var microflowExpressionService = _serviceProvider?.GetService<IMicroflowExpressionService>();
                if (microflowActivitiesService == null || microflowExpressionService == null)
                {
                    SetLastError("IMicroflowActivitiesService or IMicroflowExpressionService not available");
                    return null;
                }

                string listVariable = activityData?["list_variable"]?.ToString() ??
                                     activityData?["listVariable"]?.ToString() ??
                                     activityData?["variable_name"]?.ToString() ??
                                     throw new ArgumentException("List variable name is required for find in list activity");

                string outputVariable = activityData?["output_variable"]?.ToString() ??
                                       activityData?["outputVariable"]?.ToString() ??
                                       "FoundItem";

                string findExpr = activityData?["find_expression"]?.ToString() ??
                                 activityData?["findExpression"]?.ToString() ??
                                 activityData?["expression"]?.ToString() ??
                                 throw new ArgumentException("Find expression is required for find in list activity");

                // Determine if we are finding by attribute or by expression
                string? attributeName = activityData?["attribute_name"]?.ToString() ??
                                       activityData?["attributeName"]?.ToString() ??
                                       activityData?["attribute"]?.ToString();

                string? entityName = activityData?["entity_name"]?.ToString() ??
                                    activityData?["entityName"]?.ToString() ??
                                    activityData?["entity"]?.ToString();

                var expression = microflowExpressionService.CreateFromString(NormalizeMendixExpression(findExpr));

                // If attribute is specified, use FindByAttribute
                if (!string.IsNullOrEmpty(attributeName) && !string.IsNullOrEmpty(entityName))
                {
                    var (entity, _) = Utils.Utils.FindEntityAcrossModules(_model, entityName);
                    if (entity == null)
                    {
                        SetLastError($"Entity '{entityName}' not found for find in list activity");
                        return null;
                    }

                    var attribute = entity.GetAttributes()
                        .FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                    if (attribute == null)
                    {
                        SetLastError($"Attribute '{attributeName}' not found on entity '{entityName}' for find");
                        return null;
                    }

                    _logger.LogInformation($"Creating find by attribute activity: list='{listVariable}', output='{outputVariable}', attr='{attributeName}', expr='{findExpr}'");
                    return microflowActivitiesService.CreateFindByAttributeActivity(
                        _model, attribute, listVariable, outputVariable, expression);
                }
                else
                {
                    // Use FindByExpression (no attribute needed)
                    _logger.LogInformation($"Creating find by expression activity: list='{listVariable}', output='{outputVariable}', expr='{findExpr}'");
                    return microflowActivitiesService.CreateFindByExpressionActivity(
                        _model, listVariable, outputVariable, expression);
                }
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create find in list activity: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Creates an aggregate list activity. Supports count, sum, average, min, max, all, any.
        /// Can aggregate by attribute, by expression, or simple (count).
        /// </summary>
        private IActionActivity? CreateAggregateListActivity(JsonObject? activityData)
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                var microflowExpressionService = _serviceProvider?.GetService<IMicroflowExpressionService>();
                if (microflowActivitiesService == null)
                {
                    SetLastError("IMicroflowActivitiesService not available");
                    return null;
                }

                string listVariable = activityData?["list_variable"]?.ToString() ??
                                     activityData?["listVariable"]?.ToString() ??
                                     activityData?["variable_name"]?.ToString() ??
                                     throw new ArgumentException("List variable name is required for aggregate list activity");

                string outputVariable = activityData?["output_variable"]?.ToString() ??
                                       activityData?["outputVariable"]?.ToString() ??
                                       "AggregateResult";

                string functionStr = activityData?["function"]?.ToString()?.ToLowerInvariant() ??
                                    activityData?["aggregate_function"]?.ToString()?.ToLowerInvariant() ??
                                    activityData?["aggregateFunction"]?.ToString()?.ToLowerInvariant() ??
                                    "count";

                // Convert function string to enum
                Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.AggregateFunctionEnum aggregateFunction;
                switch (functionStr)
                {
                    case "sum": aggregateFunction = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.AggregateFunctionEnum.Sum; break;
                    case "average": case "avg": aggregateFunction = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.AggregateFunctionEnum.Average; break;
                    case "count": aggregateFunction = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.AggregateFunctionEnum.Count; break;
                    case "minimum": case "min": aggregateFunction = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.AggregateFunctionEnum.Minimum; break;
                    case "maximum": case "max": aggregateFunction = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.AggregateFunctionEnum.Maximum; break;
                    case "all": aggregateFunction = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.AggregateFunctionEnum.All; break;
                    case "any": aggregateFunction = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.AggregateFunctionEnum.Any; break;
                    default:
                        SetLastError($"Unknown aggregate function '{functionStr}'. Supported: sum, average, count, minimum, maximum, all, any");
                        return null;
                }

                // Check if aggregating by attribute
                string? attributeName = activityData?["attribute_name"]?.ToString() ??
                                       activityData?["attributeName"]?.ToString() ??
                                       activityData?["attribute"]?.ToString();

                string? entityName = activityData?["entity_name"]?.ToString() ??
                                    activityData?["entityName"]?.ToString() ??
                                    activityData?["entity"]?.ToString();

                // Check if aggregating by expression
                string? expressionStr = activityData?["expression"]?.ToString() ??
                                       activityData?["aggregate_expression"]?.ToString();

                if (!string.IsNullOrEmpty(attributeName) && !string.IsNullOrEmpty(entityName))
                {
                    // Aggregate by attribute
                    var (entity, _) = Utils.Utils.FindEntityAcrossModules(_model, entityName);
                    if (entity == null)
                    {
                        SetLastError($"Entity '{entityName}' not found for aggregate list activity");
                        return null;
                    }

                    var attribute = entity.GetAttributes()
                        .FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                    if (attribute == null)
                    {
                        SetLastError($"Attribute '{attributeName}' not found on entity '{entityName}' for aggregation");
                        return null;
                    }

                    _logger.LogInformation($"Creating aggregate list by attribute activity: list='{listVariable}', output='{outputVariable}', attr='{attributeName}', func='{functionStr}'");
                    return microflowActivitiesService.CreateAggregateListByAttributeActivity(
                        _model, attribute, listVariable, outputVariable, aggregateFunction);
                }
                else if (!string.IsNullOrEmpty(expressionStr) && microflowExpressionService != null)
                {
                    // Aggregate by expression
                    var expression = microflowExpressionService.CreateFromString(NormalizeMendixExpression(expressionStr));

                    _logger.LogInformation($"Creating aggregate list by expression activity: list='{listVariable}', output='{outputVariable}', expr='{expressionStr}', func='{functionStr}'");
                    return microflowActivitiesService.CreateAggregateListByExpressionActivity(
                        _model, expression, listVariable, outputVariable, aggregateFunction);
                }
                else
                {
                    // Simple aggregate (count, etc.)
                    _logger.LogInformation($"Creating simple aggregate list activity: list='{listVariable}', output='{outputVariable}', func='{functionStr}'");
                    return microflowActivitiesService.CreateAggregateListActivity(
                        _model, listVariable, outputVariable, aggregateFunction);
                }
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create aggregate list activity: {ex.Message}", ex);
                return null;
            }
        }

        // Placeholder - java_action_call needs full parameter mapping support
        private IActionActivity? CreateJavaActionCallActivity(JsonObject? activityData) => null;

        // Phase 11: Binary list operations (union, subtract, intersect, contains)
        private IActionActivity? CreateBinaryListOperationActivity<T>(JsonObject? activityData) where T : class, IBinaryListOperation
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                if (microflowActivitiesService == null)
                {
                    SetLastError("IMicroflowActivitiesService not available");
                    return null;
                }

                string listVariable = activityData?["list_variable"]?.ToString() ??
                                     activityData?["listVariable"]?.ToString() ??
                                     throw new ArgumentException("list_variable is required");

                string secondVariable = activityData?["second_list_variable"]?.ToString() ??
                                       activityData?["secondListVariable"]?.ToString() ??
                                       activityData?["second_variable"]?.ToString() ??
                                       throw new ArgumentException("second_list_variable is required");

                string outputVariable = activityData?["output_variable"]?.ToString() ??
                                       activityData?["outputVariable"]?.ToString() ??
                                       "ListOperationResult";

                var operation = _model.Create<T>();
                ((IBinaryListOperation)operation).SecondListOrObjectVariableName = secondVariable;

                _logger.LogInformation($"Creating {typeof(T).Name} list operation: list='{listVariable}', second='{secondVariable}', output='{outputVariable}'");
                return microflowActivitiesService.CreateListOperationActivity(
                    _model, listVariable, outputVariable, (IListOperation)operation);
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create {typeof(T).Name} activity: {ex.Message}", ex);
                return null;
            }
        }

        // Phase 11: Unary list operations (head, tail)
        private IActionActivity? CreateUnaryListOperationActivity<T>(JsonObject? activityData) where T : class, IListOperation
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                if (microflowActivitiesService == null)
                {
                    SetLastError("IMicroflowActivitiesService not available");
                    return null;
                }

                string listVariable = activityData?["list_variable"]?.ToString() ??
                                     activityData?["listVariable"]?.ToString() ??
                                     throw new ArgumentException("list_variable is required");

                string outputVariable = activityData?["output_variable"]?.ToString() ??
                                       activityData?["outputVariable"]?.ToString() ??
                                       "ListOperationResult";

                var operation = _model.Create<T>();

                _logger.LogInformation($"Creating {typeof(T).Name} list operation: list='{listVariable}', output='{outputVariable}'");
                return microflowActivitiesService.CreateListOperationActivity(
                    _model, listVariable, outputVariable, (IListOperation)operation);
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create {typeof(T).Name} activity: {ex.Message}", ex);
                return null;
            }
        }

        // Phase 11: Reduce list activity
        private IActionActivity? CreateReduceListActivity(JsonObject? activityData)
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                var microflowExpressionService = _serviceProvider?.GetService<IMicroflowExpressionService>();
                if (microflowActivitiesService == null || microflowExpressionService == null)
                {
                    SetLastError("IMicroflowActivitiesService or IMicroflowExpressionService not available");
                    return null;
                }

                string listVariable = activityData?["list_variable"]?.ToString() ??
                                     throw new ArgumentException("list_variable is required");

                string outputVariable = activityData?["output_variable"]?.ToString() ?? "ReduceResult";

                string initialValueExpr = activityData?["initial_value"]?.ToString() ??
                                         throw new ArgumentException("initial_value expression is required");

                string reduceExpr = activityData?["expression"]?.ToString() ??
                                   throw new ArgumentException("expression is required for reduce");

                string returnTypeStr = activityData?["return_type"]?.ToString()?.ToLowerInvariant() ?? "integer";

                var initialExpression = microflowExpressionService.CreateFromString(NormalizeMendixExpression(initialValueExpr));
                var expression = microflowExpressionService.CreateFromString(NormalizeMendixExpression(reduceExpr));

                DataType dataType;
                switch (returnTypeStr)
                {
                    case "integer": case "int": dataType = DataType.Integer; break;
                    case "decimal": dataType = DataType.Decimal; break;
                    case "boolean": case "bool": dataType = DataType.Boolean; break;
                    case "string": dataType = DataType.String; break;
                    case "float": dataType = DataType.Float; break;
                    default: dataType = DataType.Integer; break;
                }

                _logger.LogInformation($"Creating reduce list activity: list='{listVariable}', output='{outputVariable}', returnType='{returnTypeStr}'");
                return microflowActivitiesService.CreateReduceAggregateActivity(
                    _model, listVariable, outputVariable, initialExpression, expression, dataType);
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create reduce list activity: {ex.Message}", ex);
                return null;
            }
        }

        #endregion

        #region Change Object Activities - Proper Implementation

        /// <summary>
        /// Creates a change list activity using IMicroflowActivitiesService.
        /// </summary>
        /// <param name="activityData">Activity configuration data</param>
        /// <returns>IActionActivity for changing a list</returns>
        private IActionActivity? CreateChangeListActivity(JsonObject? activityData)
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                var microflowExpressionService = _serviceProvider?.GetService<IMicroflowExpressionService>();
                
                if (microflowActivitiesService == null)
                {
                    SetLastError("IMicroflowActivitiesService not available");
                    return null;
                }

                if (microflowExpressionService == null)
                {
                    SetLastError("IMicroflowExpressionService not available");
                    return null;
                }

                string listVariableName = activityData?["list_variable"]?.ToString() ?? 
                                         activityData?["listVariable"]?.ToString() ?? 
                                         activityData?["variable_name"]?.ToString() ?? 
                                         activityData?["variableName"]?.ToString() ?? 
                                         throw new ArgumentException("List variable name is required for change list activity");

                string operation = activityData?["operation"]?.ToString()?.ToLowerInvariant() ?? "add";
                string changeValueExpr = activityData?["change_value"]?.ToString() ?? 
                                        activityData?["changeValue"]?.ToString() ?? 
                                        activityData?["value"]?.ToString() ?? "empty";

                // Convert operation string to enum
                Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeListActionOperation operationEnum;
                switch (operation)
                {
                    case "add":
                        operationEnum = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeListActionOperation.Add;
                        break;
                    case "remove":
                        operationEnum = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeListActionOperation.Remove;
                        break;
                    case "clear":
                        operationEnum = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeListActionOperation.Clear;
                        break;
                    default:
                        operationEnum = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeListActionOperation.Add;
                        break;
                }

                // Create expression for the change value
                var changeExpression = microflowExpressionService.CreateFromString(NormalizeMendixExpression(changeValueExpr));

                return microflowActivitiesService.CreateChangeListActivity(
                    _model,
                    operationEnum,
                    listVariableName,
                    changeExpression
                );
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create change list activity: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Creates a change attribute activity using IMicroflowActivitiesService.CreateChangeAttributeActivity.
        /// This is the proper implementation using the official Mendix API.
        /// </summary>
        /// <param name="activityData">Activity configuration data</param>
        /// <returns>IActionActivity for changing an object attribute</returns>
        private IActionActivity? CreateChangeAttributeActivity(JsonObject? activityData)
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                var microflowExpressionService = _serviceProvider?.GetService<IMicroflowExpressionService>();
                
                if (microflowActivitiesService == null)
                {
                    SetLastError("IMicroflowActivitiesService not available");
                    return null;
                }

                if (microflowExpressionService == null)
                {
                    SetLastError("IMicroflowExpressionService not available");
                    return null;
                }

                // Extract parameters with multiple naming conventions
                string objectVariableName = activityData?["object_variable"]?.ToString() ?? 
                                           activityData?["objectVariable"]?.ToString() ?? 
                                           activityData?["variable_name"]?.ToString() ?? 
                                           activityData?["variableName"]?.ToString() ?? 
                                           activityData?["variable"]?.ToString() ?? 
                                           activityData?["object"]?.ToString() ?? 
                                           activityData?["change_variable"]?.ToString();

                if (string.IsNullOrEmpty(objectVariableName))
                {
                    var supportedParams = new[] { "object_variable", "variable", "object", "variable_name", "change_variable" };
                    SetLastError($"Object variable name is required for change attribute activity. Supported parameter names: {string.Join(", ", supportedParams)}.\n\nExample usage:\n{{\n  \"activity_type\": \"change_attribute\",\n  \"activity_config\": {{\n    \"variable\": \"Customer\",\n    \"attribute\": \"Name\",\n    \"value\": \"'New Value'\"\n  }}\n}}");
                    return null;
                }

                string attributeName = activityData?["attribute_name"]?.ToString() ?? 
                                      activityData?["attributeName"]?.ToString() ?? 
                                      activityData?["attribute"]?.ToString() ?? 
                                      throw new ArgumentException("Attribute name is required for change attribute activity");

                string entityName = activityData?["entity_name"]?.ToString() ?? 
                                   activityData?["entityName"]?.ToString() ?? 
                                   activityData?["entity"]?.ToString();

                string newValueExpr = activityData?["new_value"]?.ToString() ?? 
                                     activityData?["newValue"]?.ToString() ?? 
                                     activityData?["value"]?.ToString() ?? "empty";

                string changeTypeStr = activityData?["change_type"]?.ToString()?.ToLowerInvariant() ?? 
                                      activityData?["changeType"]?.ToString()?.ToLowerInvariant() ?? "set";

                string commitStr = activityData?["commit"]?.ToString()?.ToLowerInvariant() ?? "no";

                _logger.LogInformation($"Creating change attribute activity: object='{objectVariableName}', attribute='{attributeName}', entity='{entityName}', value='{newValueExpr}', changeType='{changeTypeStr}', commit='{commitStr}'");

                // Find the attribute in the domain model (search across all modules)
                IAttribute? attribute = null;

                // First try to find by entity name if provided
                if (!string.IsNullOrEmpty(entityName))
                {
                    var (entity, _) = Utils.Utils.FindEntityAcrossModules(_model, entityName);
                    if (entity != null)
                    {
                        attribute = entity.GetAttributes()
                            .FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                    }
                }

                // If not found by entity, search all entities across all modules
                if (attribute == null)
                {
                    foreach (var mod in Utils.Utils.GetAllNonAppStoreModules(_model))
                    {
                        if (mod.DomainModel == null) continue;
                        foreach (var entity in mod.DomainModel.GetEntities())
                        {
                            attribute = entity.GetAttributes()
                                .FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                            if (attribute != null)
                            {
                                _logger.LogInformation($"Found attribute '{attributeName}' in entity '{entity.Name}' (module '{mod.Name}')");
                                break;
                            }
                        }
                        if (attribute != null) break;
                    }
                }

                if (attribute == null)
                {
                    var availableAttributes = Utils.Utils.GetAllNonAppStoreModules(_model)
                        .SelectMany(m => m.DomainModel?.GetEntities() ?? Enumerable.Empty<IEntity>())
                        .SelectMany(e => e.GetAttributes().Select(a => $"{e.Name}.{a.Name}"))
                        .ToList();

                    var error = $"Attribute '{attributeName}' not found in any module. Available attributes: {string.Join(", ", availableAttributes)}";
                    SetLastError(error);
                    return null;
                }

                // Convert change type string to enum
                Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeActionItemType changeType;
                switch (changeTypeStr)
                {
                    case "set":
                        changeType = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeActionItemType.Set;
                        break;
                    case "add":
                        changeType = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeActionItemType.Add;
                        break;
                    case "remove":
                        changeType = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeActionItemType.Remove;
                        break;
                    default:
                        changeType = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeActionItemType.Set;
                        break;
                }

                // Convert commit string to enum
                Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.CommitEnum commit;
                switch (commitStr)
                {
                    case "yes":
                    case "true":
                        commit = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.CommitEnum.Yes;
                        break;
                    case "yeswithoutevents":
                    case "yes_without_events":
                        commit = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.CommitEnum.YesWithoutEvents;
                        break;
                    case "no":
                    case "false":
                    default:
                        commit = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.CommitEnum.No;
                        break;
                }

                // Create expression for the new value
                var newValueExpression = microflowExpressionService.CreateFromString(NormalizeMendixExpression(newValueExpr));

                // Use the official API method
                var activity = microflowActivitiesService.CreateChangeAttributeActivity(
                    _model,
                    attribute,
                    changeType,
                    newValueExpression,
                    objectVariableName,
                    commit
                );

                _logger.LogInformation($"Successfully created change attribute activity for '{attribute.Name}' on variable '{objectVariableName}'");
                return activity;
            }
            catch (Exception ex)
            {
                var error = $"Failed to create change attribute activity: {ex.Message}";
                _logger.LogError(ex, error);
                SetLastError(error, ex);
                return null;
            }
        }

        /// <summary>
        /// Creates a change association activity using IMicroflowActivitiesService.CreateChangeAssociationActivity.
        /// This is the proper implementation using the official Mendix API.
        /// </summary>
        /// <param name="activityData">Activity configuration data</param>
        /// <returns>IActionActivity for changing an object association</returns>
        private IActionActivity? CreateChangeAssociationActivity(JsonObject? activityData)
        {
            try
            {
                var microflowActivitiesService = _serviceProvider?.GetService<IMicroflowActivitiesService>();
                var microflowExpressionService = _serviceProvider?.GetService<IMicroflowExpressionService>();
                
                if (microflowActivitiesService == null)
                {
                    SetLastError("IMicroflowActivitiesService not available");
                    return null;
                }

                if (microflowExpressionService == null)
                {
                    SetLastError("IMicroflowExpressionService not available");
                    return null;
                }

                // Extract parameters with multiple naming conventions
                string objectVariableName = activityData?["object_variable"]?.ToString() ?? 
                                           activityData?["objectVariable"]?.ToString() ?? 
                                           activityData?["variable_name"]?.ToString() ?? 
                                           activityData?["variableName"]?.ToString() ?? 
                                           activityData?["change_variable"]?.ToString() ?? 
                                           throw new ArgumentException("Object variable name is required for change association activity");

                string associationName = activityData?["association_name"]?.ToString() ?? 
                                        activityData?["associationName"]?.ToString() ?? 
                                        activityData?["association"]?.ToString() ?? 
                                        throw new ArgumentException("Association name is required for change association activity");

                string newValueExpr = activityData?["new_value"]?.ToString() ?? 
                                     activityData?["newValue"]?.ToString() ?? 
                                     activityData?["value"]?.ToString() ?? "empty";

                string changeTypeStr = activityData?["change_type"]?.ToString()?.ToLowerInvariant() ?? 
                                      activityData?["changeType"]?.ToString()?.ToLowerInvariant() ?? "set";

                string commitStr = activityData?["commit"]?.ToString()?.ToLowerInvariant() ?? "no";

                _logger.LogInformation($"Creating change association activity: object='{objectVariableName}', association='{associationName}', value='{newValueExpr}', changeType='{changeTypeStr}', commit='{commitStr}'");

                // Find the association across all modules
                IAssociation? association = null;

                foreach (var mod in Utils.Utils.GetAllNonAppStoreModules(_model))
                {
                    if (mod.DomainModel == null) continue;
                    foreach (var entity in mod.DomainModel.GetEntities())
                    {
                        var entityAssociations = entity.GetAssociations(AssociationDirection.Both, null);
                        var foundAssociation = entityAssociations
                            .FirstOrDefault(ea => ea.Association.Name.Equals(associationName, StringComparison.OrdinalIgnoreCase));

                        if (foundAssociation != null)
                        {
                            association = foundAssociation.Association;
                            _logger.LogInformation($"Found association '{associationName}' between '{foundAssociation.Parent.Name}' and '{foundAssociation.Child.Name}' (module '{mod.Name}')");
                            break;
                        }
                    }
                    if (association != null) break;
                }

                if (association == null)
                {
                    var availableAssociations = new List<string>();
                    foreach (var mod in Utils.Utils.GetAllNonAppStoreModules(_model))
                    {
                        if (mod.DomainModel == null) continue;
                        foreach (var entity in mod.DomainModel.GetEntities())
                        {
                            var entityAssociations = entity.GetAssociations(AssociationDirection.Both, null);
                            availableAssociations.AddRange(entityAssociations.Select(ea => ea.Association.Name));
                        }
                    }

                    var error = $"Association '{associationName}' not found in any module. Available associations: {string.Join(", ", availableAssociations.Distinct())}";
                    SetLastError(error);
                    return null;
                }

                // Convert change type string to enum
                Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeActionItemType changeType;
                switch (changeTypeStr)
                {
                    case "set":
                        changeType = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeActionItemType.Set;
                        break;
                    case "add":
                        changeType = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeActionItemType.Add;
                        break;
                    case "remove":
                        changeType = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeActionItemType.Remove;
                        break;
                    default:
                        changeType = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.ChangeActionItemType.Set;
                        break;
                }

                // Convert commit string to enum
                Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.CommitEnum commit;
                switch (commitStr)
                {
                    case "yes":
                    case "true":
                        commit = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.CommitEnum.Yes;
                        break;
                    case "yeswithoutevents":
                    case "yes_without_events":
                        commit = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.CommitEnum.YesWithoutEvents;
                        break;
                    case "no":
                    case "false":
                    default:
                        commit = Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions.CommitEnum.No;
                        break;
                }

                // Create expression for the new value
                var newValueExpression = microflowExpressionService.CreateFromString(NormalizeMendixExpression(newValueExpr));

                // Use the official API method
                var activity = microflowActivitiesService.CreateChangeAssociationActivity(
                    _model,
                    association,
                    changeType,
                    newValueExpression,
                    objectVariableName,
                    commit
                );

                _logger.LogInformation($"Successfully created change association activity for '{association.Name}' on variable '{objectVariableName}'");
                return activity;
            }
            catch (Exception ex)
            {
                var error = $"Failed to create change association activity: {ex.Message}";
                _logger.LogError(ex, error);
                SetLastError(error, ex);
                return null;
            }
        }

        /// <summary>
        /// Creates a change object activity by analyzing the request and delegating to the appropriate specific handler.
        /// This provides a user-friendly interface that handles common change object scenarios automatically.
        /// </summary>
        /// <param name="activityData">Activity configuration data</param>
        /// <returns>IActionActivity for changing an object attribute or association</returns>
        private IActionActivity? CreateChangeObjectActivity(JsonObject? activityData)
        {
            try
            {
                _logger.LogInformation("CreateChangeObjectActivity called - analyzing request to determine change type");

                if (activityData == null)
                {
                    SetLastError("Activity data is required for change_object activity");
                    return null;
                }

                // Check for explicit change type specification
                var changeType = activityData["change_type"]?.ToString()?.ToLowerInvariant();
                if (changeType == "association" || changeType == "reference")
                {
                    _logger.LogInformation("Explicit association change type specified - delegating to CreateChangeAssociationActivity");
                    return CreateChangeAssociationActivity(activityData);
                }

                // Check if this looks like an association change
                var associationName = activityData["association_name"]?.ToString() ?? 
                                     activityData["associationName"]?.ToString() ?? 
                                     activityData["association"]?.ToString();

                if (!string.IsNullOrEmpty(associationName))
                {
                    _logger.LogInformation($"Association '{associationName}' specified - delegating to CreateChangeAssociationActivity");
                    return CreateChangeAssociationActivity(activityData);
                }

                // Check for changes specified as array (multiple attribute changes)
                var changesArray = activityData["changes"]?.AsArray();
                if (changesArray != null && changesArray.Count > 0)
                {
                    _logger.LogInformation($"Changes array with {changesArray.Count} items found - processing attribute changes");
                    
                    // For now, handle only single attribute change (most common case)
                    if (changesArray.Count == 1)
                    {
                        var change = changesArray[0]?.AsObject();
                        if (change != null)
                        {
                            // Convert array format to single attribute format
                            var convertedData = new JsonObject();
                            
                            // Copy all existing properties
                            foreach (var kvp in activityData)
                            {
                                if (kvp.Key != "changes")
                                {
                                    convertedData[kvp.Key] = kvp.Value?.DeepClone();
                                }
                            }
                            
                            // Add attribute-specific properties from the change
                            convertedData["attribute"] = change["attribute"]?.ToString() ?? change["attribute_name"]?.ToString();
                            convertedData["new_value"] = change["value"]?.ToString() ?? change["new_value"]?.ToString();
                            
                            _logger.LogInformation($"Converted changes array to single attribute change: {convertedData["attribute"]}");
                            return CreateChangeAttributeActivity(convertedData);
                        }
                    }
                    else
                    {
                        SetLastError("Multiple attribute changes in a single change_object activity are not yet supported. Please use separate change_attribute activities for each attribute.");
                        return null;
                    }
                }

                // Check for changes specified as object (key-value pairs)
                var changesObject = activityData["changes"]?.AsObject();
                if (changesObject != null && changesObject.Count > 0)
                {
                    _logger.LogInformation($"Changes object with {changesObject.Count} properties found - processing attribute changes");
                    
                    // For now, handle only single attribute change
                    if (changesObject.Count == 1)
                    {
                        var firstChange = changesObject.First();
                        var convertedData = new JsonObject();
                        
                        // Copy all existing properties
                        foreach (var kvp in activityData)
                        {
                            if (kvp.Key != "changes")
                            {
                                convertedData[kvp.Key] = kvp.Value?.DeepClone();
                            }
                        }
                        
                        // Add attribute-specific properties
                        convertedData["attribute"] = firstChange.Key;
                        convertedData["new_value"] = firstChange.Value?.ToString();
                        
                        _logger.LogInformation($"Converted changes object to single attribute change: {firstChange.Key}");
                        return CreateChangeAttributeActivity(convertedData);
                    }
                    else
                    {
                        SetLastError("Multiple attribute changes in a single change_object activity are not yet supported. Please use separate change_attribute activities for each attribute.");
                        return null;
                    }
                }

                // Check for direct attribute specification
                var attributeName = activityData["attribute_name"]?.ToString() ?? 
                                   activityData["attributeName"]?.ToString() ?? 
                                   activityData["attribute"]?.ToString();

                if (!string.IsNullOrEmpty(attributeName))
                {
                    _logger.LogInformation($"Direct attribute '{attributeName}' specified - delegating to CreateChangeAttributeActivity");
                    return CreateChangeAttributeActivity(activityData);
                }

                // If no specific change type detected, provide helpful error message
                var error = "Unable to determine change type for change_object activity. Please specify either:\n" +
                           "- For attribute changes: Use 'attribute' or 'changes' property\n" +
                           "- For association changes: Use 'association' property\n" +
                           "- Or use specific activity types: 'change_attribute' or 'change_association'\n" +
                           "\nExample formats:\n" +
                           "- Attribute: {\"attribute\": \"Name\", \"new_value\": \"'New Value'\"}\n" +
                           "- Changes object: {\"changes\": {\"Name\": \"'New Value'\"}}\n" +
                           "- Changes array: {\"changes\": [{\"attribute\": \"Name\", \"value\": \"'New Value'\"}]}\n" +
                           "- Association: {\"association\": \"Customer_Order\", \"new_value\": \"$NewOrder\"}";
                
                SetLastError(error);
                return null;
            }
            catch (Exception ex)
            {
                var error = $"Failed to create change object activity: {ex.Message}";
                _logger.LogError(ex, error);
                SetLastError(error, ex);
                return null;
            }
        }

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

                var seqModuleName = arguments["module_name"]?.ToString();
                var module = Utils.Utils.ResolveModule(_model, seqModuleName);
                if (module == null)
                {
                    var error = string.IsNullOrWhiteSpace(seqModuleName) ? "No module found." : $"Module '{seqModuleName}' not found.";
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
                
                // Variable name tracking for propagation across activities
                Dictionary<string, string> variableNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                
                // Debug logging to file
                var debugLogPath = GetDebugLogPath();
                await File.AppendAllTextAsync(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] VARIABLE TRACKING: Starting variable tracking for {activitiesArray.Count} activities\n");

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

                            // BUG-005 fix: If no nested activity_config, use the activity definition itself as config
                            // (support flat format where properties are at the same level as activity_type)
                            if (activityConfig == null && activityType != null)
                            {
                                // Clone the definition and remove the activity_type key to use as config
                                activityConfig = new JsonObject();
                                foreach (var prop in activityDef)
                                {
                                    if (prop.Key != "activity_type")
                                    {
                                        activityConfig[prop.Key] = prop.Value?.DeepClone();
                                    }
                                }
                            }

                            _logger.LogInformation($"Processing activity {i + 1}: type='{activityType}'");
                            await File.AppendAllTextAsync(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] VARIABLE TRACKING: Processing activity {i + 1}: type='{activityType}'\n");
                            await File.AppendAllTextAsync(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] VARIABLE TRACKING: Original config: {activityConfig?.ToJsonString()}\n");

                            if (string.IsNullOrWhiteSpace(activityType))
                            {
                                // BUG-004 fix: Check for common misname 'type' instead of 'activity_type'
                                var possibleType = activityDef["type"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(possibleType))
                                {
                                    _logger.LogWarning($"Activity at index {i} uses 'type' instead of 'activity_type'. Auto-correcting to '{possibleType}'.");
                                    activityType = possibleType;
                                }
                                else
                                {
                                    _logger.LogWarning($"Skipping activity at index {i} - no activity type specified. Use 'activity_type' field.");
                                    activityResults.Add(new { index = i + 1, type = (string?)null, status = "skipped", error = "No 'activity_type' field found. Use 'activity_type' (not 'type') to specify the activity." });
                                    continue;
                                }
                            }

                            // Apply variable name substitutions to activity config
                            await File.AppendAllTextAsync(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] VARIABLE TRACKING: Applying substitutions with {variableNameMap.Count} mappings\n");
                            var processedConfig = ApplyVariableNameSubstitutions(activityConfig, variableNameMap);
                            await File.AppendAllTextAsync(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] VARIABLE TRACKING: Processed config: {processedConfig?.ToJsonString()}\n");

                            // Create the activity (reuse existing logic)
                            IActionActivity? activity = CreateActivityByType(activityType, processedConfig);

                            if (activity != null)
                            {
                                createdActivities.Add(activity);
                                
                                // Track variable names for future activities
                                await File.AppendAllTextAsync(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] VARIABLE TRACKING: Tracking variables for activity type '{activityType}'\n");
                                TrackVariableNames(activityType, processedConfig, variableNameMap);
                                await File.AppendAllTextAsync(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] VARIABLE TRACKING: Variable map now has {variableNameMap.Count} entries: {string.Join(", ", variableNameMap.Select(kvp => $"{kvp.Key}→{kvp.Value}"))}\n");
                                
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
                            // BUG-008 fix: Don't overwrite specific error from individual activity handlers
                            var specificError = _lastError;
                            var error = !string.IsNullOrEmpty(specificError)
                                ? $"No activities were successfully created. Last error: {specificError}"
                                : "No activities were successfully created.";
                            // Only set if no specific error already exists
                            if (string.IsNullOrEmpty(specificError))
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
                case "commit_objects":
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

                case "change_object":
                    return CreateChangeObjectActivity(activityConfig);

                // Phase 11: Advanced list operations
                case "union_lists":
                case "union":
                    return CreateBinaryListOperationActivity<IUnion>(activityConfig);

                case "subtract_lists":
                case "subtract":
                    return CreateBinaryListOperationActivity<ISubtract>(activityConfig);

                case "intersect_lists":
                case "intersect":
                    return CreateBinaryListOperationActivity<IIntersect>(activityConfig);

                case "contains_in_list":
                case "contains":
                    return CreateBinaryListOperationActivity<IContains>(activityConfig);

                case "head_of_list":
                case "head":
                    return CreateUnaryListOperationActivity<IHead>(activityConfig);

                case "tail_of_list":
                case "tail":
                    return CreateUnaryListOperationActivity<ITail>(activityConfig);

                case "reduce_list":
                case "reduce":
                    return CreateReduceListActivity(activityConfig);

                default:
                    var supportedTypes = new[]
                    {
                        "log/log_message", "change_variable/change_value", "create_variable/create_object/create",
                        "microflow_call/call_microflow", "retrieve_from_database/retrieve_database/database_retrieve",
                        "retrieve_by_association/association_retrieve", "commit_object/commit", "rollback_object/rollback",
                        "delete_object/delete", "create_list/new_list", "change_list/modify_list", "sort_list", "filter_list",
                        "find_in_list/find_list_item", "aggregate_list/list_aggregate", "java_action_call/call_java_action",
                        "change_attribute", "change_association", "change_object",
                        "union_lists/union", "subtract_lists/subtract", "intersect_lists/intersect",
                        "contains_in_list/contains", "head_of_list/head", "tail_of_list/tail", "reduce_list/reduce"
                    };
                    
                    _logger.LogError($"Unsupported activity type: '{activityType}'. Supported types: {string.Join(", ", supportedTypes)}");
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

        #region Variable Name Tracking and Substitution

        /// <summary>
        /// Applies variable name substitutions to activity configuration based on tracked variables
        /// </summary>
        private JsonObject? ApplyVariableNameSubstitutions(JsonObject? activityConfig, Dictionary<string, string> variableNameMap)
        {
            if (activityConfig == null || variableNameMap.Count == 0)
            {
                var debugLogPath = GetDebugLogPath();
                File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: Early return - activityConfig null: {activityConfig == null}, variableNameMap count: {variableNameMap.Count}\n");
                return activityConfig;
            }

            try
            {
                var debugLogPath = GetDebugLogPath();
                File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: Starting substitutions\n");
                
                // Create a deep copy of the configuration to avoid modifying the original
                var configJson = activityConfig.ToJsonString();
                var processedConfig = JsonNode.Parse(configJson)?.AsObject();
                
                if (processedConfig == null)
                    return activityConfig;

                // Common variable name fields that might need substitution
                var variableFields = new[] 
                { 
                    "variable", "variableName", "variable_name", "inputVariable", "input_variable",
                    "objectVariable", "object_variable", "listVariable", "list_variable",
                    "sourceVariable", "source_variable", "targetVariable", "target_variable",
                    "object", "objects", "commit_objects", "variables"
                };

                _logger.LogInformation($"Applying variable substitutions with {variableNameMap.Count} mappings: {string.Join(", ", variableNameMap.Select(kvp => $"{kvp.Key}→{kvp.Value}"))}");

                foreach (var field in variableFields)
                {
                    if (processedConfig.ContainsKey(field))
                    {
                        var fieldValue = processedConfig[field];
                        File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: Found field '{field}' with value kind: {fieldValue?.GetValueKind()}\n");
                        
                        // Handle string fields
                        if (fieldValue?.GetValueKind() == JsonValueKind.String)
                        {
                            var currentValue = fieldValue.ToString();
                            File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: String field '{field}' has value '{currentValue}'\n");
                            
                            // Handle both plain variable names and $-prefixed variables
                            string lookupKey = currentValue;
                            if (currentValue.StartsWith("$"))
                            {
                                lookupKey = currentValue.Substring(1); // Remove $ prefix for lookup
                                File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: Found $-prefixed variable, lookup key: '{lookupKey}'\n");
                            }
                            
                            if (!string.IsNullOrEmpty(lookupKey) && variableNameMap.ContainsKey(lookupKey))
                            {
                                var actualVariableName = variableNameMap[lookupKey];
                                // For delete activities, we want just the variable name without $
                                processedConfig[field] = actualVariableName;
                                File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: ✅ Substituted variable '{currentValue}' with actual name '{actualVariableName}' in field '{field}'\n");
                                _logger.LogInformation($"Substituted variable '{currentValue}' with actual name '{actualVariableName}' in field '{field}'");
                            }
                        }
                        // Handle array fields (like "objects" in commit activities)
                        else if (fieldValue?.GetValueKind() == JsonValueKind.Array)
                        {
                            File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: Array field '{field}' has {fieldValue.AsArray().Count} elements\n");
                            var arrayValue = fieldValue.AsArray();
                            for (int i = 0; i < arrayValue.Count; i++)
                            {
                                var currentValue = arrayValue[i]?.ToString();
                                File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: Array element [{i}] = '{currentValue}'\n");
                                
                                if (!string.IsNullOrEmpty(currentValue))
                                {
                                    // Handle both plain variable names and $-prefixed variables
                                    string lookupKey = currentValue;
                                    if (currentValue.StartsWith("$"))
                                    {
                                        lookupKey = currentValue.Substring(1); // Remove $ prefix for lookup
                                    }
                                    
                                    if (variableNameMap.ContainsKey(lookupKey))
                                    {
                                        var actualVariableName = variableNameMap[lookupKey];
                                        // For activities that expect variable names, use just the name without $
                                        arrayValue[i] = actualVariableName;
                                        File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: ✅ Substituted array variable '{currentValue}' with actual name '{actualVariableName}' in field '{field}[{i}]'\n");
                                        _logger.LogInformation($"Substituted array variable '{currentValue}' with actual name '{actualVariableName}' in field '{field}[{i}]'");
                                    }
                                }
                            }
                        }
                    }
                }

                return processedConfig;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error applying variable name substitutions, using original config");
                return activityConfig;
            }
        }

        /// <summary>
        /// Tracks variable names created by activities for future reference
        /// </summary>
        private void TrackVariableNames(string activityType, JsonObject? activityConfig, Dictionary<string, string> variableNameMap)
        {
            if (activityConfig == null)
                return;

            try
            {
                var debugLogPath = GetDebugLogPath();
                File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] TRACK_VARIABLES: Processing activity type '{activityType}'\n");
                
                string? logicalName = null;
                string? actualName = null;

                switch (activityType.ToLowerInvariant())
                {
                    case "retrieve_from_database":
                    case "retrieve_database":
                    case "database_retrieve":
                        File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] TRACK_VARIABLES: Processing retrieve activity\n");
                        // For retrieve activities, track the mapping
                        logicalName = activityConfig["variable_name"]?.ToString();
                        
                        // Get the actual variable name that was used/created
                        actualName = activityConfig["outputVariable"]?.ToString() ?? 
                                   activityConfig["output"]?.ToString() ?? 
                                   activityConfig["output_variable"]?.ToString();
                        
                        // If no explicit output variable was specified, use the entity-based name  
                        if (string.IsNullOrEmpty(actualName))
                        {
                            var entityName = activityConfig["entityName"]?.ToString() ?? 
                                           activityConfig["entity"]?.ToString();
                            if (!string.IsNullOrEmpty(entityName))
                            {
                                var simpleEntityName = entityName.Contains('.') ? entityName.Split('.').Last() : entityName;
                                actualName = $"Retrieved{simpleEntityName}";
                            }
                            else
                            {
                                actualName = "RetrievedObjects";
                            }
                        }
                        break;

                    case "create_variable":
                    case "create_object":
                    case "create":
                        // For create activities
                        logicalName = activityConfig["variable_name"]?.ToString() ?? 
                                    activityConfig["variableName"]?.ToString();
                        actualName = logicalName; // Create activities typically use the specified name
                        break;

                    case "retrieve_by_association":
                    case "association_retrieve":
                        // For association retrieve activities
                        logicalName = activityConfig["variable_name"]?.ToString();
                        actualName = activityConfig["outputVariable"]?.ToString() ?? 
                                   activityConfig["output"]?.ToString() ?? 
                                   "AssociatedObjects";
                        break;

                    case "microflow_call":
                    case "call_microflow":
                        // For microflow calls that might return objects
                        logicalName = activityConfig["return_variable"]?.ToString() ?? 
                                    activityConfig["returnVariable"]?.ToString();
                        actualName = logicalName; // Microflow calls typically use the specified return variable name
                        break;
                }

                // Only track if we have both logical and actual names
                if (!string.IsNullOrEmpty(logicalName) && !string.IsNullOrEmpty(actualName))
                {
                    if (!logicalName.Equals(actualName, StringComparison.OrdinalIgnoreCase))
                    {
                        variableNameMap[logicalName] = actualName;
                        _logger.LogInformation($"Tracking variable mapping: '{logicalName}' -> '{actualName}'");
                    }
                    
                    // Also add self-mapping for the actual variable name for direct $-prefixed references
                    variableNameMap[actualName] = actualName;
                }
                    
                // For retrieve activities, also track entity-based logical names (e.g., "Customer" -> "RetrievedCustomer")
                if (activityType.ToLowerInvariant().Contains("retrieve") && !string.IsNullOrEmpty(actualName))
                {
                    var entityName = activityConfig["entityName"]?.ToString() ?? 
                                   activityConfig["entity"]?.ToString();
                    
                    if (!string.IsNullOrEmpty(entityName))
                    {
                        // Extract simple entity name (e.g., "MyFirstModule.Customer" -> "Customer")
                        var simpleEntityName = entityName.Contains('.') ? entityName.Split('.').Last() : entityName;
                        
                        if (!simpleEntityName.Equals(actualName, StringComparison.OrdinalIgnoreCase))
                        {
                            variableNameMap[simpleEntityName] = actualName;
                            _logger.LogInformation($"Tracking entity-based variable mapping: '{simpleEntityName}' -> '{actualName}'");
                        }
                        
                        // Also add self-mapping for the actual variable name for direct $-prefixed references
                        variableNameMap[actualName] = actualName;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Error tracking variable names for activity type '{activityType}'");
            }
        }

        #endregion

        #region Phase 9: Java Actions

        public async Task<string> ListJavaActions(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters?["module_name"]?.ToString();
                var modules = string.IsNullOrEmpty(moduleName)
                    ? Utils.Utils.GetAllNonAppStoreModules(_model).ToList()
                    : new List<Mendix.StudioPro.ExtensionsAPI.Model.Projects.IModule> { Utils.Utils.ResolveModule(_model, moduleName) };

                modules.RemoveAll(m => m == null);
                if (!modules.Any())
                    return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });

                var result = new List<object>();
                foreach (var module in modules)
                {
                    var javaActions = _model.Root.GetModuleDocuments<IJavaAction>(module);
                    foreach (var ja in javaActions)
                    {
                        var actionParams = ja.GetActionParameters()
                            .Select(p => new
                            {
                                name = p.Name,
                                description = p.Description,
                                category = p.Category
                            })
                            .ToList();

                        result.Add(new
                        {
                            name = ja.Name,
                            qualifiedName = ja.QualifiedName?.ToString(),
                            module = module.Name,
                            parameterCount = actionParams.Count,
                            parameters = actionParams
                        });
                    }
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    totalJavaActions = result.Count,
                    javaActions = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing Java actions");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 10: Project Settings & Runtime Configuration

        private IProjectSettings? GetProjectSettings()
        {
            var project = _model.Root as IProject;
            return project?.GetProjectDocuments().OfType<IProjectSettings>().FirstOrDefault();
        }

        private T? GetSettingsPart<T>() where T : class, IProjectSettingsPart
        {
            var settings = GetProjectSettings();
            return settings?.GetSettingsParts().OfType<T>().FirstOrDefault();
        }

        public async Task<string> ReadRuntimeSettings(JsonObject parameters)
        {
            try
            {
                var runtimeSettings = GetSettingsPart<IRuntimeSettings>();
                if (runtimeSettings == null)
                    return JsonSerializer.Serialize(new { error = "Could not find runtime settings in the project" });

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    afterStartupMicroflow = runtimeSettings.AfterStartupMicroflow?.ToString(),
                    beforeShutdownMicroflow = runtimeSettings.BeforeShutdownMicroflow?.ToString(),
                    healthCheckMicroflow = runtimeSettings.HealthCheckMicroflow?.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading runtime settings");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> SetRuntimeSettings(JsonObject parameters)
        {
            try
            {
                var runtimeSettings = GetSettingsPart<IRuntimeSettings>();
                if (runtimeSettings == null)
                    return JsonSerializer.Serialize(new { error = "Could not find runtime settings in the project" });

                using var transaction = _model.StartTransaction("Set runtime settings");
                bool changed = false;

                if (parameters.ContainsKey("after_startup_microflow"))
                {
                    var mfName = parameters["after_startup_microflow"]?.ToString();
                    runtimeSettings.AfterStartupMicroflow = string.IsNullOrEmpty(mfName) ? null : _model.ToQualifiedName<IMicroflow>(mfName);
                    changed = true;
                }
                if (parameters.ContainsKey("before_shutdown_microflow"))
                {
                    var mfName = parameters["before_shutdown_microflow"]?.ToString();
                    runtimeSettings.BeforeShutdownMicroflow = string.IsNullOrEmpty(mfName) ? null : _model.ToQualifiedName<IMicroflow>(mfName);
                    changed = true;
                }
                if (parameters.ContainsKey("health_check_microflow"))
                {
                    var mfName = parameters["health_check_microflow"]?.ToString();
                    runtimeSettings.HealthCheckMicroflow = string.IsNullOrEmpty(mfName) ? null : _model.ToQualifiedName<IMicroflow>(mfName);
                    changed = true;
                }

                if (parameters.ContainsKey("clear_after_startup") && parameters["clear_after_startup"]?.GetValue<bool>() == true)
                {
                    runtimeSettings.AfterStartupMicroflow = null;
                    changed = true;
                }
                if (parameters.ContainsKey("clear_before_shutdown") && parameters["clear_before_shutdown"]?.GetValue<bool>() == true)
                {
                    runtimeSettings.BeforeShutdownMicroflow = null;
                    changed = true;
                }
                if (parameters.ContainsKey("clear_health_check") && parameters["clear_health_check"]?.GetValue<bool>() == true)
                {
                    runtimeSettings.HealthCheckMicroflow = null;
                    changed = true;
                }

                if (!changed)
                    return JsonSerializer.Serialize(new { error = "No settings parameters provided. Use after_startup_microflow, before_shutdown_microflow, health_check_microflow, or clear_* flags." });

                transaction.Commit();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    afterStartupMicroflow = runtimeSettings.AfterStartupMicroflow?.ToString(),
                    beforeShutdownMicroflow = runtimeSettings.BeforeShutdownMicroflow?.ToString(),
                    healthCheckMicroflow = runtimeSettings.HealthCheckMicroflow?.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting runtime settings");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ReadConfigurations(JsonObject parameters)
        {
            try
            {
                var configSettings = GetSettingsPart<IConfigurationSettings>();
                if (configSettings == null)
                    return JsonSerializer.Serialize(new { error = "Could not find configuration settings in the project" });

                var configName = parameters?["configuration_name"]?.ToString();
                var configs = configSettings.GetConfigurations();

                if (!string.IsNullOrEmpty(configName))
                    configs = configs.Where(c => c.Name.Equals(configName, StringComparison.OrdinalIgnoreCase)).ToList();

                var result = configs.Select(c => new
                {
                    name = c.Name,
                    applicationRootUrl = c.ApplicationRootUrl,
                    customSettings = c.GetCustomSettings().Select(cs => new { name = cs.Name, value = cs.Value }).ToList(),
                    constantValues = c.GetConstantValues().Select(cv => new
                    {
                        constant = cv.Constant?.ToString(),
                        valueType = cv.SharedOrPrivateValue?.GetType().Name
                    }).ToList()
                }).ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    totalConfigurations = result.Count,
                    configurations = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading configurations");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> SetConfiguration(JsonObject parameters)
        {
            try
            {
                var configSettings = GetSettingsPart<IConfigurationSettings>();
                if (configSettings == null)
                    return JsonSerializer.Serialize(new { error = "Could not find configuration settings in the project" });

                var configName = parameters["configuration_name"]?.ToString();
                if (string.IsNullOrEmpty(configName))
                    return JsonSerializer.Serialize(new { error = "configuration_name is required" });

                using var transaction = _model.StartTransaction($"Set configuration '{configName}'");

                var config = configSettings.GetConfigurations()
                    .FirstOrDefault(c => c.Name.Equals(configName, StringComparison.OrdinalIgnoreCase));

                bool created = false;
                if (config == null)
                {
                    var createIfMissing = parameters["create_if_missing"]?.GetValue<bool>() ?? true;
                    if (!createIfMissing)
                        return JsonSerializer.Serialize(new { error = $"Configuration '{configName}' not found and create_if_missing is false" });

                    config = _model.Create<IConfiguration>();
                    config.Name = configName;
                    configSettings.AddConfiguration(config);
                    created = true;
                }

                if (parameters.ContainsKey("application_root_url"))
                {
                    config.ApplicationRootUrl = parameters["application_root_url"]?.ToString() ?? "";
                }

                if (parameters.ContainsKey("custom_settings") && parameters["custom_settings"] is JsonArray customSettingsArr)
                {
                    // Remove existing custom settings first
                    foreach (var existing in config.GetCustomSettings().ToList())
                        config.RemoveCustomSetting(existing);

                    foreach (var item in customSettingsArr)
                    {
                        var setting = _model.Create<ICustomSetting>();
                        setting.Name = item?["name"]?.ToString() ?? "";
                        setting.Value = item?["value"]?.ToString() ?? "";
                        config.AddCustomSetting(setting);
                    }
                }

                transaction.Commit();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    created,
                    configuration = new
                    {
                        name = config.Name,
                        applicationRootUrl = config.ApplicationRootUrl,
                        customSettingsCount = config.GetCustomSettings().Count,
                        constantValuesCount = config.GetConstantValues().Count
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting configuration");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ReadVersionControl(JsonObject parameters)
        {
            try
            {
                if (_versionControlService == null)
                    return JsonSerializer.Serialize(new { error = "Version control service is not available" });

                var isVc = _versionControlService.IsProjectVersionControlled(_model);
                if (!isVc)
                {
                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        isVersionControlled = false,
                        message = "Project is not under version control"
                    });
                }

                string? branchName = null;
                string? commitId = null;
                string? commitAuthor = null;
                string? commitDate = null;
                string? commitMessage = null;

                try
                {
                    var branch = _versionControlService.GetCurrentBranch(_model);
                    branchName = branch?.Name;

                    if (branch != null)
                    {
                        try
                        {
                            var headCommit = _versionControlService.GetHeadCommit(_model, branch);
                            commitId = headCommit?.ID;
                            commitAuthor = headCommit?.Author;
                            commitDate = headCommit?.Date;
                            commitMessage = headCommit?.Message;
                        }
                        catch (Exception) { /* Branch may have no commits */ }
                    }
                }
                catch (Exception) { /* Git config may not be readable */ }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    isVersionControlled = true,
                    branch = branchName,
                    headCommit = new
                    {
                        id = commitId,
                        author = commitAuthor,
                        date = commitDate,
                        message = commitMessage
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading version control info");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 11: Advanced Microflow Operations

        public async Task<string> SetMicroflowUrl(JsonObject parameters)
        {
            try
            {
                var microflowName = parameters["microflow_name"]?.ToString();
                if (string.IsNullOrEmpty(microflowName))
                    return JsonSerializer.Serialize(new { error = "microflow_name is required" });

                var moduleName = parameters?["module_name"]?.ToString();
                IMicroflow? microflow = null;

                var modules = string.IsNullOrEmpty(moduleName)
                    ? Utils.Utils.GetAllNonAppStoreModules(_model).ToList()
                    : new List<Mendix.StudioPro.ExtensionsAPI.Model.Projects.IModule> { Utils.Utils.ResolveModule(_model, moduleName) };

                foreach (var module in modules.Where(m => m != null))
                {
                    microflow = module.GetDocuments().OfType<IMicroflow>()
                        .FirstOrDefault(m => m.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));
                    if (microflow != null) break;
                }

                if (microflow == null)
                    return JsonSerializer.Serialize(new { error = $"Microflow '{microflowName}' not found" });

                if (parameters.ContainsKey("url"))
                {
                    var url = parameters["url"]?.ToString() ?? "";
                    using var transaction = _model.StartTransaction($"Set microflow URL for '{microflowName}'");
                    microflow.Url = url;
                    transaction.Commit();

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        microflow = microflowName,
                        url = microflow.Url,
                        message = string.IsNullOrEmpty(url) ? "URL cleared" : $"URL set to '{url}'"
                    });
                }
                else
                {
                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        microflow = microflowName,
                        url = microflow.Url
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting microflow URL");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ListRules(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters?["module_name"]?.ToString();
                var modules = string.IsNullOrEmpty(moduleName)
                    ? Utils.Utils.GetAllNonAppStoreModules(_model).ToList()
                    : new List<Mendix.StudioPro.ExtensionsAPI.Model.Projects.IModule> { Utils.Utils.ResolveModule(_model, moduleName) };

                modules.RemoveAll(m => m == null);
                if (!modules.Any())
                    return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });

                var result = new List<object>();
                foreach (var module in modules)
                {
                    var rules = _model.Root.GetModuleDocuments<IRule>(module);
                    foreach (var rule in rules)
                    {
                        result.Add(new
                        {
                            name = rule.Name,
                            qualifiedName = rule.QualifiedName?.ToString(),
                            module = module.Name
                        });
                    }
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    totalRules = result.Count,
                    rules = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing rules");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ExcludeDocument(JsonObject parameters)
        {
            try
            {
                var documentName = parameters["document_name"]?.ToString();
                if (string.IsNullOrEmpty(documentName))
                    return JsonSerializer.Serialize(new { error = "document_name is required" });

                var moduleName = parameters?["module_name"]?.ToString();
                var excluded = parameters?["excluded"]?.GetValue<bool>() ?? true;

                var modules = string.IsNullOrEmpty(moduleName)
                    ? Utils.Utils.GetAllNonAppStoreModules(_model).ToList()
                    : new List<Mendix.StudioPro.ExtensionsAPI.Model.Projects.IModule> { Utils.Utils.ResolveModule(_model, moduleName) };

                Mendix.StudioPro.ExtensionsAPI.Model.Projects.IDocument? doc = null;
                string? foundModule = null;
                foreach (var module in modules.Where(m => m != null))
                {
                    doc = module.GetDocuments()
                        .FirstOrDefault(d => d.Name.Equals(documentName, StringComparison.OrdinalIgnoreCase));
                    if (doc != null)
                    {
                        foundModule = module.Name;
                        break;
                    }
                }

                if (doc == null)
                    return JsonSerializer.Serialize(new { error = $"Document '{documentName}' not found" });

                using var transaction = _model.StartTransaction($"Set excluded={excluded} for '{documentName}'");
                doc.Excluded = excluded;
                transaction.Commit();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    document = documentName,
                    module = foundModule,
                    excluded = doc.Excluded
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error excluding document");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 12: Untyped Model Introspection

        private IModelRoot? GetUntypedModelRoot()
        {
            return _untypedModelService?.GetUntypedModel(_model);
        }

        private List<IModelUnit> GetUnitsWithFallback(IModelRoot root, string typeString)
        {
            // Try with $ separator first, then . separator
            var units = root.GetUnitsOfType(typeString)?.ToList() ?? new List<IModelUnit>();
            if (units.Count == 0 && typeString.Contains("$"))
            {
                units = root.GetUnitsOfType(typeString.Replace("$", "."))?.ToList() ?? new List<IModelUnit>();
            }
            return units;
        }

        private object SerializeModelUnit(IModelUnit unit, bool includeProperties = false, int maxProperties = 20)
        {
            var result = new Dictionary<string, object?>();
            result["name"] = unit.Name;
            result["qualifiedName"] = unit.QualifiedName;
            result["type"] = unit.Type;

            if (includeProperties)
            {
                var props = unit.GetProperties().Take(maxProperties).Select(p =>
                {
                    object? val = null;
                    try
                    {
                        if (p.IsList)
                        {
                            var values = p.GetValues();
                            val = values?.Take(5).Select(v => v?.ToString()).ToList();
                        }
                        else
                        {
                            val = p.Value?.ToString();
                        }
                    }
                    catch { val = "<error reading value>"; }

                    return new { name = p.Name, type = p.Type.ToString(), isList = p.IsList, value = val };
                }).ToList();

                result["properties"] = props;
            }

            return result;
        }

        public async Task<string> ReadSecurityInfo(JsonObject parameters)
        {
            try
            {
                var root = GetUntypedModelRoot();
                if (root == null)
                    return JsonSerializer.Serialize(new { error = "IUntypedModelAccessService is not available" });

                var moduleName = parameters?["module_name"]?.ToString();

                var securityUnits = GetUnitsWithFallback(root, "Security$ModuleSecurity");
                if (!securityUnits.Any())
                    securityUnits = GetUnitsWithFallback(root, "Security$ProjectSecurity");

                var result = new List<object>();
                foreach (var unit in securityUnits)
                {
                    if (!string.IsNullOrEmpty(moduleName))
                    {
                        var unitName = unit.Name ?? unit.QualifiedName;
                        if (unitName != null && !unitName.Contains(moduleName, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    var roles = unit.GetElementsOfType("Security$ModuleRole")
                        .Select(r => new { name = r.Name, id = r.ID })
                        .ToList();

                    var accessRules = unit.GetElementsOfType("Security$AccessRule")
                        .Select(r => new
                        {
                            name = r.Name,
                            type = r.Type,
                            propertyCount = r.GetProperties().Count
                        })
                        .Take(50)
                        .ToList();

                    result.Add(new
                    {
                        name = unit.Name,
                        qualifiedName = unit.QualifiedName,
                        type = unit.Type,
                        moduleRoles = roles,
                        accessRuleCount = accessRules.Count,
                        accessRules
                    });
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    totalSecurityUnits = result.Count,
                    security = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading security info");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ListNanoflows(JsonObject parameters)
        {
            try
            {
                var root = GetUntypedModelRoot();
                if (root == null)
                    return JsonSerializer.Serialize(new { error = "IUntypedModelAccessService is not available" });

                var moduleName = parameters?["module_name"]?.ToString();
                var nanoflows = GetUnitsWithFallback(root, "Microflows$Nanoflow");

                var result = nanoflows
                    .Where(nf => string.IsNullOrEmpty(moduleName) ||
                                 (nf.QualifiedName?.Contains(moduleName, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Select(nf => new
                    {
                        name = nf.Name,
                        qualifiedName = nf.QualifiedName,
                        module = nf.QualifiedName?.Split('.').FirstOrDefault()
                    })
                    .ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    totalNanoflows = result.Count,
                    nanoflows = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing nanoflows");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ListScheduledEvents(JsonObject parameters)
        {
            try
            {
                var root = GetUntypedModelRoot();
                if (root == null)
                    return JsonSerializer.Serialize(new { error = "IUntypedModelAccessService is not available" });

                var moduleName = parameters?["module_name"]?.ToString();
                var events = GetUnitsWithFallback(root, "ScheduledEvents$ScheduledEvent");

                var result = events
                    .Where(e => string.IsNullOrEmpty(moduleName) ||
                                (e.QualifiedName?.Contains(moduleName, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Select(e =>
                    {
                        var enabled = e.GetProperty("Enabled")?.Value?.ToString();
                        var interval = e.GetProperty("Interval")?.Value?.ToString();
                        var intervalType = e.GetProperty("IntervalType")?.Value?.ToString();
                        var startOffset = e.GetProperty("StartOffset")?.Value?.ToString();

                        return new
                        {
                            name = e.Name,
                            qualifiedName = e.QualifiedName,
                            module = e.QualifiedName?.Split('.').FirstOrDefault(),
                            enabled,
                            interval,
                            intervalType,
                            startOffset
                        };
                    })
                    .ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    totalScheduledEvents = result.Count,
                    scheduledEvents = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing scheduled events");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ListRestServices(JsonObject parameters)
        {
            try
            {
                var root = GetUntypedModelRoot();
                if (root == null)
                    return JsonSerializer.Serialize(new { error = "IUntypedModelAccessService is not available" });

                var moduleName = parameters?["module_name"]?.ToString();
                var services = GetUnitsWithFallback(root, "Rest$PublishedRestService");

                var result = services
                    .Where(s => string.IsNullOrEmpty(moduleName) ||
                                (s.QualifiedName?.Contains(moduleName, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Select(s =>
                    {
                        var path = s.GetProperty("Path")?.Value?.ToString();
                        var version = s.GetProperty("Version")?.Value?.ToString();
                        var authentication = s.GetProperty("AuthenticationType")?.Value?.ToString();

                        var resources = s.GetElementsOfType("Rest$PublishedRestServiceResource")
                            .Select(r => new
                            {
                                name = r.Name,
                                type = r.Type
                            })
                            .ToList();

                        return new
                        {
                            name = s.Name,
                            qualifiedName = s.QualifiedName,
                            module = s.QualifiedName?.Split('.').FirstOrDefault(),
                            path,
                            version,
                            authentication,
                            resourceCount = resources.Count,
                            resources
                        };
                    })
                    .ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    totalRestServices = result.Count,
                    restServices = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing REST services");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> QueryModelElements(JsonObject parameters)
        {
            try
            {
                var root = GetUntypedModelRoot();
                if (root == null)
                    return JsonSerializer.Serialize(new { error = "IUntypedModelAccessService is not available" });

                var typeName = parameters["type_name"]?.ToString();
                if (string.IsNullOrEmpty(typeName))
                    return JsonSerializer.Serialize(new { error = "type_name is required (e.g. 'Navigation$NavigationProfile', 'Microflows$Nanoflow')" });

                var moduleName = parameters?["module_name"]?.ToString();
                var includeProperties = parameters?["include_properties"]?.GetValue<bool>() ?? false;
                var maxResults = parameters?["max_results"]?.GetValue<int>() ?? 50;

                var units = GetUnitsWithFallback(root, typeName);

                // BUG-014 fix: For embedded types (Entity, Association, Attribute), use the typed API
                // since GetUnitsOfType only works for top-level document units
                var normalizedType = typeName.ToLowerInvariant();
                if (units.Count == 0 && (normalizedType.Contains("entity") || normalizedType.Contains("association") || normalizedType.Contains("attribute")))
                {
                    var embeddedResults = new List<object>();
                    var modules = string.IsNullOrEmpty(moduleName)
                        ? Utils.Utils.GetAllNonAppStoreModules(_model).ToList()
                        : new List<IModule> { Utils.Utils.GetModuleByName(_model, moduleName)! }.Where(m => m != null).ToList();

                    foreach (var mod in modules)
                    {
                        if (mod?.DomainModel == null) continue;

                        if (normalizedType.Contains("entity") && !normalizedType.Contains("association"))
                        {
                            foreach (var entity in mod.DomainModel.GetEntities().Take(maxResults - embeddedResults.Count))
                            {
                                var entityInfo = new Dictionary<string, object?>
                                {
                                    ["name"] = entity.Name,
                                    ["qualifiedName"] = entity.QualifiedName?.ToString(),
                                    ["module"] = mod.Name,
                                    ["type"] = "DomainModels$Entity"
                                };
                                if (includeProperties)
                                {
                                    entityInfo["attributes"] = entity.GetAttributes().Select(a => new { name = a.Name, type = a.Type?.GetType().Name }).ToList();
                                    entityInfo["attributeCount"] = entity.GetAttributes().Count();
                                    entityInfo["associationCount"] = entity.GetAssociations(AssociationDirection.Both, null).Count();
                                }
                                embeddedResults.Add(entityInfo);
                                if (embeddedResults.Count >= maxResults) break;
                            }
                        }
                        else if (normalizedType.Contains("association"))
                        {
                            foreach (var entity in mod.DomainModel.GetEntities())
                            {
                                foreach (var assoc in entity.GetAssociations(AssociationDirection.Both, null).Take(maxResults - embeddedResults.Count))
                                {
                                    var assocInfo = new Dictionary<string, object?>
                                    {
                                        ["name"] = assoc.Association?.Name,
                                        ["qualifiedName"] = assoc.Association?.Name != null ? $"{mod.Name}.{assoc.Association.Name}" : null,
                                        ["module"] = mod.Name,
                                        ["type"] = "DomainModels$Association",
                                        ["parent"] = assoc.Parent?.Name,
                                        ["child"] = assoc.Child?.Name
                                    };
                                    embeddedResults.Add(assocInfo);
                                    if (embeddedResults.Count >= maxResults) break;
                                }
                                if (embeddedResults.Count >= maxResults) break;
                            }
                        }
                        if (embeddedResults.Count >= maxResults) break;
                    }

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        typeName,
                        totalFound = embeddedResults.Count,
                        returned = embeddedResults.Count,
                        elements = embeddedResults,
                        note = "Results obtained via typed domain model API (embedded elements are not accessible via GetUnitsOfType)"
                    });
                }

                var filtered = units
                    .Where(u => string.IsNullOrEmpty(moduleName) ||
                                (u.QualifiedName?.Contains(moduleName, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Take(maxResults)
                    .Select(u => SerializeModelUnit(u, includeProperties))
                    .ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    typeName,
                    totalFound = units.Count,
                    returned = filtered.Count,
                    elements = filtered
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying model elements");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #endregion
    }
}
