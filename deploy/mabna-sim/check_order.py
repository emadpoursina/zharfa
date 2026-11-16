#!/usr/bin/env python3
"""Static checks of generated/*.sql (no SQL engine needed; generic, no real names).

  python3 deploy/mabna-sim/check_order.py [generated-dir]

Walks the files in run order (00, 01, 01a, 02a, 02b, 03, ...) and verifies:
  * no object name is created twice (tables, views, functions, case-insensitive)
  * every CREATE VIEW/FUNCTION references only objects created EARLIER (token-based)
  * every FOREIGN KEY target table + columns exist when the FK is added
  * every index/PK column exists in its table
  * every INSERT targets an existing table and existing columns; for tables with
    FKs among the loaded lookup rows, parents are inserted first
Exits 1 on any problem.
"""
import glob
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import depclosure as dc  # noqa: E402

NAME = r"\[((?:[^\]]|\]\])+)\]"


def batches(path):
    txt = open(path, encoding="utf-8").read()
    return [b for b in re.split(r"^GO\s*$", txt, flags=re.M | re.I) if b.strip()]


def main():
    d = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "generated")
    files = sorted(glob.glob(os.path.join(d, "0*.sql")), key=os.path.basename)
    errs, tables, other, cols = [], {}, {}, {}
    inserted = set()
    fkpar = {}
    stats = {"tables": 0, "views": 0, "functions": 0, "fks": 0, "indexes": 0, "insert_batches": 0}
    all_names = set()
    # pre-scan: every object name that will exist at some point
    for f in files:
        for b in batches(f):
            m = re.search(r"CREATE\s+(?:TABLE|VIEW|FUNCTION)\s+" + NAME + r"\." + NAME, b, re.I)
            if m:
                all_names.add(m.group(2).lower())
    for f in files:
        base = os.path.basename(f)
        for b in batches(f):
            m = re.search(r"CREATE\s+TABLE\s+" + NAME + r"\." + NAME + r"\s*\((.*)\)\s*;", b, re.I | re.S)
            if m:
                n = m.group(2).lower()
                if n in tables or n in other:
                    errs.append("%s: duplicate object %s" % (base, m.group(2)))
                body = m.group(3)
                cl = re.findall(r"^\s{4}" + NAME + r"\s", body, re.M)
                tables[n] = {c.lower() for c in cl}
                stats["tables"] += 1
                for pk in re.findall(r"PRIMARY KEY\s+\w*\s*\(([^)]*)\)", body):
                    for c in re.findall(NAME, pk):
                        if c.lower() not in tables[n]:
                            errs.append("%s: PK column %s missing in %s" % (base, c, n))
                continue
            m = re.search(r"CREATE\s+(?:UNIQUE\s+)?(?:CLUSTERED\s+|NONCLUSTERED\s+)?INDEX\s+" + NAME + r"\s+ON\s+" + NAME + r"\." + NAME + r"\s*\(([^)]*)\)", b, re.I)
            if m:
                stats["indexes"] += 1
                n = m.group(3).lower()
                if n not in tables:
                    errs.append("%s: index %s on missing table %s" % (base, m.group(1), n))
                else:
                    for c in re.findall(NAME, m.group(4)):
                        if c.lower() not in tables[n]:
                            errs.append("%s: index %s column %s missing" % (base, m.group(1), c))
                continue
            m = re.search(r"ALTER TABLE\s+" + NAME + r"\." + NAME + r"\s+ADD CONSTRAINT\s+" + NAME + r"\s+FOREIGN KEY\s*\(([^)]*)\)\s+REFERENCES\s+" + NAME + r"\." + NAME + r"\s*\(([^)]*)\)", b, re.I)
            if m:
                stats["fks"] += 1
                a, t = m.group(2).lower(), m.group(6).lower()
                for tt in (a, t):
                    if tt not in tables:
                        errs.append("%s: FK %s uses table %s that does not exist yet" % (base, m.group(3), tt))
                if a in tables and t in tables:
                    for c in re.findall(NAME, m.group(4)):
                        if c.lower() not in tables[a]:
                            errs.append("%s: FK %s source column %s missing" % (base, m.group(3), c))
                    for c in re.findall(NAME, m.group(7)):
                        if c.lower() not in tables[t]:
                            errs.append("%s: FK %s target column %s missing" % (base, m.group(3), c))
                fkpar.setdefault(a, set()).add(t)
                continue
            m = re.search(r"^\s*CREATE\s+(VIEW|FUNCTION)\s+(?:" + NAME + r"|(\w+))\s*\.\s*(?:" + NAME + r"|(\w+))", b, re.I | re.M)
            if m:
                kind = m.group(1).upper()
                n = (m.group(4) or m.group(5)).lower()
                if n in tables or n in other:
                    errs.append("%s: duplicate object %s" % (base, n))
                known = {x: "USER_TABLE" for x in all_names}
                refs, unres, remote = dc.references(b, known, {"dbo"})
                refs.discard(n)
                for r in sorted(refs):
                    if r not in tables and r not in other:
                        errs.append("%s: %s %s references %s which is not created earlier" % (base, kind, n, r))
                if remote:
                    errs.append("%s: %s %s has cross-database reference %s" % (base, kind, n, sorted(remote)))
                other[n] = kind
                stats["views" if kind == "VIEW" else "functions"] += 1
                continue
            for m in re.finditer(r"INSERT INTO\s+" + NAME + r"\." + NAME + r"\s*\(([^)]*)\)", b, re.I):
                stats["insert_batches"] += 1
                n = m.group(2).lower()
                if n not in tables:
                    errs.append("%s: INSERT into missing table %s" % (base, n))
                    continue
                for c in re.findall(NAME, m.group(3)):
                    if c.lower() not in tables[n]:
                        errs.append("%s: INSERT column %s missing in %s" % (base, c, n))
                if base.startswith("02a"):
                    for p in fkpar.get(n, ()):
                        if p != n and p in lookups_in_file(d) and p not in inserted:
                            errs.append("%s: %s inserted before FK parent %s" % (base, n, p))
                    inserted.add(n)
    print("static checks over %d files: %s" % (len(files), stats))
    for e in errs:
        print("ERROR", e)
    print("OK" if not errs else "%d problem(s)" % len(errs))
    sys.exit(1 if errs else 0)


_lk = None


def lookups_in_file(d):
    global _lk
    if _lk is None:
        p = os.path.join(d, "02a-lookups.sql")
        _lk = {m.group(1).lower() for m in re.finditer(r"INSERT INTO\s+\[[^\]]+\]\." + NAME, open(p, encoding="utf-8").read())} if os.path.exists(p) else set()
    return _lk


if __name__ == "__main__":
    main()
