using Eto.Forms;
using Mendix.StudioPro.ExtensionsAPI.UI.WebView;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using MCPExtension.MCP;
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
        private Action<ToolCallEventArgs>? _toolCallHandler;

        private const string EMBEDDED_HTML = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <title>MCP Server</title>
    <style>
        :root {
            --primary: #3b82f6;
            --primary-hover: #2563eb;
            --success: #10b981;
            --danger: #ef4444;
            --danger-hover: #dc2626;
            --warning: #f59e0b;
            --bg: #f1f5f9;
            --bg-card: #ffffff;
            --bg-dark: #1e293b;
            --bg-header: #0f172a;
            --border: #e2e8f0;
            --text: #1e293b;
            --text-dim: #64748b;
            --text-muted: #94a3b8;
            --mono: 'Consolas', 'SF Mono', 'Monaco', monospace;
            --sans: -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif;
            --radius: 8px;
            --radius-sm: 4px;
        }

        * { margin: 0; padding: 0; box-sizing: border-box; }

        body {
            font-family: var(--sans);
            background: var(--bg);
            color: var(--text);
            height: 100vh;
            display: flex;
            flex-direction: column;
            overflow: hidden;
        }

        /* Header */
        .header {
            background: var(--bg-header);
            color: #fff;
            padding: 14px 20px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            flex-shrink: 0;
        }

        .header-left {
            display: flex;
            align-items: center;
            gap: 10px;
            font-size: 15px;
            font-weight: 600;
            letter-spacing: -0.01em;
        }

        .header-icon {
            width: 22px;
            height: 22px;
            background: var(--primary);
            border-radius: 5px;
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 12px;
        }

        .header-right {
            display: flex;
            align-items: center;
            gap: 8px;
        }

        .status-dot {
            width: 10px;
            height: 10px;
            border-radius: 50%;
            background: var(--danger);
            transition: all 0.3s ease;
        }

        .status-dot.running {
            background: var(--success);
            box-shadow: 0 0 8px rgba(16, 185, 129, 0.6);
        }

        .status-dot.starting {
            background: var(--warning);
            animation: pulse 1.2s ease-in-out infinite;
        }

        @keyframes pulse {
            0%, 100% { opacity: 1; }
            50% { opacity: 0.3; }
        }

        .status-label {
            font-size: 13px;
            color: #94a3b8;
            font-weight: 500;
        }

        /* Main content area */
        .content {
            flex: 1;
            overflow-y: auto;
            padding: 16px;
            display: flex;
            flex-direction: column;
            gap: 12px;
        }

        /* Stats Panel */
        .stats-panel {
            display: none;
            background: var(--bg-card);
            border-radius: var(--radius);
            border: 1px solid var(--border);
            overflow: hidden;
        }

        .stats-panel.visible { display: block; }

        .stats-grid {
            display: grid;
            grid-template-columns: repeat(4, 1fr);
            border-bottom: 1px solid var(--border);
        }

        .stat {
            text-align: center;
            padding: 12px 8px;
            border-right: 1px solid var(--border);
        }

        .stat:last-child { border-right: none; }

        .stat-value {
            font-size: 22px;
            font-weight: 700;
            color: var(--text);
            line-height: 1.2;
        }

        .stat-label {
            font-size: 10px;
            color: var(--text-muted);
            text-transform: uppercase;
            letter-spacing: 0.5px;
            margin-top: 2px;
        }

        .endpoints {
            padding: 10px 14px;
            font-family: var(--mono);
            font-size: 11px;
            color: var(--text-dim);
            line-height: 1.6;
            background: #f8fafc;
        }

        /* Controls */
        .controls {
            display: flex;
            gap: 8px;
            flex-shrink: 0;
        }

        .btn {
            padding: 8px 16px;
            border: none;
            border-radius: 6px;
            font-size: 13px;
            font-weight: 600;
            font-family: var(--sans);
            cursor: pointer;
            transition: all 0.15s ease;
            display: flex;
            align-items: center;
            gap: 6px;
        }

        .btn:hover { transform: translateY(-1px); }
        .btn:active { transform: translateY(0); }
        .btn:disabled { opacity: 0.4; cursor: not-allowed; transform: none; }

        .btn-primary { background: var(--primary); color: #fff; }
        .btn-primary:hover:not(:disabled) { background: var(--primary-hover); }
        .btn-danger { background: var(--danger); color: #fff; }
        .btn-danger:hover:not(:disabled) { background: var(--danger-hover); }
        .btn-ghost { background: transparent; color: var(--text-dim); border: 1px solid var(--border); }
        .btn-ghost:hover:not(:disabled) { background: #f8fafc; color: var(--text); }

        /* Activity Panel */
        .activity-panel {
            flex: 1;
            min-height: 0;
            background: var(--bg-card);
            border-radius: var(--radius);
            border: 1px solid var(--border);
            display: flex;
            flex-direction: column;
            overflow: hidden;
        }

        .activity-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 10px 14px;
            border-bottom: 1px solid var(--border);
            flex-shrink: 0;
        }

        .activity-title {
            font-size: 12px;
            font-weight: 600;
            color: var(--text);
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }

        .activity-count {
            font-size: 11px;
            color: var(--text-muted);
            font-family: var(--mono);
        }

        .activity-feed {
            flex: 1;
            overflow-y: auto;
            font-family: var(--mono);
            font-size: 12px;
        }

        .empty-state {
            display: flex;
            align-items: center;
            justify-content: center;
            height: 100%;
            min-height: 80px;
            color: var(--text-muted);
            font-family: var(--sans);
            font-size: 13px;
        }

        .entry {
            display: grid;
            grid-template-columns: 68px 1fr auto auto;
            gap: 8px;
            padding: 7px 14px;
            border-bottom: 1px solid #f1f5f9;
            align-items: center;
        }

        .entry:hover { background: #f8fafc; }

        .entry-time { color: var(--text-muted); font-size: 11px; }
        .entry-tool { color: var(--text); font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

        .badge {
            padding: 1px 6px;
            border-radius: 3px;
            font-size: 10px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.3px;
        }

        .badge-running { background: #fef3c7; color: #92400e; }
        .badge-ok { background: #d1fae5; color: #065f46; }
        .badge-err { background: #fee2e2; color: #991b1b; }

        .entry-duration {
            color: var(--text-muted);
            font-size: 11px;
            text-align: right;
            min-width: 48px;
        }

        /* Spinner */
        .spinner {
            display: inline-block;
            width: 10px;
            height: 10px;
            border: 2px solid #fbbf24;
            border-top-color: transparent;
            border-radius: 50%;
            animation: spin 0.6s linear infinite;
            vertical-align: middle;
        }

        @keyframes spin { to { transform: rotate(360deg); } }

        /* Status bar */
        .status-bar {
            padding: 6px 20px;
            background: var(--bg-dark);
            font-size: 11px;
            color: #64748b;
            flex-shrink: 0;
            display: flex;
            justify-content: space-between;
        }

        .status-bar .success { color: var(--success); }
        .status-bar .error { color: var(--danger); }
        .status-bar .info { color: var(--primary); }
    </style>

    <script>
        const MAX_ENTRIES = 50;
        const activeEntries = new Map();
        let entryCount = 0;
        let pendingEvents = [];
        let rafScheduled = false;

        function handleMessageFromHost(event) {
            let raw = event.data;

            // Mendix WebView wraps PostMessage(string) as {message: string}
            let payload = (raw && typeof raw === 'object' && raw.message !== undefined) ? raw.message : raw;

            // Legacy plain-string backward compat
            if (typeof payload === 'string') {
                if (payload === 'Running') { updateServerUI('running', {}); return; }
                if (payload === 'NotRunning') { updateServerUI('stopped', {}); return; }
                try { payload = JSON.parse(payload); } catch(e) { return; }
            }

            let data = payload;
            if (!data || !data.type) return;

            switch (data.type) {
                case 'serverStatus': updateServerUI(data.status, data); break;
                case 'toolCallEvent': handleToolCallEvent(data); break;
            }
        }

        function updateServerUI(status, data) {
            const dot = document.getElementById('statusDot');
            const label = document.getElementById('statusLabel');
            const startBtn = document.getElementById('startBtn');
            const stopBtn = document.getElementById('stopBtn');
            const statsPanel = document.getElementById('statsPanel');
            const statusMsg = document.getElementById('statusMsg');

            dot.className = 'status-dot';

            switch (status) {
                case 'running':
                    dot.classList.add('running');
                    label.textContent = 'Running';
                    startBtn.disabled = true;
                    stopBtn.disabled = false;
                    statsPanel.classList.add('visible');
                    if (data.port) document.getElementById('statPort').textContent = data.port;
                    if (data.toolCount) document.getElementById('statTools').textContent = data.toolCount;
                    if (data.sseConnections !== undefined) document.getElementById('statSSE').textContent = data.sseConnections;
                    if (data.totalToolCalls !== undefined) document.getElementById('statCalls').textContent = data.totalToolCalls;
                    if (data.connectionInfo) {
                        document.getElementById('endpoints').innerHTML = data.connectionInfo.replace(/\n/g, '<br>');
                    }
                    statusMsg.textContent = 'Server started successfully';
                    statusMsg.className = 'success';
                    break;

                case 'starting':
                    dot.classList.add('starting');
                    label.textContent = 'Starting...';
                    startBtn.disabled = true;
                    stopBtn.disabled = true;
                    statusMsg.textContent = 'Starting MCP Server...';
                    statusMsg.className = 'info';
                    break;

                case 'stopped':
                    label.textContent = 'Stopped';
                    startBtn.disabled = false;
                    stopBtn.disabled = true;
                    statsPanel.classList.remove('visible');
                    statusMsg.textContent = 'Server stopped';
                    statusMsg.className = '';
                    break;

                case 'error':
                    label.textContent = 'Error';
                    startBtn.disabled = false;
                    stopBtn.disabled = true;
                    statsPanel.classList.remove('visible');
                    statusMsg.textContent = data.errorMessage || 'Failed to start server';
                    statusMsg.className = 'error';
                    break;
            }
        }

        function handleToolCallEvent(data) {
            pendingEvents.push(data);
            if (!rafScheduled) {
                rafScheduled = true;
                requestAnimationFrame(flushEvents);
            }
        }

        function flushEvents() {
            rafScheduled = false;
            const feed = document.getElementById('activityFeed');
            const empty = document.getElementById('emptyState');
            if (empty) empty.style.display = 'none';

            const frag = document.createDocumentFragment();
            let needsScroll = false;
            let latestTotalCalls, latestSSE;

            const batch = pendingEvents.splice(0);
            for (const data of batch) {
                if (data.status === 'started') {
                    const el = document.createElement('div');
                    el.className = 'entry';
                    el.id = 'c-' + data.callId;
                    el.innerHTML =
                        '<span class=""entry-time"">' + data.timestamp + '</span>' +
                        '<span class=""entry-tool"">' + data.toolName + '</span>' +
                        '<span class=""badge badge-running""><span class=""spinner""></span></span>' +
                        '<span class=""entry-duration"">...</span>';
                    frag.appendChild(el);
                    activeEntries.set(data.callId, el);
                    entryCount++;
                    needsScroll = true;
                }
                else if (data.status === 'completed' || data.status === 'failed') {
                    const el = activeEntries.get(data.callId);
                    if (el) {
                        const badge = el.querySelector('.badge');
                        const dur = el.querySelector('.entry-duration');
                        if (data.status === 'completed') {
                            badge.className = 'badge badge-ok';
                            badge.textContent = 'OK';
                        } else {
                            badge.className = 'badge badge-err';
                            badge.textContent = 'ERR';
                            el.title = data.errorMessage || '';
                        }
                        dur.textContent = data.durationMs + 'ms';
                        activeEntries.delete(data.callId);
                    }
                }

                if (data.totalToolCalls !== undefined) latestTotalCalls = data.totalToolCalls;
                if (data.sseConnections !== undefined) latestSSE = data.sseConnections;
            }

            // Single DOM append for all new entries
            if (frag.childNodes.length > 0) feed.appendChild(frag);

            // Trim old entries (single pass)
            while (feed.children.length > MAX_ENTRIES + 1) {
                const first = feed.firstElementChild;
                if (first && first.id !== 'emptyState') {
                    activeEntries.delete(first.id.replace('c-', ''));
                    feed.removeChild(first);
                    entryCount--;
                } else break;
            }

            if (needsScroll) feed.scrollTop = feed.scrollHeight;

            // Update stats once per frame
            if (latestTotalCalls !== undefined)
                document.getElementById('statCalls').textContent = latestTotalCalls;
            if (latestSSE !== undefined)
                document.getElementById('statSSE').textContent = latestSSE;

            document.getElementById('activityCount').textContent = entryCount + ' calls';
        }

        function clearLog() {
            const feed = document.getElementById('activityFeed');
            feed.innerHTML = '<div id=""emptyState"" class=""empty-state"">Log cleared</div>';
            activeEntries.clear();
            pendingEvents = [];
            entryCount = 0;
            document.getElementById('activityCount').textContent = '0 calls';
        }

        function init() {
            window.chrome.webview.addEventListener('message', handleMessageFromHost);
            chrome.webview.postMessage({ message: 'MessageListenerRegistered' });
        }

        function startEngine() {
            updateServerUI('starting', {});
            chrome.webview.postMessage({ message: 'startEngine' });
        }

        function stopEngine() {
            chrome.webview.postMessage({ message: 'stopEngine' });
        }
    </script>
</head>
<body onload=""init()"">
    <div class=""header"">
        <div class=""header-left"">
            <div class=""header-icon"">M</div>
            MCP Server
        </div>
        <div class=""header-right"">
            <span id=""statusDot"" class=""status-dot""></span>
            <span id=""statusLabel"" class=""status-label"">Stopped</span>
        </div>
    </div>

    <div class=""content"">
        <div id=""statsPanel"" class=""stats-panel"">
            <div class=""stats-grid"">
                <div class=""stat"">
                    <div class=""stat-value"" id=""statPort"">--</div>
                    <div class=""stat-label"">Port</div>
                </div>
                <div class=""stat"">
                    <div class=""stat-value"" id=""statTools"">--</div>
                    <div class=""stat-label"">Tools</div>
                </div>
                <div class=""stat"">
                    <div class=""stat-value"" id=""statSSE"">0</div>
                    <div class=""stat-label"">SSE Clients</div>
                </div>
                <div class=""stat"">
                    <div class=""stat-value"" id=""statCalls"">0</div>
                    <div class=""stat-label"">Total Calls</div>
                </div>
            </div>
            <div class=""endpoints"" id=""endpoints""></div>
        </div>

        <div class=""controls"">
            <button id=""startBtn"" class=""btn btn-primary"" onclick=""startEngine()"">Start Server</button>
            <button id=""stopBtn"" class=""btn btn-danger"" onclick=""stopEngine()"" disabled>Stop Server</button>
            <button class=""btn btn-ghost"" onclick=""clearLog()"">Clear Log</button>
        </div>

        <div class=""activity-panel"">
            <div class=""activity-header"">
                <span class=""activity-title"">Activity Feed</span>
                <span id=""activityCount"" class=""activity-count"">0 calls</span>
            </div>
            <div id=""activityFeed"" class=""activity-feed"">
                <div id=""emptyState"" class=""empty-state"">Waiting for tool calls...</div>
            </div>
        </div>
    </div>

    <div class=""status-bar"">
        <span id=""statusMsg""></span>
        <span>MCP Extension v1.0</span>
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
                System.Diagnostics.Debug.WriteLine($"Could not determine log file path: {ex.Message}");
                return System.IO.Path.Combine(Environment.CurrentDirectory, "mcp_debug.log");
            }
        }

        private void LogToFile(string message)
        {
            try
            {
                System.IO.File.AppendAllText(GetLogFilePath(), message + Environment.NewLine);
            }
            catch { }
        }

        private void PostJsonMessage(object data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data);
                currentWebView?.PostMessage(json);
            }
            catch (Exception ex)
            {
                LogToFile($"[{DateTime.Now:HH:mm:ss.fff}] PostJsonMessage error: {ex.Message}");
            }
        }

        private void WebView_MessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            try
            {
                LogToFile($"[{DateTime.Now:HH:mm:ss.fff}] WebView message: {e.Message}");

                if (e.Message.Contains("MessageListenerRegistered"))
                {
                    var isRunning = parentPanel.McpServer?.IsRunning ?? false;
                    if (isRunning)
                    {
                        SubscribeToToolCallEvents();
                        PostJsonMessage(new
                        {
                            type = "serverStatus",
                            status = "running",
                            connectionInfo = parentPanel.McpServer.GetConnectionInfo(),
                            port = parentPanel.McpServer.Port,
                            toolCount = parentPanel.McpServer?.RegisteredToolCount ?? 0,
                            sseConnections = parentPanel.McpServer.ActiveSseConnections,
                            totalToolCalls = parentPanel.McpServer.TotalToolCalls
                        });
                    }
                    else if (parentPanel.McpServer != null)
                    {
                        // Server exists but still starting
                        PostJsonMessage(new { type = "serverStatus", status = "starting" });
                    }
                    else
                    {
                        PostJsonMessage(new { type = "serverStatus", status = "stopped" });
                    }
                    return;
                }

                if (e.Message.Contains("startEngine"))
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            string result = await parentPanel.StartAPIEngineAsync();
                            var isRunning = parentPanel.McpServer?.IsRunning ?? false;

                            Application.Instance.Invoke(() =>
                            {
                                if (isRunning)
                                    NotifyServerStarted(parentPanel.McpServer.GetConnectionInfo());
                                else
                                    NotifyServerStartFailed("Server failed to start");
                            });
                        }
                        catch (Exception ex)
                        {
                            Application.Instance.Invoke(() =>
                            {
                                NotifyServerStartFailed(ex.Message);
                            });
                        }
                    });
                }
                else if (e.Message.Contains("stopEngine"))
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await parentPanel.StopAPIEngineAsync();
                            Application.Instance.Invoke(() =>
                            {
                                NotifyServerStopped("Server stopped by user");
                            });
                        }
                        catch (Exception ex)
                        {
                            Application.Instance.Invoke(() =>
                            {
                                NotifyServerStopFailed(ex.Message);
                            });
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                LogToFile($"[{DateTime.Now:HH:mm:ss.fff}] WebView_MessageReceived error: {ex.Message}");
                PostJsonMessage(new { type = "serverStatus", status = "error", errorMessage = ex.Message });
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

        private void SubscribeToToolCallEvents()
        {
            if (parentPanel.McpServer != null && _toolCallHandler == null)
            {
                _toolCallHandler = HandleToolCallEvent;
                parentPanel.McpServer.OnToolCallEvent += _toolCallHandler;
            }
        }

        private void UnsubscribeFromToolCallEvents()
        {
            if (parentPanel.McpServer != null && _toolCallHandler != null)
            {
                parentPanel.McpServer.OnToolCallEvent -= _toolCallHandler;
                _toolCallHandler = null;
            }
        }

        private void HandleToolCallEvent(ToolCallEventArgs args)
        {
            try
            {
                var message = new
                {
                    type = "toolCallEvent",
                    callId = args.CallId,
                    toolName = args.ToolName,
                    timestamp = args.Timestamp.ToString("HH:mm:ss"),
                    status = args.Status.ToString().ToLowerInvariant(),
                    durationMs = args.DurationMs,
                    errorMessage = args.ErrorMessage,
                    totalToolCalls = parentPanel.McpServer?.TotalToolCalls ?? 0,
                    sseConnections = parentPanel.McpServer?.ActiveSseConnections ?? 0
                };

                Application.Instance.Invoke(() =>
                {
                    PostJsonMessage(message);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HandleToolCallEvent error: {ex.Message}");
            }
        }

        public void NotifyServerStarted(string connectionInfo)
        {
            try
            {
                SubscribeToToolCallEvents();
                PostJsonMessage(new
                {
                    type = "serverStatus",
                    status = "running",
                    connectionInfo = connectionInfo,
                    port = parentPanel.McpServer?.Port ?? 0,
                    toolCount = parentPanel.McpServer?.RegisteredToolCount ?? 0,
                    sseConnections = parentPanel.McpServer?.ActiveSseConnections ?? 0,
                    totalToolCalls = parentPanel.McpServer?.TotalToolCalls ?? 0
                });
            }
            catch (Exception ex)
            {
                LogToFile($"[{DateTime.Now:HH:mm:ss.fff}] NotifyServerStarted error: {ex.Message}");
            }
        }

        public void NotifyServerStartFailed(string errorMessage)
        {
            try
            {
                PostJsonMessage(new
                {
                    type = "serverStatus",
                    status = "error",
                    errorMessage = errorMessage
                });
            }
            catch (Exception ex)
            {
                LogToFile($"[{DateTime.Now:HH:mm:ss.fff}] NotifyServerStartFailed error: {ex.Message}");
            }
        }

        public void NotifyServerStopped(string message)
        {
            try
            {
                UnsubscribeFromToolCallEvents();
                PostJsonMessage(new { type = "serverStatus", status = "stopped" });
            }
            catch (Exception ex)
            {
                LogToFile($"[{DateTime.Now:HH:mm:ss.fff}] NotifyServerStopped error: {ex.Message}");
            }
        }

        public void NotifyServerStopFailed(string errorMessage)
        {
            try
            {
                PostJsonMessage(new
                {
                    type = "serverStatus",
                    status = "running",
                    errorMessage = "Failed to stop: " + errorMessage
                });
            }
            catch (Exception ex)
            {
                LogToFile($"[{DateTime.Now:HH:mm:ss.fff}] NotifyServerStopFailed error: {ex.Message}");
            }
        }
    }
}
