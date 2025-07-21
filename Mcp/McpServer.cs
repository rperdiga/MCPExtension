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

namespace MCPExtension.MCP
{
    public class McpServer
    {
        private readonly ILogger<McpServer> _logger;
        private readonly Dictionary<string, Func<JsonObject, Task<object>>> _tools;
        private bool _isRunning;
        private IWebHost _webHost;
        private int _port;

        private readonly string _projectDirectory;

        public McpServer(ILogger<McpServer> logger, int port = 3001, string projectDirectory = null)
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

            LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] SSE client connected from {context.Connection.RemoteIpAddress}");
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
                    // Keepalive sent silently - no logging to prevent log file clutter
                }
            }
            catch (Exception ex)
            {
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] SSE connection error: {ex}");
                _logger.LogError(ex, "SSE connection error");
            }
            finally
            {
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] SSE client disconnected");
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
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Handling tools/list request");
                    var toolsResponse = CreateToolsListResponse(id);
                    LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Tools list response created with {_tools.Count} tools");
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
                tools.Add(new
                {
                    name = toolName,
                    description = GetToolDescription(toolName),
                    inputSchema = GetToolInputSchema(toolName)
                });
            }

            return new
            {
                jsonrpc = "2.0",
                id = id?.AsValue(),
                result = new
                {
                    tools = tools
                }
            };
        }

        private async Task<object> HandleToolCall(JsonNode id, JsonObject paramsObj)
        {
            try
            {
                var toolName = paramsObj["name"]?.ToString();
                var arguments = paramsObj["arguments"]?.AsObject();

                if (string.IsNullOrEmpty(toolName) || !_tools.ContainsKey(toolName))
                {
                    return CreateErrorResponse(id, "Tool not found", $"Unknown tool: {toolName}");
                }

                // Log the tool call and arguments for debugging
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Tool call: {toolName}");
                LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Arguments: {JsonSerializer.Serialize(arguments)}");

                var result = await _tools[toolName](arguments ?? new JsonObject());

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
                "read_domain_model" => "Read the current domain model structure",
                "create_entity" => "Create a new entity in the domain model",
                "create_association" => "Create a new association between entities",
                "delete_model_element" => "Delete an element from the domain model",
                "diagnose_associations" => "Diagnose association creation issues",
                "create_multiple_entities" => "Create multiple entities at once",
                "create_multiple_associations" => "Create multiple associations at once",
                "create_domain_model_from_schema" => "Create a complete domain model from a schema definition",
                "save_data" => "Generate realistic sample data for Mendix domain model entities",
                "generate_overview_pages" => "Generate overview pages for entities",
                "list_microflows" => "List all microflows in a module",
                "get_last_error" => "Get details about the last error",
                "list_available_tools" => "List all available tools",
                "debug_info" => "Get comprehensive debug information about the domain model",
                "read_microflow_details" => "Get details about a specific microflow",
                "create_microflow" => "Create a new microflow in the module with parameters and return type",
                _ => "Tool description not available"
            };
        }

        private object GetToolInputSchema(string toolName)
        {
            return toolName switch
            {
                "read_domain_model" => new
                {
                    type = "object",
                    properties = new { },
                    required = new string[0]
                },
                "create_entity" => new
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
                        type = new { type = "string" }
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
                                    type = new { type = "string" }
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
                        element_type = new { type = "string" },
                        entity_name = new { type = "string" },
                        attribute_name = new { type = "string" },
                        association_name = new { type = "string" }
                    },
                    required = new[] { "element_type", "entity_name" }
                },
                "diagnose_associations" => new
                {
                    type = "object",
                    properties = new { },
                    required = new string[0]
                },
                "create_multiple_entities" => new
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
                        }
                    },
                    required = new[] { "entities" }
                },
                "save_data" => new
                {
                    type = "object",
                    properties = new
                    {
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
                        generate_index_snippet = new { type = "boolean" }
                    },
                    required = new[] { "entity_names" }
                },
                "list_microflows" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string" }
                    },
                    required = new[] { "module_name" }
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
                    properties = new { },
                    required = new string[0]
                },
                "read_microflow_details" => new
                {
                    type = "object",
                    properties = new
                    {
                        module_name = new { type = "string" },
                        microflow_name = new { type = "string" }
                    },
                    required = new[] { "module_name", "microflow_name" }
                },
                "create_microflow" => new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string" },
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

        public int Port => _port;
    }
}
