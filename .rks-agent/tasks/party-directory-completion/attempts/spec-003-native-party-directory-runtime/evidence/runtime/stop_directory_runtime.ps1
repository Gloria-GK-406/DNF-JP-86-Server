[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $RuntimeRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$RuntimeRoot = (Resolve-Path -LiteralPath $RuntimeRoot).Path
. (Join-Path $PSScriptRoot 'runtime_receipt_identity.ps1')

$results = foreach ($name in 'probe', 'proxy', 'host') {
    $receiptPath = Join-Path $RuntimeRoot "$name.receipt.json"
    if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
        [ordered]@{ name = $name; status = 'receipt_missing' }
        continue
    }

    $receipt = Get-Content -Raw -LiteralPath $receiptPath | ConvertFrom-Json
    $process = Get-Process -Id $receipt.process_id -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        [ordered]@{ name = $name; process_id = $receipt.process_id; status = 'already_exited' }
        continue
    }

    $actualPath = [IO.Path]::GetFullPath($process.Path)
    $expectedPath = [IO.Path]::GetFullPath([string]$receipt.executable)
    $actualStart = $process.StartTime.ToUniversalTime()
    $expectedStart = Convert-ReceiptUtcDateTime $receipt.started_utc
    if (
        -not $actualPath.Equals($expectedPath, [StringComparison]::OrdinalIgnoreCase) -or
        [Math]::Abs(($actualStart - $expectedStart).TotalSeconds) -gt 1
    ) {
        [ordered]@{
            name = $name
            process_id = $receipt.process_id
            status = 'identity_mismatch_not_stopped'
        }
        continue
    }

    Stop-Process -Id $process.Id -Force
    $process.WaitForExit(10000) | Out-Null
    [ordered]@{ name = $name; process_id = $receipt.process_id; status = 'stopped' }
}

$payload = [ordered]@{
    stopped_utc = [DateTime]::UtcNow.ToString('o')
    results = @($results)
}
$payload | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $RuntimeRoot 'stop-result.json') -Encoding utf8
$payload | ConvertTo-Json -Depth 4
