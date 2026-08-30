function Get-RhinoMcpPackageRoot {
    [CmdletBinding()]
    param(
        [string]$RoamingAppData = $env:APPDATA
    )

    if ([string]::IsNullOrWhiteSpace($RoamingAppData)) {
        throw "APPDATA is not available; Rhino MCP package cleanup was stopped."
    }

    $packagesRoot = [IO.Path]::GetFullPath((Join-Path $RoamingAppData "McNeel\Rhinoceros\packages\8.0"))
    $packageRoot = [IO.Path]::GetFullPath((Join-Path $packagesRoot "Rhino-MCP-Easy"))
    $expectedParent = [IO.Path]::GetFullPath((Split-Path -Parent $packageRoot))

    if (-not $expectedParent.Equals($packagesRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Rhino MCP package cleanup resolved outside Rhino's package folder."
    }

    return $packageRoot
}

function Remove-RhinoMcpPackageVersions {
    [CmdletBinding()]
    param(
        [string]$RoamingAppData = $env:APPDATA
    )

    $packageRoot = Get-RhinoMcpPackageRoot -RoamingAppData $RoamingAppData
    if (-not (Test-Path -LiteralPath $packageRoot)) {
        return
    }
    if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) {
        throw "Expected the Rhino MCP package path to be a folder: $packageRoot"
    }

    Write-Host "Removing old Rhino MCP package versions from $packageRoot"
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
    if (Test-Path -LiteralPath $packageRoot) {
        throw "Old Rhino MCP package versions could not be removed from $packageRoot"
    }
}

function Assert-RhinoMcpPackageInstalled {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version,

        [string]$RoamingAppData = $env:APPDATA
    )

    $packageRoot = Get-RhinoMcpPackageRoot -RoamingAppData $RoamingAppData
    $manifestPath = Join-Path $packageRoot "manifest.txt"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Rhino did not create the active package manifest: $manifestPath"
    }

    $activeVersion = (Get-Content -LiteralPath $manifestPath -Raw).Trim()
    if ($activeVersion -ne $Version) {
        throw "Rhino selected Rhino MCP $activeVersion instead of $Version."
    }

    $versionRoot = Join-Path $packageRoot $Version
    $requiredFiles = @(
        (Join-Path $versionRoot "net48\RhinoMCP.rhp"),
        (Join-Path $versionRoot "net48\RhinoMCP.Grasshopper.gha"),
        (Join-Path $versionRoot "net7.0\RhinoMCP.rhp"),
        (Join-Path $versionRoot "net7.0\RhinoMCP.Grasshopper.gha")
    )
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "The installed Rhino MCP package is incomplete: $requiredFile"
        }
    }

    $staleVersions = Get-ChildItem -LiteralPath $packageRoot -Directory |
        Where-Object { $_.Name -ne $Version }
    if ($staleVersions) {
        $names = ($staleVersions | Select-Object -ExpandProperty Name) -join ", "
        throw "Old Rhino MCP package versions remain after installation: $names"
    }

    Write-Host "Verified Rhino MCP $Version as the only active Rhino package version."
}
