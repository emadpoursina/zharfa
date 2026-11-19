#!/usr/bin/env python3
"""Generate SYNTHETIC data-quality bad records for the Mabna simulator (spec 005, US4, FR-030, SC-006).

Writes two T-SQL scripts into the gitignored output folder (default generated/dq):
  dq-apply.sql   seeds the bad records (contracts/sim-bad-records.md, steps 1-8)
  dq-revert.sql  removes every seeded row and drops the bookkeeping table

Usage:
  python3 deploy/mabna-sim/dq_bad_records.py --map deploy/mabna-sim/generated/purchase-source-map.json \\
      --date 1405/07/15 [--out deploy/mabna-sim/generated/dq] [--database TotalSystem]

Guard rails:
  - Standard library only. The tool holds no table or column name: every name is read from the source map. Committed
    examples use the Sim* names of deploy/mabna-sim/example/purchase-source-map.example.json.
  - The scripts run against the SIMULATOR only, as sa, by hand with sqlcmd (quickstart section 0). Zharfa never runs them,
    and no login or permission is changed. Outputs are synthetic and go to a gitignored folder; never commit them.
  - Exit code 2 with a message when a required item of the map is missing or the map or date is invalid.
  - A record whose column is not mapped, or whose column must be NULL but is NOT NULL on the target, is skipped and
    logged with kind 'skipped', so the manifest always equals what was really seeded.
"""

import argparse
import json
import os
import re
import sys
from collections import Counter

# ---------------------------------------------------------------------------------------------------------------------
# Jalali <-> Gregorian (same algorithm as backend/Zharfa.Api/Analytics/JalaliDate.cs and frontend/src/utils/jalali.ts)
# ---------------------------------------------------------------------------------------------------------------------

BREAKS = [-61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178]


def div(a, b):
    """Integer division truncating toward zero (C# and JavaScript semantics)."""
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b >= 0) else -q


def jalcal(jy):
    bl = len(BREAKS)
    gy = jy + 621
    leap_j = -14
    jp = BREAKS[0]
    jump = 0
    if jy < jp or jy >= BREAKS[bl - 1]:
        raise ValueError(f"year out of range: {jy}")
    for i in range(1, bl):
        jm = BREAKS[i]
        jump = jm - jp
        if jy < jm:
            break
        leap_j = leap_j + div(jump, 33) * 8 + div(jump % 33, 4)
        jp = jm
    n = jy - jp
    leap_j = leap_j + div(n, 33) * 8 + div((n % 33) + 3, 4)
    if (jump % 33) == 4 and jump - n == 4:
        leap_j += 1
    leap_g = div(gy, 4) - div((div(gy, 100) + 1) * 3, 4) - 150
    march = 20 + leap_j - leap_g
    return gy, march


def gregorian_to_jdn(gy, gm, gd):
    d = div((gy + div(gm - 8, 6) + 100100) * 1461, 4) + div(153 * ((gm + 9) % 12) + 2, 5) + gd - 34840408
    return d - div(div(gy + 100100 + div(gm - 8, 6), 100) * 3, 4) + 752


def jdn_to_gregorian(jdn):
    j = 4 * jdn + 139361631
    j = j + div(div(4 * jdn + 183187720, 146097) * 3, 4) * 4 - 3908
    i = div(j % 1461, 4) * 5 + 308
    gd = div(i % 153, 5) + 1
    gm = (div(i, 153) % 12) + 1
    gy = div(j, 1461) - 100100 + div(8 - gm, 6)
    return gy, gm, gd


def jalali_to_gregorian(jy, jm, jd):
    gy, march = jalcal(jy)
    jdn = gregorian_to_jdn(gy, 3, march) + (jm - 1) * 31 - div(jm, 7) * (jm - 7) + jd - 1
    return jdn_to_gregorian(jdn)


PERSIAN_DIGITS = str.maketrans("۰۱۲۳۴۵۶۷۸۹٠١٢٣٤٥٦٧٨٩", "0123456789" * 2)


class MapError(Exception):
    """A map or argument problem that refuses the run (exit code 2)."""


def parse_jalali(text):
    """Reads yyyy/mm/dd (or yyyy-mm-dd) with Latin or Persian digits into (jy, jm, jd)."""
    cleaned = text.strip().translate(PERSIAN_DIGITS)
    match = re.fullmatch(r"(\d{4})[/-](\d{2})[/-](\d{2})", cleaned)
    if not match:
        raise MapError(f"--date must be yyyy/mm/dd (Jalali), got {text!r}")
    jy, jm, jd = (int(part) for part in match.groups())
    if not (1 <= jm <= 12 and 1 <= jd <= 31):
        raise MapError(f"--date is not a Jalali date: {text!r}")
    return jy, jm, jd


# ---------------------------------------------------------------------------------------------------------------------
# T-SQL helpers
# ---------------------------------------------------------------------------------------------------------------------


def br(name):
    """A bracketed identifier; a closing bracket inside the name is doubled."""
    return "[" + str(name).replace("]", "]]") + "]"


def qualified(obj):
    """'dbo.Sim' or 'Sim' -> [dbo].[Sim] (the schema defaults to dbo)."""
    schema, _, table = obj.rpartition(".")
    return f"{br(schema or 'dbo')}.{br(table)}"


def lit(value):
    """A T-SQL literal for a map value."""
    if value is None:
        return "NULL"
    if isinstance(value, bool):
        return "1" if value else "0"
    if isinstance(value, (int, float)):
        return str(value)
    return "N'" + str(value).replace("'", "''") + "'"


def nstr(text):
    return "N'" + str(text).replace("'", "''") + "'"


