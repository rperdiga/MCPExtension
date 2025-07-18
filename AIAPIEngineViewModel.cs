using Eto.Forms;
using Mendix.StudioPro.ExtensionsAPI.UI.WebView;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using System;
using System.Text.Json;
using System.Net.NetworkInformation;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace MCPExtension
{
    public class AIAPIEngineViewModel : WebViewDockablePaneViewModel
    {
        private readonly AIAPIEngine parentPanel;
        private IWebView? currentWebView;

        private bool IsPortInUse(int port)
        {
            try
            {
                IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();
                IPEndPoint[] ipEndPoints = ipProperties.GetActiveTcpListeners();
                return ipEndPoints.Any(endPoint => endPoint.Port == port);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private const string EMBEDDED_HTML = @"
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset=""UTF-8"">
            <title>MCP Server</title>
            <style>
                /* Common Base Styles */
                :root {
                    /* Color palette */
                    --primary-color: #007bff;
                    --success-color: #28a745;
                    --danger-color: #dc3545;
                    --background-color: #f8f9fa;
                    --border-color: #ddd;
                    --text-primary: #2c3e50;
                    --text-secondary: #6c757d;
                    --shadow-sm: 0 2px 4px rgba(0, 0, 0, 0.1);
                    --shadow-md: 0 4px 6px rgba(0, 0, 0, 0.1);
    
                    /* Spacing */
                    --spacing-sm: 8px;
                    --spacing-md: 16px;
                    --spacing-lg: 24px;
    
                    /* Border radius */
                    --border-radius-sm: 6px;
                    --border-radius-md: 8px;
                }

                /* Base Layout */
                body {
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                    margin: 0;
                    padding: var(--spacing-lg);
                    background-color: var(--background-color);
                    color: var(--text-primary);
                }

                /* Common Components */
                .panel {
                    background: white;
                    border-radius: var(--border-radius-md);
                    padding: var(--spacing-lg);
                    box-shadow: var(--shadow-sm);
                    margin-bottom: var(--spacing-lg);
                }

                /* Headers */
                h1 {
                    color: var(--text-primary);
                    font-size: 24px;
                    margin-bottom: var(--spacing-lg);
                    display: flex;
                    align-items: center;
                    gap: var(--spacing-md);
                }

                /* Buttons */
                button {
                    padding: var(--spacing-sm) var(--spacing-lg);
                    border: none;
                    border-radius: var(--border-radius-md);
                    cursor: pointer;
                    font-size: 16px;
                    transition: all 0.2s ease;
                    background-color: var(--primary-color);
                    color: white;
                }

                button:hover {
                    transform: translateY(-1px);
                    box-shadow: var(--shadow-sm);
                }

                button:disabled {
                    background-color: var(--text-secondary);
                    cursor: not-allowed;
                    transform: none;
                }

                /* Status Indicators */
                .status-indicator {
                    width: 12px;
                    height: 12px;
                    border-radius: 50%;
                    display: inline-block;
                }

                .status-running {
                    background-color: var(--success-color);
                    box-shadow: 0 0 8px var(--success-color);
                }

                .status-stopped {
                    background-color: var(--danger-color);
                    box-shadow: 0 0 8px var(--danger-color);
                }

                /* Tree View Specific */
                .tree-view {
                    background: white;
                    border-radius: var(--border-radius-md);
                    padding: var(--spacing-lg);
                    box-shadow: var(--shadow-sm);
                }

                .module-header, .entity-header {
                    padding: var(--spacing-md);
                    cursor: pointer;
                    border-radius: var(--border-radius-md);
                    display: flex;
                    align-items: center;
                    margin-bottom: var(--spacing-sm);
                }

                .module-header {
                    background-color: #e3e8ef;
                    border: 1px solid #d1d9e6;
                }

                .entity-header {
                    background-color: var(--background-color);
                    border: 1px solid var(--border-color);
                }

                /* Chat Panel Specific */
                #chatDisplay, #chatInput {
                    width: 100%;
                    border: 1px solid var(--border-color);
                    border-radius: var(--border-radius-md);
                    background-color: white;
                    box-shadow: var(--shadow-sm);
                }

                #chatDisplay {
                    height: 300px;
                    padding: var(--spacing-md);
                    margin-bottom: var(--spacing-md);
                    overflow-y: auto;
                }

                #chatInput {
                    height: 100px;
                    padding: var(--spacing-md);
                    resize: vertical;
                    font-family: inherit;
                }

                /* Modal Styles */
                .modal {
                    position: fixed;
                    top: 0;
                    left: 0;
                    width: 100%;
                    height: 100%;
                    background-color: rgba(0, 0, 0, 0.5);
                    display: none;
                    justify-content: center;
                    align-items: center;
                    z-index: 1000;
                    backdrop-filter: blur(2px);
                }

                .modal-content {
                    background-color: white;
                    padding: var(--spacing-lg);
                    border-radius: var(--border-radius-md);
                    width: 300px;
                    box-shadow: var(--shadow-md);
                }

                /* Form Elements */
                input {
                    width: 100%;
                    padding: var(--spacing-sm);
                    border: 1px solid var(--border-color);
                    border-radius: var(--border-radius-md);
                    margin-bottom: var(--spacing-md);
                }

                input:focus {
                    outline: none;
                    border-color: var(--primary-color);
                    box-shadow: 0 0 0 2px rgba(0, 123, 255, 0.25);
                }

                /* Engine Panel Specific */
                .control-panel {
                    background: white;
                    border-radius: var(--border-radius-md);
                    padding: var(--spacing-lg);
                    box-shadow: var(--shadow-sm);
                    margin-bottom: var(--spacing-lg);
                }

                #stopButton {
                    background-color: var(--danger-color);
                }

                .status-text {
                    font-size: 14px;
                    margin-left: var(--spacing-md);
                    color: var(--text-secondary);
                }

                /* Status message styles */
                .success {
                    color: #28a745;
                    font-weight: 500;
                }

                .error {
                    color: #dc3545;
                    font-weight: 500;
                }

                .info {
                    color: #17a2b8;
                    font-weight: 500;
                }
            </style>

            <script>
                function handleMessageFromHost(event) {
                    const data = event.data;
                    console.log('Received event:', event);
                    console.log('Event data:', data);
                    console.log('Data type:', typeof data);
                    console.log('Data message:', data.message);
                    
                    if (data.message === 'Running') {
                        console.log('Handling Running message');
                        const indicator = document.getElementById('statusIndicator');
                        const statusText = document.getElementById('statusText');
                        const startButton = document.getElementById('startButton');
                        const stopButton = document.getElementById('stopButton');
            
                        indicator.className = 'status-indicator status-running';
                        statusText.textContent = 'Running';
                        startButton.disabled = true;
                        stopButton.disabled = false;
            
                        const statusDiv = document.getElementById('status');
                        statusDiv.textContent = 'MCP Server started successfully';
                        statusDiv.className = 'success';
                    } 
                    else if (data.message === 'NotRunning') {
                        console.log('Handling NotRunning message');
                        const indicator = document.getElementById('statusIndicator');
                        const statusText = document.getElementById('statusText');
                        const startButton = document.getElementById('startButton');
                        const stopButton = document.getElementById('stopButton');
            
                        indicator.className = 'status-indicator status-stopped';
                        statusText.textContent = 'Stopped';
                        startButton.disabled = false;
                        stopButton.disabled = true;
            
                        const statusDiv = document.getElementById('status');
                        statusDiv.textContent = 'MCP Server stopped successfully';
                        statusDiv.className = 'success';
                    }
                    else {
                        console.log('No matching message handler for:', data.message);
                    }
                }

                function updateServerStatus(status) {
                    const indicator = document.getElementById('statusIndicator');
                    const statusText = document.getElementById('statusText');
                    const startButton = document.getElementById('startButton');
                    const stopButton = document.getElementById('stopButton');
        
                    if (status === true || status === 'running') {
                        indicator.className = 'status-indicator status-running';
                        statusText.textContent = 'Running';
                        startButton.disabled = true;
                        stopButton.disabled = false;
                    } else if (status === 'starting') {
                        indicator.className = 'status-indicator';
                        indicator.style.backgroundColor = '#ffc107'; // warning/orange color
                        statusText.textContent = 'Starting...';
                        startButton.disabled = true;
                        stopButton.disabled = true;
                    } else if (status === 'stopping') {
                        indicator.className = 'status-indicator';
                        indicator.style.backgroundColor = '#ffc107'; // warning/orange color
                        statusText.textContent = 'Stopping...';
                        startButton.disabled = true;
                        stopButton.disabled = true;
                    } else {
                        indicator.className = 'status-indicator status-stopped';
                        statusText.textContent = 'Stopped';
                        startButton.disabled = false;
                        stopButton.disabled = true;
                    }
                }

                function init() {
                    window.chrome.webview.addEventListener('message', handleMessageFromHost);
                    chrome.webview.postMessage({ message: 'MessageListenerRegistered' });
                }

                function startEngine() {
                    chrome.webview.postMessage({ message: 'startEngine' });
                }

                function stopEngine() {
                    chrome.webview.postMessage({ message: 'stopEngine' });
                }
            </script>




        </head>
        <body onload=""init()"">
            <h1>
                MCP Server (HTTP/SSE)
                <span id=""statusIndicator"" class=""status-indicator status-stopped""></span>
                <span id=""statusText"" class=""status-text"">Stopped</span>
            </h1>
            <div class=""control-panel"">
                <button id=""startButton"" onclick=""startEngine()"">Start MCP Server</button>
                <button id=""stopButton"" onclick=""stopEngine()"" style=""background-color: #dc3545;"" disabled>Stop MCP Server</button>
                <div id=""status""></div>
                <div id=""connectionInfo"" style=""margin-top: 16px; padding: 12px; background-color: #f8f9fa; border-radius: 6px; font-family: monospace; font-size: 12px; white-space: pre-line; display: none;""></div>
            </div>
        </body>
        </html>";
        public AIAPIEngineViewModel(string title, AIAPIEngine panel) : base()
        {
            Title = title;
            parentPanel = panel;
        }

        private string GetLogFilePath()
        {
            try
            {
                // Use the Mendix project directory instead of the extension assembly location
                var project = parentPanel.CurrentAppModel.Root as IProject;
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

        private void WebView_MessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            try
            {
                // Log to a file we can check
                var logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] WebView received message: {e.Message}";
                System.IO.File.AppendAllText(GetLogFilePath(), logMessage + Environment.NewLine);
                
                if (e.Message.Contains("MessageListenerRegistered"))
                {
                    System.IO.File.AppendAllText(GetLogFilePath(), "[MessageListenerRegistered] Checking server status..." + Environment.NewLine);
                    
                    // Check if MCP server is running by checking the actual server status
                    var isRunning = parentPanel.McpServer?.IsRunning ?? false;
                    System.IO.File.AppendAllText(GetLogFilePath(), $"[MessageListenerRegistered] MCP Server IsRunning: {isRunning}" + Environment.NewLine);
                    
                    var messageToSend = isRunning ? "Running" : "NotRunning";
                    System.IO.File.AppendAllText(GetLogFilePath(), $"[MessageListenerRegistered] Sending initial message: {messageToSend}" + Environment.NewLine);
                    currentWebView?.PostMessage(messageToSend);
                    return;
                }

                if (e.Message.Contains("startEngine"))
                {
                    System.IO.File.AppendAllText(GetLogFilePath(), "[startEngine] Command received" + Environment.NewLine);
                    
                    // Run on background thread to avoid blocking UI
                    Task.Run(async () =>
                    {
                        try
                        {
                            System.IO.File.AppendAllText(GetLogFilePath(), "[startEngine] Starting server on background thread..." + Environment.NewLine);
                            string result = await parentPanel.StartAPIEngineAsync();
                            System.IO.File.AppendAllText(GetLogFilePath(), $"[startEngine] StartAPIEngineAsync result: {result}" + Environment.NewLine);
                            
                            // Check actual server status after start
                            var isRunning = parentPanel.McpServer?.IsRunning ?? false;
                            var messageToSend = isRunning ? "Running" : "NotRunning";
                            System.IO.File.AppendAllText(GetLogFilePath(), $"[startEngine] Sending message after start: {messageToSend}" + Environment.NewLine);
                            
                            // Post message back to UI thread
                            Application.Instance.Invoke(() =>
                            {
                                currentWebView?.PostMessage(messageToSend);
                            });
                        }
                        catch (Exception ex)
                        {
                            System.IO.File.AppendAllText(GetLogFilePath(), $"[startEngine] Exception in background thread: {ex.Message}" + Environment.NewLine);
                            Application.Instance.Invoke(() =>
                            {
                                currentWebView?.PostMessage("NotRunning");
                            });
                        }
                    });
                }
                else if (e.Message.Contains("stopEngine"))
                {
                    System.IO.File.AppendAllText(GetLogFilePath(), "[stopEngine] Command received" + Environment.NewLine);
                    
                    // Run on background thread to avoid blocking UI
                    Task.Run(async () =>
                    {
                        try
                        {
                            System.IO.File.AppendAllText(GetLogFilePath(), "[stopEngine] Stopping server on background thread..." + Environment.NewLine);
                            string result = await parentPanel.StopAPIEngineAsync();
                            System.IO.File.AppendAllText(GetLogFilePath(), $"[stopEngine] StopAPIEngineAsync result: {result}" + Environment.NewLine);
                            
                            // Check actual server status after stop
                            var isRunning = parentPanel.McpServer?.IsRunning ?? false;
                            var messageToSend = isRunning ? "Running" : "NotRunning";
                            System.IO.File.AppendAllText(GetLogFilePath(), $"[stopEngine] Sending message after stop: {messageToSend}" + Environment.NewLine);
                            
                            // Post message back to UI thread
                            Application.Instance.Invoke(() =>
                            {
                                currentWebView?.PostMessage(messageToSend);
                            });
                        }
                        catch (Exception ex)
                        {
                            System.IO.File.AppendAllText(GetLogFilePath(), $"[stopEngine] Exception in background thread: {ex.Message}" + Environment.NewLine);
                            Application.Instance.Invoke(() =>
                            {
                                currentWebView?.PostMessage("NotRunning");
                            });
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                var errorMessage = $"[{DateTime.Now:HH:mm:ss.fff}] Exception in WebView_MessageReceived: {ex.Message}";
                System.IO.File.AppendAllText(GetLogFilePath(), errorMessage + Environment.NewLine);
                MessageBox.Show($"Error handling message from WebView: {ex.Message}\nStack trace: {ex.StackTrace}");
                currentWebView?.PostMessage("NotRunning");
            }
        }

        private void UpdateStatus(string message)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"UpdateStatus called with: {message}");
                // Send status message to JavaScript for display
                currentWebView?.PostMessage($"Status|{message}");
                System.Diagnostics.Debug.WriteLine($"Sent status message: Status|{message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating status: {ex.Message}");
            }
        }

        public override void InitWebView(IWebView webView)
        {
            try
            {
                currentWebView = webView;
                webView.MessageReceived -= WebView_MessageReceived;
                webView.MessageReceived += WebView_MessageReceived;

                string htmlContent = Uri.EscapeDataString(EMBEDDED_HTML);
                webView.Address = new Uri($"data:text/html,{htmlContent}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing WebView: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
        }

        // Methods to notify UI of server state changes from background tasks
        public void NotifyServerStarted(string connectionInfo)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"NotifyServerStarted called with: {connectionInfo}");
                UpdateStatus($"MCP Server started successfully");
                currentWebView?.PostMessage("Running");
                
                // Also log that we sent the Running message
                System.Diagnostics.Debug.WriteLine("Sent 'Running' message to WebView");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in NotifyServerStarted: {ex.Message}");
                UpdateStatus($"Error notifying server started: {ex.Message}");
            }
        }

        public void NotifyServerStartFailed(string errorMessage)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"NotifyServerStartFailed called with: {errorMessage}");
                UpdateStatus($"Failed to start MCP Server: {errorMessage}");
                currentWebView?.PostMessage("NotRunning");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in NotifyServerStartFailed: {ex.Message}");
                UpdateStatus($"Error notifying server start failed: {ex.Message}");
            }
        }

        public void NotifyServerStopped(string message)
        {
            try
            {
                UpdateStatus("MCP Server stopped");
                currentWebView?.PostMessage("NotRunning");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error notifying server stopped: {ex.Message}");
            }
        }

        public void NotifyServerStopFailed(string errorMessage)
        {
            try
            {
                UpdateStatus($"Failed to stop MCP Server: {errorMessage}");
                currentWebView?.PostMessage("Running");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error notifying server stop failed: {ex.Message}");
            }
        }
    }
}