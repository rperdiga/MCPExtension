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
        private McpServer _mcpServer;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _serverTask;
        private readonly int _port;
        private bool _isRunning;

        private readonly string _projectDirectory;

        public MendixMcpServer(IServiceProvider serviceProvider, ILogger<MendixMcpServer> logger, int port = 3001, string projectDirectory = null)
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
            var domainModelTools = new MendixDomainModelTools(currentApp, _serviceProvider.GetRequiredService<ILogger<MendixDomainModelTools>>());
            var additionalTools = new MendixAdditionalTools(
                currentApp, 
                _serviceProvider.GetRequiredService<ILogger<MendixAdditionalTools>>(),
                _serviceProvider.GetRequiredService<IPageGenerationService>(),
                _serviceProvider.GetRequiredService<INavigationManagerService>()
            );

            // Register domain model tools with wrapper functions
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
                registeredTools = 15, // Updated number of registered tools
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
