@echo off
REM Launch Studio Pro 10.24.13 with extension development flag
REM The MCP dockable pane must be opened manually in Studio Pro for the server to start
REM Health check: curl http://localhost:3001/health

"C:\Users\ricardo.perdigao\AppData\Local\Programs\Mendix\10.24.13.86719\modeler\studiopro.exe" "C:\Mendix Projects\MCPExtension-main\MCPExtension.mpr" -enable-extension-development
