#!/usr/bin/env python3
"""Generate the simulator DDL from the output of scratch/mabna-schema-capture.sql.

Usage:
  python3 deploy/mabna-sim/gen_schema.py <capture-output.txt> [--out DIR] [--db TotalSystem]
                                         [--outside-fk stub|drop]

Writes into DIR (default deploy/mabna-sim/generated/, gitignored: it contains
real names when fed the real capture, FR-02):
  00-database.sql  CREATE DATABASE (captured collation) + compat level, recovery,
                   snapshot isolation as captured
  01-schema.sql    tables (column collations, identity, defaults, nullability,
                   PK), indexes, FKs between captured tables
  01a-stubs.sql    minimal stub tables for FK targets outside the capture and the
                   FKs that point at them (only with --outside-fk stub, default)
  03-views.sql     captured views whose referenced objects are ALL captured
                   tables/views; the others are left out (listed in a comment)

Accepted input: sqlcmd text output ("-s|"), either the plain form (a header row
starting with `section`, optional dashes row, data rows, blank line) or the
"===== result set N (k rows) =====" form. Booleans may be 0/1 or True/False;
empty or NULL means NULL. Sections used (label in the first column):
  1b database, 4 columns, 5 keys, 6 foreign_keys, 7 indexes (optional),
  9 row_counts (optional, used by gen_seed.py), 10 view_definitions (optional,
  200-char chunks wrapped in [ ] with \\n for newlines).

FK policy for targets outside the capture: create a stub table holding only the
referenced key columns, typed from the REFERENCING columns, with a primary key
on exactly those columns; gen_seed.py fills the stubs with the synthetic keys
the children use. A FK is dropped (and reported) when its target name looks like
a security/user/message table (scu*, msg*, scuUser, UserRights, users) or when
two FKs need the same stub column with a different type/collation.
Report procedures (optional, dependency-closure mode): --proc-roots NAMES adds stored
procedures and everything they reference to the closure. Procedures are written to
04-reports.sql, followed by the contents of --report-calls FILE (hand-written, gitignored,
the EXEC statements and their parameters). --sim-stubs FILE (hand-written, gitignored) holds
simulator-only stand-ins (tables with synthetic rows, views, functions) for objects the capture
does not contain (e.g. security/session objects that are never captured); every object it
creates counts as known, is written verbatim to 01b-sim-stubs.sql and never blocks a closure.
--sim-host-id N: after the report runs, 04-reports.sql re-keys the rows written under the init
client's process id (HOST_ID()) to N and ALTERs the views/functions that filter on HOST_ID() to
the literal N (03-objects.sql keeps the captured text, so the procedures see the real HOST_ID()
while they run). HOST_ID() is the client process id, so it cannot match a later read-only
connection (README).
Every rewrite/stub is listed in generated/sim-changes.txt.
Stdlib only. Example: deploy/mabna-sim/example/schema-capture.example.txt
"""
import argparse
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import re
import sys
from collections import OrderedDict, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
NEVER_STUB = re.compile(r"^(scu.*|msg.*|users|userrights)$", re.I)


def is_true(v):
    return str(v).strip().lower() in ("1", "true")


def nz(v):
    return None if v is None or v.strip() in ("", "NULL") else v


def parse(path):
    """Return {section_label: [row-dict,...]} for every result set."""
    sections = defaultdict(list)
    header = None
    with open(path, encoding="utf-8-sig") as fh:
        for raw in fh:
            line = raw.rstrip("\r\n")
            if line.startswith("=====") or not line.strip() or (
                    line.startswith("(") and "rows affected" in line):
                header = None
                continue
            parts = [p.strip() for p in line.split("|")]
            if parts[0] == "section":
                header = parts
                continue
            if header is None or set(line.replace("|", "").strip()) <= {"-"}:
                continue
            if len(parts) > len(header):  # a '|' inside the last column
                parts = parts[:len(header) - 1] + ["|".join(parts[len(header) - 1:])]
            if len(parts) != len(header):
                continue
            row = dict(zip(header, parts))
            sections[row["section"]].append(row)
    return sections


def q(name):
    return "[" + name.replace("]", "]]") + "]"


