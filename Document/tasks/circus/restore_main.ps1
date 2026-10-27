$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$runtime = Join-Path $workspace 'artifacts/circus-probe'
$snapshot = (Get-Content -LiteralPath (Join-Path $runtime 'main-snapshot-path.txt') -Raw).Trim()
$original = Get-Content -LiteralPath (Join-Path $snapshot 'restore.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $original.exe -Algorithm SHA256).Hash -ne $original.exe_sha256) { throw 'Main binary changed since snapshot; inspect before restore.' }
foreach ($name in 'host','proxy') {
    $ownedId = [int](Get-Content -LiteralPath (Join-Path $runtime ($name+'.pid')))
    $entry = Get-CimInstance Win32_Process -Filter "ProcessId=$ownedId"
    if ($entry -and -not $entry.CommandLine.Contains($runtime)) { throw 'Task PID identity changed.' }
    if ($entry) { Stop-Process -Id $ownedId }
}
$tcp = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
if (@($tcp | Where-Object Port -in @(7001,10011,18080,18081)).Count) { throw 'Default ports remain occupied; no process was guessed or stopped.' }
$saved = @{}
try {
    foreach ($setting in $original.configuration_environment.PSObject.Properties) {
        $saved[$setting.Name] = [Environment]::GetEnvironmentVariable($setting.Name,'Process')
        [Environment]::SetEnvironmentVariable($setting.Name,$setting.Value,'Process')
    }
    $main = Start-Process -FilePath $original.exe -WorkingDirectory $original.working_directory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtime 'restored-main.stdout.log') -RedirectStandardError (Join-Path $runtime 'restored-main.stderr.log')
    $main.Id | Set-Content -LiteralPath (Join-Path $runtime 'restored-main.pid')
    Write-Output "Original Host restarted PID=$($main.Id); original player database was never replaced. Verify listeners and health."
} finally {
    foreach ($entry in $saved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process') }
}
