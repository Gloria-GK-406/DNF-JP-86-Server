"""Check the scope and evidence claims of the proposal, without touching runtime state."""
from pathlib import Path
import collections
import hashlib
import json
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
TASK = Path(__file__).resolve().parents[1]
EVIDENCE = TASK / "evidence"


def read(name):
    return json.loads((EVIDENCE/name).read_text(encoding="utf-8-sig"))


issue, pvf, scope = read("issue-371.json"), read("pvf-actions.json"), read("scope.json")
decisions, manifest = read("action-decisions.json"), read("source-manifest.json")
body = issue["body"].split("## 范围")[1].split("## 做法要点")[0]
tags = [tag for tag in dict.fromkeys(re.findall(r"`(\[[^\]]+\])`", body)) if tag != "[action type]"]
counts = collections.Counter(i["Action"][0] for i in pvf["Items"] if i["Id"] != 10006007)
assert len(tags) == 79 == len(decisions) == scope["ActionTypes"]
assert len(scope["Groups"]) == 9
assert {d["ActionType"] for d in decisions} == set(tags)
assert len({d["Case"] for d in decisions}) == len(decisions)
assert sum(counts[tag] for tag in tags) == 819 == scope["Items"]
assert sum(d["Count"] for d in decisions) == 819
assert pvf["ListedItems"] == 43041 and pvf["ActionItems"] == len(pvf["Items"]) == 1836
assert len({i["Action"][0] for i in pvf["Items"]}) == 117
assert not pvf["Errors"]
ids = {i["Id"]: i for i in pvf["Items"]}
for decision in decisions:
    representative = ids[decision["RepresentativeId"]]
    assert decision["Count"] == counts[decision["ActionType"]]
    assert representative["Action"][0] == decision["ActionType"]
    assert representative["Path"] == decision["PvfPath"]
    assert decision["AcceptanceStatus"] == "未执行" and decision["UnsupportedA21"] is False
    for field in ["Owner", "ProposedBehavior", "EvidenceGate", "RouteStatus"]:
        assert decision[field]
expected_batches = {"B1": (8,79), "B2": (23,359), "B3": (39,322), "R": (9,59)}
for batch, (kinds, templates) in expected_batches.items():
    selected = [d for d in decisions if d["Batch"] == batch]
    assert len(selected) == kinds and sum(d["Count"] for d in selected) == templates
scoped = [i for i in pvf["Items"] if i["Action"][0] in tags and i["Id"] != 10006007]
assert sum(any(s["Name"] == "[need material]" for s in i["Sections"]) for i in scoped) == 264
assert sum(not i["Name"] or not i["Name"][0] or i["Name"][0].startswith("name_") for i in scoped) == 281
assert pvf["Sha256"] == manifest["PvfSha256"]
auxiliary = next(s for s in pvf["AuxiliaryScripts"] if s["Path"] == "etc/amplifyitem.etc")
material_ids = {int(s["Values"][0]) for s in auxiliary["Sections"] if s["Name"] in
    {"[purify material]", "[purify only material]", "[purify only cera material]"}}
purify_ids = {i["Id"] for i in scoped if i["Action"][0] == "[equipment purify]"}
assert len(purify_ids & material_ids) == 9
assert purify_ids-material_ids == {10004425,10099331,490001781}
for artifact in manifest["Artifacts"]:
    path = EVIDENCE/artifact["Path"]
    assert hashlib.sha256(path.read_bytes()).hexdigest() == artifact["Sha256"], path
assert hashlib.sha256(Path(pvf["Archive"]).read_bytes()).hexdigest() == pvf["Sha256"]
client = read("client-static.json")
assert hashlib.sha256(Path(client["Path"]).read_bytes()).hexdigest() == client["Sha256"]
matrix = (TASK/"action-matrix.md").read_text(encoding="utf-8")
for decision in decisions:
    assert matrix.count(f"| {decision['Case']} `") == 1
for doc in TASK.rglob("*.md"):
    if set(doc.parts) & {"bin", "obj"}:
        continue
    content = doc.read_text(encoding="utf-8")
    for target in re.findall(r"\[[^\]]*\]\(([^\)]+)\)", content):
        if re.match(r"https?://|mailto:|#", target):
            continue
        path = (doc.parent / target.split("#",1)[0]).resolve()
        assert path.exists(), (doc, target)
for artifact in TASK.rglob("*"):
    if artifact.is_file() and artifact.suffix in {".md", ".json", ".py", ".cs", ".csproj"} and not set(artifact.parts) & {"bin", "obj"}:
        assert b"\r\n" not in artifact.read_bytes(), artifact
print(json.dumps({"Scope": {"Groups":9,"ActionTypes":79,"Templates":819},
    "Pvf": {"Listed":43041,"ActionItems":1836,"Errors":0}, "Judgments": expected_batches,
    "Checks": "scope counts, representative identity, pending status, evidence hashes, actual PVF/b1 hashes, Markdown links",
    "NewClientAcceptance": "not executed", "Result": "passed"}, ensure_ascii=False))
