#!/usr/bin/env python3
"""Dependency closure over captured T-SQL definitions (generic: no real names).

Used by gen_schema.py. Reads a definitions dump of the form

  -- ======== TYPE [schema].[name] ========
  <CREATE ... text>
  GO

(TYPE is the sys.objects type_desc; "-- NULL (encrypted or no permission)" as
the body marks an unreadable definition), tokenizes every view/function body
(comments, string literals, [bracketed] and "quoted" identifiers are handled
by a real tokenizer, not a regex over raw text) and returns, for each object,
the set of known objects it references.

Reference rules (only names that exist in the capture's object list count):
  * schema.name / db.schema.name  -> a reference to name
  * bare name in table position   -> after FROM / JOIN / APPLY / INTO / UPDATE /
                                     MERGE / USING / DELETE / EXEC, or after a comma
                                     inside a FROM list, or `name(` for a known
                                     function
  * alias.column                  -> NOT a reference (alias is not a schema)
Names in table position that are neither captured nor excluded (CTEs, temp
tables, system views) are returned as `unresolved` for review.
Stdlib only.
"""
import re
from collections import OrderedDict, defaultdict

HEADER = re.compile(r"^-- ======== ([A-Z_]+) \[([^\]]+)\]\.\[(.+)\] ========\s*$")
FROM_CLEAR = {"WHERE", "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT", "INTERSECT", "SELECT",
              "SET", "OPTION", "FOR", "ON", "WHEN", "THEN", "VALUES", "BEGIN", "END", "RETURN"}
TABLE_PREV = {"FROM", "JOIN", "APPLY", "INTO", "UPDATE", "MERGE", "USING", "EXEC", "EXECUTE", "DELETE"}
PROC_TYPE = "SQL_STORED_PROCEDURE"
FN_TYPES = ("SQL_SCALAR_FUNCTION", "SQL_TABLE_VALUED_FUNCTION", "SQL_INLINE_TABLE_VALUED_FUNCTION")


def read_definitions(path):
    """Return OrderedDict {(schema, name): {"type", "text" (None if unreadable)}}."""
    defs = OrderedDict()
    cur, buf = None, []

    def flush():
        if cur is None:
            return
        body = list(buf)
        while body and not body[-1].strip():
            body.pop()
        if body and body[-1].strip().upper() == "GO":
            body.pop()
        text = "\n".join(body).strip("\n")
        unreadable = (not text.strip()) or text.lstrip().startswith("-- NULL (encrypted")
        defs[(cur[1], cur[2])] = {"type": cur[0], "text": None if unreadable else text}

    with open(path, encoding="utf-8-sig") as fh:
        for line in fh:
            line = line.rstrip("\r\n")
            m = HEADER.match(line)
            if m:
                flush()
                cur, buf = m.groups(), []
            elif cur is not None:
                buf.append(line)
    flush()
    return defs


def tokenize(text):
    """Yield (kind, value, quoted): kind in word, id (bracket/quoted), dot, lp, rp, comma, other.
    Comments (incl. nested block comments), string literals and numbers are dropped."""
    i, n = 0, len(text)
    out = []
    while i < n:
        c = text[i]
        if c.isspace():
            i += 1
        elif text.startswith("--", i):
            j = text.find("\n", i)
            i = n if j < 0 else j + 1
        elif text.startswith("/*", i):
            depth, i = 1, i + 2
            while i < n and depth:
                if text.startswith("/*", i):
                    depth, i = depth + 1, i + 2
                elif text.startswith("*/", i):
                    depth, i = depth - 1, i + 2
                else:
                    i += 1
        elif c == "'" or (c in "nN" and text.startswith("'", i + 1) and
                          (i == 0 or not (text[i - 1].isalnum() or text[i - 1] in "_@#$"))):
            i += 1 if c == "'" else 2
            while i < n:
                if text[i] == "'":
                    if text.startswith("''", i):
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            out.append(("other", "str"))
        elif c == "[":
            j = i + 1
            buf = []
            while j < n:
                if text[j] == "]":
                    if text.startswith("]]", j):
                        buf.append("]")
                        j += 2
                        continue
                    break
                buf.append(text[j])
                j += 1
            out.append(("id", "".join(buf)))
            i = j + 1
        elif c == '"':
            j = text.find('"', i + 1)
            j = n if j < 0 else j
            out.append(("id", text[i + 1:j]))
            i = j + 1
        elif c.isalpha() or c in "_#@$" or ord(c) > 127:
            j = i + 1
            while j < n and (text[j].isalnum() or text[j] in "_@#$" or ord(text[j]) > 127):
                j += 1
            w = text[i:j]
            out.append(("var" if w[0] in "@#" else "word", w))
            i = j
        elif c.isdigit():
            j = i + 1
            while j < n and (text[j].isalnum() or text[j] == "."):
                j += 1
            out.append(("other", "num"))
            i = j
        elif c == ".":
            out.append(("dot", "."))
            i += 1
        elif c == "(":
            out.append(("lp", "("))
            i += 1
        elif c == ")":
            out.append(("rp", ")"))
            i += 1
        elif c == ",":
            out.append(("comma", ","))
            i += 1
        else:
            out.append(("other", c))
            i += 1
    return out


