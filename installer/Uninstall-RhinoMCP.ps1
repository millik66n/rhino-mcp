[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AppDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$packageHelpers = Join-Path $PSScriptRoot "RhinoMcpPackage.ps1"
if (Test-Path -LiteralPath $packageHelpers -PathType Leaf) {
    . $packageHelpers
}

$serverPath = Join-Path $AppDir "server\rhino-mcp.exe"
if (Test-Path -LiteralPath $serverPath -PathType Leaf) {
    & $serverPath uninstall --all --yes
}

$programFiles64 = $env:ProgramW6432
if ([string]::IsNullOrWhiteSpace($programFiles64)) {
    $programFiles64 = $env:ProgramFiles
}
$yakPath = Join-Path $programFiles64 "Rhino 8\System\yak.exe"
if (Test-Path -LiteralPath $yakPath -PathType Leaf) {
    & $yakPath uninstall "Rhino-MCP-Easy"
}

if (Get-Command Unregister-RhinoMcpPlugin -ErrorAction SilentlyContinue) {
    Unregister-RhinoMcpPlugin
}

if (Get-Command Remove-RhinoMcpPackageVersions -ErrorAction SilentlyContinue) {
    Remove-RhinoMcpPackageVersions
}
