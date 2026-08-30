Set-StrictMode -Version Latest

$script:RhinoMcpPluginId = "0E59A34D-7906-45DC-B8A1-B1D8219A841E"

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

function Test-RhinoMcpChildPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ChildPath,
        [Parameter(Mandatory = $true)][string]$ParentPath
    )

    $child = [IO.Path]::GetFullPath($ChildPath)
    $trimCharacters = [char[]]@(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    )
    $parent = [IO.Path]::GetFullPath($ParentPath).TrimEnd($trimCharacters) +
        [IO.Path]::DirectorySeparatorChar
    return $child.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase)
}

function Get-RhinoMcpRuntime {
    [CmdletBinding()]
    param(
        [string]$RuntimeSettingsPath =
            "HKCU:\Software\McNeel\Rhinoceros\8.0\Global Options"
    )

    $runtime = ""
    if (Test-Path -LiteralPath $RuntimeSettingsPath) {
        $settings = Get-ItemProperty -LiteralPath $RuntimeSettingsPath -ErrorAction SilentlyContinue
        $runtimeProperty = if ($null -ne $settings) {
            $settings.PSObject.Properties["DotNetRuntime"]
        } else {
            $null
        }
        if ($null -ne $runtimeProperty -and $null -ne $runtimeProperty.Value) {
            $runtime = $runtimeProperty.Value.ToString().Trim().ToLowerInvariant()
        }
    }

    if ($runtime -eq "netfx" -or $runtime -like "*framework*") {
        return "net48"
    }
    return "net7.0"
}

function Get-RhinoMcpPluginPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [ValidateSet("net48", "net7.0")][string]$Runtime,
        [string]$RoamingAppData = $env:APPDATA,
        [string]$RuntimeSettingsPath =
            "HKCU:\Software\McNeel\Rhinoceros\8.0\Global Options"
    )

    if ([string]::IsNullOrWhiteSpace($Runtime)) {
        $Runtime = Get-RhinoMcpRuntime -RuntimeSettingsPath $RuntimeSettingsPath
    }

    $packageRoot = Get-RhinoMcpPackageRoot -RoamingAppData $RoamingAppData
    $versionRoot = [IO.Path]::GetFullPath((Join-Path $packageRoot $Version))
    $pluginPath = [IO.Path]::GetFullPath(
        (Join-Path $versionRoot "$Runtime\RhinoMCP.rhp")
    )
    if (-not (Test-RhinoMcpChildPath -ChildPath $pluginPath -ParentPath $versionRoot)) {
        throw "Rhino MCP plug-in registration resolved outside its version folder."
    }
    if (-not (Test-Path -LiteralPath $pluginPath -PathType Leaf)) {
        throw "The Rhino MCP $Runtime plug-in is missing: $pluginPath"
    }
    return $pluginPath
}

function Clear-RhinoMcpPluginLoadCache {
    [CmdletBinding()]
    param(
        [string]$RegistryVersionRoot = "HKCU:\Software\McNeel\Rhinoceros\8.0"
    )

    if (-not (Test-Path -LiteralPath $RegistryVersionRoot)) {
        return
    }

    foreach ($versionKey in Get-ChildItem -LiteralPath $RegistryVersionRoot) {
        if ($versionKey.PSChildName -in @("Plug-ins", "Global Options")) {
            continue
        }
        $cachedPlugin = Join-Path $versionKey.PSPath "Plug-ins\$script:RhinoMcpPluginId"
        if (Test-Path -LiteralPath $cachedPlugin) {
            Write-Host "Clearing stale Rhino MCP discovery cache: $($versionKey.PSChildName)"
            Remove-Item -LiteralPath $cachedPlugin -Recurse -Force
        }
    }
}

function Register-RhinoMcpPlugin {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [string]$RoamingAppData = $env:APPDATA,
        [string]$RegistryVersionRoot = "HKCU:\Software\McNeel\Rhinoceros\8.0",
        [string]$RuntimeSettingsPath =
            "HKCU:\Software\McNeel\Rhinoceros\8.0\Global Options"
    )

    $runtime = Get-RhinoMcpRuntime -RuntimeSettingsPath $RuntimeSettingsPath
    $pluginPath = Get-RhinoMcpPluginPath `
        -Version $Version `
        -Runtime $runtime `
        -RoamingAppData $RoamingAppData `
        -RuntimeSettingsPath $RuntimeSettingsPath
    $versionRoot = Split-Path -Parent (Split-Path -Parent $pluginPath)

    Write-Host "Unblocking the installed Rhino MCP plug-in files..."
    Get-ChildItem -LiteralPath $versionRoot -Recurse -File | ForEach-Object {
        Unblock-File -LiteralPath $_.FullName -ErrorAction Stop
    }

    Clear-RhinoMcpPluginLoadCache -RegistryVersionRoot $RegistryVersionRoot

    $registrationRoot = Join-Path $RegistryVersionRoot "Plug-ins"
    $registrationPath = Join-Path $registrationRoot $script:RhinoMcpPluginId
    if (Test-Path -LiteralPath $registrationPath) {
        Remove-Item -LiteralPath $registrationPath -Recurse -Force
    }
    New-Item -Path $registrationRoot -Force | Out-Null
    New-Item -Path $registrationPath -Force | Out-Null
    New-ItemProperty `
        -LiteralPath $registrationPath `
        -Name "Name" `
        -Value "Rhino MCP" `
        -PropertyType String `
        -Force | Out-Null
    New-ItemProperty `
        -LiteralPath $registrationPath `
        -Name "FileName" `
        -Value $pluginPath `
        -PropertyType String `
        -Force | Out-Null
    New-ItemProperty `
        -LiteralPath $registrationPath `
        -Name "LoadMode" `
        -Value 1 `
        -PropertyType DWord `
        -Force | Out-Null

    $registration = Get-ItemProperty -LiteralPath $registrationPath
    if ($registration.Name -ne "Rhino MCP" -or
        $registration.FileName -ne $pluginPath -or
        [int]$registration.LoadMode -ne 1) {
        throw "Rhino MCP plug-in registration verification failed: $registrationPath"
    }

    Write-Host "Registered Rhino MCP for automatic startup: $pluginPath"
}

function Unregister-RhinoMcpPlugin {
    [CmdletBinding()]
    param(
        [string]$RoamingAppData = $env:APPDATA,
        [string]$RegistryVersionRoot = "HKCU:\Software\McNeel\Rhinoceros\8.0"
    )

    $registrationPath = Join-Path `
        (Join-Path $RegistryVersionRoot "Plug-ins") `
        $script:RhinoMcpPluginId
    if (Test-Path -LiteralPath $registrationPath) {
        $registration = Get-ItemProperty -LiteralPath $registrationPath
        $fileNameProperty = $registration.PSObject.Properties["FileName"]
        $fileName = if ($null -ne $fileNameProperty -and $null -ne $fileNameProperty.Value) {
            $fileNameProperty.Value.ToString()
        } else {
            ""
        }
        $packageRoot = Get-RhinoMcpPackageRoot -RoamingAppData $RoamingAppData
        if (-not [string]::IsNullOrWhiteSpace($fileName) -and
            (Test-RhinoMcpChildPath -ChildPath $fileName -ParentPath $packageRoot)) {
            Remove-Item -LiteralPath $registrationPath -Recurse -Force
            Write-Host "Removed Rhino MCP plug-in registration."
        }
    }

    Clear-RhinoMcpPluginLoadCache -RegistryVersionRoot $RegistryVersionRoot
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
