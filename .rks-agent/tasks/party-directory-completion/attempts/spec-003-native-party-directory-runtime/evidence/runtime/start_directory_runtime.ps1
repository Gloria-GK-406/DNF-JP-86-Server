[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $RuntimeRoot,

    [switch] $Hold
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExpectedDatabaseHash = 'E06DFFBA863FB5FFFF89DB62D73584F7F4DB3A34F6C22387E39C07A5CF2F5BA7'
$ExpectedClientHash = '1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD'
$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..\..\..')).Path
$HostRoot = Join-Path $RepositoryRoot 'Server\src\Dfo.Server.Host\bin\Release\net10.0'
$HostExecutable = Join-Path $HostRoot 'Dfo.Server.Host.exe'
$DatabaseSource = 'E:\Project\DNF\DNF-JP-86-Server\Server\src\Dfo.Server.Host\bin\Release\net10.0\Data\dfo-server.db'
$PvfArchive = 'E:\Project\DNF\DNFClient-A21-protocol\Script.pvf'
$LeaderExecutable = 'E:\Project\DNF\DNFClient-A21-protocol\DNF.exe'
$ObserverExecutable = 'E:\Project\DNF\DNFClient-A21-protocol2\DNF.exe'
$Python = (Get-Command python -ErrorAction Stop).Source
$ProxyScript = Join-Path $PSScriptRoot 'directory_capture_proxy.py'
$ProbeScript = Join-Path $PSScriptRoot 'directory_state_probe.py'

function Assert-FileHash {
    param([string] $Path, [string] $Expected)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file is missing: $Path"
    }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actual -ne $Expected) {
        throw "Identity mismatch for $Path (expected $Expected, received $actual)"
    }
}

function Test-TcpPort {
    param([int] $Port)
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $task = $client.ConnectAsync('127.0.0.1', $Port)
        if (-not $task.Wait(300)) { return $false }
        return $client.Connected
    }
    catch { return $false }
    finally { $client.Dispose() }
}

function Wait-Condition {
    param(
        [scriptblock] $Condition,
        [string] $Description,
        [int] $TimeoutSeconds = 90
    )
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for $Description"
}

function Write-Receipt {
    param([string] $Name, [System.Diagnostics.Process] $Process, [string] $Executable)
    $receipt = [ordered]@{
        name = $Name
        process_id = $Process.Id
        executable = [IO.Path]::GetFullPath($Executable)
        started_utc = $Process.StartTime.ToUniversalTime().ToString('o')
    }
    $receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $RuntimeRoot "$Name.receipt.json") -Encoding utf8
    return $receipt
}

function Stop-StartedProcess {
    param([System.Diagnostics.Process] $Process)
    if ($null -eq $Process) { return }
    try {
        if (-not $Process.HasExited) {
            Stop-Process -Id $Process.Id -Force -ErrorAction Stop
            $Process.WaitForExit(10000) | Out-Null
        }
    }
    catch { }
}

if (Test-Path -LiteralPath $RuntimeRoot) {
    if (Get-ChildItem -LiteralPath $RuntimeRoot -Force | Select-Object -First 1) {
        throw "Runtime root must be new or empty: $RuntimeRoot"
    }
}
else {
    New-Item -ItemType Directory -Path $RuntimeRoot | Out-Null
}
$RuntimeRoot = (Resolve-Path -LiteralPath $RuntimeRoot).Path

Assert-FileHash -Path $DatabaseSource -Expected $ExpectedDatabaseHash
Assert-FileHash -Path $LeaderExecutable -Expected $ExpectedClientHash
Assert-FileHash -Path $ObserverExecutable -Expected $ExpectedClientHash

$foreignHost = Get-Process -Name 'Dfo.Server.Host' -ErrorAction SilentlyContinue
if ($foreignHost) {
    throw "A pre-existing Dfo.Server.Host process is present; no process was stopped."
}

