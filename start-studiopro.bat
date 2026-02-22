@echo off
REM Launch Studio Pro with all required extension development flags
REM This script MUST be used to start Studio Pro for MCP extension testing

"C:\Program Files\Mendix\11.5.0\modeler\studiopro.exe" "C:\Mendix Projects\Sample\StudoProMCPServer.mpr" --enable-extension-development --enable-universal-maia --enable-microflow-generation --enable-workflow-generation --enable-maia-session-story-attachment
