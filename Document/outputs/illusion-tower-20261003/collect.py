"""Read frozen PVF/A21/70 inputs; write a bounded illusion-tower evidence bundle."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from elftools.elf.elffile import ELFFile


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(name: str, text: str) -> None:
    normalized = "\n".join(line.rstrip() for line in text.splitlines()).rstrip() + "\n"
    (OUT / name).write_text(normalized, encoding="utf-8", newline="\n")


def pvf(*args: str) -> str:
    env = dict(os.environ, PVF_ARCHIVE_PATH=str(PVF))
    result = subprocess.run(
        ["dotnet", str(PROBE), *args], env=env, capture_output=True,
        text=True, encoding="utf-8", check=True,
    )
    return result.stdout.strip()


def section(text: str, tag: str) -> str:
    lines = text.splitlines()
    try:
        start = lines.index(tag) + 1
    except ValueError:
        return ""
    values = []
    for line in lines[start:]:
        if line.startswith("[") and line.endswith("]"):
            break
        values.append(line)
    return " ".join(values).strip()


def collect_pvf() -> None:
    dgn = pvf("dump", "dungeon/Towers/TowerOfIllusion.dgn", "12000")
    numbers = [int(x) for x in section(dgn, "[death tower map indexes]").split()]
    assert numbers[0] == 15 and len(numbers[1:]) == numbers[0] * 2
    stages = list(zip(numbers[1::2], numbers[2::2]))
    assert [s for s, _ in stages] == list(range(1, 16))
    rows = ["# Current PVF: illusion tower", "", "```text", dgn, "```"]
    for path in ["etc/deathtower.etc", "etc/deathtowerclient.etc"]:
        rows += ["", f"## {path}", "", "```text", pvf("dump", path, "4000"), "```"]
    rows += ["", "## Entry item 4183", "", "```text",
             pvf("lst", "stackable/stackable.lst", "4183", "1000"), "```"]
    refills = pvf("dump", "etc/chn_server_limititemusageinfo.etc", "12000")
    rows += ["", "## Current generic daily refill declarations", "", "```text",
             "[refill item]", section(refills, "[refill item]"), "```"]
    summary = ["| Floor | Map | Ordinary monster rows | Fixed APC rows | Random APC points |",
               "|---:|---:|---:|---:|---:|"]
    combat_tags = ["[dungeon]", "[monster]", "[monster specific AI]", "[ai character]",
                   "[apc random point]", "[monster spawn pos]", "[NPC]", "[passive object]"]
    apcs = set()
    for stage, map_id in stages:
        text = pvf("lst", "map/map.lst", str(map_id), "6000")
        monsters = section(text, "[monster]")
        fixed = section(text, "[ai character]")
        monster_tokens = re.findall(r"\[[^\]]+\]|\S+", monsters)
        apc_tokens = re.findall(r"\[[^\]]+\]|\S+", fixed)
        assert len(monster_tokens) % 10 == 0, (map_id, monsters)
        assert len(apc_tokens) % 8 == 0, (map_id, fixed)
        apcs.update(int(apc_tokens[i]) for i in range(0, len(apc_tokens), 8))
        summary.append(f"| {stage} | {map_id} | {len(monster_tokens)//10} | "
                       f"{len(apc_tokens)//8} | {section(text, '[apc random point]') or 'absent'} |")
        rows += ["", f"## Floor {stage}, map {map_id}", "", "```text", text.splitlines()[0]]
        for tag in combat_tags:
            value = section(text, tag)
            if value:
                rows += [tag, value]
        rows += ["```"]
    rows[2:2] = summary + [""]
    for code in sorted(apcs):
        rows += ["", f"## Fixed APC {code}", "", "```text",
                 pvf("lst", "aicharacter/aicharacter.lst", str(code), "2500"), "```"]
    write("pvf.md", "\n".join(rows))


def collect_70() -> None:
    with ELF.open("rb") as stream:
        elf = ELFFile(stream)
        section_text = elf.get_section_by_name(".text")
        data, base = section_text.data(), section_text["sh_addr"]
        symbols = [s for s in elf.get_section_by_name(".symtab").iter_symbols()
                   if s["st_info"]["type"] == "STT_FUNC" and s["st_size"]]
        names = {s["st_value"]: s.name for s in symbols}
        selected = [s for s in symbols if s.name.startswith("_ZN8WongWork11CDeathTower")]
        selected.sort(key=lambda s: s["st_value"])
        dis = Cs(CS_ARCH_X86, CS_MODE_32)
        full_terms = ["checkClearStage", "handleStartGame", "_checkStartGameCondition",
                      "handleStageCommand", "handleMoveMap", "handleDieUser", "handleLeaveUser",
                      "_checkMemberDie", "_onStartDeathTower", "_onFinishStage",
                      "_onPrepareFinishTower", "_onClear", "_onFinishDeathTower",
                      "generateRewardExp", "generateRewardItem", "_beginTowerClearProcess",
                      "moveNextStage", "resetCurrentStage", "getCurrentStage", "setState", "onTimer"]
        call_terms = full_terms + ["consistMap", "makeStagePacket", "onTimer", "handleFinishLoading",
                                 "handleKillMonster", "handleUseStackable", "makeDisplayReward",
                                 "makeDisplayRangking", "setState", "setTowerState"]
        output = ["# 70 reference: selected DeathTower function disassembly", "",
                  "Only direct-call targets are annotated. This does not prove A21 behavior."]
        index = ["# 70 reference: DeathTower function index and direct calls", ""]
        for sym in selected:
            address, size, name = sym["st_value"], sym["st_size"], sym.name
            index.append(f"- `0x{address:08X}` ({size} bytes) `{name}`")
            if not any(t in name for t in call_terms):
                continue
            instructions = list(dis.disasm(data[address-base:address-base+size], address))
            index += ["", f"## 0x{address:08X} {name}", ""]
            rendered = []
            for ins in instructions:
                target = None
                if ins.mnemonic == "call":
                    try:
                        target = int(ins.op_str, 16)
                    except ValueError:
                        pass
                note = names.get(target, "")
                if note:
                    index.append(f"- `0x{ins.address:08X}` → `0x{target:08X}` `{note}`")
                rendered.append(f"{ins.address:08X}: {ins.mnemonic:8} {ins.op_str}"
                                + (f" ; {note}" if note else ""))
            if any(t in name for t in full_terms):
                output += ["", f"## 0x{address:08X} {name}", "", "```asm", *rendered, "```"]
        write("tw70-functions.md", "\n".join(output))
        write("tw70-calls.md", "\n".join(index))


def collect_client() -> None:
    data = DUMP.read_bytes()
    base = 0x400000
    dis = Cs(CS_ARCH_X86, CS_MODE_32)
    ranges = [
        ("NOTI registrations 008E-0092", 0x115F7C5, 0x115F824),
        ("NOTI 008E handler initial window", 0x114A8F0, 0x114AB40),
        ("NOTI 008E branch continuation", 0x114AADE, 0x114ADE8),
        ("NOTI 008F handler", 0x1159850, 0x1159E5E),
        ("NOTI 0090 handler", 0x1153A40, 0x1153F2D),
        ("NOTI 0091 handler", 0x1148850, 0x114899F),
        ("NOTI 0092 handler", 0x1146DE0, 0x1146E30),
        ("ReadU16", 0x27C52A0, 0x27C52C0),
        ("ReadU32", 0x27C5310, 0x27C532E),
        ("CMD 009F start sender block", 0x26D7556, 0x26D757C),
        ("CMD 009F clear sender block", 0x26D8AFF, 0x26D8BB2),
        ("WriteU8", 0x274D2E0, 0x274D305),
        ("WriteU16", 0x274D310, 0x274D338),
    ]
    rows = ["# A21 client static evidence", "", "Image base: `0x00400000`.", "",
            "Handler windows are bounded disassembly, not recovered function boundaries. "
            "No live packets or gameplay acceptance were collected."]
    readers = {0x27C52A0: "ReadU16", 0x27C5310: "ReadU32", 0x27C5230: "ReadBytes",
               0x274D2E0: "WriteU8", 0x274D310: "WriteU16",
               0x274D1E0: "BeginCommand", 0x274E6D0: "Send"}
    for label, start, end in ranges:
        rows += ["", f"## {label}; VA 0x{start:08X}; RVA 0x{start-base:08X}", "", "```asm"]
        for ins in dis.disasm(data[start-base:end-base], start):
            note = ""
            if ins.mnemonic == "call":
                try:
                    note = readers.get(int(ins.op_str, 16), "")
                except ValueError:
                    pass
            rows.append(f"{ins.address:08X}: {ins.mnemonic:8} {ins.op_str}"
                        + (f" ; {note}" if note else ""))
        rows += ["```"]
    write("client.md", "\n".join(rows))


def collect_sources() -> None:
    current = OUT.parents[2]
    upstream = ROOT / "ServerS4A21"
    selections = [
        (current, "Server/src/Dfo.Server.Abstractions/World/IDungeonLayoutRuleService.cs", [(20, 30), (99, 117)]),
        (current, "Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs", [(77, 101), (195, 203)]),
        (current, "Server/src/Dfo.Server.Abstractions/World/DungeonCellState.cs", [(78, 92)]),
        (current, "Server/src/Dfo.Server.Abstractions/World/MonsterExperienceRules.cs", [(7, 18), (94, 95)]),
        (current, "Server/src/Dfo.Server.Application/World/DungeonClearDomainService.cs", [(15, 35)]),
        (current, "Server/src/Dfo.Server.Application/World/DungeonCellFactory.cs", [(42, 91)]),
        (upstream, "Server/DfoServer/Network/Builders/Dungeon/DeathTowerPacketBuilder.cs", [(17, 28), (34, 74), (109, 139)]),
        (upstream, "Server/DfoServer/Network/Handlers/Dungeon/DeathTowerCoordinator.cs", [(83, 143), (158, 219)]),
        (upstream, "Server/DfoServer/Game/DeathTower/DeathTowerSettlementService.cs", [(99, 145)]),
        (upstream, "Server/DfoServer/Game/DeathTower/DeathTowerMapLoader.cs", [(1, 250)]),
    ]
    rows = ["# Source excerpts at collection", "",
            "Code proves implementation only, not wire or client acceptance."]
    for repo in [current, upstream]:
        head = subprocess.check_output(["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip()
        rows += ["", f"Repository: `{repo}`; HEAD: `{head}`."]
    for repo, relative, spans in selections:
        path = repo / relative
        lines = path.read_text(encoding="utf-8-sig").splitlines()
        rows += ["", f"## {repo.name}: {relative}", "", f"SHA-256: `{sha256(path)}`."]
        for start, end in spans:
            assert 1 <= start <= end <= len(lines), (relative, start, end)
            rows += ["", "```csharp", *[f"{n}: {lines[n-1]}" for n in range(start, end+1)], "```"]
    write("source-excerpts.md", "\n".join(rows))


parser = argparse.ArgumentParser()
parser.add_argument("--workspace", type=Path, required=True)
options = parser.parse_args()
ROOT = options.workspace.resolve()
OUT = Path(__file__).resolve().parent
PVF = ROOT / "DNFClient-A21-protocol/Script.pvf"
PROBE = ROOT / "DNF-JP-86-Server/Tool/PvfProbe/bin/Release/net10.0/PvfProbe.dll"
ELF = ROOT / "Research/1995chen-dnf/other/等级补丁/70/df_game_r"
DUMP = ROOT / "DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin"
inputs = {key: {"path": str(path), "sha256": sha256(path)}
          for key, path in {"pvf": PVF, "probe": PROBE, "tw70": ELF, "client_dump": DUMP}.items()}
write("inputs.json", json.dumps(inputs, ensure_ascii=False, indent=2))
collect_pvf()
collect_70()
collect_client()
collect_sources()
print("Collected PVF, 70 direct-call/disassembly, A21 client and source evidence in", OUT)
