# Build the LumenDownloader single-file setup.

$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "Cannot find the .NET Framework C# compiler: $csc"
}

$work = Join-Path $projectRoot '.installer-build'
$payloadRoot = Join-Path $work 'payload'
$sourceStage = Join-Path $payloadRoot 'source'
$appStage = Join-Path $payloadRoot 'app'
$toolsStage = Join-Path $payloadRoot 'tools'
$outDir = Join-Path $projectRoot 'dist'
$icon = Join-Path $projectRoot 'assets\icon.ico'
$logo = Join-Path $projectRoot 'assets\icon-256.png'
$manifest = Join-Path $projectRoot 'installer-src\setup.manifest'

$appDirectory = if ($env:LUMEN_APP_DIR) { $env:LUMEN_APP_DIR } else { 'C:\Users\Lenovo\Desktop\流光下载器' }
if (-not (Test-Path -LiteralPath (Join-Path $appDirectory '流光下载器.exe'))) {
    throw "Publish the app first, or set LUMEN_APP_DIR to the published app folder."
}
foreach ($required in @($icon, $logo, $manifest)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Missing build asset: $required"
    }
}

if (Test-Path -LiteralPath $work) {
    Remove-Item -LiteralPath $work -Recurse -Force
}
New-Item -ItemType Directory -Force $payloadRoot, $sourceStage, $appStage, $toolsStage, $outDir | Out-Null

function Copy-Tree {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [string[]]$ExcludeDirectories = @()
    )
    if (-not (Test-Path -LiteralPath $Source)) {
        return
    }
    New-Item -ItemType Directory -Force $Destination | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        if ($item.PSIsContainer) {
            if ($ExcludeDirectories -contains $item.Name) {
                continue
            }
            Copy-Tree -Source $item.FullName -Destination (Join-Path $Destination $item.Name) -ExcludeDirectories $ExcludeDirectories
        } else {
            Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $Destination $item.Name) -Force
        }
    }
}

# ---------------------------------------------------------------------------
# 1. Application payload (the verified published build)
# ---------------------------------------------------------------------------
Copy-Tree -Source $appDirectory -Destination $appStage

# ---------------------------------------------------------------------------
# 2. Full source payload, excluding build output and local caches
# ---------------------------------------------------------------------------
$exclude = @('bin', 'obj')
Copy-Tree -Source (Join-Path $projectRoot 'src') -Destination (Join-Path $sourceStage 'src') -ExcludeDirectories $exclude
Copy-Tree -Source (Join-Path $projectRoot 'tests') -Destination (Join-Path $sourceStage 'tests') -ExcludeDirectories $exclude
Copy-Tree -Source (Join-Path $projectRoot 'installer-src') -Destination (Join-Path $sourceStage 'InstallerSource') -ExcludeDirectories $exclude
Copy-Tree -Source (Join-Path $projectRoot 'assets') -Destination (Join-Path $sourceStage 'assets')
Copy-Tree -Source (Join-Path $projectRoot 'scripts') -Destination (Join-Path $sourceStage 'scripts')
Copy-Item -LiteralPath (Join-Path $projectRoot 'installer-src\SOURCE-README.md') -Destination (Join-Path $sourceStage 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $appDirectory '使用说明.txt') -Destination (Join-Path $sourceStage '使用说明.txt') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $appDirectory 'LICENSE.txt') -Destination (Join-Path $sourceStage 'LICENSE.txt') -Force -ErrorAction SilentlyContinue

# A clean NuGet configuration that works on normal machines.
$nugetConfig = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
'@
Set-Content -LiteralPath (Join-Path $sourceStage 'NuGet.config') -Value $nugetConfig -Encoding utf8

# ---------------------------------------------------------------------------
# 3. Compile the uninstaller
# ---------------------------------------------------------------------------
$references = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.IO.Compression.dll',
    '/r:System.IO.Compression.FileSystem.dll'
)

$uninstallerSources = @(
    (Join-Path $PSScriptRoot 'Common.cs'),
    (Join-Path $PSScriptRoot 'UiKit.cs'),
    (Join-Path $PSScriptRoot 'UninstallerForm.cs'),
    (Join-Path $PSScriptRoot 'AssemblyInfo.Uninstaller.cs')
)
$uninstallerPath = Join-Path $toolsStage 'Uninstall.exe'
$uninstallerArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001',
    "/win32icon:$icon",
    "/win32manifest:$manifest",
    "/out:$uninstallerPath",
    "/resource:$logo,LumenLogo.png"
) + $references + $uninstallerSources
& $csc $uninstallerArgs
if ($LASTEXITCODE -ne 0) {
    throw "Uninstaller compilation failed with exit code $LASTEXITCODE"
}

# ---------------------------------------------------------------------------
# 4. Build the embedded payload zip
# ---------------------------------------------------------------------------
$payloadZip = Join-Path $work 'LumenPayload.zip'
Compress-Archive -Path (Join-Path $payloadRoot '*') -DestinationPath $payloadZip -CompressionLevel Optimal -Force

$payloadSize = (Get-Item -LiteralPath $payloadZip).Length
Write-Host ("Payload: {0:N2} MB" -f ($payloadSize / 1MB))

# ---------------------------------------------------------------------------
# 5. Compile the installer
# ---------------------------------------------------------------------------
$installerSources = @(
    (Join-Path $PSScriptRoot 'Common.cs'),
    (Join-Path $PSScriptRoot 'UiKit.cs'),
    (Join-Path $PSScriptRoot 'InstallerForm.cs'),
    (Join-Path $PSScriptRoot 'AssemblyInfo.Installer.cs')
)
$installerPath = Join-Path $outDir '流光下载器-安装程序-v1.0.2.exe'
$installerArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001',
    "/win32icon:$icon",
    "/win32manifest:$manifest",
    "/out:$installerPath",
    "/resource:$logo,LumenLogo.png",
    "/resource:$payloadZip,LumenPayload.zip"
) + $references + $installerSources
& $csc $installerArgs
if ($LASTEXITCODE -ne 0) {
    throw "Installer compilation failed with exit code $LASTEXITCODE"
}

$installer = Get-Item -LiteralPath $installerPath
$version = $installer.VersionInfo
Write-Host ""
Write-Host "Installer built:"
Write-Host ("  {0}" -f $installer.FullName)
Write-Host ("  {0:N2} MB" -f ($installer.Length / 1MB))
Write-Host ("  {0} {1}" -f $version.FileDescription, $version.FileVersion)
