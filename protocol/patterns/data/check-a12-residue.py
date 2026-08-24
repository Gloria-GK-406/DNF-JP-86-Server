#!/usr/bin/env python3
"""Report how far the A12 content has been retired, and prove when it is gone.

The migration rule: A12 conclusions are scaffolding. A page keeps them only until the
same protocol is redone against A21, and then the whole `rks:a12` block is deleted. The
end state is a tree with no A12 content left. That is a property worth checking rather
than remembering, because "did we clean that one up?" is exactly the question nobody
answers correctly from memory a month later.

Four states per page, and `mixed` is the one that matters:

  a12-only  an A12 block, no A21 section    -- untouched by the migration
  mixed     both                             -- A21 findings recorded, A12 not yet retired
  a21       a conclusion, no A12 block       -- done, or hand-written A21-native
  empty     no conclusion either way         -- a generated page nobody has written in

`mixed` is not a defect. An `## A21` section records what differs; it does not by itself
make the page an A21 record, because the field tables above it are still A12's. A page
leaves `mixed` when its content stands on A21 evidence alone -- which for generated pages
means the machine zone has been regenerated against the A21 client.

usage: check-a12-residue.py <protocol-root> [--list-mixed]
exit 0 always; this reports, it does not gate. Gate on it when the count should be zero.
"""
import glob
import io
import os
import sys

BEGIN = '<!-- rks:a12:begin -->'
END = '<!-- rks:a12:end -->'
ORIGIN_A21 = '<!-- rks:origin:a21 -->'
MACHINE_END = '<!-- rks:machine:end -->'
A12_CLIENT_SHA = 'F78F0AEA'


SEED = '本协议尚无观察记录'


def has_conclusion(text):
    """Whether the human zone holds anything beyond the generator's seed sentence."""
    zone = text.split(MACHINE_END, 1)[-1]
    zone = zone.split('\n## A21', 1)[0]
    prose = [l.strip() for l in zone.split('\n')
             if l.strip() and not l.strip().startswith(('#', '<!--', '-->', '|', '---', '>'))]
    body = ''.join(prose)
    if body.startswith(SEED):
        k = body.find('evidence.md)')
        body = body[k + 12:] if k >= 0 else ''
    return len(body) >= 40


def classify(text):
    has_a12 = BEGIN in text
    has_a21 = '\n## A21' in text or ORIGIN_A21 in text
    if has_a12:
        return 'mixed' if has_a21 else 'a12-only'
    if has_a21:
        return 'a21'
    # No A12 block and no A21 section. Either a hand-written page that predates the
    # marker, or -- for almost all of them -- a generated page nobody has written in.
    return 'a21' if has_conclusion(text) else 'empty'


def main():
    if len(sys.argv) < 2:
        print(__doc__.strip().splitlines()[-2])
        return 2
    root = os.path.abspath(sys.argv[1])
    show = '--list-mixed' in sys.argv

    counts = {'a12-only': 0, 'mixed': 0, 'a21': 0, 'empty': 0}
    mixed, broken, machine_a12 = [], [], 0
    pages = []
    for d in ('server-to-client', 'client-to-server'):
        pages += sorted(glob.glob(os.path.join(root, 'game', d, '*.md')))

    for p in pages:
        t = io.open(p, encoding='utf-8').read().lstrip('﻿')
        rel = os.path.relpath(p, root).replace(os.sep, '/')
        if t.count(BEGIN) != t.count(END):
            broken.append(rel)
        if MACHINE_END in t and A12_CLIENT_SHA in t:
            machine_a12 += 1
        state = classify(t)
        counts[state] += 1
        if state == 'mixed':
            mixed.append(rel)

    print('=== A12 退场进度 ===')
    print('  页面总数            :', len(pages))
    print('  a12-only（未开始）  :', counts['a12-only'])
    print('  mixed（A21 已记差异，A12 待清）:', counts['mixed'])
    print('  a21（判据只剩 A21） :', counts['a21'])
    print('  empty（人工区无结论，没东西可清）:', counts['empty'])
    print()
    print('  机器区仍标 A12 客户端 SHA 的页 :', machine_a12)
    print('  -> 这些页的机器区要等对 A21 客户端重跑普查才会换掉；')
    print('     在那之前它们不可能真正离开 A12。')

    if broken:
        print()
        print('  !! a12 块不配对:', len(broken))
        for r in broken[:10]:
            print('     ', r)

    if show and mixed:
        print()
        print('  mixed 清单（下一步该清的就是这些）:')
        for r in mixed:
            print('     ', r)

    print()
    if counts['a12-only'] == counts['mixed'] == 0 and machine_a12 == 0:
        print('终态达成：树里已无 A12 内容。')
    else:
        print('尚未达到终态。')
    return 0


if __name__ == '__main__':
    sys.exit(main())
