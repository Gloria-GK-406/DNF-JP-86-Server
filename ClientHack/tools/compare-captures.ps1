param(
    [Parameter(Mandatory=$true)][string]$First,
    [Parameter(Mandatory=$true)][string]$Second
)

$a = Get-Content -LiteralPath $First | ForEach-Object { $_ | ConvertFrom-Json }
$b = Get-Content -LiteralPath $Second | ForEach-Object { $_ | ConvertFrom-Json }
$errors = [System.Collections.Generic.List[string]]::new()

function Test-CaptureOrdering($records, $label, $errors) {
    $summaryIndex = -1
    $repairIndex = -1
    $preTargetIndices = @()
    $postTargetIndices = @()
    $iatIndices = @()
    for ($index = 0; $index -lt $records.Count; ++$index) {
        $record = $records[$index]
        if ($record.type -eq 'iat_repair_summary') { $summaryIndex = $index }
        if ($record.type -eq 'iat_repair') { $repairIndex = $index }
        if ($record.type -eq 'iat') { $iatIndices += $index }
        if ($record.type -eq 'target' -and $record.phase -eq 'pre_iat') {
            $preTargetIndices += $index
        }
        if ($record.type -eq 'target' -and $record.phase -eq 'post_iat_wait') {
            $postTargetIndices += $index
        }
    }
    if ($summaryIndex -lt 0 -or $repairIndex -le $summaryIndex) {
        $errors.Add("${label}: missing or misordered IAT mutation markers")
    }
    if ($preTargetIndices.Count -ne 16 -or $iatIndices.Count -ne 406 -or
        @($preTargetIndices | Where-Object { $_ -ge $summaryIndex }).Count -ne 0 -or
        @($iatIndices | Where-Object { $_ -ge $summaryIndex }).Count -ne 0) {
        $errors.Add("${label}: pre-mutation targets/IAT evidence is incomplete or appears after mutation")
    }
    if ($postTargetIndices.Count -ne 16 -or
        @($postTargetIndices | Where-Object { $_ -le $repairIndex }).Count -ne 0) {
        $errors.Add("${label}: post-repair target evidence is incomplete or misordered")
    }
    $nonzero = @($records | Where-Object {
        $_.type -eq 'iat' -and $_.current -ne '00000000'
    })
    if ($nonzero.Count -ne 0) {
        $errors.Add("${label}: capture-before-mutation gate observed nonzero IAT source state")
    }
}

Test-CaptureOrdering $a 'first capture' $errors
Test-CaptureOrdering $b 'second capture' $errors

$targetsA = @{}
$targetsB = @{}
$a | Where-Object type -eq 'target' | ForEach-Object { $targetsA["$($_.phase):$($_.name)"] = $_.bytes }
$b | Where-Object type -eq 'target' | ForEach-Object { $targetsB["$($_.phase):$($_.name)"] = $_.bytes }
foreach ($key in $targetsA.Keys) {
    if (-not $targetsB.ContainsKey($key) -or $targetsA[$key] -ne $targetsB[$key]) {
        $errors.Add("target mismatch: $key")
    }
}

$iatA = @($a | Where-Object type -eq 'iat')
$iatB = @($b | Where-Object type -eq 'iat')
if ($iatA.Count -ne 406 -or $iatB.Count -ne 406) {
    $errors.Add("expected 406 IAT rows in each capture")
} else {
    for ($index = 0; $index -lt $iatA.Count; ++$index) {
        $left = $iatA[$index]
        $right = $iatB[$index]
        if ($left.rva -ne $right.rva -or $left.module -ne $right.module -or
            $left.function -ne $right.function -or $left.current -ne $right.current) {
            $errors.Add("IAT source-state mismatch at index $index")
        }
    }
}

$summaryA = $a | Where-Object type -eq 'iat_repair_summary'
$summaryB = $b | Where-Object type -eq 'iat_repair_summary'
if ($summaryA.resolved -ne 405 -or $summaryA.unresolved -ne 1 -or
    $summaryB.resolved -ne 405 -or $summaryB.unresolved -ne 1) {
    $errors.Add("unexpected IAT resolution summary")
}

if ($errors.Count -ne 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "captures match and precede mutation: $($targetsA.Count) target phases; 406 zero-source IAT slots; 405 resolvable; 1 intentionally unresolved"
