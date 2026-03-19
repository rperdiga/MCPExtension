@echo off
REM Launch Studio Pro with all required extension development flags
REM This script MUST be used to start Studio Pro for MCP extension testing

"C:\Users\ricardo.perdigao\AppData\Local\Programs\Mendix\10.24.13.86719\modeler\studiopro.exe" "C:\Mendix Projects\MCPExtension-main\MCPExtension.mpr" --enable-extension-development
