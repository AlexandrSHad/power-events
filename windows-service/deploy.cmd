@echo off
setlocal
REM Require Administrator
net session >nul 2>&1 || (echo Please run this script as Administrator.&exit /b 1)

set SERVICE_NAME=PowerEvents
set DISPLAY_NAME=Power Events Service
set PROJECT=power-events-service.csproj
set RUNTIME=win-x64
set CONFIG=Release
set PUBLISH_DIR=%~dp0artifacts\%CONFIG%\net10.0-windows\%RUNTIME%
set BINARY=%PUBLISH_DIR%\power-events-service.exe

echo %BINARY%
echo Publishing service...
REM TODO: Desktop development with C++ to support Native AOT on Windows
dotnet publish power-events-service.cs -o %PUBLISH_DIR% -c %CONFIG% -r %RUNTIME% --self-contained false ^
 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishAot=false ^
 /p:DebugSymbols=false /p:DebugType=None || exit /b 1

if not exist "%BINARY%" (
  echo Publish output missing: %BINARY%
  exit /b 1
)

echo Removing existing service (if any)...
sc query "%SERVICE_NAME%" >nul 2>&1
if %errorlevel%==0 (
  sc stop "%SERVICE_NAME%" >nul 2>&1
  sc delete "%SERVICE_NAME%" >nul 2>&1
)

echo Creating service...
sc create "%SERVICE_NAME%" binPath= "\"%BINARY%\"" start= auto DisplayName= "%DISPLAY_NAME%" || exit /b 1
sc description "%SERVICE_NAME%" "Publishes power state events to MQTT." >nul

echo Starting service...
sc start "%SERVICE_NAME%" || exit /b 1

echo Done. Service "%SERVICE_NAME%" is running from "%BINARY%".
endlocal
