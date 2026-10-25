param([string]$LauncherPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Plain Craft Launcher 2.exe'))

$ErrorActionPreference = 'Stop'
$expectedHash = '6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386'
$expectedFixedHash = 'BA7585206D97D63AD6F48CDF5FE3F79757B405990D85D1009E1EE19EDF4272A9'
$source = (Resolve-Path -LiteralPath $LauncherPath).Path
$output = Join-Path (Split-Path $source -Parent) 'Plain Craft Launcher 2 (Download Fixed).exe'
if ($source -eq $output) { throw 'Input and output paths must differ.' }

$bytes = [System.IO.File]::ReadAllBytes($source)
$sha = [System.Security.Cryptography.SHA256]::Create()
try { $originalHash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '') }
finally { $sha.Dispose() }
if ($originalHash -ne $expectedHash) { throw 'Unsupported launcher. This patch supports only the inspected PCL 2.99.0.0 build 413.' }
if (Test-Path -LiteralPath $output) {
    if ((Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash -eq $expectedFixedHash) {
        Write-Host "Already repaired: $output"
        Write-Host 'No file was overwritten.'
        return
    }
    throw 'A different file already occupies the output path. No file was overwritten.'
}

# PageDownloadInstall.LoaderInit, IL_001A through IL_0049.
# Replace the disabled LoaderTask construction with ModDownload.DlClientListLoader.
# Keep method size, metadata, resources, and remaining instructions unchanged.
$offset = 0x2C0DA
$length = 48
if ($bytes[$offset] -ne 0x72 -or $bytes[$offset + 43] -ne 0x73) {
    throw 'Unexpected instruction sequence. No output was written.'
}
$before = [BitConverter]::ToString($bytes, $offset, $length)
[Array]::Clear($bytes, $offset, $length)
$bytes[$offset] = 0x7E
[Array]::Copy([BitConverter]::GetBytes([int]0x0400040F), 0, $bytes, $offset + 1, 4)
$sha = [System.Security.Cryptography.SHA256]::Create()
try { $patchedHash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '') }
finally { $sha.Dispose() }
if ($patchedHash -ne $expectedFixedHash) { throw 'Patched output verification failed. No output was written.' }

# CreateNew refuses to overwrite an existing launcher, including a concurrent run.
$file = [System.IO.File]::Open($output, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
try { $file.Write($bytes, 0, $bytes.Length) }
finally { $file.Dispose() }
$fixedHash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'The source launcher changed during repair. Inspect both files before continuing.'
}
$report = [ordered]@{
    version = '2.99.0.0 (413)'
    original = $source
    originalSha256 = $originalHash
    repaired = $output
    repairedSha256 = $fixedHash
    method = 'PCL.PageDownloadInstall.LoaderInit'
    fileOffset = '0x2C0DA'
    byteRangeLength = $length
    before = $before
    after = [BitConverter]::ToString($bytes, $offset, $length)
    scope = 'Minecraft automatic-install version list; modpack import is not restored.'
}
$report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path $source -Parent) 'repair-report.json') -Encoding UTF8
Write-Host "Created: $output"
Write-Host "SHA256: $fixedHash"
Write-Host 'The original launcher has been preserved.'
