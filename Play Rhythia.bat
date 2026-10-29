@echo off
setlocal
set "PROJECT_DIR=%~dp0"
set "DOTNET_ROOT=%PROJECT_DIR%.dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"
"%PROJECT_DIR%.godot-sdk\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%PROJECT_DIR%." %*
endlocal
