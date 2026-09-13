$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'runtime_receipt_identity.ps1')

$receipt = '{"started_utc":"2026-09-13T10:20:23.8379973Z"}' | ConvertFrom-Json
$actual = Convert-ReceiptUtcDateTime $receipt.started_utc
$expected = [DateTime]::Parse(
    '2026-09-13T10:20:23.8379973Z',
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::RoundtripKind
).ToUniversalTime()

if ($actual.Kind -ne [DateTimeKind]::Utc) {
    throw "Expected UTC DateTime kind, received $($actual.Kind)"
}
if ($actual.Ticks -ne $expected.Ticks) {
    throw "Receipt timestamp drifted: expected $($expected.ToString('o')), actual $($actual.ToString('o'))"
}

[ordered]@{
    test = 'receipt UTC timestamp survives ConvertFrom-Json'
    expected = $expected.ToString('o')
    actual = $actual.ToString('o')
    status = 'passed'
} | ConvertTo-Json
