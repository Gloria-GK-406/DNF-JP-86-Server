$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$runtime = Join-Path $workspace 'artifacts/circus-probe'
$manifest = Get-Content -LiteralPath (Join-Path $runtime 'manifest.json') -Raw | ConvertFrom-Json
$ports = @($manifest.ports.channel, $manifest.ports.backend, $manifest.ports.proxy, $manifest.ports.http, $manifest.ports.control)
$tcp = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
$udp = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveUdpListeners()
$busy = @($tcp + $udp | Where-Object Port -in $ports)
if ($busy.Count) { throw "Probe ports are already occupied: $($busy.Port -join ','). Inspect PID/EXE; do not stop their owner." }
if (@($ports | Where-Object { $_ -lt 22000 -or $_ -gt 22999 }).Count) { throw 'Port range violation' }
$hostDll = Join-Path $PSScriptRoot 'ProbeHost/bin/Debug/net10.0/ProbeHost.dll'
$probeConfig = Join-Path $runtime 'probe.json'
$hostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @('"'+$hostDll+'"', '"'+$probeConfig+'"') -WorkingDirectory $runtime -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtime 'host.stdout.log') -RedirectStandardError (Join-Path $runtime 'host.stderr.log')
$hostProcess.Id | Set-Content -LiteralPath (Join-Path $runtime 'host.pid')
$proxy = Join-Path $PSScriptRoot 'probe_proxy.py'
$proxyProcess = Start-Process -FilePath 'python' -ArgumentList @('"'+$proxy+'"', '--root', '"'+$runtime+'"') -WorkingDirectory $runtime -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtime 'proxy.stdout.log') -RedirectStandardError (Join-Path $runtime 'proxy.stderr.log')
$proxyProcess.Id | Set-Content -LiteralPath (Join-Path $runtime 'proxy.pid')
Write-Output "Probe Host PID=$($hostProcess.Id); proxy PID=$($proxyProcess.Id); inspect health and actual listeners before testing."