$wal = "$DatabaseSource-wal"
if ((Test-Path -LiteralPath $wal) -and (Get-Item -LiteralPath $wal).Length -ne 0) {
    throw "Source database has a non-empty WAL; refusing an unstable copy."
}

$ports = 7001, 10010, 10011, 17001, 18080, 20010, 20011
foreach ($port in $ports) {
    if (Test-TcpPort -Port $port) {
        throw "TCP port $port is already accepting connections; no owner was stopped."
    }
}

$DatabaseCopy = Join-Path $RuntimeRoot 'dfo-server.runtime.db'
Copy-Item -LiteralPath $DatabaseSource -Destination $DatabaseCopy
Assert-FileHash -Path $DatabaseCopy -Expected $ExpectedDatabaseHash
$DatabaseSourceHash = (Get-FileHash -LiteralPath $DatabaseSource -Algorithm SHA256).Hash
$DatabaseCopyHash = (Get-FileHash -LiteralPath $DatabaseCopy -Algorithm SHA256).Hash
$PvfHash = (Get-FileHash -LiteralPath $PvfArchive -Algorithm SHA256).Hash
$HostHash = (Get-FileHash -LiteralPath $HostExecutable -Algorithm SHA256).Hash

$hostOut = Join-Path $RuntimeRoot 'host.stdout.log'
$hostErr = Join-Path $RuntimeRoot 'host.stderr.log'
$proxyOut = Join-Path $RuntimeRoot 'proxy.stdout.log'
$proxyErr = Join-Path $RuntimeRoot 'proxy.stderr.log'
$probeOut = Join-Path $RuntimeRoot 'probe.stdout.log'
$probeErr = Join-Path $RuntimeRoot 'probe.stderr.log'
$frameLog = Join-Path $RuntimeRoot 'directory-targeted-frames.jsonl'
$probeLog = Join-Path $RuntimeRoot 'directory-state.jsonl'
$proxyReady = Join-Path $RuntimeRoot 'proxy.ready.json'
$probeReady = Join-Path $RuntimeRoot 'probe.ready.json'

