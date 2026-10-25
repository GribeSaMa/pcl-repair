$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$output = Join-Path $PSScriptRoot 'PCL-Repair.exe'
$buildDirectory = Join-Path $PSScriptRoot 'tools\build'
New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
$iconBuilder = Join-Path $buildDirectory 'IconBuilder.exe'
$iconFile = Join-Path $PSScriptRoot 'src\app.ico'
& $compiler /nologo /target:exe /optimize+ "/out:$iconBuilder" /reference:System.Drawing.dll "$PSScriptRoot\tools\IconBuilder.cs"
if ($LASTEXITCODE -ne 0) { throw 'Icon builder compilation failed.' }
& $iconBuilder $iconFile
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /codepage:65001 "/out:$output" "/win32icon:$iconFile" "/win32manifest:$PSScriptRoot\src\app.manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "$PSScriptRoot\src\RepairCore.cs" "$PSScriptRoot\src\App.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Get-Item -LiteralPath $output | Select-Object FullName,Length
Get-FileHash -LiteralPath $output -Algorithm SHA256