class Entity:
    """One entity of the map: its object (schema-qualified) and its mapped columns."""

    def __init__(self, name, data):
        self.name = name
        self.data = data or {}
        obj = self.data.get("object")
        self.obj = (obj if "." in obj else f"dbo.{obj}") if isinstance(obj, str) and obj else None

    def col(self, item):
        spec = self.data.get(item)
        if isinstance(spec, dict) and isinstance(spec.get("column"), str) and spec["column"]:
            return spec["column"]
        return None

    def date_format(self):
        spec = self.data.get("date") or {}
        return spec.get("format", "jalaliText")

    def date_col(self):
        spec = self.data.get("date") or {}
        return spec.get("column") if isinstance(spec.get("column"), str) and spec.get("column") else None


class Ctx:
    """The map resolved into entities plus the record plan's shared settings."""

    def __init__(self, map_data, date_text, database):
        entities = (map_data or {}).get("entities") or {}
        self.inv = self._required(entities, "purchaseInvoice", ["key", "date"])
        self.line = self._required(entities, "purchaseLine", ["key", "documentKey", "grossAmount"])
        self.ret = Entity("purchaseReturn", entities["purchaseReturn"]) if "purchaseReturn" in entities else None
        if self.ret and not self.ret.obj:
            self.ret = None
        self.vendor = Entity("vendor", entities["vendor"]) if "vendor" in entities else None
        self.product = Entity("product", entities["product"]) if "product" in entities else None
        self.category = Entity("productCategory", entities["productCategory"]) if "productCategory" in entities else None
        # Phase 6 optional financial entities: absent entities only skip the financial records.
        self.pay_doc = self._optional(entities, "payableDocument")
        self.pay_chq = self._optional(entities, "payableCheque")
        self.pay_pay = self._optional(entities, "vendorPayment")
        self.pay_alloc = self._optional(entities, "paymentAllocation")
        self.database = database
        self.date_text = date_text
        jy, jm, jd = parse_jalali(date_text)
        self.jalali = (jy, jm, jd)
        if self.inv.date_format() not in ("jalaliText", "jalaliInt", "gregorian"):
            raise MapError(f"unknown date format {self.inv.date_format()!r} in purchaseInvoice.date")

    @staticmethod
    def _required(entities, name, items):
        if name not in entities:
            raise MapError(f"required entity missing from the map: {name}")
        entity = Entity(name, entities[name])
        if not entity.obj:
            raise MapError(f"required item missing from the map: {name}.object")
        for item in items:
            if not entity.col(item):
                raise MapError(f"required item missing from the map: {name}.{item}")
        return entity

    @staticmethod
    def _optional(entities, name):
        if name not in entities:
            return None
        entity = Entity(name, entities[name])
        return entity if entity.obj else None

    def date_literal_for(self, entity, jalali):
        """The date value in the entity's own date format (defaults to the invoice's format)."""
        jy, jm, jd = jalali
        fmt = entity.date_format() if entity is not None else self.inv.date_format()
        if fmt == "jalaliText":
            return nstr(f"{jy:04d}/{jm:02d}/{jd:02d}")
        if fmt == "jalaliInt":
            return str(jy * 10000 + jm * 100 + jd)
        gy, gm, gd = jalali_to_gregorian(jy, jm, jd)
        return nstr(f"{gy:04d}-{gm:02d}-{gd:02d}")

    def date_literal(self, jalali):
        """The date value in the map's format for a Jalali (jy, jm, jd) triple."""
        jy, jm, jd = jalali
        fmt = self.inv.date_format()
        if fmt == "jalaliText":
            return nstr(f"{jy:04d}/{jm:02d}/{jd:02d}")
        if fmt == "jalaliInt":
            return str(jy * 10000 + jm * 100 + jd)
        gy, gm, gd = jalali_to_gregorian(jy, jm, jd)
        return nstr(f"{gy:04d}-{gm:02d}-{gd:02d}")

    def filter_predicate(self, entity, alias):
        parts = []
        for item in entity.data.get("filter") or []:
            column = item.get("column")
            if not column:
                continue
            if item.get("op") != "Eq":
                raise MapError(f"unsupported filter op {item.get('op')!r} in {entity.name}")
            parts.append(f"{alias}.{br(column)} = {lit(item.get('value'))}")
        return " AND ".join(parts) or "1 = 1"

    def not_cancelled(self, entity, alias):
        spec = entity.data.get("cancelled") or {}
        column = spec.get("column")
        if spec.get("rule") == "column" and column:
            ref = f"{alias}.{br(column)}"
            return f"({ref} IS NULL OR {ref} <> {lit(spec.get('value'))})"
        return "1 = 1"


# ---------------------------------------------------------------------------------------------------------------------
# The records (contracts/sim-bad-records.md)
# ---------------------------------------------------------------------------------------------------------------------


class Plan:
    """Collects the T-SQL of the apply script and the expected manifest counts."""

    def __init__(self, ctx):
        self.ctx = ctx
        self.body = []
        self.expected = Counter()
        self.skipped = []
        self.nullable_objects = set()
        self.financial_objects = []  # Phase 6: financial tables the records touched (constraints + revert)

    def skip(self, number, issue, reason, obj):
        self.skipped.append((number, issue, reason))
        self.body.append(
            f"INSERT INTO dbo.SimDqSeed (object, key_value, kind, issue_type, note) "
            f"VALUES ({nstr(obj or '-')}, N'-', N'skipped', {nstr(issue)}, {nstr(f'record {number}: {reason}')});"
        )

    def add(self, text):
        self.body.append(text)


