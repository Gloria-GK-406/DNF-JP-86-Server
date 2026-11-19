"""Rebuild read-only scope, reference, and binary evidence for issue 371."""
from pathlib import Path
import collections
import hashlib
import json
import re
import struct
import sys

import capstone
import pefile

sys.stdout.reconfigure(encoding="utf-8")
TASK = Path(__file__).resolve().parents[1]
REPO = TASK.parents[2]
WORKSPACE = REPO.parent
EVIDENCE = TASK / "evidence"


def read_json(name):
    return json.loads((EVIDENCE / name).read_text(encoding="utf-8-sig"))


def write_json(name, value):
    (EVIDENCE / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


issue = read_json("issue-371.json")
pvf = read_json("pvf-actions.json")
scope_body = issue["body"].split("## 范围")[1].split("## 做法要点")[0]
groups = []
for line in scope_body.splitlines():
    if not line.startswith("|"):
        continue
    tags = re.findall(r"`(\[[^\]]+\])`", line)
    if not tags:
        continue
    items = [i for i in pvf["Items"] if i["Action"][0] in tags and i["Id"] != 10006007]
    groups.append({"Group": line.split("|")[1].strip(), "Tags": tags, "Items": len(items)})
tags = [tag for group in groups for tag in group["Tags"]]
assert len(tags) == len(set(tags))
scoped = [i for i in pvf["Items"] if i["Action"][0] in tags and i["Id"] != 10006007]
by_tag = {tag: [i for i in scoped if i["Action"][0] == tag] for tag in tags}
representatives = []
for group in groups:
    for tag in group["Tags"]:
        items = by_tag[tag]
        representative = next((i for i in items if i["Name"] and i["Name"][0] and not i["Name"][0].startswith("name_")), items[0])
        shapes = collections.Counter(" ".join(i["Action"][1:]) for i in items)
        representatives.append({"Group": group["Group"], "ActionType": tag, "Count": len(items),
            "Representative": representative, "OperandVariants": dict(shapes),
            "UseActionPacketVariants": dict(collections.Counter(" ".join(s["Values"]) for i in items for s in i["Sections"] if s["Name"] == "[use action packet]"))})
write_json("scope.json", {"Groups": groups, "ActionTypes": len(tags), "Items": len(scoped),
    "ArchiveActionTypes": len(set(i["Action"][0] for i in pvf["Items"])), "Representatives": representatives})

roots = {"CurrentImplementation": REPO / "Server/src",
         "UpstreamReference": WORKSPACE / "ServerS4A21/Server/DfoServer",
         "ThirdPartyReference": WORKSPACE / "Research/third-party-s4a21-v3.3/decompiled/DfoServer"}
references = {tag: {source: [] for source in roots} for tag in tags}
for source, root in roots.items():
    for file in root.rglob("*.cs"):
        if set(file.parts) & {"bin", "obj"}:
            continue
        content = file.read_text(encoding="utf-8-sig", errors="replace")
        found = [tag for tag in tags if tag.lower() in content.lower()]
        if not found:
            continue
        source_hash = sha(file)
        for number, line in enumerate(content.splitlines(), 1):
            for tag in found:
                if tag.lower() in line.lower():
                    references[tag][source].append({"Path": str(file.relative_to(root)), "Line": number,
                        "Text": line.strip()[:350], "Sha256": source_hash})
elf = WORKSPACE / "Research/1995chen-dnf/other/等级补丁/70/df_game_r"
elf_bytes = elf.read_bytes()
write_json("reference-matches.json", {"SearchBoundary": {k: str(v) for k,v in roots.items()},
    "Note": "Literal matches locate candidates; absence is not proof of missing behavior; references are not A21 evidence.",
    "Matches": references,
    "70Reference": {"Path": str(elf), "Sha256": hashlib.sha256(elf_bytes).hexdigest(),
                    "LiteralPresent": {tag: tag.encode() in elf_bytes for tag in tags}}})

binary = WORKSPACE / "ClientStore/versions/debug/20261115.3/DNF.unpacked-b1-full.exe"
data = binary.read_bytes()
pe = pefile.PE(data=data)
base = pe.OPTIONAL_HEADER.ImageBase
cs = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
def disasm(va, length):
    off = pe.get_offset_from_rva(va-base)
    return [{"VA": f"0x{i.address:08X}", "Bytes": i.bytes.hex(), "Instruction": f"{i.mnemonic} {i.op_str}"}
            for i in cs.disasm(data[off:off+length], va)]
windows = {"ConfirmationBuilder": disasm(0x01603C70, 130),
           "CopyItemActionByte": disasm(0x01605F66, 90),
           "CopyDefinitionAction": disasm(0x016079D0, 140),
           "AckSuccess": disasm(0x016041EE, 210),
           "AckFailure": disasm(0x0160486F, 170),
           "LocalConsumptionPredicate": disasm(0x015FC3D0, 640)}
write_json("client-static.json", {"Component": "debug:20261115.3", "Path": str(binary),
    "Sha256": hashlib.sha256(data).hexdigest(), "ImageBase": f"0x{base:08X}", "Addresses": "VA; RVA = VA - ImageBase",
    "LiteralSearch": {tag: {"ASCII": data.find(tag.encode()), "UTF16LE": data.find(tag.encode("utf-16le"))} for tag in tags},
    "Windows": windows, "Boundary": "ClientStatic; no new wire capture or client acceptance; string-search absence proves no unsupported gameplay."})

audit = WORKSPACE / "DNF-JP-86-Server/Document/tasks/consumable-use-audit-20261117"
write_json("source-manifest.json", {"MainBaseline": "aae13ba824989b7b2b1d966f4a6fc53d7aff15ce",
    "UpstreamBaseline": "1870c85860375f7232a022b6b1b98e78cf840e4f",
    "PvfSha256": pvf["Sha256"], "ClientContentSha256": "1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD",
    "PriorAudit": [{"Path": str(p), "Sha256": sha(p)} for p in [audit/"analysis.md",audit/"evidence/actions.tsv",audit/"evidence/examples.tsv"]],
    "Artifacts": [{"Path": p.name, "Sha256": sha(p)} for p in sorted(EVIDENCE.glob("*.json")) if p.name != "source-manifest.json"]})
print(json.dumps({"Groups": groups, "ActionTypes": len(tags), "Items": len(scoped), "Errors": pvf["Errors"]}, ensure_ascii=False))
