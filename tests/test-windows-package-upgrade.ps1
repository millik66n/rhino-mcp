[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "..\installer\RhinoMcpPackage.ps1")

$tempParent = $env:RUNNER_TEMP
if ([string]::IsNullOrWhiteSpace($tempParent)) {
    $tempParent = [IO.Path]::GetTempPath()
}
$testRoot = Join-Path $tempParent ("rhino-mcp-package-" + [Guid]::NewGuid().ToString("N"))
try {
    $packageRoot = Get-RhinoMcpPackageRoot -RoamingAppData $testRoot
    New-Item -ItemType Directory -Path (Join-Path $packageRoot "0.3.0") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $packageRoot "0.4.5") -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $packageRoot "manifest.txt") -Value "0.3.0"

    Remove-RhinoMcpPackageVersions -RoamingAppData $testRoot
    if (Test-Path -LiteralPath $packageRoot) {
        throw "The package cleanup left old Rhino MCP versions behind."
    }

    $version = "9.9.9"
    $versionRoot = Join-Path $packageRoot $version
    foreach ($runtime in ("net48", "net7.0")) {
        $runtimeRoot = Join-Path $versionRoot $runtime
        New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
        New-Item -ItemType File -Path (Join-Path $runtimeRoot "RhinoMCP.rhp") -Force | Out-Null
        New-Item -ItemType File -Path (Join-Path $runtimeRoot "RhinoMCP.Grasshopper.gha") -Force | Out-Null
    }
    Set-Content -LiteralPath (Join-Path $packageRoot "manifest.txt") -Value $version

    Assert-RhinoMcpPackageInstalled -Version $version -RoamingAppData $testRoot

    New-Item -ItemType Directory -Path (Join-Path $packageRoot "0.4.5") -Force | Out-Null
    try {
        Assert-RhinoMcpPackageInstalled -Version $version -RoamingAppData $testRoot
        throw "The package verifier accepted a stale package version."
    }
    catch {
        if ($_.Exception.Message -eq "The package verifier accepted a stale package version.") {
            throw
        }
    }
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
