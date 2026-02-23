# MCPExtension for Studio Pro 10.x

Build and deploy instructions for running the MCPExtension on **Mendix Studio Pro 10.24.13+**.

This is the same **83-tool** extension as the Studio Pro 11.x version — same codebase, no code duplication. The Mendix Extensions API is 99% identical between 10.x and 11.x, so all tools work on both versions.

> **Pre-built available**: A ready-to-use `.mxmodule` file is in [`dist/MCP-StudioPro-10.24.13.mxmodule`](../dist/MCP-StudioPro-10.24.13.mxmodule) — no build required. See the [main README](../README.md#pre-built-extension-no-build-required) for installation instructions.

> For the full tool reference, usage examples, and troubleshooting, see the [main README](../README.md).

## Prerequisites

- **Mendix Studio Pro 10.24.13+**
- **.NET 8.0 SDK**

## Build & Deploy

```bash
dotnet build backport-10x/MCPExtension.10x.csproj
```

The post-build step automatically copies the extension to `{YourProject}/extensions/MCP/`.

> **Important**: Edit the `PostBuild` target in `MCPExtension.10x.csproj` to match your Mendix project path:
> ```xml
> <Exec Command="xcopy /y /s /i &quot;$(TargetDir)&quot; &quot;C:\path\to\your\project\extensions\MCP&quot;" />
> ```

## Launch Studio Pro

Studio Pro 10.x only needs **one flag**:

```bash
studiopro.exe "YourProject.mpr" -enable-extension-development
```

> **Note**: The additional flags used by Studio Pro 11.x (`--enable-universal-maia`, `--enable-microflow-generation`, etc.) are not needed for 10.x.

## Start the MCP Server

Open the **MCP dockable pane** in Studio Pro. The server starts when the pane is opened.

## Connect

| Endpoint | URL |
|----------|-----|
| SSE | `http://localhost:3001/sse` |
| Messages | `http://localhost:3001/message` |
| Health | `http://localhost:3001/health` |
| MCP Metadata | `http://localhost:3001/.well-known/mcp` |

Port auto-increments from 3001 if occupied.

## Customizing for Your Environment

Two files need paths adjusted for your setup:

### 1. `MCPExtension.10x.csproj` — Deploy target

Change the PostBuild path to your Mendix project:

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Exec Command="xcopy /y /s /i &quot;$(TargetDir)&quot; &quot;C:\YourProject\extensions\MCP&quot;" />
</Target>
```

### 2. `start-studiopro-10x.bat` — Studio Pro path

Update the Studio Pro executable path and project file:

```batch
"C:\path\to\studiopro.exe" "C:\path\to\YourProject.mpr" -enable-extension-development
```

## How It Works

The `MCPExtension.10x.csproj` references all source files from the parent directory — there is zero code duplication between the 10.x and 11.x builds:

```
backport-10x/
  MCPExtension.10x.csproj   <-- References ../*.cs source files
  manifest.json              <-- Extension manifest (identical)
  start-studiopro-10x.bat   <-- Launch script for 10.x
  TOOLS-COMPARISON.md        <-- Before (17 tools) vs after (83 tools)
  README.md                  <-- This file
```

Both builds use the same NuGet package (`Mendix.StudioPro.ExtensionsAPI v10.21.1`) which is compatible with Studio Pro 10.x and 11.x.

## API Compatibility

The Mendix Extensions API is **99% identical** between Studio Pro 10.24.13 and 11.5:

- 112 public types, 148 methods, 129 properties — all identical
- Only difference: `IAppService.TryImportModule()` parameter type changed — **not used by MCPExtension**
- Three 11.5-only additions (IComponentUrlResolutionService, auth events) — **not used by MCPExtension**
- All 83 tools verified working on Studio Pro 10.24.13

See [TOOLS-COMPARISON.md](TOOLS-COMPARISON.md) for the full before/after tool list.

## More Information

- [Main README](../README.md) — Full tool reference (83 tools), usage examples, known limitations, troubleshooting
- [TOOLS-COMPARISON.md](TOOLS-COMPARISON.md) — Before (17 tools) vs after (83 tools) comparison table