def doc_record(plan, n, issue, template, doc_sets, line_sets, flag, nullable=()):
    """
    One cloned document with its one line (template 'inv' or 'ret').
    doc_sets: (item, sql) on the document; a missing 'number' keeps @nNum + n, a missing 'date' keeps --date.
    line_sets: (item, sql) on the line. flag: ('document'|'line', note) for the flagged record; the other row is support.
    nullable: items that the record sets to NULL; each is guarded at run time (sys.columns.is_nullable).
    """
    ctx = plan.ctx
    entity = ctx.inv if template == "inv" else ctx.ret
    if entity is None:
        plan.skip(n, issue, "no purchase return in the map", None)
        return
    doc_items = dict(doc_sets)
    line_items = dict(line_sets)
    for item in doc_items:
        if item in ("number", "date"):
            continue
        if not entity.col(item):
            plan.skip(n, issue, f"{entity.name}.{item} is not mapped", entity.obj)
            return
    for item in line_items:
        if not ctx.line.col(item):
            plan.skip(n, issue, f"{ctx.line.name}.{item} is not mapped", ctx.line.obj)
            return
    if "date" not in doc_items:
        doc_items["date"] = ctx.date_literal(ctx.jalali)
    if flag[0] == "line" and not ctx.line.col("key"):
        plan.skip(n, issue, "line key is not mapped", ctx.line.obj)
        return

    t_doc = "@tInv" if template == "inv" else "@tRet"
    t_line = "@tLine" if template == "inv" else "@tRetLine"
    dkey = entity.col("key")
    lkey = ctx.line.col("key")
    ldoc = ctx.line.col("documentKey")

    extra_args = f", {nstr(ldoc)}, @d{n}" if ldoc else ""
    body = [
        f"DECLARE @d{n} bigint = @kDoc + {n};",
        f"DECLARE @l{n} bigint = @kLine + {n};",
        f"EXEC #dq_clone {nstr(entity.obj)}, {nstr(dkey)}, {t_doc}, @d{n};",
        f"EXEC #dq_clone {nstr(ctx.line.obj)}, {nstr(lkey)}, {t_line}, @l{n}{extra_args};",
        f"UPDATE {qualified(ctx.line.obj)} SET {br(ldoc)} = @d{n} WHERE {br(lkey)} = @l{n};",
    ]
    line_set_sql = []
    for item, sql in line_sets:
        line_set_sql.append(f"{br(ctx.line.col(item))} = {sql}")
    if line_set_sql:
        body.append(f"UPDATE {qualified(ctx.line.obj)} SET {', '.join(line_set_sql)} WHERE {br(lkey)} = @l{n};")

    doc_set_sql = []
    number_col = entity.col("number")
    if number_col and "number" in doc_items:
        value = doc_items["number"] if doc_items["number"] is not None else "NULL"
        doc_set_sql.append(f"{br(number_col)} = {value}")
    elif number_col:
        doc_set_sql.append(f"{br(number_col)} = @nNum + {n}")
    for item, sql in doc_sets:
        if item in ("number", "date"):
            continue
        doc_set_sql.append(f"{br(entity.col(item))} = {sql}")
    doc_disc_col = entity.col("docDiscount")
    if doc_disc_col and "docDiscount" not in doc_items:
        doc_set_sql.append(f"{br(doc_disc_col)} = 0")
    doc_set_sql.append(f"{br(entity.col('date'))} = {doc_items['date']}")
    body.append(f"UPDATE {qualified(entity.obj)} SET {', '.join(doc_set_sql)} WHERE {br(dkey)} = @d{n};")

    flag_kind, note = flag
    doc_kind = "flagged" if flag_kind == "document" else "support"
    line_kind = "flagged" if flag_kind == "line" else "support"
    body.append(f"-- record {n}: {issue} ({note})")
    body.append(f"INSERT INTO dbo.SimDqSeed (object, key_value, kind, issue_type, note) VALUES ("
                f"{nstr(entity.obj)}, CAST(@d{n} AS nvarchar(100)), {nstr(doc_kind)}, "
                f"{nstr(issue) if doc_kind == 'flagged' else 'NULL'}, {nstr(note if doc_kind == 'flagged' else 'support document')});")
    body.append(f"INSERT INTO dbo.SimDqSeed (object, key_value, kind, issue_type, note) VALUES ("
                f"{nstr(ctx.line.obj)}, CAST(@l{n} AS nvarchar(100)), {nstr(line_kind)}, "
                f"{nstr(issue) if line_kind == 'flagged' else 'NULL'}, {nstr(note if line_kind == 'flagged' else 'support line')});")
    if doc_kind == "flagged":
        plan.expected[issue] += 1
    if line_kind == "flagged":
        plan.expected[issue] += 1

    guards = []
    for item in nullable:
        owner = ctx.line if item[0] == "line" else entity
        column = owner.col(item[1])
        guards.append((owner.obj, column))
    if guards:
        checks = " AND ".join(
            f"EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID({nstr(obj)}) AND name = {nstr(col)} AND is_nullable = 1)"
            for obj, col in guards)
        plan.nullable_objects.update(obj for obj, _ in guards)
        plan.add(f"IF {checks}")
        plan.add("BEGIN")
        plan.body.extend(body)
        plan.add("END")
        plan.add("ELSE")
        plan.add(f"    INSERT INTO dbo.SimDqSeed (object, key_value, kind, issue_type, note) VALUES "
                 f"({nstr(entity.obj)}, N'-', N'skipped', {nstr(issue)}, {nstr(f'record {n}: column not nullable in the target')});")
    else:
        plan.body.extend(body)


