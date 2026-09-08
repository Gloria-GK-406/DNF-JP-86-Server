"""Independent, standard-library verification of a finalized CrapMeter run."""
import collections
import hashlib
import json
import math
import pathlib
import sys
import xml.etree.ElementTree as ET

run = pathlib.Path(sys.argv[1]).resolve()
def read(name):
    return json.loads((run / name).read_text(encoding="utf-8-sig"))

manifest = read("manifest.json")
detail = read("function-details.json")
rows = detail["functions"]
dist = read("distributions.json")
assert manifest["Run"]["Status"] == "complete", manifest["Run"]
assert manifest["MeasurementStatus"] == "partial"
assert manifest["Before"]["Commit"] == manifest["After"]["Commit"]
assert manifest["Before"]["Files"] == manifest["After"]["Files"]
assert len(manifest["Scope"]["Projects"]) == 10
assert len({r["id"] for r in rows}) == len(rows) == detail["count"]
inventory = read("raw/source-inventory.json")
assert {m["Id"] for f in inventory["Files"] for m in f["Members"]} == {r["id"] for r in rows}
for artifact in manifest["Artifacts"]:
    path = (run / artifact["Path"]).resolve()
    assert path.is_relative_to(run), artifact
    assert path.exists() == artifact["Exists"], artifact

raw = {}
repository = pathlib.Path(manifest["Repository"]["Root"]).resolve()
for report in sorted((run / "raw/coverage").glob("*.xml")):
    tree = ET.parse(report)
    for module in tree.findall(".//Module"):
        name = module.findtext("ModuleName")
        files = {f.attrib["uid"]: f.attrib["fullPath"] for f in module.findall("./Files/File")}
        for method in module.findall("./Classes/Class/Methods/Method"):
            method_name = method.findtext("Name")
            for point in method.findall("./SequencePoints/SequencePoint"):
                a = point.attrib
                if a.get("fileid") not in files:
                    continue
                path = pathlib.Path(files[a["fileid"]]).resolve()
                if not path.is_relative_to(repository):
                    continue
                relative = path.relative_to(repository).as_posix()
                key = (name, method_name + "#sp" + a["ordinal"])
                previous = raw.get(key, (int(a["sl"]), relative, 0))
                assert previous[:2] == (int(a["sl"]), relative)
                raw[key] = (previous[0], previous[1], previous[2] + int(a["vc"]))
    del tree

used = set()
for r in rows:
    points = r["coveragePoints"]
    assert len(points) == r["sequencePointsTotal"]
    assert sum(p["Hits"] > 0 for p in points) == r["sequencePointsCovered"]
    for ref in r["coverageReports"]:
        path = (run / ref).resolve()
        assert path.is_relative_to(run) and path.exists()
    for p in points:
        key = (p["Module"], p["Key"])
        assert key not in used, (r["id"], key)
        used.add(key)
        assert raw[key] == (p["Line"], p["RelativePath"], p["Hits"])
        assert r["startLine"] <= p["Line"] <= r["endLine"]
    if r["evidenceState"] == "Computed":
        coverage = r["sequencePointsCovered"] / r["sequencePointsTotal"]
        expected = r["complexity"] ** 2 * (1 - coverage) ** 3 + r["complexity"]
        assert 0 <= coverage <= 1 and math.isclose(expected, r["crap"], rel_tol=1e-12)
        assert r["measuredZero"] == (coverage == 0)
    else:
        assert r["crap"] is None and not r["measuredZero"]

rec = manifest["Reconciliation"]
counts = collections.Counter(r["evidenceState"] for r in rows)
assert sum(counts.values()) == len(rows) == rec["InventoryMembers"]
assert rec["SequencePointsMerged"] == len(raw)
assert rec["SequencePointsAttributed"] == len(used)
assert rec["SequencePointsMerged"] == rec["SequencePointsAttributed"] + rec["SequencePointsUnmapped"]
host = [r for r in rows if r["project"] == "Dfo.Server.Host"]
assert len(host) == 2
assert all(r["evidenceState"] == "Missing" and r["reason"] == "child-process-coverage-not-collected" for r in host)

