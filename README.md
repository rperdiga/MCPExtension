# MCPExtension

## Overview

MCPExtension is an experimental C# Mendix Extensibility framework project that exposes **Mendix Studio Pro capabilities and tools** through a **Model Context Protocol (MCP) Server**. This extension allows external applications and AI tools to interact with Mendix Studio Pro's domain modeling capabilities via standardized MCP protocols.

## Features

- **HTTP/SSE MCP Server** - Provides a standards-compliant MCP server implementation
- **Domain Model Management** - Create, read, update, and delete domain model entities and associations
- **Page Generation** - Generate overview pages for domain entities
- **Microflow Inspection** - List and inspect microflows in your Mendix project
- **Sample Data Generation** - Create realistic sample data for testing
- **Real-time Debug Logging** - Comprehensive logging for troubleshooting
- **Visual Studio Pro Integration** - Seamless integration with Mendix Studio Pro through a dockable pane

## Architecture

The extension consists of several key components:

### Core Components

- **`AIAPIEngine`** - Main extension entry point and lifecycle management
- **`MendixMcpServer`** - MCP server implementation with tool registration
- **`McpServer`** - HTTP/SSE server handling MCP protocol messages
- **`AIAPIEngineViewModel`** - WebView-based UI for server control

### Tool Categories

#### Domain Model Tools (`MendixDomainModelTools`)
- `read_domain_model` - Read current domain model structure
- `create_entity` - Create new entities with attributes
- `create_association` - Create associations between entities
- `create_multiple_entities` - Bulk entity creation
- `create_multiple_associations` - Bulk association creation
- `create_domain_model_from_schema` - Create complete domain models from JSON schemas
- `delete_model_element` - Delete entities, attributes, or associations
- `diagnose_associations` - Troubleshoot association creation issues

#### Additional Tools (`MendixAdditionalTools`)
- `save_data` - Generate and validate sample data
- `generate_overview_pages` - Create list view pages for entities
- `list_microflows` - List microflows in a module
- `read_microflow_details` - Get detailed microflow information
- `get_last_error` - Retrieve last error information
- `list_available_tools` - List all available MCP tools
- `debug_info` - Get comprehensive domain model debug information

## Installation

1. **Build the Extension**
   ```powershell
   dotnet build MCPExtension.sln
   ```

2. **Deploy to Mendix Studio Pro**
   - The extension automatically copies to `C:\Mendix Projects\Sample\extensions\MCP\` after build
   - Alternatively, copy the built files to your Mendix project's `extensions` folder

3. **Load in Studio Pro**
   - Open Mendix Studio Pro while using --enable-extension-development
   - Open your Mendix project
   - Access via menu: **Extensions → MCP → MCP Server**

## Usage

### Server Endpoints

Once started, the MCP server exposes several endpoints:

- **SSE Endpoint**: `http://localhost:3001/sse` - Server-Sent Events connection
- **Message Endpoint**: `http://localhost:3001/message` - MCP message handling
- **Health Check**: `http://localhost:3001/health` - Server health status
- **MCP Metadata**: `http://localhost:3001/.well-known/mcp` - MCP server metadata

### Logging

Debug logs are written to:
- `{MendixProjectPath}/resources/mcp_debug.log`

## Configuration

### Port Configuration

The server automatically finds an available port starting from 3001. You can modify the port in `AIAPIEngine.cs`:

```csharp
// Use a different starting port
_mcpPort = FindAvailablePort(3001);
```

### Project Directory

The extension automatically detects the Mendix project directory. Sample data and logs are stored in:
- `{MendixProjectPath}/resources/`

## Troubleshooting

### Server Won't Start

1. Check if the port is already in use
2. Verify the Mendix project is open
3. Check the debug log at `{MendixProjectPath}/resources/mcp_debug.log`

### Connection Issues

1. Verify the server is running (check the UI status indicator)
2. Test the health endpoint: `http://localhost:3001/health`
3. Check Windows Firewall settings
4. Ensure no antivirus software is blocking the connection

### Tool Errors

1. Use the `get_last_error` tool to retrieve detailed error information
2. Use the `debug_info` tool to inspect the current domain model state
3. Check the debug log for detailed error traces

## Development

### Dependencies

- **.NET 8.0** - Target framework
- **Mendix.StudioPro.ExtensionsAPI** - Mendix Studio Pro integration
- **Microsoft.AspNetCore** - HTTP server functionality
- **System.Text.Json** - JSON serialization
- **Eto.Forms** - UI framework

### Building from Source

```powershell
# Clone the repository
git clone https://github.com/rperdiga/MCPExtension.git
cd MCPExtension

# Build the solution
dotnet build

# The extension will be deployed to the configured Mendix project
```

### Extending the Framework

To add new MCP tools:

1. Create a new method in `MendixDomainModelTools` or `MendixAdditionalTools`
2. Register the tool in `MendixMcpServer.RegisterTools()`
3. Add the tool schema in `McpServer.GetToolInputSchema()`
4. Add the tool description in `McpServer.GetToolDescription()`

## License

This project is experimental and provided as-is for research and development purposes.

## Contributing

This is an experimental project. Feel free to submit issues and enhancement requests.

## Support

For issues and questions:
1. Check the debug logs first
2. Use the built-in diagnostic tools (`debug_info`, `get_last_error`)
3. Submit issues with detailed error information and steps to reproduce