def product_record(plan, n, key_offset, issue, name_sql, category, support_n):
    """A product cloned from the template product, plus a support invoice whose line uses it (records 8 and 15)."""
    ctx = plan.ctx
    if ctx.product is None or not ctx.product.col("key") or not ctx.product.col("name"):
        plan.skip(n, issue, "product master is not mapped", None)
        return False
    if not ctx.line.col("productKey"):
        plan.skip(n, issue, "purchaseLine.productKey is not mapped", ctx.line.obj)
        return False
    pkey = ctx.product.col("key")
    body = [
        f"DECLARE @p{n} bigint = @kProd + {key_offset};",
        f"EXEC #dq_clone {nstr(ctx.product.obj)}, {nstr(pkey)}, @tProd, @p{n};",
    ]
    sets = []
    if name_sql is not None:
        sets.append(f"{br(ctx.product.col('name'))} = {name_sql}")
    if category:
        if not ctx.product.col("categoryKey") or ctx.category is None or not ctx.category.col("key"):
            plan.skip(n, issue, "product category is not mapped", ctx.product.obj)
            return False
        sets.append(f"{br(ctx.product.col('categoryKey'))} = @noCategory")
    if sets:
        body.append(f"UPDATE {qualified(ctx.product.obj)} SET {', '.join(sets)} WHERE {br(pkey)} = @p{n};")
    body.append(f"INSERT INTO dbo.SimDqSeed (object, key_value, kind, issue_type, note) VALUES ("
                f"{nstr(ctx.product.obj)}, CAST(@p{n} AS nvarchar(100)), N'flagged', {nstr(issue)}, "
                f"{nstr('product: ' + ('unknown category' if category else 'duplicate name'))});")
    plan.add("\n".join(body))
    plan.expected[issue] += 1
    # The support invoice gives the product a line in the test month, so the product shows in the reports.
    doc_record(plan, support_n, issue, "inv", [], [("productKey", f"@p{n}")], ("none", "support document"))
    return True


def financial_record(plan, n, entity, template_var, base_var, issue, sets, note, needs=(), nullable_item=None, guards=()):
    """One cloned financial row (Phase 6, spec 008): payable document, payable cheque or vendor payment.
    Guarded at run time by the template row's existence, the column's nullability and any extra SQL guards;
    a guarded-out record lands in the manifest as 'skipped' so it never counts as seeded."""
    if entity is None or not entity.obj or not entity.col("key"):
        plan.skip(n, issue, "entity not in the map", None)
        return
    for item in needs:
        if not entity.col(item):
            plan.skip(n, issue, f"{entity.name}.{item} is not mapped", entity.obj)
            return
    keycol = entity.col("key")
    conds = [f"{template_var} IS NOT NULL"]
    if nullable_item:
        col = entity.col(nullable_item)
        if not col:
            plan.skip(n, issue, f"{entity.name}.{nullable_item} is not mapped", entity.obj)
            return
        conds.append(
            f"EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID({nstr(entity.obj)}) "
            f"AND name = {nstr(col)} AND is_nullable = 1)")
    conds.extend(guards)
    body = [
        f"IF NOT ({' AND '.join(conds)})",
        f"    INSERT INTO dbo.SimDqSeed (object, key_value, kind, issue_type, note) VALUES ("
        f"{nstr(entity.obj)}, N'-', N'skipped', {nstr(issue)}, {nstr(f'record {n}: template row or nullable column missing')});",
        "ELSE",
        "BEGIN",
        f"    DECLARE @f{n} bigint = {base_var} + {n};",
        f"    EXEC #dq_clone {nstr(entity.obj)}, {nstr(keycol)}, {template_var}, CAST(@f{n} AS nvarchar(100));",
    ]
    if sets:
        body.append(f"    UPDATE {qualified(entity.obj)} SET {', '.join(sets)} "
                    f"WHERE {br(keycol)} = CAST(@f{n} AS nvarchar(100));")
    body.append(f"    INSERT INTO dbo.SimDqSeed (object, key_value, kind, issue_type, note) VALUES ("
                f"{nstr(entity.obj)}, CAST(@f{n} AS nvarchar(100)), N'flagged', {nstr(issue)}, {nstr(note)});")
    body.append("END")
    plan.add("\n".join(body))
    plan.expected[issue] += 1
    if entity.obj not in plan.financial_objects:
        plan.financial_objects.append(entity.obj)


