param([string]$WorkDir = (Join-Path $PSScriptRoot '.build\tests'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows x64 .NET Framework C# compiler was not found.' }
$work = [IO.Path]::GetFullPath($WorkDir)
if (Test-Path -LiteralPath $work) { throw 'Use a fresh test directory with -WorkDir.' }
New-Item -ItemType Directory -Path $work | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets') -Destination $work -Recurse
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$helpers = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$flags = @('/nologo','/utf8output','/platform:x64','/optimize+','/target:exe','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll')
foreach ($name in @('CheckFinishStoreV125','CheckObservationV126')) {
    $program = Join-Path $work ($name + '.exe')
    & $compiler @flags ('/main:' + $name) ('/out:' + $program) $sources $helpers
    if ($LASTEXITCODE -ne 0) { throw ('Test compile failed: ' + $name) }
    $fixture = Join-Path $work $name
    & $program $fixture
    if ($LASTEXITCODE -ne 0) { throw ('Test failed: ' + $name) }
}
Write-Output 'PASS: 49 finish archive checks + 59 observation checks.'