computed = [r for r in rows if r["crap"] is not None]
complexities = sorted(r["complexity"] for r in computed)
threshold = complexities[math.ceil(0.75 * len(complexities)) - 1]
def close(a, b):
    if a is None or b is None:
        assert a is b, (a, b)
    else:
        assert math.isclose(a, b, rel_tol=1e-11, abs_tol=1e-11), (a, b)

def check_group(d, subset):
    cs = [r for r in subset if r["crap"] is not None]
    scores = sorted(r["crap"] for r in cs)
    assert d["InScope"] == len(subset)
    for state in ("Computed", "Missing", "Ambiguous", "Excluded"):
        assert d[state] == sum(r["evidenceState"] == state for r in subset)
    assert d["MeasuredZero"] == sum(r["measuredZero"] for r in subset)
    for field, p in (("Min", 0), ("Median", .5), ("P75", .75), ("P90", .9), ("P95", .95), ("Max", 1)):
        close(d[field], scores[max(0, math.ceil(len(scores) * p) - 1)] if scores else None)
    bands = {"Low": sum(s <= 5 for s in scores), "Moderate": sum(5 < s <= 15 for s in scores),
             "Elevated": sum(15 < s <= 30 for s in scores), "AboveWarning": sum(s > 30 for s in scores)}
    for field, count in bands.items():
        assert d[field] == count
        close(d[field + "Share"], count / len(scores) if scores else None)
    close(d["FunctionScoreSum"], sum(scores) if scores else None)
    close(d["FunctionScoreMean"], sum(scores) / len(scores) if scores else None)
    denominator = len(subset) - d["Excluded"]
    close(d["MeasuredShare"], len(cs) / denominator if denominator else None)
    for high in (False, True):
        for low in (False, True):
            name = ("High" if high else "Low") + "Complexity" + ("Low" if low else "High") + "Coverage"
            assert d[name] == sum((r["complexity"] > threshold) == high and (r["coverage"] < .5) == low for r in cs)
    expected = sorted(cs, key=lambda r: (-r["crap"], r["id"]))[:manifest["Execution"]["TopN"]]
    assert [r["Id"] for r in d["TopFunctions"]] == [r["id"] for r in expected]

check_group(dist["whole"], rows)
scopes = [("byProject", lambda r: r["project"]), ("byFile", lambda r: r["path"]),
          ("byClass", lambda r: r["project"] + "::" + r["container"])]
for field, key in scopes:
    groups = collections.defaultdict(list)
    for r in rows:
        groups[key(r)].append(r)
    if field == "byProject":
        for project in manifest["Scope"]["Projects"]:
            groups[project]
    assert set(groups) == {d["Key"] for d in dist[field]}
    for d in dist[field]:
        check_group(d, groups[d["Key"]])

execution = manifest["Execution"]
assert execution["BuildExitCode"] == execution["TestExitCode"] == 0
assert execution["AllTestsPassed"] and len(execution["Tests"]) == 7
passed = sum(t["Passed"] for t in execution["Tests"])
assert passed >= 2392 and sum(t["Failed"] for t in execution["Tests"]) == 0
report = (run / "report.md").read_text(encoding="utf-8-sig")
headings = [report.index(h) for h in ("## 1.", "## 2.", "## 3.", "## 4.", "## 5.")]
assert headings == sorted(headings)
assert "子进程" in report[:report.index("## 2.")]
samples = [min(computed, key=lambda r: r["crap"]),
           next(r for r in computed if 5 < r["crap"] <= 15),
           max(computed, key=lambda r: r["crap"]), next(r for r in computed if r["measuredZero"])]
print(json.dumps({"run": run.name, "decision": "pass", "states": counts,
    "raw_points": len(raw), "attributed_points": len(used), "groups": 1 + sum(len(dist[k]) for k, _ in scopes),
    "passed_tests": passed, "above_warning": dist["whole"]["AboveWarning"],
    "samples": [{k: r[k] for k in ("id", "symbol", "complexity", "coverage", "crap")} for r in samples]}, ensure_ascii=False, indent=2))
