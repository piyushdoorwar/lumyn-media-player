param(
    [string]$Configuration = $env:CONFIGURATION,
    [string]$Rid = $env:RID,
    [string]$Version = $env:VERSION,
    [string]$MpvBinDir = $env:MPV_BIN_DIR,
    [string]$MpvArchiveUrl = $env:MPV_ARCHIVE_URL
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Configuration)) { $Configuration = "Release" }
if ([string]::IsNullOrWhiteSpace($Rid)) { $Rid = "win-x64" }
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "0.0.0-dev"
}

if ($Rid -ne "win-x64") {
    throw "Unsupported Windows RID: $Rid. This script currently packages win-x64."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
Set-Location $repoRoot

$appProject = "src/Lumyn.App/Lumyn.App.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\$Rid"
$packageDir = Join-Path $repoRoot "artifacts\pkg\lumyn-windows"
$packageRoot = Join-Path $packageDir "Lumyn"
$packageOutDir = Join-Path $repoRoot "artifacts\packages"

function Get-MpvArchiveUrl {
    param(
        [ValidateSet("Runtime", "Dev")]
        [string]$Kind
    )

    $apiUrl = "https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/latest"
    $release = Invoke-RestMethod -Uri $apiUrl -Headers @{ "User-Agent" = "Lumyn-Packager" }

    $pattern = if ($Kind -eq "Dev") {
        '^mpv-dev-x86_64.*\.7z$'
    } else {
        '^mpv-x86_64.*\.7z$'
    }

    $asset = $release.assets |
        Where-Object {
            $_.name -match $pattern -and
            $_.name -notmatch 'debug|symbols'
        } |
        Select-Object -First 1

    if ($null -eq $asset) {
        throw "Could not find a win-x64 mpv $Kind archive in the latest shinchiro/mpv-winbuild-cmake release."
    }

    return $asset.browser_download_url
}

function Expand-MpvArchive {
    param(
        [string]$ArchiveUrl,
        [string]$Destination,
        [string]$FileName
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $archivePath = Join-Path $Destination $FileName
    Invoke-WebRequest -Uri $ArchiveUrl -OutFile $archivePath -Headers @{ "User-Agent" = "Lumyn-Packager" }

    $sevenZip = Get-Command 7z -ErrorAction SilentlyContinue
    if ($null -eq $sevenZip) {
        $sevenZip = Get-Command 7za -ErrorAction SilentlyContinue
    }
    if ($null -eq $sevenZip) {
        throw "7z/7za was not found. Install 7-Zip or provide MPV_BIN_DIR with mpv-2.dll and its dependency DLLs."
    }

    & $sevenZip.Source x $archivePath "-o$Destination" -y | Out-Null
}

function Resolve-MpvDirectory {
    param(
        [string]$ProvidedDir,
        [string]$ArchiveUrl
    )

    if (-not [string]::IsNullOrWhiteSpace($ProvidedDir)) {
        $resolved = Resolve-Path $ProvidedDir
        $dll = Get-ChildItem -Path $resolved -Recurse -File |
            Where-Object { $_.Name -in @("mpv-2.dll", "libmpv-2.dll") } |
            Select-Object -First 1
        if ($null -eq $dll) {
            throw "MPV_BIN_DIR does not contain mpv-2.dll or libmpv-2.dll: $ProvidedDir"
        }
        return $resolved.Path
    }

    $extractDir = Join-Path "artifacts/downloads" "mpv-win-x64"
    Remove-Item -Recurse -Force $extractDir -ErrorAction SilentlyContinue

    if ([string]::IsNullOrWhiteSpace($ArchiveUrl)) {
        $runtimeUrl = Get-MpvArchiveUrl -Kind Runtime
        $devUrl = Get-MpvArchiveUrl -Kind Dev
        Expand-MpvArchive -ArchiveUrl $runtimeUrl -Destination $extractDir -FileName "mpv-runtime.7z"
        Expand-MpvArchive -ArchiveUrl $devUrl -Destination $extractDir -FileName "mpv-dev.7z"
    } else {
        Expand-MpvArchive -ArchiveUrl $ArchiveUrl -Destination $extractDir -FileName "mpv.7z"
    }

    $mpvDll = Get-ChildItem -Path $extractDir -Recurse -File |
        Where-Object { $_.Name -in @("mpv-2.dll", "libmpv-2.dll") } |
        Select-Object -First 1
    if ($null -eq $mpvDll) {
        throw "The mpv archive set did not contain mpv-2.dll or libmpv-2.dll."
    }

    return $extractDir
}

function Copy-MpvRuntime {
    param(
        [string]$SourceDir,
        [string]$DestinationDir
    )

    Get-ChildItem -Path $SourceDir -Recurse -File |
        Where-Object { $_.Extension -in @(".dll", ".conf") } |
        ForEach-Object {
            Copy-Item $_.FullName (Join-Path $DestinationDir $_.Name) -Force
        }

    $mpvDll = Get-ChildItem -Path $DestinationDir -File |
        Where-Object { $_.Name -in @("mpv-2.dll", "libmpv-2.dll") } |
        Select-Object -First 1
    if ($null -eq $mpvDll) {
        throw "mpv runtime DLL was not copied into $DestinationDir."
    }
}

function Write-FileAssociationScript {
    param(
        [string]$DestinationDir
    )

    $extensions = @(
        # Video
        ".mp4", ".m4v",
        ".mkv", ".mk3d",
        ".webm",
        ".avi",
        ".mov",
        ".mpg", ".mpeg",
        ".flv",
        ".3gp",
        ".wmv",
        ".ogv", ".ogm",
        ".ts",
        ".divx",
        # Audio
        ".mp3",
        ".flac",
        ".ogg", ".oga",
        ".wav",
        ".m4a",
        ".aac",
        ".wma",
        ".opus",
        ".mka"
    )

    $scriptContent = @'
<#
.SYNOPSIS
    Registers Lumyn as a handler for common media file types in Windows.
    Writes to HKCU (current user only) — no administrator rights required.
.DESCRIPTION
    Run this script once after extracting Lumyn to your preferred location.
    To unregister, run with -Unregister.
.PARAMETER Unregister
    Removes Lumyn file associations from the current user account.
#>
param(
    [switch]$Unregister
)

$ErrorActionPreference = "Stop"

$exePath = Join-Path $PSScriptRoot "Lumyn.exe"
if (-not (Test-Path $exePath)) {
    throw "Lumyn.exe not found at: $exePath. Run this script from the Lumyn installation folder."
}

$progId   = "Lumyn.MediaFile"
$classKey = "HKCU:\Software\Classes"

if ($Unregister) {
    Write-Host "Removing Lumyn file associations..."

    $extensions = @(
'@
    foreach ($ext in $extensions) {
        $scriptContent += "        `"$ext`",`n"
    }
    # Remove trailing comma+newline, close array
    $scriptContent = $scriptContent.TrimEnd(",`n") + "`n"
    $scriptContent += @'
    )

    foreach ($ext in $extensions) {
        $key = Join-Path $classKey $ext
        if (Test-Path $key) {
            $val = (Get-ItemProperty -Path $key -Name "(default)" -ErrorAction SilentlyContinue)."(default)"
            if ($val -eq $progId) {
                Remove-Item -Path $key -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "  Removed $ext"
            }
        }
    }

    $progKey = Join-Path $classKey $progId
    if (Test-Path $progKey) {
        Remove-Item -Path $progKey -Recurse -Force
        Write-Host "  Removed ProgId: $progId"
    }

    # Notify the shell
    $sig = '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);'
    $type = Add-Type -MemberDefinition $sig -Name "WinAPI" -Namespace "Shell32" -PassThru
    $type::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)

    Write-Host "Done. You may need to sign out and back in for all changes to take effect."
    exit 0
}

Write-Host "Registering Lumyn file associations..."

# Create the ProgId entry
$progKey = Join-Path $classKey $progId
New-Item -Path $progKey -Force | Out-Null
Set-ItemProperty -Path $progKey -Name "(default)" -Value "Media file"

$iconKey = Join-Path $progKey "DefaultIcon"
New-Item -Path $iconKey -Force | Out-Null
Set-ItemProperty -Path $iconKey -Name "(default)" -Value "`"$exePath`",0"

$openKey = Join-Path $progKey "shell\open\command"
New-Item -Path $openKey -Force | Out-Null
Set-ItemProperty -Path $openKey -Name "(default)" -Value "`"$exePath`" `"%1`""

$extensions = @(
'@
    foreach ($ext in $extensions) {
        $scriptContent += "    `"$ext`",`n"
    }
    $scriptContent = $scriptContent.TrimEnd(",`n") + "`n"
    $scriptContent += @'
)

foreach ($ext in $extensions) {
    $key = Join-Path $classKey $ext
    New-Item -Path $key -Force | Out-Null
    Set-ItemProperty -Path $key -Name "(default)" -Value $progId
    Write-Host "  Registered $ext"
}

# Notify the shell of association changes
$sig = '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);'
$type = Add-Type -MemberDefinition $sig -Name "WinAPI" -Namespace "Shell32" -PassThru
$type::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host "Done. Lumyn is now set as the default player for the above file types."
Write-Host "To unregister: .\Register-FileAssociations.ps1 -Unregister"
'@

    $scriptPath = Join-Path $DestinationDir "Register-FileAssociations.ps1"
    [System.IO.File]::WriteAllText($scriptPath, $scriptContent, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Written: $scriptPath"
}

function Get-FfmpegExe {
    param(
        [string]$DestinationDir
    )

    # If a pre-built ffmpeg.exe is already staged (e.g. from a CI cache or manual setup)
    $staged = Join-Path $DestinationDir "ffmpeg.exe"
    if (Test-Path $staged) {
        Write-Host "Using staged ffmpeg.exe"
        return
    }

    Write-Host "Downloading ffmpeg for Windows..."
    $ffmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip"
    $extractDir = Join-Path "artifacts/downloads" "ffmpeg-win-x64"
    Remove-Item -Recurse -Force $extractDir -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

    $zipPath = Join-Path $extractDir "ffmpeg.zip"
    Invoke-WebRequest -Uri $ffmpegUrl -OutFile $zipPath -Headers @{ "User-Agent" = "Lumyn-Packager" }
    Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

    $ffmpegExe = Get-ChildItem -Path $extractDir -Recurse -File -Filter "ffmpeg.exe" | Select-Object -First 1
    if ($null -eq $ffmpegExe) {
        throw "ffmpeg.exe not found in the downloaded archive."
    }

    Copy-Item $ffmpegExe.FullName (Join-Path $DestinationDir "ffmpeg.exe") -Force
    Write-Host "Copied: ffmpeg.exe"
}

function Copy-Notices {
    param(
        [string]$MpvSourceDir,
        [string]$DestinationDir
    )

    $noticesDir = Join-Path $DestinationDir "licenses"
    New-Item -ItemType Directory -Force -Path $noticesDir | Out-Null

    Copy-Item "LICENSE" (Join-Path $noticesDir "Lumyn-LICENSE.txt") -Force

    $projectNotice = "packaging/windows/THIRD-PARTY-NOTICES.txt"
    if (Test-Path $projectNotice) {
        Copy-Item $projectNotice (Join-Path $noticesDir "THIRD-PARTY-NOTICES.txt") -Force
    }

    Get-ChildItem -Path $MpvSourceDir -Recurse -File |
        Where-Object { $_.Name -match '^(LICENSE|COPYING|Copyright|NOTICE)' } |
        ForEach-Object {
            Copy-Item $_.FullName (Join-Path $noticesDir ("mpv-" + $_.Name + ".txt")) -Force
        }
}

function New-PlaceholderImage {
    param(
        [string]$OutputPath,
        [int]$Width,
        [int]$Height
    )

    # Create a minimal valid PNG file (1x1 transparent pixel, scaled)
    # PNG magic number + minimal IHDR chunk for the specified dimensions
    $pngBytes = @(
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,  # PNG signature
        0x00, 0x00, 0x00, 0x0D,                            # IHDR chunk size
        0x49, 0x48, 0x44, 0x52,                            # "IHDR"
        [byte]($Width -shr 24), [byte]($Width -shr 16), [byte]($Width -shr 8), [byte]$Width,  # Width
        [byte]($Height -shr 24), [byte]($Height -shr 16), [byte]($Height -shr 8), [byte]$Height,  # Height
        0x08, 0x02, 0x00, 0x00, 0x00,                      # Bit depth, color type, compression, filter, interlace
        0xAF, 0xC8, 0xB5, 0x5C,                            # CRC
        0x00, 0x00, 0x00, 0x0C,                            # IDAT chunk size
        0x49, 0x44, 0x41, 0x54,                            # "IDAT"
        0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00, 0x00,  # Compressed pixel data
        0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D, 0xB4,  # End of IDAT
        0x00, 0x00, 0x00, 0x00,                            # IEND chunk size
        0x49, 0x45, 0x4E, 0x44,                            # "IEND"
        0xAE, 0x42, 0x60, 0x82                             # CRC
    )

    [System.IO.File]::WriteAllBytes($OutputPath, $pngBytes)
    Write-Host "Generated placeholder image: $OutputPath ($Width x $Height)"
}

function Ensure-MsixAssets {
    param(
        [string]$AssetsDir
    )

    New-Item -ItemType Directory -Force -Path $AssetsDir | Out-Null

    $requiredImages = @(
        @{ Name = "square44x44logo.png"; Width = 44; Height = 44 }
        @{ Name = "square71x71logo.png"; Width = 71; Height = 71 }
        @{ Name = "square150x150logo.png"; Width = 150; Height = 150 }
        @{ Name = "wide310x150logo.png"; Width = 310; Height = 150 }
        @{ Name = "square310x310logo.png"; Width = 310; Height = 310 }
        @{ Name = "StoreLogo.png"; Width = 50; Height = 50 }
        @{ Name = "SplashScreen.png"; Width = 620; Height = 300 }
    )

    foreach ($image in $requiredImages) {
        $imagePath = Join-Path $AssetsDir $image.Name
        if (-not (Test-Path $imagePath)) {
            New-PlaceholderImage -OutputPath $imagePath -Width $image.Width -Height $image.Height
        }
    }

    Write-Host "MSIX assets verified in: $AssetsDir"
}

function New-MsixPackage {
    param(
        [string]$PackageDir,
        [string]$Version,
        [string]$PackageOutDir
    )

    # Convert version to MSIX format (X.X.X.X)
    $versionParts = $Version -split "\."
    $majorMinorPatch = @($versionParts[0], $versionParts[1], $versionParts[2]) -join "."
    if ($versionParts.Count -lt 4) {
        $msixVersion = "$majorMinorPatch.0"
    } else {
        $msixVersion = $Version
    }

    $appxManifest = Join-Path $scriptDir "..\packaging\windows\AppxManifest.xml"
    if (-not (Test-Path $appxManifest)) {
        throw "AppxManifest.xml not found at: $appxManifest"
    }

    $appxManifestTemp = Join-Path $PackageDir "AppxManifest.xml"

    # Copy manifest and update version
    Copy-Item $appxManifest $appxManifestTemp -Force
    
    $manifestContent = Get-Content $appxManifestTemp -Raw
    $manifestContent = $manifestContent -replace 'Version="[^"]*"', "Version=`"$msixVersion`""
    Set-Content $appxManifestTemp -Value $manifestContent
    Write-Host "Manifest updated with version: $msixVersion"

    # Copy assets
    $assetsSource = Join-Path $scriptDir "..\packaging\windows\Assets"
    $assetsTarget = Join-Path $PackageDir "Assets"
    
    if (-not (Test-Path $assetsSource)) {
        Write-Host "Assets directory not found, creating: $assetsSource"
        New-Item -ItemType Directory -Force -Path $assetsSource | Out-Null
    }

    Ensure-MsixAssets -AssetsDir $assetsSource
    Copy-Item $assetsSource $assetsTarget -Recurse -Force -ErrorAction Stop
    Write-Host "Assets copied to: $assetsTarget"

    # Find MakeAppx.exe (comes with Windows SDK)
    $makeappx = $null
    
    # Try to find MakeAppx via command
    $cmdResult = Get-Command makeappx.exe -ErrorAction SilentlyContinue
    if ($null -ne $cmdResult) {
        $makeappx = $cmdResult.Source
        Write-Host "Found MakeAppx via command: $makeappx"
    } else {
        # Try common SDK paths
        $sdk10Paths = @(
            "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\makeappx.exe",
            "C:\Program Files\Windows Kits\10\bin\*\x64\makeappx.exe",
            "C:\Program Files (x86)\Windows Kits\10\bin\10.0.*\x64\makeappx.exe",
            "C:\Program Files\Windows Kits\10\bin\10.0.*\x64\makeappx.exe"
        )
        
        foreach ($pattern in $sdk10Paths) {
            Write-Host "Searching: $pattern"
            $result = @(Get-Item $pattern -ErrorAction SilentlyContinue) | Sort-Object -Descending | Select-Object -First 1
            if ($null -ne $result) {
                $makeappx = $result.FullName
                Write-Host "Found MakeAppx at: $makeappx"
                break
            }
        }
    }

    if ($null -eq $makeappx) {
        throw "MakeAppx.exe not found. Windows SDK must be installed. Searched: $($sdk10Paths -join ', ')"
    }

    $msixPath = Join-Path $PackageOutDir "lumyn_${msixVersion}_win-x64.msix"
    
    Write-Host "Creating MSIX package: $msixPath"
    Write-Host "  Package source: $PackageDir"
    Write-Host "  MakeAppx tool: $makeappx"
    
    & $makeappx pack /d $PackageDir /p $msixPath

    if ($LASTEXITCODE -ne 0) {
        throw "MakeAppx.exe failed with exit code $LASTEXITCODE while creating MSIX package."
    }

    if (-not (Test-Path $msixPath)) {
        throw "MSIX package was not created at: $msixPath"
    }

    $msixSize = (Get-Item $msixPath).Length / 1MB
    Write-Host "MSIX package created successfully: $msixPath ($([math]::Round($msixSize, 2)) MB)"
    return $msixPath
}

dotnet restore Lumyn.sln
dotnet build Lumyn.sln -c $Configuration --no-restore
dotnet publish $appProject -c $Configuration -r $Rid --self-contained true -o $publishDir `
    -p:Version=$Version -p:InformationalVersion=$Version

$mpvDir = Resolve-MpvDirectory -ProvidedDir $MpvBinDir -ArchiveUrl $MpvArchiveUrl

Remove-Item -Recurse -Force $packageDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $packageRoot, $packageOutDir | Out-Null
Copy-Item (Join-Path $publishDir "*") $packageRoot -Recurse -Force
Copy-MpvRuntime -SourceDir $mpvDir -DestinationDir $packageRoot
Get-FfmpegExe -DestinationDir $packageRoot
Copy-Notices -MpvSourceDir $mpvDir -DestinationDir $packageRoot

# ── Compile Windows installer via Inno Setup ──────────────────────────────
# Inno Setup 6 is pre-installed on GitHub Actions windows-latest runners.
$isccExe = $null
$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($null -ne $iscc) {
    $isccExe = $iscc.Source
} else {
    foreach ($candidate in @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 5\ISCC.exe"
    )) {
        if (Test-Path $candidate) { $isccExe = $candidate; break }
    }
}
if ($null -eq $isccExe) {
    throw "Inno Setup compiler (ISCC.exe) not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php"
}

$issScript = Join-Path $scriptDir "..\packaging\windows\lumyn.iss"
& $isccExe `
    "/DAppVersion=$Version" `
    "/DSourceDir=$packageRoot" `
    "/DRepoRoot=$repoRoot" `
    $issScript

if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe exited with code $LASTEXITCODE."
}

$installerFile = Join-Path $packageOutDir "lumyn_${Version}_win-x64_setup.exe"

# ── Create MSIX package ────────────────────────────────────────────────────
$msixFile = New-MsixPackage -PackageDir $packageRoot -Version $Version -PackageOutDir $packageOutDir

Write-Host ""
Write-Host "Windows artifacts:"
Write-Host $installerFile
Write-Host $msixFile
