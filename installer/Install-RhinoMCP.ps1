[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("codex", "claude", "cursor")]
    [string]$Client,

    [Parameter(Mandatory = $true)]
    [ValidateSet("basic", "grasshopper", "developer")]
    [string]$Profile,

    [Parameter(Mandatory = $true)]
    [string]$AppDir,

    [Parameter(Mandatory = $true)]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "RhinoMcpPackage.ps1")

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $FilePath"
    }
}

$logDirectory = Join-Path $env:LOCALAPPDATA "Rhino MCP\Logs"
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logPath = Join-Path $logDirectory "install.log"
Start-Transcript -Path $logPath -Append | Out-Null

try {
    $programFiles64 = $env:ProgramW6432
    if ([string]::IsNullOrWhiteSpace($programFiles64)) {
        $programFiles64 = $env:ProgramFiles
    }

    $yakPath = Join-Path $programFiles64 "Rhino 8\System\yak.exe"
    if (-not (Test-Path -LiteralPath $yakPath -PathType Leaf)) {
        throw "Rhino 8 was not found. Install Rhino 8 before installing Rhino MCP."
    }

    if (Get-Process -Name "Rhino" -ErrorAction SilentlyContinue) {
        throw "Rhino is running. Save your work, close Rhino, and run this installer again."
    }

    $package = Get-ChildItem -LiteralPath (Join-Path $AppDir "payload") -Filter "*.yak" -File |
        Select-Object -First 1
    if ($null -eq $package) {
        throw "The installer is missing its Rhino plug-in payload."
    }

    $serverPath = Join-Path $AppDir "server\rhino-mcp.exe"
    if (-not (Test-Path -LiteralPath $serverPath -PathType Leaf)) {
        throw "The installer is missing its bundled MCP server."
    }

    Write-Host "Removing previous Rhino MCP plug-in versions..."
    & $yakPath uninstall "Rhino-MCP-Easy"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "No active Rhino MCP package was registered; checking for stale files."
    }
    Remove-RhinoMcpPackageVersions

    Write-Host "Installing the Rhino and Grasshopper bridges..."
    Invoke-Checked -FilePath $yakPath -Arguments @("install", $package.FullName)
    Assert-RhinoMcpPackageInstalled -Version $Version

    Write-Host "Configuring $Client with the $Profile profile..."
    Invoke-Checked -FilePath $serverPath -Arguments @(
        "setup", $Client, "--profile", $Profile, "--non-interactive"
    )
    if ($Client -eq "codex") {
        Write-Host "/RhinoMCP routing installed. Begin each Rhino request with /RhinoMCP."
    }

    Write-Host "Running installation checks..."
    $doctorOutput = & $serverPath doctor --json 2>&1 | Out-String
    $doctorOutput | Set-Content -LiteralPath (Join-Path $AppDir "doctor.json") -Encoding UTF8
    if ($LASTEXITCODE -ne 0) {
        throw "Installation verification failed. See $AppDir\doctor.json for details."
    }

    [ordered]@{
        installed_at = [DateTime]::UtcNow.ToString("o")
        client = $Client
        profile = $Profile
        version = $Version
        server = $serverPath
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $AppDir "installed.json") -Encoding UTF8

    Write-Host "Rhino MCP is installed. In Codex, begin with /RhinoMCP; Rhino and the Chrome status page open automatically."
}
finally {
    Stop-Transcript | Out-Null
}
