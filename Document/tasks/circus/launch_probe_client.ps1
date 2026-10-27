param([switch]$B1)
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$runtime = Join-Path $workspace 'artifacts/circus-probe'
$client = Join-Path $runtime 'client'
$credentials = Get-Content -LiteralPath (Join-Path $runtime 'account.json') -Raw
$login = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:22080/api/launcher/v1/login' -ContentType 'application/json' -Body $credentials
if ($login.channel.host -ne '127.0.0.1' -or $login.channel.port -ne 22000) { throw 'Unexpected backend endpoint' }
$slots = @('9','127.0.0.1','22000',$login.mId,$login.ticket,'1','0','6','0','2','1','','','0','0','0','9n2b1c8r3w7y','0','0','0','0','0','2','0','0','0','0','0','0','0','0','0','0','0','4D87')
$previous = $env:CLIENTHACK_B1_MODE
try {
    $env:CLIENTHACK_B1_MODE = if ($B1) { '1' } else { $null }
    $exe = if ($B1) { 'DNF.unpacked-b1-debug.exe' } else { 'DNF.exe' }
    $process = Start-Process -FilePath (Join-Path $client $exe) -ArgumentList ($slots -join '?') -WorkingDirectory $client -PassThru
    $process.Id | Set-Content -LiteralPath (Join-Path $runtime 'client.pid')
    Write-Output "Private probe client PID=$($process.Id). Fresh ticket was not logged."
} finally { $env:CLIENTHACK_B1_MODE = $previous }
