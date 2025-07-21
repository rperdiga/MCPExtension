using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MCPExtension.Tools;
using System.Text.Json;
using System.Linq;

namespace MCPExtension
{
    [Export(typeof(DockablePaneExtension))]
    public class AIAPIEngine : DockablePaneExtension
    {
        public const string ID = "AIAPIEngine";
        public override string Id => ID;
        private MendixMcpServer _mcpServer;
        private IServiceProvider _serviceProvider;
        private ILogger<AIAPIEngine> _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly int _mcpPort;
        private AIAPIEngineViewModel _currentViewModel;
        private readonly IPageGenerationService _pageGenerationService;
        private readonly INavigationManagerService _navigationManagerService;
        private readonly IMicroflowService _microflowService;
        private readonly IMicroflowExpressionService _microflowExpressionService;

        // Public property for the ViewModel to check server status
        public MendixMcpServer McpServer => _mcpServer;
        
        // Public property for the ViewModel to access the current app
        public IModel CurrentAppModel => CurrentApp;

        [ImportingConstructor]
        public AIAPIEngine(
            IPageGenerationService pageGenerationService,
            INavigationManagerService navigationManagerService,
            IMicroflowService microflowService,
            IMicroflowExpressionService microflowExpressionService)
        {
            _pageGenerationService = pageGenerationService;
            _navigationManagerService = navigationManagerService;
            _microflowService = microflowService;
            _microflowExpressionService = microflowExpressionService;
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            
            // Use a random available port starting from 3001
            _mcpPort = FindAvailablePort(3001);
        }

        private int FindAvailablePort(int startPort)
        {
            var tcpListeners = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            for (int port = startPort; port < startPort + 100; port++)
            {
                if (!tcpListeners.Any(l => l.Port == port))
                {
                    return port;
                }
            }
            return startPort; // Fallback to original port
        }

        public override DockablePaneViewModelBase Open()
        {
            // Initialize services when the pane is opened and we have access to CurrentApp
            if (CurrentApp != null && _mcpServer == null)
            {
                InitializeServices();
            }
            _currentViewModel = new AIAPIEngineViewModel("MCP Server", this);
            return _currentViewModel;
        }

        private void InitializeServices()
        {
            // Configure services
            var services = new ServiceCollection();
            
            // Add logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Register Mendix services
            services.AddSingleton<IModel>(CurrentApp);
            services.AddSingleton(_pageGenerationService);
            services.AddSingleton(_navigationManagerService);
            services.AddSingleton(_microflowService);
            services.AddSingleton(_microflowExpressionService);

            // Register our tools
            services.AddSingleton<MendixDomainModelTools>();
            services.AddSingleton<MendixAdditionalTools>(provider => 
                new MendixAdditionalTools(
                    provider.GetRequiredService<IModel>(),
                    provider.GetRequiredService<ILogger<MendixAdditionalTools>>(),
                    provider.GetRequiredService<IPageGenerationService>(),
                    provider.GetRequiredService<INavigationManagerService>(),
                    provider
                ));

            // Build service provider
            _serviceProvider = services.BuildServiceProvider();

            // Get logger
            _logger = _serviceProvider.GetRequiredService<ILogger<AIAPIEngine>>();

            // Create MCP server
            var mcpLogger = _serviceProvider.GetRequiredService<ILogger<MendixMcpServer>>();
            
            // Get the Mendix project directory
            var project = CurrentApp.Root as IProject;
            string projectDirectory = project?.DirectoryPath;
            
            _mcpServer = new MendixMcpServer(_serviceProvider, mcpLogger, _mcpPort, projectDirectory);
        }

        public async Task<string> StartAPIEngineAsync()
        {
            try
            {
                // Clear the log file when starting the MCP server
                ClearLogFile();
                
                if (CurrentApp == null)
                {
                    return SerializeResponse("No current application available.", false);
                }

                if (_mcpServer == null)
                {
                    InitializeServices();
                }

                // Start server asynchronously to avoid deadlock
                await _mcpServer.StartAsync();
                _logger?.LogInformation("MCP Server started successfully");
                
                // Return immediately with connection info
                var connectionInfo = _mcpServer.GetConnectionInfo();
                return SerializeResponse($"MCP Server started successfully.\n\n{connectionInfo}");
            }
            catch (Exception ex)
            {
                return SerializeResponse($"Error starting MCP Server: {ex.Message}", false);
            }
        }

