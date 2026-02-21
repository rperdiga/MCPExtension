using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.IO;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using MCPExtension.MCP;

namespace MCPExtension.MCP
{
    public class McpServer
    {
        private readonly ILogger<McpServer> _logger;
        private readonly Dictionary<string, Func<JsonObject, Task<object>>> _tools;
        private bool _isRunning;
        private IWebHost? _webHost;
        private int _port;
        private int _activeSseConnections;
        private int _totalToolCalls;

        private readonly string? _projectDirectory;

        public int Port => _port;
        public int ActiveSseConnections => _activeSseConnections;
        public int TotalToolCalls => _totalToolCalls;
        public event Action<ToolCallEventArgs>? OnToolCallEvent;

        public McpServer(ILogger<McpServer> logger, int port = 3001, string? projectDirectory = null)
        {
            _logger = logger;
            _tools = new Dictionary<string, Func<JsonObject, Task<object>>>();
            _port = port;
            _projectDirectory = projectDirectory;
        }

        public void RegisterTool(string name, Func<JsonObject, Task<object>> handler)
        {
            _tools[name] = handler;
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            _isRunning = true;
            LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] MCP Server starting on port {_port}...");
            _logger.LogInformation($"MCP Server starting on port {_port}...");

            try
            {
                var builder = new WebHostBuilder()
                    .UseKestrel(options =>
                    {
                        options.ListenLocalhost(_port);
                    })
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(_logger);
                        services.AddSingleton(this);
                    })
                    .Configure(app =>
                    {
                        // Add middleware to log all incoming requests
                        app.Use(async (context, next) =>
                        {
                            LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Incoming request: {context.Request.Method} {context.Request.Path}{context.Request.QueryString} from {context.Connection.RemoteIpAddress}");
                            if (context.Request.Headers.Count > 0)
                            {
                                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Headers: {string.Join(", ", context.Request.Headers.Select(h => $"{h.Key}={h.Value}"))}");
                            }
                            await next();
                        });
                        
                        // Handle SSE endpoint
                        app.Map("/sse", HandleSseApp);
                        
                        // Handle MCP message endpoint - this is where clients send MCP requests
                        app.Map("/message", messageApp =>
                        {
                            messageApp.Run(async context =>
                            {
                                await HandleMcpMessage(context);
                            });
                        });
                        
                        // Handle root endpoint for MCP messages (some clients might expect this)
                        app.Use(async (context, next) =>
                        {
                            if (context.Request.Method == "POST" && context.Request.Path == "/")
                            {
                                await HandleMcpMessage(context);
                                return;
                            }
                            await next();
                        });
                        
                        // Handle health endpoint
                        app.Map("/health", healthApp =>
                        {
                            healthApp.Run(async context =>
                            {
                                await context.Response.WriteAsync("MCP Server is running");
                            });
                        });
                        
                        // Handle metadata endpoint
                        app.Map("/.well-known/mcp", metadataApp =>
                        {
                            metadataApp.Run(async context =>
                            {
                                var metadata = new
                                {
                                    transport = "sse",
                                    sse = new
                                    {
                                        endpoint = "/sse"
                                    },
                                    message = new
                                    {
                                        endpoint = "/message"
                                    },
                                    serverInfo = new
                                    {
                                        name = "mendix-mcp-server",
                                        version = "1.0.0"
                                    }
                                };
                                
                                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Metadata endpoint accessed");
                                context.Response.ContentType = "application/json";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(metadata));
                            });
                        });
                        
                        // Fallback handler for any other requests
                        app.Run(async context =>
                        {
                            LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Unhandled request: {context.Request.Method} {context.Request.Path}{context.Request.QueryString}");
                            context.Response.StatusCode = 404;
                            await context.Response.WriteAsync("Not Found");
                        });
                    });

                _webHost = builder.Build();
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] WebHost built, starting...");
                await _webHost.StartAsync(cancellationToken);
                
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] MCP Server started successfully on http://localhost:{_port}");
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Available endpoints:");
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] - SSE: http://localhost:{_port}/sse");
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] - Messages: http://localhost:{_port}/message");
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] - Root POST: http://localhost:{_port}/");
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] - Health: http://localhost:{_port}/health");
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] - Metadata: http://localhost:{_port}/.well-known/mcp");
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Registered {_tools.Count} tools");
                _logger.LogInformation($"MCP Server started successfully on http://localhost:{_port}");
                
                // Keep the server running
                while (!cancellationToken.IsCancellationRequested && _isRunning)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] MCP Server error: {ex}");
                _logger.LogError(ex, "MCP Server error");
                throw;
            }
        }

        private void HandleSseApp(IApplicationBuilder app)
        {
            app.Run(async context =>
            {
                if (context.Request.Method == "GET")
                {
                    // Handle SSE connection
                    await HandleSseConnection(context);
                }
                else if (context.Request.Method == "POST")
                {
                    // Handle MCP message sent to SSE endpoint
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] POST request to /sse from {context.Connection.RemoteIpAddress}");
                    await HandleMcpMessage(context);
                }
                else
                {
                    context.Response.StatusCode = 405; // Method Not Allowed
                    await context.Response.WriteAsync("Method not allowed. Use GET for SSE connection or POST for messages.");
                }
            });
        }

        private async Task HandleMcpMessage(HttpContext context)
        {
            try
            {
                // Add detailed logging
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] MCP Message received - Method: {context.Request.Method}, ContentType: {context.Request.ContentType}");
                
                if (context.Request.Method != "POST")
                {
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Invalid method: {context.Request.Method}, expected POST");
                    context.Response.StatusCode = 405; // Method Not Allowed
                    await context.Response.WriteAsync("Method not allowed. Use POST.");
                    return;
                }

                // Read the request body
                string requestBody;
                using (var reader = new StreamReader(context.Request.Body))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] MCP Request body: {requestBody}");

                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Empty request body");
                    context.Response.StatusCode = 400; // Bad Request
                    await context.Response.WriteAsync("Empty request body");
                    return;
                }

                // Parse JSON
                JsonObject request;
                try
                {
                    request = JsonNode.Parse(requestBody)?.AsObject();
                }
                catch (JsonException ex)
                {
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] JSON parsing error: {ex.Message}");
                    context.Response.StatusCode = 400; // Bad Request
                    await context.Response.WriteAsync($"Invalid JSON: {ex.Message}");
                    return;
                }

                if (request == null)
                {
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Request is null after parsing");
                    context.Response.StatusCode = 400; // Bad Request
                    await context.Response.WriteAsync("Invalid JSON object");
                    return;
                }

                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Processing MCP request...");

                // Process the MCP request
                var response = await ProcessRequest(request);

                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] MCP Response: {JsonSerializer.Serialize(response)}");

                // Send response
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] HandleMcpMessage error: {ex}");
                _logger.LogError(ex, "Error handling MCP message");
                
                context.Response.StatusCode = 500; // Internal Server Error
                await context.Response.WriteAsync($"Internal server error: {ex.Message}");
            }
        }

        private string GetLogFilePath()
        {
            try
            {
                // Use the Mendix project directory if available
                if (!string.IsNullOrEmpty(_projectDirectory))
                {
                    string resourcesDir = System.IO.Path.Combine(_projectDirectory, "resources");
                    if (!System.IO.Directory.Exists(resourcesDir))
                    {
                        System.IO.Directory.CreateDirectory(resourcesDir);
                    }
                    
                    return System.IO.Path.Combine(resourcesDir, "mcp_debug.log");
                }
                
                // Fallback to extension project directory if no project directory provided
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                string executingDirectory = System.IO.Path.GetDirectoryName(assembly.Location);
                DirectoryInfo directory = new DirectoryInfo(executingDirectory);
                string targetDirectory = directory?.Parent?.Parent?.Parent?.FullName 
                    ?? throw new InvalidOperationException("Could not determine target directory");

                string resourcesDir2 = System.IO.Path.Combine(targetDirectory, "resources");
                if (!System.IO.Directory.Exists(resourcesDir2))
                {
                    System.IO.Directory.CreateDirectory(resourcesDir2);
                }
                
                return System.IO.Path.Combine(resourcesDir2, "mcp_debug.log");
            }
            catch (Exception ex)
            {
                // Fallback to current directory if we can't determine project directory
                System.Diagnostics.Debug.WriteLine($"Could not determine log file path: {ex.Message}");
                return System.IO.Path.Combine(Environment.CurrentDirectory, "mcp_debug.log");
            }
        }

        private void LogToFile(string message)
        {
            try
            {
                var logPath = GetLogFilePath();
                File.AppendAllText(logPath, message + Environment.NewLine);
            }
            catch
            {
                // Ignore logging errors to prevent infinite loops
            }
        }

        private async Task HandleSseConnection(HttpContext context)
        {
            context.Response.Headers.Add("Content-Type", "text/event-stream");
            context.Response.Headers.Add("Cache-Control", "no-cache");
            context.Response.Headers.Add("Connection", "keep-alive");
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Headers", "Cache-Control");

            Interlocked.Increment(ref _activeSseConnections);
            LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] SSE client connected from {context.Connection.RemoteIpAddress} (active: {_activeSseConnections})");
            _logger.LogInformation("SSE client connected");

            try
            {
                // Send initial connection message
                await SendSseMessage(context.Response, "connected", "MCP Server ready");
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Sent SSE connected message");

                // Keep connection alive and handle incoming messages via POST to /message endpoint
                while (!context.RequestAborted.IsCancellationRequested && _isRunning)
                {
                    await Task.Delay(30000, context.RequestAborted); // Send keepalive every 30 seconds
                    await SendSseMessage(context.Response, "keepalive", "");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] SSE connection error: {ex}");
                _logger.LogError(ex, "SSE connection error");
            }
            finally
            {
                Interlocked.Decrement(ref _activeSseConnections);
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] SSE client disconnected (active: {_activeSseConnections})");
                _logger.LogInformation("SSE client disconnected");
            }
        }

        private async Task SendSseMessage(HttpResponse response, string eventType, string data)
        {
            var message = $"event: {eventType}\ndata: {data}\n\n";
            var bytes = Encoding.UTF8.GetBytes(message);
            await response.Body.WriteAsync(bytes);
            await response.Body.FlushAsync();
        }

        public async Task<object> ProcessMcpRequest(JsonObject request)
        {
            return await ProcessRequest(request);
        }

        private async Task<object> ProcessRequest(JsonObject request)
        {
            var method = request["method"]?.ToString();
            var id = request["id"]?.AsValue();

            LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Processing method: {method}, id: {id}");

            switch (method)
            {
                case "initialize":
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Handling initialize request");
                    var initResponse = CreateInitializeResponse(id);
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Initialize response created: {JsonSerializer.Serialize(initResponse)}");
                    return initResponse;

                case "tools/list":
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] === TOOLS/LIST REQUEST ===");
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Handling tools/list request");
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Available tools count: {_tools.Count}");
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Available tools: {string.Join(", ", _tools.Keys)}");
                    var toolsResponse = CreateToolsListResponse(id);
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Tools list response created with {_tools.Count} tools");
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] === END TOOLS/LIST REQUEST ===");
                    return toolsResponse;

                case "tools/call":
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Handling tools/call request");
                    var paramsObj = request["params"]?.AsObject();
                    if (paramsObj != null)
                    {
                        return await HandleToolCall(id, paramsObj);
                    }
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] tools/call missing params");
                    return CreateErrorResponse(id, "Invalid parameters", "Missing params");

                default:
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Unknown method: {method}");
                    return CreateErrorResponse(id, "Method not found", $"Unknown method: {method}");
            }
        }

        private object CreateInitializeResponse(JsonNode id)
        {
            return new
            {
                jsonrpc = "2.0",
                id = id?.AsValue(),
                result = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new
                    {
                        tools = new
                        {
                            listChanged = false
                        }
                    },
                    serverInfo = new
                    {
                        name = "mendix-mcp-server",
                        version = "1.0.0"
                    }
                }
            };
        }

        private object CreateToolsListResponse(JsonNode id)
        {
            var tools = new List<object>();
            
            foreach (var toolName in _tools.Keys)
            {
                var schema = GetToolInputSchema(toolName);
                var description = GetToolDescription(toolName);
                
                var tool = new
                {
                    name = toolName,
                    description = description,
                    inputSchema = schema
                };
                
                // Special logging for create_microflow_activities
                if (toolName == "create_microflow_activities")
                {
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] === create_microflow_activities TOOL DEFINITION ===");
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Tool name: {toolName}");
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Description: {description}");
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Schema: {JsonSerializer.Serialize(schema)}");
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Full tool object: {JsonSerializer.Serialize(tool)}");
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] === END TOOL DEFINITION ===");
                }
                
                tools.Add(tool);
            }

            var response = new
            {
                jsonrpc = "2.0",
                id = id?.AsValue(),
                result = new
                {
                    tools = tools
                }
            };
            
            LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Tools list response contains {tools.Count} tools");
            
            return response;
        }

        private async Task<object> HandleToolCall(JsonNode id, JsonObject paramsObj)
        {
            var toolName = paramsObj["name"]?.ToString();
            var arguments = paramsObj["arguments"]?.AsObject();

            if (string.IsNullOrEmpty(toolName) || !_tools.ContainsKey(toolName))
            {
                return CreateErrorResponse(id, "Tool not found", $"Unknown tool: {toolName}");
            }

            var callId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var startTime = DateTime.Now;
            Interlocked.Increment(ref _totalToolCalls);

            LogToFile($"[{startTime:HH:mm:ss.fff}] Tool call [{callId}]: {toolName}");

            // Fire "Started" event
            OnToolCallEvent?.Invoke(new ToolCallEventArgs
            {
                CallId = callId,
                ToolName = toolName,
                Timestamp = startTime,
                Status = ToolCallStatus.Started
            });

            try
            {
                var result = await _tools[toolName](arguments ?? new JsonObject());
                var durationMs = (long)(DateTime.Now - startTime).TotalMilliseconds;

                LogToFile($"[{DateTime.Now:HH:mm:ss.fff}] Tool [{callId}] completed in {durationMs}ms");

                // Fire "Completed" event
                OnToolCallEvent?.Invoke(new ToolCallEventArgs
                {
                    CallId = callId,
                    ToolName = toolName,
                    Timestamp = startTime,
                    Status = ToolCallStatus.Completed,
                    DurationMs = durationMs
                });

                return new
                {
                    jsonrpc = "2.0",
                    id = id?.AsValue(),
                    result = new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = result
                            }
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                var durationMs = (long)(DateTime.Now - startTime).TotalMilliseconds;

                LogToFile($"[{DateTime.Now:HH:mm:ss.fff}] Tool [{callId}] failed in {durationMs}ms: {ex.Message}");

                // Fire "Failed" event
                OnToolCallEvent?.Invoke(new ToolCallEventArgs
                {
                    CallId = callId,
                    ToolName = toolName,
                    Timestamp = startTime,
                    Status = ToolCallStatus.Failed,
                    DurationMs = durationMs,
                    ErrorMessage = ex.Message
                });

                _logger.LogError(ex, "Error executing tool");
                return CreateErrorResponse(id, "Tool execution error", ex.Message);
            }
        }

        private object CreateErrorResponse(JsonNode id, string message, string details)
        {
            return new
            {
                jsonrpc = "2.0",
                id = id?.AsValue(),
                error = new
                {
                    code = -32000,
                    message = message,
                    data = details
                }
            };
        }

        private string GetToolDescription(string toolName)
        {
            return toolName switch
            {
                "list_modules" => "List all modules in the project with metadata (name, fromAppStore, entity count). Use this to discover available modules before performing operations.",
                "create_module" => "Create a new module in the project. Returns the created module metadata.",
                "set_entity_generalization" => "Set inheritance (generalization) for an entity to inherit from another entity. Supports cross-module inheritance.",
                "remove_entity_generalization" => "Remove generalization from an entity, making it a root entity.",
                "add_event_handler" => "Add a before/after event handler (create/commit/delete/rollback) to an entity, linked to a microflow.",
                "add_attribute" => "Add an attribute to an existing entity. Supports all types: String, Integer, Long, Decimal, Boolean, DateTime, AutoNumber, Binary, HashedString, Enumeration. For Enumeration: provide 'enumeration_values' to create a new enum, or use 'Enumeration:EnumName' syntax in attribute_type (e.g. 'Enumeration:OrderStatus') to reference an existing enumeration. Alternative: pass 'enumeration_name' parameter. Optional: default_value.",
                "set_calculated_attribute" => "Set an existing attribute to be calculated by a microflow instead of stored. The microflow receives the entity and returns the computed value.",
                "read_domain_model" => "Read domain model structure including generalizations, event handlers, attribute default values and calculated status, association delete behaviors and owner. Specify module_name for a specific module, or omit to get all non-Marketplace modules.",
                "create_entity" => "Create a new entity in the domain model. Specify module_name to target a specific module.",
                "create_association" => "Create a new association between entities. Supports cross-module associations via parent_module and child_module parameters. Configure delete behavior (parent_delete_behavior, child_delete_behavior) and owner (default, both).",
                "delete_model_element" => "Delete an element from the model. Supports element_type: entity, attribute, association, microflow, constant, enumeration. For entity/attribute/association use entity_name. For microflow/constant/enumeration use document_name (or entity_name as fallback). Specify module_name to target a specific module.",
                "diagnose_associations" => "Diagnose association creation issues. Specify module_name or omit to diagnose the default module.",
                "create_multiple_entities" => "Create multiple entities at once. Supports per-entity module_name override.",
                "create_multiple_associations" => "Create multiple associations at once. Supports cross-module via per-association parent_module/child_module. Configure delete behavior and owner per association.",
                "create_domain_model_from_schema" => "Create a complete domain model from a schema definition. Specify module_name to target a specific module.",
                "save_data" => "Generate realistic sample data for Mendix domain model entities. Specify module_name to target a specific module.",
                "generate_overview_pages" => "Generate overview pages for entities. Specify module_name to target a specific module.",
                "list_microflows" => "List all microflows in a module. Specify module_name or omit for default module.",
                "check_model" => "Validate the model for common issues: broken generalizations, missing event handler microflows, broken associations, calculated attributes with missing microflows. Use this after making changes to verify model health. Returns errors, warnings, and module statistics.",
                "get_studio_pro_logs" => "Read Studio Pro log files and MCP extension error logs. Filter by level (ERROR, WARN, INFO, ALL) and time window (last_minutes). Use this to see if Studio Pro encountered any errors from recent operations.",
                "check_project_errors" => "Run mx.exe check against the on-disk MPR file for Studio Pro consistency errors (error codes like CE3945). IMPORTANT: reads the saved .mpr file, NOT in-memory changes. Use 'check_model' for in-memory validation after MCP tool changes. Only use 'check_project_errors' after the project has been saved in Studio Pro. Optional: studio_pro_version (e.g. '11.5.0', auto-detects if omitted).",
                "create_constant" => "Create a new constant in a module. Params: name (required), type (string/integer/boolean/decimal/datetime/float, default: string), default_value, exposed_to_client (bool), module_name.",
                "list_constants" => "List all constants across all modules or in a specific module. Optional: module_name.",
                "create_enumeration" => "Create a new enumeration in a module. Params: name (required), values (array of {name, caption} or strings), module_name.",
                "list_enumerations" => "List all enumerations with their values across all modules or in a specific module. Optional: module_name.",
                "read_project_info" => "Get a comprehensive overview of the project: all modules with entity, association, microflow, constant, and enumeration counts.",
                "get_last_error" => "Get details about the last error",
                "list_available_tools" => "List all available tools",
                "debug_info" => "Get comprehensive debug information about the domain model. Specify module_name to target a specific module.",
                "read_microflow_details" => "Get details about a specific microflow including activities with their positions. Specify module_name to target a specific module.",
                "create_microflow" => "Create a new microflow in the module with parameters and return type. Specify module_name to target a specific module.",
                "create_microflow_activities" => "Create one or more microflow activities in sequence within an existing microflow. IMPORTANT: Mendix expressions use single quotes for string literals (e.g. 'Hello World'), not double quotes. Double quotes are auto-converted to single quotes as a convenience. Supported activity_type values: create_object (entity, variableName, commit, refresh_in_client, initial_values:[{attribute,value}]), change_attribute, change_association, commit, rollback, delete, retrieve_from_database, retrieve_by_association (association_name, output_variable, input_variable, module_name), microflow_call (microflow_name or Module.MicroflowName, return_variable, parameters:[{name,value}]), create_list (entity, output_variable), change_list (list_variable, operation:add/remove/clear/set, change_value), sort_list (list_variable, entity, sort_by:[{attribute,descending}], output_variable), filter_list (list_variable, entity, attribute, filter_expression, output_variable), find_in_list (list_variable, expression; optionally entity+attribute for find-by-attribute), aggregate_list (list_variable, function:count/sum/average/min/max/all/any; optionally entity+attribute for by-attribute, or expression for by-expression), show_message, log_message, union_lists (list_variable, second_list_variable, output_variable), subtract_lists, intersect_lists, contains_in_list, head_of_list (list_variable, output_variable), tail_of_list, reduce_list (list_variable, output_variable, initial_value, expression, return_type:integer/decimal/string/boolean). Specify module_name to target a specific module.",
                "configure_system_attributes" => "Toggle system attributes on a root entity (no generalization): HasCreatedDate, HasChangedDate, HasOwner, HasChangedBy, Persistable. Only works on entities without a generalization (inheriting entities get system attrs from parent).",
                "manage_folders" => "Create, list, or move documents between folders within a module. Actions: 'list' (show all folders and documents), 'create' (create a new folder), 'move_document' (move a document into a folder).",
                "validate_name" => "Validate a candidate name for a Mendix model element. Returns whether the name is valid and optionally auto-fixes it to a valid name.",
                "copy_model_element" => "Deep-copy an entity, microflow, constant, or enumeration within the same module or to a different module. The copy gets a new name.",
                "list_java_actions" => "List all Java actions in a module or across the project, including their parameter names and descriptions.",
                "read_runtime_settings" => "Read project runtime settings: AfterStartupMicroflow, BeforeShutdownMicroflow, and HealthCheckMicroflow assignments.",
                "set_runtime_settings" => "Assign or clear microflows for runtime hooks (after startup, before shutdown, health check). Use qualified names like 'MyModule.ASu_Startup'.",
                "read_configurations" => "List run configurations with their application root URLs, custom settings, and constant value overrides.",
                "set_configuration" => "Create or update a run configuration with application root URL and custom settings. Creates the configuration if it doesn't exist.",
                "read_version_control" => "Read version control status: whether the project is under VC, current branch name, and head commit details (ID, author, date, message).",
                "set_microflow_url" => "Read or set the URL of a microflow. When a URL is set, the microflow is exposed as a REST endpoint. Omit 'url' to read, provide it to set (empty string to clear).",
                "list_rules" => "List validation rules (special server-side microflows) across modules or in a specific module.",
                "exclude_document" => "Mark a document (microflow, page, etc.) as excluded from the project, or un-exclude it. Excluded documents are not compiled or deployed.",
                _ => "Tool description not available"
            };
        }

        private object GetToolInputSchema(string toolName)
        {
            return toolName switch
            {
                "list_modules" => new
                {
                    type = "object",
                    properties = new { },
                    required = new string[0]
                },
                "create_module" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string", description = "Name of the new module to create." }
                    },
                    required = new[] { "module_name" }
                },
                "set_entity_generalization" => new
                {
                    type = "object",
                    properties = new
                    {
                        entity_name = new { type = "string", description = "Name of the entity to set generalization on." },
                        parent_entity = new { type = "string", description = "Name of the parent entity to inherit from." },
                        module_name = new { type = "string", description = "Module containing the entity. Searches all modules if omitted." },
                        parent_module = new { type = "string", description = "Module containing the parent entity. Searches all modules if omitted." }
                    },
                    required = new[] { "entity_name", "parent_entity" }
                },
                "remove_entity_generalization" => new
                {
                    type = "object",
                    properties = new
                    {
                        entity_name = new { type = "string", description = "Name of the entity to remove generalization from." },
                        module_name = new { type = "string", description = "Module containing the entity. Searches all modules if omitted." }
                    },
                    required = new[] { "entity_name" }
                },
                "add_event_handler" => new
                {
                    type = "object",
                    properties = new
                    {
                        entity_name = new { type = "string", description = "Name of the entity to add the event handler to." },
                        @event = new { type = "string", description = "Event type: 'create', 'commit', 'delete', or 'rollback'." },
                        moment = new { type = "string", description = "When to trigger: 'before' or 'after'." },
                        microflow = new { type = "string", description = "Name of the microflow to call when the event fires." },
                        raise_error_on_false = new { type = "boolean", description = "If true (default), raises an error when the microflow returns false." },
                        module_name = new { type = "string", description = "Module containing the entity. Searches all modules if omitted." }
                    },
                    required = new[] { "entity_name", "event", "moment", "microflow" }
                },
                "add_attribute" => new
                {
                    type = "object",
                    properties = new
                    {
                        entity_name = new { type = "string", description = "Name of the entity to add the attribute to." },
                        attribute_name = new { type = "string", description = "Name for the new attribute." },
                        attribute_type = new { type = "string", description = "Type: String, Integer, Long, Decimal, Boolean, DateTime, AutoNumber, Binary, HashedString, or Enumeration. To reference an existing enumeration use 'Enumeration:EnumName' (e.g. 'Enumeration:OrderStatus')." },
                        default_value = new { type = "string", description = "Optional default value for the attribute." },
                        enumeration_values = new { type = "array", items = new { type = "string" }, description = "Provide when attribute_type is 'Enumeration' (exact) to create a new enumeration. List of value names." },
                        enumeration_name = new { type = "string", description = "Name of an existing enumeration to link to this attribute. Alternative to the 'Enumeration:EnumName' colon syntax in attribute_type." },
                        module_name = new { type = "string", description = "Module containing the entity. Searches all modules if omitted." }
                    },
                    required = new[] { "entity_name", "attribute_name", "attribute_type" }
                },
                "set_calculated_attribute" => new
                {
                    type = "object",
                    properties = new
                    {
                        entity_name = new { type = "string", description = "Name of the entity containing the attribute." },
                        attribute_name = new { type = "string", description = "Name of the attribute to make calculated." },
                        microflow = new { type = "string", description = "Name of the microflow that computes the value." },
                        module_name = new { type = "string", description = "Module containing the entity. Searches all modules if omitted." }
                    },
                    required = new[] { "entity_name", "attribute_name", "microflow" }
                },
                "read_domain_model" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string", description = "Module name to read. If omitted, returns domain models from all non-Marketplace modules." }
                    },
                    required = new string[0]
                },
                "create_entity" => new
                {
                    type = "object",
                    properties = new
                    {
                        entity_name = new { type = "string" },
                        module_name = new { type = "string", description = "Target module name. Falls back to default module if omitted." },
                        attributes = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string" },
                                    type = new { type = "string", description = "String, Integer, Long, Decimal, Boolean, DateTime, AutoNumber, Binary, HashedString, or Enumeration." },
                                    default_value = new { type = "string", description = "Optional default value for the attribute." },
                                    enumerationValues = new
                                    {
                                        type = "array",
                                        items = new { type = "string" }
                                    }
                                }
                            }
                        }
                    },
                    required = new[] { "entity_name", "attributes" }
                },
                "create_association" => new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string" },
                        parent = new { type = "string" },
                        child = new { type = "string" },
                        type = new { type = "string" },
                        module_name = new { type = "string", description = "Default module for both entities. Can be overridden per entity." },
                        parent_module = new { type = "string", description = "Module containing the parent entity. Overrides module_name." },
                        child_module = new { type = "string", description = "Module containing the child entity. Overrides module_name." },
                        parent_delete_behavior = new { type = "string", description = "Behavior when parent is deleted: delete_me_and_references (cascade), delete_me_but_keep_references (default), delete_me_if_no_references (prevent)." },
                        child_delete_behavior = new { type = "string", description = "Behavior when child is deleted: delete_me_and_references (cascade), delete_me_but_keep_references (default), delete_me_if_no_references (prevent)." },
                        owner = new { type = "string", description = "Association owner: 'default' (child owns) or 'both' (bidirectional)." }
                    },
                    required = new[] { "name", "parent", "child" }
                },
                "create_multiple_associations" => new
                {
                    type = "object",
                    properties = new
                    {
                        associations = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string" },
                                    parent = new { type = "string" },
                                    child = new { type = "string" },
                                    type = new { type = "string" },
                                    parent_module = new { type = "string", description = "Module containing the parent entity." },
                                    child_module = new { type = "string", description = "Module containing the child entity." },
                                    parent_delete_behavior = new { type = "string", description = "Behavior when parent is deleted: delete_me_and_references, delete_me_but_keep_references (default), delete_me_if_no_references." },
                                    child_delete_behavior = new { type = "string", description = "Behavior when child is deleted: delete_me_and_references, delete_me_but_keep_references (default), delete_me_if_no_references." },
                                    owner = new { type = "string", description = "Association owner: 'default' or 'both'." }
                                },
                                required = new[] { "name", "parent", "child" }
                            }
                        }
                    },
                    required = new[] { "associations" }
                },
                "create_domain_model_from_schema" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string", description = "Target module for entity creation. Falls back to default module if omitted." },
                        schema = new
                        {
                            type = "object",
                            properties = new
                            {
                                entities = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            entity_name = new { type = "string" },
                                            attributes = new
                                            {
                                                type = "array",
                                                items = new
                                                {
                                                    type = "object",
                                                    properties = new
                                                    {
                                                        name = new { type = "string" },
                                                        type = new { type = "string" },
                                                        enumerationValues = new
                                                        {
                                                            type = "array",
                                                            items = new { type = "string" }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                },
                                associations = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            name = new { type = "string" },
                                            parent = new { type = "string" },
                                            child = new { type = "string" },
                                            type = new { type = "string" }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    required = new[] { "schema" }
                },
                "delete_model_element" => new
                {
                    type = "object",
                    properties = new
                    {
                        element_type = new { type = "string", description = "Type to delete: entity, attribute, association, microflow, constant, enumeration" },
                        entity_name = new { type = "string", description = "Entity name (required for entity/attribute/association; also used as fallback for document_name)" },
                        document_name = new { type = "string", description = "Document name (for microflow/constant/enumeration deletion)" },
                        attribute_name = new { type = "string", description = "Attribute name (for attribute deletion)" },
                        association_name = new { type = "string", description = "Association name (for association deletion)" },
                        module_name = new { type = "string", description = "Module containing the element. Falls back to default module if omitted." }
                    },
                    required = new[] { "element_type" }
                },
                "diagnose_associations" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string", description = "Module to diagnose. Falls back to default module if omitted." }
                    },
                    required = new string[0]
                },
                "create_multiple_entities" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string", description = "Default module for all entities. Individual entities can override with their own module_name." },
                        entities = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    entity_name = new { type = "string" },
                                    module_name = new { type = "string", description = "Override module for this specific entity." },
                                    attributes = new
                                    {
                                        type = "array",
                                        items = new
                                        {
                                            type = "object",
                                            properties = new
                                            {
                                                name = new { type = "string" },
                                                type = new { type = "string" },
                                                enumerationValues = new
                                                {
                                                    type = "array",
                                                    items = new { type = "string" }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    required = new[] { "entities" }
                },
                "save_data" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string", description = "Target module for data validation. Falls back to default module if omitted." },
                        data = new {
                            type = "object",
                            description = "Entity data organized by ModuleName.EntityName keys with arrays of records containing VirtualId for relationships",
                            additionalProperties = new {
                                type = "array",
                                items = new {
                                    type = "object",
                                    properties = new {
                                        VirtualId = new { type = "string", description = "Unique temporary identifier for establishing relationships" }
                                    },
                                    required = new[] { "VirtualId" }
                                }
                            }
                        }
                    },
                    required = new[] { "data" }
                },
                "generate_overview_pages" => new
                {
                    type = "object",
                    properties = new
                    {
                        entity_names = new
                        {
                            type = "array",
                            items = new { type = "string" }
                        },
                        generate_index_snippet = new { type = "boolean" },
                        module_name = new { type = "string", description = "Module containing the entities. Falls back to default module if omitted." }
                    },
                    required = new[] { "entity_names" }
                },
                "list_microflows" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string", description = "Module to list microflows from. Falls back to default module if omitted." }
                    },
                    required = new string[0]
                },
                "check_model" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string", description = "Module to check. If omitted, checks all non-Marketplace modules." }
                    },
                    required = new string[0]
                },
                "get_studio_pro_logs" => new
                {
                    type = "object",
                    properties = new
                    {
                        level = new { type = "string", description = "Log level filter: ERROR (default), WARN, INFO, or ALL." },
                        last_minutes = new { type = "integer", description = "Time window in minutes (default: 30). Only shows entries from this many minutes ago." }
                    },
                    required = new string[0]
                },
                "get_last_error" => new
                {
                    type = "object",
                    properties = new { },
                    required = new string[0]
                },
                "list_available_tools" => new
                {
                    type = "object",
                    properties = new { },
                    required = new string[0]
                },
                "debug_info" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string", description = "Module to debug. Falls back to default module if omitted." }
                    },
                    required = new string[0]
                },
                "read_microflow_details" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string", description = "Module containing the microflow. Falls back to default module if omitted." },
                        microflow_name = new { type = "string" }
                    },
                    required = new[] { "microflow_name" }
                },
                "create_microflow" => new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string" },
                        module_name = new { type = "string", description = "Target module. Falls back to default module if omitted." },
                        parameters = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string" },
                                    type = new { type = "string" }
                                },
                                required = new[] { "name", "type" }
                            }
                        },
                        returnType = new { type = "string" }
                    },
                    required = new[] { "name" }
                },
                "create_microflow_activities" => new
                {
                    type = "object",
                    properties = new
                    {
                        microflow_name = new { type = "string", description = "Name of the microflow to add activities to" },
                        module_name = new { type = "string", description = "Module containing the microflow. Falls back to default module if omitted." },
                        activities = new 
                        { 
                            type = "array", 
                            description = "Array of activity definitions to create in sequence. For single activities, use an array with one item.",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    activity_type = new { type = "string", description = "Type of activity to create (e.g., 'create_object', 'commit', 'retrieve_from_database')" },
                                    activity_config = new { 
                                        type = "object", 
                                        description = "Configuration object for the activity",
                                        additionalProperties = true
                                    }
                                },
                                required = new[] { "activity_type" }
                            }
                        }
                    },
                    required = new[] { "microflow_name", "activities" }
                },
                "check_project_errors" => new
                {
                    type = "object",
                    properties = new
                    {
                        studio_pro_version = new { type = "string", description = "Studio Pro version (e.g., '11.5.0'). Auto-detects latest if omitted." }
                    },
                    required = new string[0]
                },
                "create_constant" => new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Name of the constant" },
                        type = new { type = "string", description = "Data type: string, integer, boolean, decimal, datetime, float (default: string)" },
                        default_value = new { type = "string", description = "Default value for the constant" },
                        exposed_to_client = new { type = "boolean", description = "Whether the constant is exposed to the client (default: false)" },
                        module_name = new { type = "string", description = "Target module (default module if omitted)" }
                    },
                    required = new[] { "name" }
                },
                "list_constants" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string", description = "Module to list constants from. Lists all modules if omitted." }
                    },
                    required = new string[0]
                },
                "create_enumeration" => new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Name of the enumeration" },
                        values = new
                        {
                            type = "array",
                            description = "Enumeration values. Each item can be a string or {name, caption}.",
                            items = new { type = "object" }
                        },
                        module_name = new { type = "string", description = "Target module (default module if omitted)" }
                    },
                    required = new[] { "name", "values" }
                },
                "list_enumerations" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string", description = "Module to list enumerations from. Lists all modules if omitted." }
                    },
                    required = new string[0]
                },
                "read_project_info" => new
                {
                    type = "object",
                    properties = new { },
                    required = new string[0]
                },
                "configure_system_attributes" => new
                {
                    type = "object",
                    properties = new
                    {
                        entity_name = new { type = "string", description = "Name of the entity to configure." },
                        module_name = new { type = "string", description = "Module containing the entity. Searches all modules if omitted." },
                        has_created_date = new { type = "boolean", description = "Store the date and time of when the object is created." },
                        has_changed_date = new { type = "boolean", description = "Store the date and time of the last change." },
                        has_owner = new { type = "boolean", description = "Store the owner (creator) of the object." },
                        has_changed_by = new { type = "boolean", description = "Store the user who last changed the object." },
                        persistable = new { type = "boolean", description = "Whether the entity is persistable (stored in database) or non-persistable (in-memory only)." }
                    },
                    required = new[] { "entity_name" }
                },
                "manage_folders" => new
                {
                    type = "object",
                    properties = new
                    {
                        action = new { type = "string", description = "Action: 'list', 'create', or 'move_document'." },
                        module_name = new { type = "string", description = "Target module." },
                        folder_name = new { type = "string", description = "Name for the new folder (for 'create' action)." },
                        parent_folder = new { type = "string", description = "Parent folder name for nested creation (for 'create' action)." },
                        document_name = new { type = "string", description = "Name of the document to move (for 'move_document' action)." },
                        target_folder = new { type = "string", description = "Target folder to move document to (for 'move_document' action)." }
                    },
                    required = new[] { "action", "module_name" }
                },
                "validate_name" => new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "The candidate name to validate." },
                        auto_fix = new { type = "boolean", description = "If true and name is invalid, returns a corrected valid name. Default: false." }
                    },
                    required = new[] { "name" }
                },
                "copy_model_element" => new
                {
                    type = "object",
                    properties = new
                    {
                        element_type = new { type = "string", description = "Type of element: 'entity', 'microflow', 'constant', or 'enumeration'." },
                        source_name = new { type = "string", description = "Name of the element to copy." },
                        new_name = new { type = "string", description = "Name for the copy." },
                        source_module = new { type = "string", description = "Module containing the source element. Searches all modules if omitted." },
                        target_module = new { type = "string", description = "Module to place the copy in. Same as source if omitted." }
                    },
                    required = new[] { "element_type", "source_name", "new_name" }
                },
                "list_java_actions" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string", description = "Module to list Java actions from. Lists all modules if omitted." }
                    },
                    required = new string[0]
                },
                "read_runtime_settings" => new
                {
                    type = "object",
                    properties = new { },
                    required = new string[0]
                },
                "set_runtime_settings" => new
                {
                    type = "object",
                    properties = new
                    {
                        after_startup_microflow = new { type = "string", description = "Qualified name of microflow to run after startup (e.g. 'MyModule.ASu_Startup')" },
                        before_shutdown_microflow = new { type = "string", description = "Qualified name of microflow to run before shutdown" },
                        health_check_microflow = new { type = "string", description = "Qualified name of microflow for health check endpoint" },
                        clear_after_startup = new { type = "boolean", description = "Set true to clear the after-startup microflow assignment" },
                        clear_before_shutdown = new { type = "boolean", description = "Set true to clear the before-shutdown microflow assignment" },
                        clear_health_check = new { type = "boolean", description = "Set true to clear the health-check microflow assignment" }
                    },
                    required = new string[0]
                },
                "read_configurations" => new
                {
                    type = "object",
                    properties = new
                    {
                        configuration_name = new { type = "string", description = "Name of specific configuration to read. Lists all if omitted." }
                    },
                    required = new string[0]
                },
                "set_configuration" => new
                {
                    type = "object",
                    properties = new
                    {
                        configuration_name = new { type = "string", description = "Name of the configuration to create or update" },
                        application_root_url = new { type = "string", description = "Application root URL (e.g. 'http://localhost:8080/')" },
                        custom_settings = new { type = "array", description = "Array of {name, value} objects for custom runtime settings", items = new { type = "object" } },
                        create_if_missing = new { type = "boolean", description = "Create the configuration if it doesn't exist (default: true)" }
                    },
                    required = new[] { "configuration_name" }
                },
                "read_version_control" => new
                {
                    type = "object",
                    properties = new { },
                    required = new string[0]
                },
                "set_microflow_url" => new
                {
                    type = "object",
                    properties = new
                    {
                        microflow_name = new { type = "string", description = "Name of the microflow" },
                        module_name = new { type = "string", description = "Module containing the microflow (searches all if omitted)" },
                        url = new { type = "string", description = "URL to set (e.g. '/api/v1/myendpoint'). Omit to read current URL. Set empty string to clear." }
                    },
                    required = new[] { "microflow_name" }
                },
                "list_rules" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string", description = "Module to list rules from. Lists all modules if omitted." }
                    },
                    required = new string[0]
                },
                "exclude_document" => new
                {
                    type = "object",
                    properties = new
                    {
                        document_name = new { type = "string", description = "Name of the document to exclude/include" },
                        module_name = new { type = "string", description = "Module containing the document (searches all if omitted)" },
                        excluded = new { type = "boolean", description = "True to exclude, false to include (default: true)" }
                    },
                    required = new[] { "document_name" }
                },
                _ => new
                {
                    type = "object",
                    properties = new { },
                    required = new string[0]
                }
            };
        }

        public void Stop()
        {
            _isRunning = false;
            _webHost?.StopAsync().Wait(5000);
            _webHost?.Dispose();
        }
    }
}