def build_plan(ctx):
    plan = Plan(ctx)
    inv, line, ret, vend, prod, cat = ctx.inv, ctx.line, ctx.ret, ctx.vendor, ctx.product, ctx.category
    not_cancelled_inv = ctx.not_cancelled(inv, "d")
    filter_inv = ctx.filter_predicate(inv, "d")
    inv_obj, inv_key, inv_num, inv_vendor = inv.obj, inv.col("key"), inv.col("number"), inv.col("vendorKey")
    line_obj, line_key, line_doc = line.obj, line.col("key"), line.col("documentKey")
    line_prd, line_qty = line.col("productKey"), line.col("quantity")
    line_gross, line_disc, line_vat = line.col("grossAmount"), line.col("lineDiscount"), line.col("vat")
    doc_disc = inv.col("docDiscount")

    # Templates and key bases (run-time selection, contracts/sim-bad-records.md step 5).
    header = [
        "-- Templates and new key bases (run-time selection).",
        "DECLARE @tInv nvarchar(100), @tRet nvarchar(100), @tLine nvarchar(100), @tRetLine nvarchar(100);",
        "DECLARE @tProd nvarchar(100), @qProd nvarchar(100);",
        "DECLARE @kDoc bigint = NULL, @kLine bigint = NULL, @kProd bigint = NULL, @nNum bigint = NULL;",
        "DECLARE @noVendor bigint = NULL, @noProduct bigint = NULL, @noCategory bigint = NULL;",
        f"SELECT TOP (1) @tInv = CAST(d.{br(inv_key)} AS nvarchar(100)) FROM {qualified(inv_obj)} AS d "
        f"WHERE {filter_inv} AND {not_cancelled_inv} "
        f"AND EXISTS (SELECT 1 FROM {qualified(line_obj)} AS i WHERE i.{br(line_doc)} = d.{br(inv_key)}) "
        f"ORDER BY d.{br(inv_key)};",
        f"SELECT TOP (1) @tLine = CAST(i.{br(line_key)} AS nvarchar(100)) FROM {qualified(line_obj)} AS i "
        f"WHERE i.{br(line_doc)} = @tInv ORDER BY i.{br(line_key)};",
    ]
    if ret is not None:
        ret_filter = ctx.filter_predicate(ret, "d")
        ret_not_cancelled = ctx.not_cancelled(ret, "d")
        header.append(
            f"SELECT TOP (1) @tRet = CAST(d.{br(ret.col('key'))} AS nvarchar(100)) FROM {qualified(ret.obj)} AS d "
            f"WHERE {ret_filter} AND {ret_not_cancelled} "
            f"AND EXISTS (SELECT 1 FROM {qualified(line_obj)} AS i WHERE i.{br(line_doc)} = d.{br(ret.col('key'))}) "
            f"ORDER BY d.{br(ret.col('key'))};")
        header.append(
            f"SELECT TOP (1) @tRetLine = CAST(i.{br(line_key)} AS nvarchar(100)) FROM {qualified(line_obj)} AS i "
            f"WHERE i.{br(line_doc)} = @tRet ORDER BY i.{br(line_key)};")
    if line_prd and prod is not None and prod.col("key"):
        header.append(
            f"SELECT TOP (1) @tProd = CAST(i.{br(line_prd)} AS nvarchar(100)) FROM {qualified(line_obj)} AS i "
            f"WHERE i.{br(line_key)} = @tLine;")
        header.append(
            f"SELECT TOP (1) @qProd = CAST(p.{br(prod.col('key'))} AS nvarchar(100)) FROM {qualified(prod.obj)} AS p "
            f"WHERE CAST(p.{br(prod.col('key'))} AS nvarchar(100)) <> @tProd ORDER BY p.{br(prod.col('key'))};")

    doc_objs = [inv_obj] + ([ret.obj] if ret is not None else [])
    key_union = " UNION ALL ".join(
        f"SELECT TRY_CAST(x.{br(inv_key if obj == inv_obj else ret.col('key'))} AS bigint) AS k FROM {qualified(obj)} AS x"
        for obj in doc_objs)
    number_union = " UNION ALL ".join(
        f"SELECT TRY_CAST(x.{br(inv_num)} AS bigint) AS n FROM {qualified(obj)} AS x"
        for obj in doc_objs) if inv_num else None
    header.append(f"SELECT @kDoc = ISNULL(MAX(u.k), 0) + 100000 FROM ({key_union}) AS u;")
    header.append(
        f"SELECT @kLine = ISNULL(MAX(TRY_CAST(x.{br(line_key)} AS bigint)), 0) + 100000 FROM {qualified(line_obj)} AS x;")
    if number_union:
        header.append(
            f"SELECT @nNum = ISNULL(MAX(u.n) + 100000, 900000) FROM ({number_union}) AS u;")
    else:
        header.append("SELECT @nNum = 900000;")
    if prod is not None and prod.col("key"):
        header.append(
            f"SELECT @kProd = ISNULL(MAX(TRY_CAST(x.{br(prod.col('key'))} AS bigint)), 0) + 100000 "
            f"FROM {qualified(prod.obj)} AS x;")
        # Records 7 and 15 reference keys that exist nowhere: the offset keeps them apart from the new product keys.
        header.append(
            f"SELECT @noProduct = ISNULL(MAX(TRY_CAST(x.{br(prod.col('key'))} AS bigint)), 0) + 200000 "
            f"FROM {qualified(prod.obj)} AS x;")
    if vend is not None and vend.obj and vend.col("key"):
        header.append(
            f"SELECT @noVendor = ISNULL(MAX(TRY_CAST(x.{br(vend.col('key'))} AS bigint)), 0) + 100000 "
            f"FROM {qualified(vend.obj)} AS x;")
    if cat is not None and cat.obj and cat.col("key"):
        prod_cat_col = prod.col("categoryKey") if prod else None
        cat_check = f"CASE WHEN ISNULL(COL_LENGTH({nstr(cat.obj)}, {nstr(cat.col('key'))}), 4) <= 2"
        if prod_cat_col:
            cat_check += f" OR ISNULL(COL_LENGTH({nstr(prod.obj)}, {nstr(prod_cat_col)}), 4) <= 2"
        cat_check += " THEN 100 ELSE 100000 END"
        header.append(
            f"SELECT @noCategory = ISNULL(MAX(TRY_CAST(x.{br(cat.col('key'))} AS bigint)), 0) + {cat_check} "
            f"FROM {qualified(cat.obj)} AS x;")
    header.append(
        "IF @tInv IS NULL OR @tLine IS NULL RAISERROR(N'no template invoice with a line in the source', 16, 1);")

    # Phase 6 (spec 008): template key and new key base per mapped financial entity (skipped entities
    # only skip their records; the purchase records above must never fail because of them).
    for ent, tv, bv in ((ctx.pay_doc, "tPayDoc", "kPayDoc"),
                        (ctx.pay_chq, "tChq", "kChq"),
                        (ctx.pay_pay, "tPay", "kPay")):
        if ent is not None and ent.col("key"):
            kc = ent.col("key")
            header.append(f"DECLARE @{tv} nvarchar(100), @{bv} bigint;")
            header.append(
                f"SELECT TOP (1) @{tv} = CAST(d.{br(kc)} AS nvarchar(100)) FROM {qualified(ent.obj)} AS d "
                f"ORDER BY d.{br(kc)};")
            header.append(
                f"SELECT @{bv} = ISNULL(MAX(TRY_CAST(x.{br(kc)} AS bigint)), 0) + 100000 "
                f"FROM {qualified(ent.obj)} AS x;")
    plan.add("\n".join(header))

    date_sql = plan.ctx.date_literal(plan.ctx.jalali)
    other_date = lambda jy, jm, jd: plan.ctx.date_literal((jy, jm, jd))  # noqa: E731

    # 1-4: missing values (NULL on the document or line; guarded by the target's nullability).
    doc_record(plan, 1, "missing-values", "inv", [("number", "NULL")], [],
               ("document", "number"), nullable=[("doc", "number")])
    doc_record(plan, 2, "missing-values", "inv", [("vendorKey", "NULL")], [],
               ("document", "vendor"), nullable=[("doc", "vendorKey")])
    doc_record(plan, 3, "missing-values", "inv", [], [("productKey", "NULL")],
               ("line", "product"), nullable=[("line", "productKey")])
    doc_record(plan, 4, "missing-values", "inv", [], [("quantity", "NULL")],
               ("line", "quantity"), nullable=[("line", "quantity")])
    # 5: a duplicate of the template's number, same vendor (type + number + vendor, DU1).
    doc_record(plan, 5, "duplicate-records", "inv",
               [("number", f"(SELECT d0.{br(inv_num)} FROM {qualified(inv_obj)} AS d0 WHERE d0.{br(inv_key)} = @tInv)")],
               [], ("document", "number"))
    # 6-7: broken relationships (vendor and product keys that exist in no master).
    if vend is not None and vend.obj and vend.col("key") and inv_vendor:
        doc_record(plan, 6, "broken-relationships", "inv", [("vendorKey", "@noVendor")], [],
                   ("document", "vendor"))
    else:
        plan.skip(6, "broken-relationships", "vendor master or invoice vendorKey is not mapped", inv_obj)
    if line_prd and prod is not None and prod.col("key"):
        doc_record(plan, 7, "broken-relationships", "inv", [], [("productKey", "@noProduct")],
                   ("line", "product"))
    else:
        plan.skip(7, "broken-relationships", "purchaseLine.productKey or the product master is not mapped", line_obj)
    # 8: a second product with the same name (PI1, heuristic) and a support line in the test month.
    product_record(plan, 8, 0, "inconsistent-product-ids", None, False, 58)
    # 9-10: invalid dates (future and before the default earliest date).
    doc_record(plan, 9, "invalid-dates", "inv", [("date", other_date(1499, 1, 1))], [], ("document", "date"))
    doc_record(plan, 10, "invalid-dates", "inv", [("date", other_date(1350, 1, 1))], [], ("document", "date"))
    # 11-12: unexpected quantities, on the product @qProd so that only this record touches it.
    if line_prd and prod is not None and prod.col("key") and line_qty:
        doc_record(plan, 11, "unexpected-quantities", "inv", [], [("productKey", "@qProd"), ("quantity", "0")],
                   ("line", "quantity"))
        doc_record(plan, 12, "unexpected-quantities", "ret", [], [("productKey", "@qProd"), ("quantity", "-3")],
                   ("line", "quantity"))
    else:
        plan.skip(11, "unexpected-quantities", "product or quantity is not mapped", line_obj)
        plan.skip(12, "unexpected-quantities", "product or quantity is not mapped", line_obj)
    # 13-14: amounts on the line (only when the column is mapped).
    if line_disc and line_gross:
        doc_record(plan, 13, "inconsistent-amounts", "inv", [],
                   [("lineDiscount", f"{br(line_gross)} + 1")], ("line", "discount"))
    else:
        plan.skip(13, "inconsistent-amounts", "purchaseLine.lineDiscount is not mapped", line_obj)
    if line_vat:
        doc_record(plan, 14, "inconsistent-amounts", "inv", [], [("vat", "-500")], ("line", "vat"))
    else:
        plan.skip(14, "inconsistent-amounts", "purchaseLine.vat is not mapped", line_obj)
    # 15: a product with an unknown category and the distinct name SimDqProduct-BR3 (BR3).
    if cat is not None and cat.obj and cat.col("key") and prod is not None and prod.col("categoryKey"):
        product_record(plan, 15, 1, "broken-relationships", nstr("SimDqProduct-BR3"), True, 65)
    else:
        plan.skip(15, "broken-relationships", "product category master or product.categoryKey is not mapped", None)
    # 16: the document discount is above the sum of its lines (AM3); the line itself stays consistent.
    if doc_disc:
        line_net = (f"(SELECT l.{br(line_gross)} - ISNULL(l.{br(line_disc)}, 0) FROM {qualified(line_obj)} AS l "
                    f"WHERE l.{br(line_key)} = @l16) + 1000") if line_disc else \
                   (f"(SELECT l.{br(line_gross)} FROM {qualified(line_obj)} AS l WHERE l.{br(line_key)} = @l16) + 1000")
        doc_record(plan, 16, "inconsistent-amounts", "inv", [("docDiscount", line_net)], [], ("document", "document-discount"))
    else:
        plan.skip(16, "inconsistent-amounts", "purchaseInvoice.docDiscount is not mapped", inv_obj)

    # Phase 6 (spec 008 T027, FR-031): flagged payable / cheque / payment rows. Admin -> Data quality lists
    # them after the next sync; the Financials figures keep computing from the same copy (missing vendor rows
    # simply do not join a vendor). All names come from the map; every record is guarded at run time.
    pay_doc, pay_chq, pay_pay = ctx.pay_doc, ctx.pay_chq, ctx.pay_pay
    vendor_master = vend is not None and vend.obj and vend.col("key")

    financial_record(
        plan, 17, pay_doc, "@tPayDoc", "@kPayDoc", "missing-values",
        [f"{br(pay_doc.col('vendorKey'))} = NULL"] if pay_doc and pay_doc.col("vendorKey") else [],
        "payable document without a vendor",
        needs=["vendorKey"], nullable_item="vendorKey")
    if vendor_master and pay_chq is not None and pay_chq.col("vendorKey"):
        financial_record(
            plan, 18, pay_chq, "@tChq", "@kChq", "broken-relationships",
            [f"{br(pay_chq.col('vendorKey'))} = @noVendor"],
            "payable cheque pointing at no vendor master",
            needs=["vendorKey"])
    else:
        plan.skip(18, "broken-relationships", "vendor master or payableCheque.vendorKey is not mapped",
                  pay_chq.obj if pay_chq else None)
    financial_record(
        plan, 19, pay_doc, "@tPayDoc", "@kPayDoc", "invalid-dates",
        [f"{br(pay_doc.col('date'))} = {ctx.date_literal_for(pay_doc, (1499, 1, 1))}"] if pay_doc and pay_doc.col("date") else [],
        "payable document dated far in the future",
        needs=["date"])
    financial_record(
        plan, 20, pay_chq, "@tChq", "@kChq", "invalid-dates",
        [f"{br(pay_chq.col('date'))} = {ctx.date_literal_for(pay_chq, (1350, 1, 1))}"] if pay_chq and pay_chq.col("date") else [],
        "payable cheque dated before the earliest plausible date",
        needs=["date"])
    financial_record(
        plan, 21, pay_pay, "@tPay", "@kPay", "invalid-dates",
        [f"{br(pay_pay.col('date'))} = {ctx.date_literal_for(pay_pay, (1499, 1, 1))}"] if pay_pay and pay_pay.col("date") else [],
        "vendor payment dated far in the future",
        needs=["date"])
    if pay_chq is not None and pay_chq.col("amount") and pay_chq.col("remainingAmount"):
        financial_record(
            plan, 22, pay_chq, "@tChq", "@kChq", "inconsistent-amounts",
            [f"{br(pay_chq.col('amount'))} = 5000000",
             f"{br(pay_chq.col('remainingAmount'))} = 6000000"],
            "payable cheque remaining above its face amount",
            needs=["amount", "remainingAmount"])
    else:
        plan.skip(22, "inconsistent-amounts", "payableCheque.amount or remainingAmount is not mapped",
                  pay_chq.obj if pay_chq else None)
    if pay_pay is not None and pay_pay.col("number") and pay_pay.col("vendorKey"):
        # The clone repeats the template payment's (vendor, reference): DU2 keeps the template and flags the
        # clone. Guarded so a template with an empty reference or no vendor is skipped, not miscounted.
        pay_tbl = qualified(pay_pay.obj)
        pay_key = pay_pay.col("key")
        pay_ref = pay_pay.col("number")
        pay_vendor = pay_pay.col("vendorKey")
        financial_record(
            plan, 23, pay_pay, "@tPay", "@kPay", "duplicate-records",
            [], "duplicate payment reference for the same vendor",
            needs=["number", "vendorKey"],
            guards=[f"EXISTS (SELECT 1 FROM {pay_tbl} AS tp WHERE tp.{br(pay_key)} = @tPay "
                    f"AND NULLIF(LTRIM(RTRIM(ISNULL(CAST(tp.{br(pay_ref)} AS nvarchar(200)), N''))), N'') IS NOT NULL "
                    f"AND tp.{br(pay_vendor)} IS NOT NULL)"])
    else:
        plan.skip(23, "duplicate-records", "vendorPayment.number or vendorPayment.vendorKey is not mapped",
                  pay_pay.obj if pay_pay else None)

    return plan


