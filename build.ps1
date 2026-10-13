#!/usr/bin/env pwsh
<#
.SYNOPSIS
    NetBind Pro build script — wraps dotnet CLI for common tasks.

.DESCRIPTION
    Handles restoring packages, building both x86 and x64 Hook variants,
    publishing, and optionally building the Inno Setup installer.

.PARAMETER Configuration
    Build configuration: Debug or Release (default: Release)

.PARAMETER Publish
    If set, publishes the output to ./publish/win-x64

.PARAMETER Installer
    If set, builds the Inno Setup installer (requires ISCC in PATH)

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Debug
    .\build.ps1 -Publish -Installer
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$Publish,
    [switch]$Installer,
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Solution  = "NetBindPro.sln"
$PublishDir = "publish\win-x64"

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "  ▶ $msg" -ForegroundColor Cyan
    Write-Host ""
}

function Assert-ExitCode {
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ✕ Build failed (exit $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

# ── Clean ────────────────────────────────────────────────────────────────────

if ($Clean) {
    Write-Step "Cleaning..."
    Get-ChildItem -Recurse -Directory -Filter "bin"   | Remove-Item -Recurse -Force
    Get-ChildItem -Recurse -Directory -Filter "obj"   | Remove-Item -Recurse -Force
    Remove-Item -Recurse -Force "publish" -ErrorAction SilentlyContinue
    Write-Host "  ✓ Clean complete" -ForegroundColor Green
}

# ── Restore ──────────────────────────────────────────────────────────────────

Write-Step "Restoring NuGet packages..."
dotnet restore $Solution
Assert-ExitCode
Write-Host "  ✓ Restore complete" -ForegroundColor Green

# ── Build Core ───────────────────────────────────────────────────────────────

Write-Step "Building NetBindPro.Core (x64)..."
dotnet build "src\NetBindPro.Core\NetBindPro.Core.csproj" `
    --configuration $Configuration --no-restore /p:Platform=x64
Assert-ExitCode

# ── Build Hook (x64 + x86) ───────────────────────────────────────────────────

Write-Step "Building NetBindPro.Hook (x64)..."
dotnet build "src\NetBindPro.Hook\NetBindPro.Hook.csproj" `
    --configuration $Configuration --no-restore /p:Platform=x64
Assert-ExitCode

Write-Step "Building NetBindPro.Hook (x86) — required for 32-bit target processes..."
dotnet build "src\NetBindPro.Hook\NetBindPro.Hook.csproj" `
    --configuration $Configuration --no-restore /p:Platform=x86
Assert-ExitCode

# ── Build UI ─────────────────────────────────────────────────────────────────

Write-Step "Building NetBindPro.UI (x64)..."
dotnet build "src\NetBindPro.UI\NetBindPro.UI.csproj" `
    --configuration $Configuration --no-restore /p:Platform=x64
Assert-ExitCode

Write-Host ""
Write-Host "  ✓ Build complete ($Configuration)" -ForegroundColor Green

# ── Publish ──────────────────────────────────────────────────────────────────

if ($Publish -or $Installer) {
    Write-Step "Publishing to $PublishDir..."

    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

    dotnet publish "src\NetBindPro.UI\NetBindPro.UI.csproj" `
        --configuration Release --runtime win-x64 --self-contained `
        --output $PublishDir /p:Platform=x64
    Assert-ExitCode

    dotnet publish "src\NetBindPro.Hook\NetBindPro.Hook.csproj" `
        --configuration Release --runtime win-x64 --self-contained `
        --output $PublishDir /p:Platform=x64
    Assert-ExitCode

    # Build x86 Hook and copy alongside x64 output
    $Publish86Dir = "publish\win-x86"
    dotnet publish "src\NetBindPro.Hook\NetBindPro.Hook.csproj" `
        --configuration Release --runtime win-x86 --self-contained `
        --output $Publish86Dir /p:Platform=x86
    Assert-ExitCode

    Copy-Item "$Publish86Dir\NetBindPro.Hook.exe" "$PublishDir\NetBindPro.Hook.x86.exe" -Force

    Write-Host "  ✓ Publish complete → $PublishDir" -ForegroundColor Green
}

# ── Installer ────────────────────────────────────────────────────────────────

if ($Installer) {
    Write-Step "Building Inno Setup installer..."

    $iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if (-not $iscc) {
        Write-Host "  ✕ ISCC.exe not found in PATH. Install Inno Setup 6: https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
    } else {
        New-Item -ItemType Directory -Path "publish\installer" -Force | Out-Null
        & ISCC.exe "installer\NetBindPro.Setup.iss" /O"publish\installer"
        Assert-ExitCode
        Write-Host "  ✓ Installer built → publish\installer\" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "  ══ Done ══" -ForegroundColor Cyan
Write-Host ""