def sql_type(c):
    """T-SQL type of a column dict (user-defined alias types use their base type)."""
    t = (c.get("base_type") or c["type_name"]).lower()
    ml, prec, sc = int(c["max_length"]), int(c["precision"]), int(c["scale"])
    if t in ("nvarchar", "nchar"):
        return "%s(%s)" % (t, "max" if ml == -1 else ml // 2)
    if t in ("varchar", "char", "varbinary", "binary"):
        return "%s(%s)" % (t, "max" if ml == -1 else ml)
    if t in ("decimal", "numeric"):
        return "%s(%d,%d)" % (t, prec, sc)
    if t in ("datetime2", "time", "datetimeoffset"):
        return "%s(%d)" % (t, sc)
    return t  # int, bigint, tinyint, smallint, bit, datetime, money, float, image...


class Model:
    pass


def load_model(path, outside_fk="stub", selection=None, row_counts=None):
    """selection: optional set of (schema, table) to keep (others are ignored, as are
    their FKs); row_counts: optional plain 'SchemaName|ObjectName|type_desc|row_count' file."""
    s = parse(path)
    m = Model()
    m.sections = s
    cols = [r for r in s.get("4 columns", []) if r.get("type_desc") == "USER_TABLE"]
    if not cols:
        sys.exit("no '4 columns' rows for USER_TABLEs found in " + path)
    m.tables = OrderedDict()
    for r in cols:
        if selection is not None and (r["SchemaName"], r["ObjectName"]) not in selection:
            continue
        m.tables.setdefault((r["SchemaName"], r["ObjectName"]), []).append(r)
    for v in m.tables.values():
        v.sort(key=lambda r: int(r["ordinal"]))
    db = s.get("1b database", [])
    m.db = db[0] if db else {}
    m.collation = nz(m.db.get("collation_name", ""))

    m.pk = {}
    keys = defaultdict(list)
    for r in s.get("5 keys", []):
        if r.get("type_desc") == "PRIMARY_KEY_CONSTRAINT":
            keys[(r["SchemaName"], r["ObjectName"])].append(r)
    for k, rs in keys.items():
        rs.sort(key=lambda r: int(r["key_ordinal"]))
        m.pk[k] = {"name": rs[0]["constraint_name"],
                   "clustered": (rs[0].get("index_type") or "CLUSTERED").upper() == "CLUSTERED",
                   "cols": [r["column_name"] for r in rs]}

    m.indexes = defaultdict(list)  # non-PK indexes of captured tables
    idx = OrderedDict()
    for r in s.get("7 indexes", []):
        k = (r["SchemaName"], r["ObjectName"])
        if k not in m.tables or is_true(r["is_primary_key"]) or r["type_desc"] == "HEAP":
            continue
        if is_true(r.get("is_disabled", "0")) or is_true(r.get("has_filter", "0")):
            continue
        e = idx.setdefault((k, r["index_id"]), {
            "name": r["index_name"], "clustered": r["type_desc"] == "CLUSTERED",
            "unique": is_true(r["is_unique"]), "keys": [], "incl": []})
        if is_true(r["is_included_column"]):
            e["incl"].append(r["column_name"])
        else:
            e["keys"].append((int(r["key_ordinal"]), r["column_name"], is_true(r["is_descending_key"])))
    for (k, _), e in idx.items():
        e["keys"].sort()
        m.indexes[k].append(e)

    m.row_counts = {(r["SchemaName"], r["ObjectName"]): int(r["row_count"])
                    for r in s.get("9 row_counts", [])}
    if row_counts:
        with open(row_counts, encoding="utf-8-sig") as fh:
            for line in fh:
                p = line.rstrip("\r\n").split("|")
                if len(p) == 4 and p[3].strip().isdigit():
                    m.row_counts[(p[0].strip(), p[1].strip())] = int(p[3])

    # --- foreign keys, with the outside-target policy -----------------------
    colmap = {k: {c["column_name"]: c for c in v} for k, v in m.tables.items()}
    raw = OrderedDict()
    for r in s.get("6 foreign_keys", []):
        raw.setdefault((r["from_schema"], r["from_table"], r["fk_name"]), []).append(r)
    m.fks, m.dropped, m.stubs = [], [], OrderedDict()
    for (fs, ft, name), rs in raw.items():
        rs.sort(key=lambda r: int(r["column_ordinal"]))
        frm, to = (fs, ft), (rs[0]["to_schema"], rs[0]["to_table"])
        if frm not in m.tables:
            continue
        acts = []
        for key, kw in (("delete_referential_action_desc", "DELETE"),
                        ("update_referential_action_desc", "UPDATE")):
            v = rs[0].get(key, "NO_ACTION").replace("_", " ")
            if v != "NO ACTION":
                acts.append("ON %s %s" % (kw, v))
        fk = {"name": name, "from": frm, "cols": [r["from_column"] for r in rs], "to": to,
              "tocols": [r["to_column"] for r in rs], "act": " ".join(acts), "stub": False}
        if to in m.tables:
            m.fks.append(fk)
            continue
        if outside_fk != "stub":
            m.dropped.append((name, to[1], "outside the capture (policy: drop)"))
            continue
        if NEVER_STUB.match(to[1]):
            m.dropped.append((name, to[1], "security/user/message table is never stubbed"))
            continue
        stub = m.stubs.setdefault(to, OrderedDict())
        trial, bad = OrderedDict(), None
        for fc, tc in zip(fk["cols"], fk["tocols"]):
            c = colmap[frm][fc]
            sig = (sql_type(c), nz(c.get("collation_name", "")))
            old = stub.get(tc)
            if old is not None and old[0] != sig:
                bad = "column %s needs %s but the stub has %s" % (tc, sig[0], old[0])
                break
            trial[tc] = sig
        if bad:
            m.dropped.append((name, to[1], bad))
            continue
        for fc, tc in zip(fk["cols"], fk["tocols"]):
            if tc not in stub:
                c = dict(colmap[frm][fc])
                stub[tc] = (trial[tc], c)
        fk["stub"] = True
        m.fks.append(fk)
    m.stubs = OrderedDict((k, v) for k, v in m.stubs.items()
                          if any(f["to"] == k for f in m.fks))

    # --- views --------------------------------------------------------------
    chunks = defaultdict(list)
    for r in s.get("10 view_definitions", []):
        chunks[(r["SchemaName"], r["ObjectName"])].append(r)
    m.views = OrderedDict()
    for k, rs in chunks.items():
        rs.sort(key=lambda r: int(r["chunk_no"]))
        txt = "".join(r["definition_chunk"][1:-1]
                      if r["definition_chunk"].startswith("[") and r["definition_chunk"].endswith("]")
                      else r["definition_chunk"] for r in rs)
        m.views[k] = txt.replace("\\n", "\n")
    return m


def column_ddl(c, with_null=True):
    p = "    %s %s" % (q(c["column_name"]), sql_type(c))
    if nz(c.get("collation_name", "")):
        p += " COLLATE " + c["collation_name"]
    if is_true(c.get("is_identity", "0")):
        p += " IDENTITY(%s,%s)" % (nz(c.get("identity_seed", "")) or 1,
                                   nz(c.get("identity_increment", "")) or 1)
    if nz(c.get("default_definition", "")):
        p += " DEFAULT " + c["default_definition"]
    if with_null:
        p += " NULL" if is_true(c["is_nullable"]) else " NOT NULL"
    return p


def fk_ddl(f):
    return "ALTER TABLE %s.%s ADD CONSTRAINT %s FOREIGN KEY (%s)\n    REFERENCES %s.%s (%s)%s;\nGO\n" % (
        q(f["from"][0]), q(f["from"][1]), q(f["name"]), ", ".join(map(q, f["cols"])),
        q(f["to"][0]), q(f["to"][1]), ", ".join(map(q, f["tocols"])),
        (" " + f["act"]) if f["act"] else "")


def view_refs(txt):
    """Object names after FROM/JOIN (comments stripped); crude but conservative."""
    t = re.sub(r"--[^\n]*", " ", txt)
    t = re.sub(r"/\*.*?\*/", " ", t, flags=re.S)
    names = set()
    for mt in re.finditer(r"\b(?:from|join)\s+(\[?[\w]+\]?(?:\s*\.\s*\[?[\w]+\]?)?)", t, re.I):
        n = re.sub(r"[\[\]\s]", "", mt.group(1)).split(".")[-1]
        names.add(n)
    names -= {"select", "values"}
    seen = {}
    for n in sorted(names):
        seen.setdefault(n.lower(), n)
    return set(seen.values())


def parse_lookups(path):
    """lookups dump: '########## Table (n rows)' + header row + pipe-separated rows.
    Returns (OrderedDict {table: (header, rows)}, [skipped table names])."""
    out, skipped, cur = OrderedDict(), [], None
    with open(path, encoding="utf-8-sig") as fh:
        for raw in fh:
            line = raw.rstrip("\r\n")
            if line.startswith("##########"):
                head = line[10:].strip()
                if "SKIPPED" in head:
                    skipped.append(head.split(":")[0].strip())
                    cur = None
                    continue
                cur = [head.split(" ")[0], None, []]
                out[cur[0]] = cur
            elif cur is not None and line.strip():
                if cur[1] is None:
                    cur[1] = line.split("|")
                else:
                    cur[2].append(line.split("|"))
    return OrderedDict((k, (v[1], v[2])) for k, v in out.items() if v[1]), skipped


def fold_varchar(t):
    """What a CP1256 varchar column can hold (Persian yeh -> Arabic yeh, digits ASCII)."""
    o = []
    for ch in t:
        if ch == "ی":
            ch = "ي"
        elif "۰" <= ch <= "۹":
            ch = chr(48 + ord(ch) - 0x06F0)
        try:
            ch.encode("cp1256")
        except UnicodeEncodeError:
            ch = "?"
        o.append(ch)
    return "".join(o)


def lookup_literal(c, v):
    t = (c.get("base_type") or c["type_name"]).lower()
    null = is_true(c["is_nullable"])
    if v == "" or v == "NULL":
        if t in ("varchar", "char", "nvarchar", "nchar") and not null:
            return "N''"
        return "NULL"
    if t in ("varchar", "char"):
        return "N'" + fold_varchar(v).replace("'", "''") + "'"
    if t in ("nvarchar", "nchar"):
        return "N'" + v.replace("'", "''") + "'"
    if t == "bit":
        return "1" if v.strip().lower() in ("1", "true") else "0"
    if t in ("datetime", "smalldatetime", "datetime2", "date", "time", "datetimeoffset", "uniqueidentifier"):
        return "'" + v.replace("'", "''") + "'"
    if t in ("binary", "varbinary", "image"):
        return v if v.lower().startswith("0x") else "NULL"
    return v  # numeric types


HOST_ID_RE = re.compile(r"\bhost_id\s*\(\s*\)", re.I)
STUB_RE = re.compile(r"CREATE\s+(TABLE|VIEW|FUNCTION)\s+(?:\[?\w+\]?\s*\.\s*)?\[?(\w+)\]?(.*?)(?=\bGO\b|\Z)",
                     re.I | re.S)


USE_RE = re.compile(r"^\s*USE\s+\[?(\w+)\]?\s*;?\s*$", re.I | re.M)


def parse_sim_stubs(path, db):
    """{lowercase name: type_desc} of every table/view/function the stub file creates in
    database `db` (objects created after a `USE other_db` line, e.g. a simulator-only second
    database, are not part of the main database and are skipped)."""
    out = {}
    with open(path, encoding="utf-8-sig") as fh:
        txt = fh.read()
    segs, cur, last = [], db, 0
    for um in USE_RE.finditer(txt):
        segs.append((cur, txt[last:um.start()]))
        cur, last = um.group(1), um.end()
    segs.append((cur, txt[last:]))
    txt = "\n".join(t for d, t in segs if d.lower() == db.lower())
    for m in STUB_RE.finditer(txt):
        kind, name, rest = m.group(1).upper(), m.group(2).lower(), m.group(3)
        if kind == "TABLE":
            out[name] = "USER_TABLE"
        elif kind == "VIEW":
            out[name] = "VIEW"
        else:
            r = re.search(r"\bRETURNS\s+(@|TABLE)?", rest, re.I)
            out[name] = ("SQL_TABLE_VALUED_FUNCTION" if r and r.group(1) == "@" else
                         "SQL_INLINE_TABLE_VALUED_FUNCTION" if r and r.group(1) else "SQL_SCALAR_FUNCTION")
    return out


def select_objects(a, m_all):
    """Dependency closure. Returns a dict describing the selection (see main)."""
    import depclosure as dc
    s = m_all.sections
    known = {r["ObjectName"].lower(): r["type_desc"] for r in s.get("3 objects", [])}
    stubs = parse_sim_stubs(a.sim_stubs, a.db) if a.sim_stubs else {}
    known.update(stubs)  # simulator stand-ins: known, never excluded
    schemas = {r["SchemaName"].lower() for r in s.get("3 objects", [])} | {"dbo"}
    excluded = {r["ObjectName"].lower() for r in s.get("2 allowlist", [])
                if r.get("status", "").startswith("EXCLUDED")}
    defs = dc.read_definitions(a.definitions)
    proc_roots = [x.strip().lower() for x in a.proc_roots.split(",") if x.strip()] if a.proc_roots else []
    graph = dc.build_graph(defs, known, schemas, excluded, NEVER_STUB, include_procs=bool(proc_roots))
    by_name = {k[1].lower(): v for k, v in graph.items()}
    key_of = {k[1].lower(): k for k in graph}

    base_tables, base_views = set(), set()
    if a.base_capture:
        for r in parse(a.base_capture).get("2 allowlist", []):
            if r.get("status", "").startswith("OK"):
                (base_views if "VIEW" in r["status"] else base_tables).add(r["ObjectName"].lower())
    roots = [x.strip().lower() for x in a.roots.split(",") if x.strip()] if a.roots else sorted(base_views)
    roots = roots + [r for r in proc_roots if r not in roots]
    lookups, lk_skipped = (parse_lookups(a.lookups) if a.lookups else (OrderedDict(), []))

    # blocked: unreadable definition, excluded dependency, or cross-database reference;
    # propagates to everything that depends on a blocked object
    blocked = {}
    for n, node in by_name.items():
        d = defs[key_of[n]]
        if d["text"] is None:
            blocked[n] = "definition unreadable (encrypted or NULL)"
        elif node["excluded"]:
            blocked[n] = "uses excluded object(s): " + ", ".join(sorted(node["excluded"]))
        elif any(not r.startswith(a.db.lower() + ".") for r in node["remote"]):
            blocked[n] = "cross-database reference: " + ", ".join(sorted(node["remote"]))
    changed = True
    while changed:
        changed = False
        for n, node in by_name.items():
            if n not in blocked:
                bd = sorted(x for x in node["deps"] if x in blocked)
                if bd:
                    blocked[n] = "depends on blocked object(s): " + ", ".join(bd)
                    changed = True

    full = dc.closure(by_name, roots)  # includes blocked branches
    stack, ok = list(roots), set()
    while stack:
        n = stack.pop()
        if n in ok or n in blocked:
            continue
        ok.add(n)
        if n in by_name:
            stack.extend(by_name[n]["deps"])
    kind = lambda n: known.get(n, "?")
    objs = {n for n in ok if kind(n) in ("VIEW",) + dc.FN_TYPES and n not in stubs}
    procs = {n for n in ok if kind(n) == dc.PROC_TYPE}
    dep_tables = {n for n in ok if kind(n) == "USER_TABLE" and n not in stubs}
    full_tables = {n for n in full if kind(n) == "USER_TABLE"}
    other = {n for n in ok if kind(n) not in ("VIEW", "USER_TABLE", dc.PROC_TYPE) + dc.FN_TYPES}
    used_stubs = sorted(n for n in ok if n in stubs)

    tname = {k[1].lower(): k for k in m_all.tables}  # all captured USER_TABLEs
    lk_tables = {n.lower() for n in lookups if n.lower() in tname}
    want = base_tables | lk_tables | dep_tables
    base_missing = sorted(want - set(tname))
    want &= set(tname)
    # FK parents, transitively
    parents = defaultdict(set)
    for r in s.get("6 foreign_keys", []):
        parents[r["from_table"].lower()].add(r["to_table"].lower())
    direct = set(want)
    stack, fk_added, fk_excluded = list(want), set(), set()
    while stack:
        t = stack.pop()
        for p in parents.get(t, ()):
            if p in tname:
                if p not in want:
                    want.add(p)
                    fk_added.add(p)
                    stack.append(p)
            else:
                fk_excluded.add(p)
    order, cyc = dc.topo_order(objs, lambda n: by_name[n]["deps"] if n in by_name else ())
    proc_order, _ = dc.topo_order(procs, lambda n: by_name[n]["deps"] if n in by_name else ())
    return {"procs": proc_order, "stubs": stubs, "used_stubs": used_stubs, "proc_roots": proc_roots,
            "tables": want, "tname": tname, "direct_tables": direct, "fk_added": fk_added,
            "fk_outside": fk_excluded, "objs": objs, "order": order, "cyclic": cyc,
            "defs": defs, "key_of": key_of, "known": known, "blocked": blocked, "roots": roots,
            "graph": by_name, "full_tables": full_tables, "dep_tables": dep_tables,
            "other": other, "lookups": lookups, "lk_skipped": lk_skipped, "lk_tables": lk_tables,
            "base_tables": base_tables, "base_views": base_views, "base_missing": base_missing,
            "full": full}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("capture")
    ap.add_argument("--out", default=os.path.join(HERE, "generated"))
    ap.add_argument("--db", default="TotalSystem")
    ap.add_argument("--outside-fk", choices=("stub", "drop"), default="stub")
    ap.add_argument("--definitions", help="definitions dump (-- ======== TYPE [s].[n] ======== ... GO): "
                    "enables dependency-closure mode; views/functions come from it")
    ap.add_argument("--roots", help="comma-separated root views (default: the views of --base-capture)")
    ap.add_argument("--base-capture", help="earlier capture whose '2 allowlist' OK rows are always kept")
    ap.add_argument("--lookups", help="lookups dump; writes 02a-lookups.sql and keeps those tables")
    ap.add_argument("--row-counts", help="plain row-count file (SchemaName|ObjectName|type_desc|row_count)")
    ap.add_argument("--proc-roots", help="comma-separated stored procedures to add (with their closure); "
                    "written to 04-reports.sql (needs --definitions)")
    ap.add_argument("--sim-stubs", help="gitignored SQL file with simulator-only stand-in objects "
                    "(tables with synthetic rows, views, functions); written to 01b-sim-stubs.sql")
    ap.add_argument("--report-calls", help="gitignored SQL file with the EXEC statements that run the "
                    "procedures (appended to 04-reports.sql)")
    ap.add_argument("--sim-host-id", type=int, help="after the report runs: re-key hostid rows written by the "
                    "procedures to this value and ALTER views/functions to filter on it instead of HOST_ID()")
    a = ap.parse_args()

    sel = None
    if a.definitions:
        m_all = load_model(a.capture, a.outside_fk)
        sel = select_objects(a, m_all)
        keep = {sel["tname"][n] for n in sel["tables"]}
        m = load_model(a.capture, a.outside_fk, keep, a.row_counts)
    else:
        m = load_model(a.capture, a.outside_fk, None, a.row_counts)
    if not m.collation:
        print("WARNING: no database collation in capture; using server default", file=sys.stderr)
    os.makedirs(a.out, exist_ok=True)
    hdr = ("-- GENERATED by deploy/mabna-sim/gen_schema.py from a schema capture.\n"
           "-- Contains real Mabna names when fed the real capture: gitignored (FR-02).\n"
           "-- No data. SYNTHETIC DEMONSTRATION DATA only is loaded by 02-seed.sql.\n")

    compat = nz(m.db.get("compatibility_level", "")) or "150"
    with open(os.path.join(a.out, "00-database.sql"), "w", encoding="utf-8") as f:
        f.write(hdr)
        f.write("IF DB_ID(N'%s') IS NULL\n    CREATE DATABASE %s%s;\nGO\n" % (
            a.db, q(a.db), (" COLLATE " + m.collation) if m.collation else ""))
        f.write("ALTER DATABASE %s SET COMPATIBILITY_LEVEL = %s;\nGO\n" % (q(a.db), compat))
        rec = nz(m.db.get("recovery_model_desc", ""))
        if rec in ("SIMPLE", "FULL", "BULK_LOGGED"):
            f.write("ALTER DATABASE %s SET RECOVERY %s;\nGO\n" % (q(a.db), rec))
        if (nz(m.db.get("snapshot_isolation_state_desc", "")) or "").upper() == "ON":
            f.write("ALTER DATABASE %s SET ALLOW_SNAPSHOT_ISOLATION ON;\nGO\n" % q(a.db))

    out = [hdr, "USE %s;\nGO\n" % q(a.db)]
    for sc in sorted({k[0] for k in m.tables if k[0] != "dbo"}):
        out.append("IF SCHEMA_ID(N'%s') IS NULL EXEC(N'CREATE SCHEMA %s');\nGO\n" % (sc, q(sc)))
    for (sc, tb), cl in m.tables.items():
        lines = []
        for c in cl:
            if is_true(c.get("is_computed", "0")):
                lines.append("    %s AS %s" % (q(c["column_name"]), c["computed_definition"]))
            else:
                lines.append(column_ddl(c))
        k = m.pk.get((sc, tb))
        if k:
            lines.append("    CONSTRAINT %s PRIMARY KEY %s (%s)" % (
                q(k["name"]), "CLUSTERED" if k["clustered"] else "NONCLUSTERED",
                ", ".join(map(q, k["cols"]))))
        out.append("CREATE TABLE %s.%s (\n%s\n);\nGO\n" % (q(sc), q(tb), ",\n".join(lines)))
    n_idx = 0
    for (sc, tb), lst in m.indexes.items():
        for e in lst:
            cs = ", ".join(q(c) + (" DESC" if d else "") for _, c, d in e["keys"])
            out.append("CREATE %s%s INDEX %s ON %s.%s (%s)%s;\nGO\n" % (
                "UNIQUE " if e["unique"] else "", "CLUSTERED" if e["clustered"] else "NONCLUSTERED",
                q(e["name"]), q(sc), q(tb), cs,
                (" INCLUDE (%s)" % ", ".join(map(q, e["incl"]))) if e["incl"] else ""))
            n_idx += 1
    plain = [f for f in m.fks if not f["stub"]]
    out.extend(fk_ddl(f) for f in plain)
    with open(os.path.join(a.out, "01-schema.sql"), "w", encoding="utf-8") as f:
        f.write("\n".join(out))

    stub_fks = [f for f in m.fks if f["stub"]]
    sp = os.path.join(a.out, "01a-stubs.sql")
    if m.stubs:
        so = [hdr, "-- Minimal stub tables for FK targets outside the capture: key columns only,\n"
                   "-- typed from the referencing columns, PK on exactly those columns.\n"
                   "USE %s;\nGO\n" % q(a.db)]
        for (sc, tb), cs in m.stubs.items():
            lines = []
            for cn, (_, c) in cs.items():
                c = dict(c, column_name=cn, is_identity="0", default_definition="", is_nullable="0")
                lines.append(column_ddl(c))
            lines.append("    CONSTRAINT %s PRIMARY KEY (%s)" % (q("PK_" + tb), ", ".join(map(q, cs))))
            so.append("CREATE TABLE %s.%s (\n%s\n);\nGO\n" % (q(sc), q(tb), ",\n".join(lines)))
        so.extend(fk_ddl(f) for f in stub_fks)
        with open(sp, "w", encoding="utf-8") as f:
            f.write("\n".join(so))
    elif os.path.exists(sp):
        os.remove(sp)

    legacy_views = sel is None
    made, skipped, changes = [], [], []
    vp = os.path.join(a.out, "03-views.sql")
    op = os.path.join(a.out, "03-objects.sql")
    for stale in (vp, op):
        if os.path.exists(stale):
            os.remove(stale)
    if legacy_views:
        # views only when every referenced object is a captured table/view
        have = {k[1].lower() for k in m.tables} | {k[1].lower() for k in m.views}
        vo = [hdr, "USE %s;\nGO\n" % q(a.db)]
        for (sc, vn), txt in m.views.items():
            missing = sorted(n for n in view_refs(txt) if n.lower() not in have)
            if missing:
                skipped.append((vn, missing))
                continue
            vo.append(txt.strip() + "\nGO\n")
            made.append(vn)
        for vn, missing in skipped:
            vo.append("-- SKIPPED view %s: referenced objects not in the capture: %s\n" % (vn, ", ".join(missing)))
        with open(vp, "w", encoding="utf-8") as f:
            f.write("\n".join(vo) + "\n")
    else:
        vo = [hdr, "-- Functions and views from the captured definitions, dependencies first.\n"
                   "USE %s;\nGO\n" % q(a.db)]
        for n in sel["order"]:
            k = sel["key_of"][n]
            d = sel["defs"][k]
            body = d["text"].strip()
            vo.append("-- %s %s\n%s\nGO\n" % (d["type"], k[1], body))
            made.append(k[1])
        with open(op, "w", encoding="utf-8") as f:
            f.write("\n".join(vo) + "\n")

    # --- simulator stubs and report procedures ----------------------------
    write_reports(a, m, sel, hdr, changes)

    # --- lookup tables ---------------------------------------------------
    n_lk_rows = 0
    lp = os.path.join(a.out, "02a-lookups.sql")
    if os.path.exists(lp):
        os.remove(lp)
    lk_done = []
    if a.lookups and sel is not None:
        tbl = {k[1].lower(): k for k in m.tables}
        lk = OrderedDict((tbl[n.lower()], v) for n, v in sel["lookups"].items() if n.lower() in tbl)
        # order by FK among lookup tables
        deps = {t: {f["to"] for f in m.fks if f["from"] == t and f["to"] in lk and f["to"] != t} for t in lk}
        ordered, seen = [], set()

        def visit(t):
            if t in seen:
                return
            seen.add(t)
            for d in sorted(deps[t]):
                visit(d)
            ordered.append(t)
        for t in lk:
            visit(t)
        outside = {t: {f["to"] for f in m.fks if f["from"] == t and f["to"] not in lk} for t in lk}
        lo = ["-- ==================================================================\n"
              "-- Reference/code tables loaded with their captured codes and labels\n"
              "-- (not business rows). Gitignored: contains real names.\n"
              "-- ==================================================================\n",
              "USE %s;\nGO\nSET NOCOUNT ON;\n" % q(a.db)]
        for t in ordered:
            header, rows = lk[t]
            cmap = {c["column_name"]: c for c in m.tables[t]}
            cl = [cmap[h] for h in header if h in cmap and not is_true(cmap[h].get("is_computed", "0"))]
            idx = [i for i, h in enumerate(header) if h in cmap and not is_true(cmap[h].get("is_computed", "0"))]
            ident = any(is_true(c.get("is_identity", "0")) for c in cl)
            fq = "%s.%s" % (q(t[0]), q(t[1]))
            if outside[t]:
                lo.append("ALTER TABLE %s NOCHECK CONSTRAINT ALL;\n" % fq)
            if ident:
                lo.append("SET IDENTITY_INSERT %s ON;\n" % fq)
            names = ", ".join(q(c["column_name"]) for c in cl)
            for i in range(0, len(rows), 200):
                lo.append("INSERT INTO %s (%s) VALUES\n%s;\n" % (fq, names, ",\n".join(
                    "    (" + ", ".join(lookup_literal(c, r[ix]) for c, ix in zip(cl, idx)) + ")"
                    for r in rows[i:i + 200])))
            if ident:
                lo.append("SET IDENTITY_INSERT %s OFF;\n" % fq)
            if outside[t]:
                lo.append("ALTER TABLE %s CHECK CONSTRAINT ALL;\n" % fq)
            lo.append("GO\n")
            n_lk_rows += len(rows)
            lk_done.append((t[1], len(rows), sorted(x[1] for x in outside[t])))
        with open(lp, "w", encoding="utf-8") as f:
            f.write("\n".join(lo))

    if sel is not None:
        info = {"tables": sorted(k[1] for k in m.tables),
                "procedures": [sel["key_of"][n][1] for n in sel["procs"]],
                "lookups": [x[0] for x in lk_done],
                "views_functions": [sel["key_of"][n][1] for n in sel["order"]],
                "roots": sel["roots"]}
        import json
        with open(os.path.join(a.out, "selection.json"), "w", encoding="utf-8") as f:
            json.dump(info, f, indent=1)

    print("wrote %d tables, %d indexes, %d FKs among captured tables, %d stub tables with %d FKs, "
          "%d FKs dropped, %d views created, %d views skipped to %s" % (
              len(m.tables), n_idx, len(plain), len(m.stubs), len(stub_fks), len(m.dropped),
              len(made), len(skipped), a.out))
    for d in m.dropped:
        print("  dropped FK %s -> %s: %s" % d)
    for vn, missing in skipped:
        print("  skipped view %s: missing %s" % (vn, ", ".join(missing)))
    if sel is not None:
        report(sel, m, made, lk_done, n_lk_rows)


def write_reports(a, m, sel, hdr, changes):
    """01b-sim-stubs.sql, 04-reports.sql and sim-changes.txt (all generated/, gitignored)."""
    sp = os.path.join(a.out, "01b-sim-stubs.sql")
    rp = os.path.join(a.out, "04-reports.sql")
    cp = os.path.join(a.out, "sim-changes.txt")
    for stale in (sp, rp, cp):
        if os.path.exists(stale):
            os.remove(stale)
    if sel is None:
        return
    lines = []
    if a.sim_stubs:
        with open(a.sim_stubs, encoding="utf-8-sig") as fh:
            body = fh.read()
        with open(sp, "w", encoding="utf-8") as f:
            f.write(hdr + "-- SIMULATOR-ONLY stand-ins for objects the capture does not contain.\n"
                    "-- Synthetic demonstration data. Source: %s\n" % os.path.basename(a.sim_stubs))
            f.write("USE %s;\nGO\n" % q(a.db))
            f.write(body.rstrip() + "\n")
        for n in sel["used_stubs"]:
            lines.append("STUB (simulator-only, from %s): %s [%s]" % (
                os.path.basename(a.sim_stubs), n, sel["stubs"][n]))
    if sel["procs"]:
        out = [hdr, "-- Report procedures: run ONLY inside the simulator during init. They WRITE to the\n"
                    "-- per-host report tables; Zharfa never runs them (read-only connector, SELECT-only guard).\n"
                    "USE %s;\nGO\n" % q(a.db)]
        for n in sel["procs"]:
            k = sel["key_of"][n]
            out.append("-- %s %s\n%s\nGO\n" % (sel["defs"][k]["type"], k[1], sel["defs"][k]["text"].strip()))
        if a.report_calls:
            with open(a.report_calls, encoding="utf-8-sig") as fh:
                out.append("-- Report runs (parameters from %s)\n%s\nGO\n" % (
                    os.path.basename(a.report_calls), fh.read().rstrip()))
        if a.sim_host_id is not None:
            hosts = sorted((sc, tb, c["column_name"]) for (sc, tb), cl in m.tables.items()
                           for c in cl if c["column_name"].lower() == "hostid")
            out.append("-- Re-key the rows written under this client's process id to the fixed simulator\n"
                       "-- host id that the generated views use (HOST_ID() cannot match a later connection).\n"
                       "SET NOCOUNT ON;")
            for sc, tb, col in hosts:
                out.append("UPDATE %s.%s SET %s = %d WHERE %s = CAST(HOST_ID() AS int);" % (
                    q(sc), q(tb), q(col), a.sim_host_id, q(col)))
                changes.append("REKEY %s.%s.%s: rows written by report procedures set to %d" % (
                    sc, tb, col, a.sim_host_id))
            out.append("GO\n")
            # Phase 2: only now do the views/functions that filter on HOST_ID() get the fixed id
            # (during the report runs they must keep matching the process id the procedures write).
            for n in sel["order"]:
                k = sel["key_of"][n]
                d = sel["defs"][k]
                body, cnt = HOST_ID_RE.subn(str(a.sim_host_id), d["text"].strip())
                if not cnt:
                    continue
                body, nalt = re.subn(r"(?im)^(\s*)CREATE(\s+)(VIEW|FUNCTION)\b", r"\1ALTER\2\3", body, count=1)
                if nalt != 1:
                    sys.exit("cannot turn %s into ALTER" % k[1])
                out.append("-- SIM CHANGE: %s %s: HOST_ID() replaced by %d (%d occurrence(s)); see README\n%s\nGO\n" % (
                    d["type"], k[1], a.sim_host_id, cnt, body))
                changes.append("%s %s: ALTERed after the report runs, HOST_ID() -> %d (%d occurrence(s))" % (
                    d["type"], k[1], a.sim_host_id, cnt))
        with open(rp, "w", encoding="utf-8") as f:
            f.write("\n".join(out) + "\n")
        lines.extend("PROCEDURE created unchanged: " + sel["key_of"][n][1] for n in sel["procs"])
    with open(cp, "w", encoding="utf-8") as f:
        f.write("Simulator changes to captured code and simulator-only objects (gitignored, real names).\n")
        f.write("\n".join(changes + lines) + "\n")


def report(sel, m, made, lk_done, n_lk_rows):
    from collections import Counter
    t = Counter(sel["known"][n] for n in sel["objs"])
    print("closure: roots=%s" % ", ".join(sel["roots"]))
    print("  views/functions created: %d %s" % (len(made), dict(t)))
    print("  tables created: %d (base %d, lookups %d, referenced %d, FK parents added %d)" % (
        len(m.tables), len(sel["base_tables"]), len(sel["lk_tables"]), len(sel["dep_tables"]), len(sel["fk_added"])))
    print("  tables only needed by blocked objects (omitted): %d" % len(
        sel["full_tables"] - sel["dep_tables"] - sel["base_tables"]))
    print("  stubs: %d" % len(m.stubs))
    print("  blocked views/functions: %d" % len(sel["blocked"]))
    for n, why in sorted(sel["blocked"].items()):
        if n in sel["full"]:
            print("    BLOCKED (in closure) %s: %s" % (n, why))
    if sel["cyclic"]:
        print("  cycles: %s" % ", ".join(sel["cyclic"]))
    if sel["other"]:
        print("  referenced non-table/view/function objects (not created): %s" % ", ".join(sorted(sel["other"])))
    if sel["base_missing"]:
        print("  wanted but not in capture: %s" % ", ".join(sel["base_missing"]))
    print("  FK parents outside the capture/excluded: %s" % ", ".join(sorted(sel["fk_outside"])) )
    print("  lookups: %d tables, %d rows; skipped %s" % (len(lk_done), n_lk_rows, sel["lk_skipped"]))
    unres = Counter()
    for n in sel["objs"]:
        for u in sel["graph"][n]["unresolved"]:
            unres[u] += 1
    print("  unresolved table-position names in created objects: %s" % dict(unres))


if __name__ == "__main__":
    main()