def render_apply(ctx, plan, source_name):
    lines = [
        "-- dq-apply.sql: SYNTHETIC data-quality bad records for the Mabna simulator (spec 005, FR-030, US4).",
        f"-- Generated by deploy/mabna-sim/dq_bad_records.py from {source_name}; Jalali date {ctx.date_text}.",
        "-- Run against the simulator only, by hand:  sqlcmd -b -S 127.0.0.1,14330 -U sa -i dq-apply.sql",
        "-- Zharfa never runs this script. Revert with dq-revert.sql before applying again.",
        ":on error exit",
        f"USE {br(ctx.database)};",
        "SET XACT_ABORT ON;",
        "SET NOCOUNT ON;",
        "BEGIN TRAN;",
        "IF OBJECT_ID(N'dbo.SimDqSeed') IS NULL",
        "    CREATE TABLE dbo.SimDqSeed (seq int IDENTITY(1,1) NOT NULL PRIMARY KEY, object sysname NOT NULL, "
        "key_value nvarchar(100) NOT NULL, kind nvarchar(20) NOT NULL, issue_type nvarchar(40) NULL, note nvarchar(200) NULL);",
        "IF EXISTS (SELECT 1 FROM dbo.SimDqSeed)",
        "    RAISERROR(N'dq records already applied; run dq-revert.sql first', 16, 1);",
        "GO",
        "CREATE PROCEDURE #dq_clone",
        "    @object sysname, @keyColumn sysname, @templateKey nvarchar(100), @newKey nvarchar(100),",
        "    @extraCol sysname = NULL, @extraVal nvarchar(100) = NULL",
        "AS",
        "BEGIN",
        "    SET NOCOUNT ON;",
        "    DECLARE @obj nvarchar(400) = QUOTENAME(PARSENAME(@object, 2)) + N'.' + QUOTENAME(PARSENAME(@object, 1));",
        "    DECLARE @cols nvarchar(max), @vals nvarchar(max), @sql nvarchar(max), @hasIdentityKey bit = 0;",
        "    SELECT @cols = STUFF((",
        "        SELECT N', ' + QUOTENAME(c.name)",
        "        FROM sys.columns AS c",
        "        WHERE c.object_id = OBJECT_ID(@obj) AND c.is_computed = 0 AND c.system_type_id <> 189",
        "          AND (c.is_identity = 0 OR c.name = @keyColumn)",
        "        ORDER BY c.column_id",
        "        FOR XML PATH(N'')",
        "    ), 1, 2, N'');",
        "    SELECT @vals = STUFF((",
        "        SELECT N', ' + CASE",
        "            WHEN c.name = @keyColumn THEN N'@newKey'",
        "            WHEN @extraCol IS NOT NULL AND c.name = @extraCol THEN N'@extraVal'",
        "            WHEN c.system_type_id = 36 THEN N'NEWID()'",
        "            ELSE QUOTENAME(c.name)",
        "        END",
        "        FROM sys.columns AS c",
        "        WHERE c.object_id = OBJECT_ID(@obj) AND c.is_computed = 0 AND c.system_type_id <> 189",
        "          AND (c.is_identity = 0 OR c.name = @keyColumn)",
        "        ORDER BY c.column_id",
        "        FOR XML PATH(N'')",
        "    ), 1, 2, N'');",
        "    SELECT @hasIdentityKey = c.is_identity FROM sys.columns AS c",
        "    WHERE c.object_id = OBJECT_ID(@obj) AND c.name = @keyColumn;",
        "    SET @sql = N'INSERT INTO ' + @obj + N' (' + @cols + N') SELECT ' + @vals + N' FROM ' + @obj",
        "             + N' WHERE ' + QUOTENAME(@keyColumn) + N' = @templateKey;';",
        "    IF @hasIdentityKey = 1",
        "        SET @sql = N'SET IDENTITY_INSERT ' + @obj + N' ON; ' + @sql + N' SET IDENTITY_INSERT ' + @obj + N' OFF;';",
        "    EXEC sp_executesql @sql, N'@templateKey nvarchar(100), @newKey nvarchar(100), @extraVal nvarchar(100)',",
        "         @templateKey = @templateKey, @newKey = @newKey, @extraVal = @extraVal;",
        "END;",
        "GO",
        "-- Step 4: constraints off on the objects that the records touch (re-enabled WITH NOCHECK in step 7).",
    ]
    objects = []
    for obj in [ctx.inv.obj, ctx.line.obj] + ([ctx.ret.obj] if ctx.ret else []):
        if obj not in objects:
            objects.append(obj)
    for obj in plan.financial_objects:
        if obj not in objects:
            objects.append(obj)
    for obj in objects:
        lines.append(f"ALTER TABLE {qualified(obj)} NOCHECK CONSTRAINT ALL;")
    lines.append("-- Steps 5-6: templates, keys and the records (kind flagged = expected count, support = supporting rows).")
    lines.append(plan_text(plan))
    lines.append("-- Step 7: constraints back on, WITH NOCHECK (the data stays as seeded).")
    for obj in objects:
        lines.append(f"ALTER TABLE {qualified(obj)} WITH NOCHECK CHECK CONSTRAINT ALL;")
    lines += [
        "COMMIT;",
        "GO",
        "-- Step 8: manifest (expected seeded counts) and skipped records.",
        "SELECT issue_type, COUNT(*) AS seeded FROM dbo.SimDqSeed WHERE kind = 'flagged' GROUP BY issue_type ORDER BY issue_type;",
        "SELECT note AS skipped_record FROM dbo.SimDqSeed WHERE kind = 'skipped' ORDER BY seq;",
        "",
    ]
    return "\n".join(lines)


