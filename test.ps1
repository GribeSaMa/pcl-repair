param([string]$LauncherPath = (Join-Path $PSScriptRoot 'Plain Craft Launcher 2.exe'))

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $LauncherPath -PathType Leaf)) {
    throw 'Supply the verified original PCL executable with -LauncherPath. The launcher is not included in this repository.'
}
$buildDirectory = Join-Path $PSScriptRoot 'tools\build'
New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
$testExe = Join-Path $buildDirectory 'CoreTests.exe'
& $compiler /nologo /target:exe /optimize+ /codepage:65001 "/out:$testExe" /reference:System.dll /reference:System.Core.dll "$PSScriptRoot\src\RepairCore.cs" "$PSScriptRoot\tests\CoreTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
$temporaryBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$testDirectory = Join-Path $temporaryBase ('PclRepair-Tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDirectory | Out-Null
try {
    & $testExe ([System.IO.Path]::GetFullPath($LauncherPath)) $testDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    $evidenceDirectory = Join-Path $PSScriptRoot 'tests\evidence'
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $lines = Get-Content -LiteralPath (Join-Path $testDirectory 'latest-results.txt') | Where-Object { $_ -notlike 'Evidence:*' }
    $lines | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'latest-results.txt') -Encoding UTF8
}
finally {
    $resolvedTests = [System.IO.Path]::GetFullPath($testDirectory)
    if (-not $resolvedTests.StartsWith($temporaryBase.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Test cleanup escaped the temporary directory.' }
    if ([System.IO.Path]::GetFileName($resolvedTests) -notlike 'PclRepair-Tests-*') { throw 'Unexpected test cleanup target.' }
    Remove-Item -LiteralPath $resolvedTests -Recurse -Force
}
