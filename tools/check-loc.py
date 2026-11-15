#!/usr/bin/env python3
"""Lists Spanish UI strings (L.T / L.F / L.Count / {local:T '...'}) that have no English entry in LocEn or LocEnDesk.
Usage: python3 tools/check-loc.py [--unused]   (--unused also lists English entries no code uses)."""
import os, re, sys
root = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'BuzzDesk')
lit = r'"((?:[^"\\]|\\.)*)"'
used = set()
for dp, _, files in os.walk(root):
    if os.sep + 'obj' in dp or os.sep + 'bin' in dp: continue
    for f in files:
        p = os.path.join(dp, f)
        if f.endswith('.cs') and f not in ('LocEn.cs', 'LocEnDesk.cs'):
            s = open(p, encoding='utf-8-sig').read()
            for m in re.finditer(r'L\.[TF]\(\s*' + lit, s): used.add(m.group(1))
            for m in re.finditer(r'L\.Count\([^,]+,\s*' + lit + r'\s*,\s*' + lit, s): used.update(m.groups())
            for m in re.finditer(r'\bT\(\s*' + lit + r'\s*\)', s): used.add(m.group(1))
        elif f.endswith('.xaml'):
            s = open(p, encoding='utf-8-sig').read()
            for m in re.finditer(r"\{local:T '((?:[^'])*)'\}", s): used.add(m.group(1).replace('&#xE710;', ''))
known = set()
for name in ('LocEn.cs', 'LocEnDesk.cs'):
    p = os.path.join(root, 'Core', name)
    if os.path.exists(p):
        for m in re.finditer(r'\[' + lit + r'\]\s*=', open(p, encoding='utf-8-sig').read()): known.add(m.group(1))
# Strings already written in English (Boberto's engine messages): L.T returns them as they are.
english = re.compile(r'^(Boberto|Invalid Boberto|Loading Boberto|load failed|\\nClick to open|JetBrains Mono)')
missing = sorted(s for s in used - known if not english.match(s))
for s in missing: print('MISSING:', s)
if '--unused' in sys.argv:
    for s in sorted(known - used): print('UNUSED:', s)
print(f'{len(used)} strings used, {len(missing)} missing', file=sys.stderr)
sys.exit(1 if missing else 0)
