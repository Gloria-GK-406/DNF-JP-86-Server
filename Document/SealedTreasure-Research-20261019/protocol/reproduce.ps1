[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$a21 = Join-Path $PSScriptRoot 'a21_dump_inspect.py'
$inspect70 = Join-Path $repo 'Document\abyss-research-20261007\evidence\70-inspect.py'
$inventory = Join-Path $repo 'Document\SealedTreasure-Research-20261019\content\inventory.json'

Push-Location $repo
try {
    Write-Output '=== A21 dump identity ==='
    python $a21 identity

    Write-Output '=== A21 relevant resource/action descriptors ==='
    python $a21 strings 'spicies dungeon|event required item|required item|PARTY SYMBOL'

    Write-Output '=== A21 descriptor absolute references ==='
    python $a21 xrefs 0x03657F90 0x03659BF0 0x03659F78 0x0367FFAC 0x0367FFE8 0x03683038 0x036831CC

    Write-Output '=== 70 server identity and generic admission symbols ==='
    python $inspect70 identity
    python $inspect70 symbols 'CheckEnterEventDungeon|do_after_dungeon_start'

    Write-Output '=== 70 generic dual-ticket admission/deletion assembly ==='
    python $inspect70 asm 0x0859EE66 0x0859FCFC 0x085A0756

    Write-Output '=== Frozen A21 PVF inventory identity/counts ==='
    python -c "import json,pathlib; p=pathlib.Path(r'$inventory'); d=json.loads(p.read_text(encoding='utf-8')); print(d['archive']); print('maps',len(d['maps']),'monsters',len(d['usedMonsterIds']),'passiveObjects',len(d['usedPassiveObjectIds']))"
}
finally {
    Pop-Location
}