def plan_text(plan):
    return "\n".join(plan.body)


def render_revert(ctx, plan=None):
    lines = [
        "-- dq-revert.sql: removes every row seeded by dq-apply.sql and drops dbo.SimDqSeed (spec 005, FR-030, US4).",
        "-- Run against the simulator only, by hand:  sqlcmd -b -S 127.0.0.1,14330 -U sa -i dq-revert.sql",
        ":on error exit",
        f"USE {br(ctx.database)};",
        "SET XACT_ABORT ON;",
        "SET NOCOUNT ON;",
        "IF OBJECT_ID(N'dbo.SimDqSeed') IS NULL",
        "    RAISERROR(N'nothing to revert: dbo.SimDqSeed does not exist', 16, 1);",
        "BEGIN TRAN;",
        "-- Lines first, then documents, then products (records 8 and 15 are product rows).",
    ]
    order = [ctx.line]
    for entity in [ctx.inv, ctx.ret]:
        if entity is not None:
            order.append(entity)
    for entity in [ctx.product]:
        if entity is not None and entity.col("key"):
            order.append(entity)
    if plan is not None:
        for entity in (ctx.pay_doc, ctx.pay_chq, ctx.pay_pay, ctx.pay_alloc):
            if entity is not None and entity.obj in plan.financial_objects:
                order.append(entity)
    seen = set()
    for entity in order:
        if entity.obj in seen or entity is None or not entity.col("key"):
            continue
        seen.add(entity.obj)
        key = entity.col("key")
        lines.append(
            f"DELETE FROM {qualified(entity.obj)} WHERE CAST({br(key)} AS nvarchar(100)) IN "
            f"(SELECT key_value FROM dbo.SimDqSeed WHERE object = {nstr(entity.obj)});")
    lines += [
        "DROP TABLE dbo.SimDqSeed;",
        "COMMIT;",
        "",
    ]
    return "\n".join(lines)


