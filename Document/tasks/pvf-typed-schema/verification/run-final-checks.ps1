$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$runtimeArchive = 'E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
Set-Location -LiteralPath $taskRoot
$env:PVF_ARCHIVE_PATH = $runtimeArchive
$checks = [System.Collections.Generic.List[object]]::new()
function Invoke-Check([string]$Name, [string[]]$Arguments) {
    $log = Join-Path $PSScriptRoot "$Name.log"
    $started = [DateTimeOffset]::UtcNow
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath $log | Out-Null
    $result = $LASTEXITCODE
    $checks.Add([ordered]@{ name = $Name; command = "dotnet $($Arguments -join ' ')"; exitCode = $result; startedUtc = $started.ToString('o'); finishedUtc = [DateTimeOffset]::UtcNow.ToString('o') })
    $checks | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'final-checks.json') -Encoding utf8
    Write-Output "$Name exit=$result"
    if ($result -ne 0) {
        Get-Content -LiteralPath $log -Tail 30
        throw "$Name failed; see $log"
    }
}
Invoke-Check 'final-release-build' @('build', 'Server/86JP.Server.slnx', '-c', 'Release', '--no-restore')
foreach ($project in @('Dfo.Pvf', 'Dfo.Server.Infrastructure', 'Dfo.Server.Application', 'Dfo.Server.Persistence', 'Dfo.Server.Protocol', 'Dfo.Server.Networking', 'Dfo.Server.HttpApi', 'Dfo.Server.Host')) {
    Invoke-Check "final-$project" @('test', "Server/tests/$project.Tests/$project.Tests.csproj", '-c', 'Release', '--no-build', '--no-restore', '--logger', "trx;LogFileName=final-$project.trx", '--results-directory', $PSScriptRoot)
}
Invoke-Check 'final-probe-build' @('build', 'Tool/PvfProbe/PvfProbe.csproj', '-c', 'Release', '--no-restore')
