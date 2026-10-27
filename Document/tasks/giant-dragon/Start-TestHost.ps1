param([Parameter(Mandatory)][string]$RunRoot,[Parameter(Mandatory)][string]$Database,[string]$Label='compatibility',[switch]$KeepRunning)
$ErrorActionPreference='Stop'
if($Label -notmatch '^[a-z-]+$'){throw 'Invalid label'}
$taskRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$RunRoot=[IO.Path]::GetFullPath($RunRoot)
if(-not $RunRoot.StartsWith((Join-Path $taskRepo 'artifacts/giant-dragon')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Outside task deployment'}
$taskPorts=@(23070,23071,23072,23073)
if(@(Get-NetTCPConnection -State Listen | Where-Object LocalPort -in $taskPorts).Count -or @(Get-NetUDPEndpoint | Where-Object LocalPort -in $taskPorts).Count){throw 'Candidate port occupied'}
$taskExe=Join-Path $RunRoot 'host/Dfo.Server.Host.exe'
$taskPvf=Join-Path $RunRoot 'Script-giant-dragon-active.pvf'
$taskArguments=@("`"--Database:Path=$Database`"","`"--Pvf:ArchivePath=$taskPvf`"",'--Channel:UseScriptCatalog=false','--Networking:Enabled=true','--Networking:UdpDiscoveryEnabled=false','--Networking:ChannelPort=23070','--Networking:GamePorts:0=23071','--LauncherAuth:Enabled=true','--LauncherAuth:ListenUrl=http://127.0.0.1:23072','--LauncherAuth:PublicChannel:Port=23070','--GmWeb:Enabled=true','--GmWeb:ListenUrl=http://127.0.0.1:23073','--GiantDragon:Enabled=true','--GiantDragon:RestoredVariantsEnabled=true','--GameLogin:ConnectionPort=23071','--GameLogin:EndpointPortA=23071','--GameLogin:EndpointPortB=23071')
$taskProcess=$null
$taskSuccess=$false
try {
    $taskProcess=Start-Process -FilePath $taskExe -ArgumentList $taskArguments -WorkingDirectory (Join-Path $RunRoot 'host') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $RunRoot "$Label-stdout.log") -RedirectStandardError (Join-Path $RunRoot "$Label-stderr.log")
    $taskReady=$null
    for($taskAttempt=0;$taskAttempt -lt 60;$taskAttempt++){
        $taskProcess.Refresh();if($taskProcess.HasExited){throw "Candidate exited $($taskProcess.ExitCode)"}
        try {$taskReady=Invoke-RestMethod 'http://127.0.0.1:23072/api/launcher/v1/bootstrap' -TimeoutSec 1;if($taskReady.status -eq 'ready'){break}}catch{}
        Start-Sleep -Milliseconds 500
    }
    if($taskReady.status -ne 'ready'){throw 'Candidate readiness failed'}
    $taskTcp=@(Get-NetTCPConnection -State Listen | Where-Object OwningProcess -eq $taskProcess.Id)
    $taskUdp=@(Get-NetUDPEndpoint | Where-Object OwningProcess -eq $taskProcess.Id)
    if(@($taskTcp.LocalPort | Sort-Object -Unique).Count -ne 4 -or @($taskTcp | Where-Object LocalPort -notin $taskPorts).Count -or $taskUdp.Count){throw 'Candidate actual listeners differ'}
    $taskIdentity=Get-CimInstance Win32_Process -Filter "ProcessId=$($taskProcess.Id)"
    if($taskIdentity.ExecutablePath -ne $taskExe){throw 'Candidate executable identity differs'}
    $taskGmPage=Invoke-WebRequest 'http://127.0.0.1:23073/gm' -TimeoutSec 5
    $taskGm=$taskGmPage.StatusCode
    if($taskGm -ne 200){throw 'GM static page unavailable'}
    $taskAssets=@([regex]::Matches($taskGmPage.Content,'(?:src|href)="(/gm/assets/[^\"]+)"') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
    if(-not $taskAssets.Count){throw 'GM asset references missing'}
    foreach($taskAsset in $taskAssets){if((Invoke-WebRequest ('http://127.0.0.1:23073'+$taskAsset) -TimeoutSec 5).StatusCode -ne 200){throw 'GM asset unavailable'}}
    $taskResult=[ordered]@{Pid=$taskProcess.Id;Executable=$taskIdentity.ExecutablePath;Arguments=$taskArguments;Database=$Database;Pvf=$taskPvf;PvfSha256=(Get-FileHash -LiteralPath $taskPvf -Algorithm SHA256).Hash;Version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $RunRoot 'host/Dfo.Server.Host.dll')).ProductVersion;Bootstrap=$taskReady;GmStatus=$taskGm;Tcp=$taskTcp | Select-Object LocalAddress,LocalPort,OwningProcess;Udp=$taskUdp;At=(Get-Date).ToString('o');KeptRunning=[bool]$KeepRunning}
    $taskResult | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $RunRoot "$Label-process.json") -Encoding utf8
    $taskResult | ConvertTo-Json -Depth 8
    $taskSuccess=$true
}finally{
    if($null -ne $taskProcess -and (-not $KeepRunning -or -not $taskSuccess)){
        $taskProcess.Refresh();if(-not $taskProcess.HasExited){Stop-Process -Id $taskProcess.Id;$taskProcess.WaitForExit()}
    }
}
