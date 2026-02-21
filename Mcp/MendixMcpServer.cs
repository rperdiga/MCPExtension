using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MCPExtension.MCP;
using MCPExtension.Tools;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace MCPExtension
{
    public class MendixMcpServer
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MendixMcpServer> _logger;
        private McpServer? _mcpServer;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _serverTask;
        private readonly int _port;
        private bool _isRunning;

        private readonly string? _projectDirectory;

        public event Action<ToolCallEventArgs>? OnToolCallEvent;
        public int ActiveSseConnections => _mcpServer?.ActiveSseConnections ?? 0;
        public int TotalToolCalls => _mcpServer?.TotalToolCalls ?? 0;

        public MendixMcpServer(IServiceProvider serviceProvider, ILogger<MendixMcpServer> logger, int port = 3001, string? projectDirectory = null)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _port = port;
            _projectDirectory = projectDirectory;
        }

        public async Task StartAsync()
        {
            try
            {
                _logger.LogInformation("Starting Mendix MCP Server...");

                var mcpLogger = _serviceProvider.GetRequiredService<ILogger<McpServer>>();
                _mcpServer = new McpServer(mcpLogger, _port, _projectDirectory);

                // Relay tool call events to subscribers
                _mcpServer.OnToolCallEvent += (args) => OnToolCallEvent?.Invoke(args);

                // Register tools
                _logger.LogInformation("Registering MCP tools...");
                RegisterTools();

                _cancellationTokenSource = new CancellationTokenSource();
                
                _logger.LogInformation($"Starting MCP server on port {_port}...");
                _serverTask = Task.Run(() => _mcpServer.RunAsync(_cancellationTokenSource.Token));

                // Wait a moment for the server to start
                await Task.Delay(2000); // Increased delay to ensure server starts

                _isRunning = true;
                _logger.LogInformation($"Mendix MCP Server started successfully on http://localhost:{_port}");
            }
            catch (Exception ex)
            {
                _isRunning = false;
                _logger.LogError(ex, "Failed to start Mendix MCP Server");
                throw;
            }
        }

        private void RegisterTools()
        {
            var currentApp = _serviceProvider.GetRequiredService<IModel>();
            
            // Create tool instances with dependencies
            var nameValidationService = _serviceProvider.GetService<Mendix.StudioPro.ExtensionsAPI.Services.INameValidationService>();
            var domainModelTools = new MendixDomainModelTools(currentApp, _serviceProvider.GetRequiredService<ILogger<MendixDomainModelTools>>(), nameValidationService);
            var additionalTools = new MendixAdditionalTools(
                currentApp,
                _serviceProvider.GetRequiredService<ILogger<MendixAdditionalTools>>(),
                _serviceProvider.GetRequiredService<IPageGenerationService>(),
                _serviceProvider.GetRequiredService<INavigationManagerService>(),
                _serviceProvider,
                _projectDirectory
            );

            // Register domain model tools with wrapper functions
            _mcpServer.RegisterTool("list_modules", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.ListModules(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("create_module", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.CreateModule(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("set_entity_generalization", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.SetEntityGeneralization(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("remove_entity_generalization", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.RemoveEntityGeneralization(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("add_event_handler", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.AddEventHandler(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("add_attribute", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.AddAttribute(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("set_calculated_attribute", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.SetCalculatedAttribute(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("read_domain_model", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.ReadDomainModel(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("create_entity", async (JsonObject parameters) => 
            {
                var result = await domainModelTools.CreateEntity(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("create_association", async (JsonObject parameters) => 
            {
                var result = await domainModelTools.CreateAssociation(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("delete_model_element", async (JsonObject parameters) => 
            {
                var result = await domainModelTools.DeleteModelElement(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("diagnose_associations", async (JsonObject parameters) => 
            {
                var result = await domainModelTools.DiagnoseAssociations(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("create_multiple_entities", async (JsonObject parameters) => 
            {
                var result = await domainModelTools.CreateMultipleEntities(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("create_multiple_associations", async (JsonObject parameters) => 
            {
                var result = await domainModelTools.CreateMultipleAssociations(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("create_domain_model_from_schema", async (JsonObject parameters) => 
            {
                var result = await domainModelTools.CreateDomainModelFromSchema(parameters);
                return (object)result;
            });

            // Register additional tools with wrapper functions
            _mcpServer.RegisterTool("save_data", async (JsonObject parameters) => 
            {
                var result = await additionalTools.SaveData(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("generate_overview_pages", async (JsonObject parameters) => 
            {
                var result = await additionalTools.GenerateOverviewPages(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("list_microflows", async (JsonObject parameters) => 
            {
                var result = await additionalTools.ListMicroflows(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("check_model", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.CheckModel(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("create_constant", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.CreateConstant(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("list_constants", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.ListConstants(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("create_enumeration", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.CreateEnumeration(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("list_enumerations", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.ListEnumerations(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("read_project_info", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.ReadProjectInfo(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("get_studio_pro_logs", async (JsonObject parameters) =>
            {
                var result = await additionalTools.GetStudioProLogs(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("check_project_errors", async (JsonObject parameters) =>
            {
                var result = await additionalTools.CheckProjectErrors(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("get_last_error", async (JsonObject parameters) =>
            {
                var result = await additionalTools.GetLastError(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("list_available_tools", async (JsonObject parameters) => 
            {
                var result = await additionalTools.ListAvailableTools(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("debug_info", async (JsonObject parameters) => 
            {
                var result = await additionalTools.DebugInfo(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("read_microflow_details", async (JsonObject parameters) => 
            {
                var result = await additionalTools.ReadMicroflowDetails(parameters);
                return (object)result;
            });

            // Register create_microflow tool with special handling
            _mcpServer.RegisterTool("create_microflow", async (JsonObject parameters) => 
            {
                // Create a specialized microflow handler that has access to IMicroflowService and IServiceProvider
                var microflowService = _serviceProvider.GetRequiredService<IMicroflowService>();
                var result = await additionalTools.CreateMicroflowWithService(parameters, microflowService, _serviceProvider);
                return (object)result;
            });

            // Register create_microflow_activities tool (replaces both individual and sequence)
            _mcpServer.RegisterTool("create_microflow_activities", async (JsonObject parameters) => 
            {
                _logger.LogInformation("=== MCP Tool create_microflow_activities Called ===");
                _logger.LogInformation($"Parameters received in MCP server: {parameters?.ToJsonString()}");
                var result = await additionalTools.CreateMicroflowActivitiesSequence(parameters);
                _logger.LogInformation($"Result from CreateMicroflowActivitiesSequence: {result}");
                return (object)result;
            });

            // Phase 9: Entity Configuration & Module Organization
            _mcpServer.RegisterTool("configure_system_attributes", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.ConfigureSystemAttributes(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("manage_folders", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.ManageFolders(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("validate_name", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.ValidateName(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("copy_model_element", async (JsonObject parameters) =>
            {
                var result = await domainModelTools.CopyModelElement(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("list_java_actions", async (JsonObject parameters) =>
            {
                var result = await additionalTools.ListJavaActions(parameters);
                return (object)result;
            });

            // Phase 10: Project Settings & Runtime Configuration
            _mcpServer.RegisterTool("read_runtime_settings", async (JsonObject parameters) =>
            {
                var result = await additionalTools.ReadRuntimeSettings(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("set_runtime_settings", async (JsonObject parameters) =>
            {
                var result = await additionalTools.SetRuntimeSettings(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("read_configurations", async (JsonObject parameters) =>
            {
                var result = await additionalTools.ReadConfigurations(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("set_configuration", async (JsonObject parameters) =>
            {
                var result = await additionalTools.SetConfiguration(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("read_version_control", async (JsonObject parameters) =>
            {
                var result = await additionalTools.ReadVersionControl(parameters);
                return (object)result;
            });

            // Phase 11: Advanced Microflow Operations
            _mcpServer.RegisterTool("set_microflow_url", async (JsonObject parameters) =>
            {
                var result = await additionalTools.SetMicroflowUrl(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("list_rules", async (JsonObject parameters) =>
            {
                var result = await additionalTools.ListRules(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("exclude_document", async (JsonObject parameters) =>
            {
                var result = await additionalTools.ExcludeDocument(parameters);
                return (object)result;
            });

            // Phase 12: Untyped Model Introspection
            _mcpServer.RegisterTool("read_security_info", async (JsonObject parameters) =>
            {
                var result = await additionalTools.ReadSecurityInfo(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("list_nanoflows", async (JsonObject parameters) =>
            {
                var result = await additionalTools.ListNanoflows(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("list_scheduled_events", async (JsonObject parameters) =>
            {
                var result = await additionalTools.ListScheduledEvents(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("list_rest_services", async (JsonObject parameters) =>
            {
                var result = await additionalTools.ListRestServices(parameters);
                return (object)result;
            });
            _mcpServer.RegisterTool("query_model_elements", async (JsonObject parameters) =>
            {
                var result = await additionalTools.QueryModelElements(parameters);
                return (object)result;
            });

            _logger.LogInformation("MCP tools registered successfully");
        }

        public async Task StopAsync()
        {
            try
            {
                _logger.LogInformation("Stopping Mendix MCP Server...");

                if (_mcpServer != null)
                {
                    _mcpServer.Stop();
                }

                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Cancel();
                }

                if (_serverTask != null)
                {
                    await _serverTask;
                }

                _isRunning = false;
                _logger.LogInformation("Mendix MCP Server stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Mendix MCP Server");
            }
        }

        public async Task<object> GetStatusAsync()
        {
            var status = new
            {
                isRunning = _isRunning && _serverTask != null && !_serverTask.IsCompleted,
                serverTaskStatus = _serverTask?.Status.ToString() ?? "Not Started",
                registeredTools = 50, // Phase 12: +5 untyped model introspection tools
                port = _port,
                sseEndpoint = $"http://localhost:{_port}/sse",
                healthEndpoint = $"http://localhost:{_port}/health",
                metadataEndpoint = $"http://localhost:{_port}/.well-known/mcp"
            };

            return status;
        }

        public string GetConnectionInfo()
        {
            return $"MCP Server running on http://localhost:{_port}\n" +
                   $"SSE Endpoint: http://localhost:{_port}/sse\n" +
                   $"Health Check: http://localhost:{_port}/health\n" +
                   $"MCP Metadata: http://localhost:{_port}/.well-known/mcp";
        }

        public bool IsRunning => _isRunning && _serverTask != null && !_serverTask.IsCompleted;

        public int Port => _port;
    }
}