        public async Task<string> StopAPIEngineAsync()
        {
            try
            {
                if (_mcpServer == null)
                {
                    return SerializeResponse("MCP Server is not initialized.");
                }

                // Stop server asynchronously to avoid deadlock
                await _mcpServer.StopAsync();
                _logger?.LogInformation("MCP Server stopped successfully");

                return SerializeResponse("MCP Server stopped successfully.");
            }
            catch (Exception ex)
            {
                return SerializeResponse($"Error stopping MCP Server: {ex.Message}", false);
            }
        }

        // Keep synchronous versions for backward compatibility
        public string StartAPIEngine()
        {
            try
            {
                if (CurrentApp == null)
                {
                    return SerializeResponse("No current application available.", false);
                }

                if (_mcpServer == null)
                {
                    InitializeServices();
                }

                // Start server synchronously like the old version
                _mcpServer.StartAsync().Wait();
                _logger?.LogInformation("MCP Server started successfully");
                
                // Return immediately with connection info
                var connectionInfo = _mcpServer.GetConnectionInfo();
                return SerializeResponse($"MCP Server started successfully.\n\n{connectionInfo}");
            }
            catch (Exception ex)
            {
                return SerializeResponse($"Error starting MCP Server: {ex.Message}", false);
            }
        }

        public string StopAPIEngine()
        {
            try
            {
                if (_mcpServer == null)
                {
                    return SerializeResponse("MCP Server is not initialized.");
                }

                // Stop server synchronously like the old version
                _mcpServer.StopAsync().Wait();
                _logger?.LogInformation("MCP Server stopped successfully");

                return SerializeResponse("MCP Server stopped successfully.");
            }
            catch (Exception ex)
            {
                return SerializeResponse($"Error stopping MCP Server: {ex.Message}", false);
            }
        }

        public string GetServerStatus()
        {
            try
            {
                if (_mcpServer == null)
                {
                    return SerializeResponse("MCP Server is not initialized.");
                }

                // Get status without blocking UI thread
                var isRunning = _mcpServer.IsRunning;
                return SerializeResponse(isRunning ? "MCP Server is Running" : "MCP Server is Stopped");
            }
            catch (Exception ex)
            {
                return SerializeResponse($"Error getting server status: {ex.Message}", false);
            }
        }

        private string GetLogFilePath()
        {
            try
            {
                // Use the Mendix project directory instead of the extension assembly location
                var project = CurrentApp.Root as IProject;
                if (project?.DirectoryPath == null)
                {
                    throw new InvalidOperationException("Could not determine Mendix project directory");
                }

                string resourcesDir = System.IO.Path.Combine(project.DirectoryPath, "resources");
                if (!System.IO.Directory.Exists(resourcesDir))
                {
                    System.IO.Directory.CreateDirectory(resourcesDir);
                }
                
                return System.IO.Path.Combine(resourcesDir, "mcp_debug.log");
            }
            catch (Exception ex)
            {
                // Fallback to current directory if we can't determine project directory
                System.Diagnostics.Debug.WriteLine($"Could not determine log file path: {ex.Message}");
                return System.IO.Path.Combine(Environment.CurrentDirectory, "mcp_debug.log");
            }
        }

        private void ClearLogFile()
        {
            try
            {
                var logFilePath = GetLogFilePath();
                if (System.IO.File.Exists(logFilePath))
                {
                    System.IO.File.WriteAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Log file cleared on MCP server start{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                // Ignore errors when clearing log file - don't want to break server start
                System.Diagnostics.Debug.WriteLine($"Could not clear log file: {ex.Message}");
            }
        }

        private string SerializeResponse(string message, bool success = true)
        {
            var response = new
            {
                success,
                message
            };

            return JsonSerializer.Serialize(response, _jsonOptions);
        }
    }
}
