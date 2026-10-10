$ErrorActionPreference = 'Stop'
$taskArtifact = 'E:/Project/DNF/DNF-JP-86-Server/artifacts/usage-quota-direct-events-20261010'
$taskRuntime = Join-Path $taskArtifact 'runtime'
$taskOldRuntime = 'E:/Project/DNF/DNF-JP-86-Server/artifacts/mirror-arad-entry-20261010/runtime'
$taskOldExe = Join-Path $taskOldRuntime 'Dfo.Server.Host.exe'
$taskNewExe = Join-Path $taskRuntime 'Dfo.Server.Host.exe'
$taskVersion = '0.2.3+quota-direct.20261010.r1'
$taskSource = 'E:/Project/DNF/DNF-JP-86-Server/.worktrees/monster-declaration-parser'
$taskCommit = (git -C $taskSource rev-parse HEAD).Trim()
if ($taskCommit -ne 'a0ef0f150dc8bf9d567177a007cd1766033ad022') {
    throw 'Source baseline changed; deployment aborted.'
}
# Publication occurs before the one accepted-result commit. The exact compiled Server tree
# is pinned by a manifest, including new tracked-candidate files; docs can record this rollout.
$taskManifestPath = Join-Path $taskArtifact 'source-manifest.json'
$taskManifest = Get-Content -LiteralPath $taskManifestPath -Raw | ConvertFrom-Json
$taskFiles = @(git -C $taskSource ls-files --cached --others --exclude-standard -- Server | Sort-Object -Unique)
$taskManifestPaths = @($taskManifest.Files | ForEach-Object Path | Sort-Object -Unique)
if (@(Compare-Object $taskFiles $taskManifestPaths).Count -ne 0) { throw 'Server source file set changed since publish.' }
foreach ($taskFile in $taskManifest.Files) {
    $taskResolved = [IO.Path]::GetFullPath((Join-Path $taskSource $taskFile.Path))
    if (-not $taskResolved.StartsWith([IO.Path]::GetFullPath($taskSource) + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escaped worktree.' }
    if ((Get-FileHash -LiteralPath $taskResolved -Algorithm SHA256).Hash -ne $taskFile.SHA256) {
        throw ('Server source changed since publish: ' + $taskFile.Path)
    }
}
$taskOld = @(Get-CimInstance Win32_Process -Filter "Name='Dfo.Server.Host.exe'")
if ($taskOld.Count -ne 1 -or $taskOld[0].ProcessId -ne 49556 -or
    [IO.Path]::GetFullPath($taskOld[0].ExecutablePath) -ne [IO.Path]::GetFullPath($taskOldExe)) {
    throw 'Current server identity changed; no process was stopped.'
}
$taskAssemblies = @(Get-ChildItem -LiteralPath $taskRuntime -Filter 'Dfo.Server.*.dll' | ForEach-Object {
    $taskAssemblyVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($_.FullName).ProductVersion
    if ($taskAssemblyVersion -ne $taskVersion) { throw ('Unexpected assembly identity: ' + $_.Name) }
    [pscustomobject]@{Name=$_.Name; SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; ProductVersion=$taskAssemblyVersion}
})
Copy-Item -LiteralPath (Join-Path $taskRuntime 'appsettings.json') -Destination (Join-Path $taskArtifact 'appsettings.published.json')
Copy-Item -LiteralPath (Join-Path $taskOldRuntime 'appsettings.json') -Destination (Join-Path $taskArtifact 'appsettings.previous.json')
Copy-Item -LiteralPath (Join-Path $taskOldRuntime 'appsettings.json') -Destination (Join-Path $taskRuntime 'appsettings.json') -Force
$taskConfiguration = Get-Content -LiteralPath (Join-Path $taskRuntime 'appsettings.json') -Raw | ConvertFrom-Json
$taskDatabase = [IO.Path]::GetFullPath($taskConfiguration.Database.Path)
if ($taskDatabase -ne [IO.Path]::GetFullPath('E:/Project/DNF/DNF-JP-86-Server/Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db')) {
    throw 'Unexpected database authority.'
}
if (-not (Test-Path -LiteralPath $taskDatabase) -or -not (Test-Path -LiteralPath $taskConfiguration.Pvf.ArchivePath)) {
    throw 'Database or PVF unavailable.'
}
$taskExpectedPorts = @(7001,10010,10011,10100,18080,18081)
if (-not (Test-Path -LiteralPath (Join-Path $taskRuntime 'wwwroot/gm/index.html'))) {
    throw 'GM static page missing; no process was stopped.'
}
$taskOldListeners = @(Get-NetTCPConnection -State Listen | Where-Object LocalPort -in $taskExpectedPorts)
if ($taskOldListeners.Count -ne 6 -or @($taskOldListeners | Where-Object OwningProcess -ne $taskOld[0].ProcessId).Count -ne 0) {
    throw 'Listener ownership changed; no process was stopped.'
}
$taskBackup = Join-Path $taskArtifact ('database-before-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $taskBackup | Out-Null
$taskOldProcess = Get-Process -Id $taskOld[0].ProcessId
Stop-Process -Id $taskOld[0].ProcessId -Force
if (-not $taskOldProcess.WaitForExit(10000)) { throw 'Old server did not exit; startup aborted.' }
$taskStarted = $null
try {
    foreach ($taskSuffix in @('', '-wal', '-shm')) {
        $taskSourceFile = $taskDatabase + $taskSuffix
        if (Test-Path -LiteralPath $taskSourceFile) { Copy-Item -LiteralPath $taskSourceFile -Destination $taskBackup }
    }
    $taskIntegrityOutput = & python -c 'import sqlite3,sys,json,pathlib; c=sqlite3.connect(pathlib.Path(sys.argv[1]).as_uri()+"?mode=ro",uri=True); checks=[r[0] for r in c.execute("pragma quick_check")]; print(json.dumps({"quickCheck":checks})); c.close(); sys.exit(0 if checks==["ok"] else 1)' (Join-Path $taskBackup 'dfo-server.db')
    if ($LASTEXITCODE -ne 0) { throw 'Database backup integrity check failed.' }
    $taskIntegrity = $taskIntegrityOutput | ConvertFrom-Json
    $taskAudit = Join-Path $taskSource 'Document/tasks/usage-quota-direct-events-20261010/audit-quota.py'
    $taskBeforeAuditPath = Join-Path $taskArtifact 'quota-before-start.json'
    $taskAuditOutput = & python -X utf8 $taskAudit (Join-Path $taskBackup 'dfo-server.db') $taskBeforeAuditPath
    if ($LASTEXITCODE -ne 0) { throw 'Quota backup audit failed.' }
    $taskStarted = Start-Process -FilePath $taskNewExe -WorkingDirectory $taskRuntime -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $taskArtifact 'stdout.log') -RedirectStandardError (Join-Path $taskArtifact 'stderr.log')
    $taskReady = $false
    $taskLastFailure = ''
    for ($taskAttempt = 0; $taskAttempt -lt 25; $taskAttempt++) {
        if ($taskStarted.HasExited) { throw 'New server exited during readiness check.' }
        try {
            $taskHealth = Invoke-RestMethod 'http://127.0.0.1:18081/api/gm/v1/health' -TimeoutSec 2
            $taskBootstrap = Invoke-RestMethod 'http://127.0.0.1:18080/api/launcher/v1/bootstrap' -TimeoutSec 2
            if ($taskHealth.status -eq 'ready' -and $taskBootstrap.status -eq 'ready') { $taskReady = $true; break }
        } catch { $taskLastFailure = $_.Exception.Message }
        Start-Sleep -Milliseconds 500
    }
    if (-not $taskReady) { throw ('New server did not become ready: ' + $taskLastFailure) }
    $taskCurrent = Get-CimInstance Win32_Process -Filter "ProcessId=$($taskStarted.Id)"
    if ([IO.Path]::GetFullPath($taskCurrent.ExecutablePath) -ne [IO.Path]::GetFullPath($taskNewExe)) { throw 'New executable identity mismatch.' }
    $taskListeners = @(Get-NetTCPConnection -State Listen | Where-Object LocalPort -in $taskExpectedPorts | Select-Object LocalPort,OwningProcess)
    if ($taskListeners.Count -ne 6 -or @($taskListeners | Where-Object OwningProcess -ne $taskStarted.Id).Count -ne 0) { throw 'New server does not own all required listeners.' }
    $taskMailStatus = (Invoke-WebRequest 'http://127.0.0.1:18081/gm/mail' -TimeoutSec 3).StatusCode
    if ($taskMailStatus -ne 200) { throw 'GM page failed readiness check.' }
    $taskAfterAuditPath = Join-Path $taskArtifact 'quota-after-start.json'
    $taskAuditOutput = & python -X utf8 $taskAudit $taskDatabase $taskAfterAuditPath
    if ($LASTEXITCODE -ne 0) { throw 'Quota runtime audit failed.' }
    $taskBeforeAudit = Get-Content -LiteralPath $taskBeforeAuditPath -Raw | ConvertFrom-Json
    $taskAfterAudit = Get-Content -LiteralPath $taskAfterAuditPath -Raw | ConvertFrom-Json
    $taskQuotaUnchanged = ($taskBeforeAudit.mirror | ConvertTo-Json -Depth 8 -Compress) -ceq ($taskAfterAudit.mirror | ConvertTo-Json -Depth 8 -Compress)
    $taskCountersUnchanged = ($taskBeforeAudit.counters | ConvertTo-Json -Depth 8 -Compress) -ceq ($taskAfterAudit.counters | ConvertTo-Json -Depth 8 -Compress)
    $taskBackupFiles = @(Get-ChildItem -LiteralPath $taskBackup -File | ForEach-Object {
        [pscustomobject]@{Name=$_.Name;Length=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
    $taskReceipt = [ordered]@{
        SourceBaseline=$taskCommit; SourceManifest=$taskManifestPath; SourceManifestSHA256=(Get-FileHash -LiteralPath $taskManifestPath -Algorithm SHA256).Hash; Branch='task/monster-declaration-parser'; InformationalVersion=$taskVersion
        ProcessId=$taskStarted.Id; Executable=[IO.Path]::GetFullPath($taskNewExe)
        ExecutableSHA256=(Get-FileHash -LiteralPath $taskNewExe -Algorithm SHA256).Hash
        PvfArchive=$taskConfiguration.Pvf.ArchivePath
        OldProcessId=$taskOld[0].ProcessId; OldExecutable=[IO.Path]::GetFullPath($taskOldExe)
        Database=$taskDatabase; DatabaseBackup=[IO.Path]::GetFullPath($taskBackup)
        StartedAt=[DateTimeOffset]::Now.ToString('o')
        ClientAcceptance='Pending: authoritative mirror remaining entries, failure/duplicate consumption, consumable and premium counters; Document/tasks/usage-quota-direct-events-20261010/client-test-set.md.'
        Verification=@{Health=$taskHealth; Bootstrap=$taskBootstrap; GmMailStatus=$taskMailStatus; Listeners=$taskListeners; Assemblies=$taskAssemblies; BackupIntegrity=$taskIntegrity; BackupFiles=$taskBackupFiles; QuotaRowsUnchanged=$taskQuotaUnchanged; CounterRowsUnchanged=$taskCountersUnchanged; BeforeQuotaAudit=$taskBeforeAuditPath; AfterQuotaAudit=$taskAfterAuditPath}
        TestEvidenceDirectory=(Join-Path $taskSource 'Document/tasks/usage-quota-direct-events-20261010/verification')
    }
    $taskReceipt | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $taskArtifact 'deployment.json') -Encoding utf8
    [pscustomobject]@{ProcessId=$taskStarted.Id;Version=$taskVersion;Health=$taskHealth.status;Bootstrap=$taskBootstrap.status;GmMailStatus=$taskMailStatus;DatabaseBackup=$taskBackup;BackupQuickCheck=$taskIntegrity.quickCheck} | ConvertTo-Json -Compress
}
catch {
    if ($null -ne $taskStarted -and -not $taskStarted.HasExited) { Stop-Process -Id $taskStarted.Id -Force; $taskStarted.WaitForExit(10000) | Out-Null }
    Start-Process -FilePath $taskOldExe -WorkingDirectory $taskOldRuntime -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $taskArtifact 'rollback-stdout.log') -RedirectStandardError (Join-Path $taskArtifact 'rollback-stderr.log') | Out-Null
    throw
}
