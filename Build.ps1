param([string]$OutDir = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows x64 .NET Framework C# compiler was not found.' }
$output = [IO.Path]::GetFullPath($OutDir)
if ($output -eq [IO.Path]::GetFullPath($PSScriptRoot)) { throw 'Choose a separate output directory.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
if ($sources.Count -eq 0) { throw 'No C# source files found.' }
$binary = Join-Path $output '手动回放快速入口v1.2.6.exe'
$flags = @('/nologo','/utf8output','/platform:x64','/optimize+','/target:winexe','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll')
& $compiler @flags ('/win32icon:' + (Join-Path $PSScriptRoot 'assets\manual-replay.ico')) ('/out:' + $binary) $sources
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets') -Destination $output -Recurse -Force
foreach ($name in @('README.md','LICENSE','NOTICE','THIRD_PARTY_NOTICES.md','CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $output -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination $output -Recurse -Force
Write-Output ('Built: ' + $binary)
