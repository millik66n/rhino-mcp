[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$distRoot = Join-Path $repoRoot "dist"
$payloadRoot = Join-Path $distRoot "windows"
$buildRoot = Join-Path $repoRoot "build\windows-installer"
$yakStage = Join-Path $buildRoot "yak"

Push-Location $repoRoot
try {
    $version = (& python -c "from rhino_mcp import __version__; print(__version__)").Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($version)) {
        throw "Could not determine the Rhino MCP version."
    }

    if (Test-Path -LiteralPath $payloadRoot) {
        Remove-Item -LiteralPath $payloadRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $buildRoot) {
        Remove-Item -LiteralPath $buildRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $payloadRoot, $yakStage -Force | Out-Null

    & dotnet build "native\RhinoMCP.Plugin\RhinoMCP.Plugin.csproj" -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Rhino plug-in build failed." }
    & dotnet build "native\RhinoMCP.Grasshopper\RhinoMCP.Grasshopper.csproj" -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Grasshopper add-on build failed." }

    $yakNet48 = Join-Path $yakStage "net48"
    $yakNet7 = Join-Path $yakStage "net7.0"
    New-Item -ItemType Directory -Path $yakNet48, $yakNet7 -Force | Out-Null

    Copy-Item "native\RhinoMCP.Plugin\bin\$Configuration\net7.0\RhinoMCP.rhp" $yakNet7
    Copy-Item "native\RhinoMCP.Grasshopper\bin\$Configuration\net7.0\RhinoMCP.Grasshopper.gha" $yakNet7

    Copy-Item "native\RhinoMCP.Plugin\bin\$Configuration\net48\RhinoMCP.rhp" $yakNet48
    Copy-Item "native\RhinoMCP.Grasshopper\bin\$Configuration\net48\RhinoMCP.Grasshopper.gha" $yakNet48
    Copy-Item "native\RhinoMCP.Plugin\bin\$Configuration\net48\*.dll" $yakNet48
    Copy-Item "native\package\manifest.yml" $yakStage

    $yakZip = Join-Path $payloadRoot "rhino-mcp-easy-$version-rh8_0-win.zip"
    $yakPackage = [System.IO.Path]::ChangeExtension($yakZip, ".yak")
    Compress-Archive -Path (Join-Path $yakStage "*") -DestinationPath $yakZip -CompressionLevel Optimal
    Move-Item -LiteralPath $yakZip -Destination $yakPackage
    Copy-Item -LiteralPath $yakPackage -Destination (Join-Path $distRoot ([IO.Path]::GetFileName($yakPackage)))

    & python -m PyInstaller `
        --noconfirm `
        --clean `
        --onedir `
        --console `
        --name "rhino-mcp" `
        --distpath $payloadRoot `
        --workpath (Join-Path $buildRoot "pyinstaller") `
        --specpath $buildRoot `
        --collect-data "mcp" `
        --copy-metadata "mcp" `
        --copy-metadata "rhino-mcp" `
        "installer\rhino_mcp_entry.py"
    if ($LASTEXITCODE -ne 0) { throw "Bundled MCP server build failed." }

    $bundledServer = Join-Path $payloadRoot "rhino-mcp\rhino-mcp.exe"
    if (-not (Test-Path -LiteralPath $bundledServer -PathType Leaf)) {
        throw "PyInstaller did not create $bundledServer."
    }
    & $bundledServer --version
    if ($LASTEXITCODE -ne 0) { throw "The bundled MCP server failed its smoke test." }
    & python "scripts\smoke-bundled-server.py" $bundledServer
    if ($LASTEXITCODE -ne 0) { throw "The bundled MCP stdio handshake failed." }

    $isccCandidates = @(
        (Get-Command "ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) }
    $iscc = $isccCandidates | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($iscc)) {
        throw "Inno Setup compiler ISCC.exe was not found."
    }

    & $iscc `
        "/DAppVersion=$version" `
        "/DPayloadDir=$payloadRoot" `
        "/DInstallerOutputDir=$distRoot" `
        "installer\RhinoMCP.iss"
    if ($LASTEXITCODE -ne 0) { throw "Windows installer compilation failed." }

    $installer = Join-Path $distRoot "RhinoMCP-Windows-Setup-$version.exe"
    if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
        throw "Inno Setup did not create $installer."
    }
    Write-Host "Windows installer: $installer"
}
finally {
    Pop-Location
}
