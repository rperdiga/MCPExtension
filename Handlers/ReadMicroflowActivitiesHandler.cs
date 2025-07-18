using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Reflection;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.Model.Texts;
using MCPExtension.Core;

namespace MCPExtension.Handlers
{
    public class ReadMicroflowActivitiesHandler : BaseApiHandler
    {
        public override string Path => "/api/microflow/activities";
        public override string Method => "GET";
        
        private readonly IMicroflowService microflowService;

        public ReadMicroflowActivitiesHandler(IModel currentApp, IMicroflowService microflowService) : base(currentApp) 
        {
            this.microflowService = microflowService;
        }

        public override async Task HandleAsync(HttpContext context)
        {
            try
            {
                // Set CORS headers safely
                context.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
                context.Response.Headers.TryAdd("Access-Control-Allow-Methods", "GET, OPTIONS");
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

                // Get microflow name from query parameters
                var microflowName = context.Request.Query["microflowName"].ToString();
                if (string.IsNullOrEmpty(microflowName) || !microflowName.Contains("."))
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        message = "Microflow name must include the module name (e.g., 'ModuleName.MicroflowName')."
                    }, JsonOptions));
                    return;
                }

                // Split into module name and microflow name
                var parts = microflowName.Split('.');
                var moduleName = parts[0];
                var microflowShortName = parts[1];

                // Find the module
                var module = CurrentApp.Root.GetModules().FirstOrDefault(m => m.Name.Equals(moduleName, System.StringComparison.OrdinalIgnoreCase));
                if (module == null)
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        message = $"Module '{moduleName}' not found."
                    }, JsonOptions));
                    return;
                }

                // Find the microflow within the module
                var microflow = module.GetDocuments().OfType<IMicroflow>()
                    .FirstOrDefault(mf => mf.Name.Equals(microflowShortName, System.StringComparison.OrdinalIgnoreCase));

                if (microflow == null)
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        message = $"Microflow '{microflowShortName}' not found in module '{moduleName}'."
                    }, JsonOptions));
                    return;
                }

                var actionActivities = microflowService.GetAllMicroflowActivities(microflow);
                int actionActivityCount = actionActivities.Count;

                var activitiesList = new List<Dictionary<string, object>>();
                
                foreach (var actionActivity in actionActivities)
                {
                    // Check if the activity is of a known type
                    var activityType = actionActivity.GetType();
                    var activityInfo = new Dictionary<string, object>
                    {
                        ["ActivityId"] = actionActivity.GetHashCode(),
                        ["ActivityType"] = activityType.Name,
                        ["ActivityFullType"] = activityType.FullName
                    };

                    // Use reflection to extract Caption and Documentation if available
                    var captionProperty = activityType.GetProperty("Caption");
                    if (captionProperty != null)
                    {
                        var captionValue = captionProperty.GetValue(actionActivity) as IText;
                        activityInfo["Caption"] = captionValue?.GetTranslations()?.FirstOrDefault()?.Text ?? "No Caption";
                    }

                    var documentationProperty = activityType.GetProperty("Documentation");
                    if (documentationProperty != null)
                    {
                        activityInfo["Documentation"] = documentationProperty.GetValue(actionActivity)?.ToString() ?? "No Documentation";
                    }

                    // Check if the activity is an IActionActivity to extract more details
                    if (actionActivity is IActionActivity action)
                    {
                        activityInfo["Disabled"] = action.Disabled;
                        activityInfo["ActionType"] = action.Action?.GetType().Name ?? "Unknown";
                        activityInfo["ActionDetails"] = action.Action?.ToString() ?? "No Details";
                    }

                    ExtractSpecificActionProperties(actionActivity, activityInfo);

                    // Add the activity info to the list
                    activitiesList.Add(activityInfo);
                }

                // Get input parameters
                var inputParameters = microflowService.GetParameters(microflow)
                    .Select(param => new
                    {
                        param.Name,
                        DataType = param.Type?.GetType().Name ?? "Unknown",
                        TypeFullName = param.Type?.GetType().FullName ?? "Unknown"
                    })
                    .ToList();

                // Get output parameter with more detailed info
                var outputParameter = new
                {
                    ReturnType = microflow.ReturnType?.GetType().Name ?? "Void",
                    ReturnTypeFullName = microflow.ReturnType?.GetType().FullName ?? "Void"
                };

                // Return the combined data with debug info
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    data = new
                    {
                        InputParameters = inputParameters,
                        Activities = activitiesList,
                        ActivityCount = actionActivityCount,
                        OutputParameter = outputParameter,
                        MicroflowName = microflow.Name,
                        QualifiedName = microflow.QualifiedName?.FullName ?? "Unknown"
                    }
                }, JsonOptions));
            }
            catch (System.Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    message = $"Error retrieving microflow details: {ex.Message}",
                    stackTrace = ex.StackTrace
                }, JsonOptions));
            }
        }
        
        private void ExtractSpecificActionProperties(object activity, Dictionary<string, object> activityInfo)
        {
            try
            {
                var activityType = activity.GetType();

                // Handle ActionActivityProxy specifically to get the underlying Action details
                if (activityType.Name == "ActionActivityProxy")
                {
                    var actionProperty = activityType.GetProperty("Action");
                    if (actionProperty != null)
                    {
                        var actionValue = actionProperty.GetValue(activity);
                        if (actionValue != null)
                        {
                            // Extract details from the Action object using the enhanced helper
                            var actionDetails = ExtractValueDetails(actionValue);
                            if (actionDetails is Dictionary<string, object> detailsDict)
                            {
                                activityInfo["ActionProperties"] = detailsDict;
                            }
                            else if (actionDetails != null) {
                                 activityInfo["ActionValue"] = actionDetails; // Fallback if not a dictionary
                            }
                        }
                    }

                    // Handle ProxyCache (assuming it's a property of ActionActivityProxy)
                    var proxyCacheProperty = activityType.GetProperty("ProxyCache");
                    if (proxyCacheProperty != null)
                    {
                        var proxyCacheValue = proxyCacheProperty.GetValue(activity);
                         // Only extract basic info for ProxyCache, not the full context
                         if (proxyCacheValue != null) {
                            activityInfo["ProxyCacheType"] = proxyCacheValue.GetType().FullName;
                         }
                    }
                }
                // Add specific handling for other activity types if needed.
                // For example, directly extracting properties from RetrieveActionProxy if it's not nested under ActionActivityProxy.
                else if (activity is IRetrieveAction retrieveAction) // Example for direct handling if needed
                {
                     activityInfo["RetrieveActionProperties"] = ExtractValueDetails(retrieveAction);
                }
                // Add other common properties if they weren't part of the Action object
                else {
                     var variableNameProp = activityType.GetProperty("VariableName");
                     if (variableNameProp != null) {
                         var value = variableNameProp.GetValue(activity)?.ToString();
                         if (!string.IsNullOrEmpty(value)) activityInfo["VariableName"] = value;
                     }
                     // Extract other relevant properties directly from the activity object if necessary
                }

            }
            catch (Exception ex)
            {
                // Log or handle extraction errors
                activityInfo["ExtractionError"] = $"Error during property extraction: {ex.Message}";
            }
        }


        // Revised helper method to recursively extract details with more skipping
        private object ExtractValueDetails(object value, int recursionDepth = 0, HashSet<object> visited = null)
        {
            if (value == null) return null;

            // --- Cycle Detection & Depth Limit ---
            const int MaxRecursionDepth = 5; // Keep the depth limit
            if (recursionDepth > MaxRecursionDepth)
            {
                return $"<Max Recursion Depth Reached: {value.GetType().Name}>";
            }

            // Initialize visited set for cycle detection at the start of the top-level call
            if (visited == null) visited = new HashSet<object>();

            // Prevent cycles for non-primitive types
            if (!value.GetType().IsValueType && value is not string)
            {
                if (!visited.Add(value))
                {
                    return $"<Cycle Detected: {value.GetType().Name}>";
                }
            }
            // --- End Cycle Detection ---

            var valueType = value.GetType();

            try
            {
                // --- Skip Known Complex/Framework Types ---
                if (value is IModel || value is IProject || value is IModule || value is IDocument || value is System.Reflection.Assembly || value is System.Reflection.Module || value is Type || value.GetType().FullName.StartsWith("Mendix.Modeler.Undo") || value.GetType().FullName.StartsWith("Mendix.Modeler.Storage") || value.GetType().FullName.StartsWith("Mendix.Modeler.ExtensionLoader.ModelProxies.ProxyCache"))
                {
                    return $"<{valueType.Name}>"; // Just return the type name
                }
                // --- End Skipping ---

                // Handle IText specifically
                if (value is IText textValue)
                {
                    return textValue.GetTranslations()?.FirstOrDefault()?.Text ?? textValue.ToString();
                }

                // Handle primitive types, strings, decimals, DateTime, Guid directly
                if (valueType.IsPrimitive || value is string || value is decimal || value is DateTime || value is Guid || valueType.IsEnum)
                {
                    return value.ToString();
                }

                // Handle Mendix QualifiedName specifically
                if (valueType.IsGenericType && valueType.GetGenericTypeDefinition().Name.Contains("IQualifiedName")) // More robust check
                {
                     try {
                        dynamic qualifiedName = value;
                        return qualifiedName.FullName ?? value.ToString();
                     } catch {
                         return value.ToString(); // Fallback
                     }
                }

                // Handle Expressions specifically (extract Text property)
                if (valueType.FullName != null && (valueType.FullName.Contains("Expression") || valueType.FullName.Contains("Template"))) {
                     var textProperty = valueType.GetProperty("Text");
                     if (textProperty != null) {
                         return textProperty.GetValue(value)?.ToString();
                     }
                }

                // Handle IEnumerable (excluding strings)
                if (value is System.Collections.IEnumerable enumerable && !(value is string))
                {
                    var list = new List<object>();
                    foreach (var item in enumerable)
                    {
                        // Recursively extract details for each item
                        var itemDetails = ExtractValueDetails(item, recursionDepth + 1, visited); // Pass visited set
                        if (itemDetails != null) list.Add(itemDetails);
                    }
                    return list.Count > 0 ? list : null;
                }

                // Handle other complex objects (including proxies)
                var details = new Dictionary<string, object>();
                var properties = valueType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                // Add Type Name for clarity
                details["_ObjectType"] = valueType.FullName ?? valueType.Name;

                // --- Properties to Skip ---
                var propertiesToSkip = new HashSet<string> {
                    "StorageContainer", "UndoManager", "Context", "Parent", "Model", "ContainingObject",
                    "Module", "Assembly", "DeclaringMethod", "ReflectedType", "BaseType", "UnderlyingSystemType",
                    "TypeHandle", "CustomAttributes", "DeclaredMembers", "DeclaredMethods", "DeclaredProperties",
                    "ImplementedInterfaces", "GenericTypeArguments", "GenericTypeParameters", "StructLayoutAttribute",
                    "TypeInitializer", "MetadataToken", "GenericParameterAttributes", "DeclaringType", "ResolvedType",
                    "Original", // Often points back causing cycles or excessive data
                    "ProxyCache" // Handled separately or skipped
                };
                // --- End Properties to Skip ---


                foreach (var property in properties)
                {
                    // Skip indexer properties and explicitly skipped names
                    if (property.GetIndexParameters().Length > 0 || propertiesToSkip.Contains(property.Name)) continue;

                    try
                    {
                        var propValue = property.GetValue(value);
                        // Recursive call for nested objects
                        var extractedValue = ExtractValueDetails(propValue, recursionDepth + 1, visited); // Pass visited set
                        if (extractedValue != null)
                        {
                            bool isEmptyCollection = (extractedValue is System.Collections.ICollection coll && coll.Count == 0);
                            bool isTypeNameOnly = (extractedValue is string str && str.StartsWith("<") && str.EndsWith(">")); // Don't add if we only got the type name placeholder

                            if (!isEmptyCollection && !isTypeNameOnly) {
                                details[property.Name] = extractedValue;
                            }
                        }
                    }
                    catch (TargetInvocationException ex) {
                         details[property.Name] = $"<Error accessing {property.Name}: {ex.InnerException?.Message ?? ex.Message}>";
                    }
                    catch (Exception ex)
                    {
                         details[property.Name] = $"<Error accessing {property.Name}: {ex.Message}>";
                    }
                }

                // Remove the object from visited set after processing its properties
                if (!value.GetType().IsValueType && value is not string)
                {
                    visited.Remove(value);
                }

                // Return dictionary only if it contains more than just the type name
                return details.Count > 1 ? details : null;
            }
            catch (Exception ex)
            {
                // Remove the object from visited set in case of error during processing
                if (!value.GetType().IsValueType && value is not string)
                {
                    visited?.Remove(value);
                }
                return $"<Error processing {valueType.Name}: {ex.Message}>";
            }
        }


        // Keep the original ExtractProxyDetails signature for compatibility if needed,
        // but it might be better to remove it and use ExtractValueDetails directly.
        // For now, let it call the helper.
        private Dictionary<string, object> ExtractProxyDetails(object proxy)
        {
            var result = ExtractValueDetails(proxy); // Start recursion
            if (result is Dictionary<string, object> dict) {
                return dict;
            }
            // Return empty dictionary if the helper didn't return a dictionary
            return new Dictionary<string, object>();
        }
    }
}
