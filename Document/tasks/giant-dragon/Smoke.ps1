param([int]$ChannelPort=23070, [int]$GamePort=23071, [int]$HttpPort=23072,
    [ValidateSet('Debug','Release')][string]$Configuration='Debug')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$runRoot=Join-Path $repo ('artifacts/giant-dragon/smoke-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
if (-not $runRoot.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Outside task workspace' }
$ports=@($ChannelPort,$GamePort,$HttpPort)
if (($ports | Select-Object -Unique).Count -ne 3 -or ($ports | Where-Object { $_ -lt 23000 -or $_ -gt 23999 })) { throw 'Use distinct task ports 23000-23999' }
$beforeTcp=@(Get-NetTCPConnection -State Listen | Where-Object { $_.LocalPort -in $ports })
$beforeUdp=@(Get-NetUDPEndpoint | Where-Object { $_.LocalPort -in $ports })
if ($beforeTcp.Count -or $beforeUdp.Count) { throw 'A candidate port is occupied; no existing process will be stopped' }
New-Item -ItemType Directory -Path $runRoot | Out-Null
$source=Join-Path $repo "Server/src/Dfo.Server.Host/bin/$Configuration/net10.0"
Get-ChildItem -LiteralPath $source | Copy-Item -Destination $runRoot -Recurse
$pvf=Join-Path $repo 'artifacts/giant-dragon/resources/Script-giant-dragon-quests.pvf'
if (-not (Test-Path -LiteralPath $pvf)) { throw 'Generate the task PVF first' }
$database=Join-Path $runRoot 'Data/smoke.db'
$config=Get-Content -LiteralPath (Join-Path $runRoot 'appsettings.json') -Raw | ConvertFrom-Json
$config.Channel.UseScriptCatalog=$false
$config.Channel.Channels=@([pscustomobject]@{Id=71;Name='#giant-dragon';Host='127.0.0.1';Port=$GamePort})
$config.Networking.Enabled=$true
$config.Networking.UdpDiscoveryEnabled=$false
$config.Networking.ChannelPort=$ChannelPort
$config.Networking.GamePorts=@($GamePort)
$config.LauncherAuth.Enabled=$true
$config.LauncherAuth.ListenUrl="http://127.0.0.1:$HttpPort"
$config.LauncherAuth.PublicChannel.Port=$ChannelPort
$config.GmWeb.Enabled=$false
$config.Database.Path=$database
$config.GiantDragon.RestoredVariantsEnabled=$true
$config | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 (Join-Path $runRoot 'appsettings.json')
$exe=Join-Path $runRoot 'Dfo.Server.Host.exe'
# Explicit CLI values beat inherited environment overrides. The copied JSON replaces both
# arrays; actual owned listeners below additionally guard against unexpected extra endpoints.
$arguments=@("--Pvf:ArchivePath=$pvf", "--Database:Path=$database", '--Channel:UseScriptCatalog=false',
    '--Networking:Enabled=true', '--Networking:UdpDiscoveryEnabled=false', "--Networking:ChannelPort=$ChannelPort",
    '--LauncherAuth:Enabled=true', "--LauncherAuth:ListenUrl=http://127.0.0.1:$HttpPort", '--GmWeb:Enabled=false')
$process=$null
try {
    $process=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $runRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $runRoot 'stdout.log') -RedirectStandardError (Join-Path $runRoot 'stderr.log')
    $ready=$null
    for($attempt=0;$attempt -lt 60;$attempt++) {
        $process.Refresh()
        if($process.HasExited) { throw "Task Host exited: $($process.ExitCode)" }
        try { $ready=Invoke-RestMethod "http://127.0.0.1:$HttpPort/api/launcher/v1/bootstrap" -TimeoutSec 1; break } catch { Start-Sleep -Milliseconds 500 }
    }
    if($ready.status -ne 'ready') { throw 'Task Host did not become ready' }
    $ownedTcp=@(Get-NetTCPConnection -State Listen | Where-Object OwningProcess -eq $process.Id)
    $ownedUdp=@(Get-NetUDPEndpoint | Where-Object OwningProcess -eq $process.Id)
    if(@($ownedTcp.LocalPort | Sort-Object -Unique).Count -ne 3 -or @($ownedTcp | Where-Object { $_.LocalPort -notin $ports }).Count -or $ownedUdp.Count) { throw 'Unexpected owned listeners' }
    $identity=Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)"
    if($identity.ExecutablePath -ne $exe) { throw 'Host executable identity differs' }
    $hostAssembly=Join-Path $runRoot 'Dfo.Server.Host.dll'
    $result=[ordered]@{Pid=$process.Id;Executable=$identity.ExecutablePath;Database=$database;Pvf=$pvf;
        Configuration=$Configuration;Version=[Diagnostics.FileVersionInfo]::GetVersionInfo($hostAssembly).ProductVersion;
        HostAssemblySha256=(Get-FileHash -LiteralPath $hostAssembly -Algorithm SHA256).Hash;
        PvfSha256=(Get-FileHash -LiteralPath $pvf -Algorithm SHA256).Hash;Bootstrap=$ready;
        UseScriptCatalog=$false;ChannelId=71;GamePort=$GamePort;ChannelPort=$ChannelPort;HttpPort=$HttpPort;
        UdpDiscoveryEnabled=$false;UdpReason='Production UDP discovery is fixed to shared 17200/17201; disabled for isolated smoke';
        Tcp=$ownedTcp | Select-Object LocalAddress,LocalPort,OwningProcess;Udp=$ownedUdp;At=(Get-Date).ToString('o')}
    $result | ConvertTo-Json -Depth 10 | Set-Content -Encoding utf8 (Join-Path $runRoot 'result.json')
    Get-Content -LiteralPath (Join-Path $runRoot 'result.json')
} finally {
    if($null -ne $process) { $process.Refresh(); if(-not $process.HasExited) { Stop-Process -Id $process.Id; $process.WaitForExit() } }
}
