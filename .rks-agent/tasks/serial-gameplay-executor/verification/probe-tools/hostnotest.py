"""(A) records with no test frame: hook, stack length, entry frames, bottom frames. usage: hostnotest.py <round-dir> [project=Host]"""
import collections, json, os, re, sys
project = sys.argv[2] if len(sys.argv) > 2 else 'Host'
rs = json.load(open(os.path.join(sys.argv[1], project + '.json')))
a = [r for r in rs if r['class'] == 'A']
no = [r for r in a if not r['test_outer_full']]
print(f'{project} (A) records {len(a)} hits {sum(r["count"] for r in a)}; with a test frame {len(a) - len(no)} / '
      f'{sum(r["count"] for r in a if r["test_outer_full"])}; without {len(no)} / {sum(r["count"] for r in no)}')
groups = collections.defaultdict(list)
for r in no:
    frames = r['frames']
    bottom = [re.sub(r'^[^!]*!', '', f) for f in frames[-4:]]
    key = (r['hook'], len(frames) >= 40, r['entry'], r['entry_outer'], ' <- '.join(b[:80] for b in bottom))
    groups[key].append(r)
for (hook, truncated, entry, outer, bottom), rows in sorted(groups.items(), key=lambda kv: -sum(r['count'] for r in kv[1])):
    print(f'{len(rows):3d} rec {sum(r["count"] for r in rows):4d} hits | {hook} | {"truncated(40)" if truncated else "complete"} | entry {entry} | outer {outer}')
    print(f'      bottom: {bottom}')
