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
    $versionFile = Join-Path (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..")) "VERSION"
    $baseVersion = $env:BASE_VERSION
    if ([string]::IsNullOrWhiteSpace($baseVersion)) {
        $baseVersion = (Get-Content $versionFile -Raw).Trim()
    }

    $buildNumber = $env:BUILD_NUMBER
    if ([string]::IsNullOrWhiteSpace($buildNumber)) {
        $buildNumber = $env:GITHUB_RUN_NUMBER
    }
    if ([string]::IsNullOrWhiteSpace($buildNumber)) {
        $buildNumber = "0"
    }

    $Version = "$baseVersion.$buildNumber"
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

dotnet restore Lumyn.sln
dotnet build Lumyn.sln -c $Configuration --no-restore
dotnet publish $appProject -c $Configuration -r $Rid --self-contained true -o $publishDir

$mpvDir = Resolve-MpvDirectory -ProvidedDir $MpvBinDir -ArchiveUrl $MpvArchiveUrl

Remove-Item -Recurse -Force $packageDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $packageRoot, $packageOutDir | Out-Null
Copy-Item (Join-Path $publishDir "*") $packageRoot -Recurse -Force
Copy-MpvRuntime -SourceDir $mpvDir -DestinationDir $packageRoot
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
Write-Host "Windows artifacts:"
Write-Host $installerFile