def load_map(path):
    try:
        with open(path, encoding="utf-8") as handle:
            return json.load(handle)
    except OSError as error:
        raise MapError(f"cannot read the source map: {error.strerror}") from None
    except json.JSONDecodeError as error:
        raise MapError(f"the source map is not valid JSON (line {error.lineno})") from None


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--map", required=True, help="purchase source map JSON (generated or the tracked example)")
    parser.add_argument("--date", required=True, help="Jalali date of the bad documents, yyyy/mm/dd (test month)")
    parser.add_argument("--out", default="deploy/mabna-sim/generated/dq", help="output folder (gitignored)")
    parser.add_argument("--database", default="TotalSystem", help="simulator database name")
    args = parser.parse_args(argv)

    try:
        ctx = Ctx(load_map(args.map), args.date, args.database)
        plan = build_plan(ctx)
    except MapError as error:
        print(f"dq_bad_records: {error}", file=sys.stderr)
        return 2

    os.makedirs(args.out, exist_ok=True)
    apply_path = os.path.join(args.out, "dq-apply.sql")
    revert_path = os.path.join(args.out, "dq-revert.sql")
    with open(apply_path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(render_apply(ctx, plan, os.path.basename(args.map)))
    with open(revert_path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(render_revert(ctx, plan))

    print(f"wrote {apply_path}")
    print(f"wrote {revert_path}")
    print("expected flagged counts:")
    for issue in sorted(plan.expected):
        print(f"  {issue}: {plan.expected[issue]}")
    for number, issue, reason in plan.skipped:
        print(f"  skipped record {number} ({issue}): {reason}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
