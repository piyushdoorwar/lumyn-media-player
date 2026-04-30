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
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = "0.1.4" }

if ($Rid -ne "win-x64") {
    throw "Unsupported Windows RID: $Rid. This script currently packages win-x64."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
Set-Location $repoRoot

$appProject = "src/Lumyn.App/Lumyn.App.csproj"
$publishDir = Join-Path "artifacts/publish" $Rid
$packageDir = Join-Path "artifacts/pkg" "lumyn-windows"
$packageRoot = Join-Path $packageDir "Lumyn"
$packageOutDir = "artifacts/packages"
$zipFile = Join-Path $packageOutDir "lumyn_${Version}_win-x64.zip"

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

Remove-Item -Force $zipFile -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipFile -CompressionLevel Optimal

Write-Host "Windows artifacts:"
Write-Host $zipFile
