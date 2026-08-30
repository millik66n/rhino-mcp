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
$registryTestId = [Guid]::NewGuid().ToString("N")
$registryTestRoot = "HKCU:\Software\RhinoMCP\Tests\$registryTestId"
$registryVersionRoot = Join-Path $registryTestRoot "8.0"
$runtimeSettingsPath = Join-Path $registryVersionRoot "Global Options"
try {
    $packageRoot = Get-RhinoMcpPackageRoot -RoamingAppData $testRoot
    New-Item -ItemType Directory -Path (Join-Path $packageRoot "0.3.0") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $packageRoot "0.4.5") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $packageRoot "0.4.6") -Force | Out-Null
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

    $cachedRegistration = Join-Path `
        (Join-Path $registryVersionRoot "20260830.12345\Plug-ins") `
        $script:RhinoMcpPluginId
    $otherPluginRegistration = Join-Path `
        (Split-Path -Parent $cachedRegistration) `
        ([Guid]::NewGuid().ToString())
    New-Item -Path (Split-Path -Parent $cachedRegistration) -Force | Out-Null
    New-Item -Path $cachedRegistration -Force | Out-Null
    New-Item -Path $otherPluginRegistration -Force | Out-Null
    New-ItemProperty `
        -LiteralPath $cachedRegistration `
        -Name "FileName" `
        -Value "C:\stale\RhinoMCP.rhp" `
        -PropertyType String `
        -Force | Out-Null

    Register-RhinoMcpPlugin `
        -Version $version `
        -RoamingAppData $testRoot `
        -RegistryVersionRoot $registryVersionRoot `
        -RuntimeSettingsPath $runtimeSettingsPath

    $registrationPath = Join-Path `
        (Join-Path $registryVersionRoot "Plug-ins") `
        $script:RhinoMcpPluginId
    $registration = Get-ItemProperty -LiteralPath $registrationPath
    $expectedNet7 = Join-Path $versionRoot "net7.0\RhinoMCP.rhp"
    if ($registration.FileName -ne $expectedNet7 -or [int]$registration.LoadMode -ne 1) {
        throw "Rhino MCP was not registered for the default Rhino 8 runtime."
    }
    if (Test-Path -LiteralPath $cachedRegistration) {
        throw "The stale Rhino MCP discovery cache was not cleared."
    }
    if (-not (Test-Path -LiteralPath $otherPluginRegistration)) {
        throw "Rhino MCP cleanup removed another plug-in's cache entry."
    }

    New-Item -Path $runtimeSettingsPath -Force | Out-Null
    New-ItemProperty `
        -LiteralPath $runtimeSettingsPath `
        -Name "DotNetRuntime" `
        -Value "netfx" `
        -PropertyType String `
        -Force | Out-Null
    Register-RhinoMcpPlugin `
        -Version $version `
        -RoamingAppData $testRoot `
        -RegistryVersionRoot $registryVersionRoot `
        -RuntimeSettingsPath $runtimeSettingsPath
    $registration = Get-ItemProperty -LiteralPath $registrationPath
    $expectedNet48 = Join-Path $versionRoot "net48\RhinoMCP.rhp"
    if ($registration.FileName -ne $expectedNet48) {
        throw "Rhino MCP did not select net48 for Rhino's .NET Framework mode."
    }

    Unregister-RhinoMcpPlugin `
        -RoamingAppData $testRoot `
        -RegistryVersionRoot $registryVersionRoot
    if (Test-Path -LiteralPath $registrationPath) {
        throw "Rhino MCP plug-in registration remained after uninstall."
    }

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
    if (Test-Path -LiteralPath $registryTestRoot) {
        Remove-Item -LiteralPath $registryTestRoot -Recurse -Force
    }
}
