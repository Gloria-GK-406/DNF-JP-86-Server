"""Summarize scan.json without assigning semantics to field positions."""
import collections
import json
from pathlib import Path

root = Path(__file__).parent
d = json.loads((root / "scan.json").read_text(encoding="utf-8-sig"))
records = d["records"]
width = collections.Counter()
rows = []
for r in records:
    for values in r["sections"]["[enemy reward item]"]:
        width[len(values)] += 1
        if len(values) % 8 == 0:
            for k in range(0, len(values), 8):
                rows.append((r, tuple(map(int, values[k:k+8]))))

print("tag_count", d["enemyTagCount"], "files", len(records), "listed", sum(bool(r["questIds"]) for r in records))
print("width", sorted(width.items()))
print("rows", len(rows), "distinct_rows", len(set(row for _, row in rows)))
for i in range(8):
    vals = collections.Counter(row[i] for _, row in rows)
    print("field", i+1, "unique", len(vals), "minmax", min(vals), max(vals), "common", vals.most_common(18))
print("both_tags", sum("[monster reward item]" in r["sections"] for r in records))
print("types", collections.Counter(r["sections"].get("[type]", [["(absent)"]])[0][0] for r in records))
print("int_data_match_f5", sum(str(row[4]) in r["sections"].get("[int data]", [[]])[0] for r,row in rows), "of", len(rows))
lookups = d["lookups"]
for type_code in (1, 2, 3):
    sub = [(r,row) for r,row in rows if row[1] == type_code]
    print("type", type_code, "rows", len(sub), "unique_targets", len({row[0] for _,row in sub}))
    print("lists", {key: sum(str(row[0]) in lookups[key] for _,row in sub)
                    for key in ("monster", "aicharacter", "passiveobject")})
    print("examples", [(row[0], {key:lookups[key].get(str(row[0])) for key in ("monster", "aicharacter", "passiveobject") if str(row[0]) in lookups[key]}) for _,row in sub[:5]])
listed_rows = [(r,row) for r,row in rows if r["questIds"]]
print("listed_rows", len(listed_rows), "listed_width", sorted(collections.Counter(len(r["sections"]["[enemy reward item]"][0]) for r in records if r["questIds"]).items()))
print("listed_unique_rows", len({row for _,row in listed_rows}), "listed_unique_sources", len({(row[0],row[1]) for _,row in listed_rows}))
print("listed_types", collections.Counter(row[1] for _,row in listed_rows))
print("listed_f3", collections.Counter(row[2] for _,row in listed_rows).most_common(20))
print("listed_f4", collections.Counter(row[3] for _,row in listed_rows))
print("listed_f6", collections.Counter(row[5] for _,row in listed_rows))
print("listed_f7", collections.Counter(row[6] for _,row in listed_rows))
print("listed_f8", collections.Counter(row[7] for _,row in listed_rows))
for label, population in (("all", rows), ("listed", listed_rows)):
    relations = collections.Counter()
    other = []
    for r,row in population:
        data = list(map(int, r["sections"].get("[int data]", [[]])[0]))
        requirements = dict(zip(data[::2], data[1::2])) if r["sections"].get("[type]") == [["[seeking]"]] else {}
        required = requirements.get(row[4])
        key = "minus1" if row[7] == -1 else "equal_requirement" if row[7] == required else "different" if required is not None else "no_seeking_requirement"
        relations[key] += 1
        if key == "different" and len(other) < 8: other.append((r["path"], row, required))
    print(label, "field8_relation", relations, "different_examples", other)
print("lookup_dungeon", sum(str(row[2]) in lookups["dungeon"] for _,row in rows), "of", len(rows), "nonnegative", sum(row[2]>=0 for _,row in rows))
print("lookup_item", sum(str(row[4]) in lookups["stackable"] for _,row in rows), "of", len(rows))
print("unmatched_item", [(r["path"],row) for r,row in rows if str(row[4]) not in lookups["stackable"]][:12])
print("int_mismatch", [(r["path"],row,r["sections"].get("[int data]")) for r,row in rows if str(row[4]) not in r["sections"].get("[int data]", [[]])[0]][:10])
print("unmatched_dungeon_positive", [(r["path"],row[2]) for r,row in rows if row[2]>=0 and str(row[2]) not in lookups["dungeon"]][:20])
print("unmatched_target", [(r["path"],row) for r,row in rows if str(row[0]) not in lookups[{1:"monster",2:"aicharacter",3:"passiveobject"}[row[1]]]][:20])
print("listed_names_by_length")
for r in records:
    if r["questIds"] and len(r["sections"]["[enemy reward item]"][0]) >= 136:
        print(r["questIds"],r["path"],r["sections"].get("[name]"),len(r["sections"]["[enemy reward item]"][0]),r["sections"].get("[int data]"))
        if r["questIds"] == [13050]: break
print("sample_files")
for r in records:
    vals = r["sections"]["[enemy reward item]"][0]
    if len(vals) <= 16 and r["questIds"]:
        print(r["questIds"], r["path"], r["sections"].get("[name]"), "type", r["sections"].get("[type]"), "int_data", r["sections"].get("[int data]"), "enemy", vals)
        if sum(1 for x in records if len(x["sections"]["[enemy reward item]"][0]) <= 16 and x["questIds"] and records.index(x) <= records.index(r)) >= 30:
            break
print("chosen_samples")
for qid in (2201, 2415, 13607, 5651, 7630, 3081):
    r = next(r for r in records if qid in r["questIds"])
    s = r["sections"]
    values = list(map(int, s["[enemy reward item]"][0]))
    print(qid, r["path"], "name", s.get("[name]"), "type", s.get("[type]"),
          "int", s.get("[int data]"), "rows", len(values)//8, "first", values[:32],
          "condition", s.get("[condition message]"))
print("same_source_multi_rows")
multi_same_key = 0
multi_different_item = 0
multi_same_target = 0
for r in records:
    if not r["questIds"]: continue
    vals = list(map(int,r["sections"]["[enemy reward item]"][0]))
    grouped = collections.defaultdict(list)
    for i in range(0,len(vals),8):
        row = tuple(vals[i:i+8])
        grouped[row[:4]].append(row)
    for key, group in grouped.items():
        if len(group)>1:
            multi_same_key += 1
            print("duplicate_key",r["questIds"],r["path"],r["sections"].get("[name]"),group)
        if len(group)>1 and len({row[4] for row in group})>1:
            multi_different_item += 1
            print(r["questIds"],r["path"],r["sections"].get("[name]"),key,group[:8],"int",r["sections"].get("[int data]"))
            break
    grouped_target = collections.defaultdict(list)
    for group in grouped.values():
        for row in group: grouped_target[row[:2]].append(row)
    if any(len(v)>1 and len({row[4] for row in v})>1 for v in grouped_target.values()): multi_same_target += 1
print("same_key_multiple",multi_same_key,"different_item",multi_different_item,"same_target_different_item_files",multi_same_target)
