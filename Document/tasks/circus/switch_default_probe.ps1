$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$runtime = Join-Path $workspace 'artifacts/circus-probe'
$hostDll = Join-Path $runtime 'default-host/ProbeHost.dll'
if (-not (Test-Path -LiteralPath $hostDll)) { throw 'Build the private default-port Host before stopping the original service.' }
$snapshot = (Get-Content -LiteralPath (Join-Path $runtime 'main-snapshot-path.txt') -Raw).Trim()
$original = Get-Content -LiteralPath (Join-Path $snapshot 'restore.json') -Raw | ConvertFrom-Json
$mainProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$($original.pid)"
if ($mainProcess -and $mainProcess.ExecutablePath -ne $original.exe) { throw 'Original Host identity changed; inspect before switching.' }
if ((Get-FileHash -LiteralPath $original.exe -Algorithm SHA256).Hash -ne $original.exe_sha256) { throw 'Original Host binary changed.' }
$owned = @()
foreach ($name in 'host','proxy','client') {
    $pidFile = Join-Path $runtime ($name+'.pid')
    if (Test-Path -LiteralPath $pidFile) {
        $ownedId = [int](Get-Content -LiteralPath $pidFile)
        $entry = Get-CimInstance Win32_Process -Filter "ProcessId=$ownedId"
        if ($entry -and (($name -eq 'client' -and $entry.ExecutablePath.StartsWith($runtime+'\client\')) -or
            ($name -ne 'client' -and $entry.CommandLine.Contains($runtime)))) {
            $owned += $ownedId
        } elseif ($entry) { throw "Task PID $ownedId was reused by another process." }
    }
}
# Explicit user authorization: pause this exact main Host and use default ports.
# SQLite remains in place, with its WAL. A backup API snapshot is made again after stop.
if ($mainProcess) {
    Stop-Process -Id $original.pid
    Wait-Process -Id $original.pid -ErrorAction SilentlyContinue
}
foreach ($ownedId in $owned) { Stop-Process -Id $ownedId -ErrorAction SilentlyContinue }
$tcp = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
$ports = @(7001,10011,18080,22001,22082)
# This profile disables UDP discovery. An unrelated UDP endpoint cannot conflict
# with these TCP listeners (NVIDIA Overlay may happen to use UDP 10011).
if (@($tcp | Where-Object Port -in $ports).Count) { throw 'A target TCP port is still occupied; inspect its owner.' }
python (Join-Path $PSScriptRoot 'prepare_default_probe.py')
if ($LASTEXITCODE -ne 0) { throw 'Private database/config preparation failed; do not start a partial probe.' }
$config = Join-Path $runtime 'default-probe.json'
$hostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @('"'+$hostDll+'"','"'+$config+'"') -WorkingDirectory $runtime -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtime 'default-host.stdout.log') -RedirectStandardError (Join-Path $runtime 'default-host.stderr.log')
$hostProcess.Id | Set-Content -LiteralPath (Join-Path $runtime 'host.pid')
$proxy = Join-Path $PSScriptRoot 'probe_proxy.py'
$proxyProcess = Start-Process -FilePath 'python' -ArgumentList @('"'+$proxy+'"','--root','"'+$runtime+'"','--front','10011','--allow-default-front','--circus-schedule-on-select') -WorkingDirectory $runtime -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtime 'default-proxy.stdout.log') -RedirectStandardError (Join-Path $runtime 'default-proxy.stderr.log')
$proxyProcess.Id | Set-Content -LiteralPath (Join-Path $runtime 'proxy.pid')
Write-Output "Default-port probe Host PID=$($hostProcess.Id); proxy PID=$($proxyProcess.Id). Verify ready/listeners before user login."
