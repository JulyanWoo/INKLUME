$developmentRoot = Split-Path -Parent $PSScriptRoot
$developmentLocal = Join-Path $developmentRoot '.local'

# These process-scoped settings keep tool writes inside this checkout.
$env:DOTNET_CLI_HOME = Join-Path $developmentLocal 'dotnet'
$env:NUGET_PACKAGES = Join-Path $developmentLocal 'nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $developmentLocal 'nuget\http-cache'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $developmentLocal 'nuget\plugins-cache'
$env:APPDATA = Join-Path $developmentLocal 'appdata'
$env:LOCALAPPDATA = Join-Path $developmentLocal 'localappdata'
$env:TEMP = Join-Path $developmentLocal 'temp'
$env:TMP = $env:TEMP
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $developmentLocal 'bundles'

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:UseSharedCompilation = 'false'
$env:VSTEST_TELEMETRY_OPTEDIN = '0'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'

foreach ($developmentDirectory in @(
    $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:NUGET_HTTP_CACHE_PATH,
    $env:NUGET_PLUGINS_CACHE_PATH, $env:APPDATA, $env:LOCALAPPDATA,
    $env:TEMP, $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
)) {
    [System.IO.Directory]::CreateDirectory($developmentDirectory) | Out-Null
}
