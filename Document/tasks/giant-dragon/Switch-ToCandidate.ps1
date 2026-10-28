param([Parameter(Mandatory)][string]$RunRoot)
$ErrorActionPreference='Stop'
$RunRoot=[IO.Path]::GetFullPath($RunRoot)
$taskRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if(-not $RunRoot.StartsWith((Join-Path $taskRepo 'artifacts/giant-dragon')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Outside deployment root'}
$taskInput=Get-Content -LiteralPath (Join-Path $RunRoot 'switch-inputs.json') -Raw | ConvertFrom-Json
$taskPrevious=Get-CimInstance Win32_Process -Filter "ProcessId=$($taskInput.PreviousPid)"
if($null -eq $taskPrevious -or $taskPrevious.ExecutablePath -ne $taskInput.PreviousExe -or $taskPrevious.CommandLine -notlike ('*--Database:Path='+$taskInput.PlayerDatabase+'*')){throw 'Active player Host changed; re-audit required'}
$taskOtherHosts=@(Get-CimInstance Win32_Process -Filter "Name='Dfo.Server.Host.exe'" | Where-Object ProcessId -ne $taskInput.PreviousPid)
if($taskOtherHosts.Count){throw 'Another Host is active; inspect its database before switching'}
if(@(Get-CimInstance Win32_Process -Filter "Name='DNF.exe'").Count){throw 'Close the game normally before switching'}
if(@(Get-NetTCPConnection -State Listen | Where-Object LocalPort -in @(23070,23071,23072,23073)).Count){throw 'Candidate port occupied'}
$taskPvf=Join-Path $taskInput.ClientDirectory 'Script.pvf'
$taskLauncher=Join-Path $taskInput.ClientDirectory 'Launcher.ini'
$taskExpectedSource='3822A9ED183CD17F74DFEE9C11B8F2E84AF596B23972A737846022148FB05C6B'
if((Get-FileHash -LiteralPath $taskPvf -Algorithm SHA256).Hash -ne $taskExpectedSource){throw 'Active PVF changed; re-audit required'}
$taskChangedClient=$false
$taskStopped=$false
try{
    Stop-Process -Id $taskPrevious.ProcessId
    Wait-Process -Id $taskPrevious.ProcessId -ErrorAction SilentlyContinue
    $taskStopped=$true
    & python (Join-Path $PSScriptRoot 'backup_player.py') $taskInput.PlayerDatabase (Join-Path $RunRoot 'player-at-switch.db') > (Join-Path $RunRoot 'final-backup.log') 2>&1
    if($LASTEXITCODE -ne 0){throw 'Final SQLite backup failed'}
    $taskConfigured=Get-Content -LiteralPath (Join-Path $RunRoot 'host/appsettings.json') -Raw | ConvertFrom-Json
    $taskConfigured.Database.Path=$taskInput.PlayerDatabase
    foreach($taskChannel in $taskConfigured.Channel.Channels){$taskChannel.Name="#ch.$($taskChannel.Id)"}
    $taskConfigured | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $RunRoot 'host/appsettings.json') -Encoding utf8
    $taskChangedClient=$true
    Copy-Item -LiteralPath (Join-Path $RunRoot 'Script-giant-dragon-active.pvf') -Destination $taskPvf -Force
    [IO.File]::WriteAllText($taskLauncher,"[Launcher]`r`nBaseUrl=http://127.0.0.1:23072/`r`n",[Text.Encoding]::ASCII)
    & (Join-Path $PSScriptRoot 'Start-TestHost.ps1') -RunRoot $RunRoot -Database $taskInput.PlayerDatabase -Label live -KeepRunning
    if(-not $?){throw 'Candidate startup check failed'}
    if((Get-FileHash -LiteralPath $taskPvf -Algorithm SHA256).Hash -ne '01F1BE685D46647961DF65207A36304777DB20EF5A3D03C68EFAA92F8B0C5189'){throw 'Installed client PVF differs'}
    [ordered]@{State='switched';PreviousPid=$taskPrevious.ProcessId;PreviousExe=$taskPrevious.ExecutablePath;Database=$taskInput.PlayerDatabase;Client=$taskInput.ClientDirectory;At=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $RunRoot 'switch-result.json') -Encoding utf8
}catch{
    $taskFailure=$_
    $taskCandidateRecord=Join-Path $RunRoot 'live-process.json'
    if(Test-Path -LiteralPath $taskCandidateRecord){
        $taskCandidate=Get-Content -LiteralPath $taskCandidateRecord -Raw | ConvertFrom-Json
        $taskRunning=Get-CimInstance Win32_Process -Filter "ProcessId=$($taskCandidate.Pid)"
        if($null -ne $taskRunning -and $taskRunning.ExecutablePath -eq $taskInput.CandidateExe){Stop-Process -Id $taskRunning.ProcessId;Wait-Process -Id $taskRunning.ProcessId -ErrorAction SilentlyContinue}
    }
    if($taskChangedClient){Copy-Item -LiteralPath (Join-Path $RunRoot 'rollback/Script.pvf') -Destination $taskPvf -Force;Copy-Item -LiteralPath (Join-Path $RunRoot 'rollback/Launcher.ini') -Destination $taskLauncher -Force}
    if($taskStopped){$taskRestored=Start-Process -FilePath $taskInput.PreviousExe -ArgumentList $taskInput.PreviousArguments -WorkingDirectory (Split-Path $taskInput.PreviousExe) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $RunRoot 'rollback-start-stdout.log') -RedirectStandardError (Join-Path $RunRoot 'rollback-start-stderr.log');[ordered]@{State='restored-after-failure';Pid=$taskRestored.Id;Error=$taskFailure.Exception.Message} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $RunRoot 'switch-result.json') -Encoding utf8}
    throw $taskFailure
}
