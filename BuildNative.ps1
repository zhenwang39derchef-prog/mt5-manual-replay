param(
    [Parameter(Mandatory=$true)][string]$MetaEditor,
    [Parameter(Mandatory=$true)][string]$MT5DataFolder
)
$ErrorActionPreference = 'Stop'
$editor = [IO.Path]::GetFullPath($MetaEditor)
$mql = Join-Path ([IO.Path]::GetFullPath($MT5DataFolder)) 'MQL5'
if (-not (Test-Path -LiteralPath $editor -PathType Leaf)) { throw 'MetaEditor executable was not found.' }
if (-not (Test-Path -LiteralPath (Join-Path $mql 'Include\Trade\Trade.mqh'))) { throw 'MT5 standard MQL5 headers were not found.' }
$work = Join-Path $PSScriptRoot ('.build\native\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assets') -File |
    Where-Object { $_.Extension -in @('.mq5','.mqh') } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $work }
$names = @('CN_Replay_3EMA','CN_Replay_BIAS','CN_Manual_Replay_Chart_Preview')
foreach ($name in $names) {
    $source = Join-Path $work ($name + '.mq5')
    $log = Join-Path $work ($name + '.log')
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $editor
    $info.Arguments = '/compile:"' + $source + '" /inc:"' + $mql + '" /log:"' + $log + '"'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $process = [Diagnostics.Process]::Start($info)
    if (-not $process.WaitForExit(90000)) { throw ('Native compilation is still running: ' + $name) }
    $process.Dispose()
    if (-not (Test-Path -LiteralPath $log)) { throw ('Compile log missing: ' + $name) }
    $result = [IO.File]::ReadAllText($log)
    if (-not $result.Contains('0 errors, 0 warnings') -or -not (Test-Path -LiteralPath (Join-Path $work ($name + '.ex5')))) {
        throw ('Native compilation failed. See ' + $log)
    }
    Write-Output ($name + ': 0 errors, 0 warnings')
}
foreach ($name in $names) {
    Copy-Item -LiteralPath (Join-Path $work ($name + '.ex5')) -Destination (Join-Path $PSScriptRoot 'assets') -Force
}
Write-Output 'All native files built. Run Build.ps1 to create the desktop package.'
