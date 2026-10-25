param([string]$Version = '26.3')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$minecraft = Join-Path $root '.minecraft'
$metadata = Get-Content -Raw -LiteralPath (Join-Path $minecraft "versions\$Version\$Version.json") | ConvertFrom-Json
$failures = [System.Collections.Generic.List[string]]::new()
$verified = 0
$totalBytes = [long]0

function Test-DownloadedFile([string]$Path, [string]$Sha1, [long]$Size) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        $failures.Add("Missing: $Path")
        return
    }
    $actualSize = (Get-Item -LiteralPath $Path).Length
    if ($actualSize -ne $Size) { $failures.Add("Wrong size: $Path"); return }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA1).Hash -ne $Sha1) {
        $failures.Add("SHA1 mismatch: $Path")
        return
    }
    $script:verified++
    $script:totalBytes += $actualSize
}

Test-DownloadedFile (Join-Path $minecraft "versions\$Version\$Version.jar") $metadata.downloads.client.sha1 $metadata.downloads.client.size
$clientVerified = $verified -eq 1
$indexPath = Join-Path $minecraft "assets\indexes\$($metadata.assetIndex.id).json"
Test-DownloadedFile $indexPath $metadata.assetIndex.sha1 $metadata.assetIndex.size
$indexData = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json
$assetHashes = @{}
foreach ($item in $indexData.objects.PSObject.Properties) {
    $asset = $item.Value
    if ($assetHashes.ContainsKey($asset.hash)) { continue }
    $assetHashes[$asset.hash] = $true
    Test-DownloadedFile (Join-Path $minecraft "assets\objects\$($asset.hash.Substring(0, 2))\$($asset.hash)") $asset.hash $asset.size
}

$libraryCount = 0
foreach ($library in $metadata.libraries) {
    $allowed = -not $library.rules
    foreach ($rule in $library.rules) {
        $matchesPlatform = $true
        if ($rule.os.name -and $rule.os.name -ne 'windows') { $matchesPlatform = $false }
        if ($rule.os.arch -and $rule.os.arch -notin @('x86_64', 'amd64', 'x64')) { $matchesPlatform = $false }
        if ($rule.os.version -and [Environment]::OSVersion.Version.ToString() -notmatch $rule.os.version) { $matchesPlatform = $false }
        if ($rule.features) {
            foreach ($feature in $rule.features.PSObject.Properties) {
                if ($feature.Value -eq $true) { $matchesPlatform = $false }
            }
        }
        if ($matchesPlatform) { $allowed = $rule.action -eq 'allow' }
    }
    if (-not $allowed) { continue }
    $artifact = $library.downloads.artifact
    if ($artifact) {
        Test-DownloadedFile (Join-Path $minecraft ('libraries\' + $artifact.path)) $artifact.sha1 $artifact.size
        $libraryCount++
    }
    if ($library.natives.windows) {
        $classifierName = $library.natives.windows.Replace('${arch}', '64')
        $nativeArtifact = $library.downloads.classifiers.$classifierName
        if ($nativeArtifact) {
            Test-DownloadedFile (Join-Path $minecraft ('libraries\' + $nativeArtifact.path)) $nativeArtifact.sha1 $nativeArtifact.size
            $libraryCount++
        }
    }
}

$result = [ordered]@{
    version = $Version
    clientJarVerified = $clientVerified
    assetObjects = $assetHashes.Count
    windowsLibraryFiles = $libraryCount
    verifiedFiles = $verified
    verifiedBytes = $totalBytes
    failures = @($failures.ToArray())
    gameLaunched = $false
}
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'download-verification.json') -Encoding UTF8
$result | ConvertTo-Json -Depth 5
if ($failures.Count) { throw "Download verification failed: $($failures.Count) file(s)." }