$hostProcess = $null
$proxyProcess = $null
$probeProcess = $null
try {
    $hostEnvironment = @{
        'Networking__Enabled' = 'true'
        'Networking__ChannelPort' = '17001'
        'Networking__GamePorts__0' = '20010'
        'Networking__GamePorts__1' = '20011'
        'Channel__PublicServerIp' = '127.0.0.1'
        'Channel__Channels__0__Id' = '10'
        'Channel__Channels__0__Name' = '#ch.10'
        'Channel__Channels__0__Host' = '127.0.0.1'
        'Channel__Channels__0__Port' = '10010'
        'Channel__Channels__1__Id' = '11'
        'Channel__Channels__1__Name' = '#ch.11'
        'Channel__Channels__1__Host' = '127.0.0.1'
        'Channel__Channels__1__Port' = '10011'
        'Database__Path' = $DatabaseCopy
        'Pvf__ArchivePath' = $PvfArchive
        'Game__EndpointHost' = '127.0.0.1'
        'LauncherAuth__Enabled' = 'true'
        'LauncherAuth__ListenUrl' = 'http://127.0.0.1:18080'
        'LauncherAuth__PublicChannel__Host' = '127.0.0.1'
        'LauncherAuth__PublicChannel__Port' = '7001'
    }
    $hostProcess = Start-Process -FilePath $HostExecutable -WorkingDirectory $HostRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $hostOut -RedirectStandardError $hostErr -Environment $hostEnvironment
    $hostReceipt = Write-Receipt -Name 'host' -Process $hostProcess -Executable $HostExecutable

    Wait-Condition -Description 'isolated Host listeners' -Condition {
        -not $hostProcess.HasExited -and
        (Test-TcpPort 17001) -and (Test-TcpPort 20010) -and (Test-TcpPort 20011)
    }
    Wait-Condition -Description 'launcher bootstrap readiness' -Condition {
        if ($hostProcess.HasExited) { return $false }
        try {
            $response = Invoke-RestMethod -Uri 'http://127.0.0.1:18080/api/launcher/v1/bootstrap' -TimeoutSec 2
            return $response.status -eq 'ready'
        }
        catch { return $false }
    }

    $proxyArguments = @('-u', $ProxyScript, '--log', $frameLog, '--ready', $proxyReady)
    $proxyProcess = Start-Process -FilePath $Python -ArgumentList $proxyArguments -WorkingDirectory $PSScriptRoot `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $proxyOut -RedirectStandardError $proxyErr
    $proxyReceipt = Write-Receipt -Name 'proxy' -Process $proxyProcess -Executable $Python
    Wait-Condition -Description 'proxy readiness receipt and public listeners' -Condition {
        -not $proxyProcess.HasExited -and (Test-Path -LiteralPath $proxyReady) -and
        (Test-TcpPort 7001) -and (Test-TcpPort 10010) -and (Test-TcpPort 10011)
    }

    $probeArguments = @(
        '-u', $ProbeScript,
        '--exe', $ObserverExecutable,
        '--log', $probeLog,
        '--ready', $probeReady,
        '--interval', '2.0'
    )
    $probeProcess = Start-Process -FilePath $Python -ArgumentList $probeArguments -WorkingDirectory $PSScriptRoot `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $probeOut -RedirectStandardError $probeErr
    $probeReceipt = Write-Receipt -Name 'probe' -Process $probeProcess -Executable $Python
    Wait-Condition -Description 'read-only observer probe readiness receipt' -Condition {
        -not $probeProcess.HasExited -and (Test-Path -LiteralPath $probeReady)
    }

    $manifest = [ordered]@{
        started_utc = [DateTime]::UtcNow.ToString('o')
        runtime_root = $RuntimeRoot
        repository_head = (git -C $RepositoryRoot rev-parse HEAD).Trim()
        database_source = $DatabaseSource
        database_source_sha256 = $DatabaseSourceHash
        database_copy = $DatabaseCopy
        database_copy_sha256 = $DatabaseCopyHash
        pvf_archive = $PvfArchive
        pvf_sha256 = $PvfHash
        leader_executable = $LeaderExecutable
        observer_executable = $ObserverExecutable
        dnf_sha256 = $ExpectedClientHash
        host_sha256 = $HostHash
        process_receipts = @($hostReceipt, $proxyReceipt, $probeReceipt)
        backend_ports = @(17001, 20010, 20011)
        public_proxy_ports = @(7001, 10010, 10011)
        launcher_api = 'http://127.0.0.1:18080/'
        channel_traffic_persisted = $false
        observer_process_access_mask = '0x0410'
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $RuntimeRoot 'runtime-manifest.json') -Encoding utf8
    Write-Output "RUNTIME_READY=$RuntimeRoot"
    if ($Hold) {
        $heartbeatPath = Join-Path $RuntimeRoot 'runtime-heartbeat.json'
        while ($true) {
            foreach ($owned in @($hostProcess, $proxyProcess, $probeProcess)) {
                if ($owned.HasExited) {
                    throw "Owned runtime process $($owned.Id) exited while the runtime was held."
                }
            }
            [ordered]@{
                heartbeat_utc = [DateTime]::UtcNow.ToString('o')
                owner_shell_pid = $PID
                process_ids = @($hostProcess.Id, $proxyProcess.Id, $probeProcess.Id)
            } | ConvertTo-Json | Set-Content -LiteralPath $heartbeatPath -Encoding utf8
            Start-Sleep -Seconds 2
        }
    }
}
catch {
    Stop-StartedProcess $probeProcess
    Stop-StartedProcess $proxyProcess
    Stop-StartedProcess $hostProcess
    throw
}
