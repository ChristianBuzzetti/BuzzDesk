#!/usr/bin/env python3
"""Appends Spanish → English pairs to LocEn.cs (before the closing brace), skipping keys already present.
Input on stdin: one pair per line, 'spanish<TAB>english'. Usage: python3 tools/add-loc.py "section name" < pairs.tsv"""
import os, re, sys
p = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'BuzzDesk', 'Core', 'LocEn.cs')
s = open(p, encoding='utf-8').read()
have = set(m.group(1) for m in re.finditer(r'\["((?:[^"\\]|\\.)*)"\]\s*=', s))
def q(t): return '"' + t.replace('\\n', '\x00').replace('\\', '\\\\').replace('"', '\\"').replace('\x00', '\\n') + '"'
lines = []
for raw in sys.stdin.read().splitlines():
    if not raw.strip(): continue
    es, en = raw.split('\t')
    key = q(es)[1:-1]
    if key in have: continue
    have.add(key)
    lines.append(f'        [{q(es)}] = {q(en)},')
if lines:
    section = sys.argv[1] if len(sys.argv) > 1 else 'BuzzDesk'
    i = s.rindex('    };')
    s = s[:i] + f'        // ---- {section}\n' + '\n'.join(lines) + '\n' + s[i:]
    open(p, 'w', encoding='utf-8').write(s)
print(f'{len(lines)} added')