def references(text, known, schemas):
    """Return (refs, unresolved, remote): refs = set of lowercase names of known objects;
    unresolved = lowercase bare names seen in table position that are not known;
    remote = set of 3/4-part names whose database part is not the local one."""
    toks = tokenize(text)
    refs, unresolved, remote = set(), set(), set()
    depth, from_mode = 0, {0: False}
    prev_kw = None  # last significant token (upper word) before the current chain
    prev_kind = None
    i, n = 0, len(toks)
    while i < n:
        kind, val = toks[i]
        if kind == "lp":
            depth += 1
            from_mode[depth] = False
            prev_kind, prev_kw, i = "lp", None, i + 1
            continue
        if kind == "rp":
            from_mode.pop(depth, None)
            depth = max(0, depth - 1)
            prev_kind, prev_kw, i = "rp", None, i + 1
            continue
        if kind == "comma":
            prev_kind, prev_kw, i = "comma", None, i + 1
            continue
        if kind == "word" and val.upper() in FROM_CLEAR | {"FROM", "JOIN"}:
            u = val.upper()
            if u in ("FROM", "JOIN"):
                from_mode[depth] = True
            elif u in FROM_CLEAR:
                from_mode[depth] = False
            prev_kind, prev_kw, i = "word", u, i + 1
            continue
        if kind in ("word", "id"):
            parts, quoted = [val], [kind == "id"]
            j = i + 1
            while j + 1 < n and toks[j][0] == "dot" and toks[j + 1][0] in ("word", "id"):
                parts.append(toks[j + 1][1])
                quoted.append(toks[j + 1][0] == "id")
                j += 2
            # 'db..obj' / 'schema..' forms: a dot followed by a dot
            while j < n and toks[j][0] == "dot":
                j += 1
            low = [p.lower() for p in parts]
            nxt_lp = j < n and toks[j][0] == "lp"
            in_table_pos = (prev_kind == "word" and prev_kw in TABLE_PREV) or \
                           (prev_kind == "comma" and from_mode.get(depth, False))
            if len(parts) == 1:
                is_kw_only = kind == "word" and val.upper() in {"AS", "NULL", "AND", "OR", "NOT", "IN", "EXISTS"}
                name = low[0]
                if not is_kw_only:
                    if in_table_pos:
                        if name in known:
                            refs.add(name)
                        else:
                            unresolved.add(name)
                    elif nxt_lp and name in known and known[name] in FN_TYPES:
                        refs.add(name)
            elif len(parts) == 2 and low[0] in schemas:
                if low[1] in known:
                    refs.add(low[1])
                elif in_table_pos or nxt_lp:
                    unresolved.add(low[1])
            elif len(parts) == 3 and low[1] in schemas:
                remote.add(".".join(low))  # db.schema.obj: judged by caller (same DB name -> local)
            elif len(parts) >= 3:
                remote.add(".".join(low))
            prev_kind = "word" if kind == "word" and len(parts) == 1 else "id"
            prev_kw = val.upper() if prev_kind == "word" else None
            i = j
            continue
        prev_kind, prev_kw = "other", None
        i += 1
    return refs, unresolved, remote


def build_graph(defs, known_types, schemas, excluded_names, excluded_re, include_procs=False):
    """known_types: {lowercase name: type_desc} of every captured object (plus any simulator
    stub objects: a known name is never treated as excluded).
    Returns {(schema,name): {"deps": set(lowercase), "excluded": set, "unresolved": set, "remote": set}}
    for every view / function in defs (and every stored procedure when include_procs)."""
    known = dict(known_types)
    graph = OrderedDict()
    for key, d in defs.items():
        if d["type"] != "VIEW" and d["type"] not in FN_TYPES and not (include_procs and d["type"] == PROC_TYPE):
            continue
        if d["text"] is None:
            graph[key] = {"deps": set(), "excluded": set(), "unresolved": set(), "remote": set()}
            continue
        # excluded names are looked up as if known so that hits are reported
        view = dict(known)
        for en in excluded_names:
            view.setdefault(en, "EXCLUDED")
        refs, unres, remote = references(d["text"], view, schemas)
        exc = {r for r in refs if view[r] == "EXCLUDED"}
        rest = set()
        for u in unres:
            if excluded_re.match(u):
                exc.add(u)
            else:
                rest.add(u)
        refs -= exc
        refs.discard(key[1].lower())
        graph[key] = {"deps": refs, "excluded": exc, "unresolved": rest, "remote": remote}
    return graph


def closure(graph_by_name, roots):
    """Transitive closure over view/function nodes. graph_by_name: {lname: node}.
    Returns the set of lowercase names reachable from roots (roots included)."""
    seen, stack = set(), list(roots)
    while stack:
        n = stack.pop()
        if n in seen:
            continue
        seen.add(n)
        node = graph_by_name.get(n)
        if node:
            stack.extend(node["deps"])
    return seen


def topo_order(nodes, deps_of):
    """Dependencies first. Returns (order, cyclic) where cyclic lists names in cycles
    (appended at the end in name order)."""
    order, state, cyc = [], {}, []

    def visit(n, path):
        if state.get(n) == 2:
            return
        if state.get(n) == 1:
            cyc.append(n)
            return
        state[n] = 1
        for d in sorted(deps_of(n)):
            if d in nodes:
                visit(d, path + [n])
        state[n] = 2
        order.append(n)

    for n in sorted(nodes):
        visit(n, [])
    return order, sorted(set(cyc))
