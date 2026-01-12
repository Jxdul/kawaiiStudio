$ErrorActionPreference = "Stop"

param(
    [string]$OutputRoot = "publish",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true
)

$repoRoot = $PSScriptRoot
$appProject = Join-Path $repoRoot "src\KawaiiStudio.App\KawaiiStudio.App.csproj"
$publishRoot = Join-Path $repoRoot $OutputRoot
$appOut = Join-Path $publishRoot "KawaiiStudio"
$configSource = Join-Path $repoRoot "Config"
$configTarget = Join-Path $publishRoot "Config"
$thirdParty = Join-Path $repoRoot "third_party"

if (-not (Test-Path $appProject)) {
    throw "Project not found: $appProject"
}

$selfContainedValue = $SelfContained.ToString().ToLowerInvariant()

Write-Host "Publishing app to $appOut"
dotnet publish $appProject -c Release -r $Runtime --self-contained $selfContainedValue -o $appOut

if (Test-Path $configSource) {
    Write-Host "Copying Config to $configTarget"
    Copy-Item -Path $configSource -Destination $configTarget -Recurse -Force
} else {
    Write-Host "Config folder not found, skipping."
}

if (Test-Path $thirdParty) {
    $ffmpegDirs = Get-ChildItem -Path $thirdParty -Directory -Filter "ffmpeg*" -ErrorAction SilentlyContinue
    foreach ($dir in $ffmpegDirs) {
        $target = Join-Path $publishRoot "third_party\$($dir.Name)"
        Write-Host "Copying $($dir.FullName) to $target"
        Copy-Item -Path $dir.FullName -Destination $target -Recurse -Force
    }

    if ($ffmpegDirs.Count -eq 0) {
        Write-Host "No ffmpeg folder found under third_party, skipping."
    }
} else {
    Write-Host "third_party folder not found, skipping."
}

New-Item -ItemType Directory -Force -Path (Join-Path $publishRoot "prints") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $publishRoot "videos") | Out-Null

Write-Host "Publish bundle ready in $publishRoot"
