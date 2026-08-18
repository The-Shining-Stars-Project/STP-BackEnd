#!/usr/bin/env python3
"""
build_review_sheet.py  —  one-off, throwaway.  NOT part of the application.

Turns the messy "Client (stars) Contact List" workbook into ONE clean, review-ready
worksheet that a person who knows the families signs off on before anyone types records
into the CRM.  The machine does the tedium it is good at — surfacing hidden rows, exposing
shifted cells, cross-checking both anaphylaxis signals, gathering one person's scattered
rows together.  Every genuine judgement is left to the reviewer, marked, never guessed.

It does NOT write to the database.  Its only output is an .xlsx for human eyes.

Design rules (each earned from a specific failure the review would otherwise cause):
  * Read every sheet BY POSITION.  Headers are only used to assert the expected layout,
    because the known corruption is rows pasted under the wrong layout — a header-driven
    reader silently loses the cells that moved past the header.
  * Shifted rows are FLAGGED with every raw cell exposed, and their fields left blank, so
    nothing mis-mapped can slip through and nothing is dropped.  A human realigns them.
  * Cluster by PERSON, not by row.  One output row per person, listing every source row,
    with text fields unioned so the richest note is never discarded.
  * Never synthesise a day-of-month.  Month-name / malformed dates ship raw, flagged.
  * Both anaphylaxis signals are checked: a '*' on the first name AND allergy/epipen prose.
  * Blank allergies get an explicit provenance note — "empty at source, not verified" —
    so "we asked and there are none" never looks the same as "nobody has checked".

Usage:
    python3 build_review_sheet.py
Writes:  <repo>/../xls/REVIEW - Stars to enter.xlsx  (override with a 2nd argument)
Prints:  counts only.  Never prints a name, phone, allergy, or any other personal value.
"""

import os
import re
import datetime as dt
from collections import defaultdict

import openpyxl
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side

# Paths default to the workbook folder beside the repo; override with arguments so the
# tool is not tied to one machine's layout:
#   python3 build_review_sheet.py <source workbook> [output workbook]
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_XLS = os.path.abspath(os.path.join(HERE, "..", "..", "xls"))
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    DEFAULT_XLS, "Client (stars)Contact List (1).xlsx")
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(
    DEFAULT_XLS, "REVIEW - Stars to enter.xlsx")

# ---- canonical field -> column INDEX, per sheet layout (0-based) -------------
# Established by reading each sheet's header row; hard-coded so a shifted data row
# is measured against the layout it was SUPPOSED to have.
LAYOUTS = {
    "active_mjc": {  # Clients-MJC, Clients-Manteca PT
        "last": 0, "first": 1, "phone": 2, "parent": 3, "email": 4,
        "sc_name": 5, "sc_email": 6, "remind": 7, "intake": 8, "start": 9,
        "shirt": 11, "dob": 12, "pos": 13, "ipp": 14, "allergy": 15,
        "areas": 16, "age": 17, "last_col": 19,
    },
    "active_pathways": {  # Clients-Pathways (extra ISP date columns push allergy right)
        "last": 0, "first": 1, "phone": 2, "parent": 3, "email": 4,
        "sc_name": 5, "sc_email": 6, "remind": 7, "intake": 8, "start": 9,
        "shirt": 11, "dob": 12, "pos": 13, "ipp": 14, "allergy": 18,
        "areas": 19, "age": None, "last_col": 19,
    },
    "prospective": {  # the three Prospective sheets
        "last": 0, "first": 1, "phone": 2, "parent": 3, "email": 4,
        "sc_name": 5, "sc_email": 6, "sc_phone": 7, "intake": 8, "shirt": 9,
        "dob": 10, "pos": 11, "ipp": 12, "allergy": 13, "areas": 14,
        "age": None, "last_col": 14,
    },
    "former": {  # Former Clients
        "last": 0, "first": 1, "phone": 2, "parent": 3, "email": 4,
        "sc_name": 5, "sc_email": 6, "remind": 7, "intake": 8, "shirt": 9,
        "dob": 10, "pos": 11, "ipp": 12, "allergy": 13, "areas": 14,
        "age": 15, "last_col": 17,
    },
}

# sheet name -> (layout, candidate status, what "hidden" means on this sheet)
SHEETS = {
    "Clients-MJC":            ("active_mjc",      "Active",      "departed"),
    "Clients-Pathways":       ("active_pathways", "Active",      "departed"),
    "Clients-Manteca PT":     ("active_mjc",      "Active",      "departed"),
    "Prospective Clients MJC":      ("prospective", "Prospective", "converted"),
    "Prospective Clients Pathways": ("prospective", "Prospective", "converted"),
    "Manteca PT Prospective Clts":  ("prospective", "Prospective", "converted"),
    "Former Clients":         ("former",          "Former",      "departed"),
}
# 'Volunteer Students', 'Tracking list No trial yet', 'No Longer InterestedUnreachable'
# are deliberately NOT imported — see the note printed at the end.

# Program is not a column in the workbook; it is implied by which sheet a row sits on.
# Former Clients carries no programme at all, so it is left blank for the reviewer.
SHEET_PROGRAM = {
    "Clients-MJC": "mjc",
    "Prospective Clients MJC": "mjc",
    "Clients-Pathways": "pathways",
    "Prospective Clients Pathways": "pathways",
    "Clients-Manteca PT": "manteca",
    "Manteca PT Prospective Clts": "manteca",
    "Former Clients": "",
}

ANAPHYLAXIS_PROSE = re.compile(r"anaphyla|epi\s*-?\s*pen|epipen|albuterol|inhaler", re.I)
CONDITION_VOCAB = re.compile(
    r"seizure|epilep|asthma|diabet|\bTBI\b|cardiac|anxiety|surger|autism", re.I)
SHIRT_MAP = {
    "ys": "YS", "ym": "YM", "yl": "YL", "s": "S", "m": "M", "l": "L",
    "xl": "XL", "2xl": "2XL", "youth s": "YS", "youth m": "YM", "youth l": "YL",
    "adult s": "S", "adult m": "M", "adult l": "L",
}


def norm(v):
    """Trim a cell to a clean string; None/blank -> ''."""
    if v is None:
        return ""
    if isinstance(v, dt.datetime):
        return v.strftime("%Y-%m-%d")
    if isinstance(v, dt.date):
        return v.strftime("%Y-%m-%d")
    return str(v).strip()


def person_key(last, first):
    """Cluster key: lowercased, asterisks/quotes/whitespace stripped."""
    def clean(s):
        s = re.sub(r'[\*"“”]', "", s or "")
        return re.sub(r"\s+", " ", s).strip().lower()
    return (clean(last), clean(first))


def looks_like_date(v):
    return isinstance(v, (dt.datetime, dt.date))


def coerce_date(v):
    """Return ('YYYY-MM-DD', clean) or (raw_text, 'REVIEW') — never invents a day."""
    if v is None or (isinstance(v, str) and not v.strip()):
        return ("", "ok")
    if looks_like_date(v):
        return (norm(v), "ok")
    txt = str(v).strip()
    if re.fullmatch(r"\d{4}-\d{2}-\d{2}", txt):
        return (txt, "ok")
    return (txt, "review")  # month-name, "07/312026", "*", "/", etc.


def row_is_shifted(cells, layout):
    """Content past the last header column, OR a real date sitting in a text column."""
    last = layout["last_col"]
    for j in range(last + 1, len(cells)):
        if norm(cells[j]):
            return True
    for f in ("age", "allergy", "areas"):
        idx = layout.get(f)
        if idx is not None and idx < len(cells) and looks_like_date(cells[idx]):
            return True
    return False


def main():
    wb = openpyxl.load_workbook(SRC, data_only=True)

    # gather every source row, tagged with sheet / hidden / shift, keyed by person
    people = defaultdict(list)
    shifted_rows = []          # (sheet, excel_row, [raw cells])
    volunteer_rows = 0
    for sheet_name, (layout_key, status, hidden_meaning) in SHEETS.items():
        if sheet_name not in wb.sheetnames:
            continue
        ws = wb[sheet_name]
        layout = LAYOUTS[layout_key]
        for r in range(2, ws.max_row + 1):
            dim = ws.row_dimensions.get(r)
            hidden = bool(dim and dim.hidden)
            cells = [ws.cell(row=r, column=c).value for c in range(1, ws.max_column + 1)]
            last = norm(cells[layout["last"]]) if layout["last"] < len(cells) else ""
            first = norm(cells[layout["first"]]) if layout["first"] < len(cells) else ""
            if not last and not first:
                continue  # blank/separator row
            rec = {
                "sheet": sheet_name, "excel_row": r, "hidden": hidden,
                "hidden_meaning": hidden_meaning, "status": status,
                "cells": cells, "layout": layout,
            }
            if row_is_shifted(cells, layout):
                shifted_rows.append((sheet_name, r, [norm(c) for c in cells if norm(c)]))
                rec["shifted"] = True
            else:
                rec["shifted"] = False
            people[person_key(last, first)].append(rec)

    # volunteer students – counted and reported, never imported as clients
    if "Volunteer Students" in wb.sheetnames:
        vs = wb["Volunteer Students"]
        for r in range(2, vs.max_row + 1):
            if any(norm(vs.cell(row=r, column=c).value) for c in range(1, 4)):
                volunteer_rows += 1

    # ---- build one review record per person --------------------------------
    out_rows = []
    tally = defaultdict(int)
    for key, recs in people.items():
        flags = []
        sources = []
        anaphylaxis_signal = []

        def collect(field):
            vals = []
            for rec in recs:
                lay = rec["layout"]
                idx = lay.get(field)
                if idx is None or idx >= len(rec["cells"]):
                    continue
                if rec["shifted"]:
                    continue  # never trust a mapped field on a shifted row
                v = norm(rec["cells"][idx])
                if v:
                    vals.append((v, f"{rec['sheet']} r{rec['excel_row']}"))
            return vals

        # provenance / source rows
        for rec in recs:
            tag = f"{rec['sheet']} r{rec['excel_row']}"
            if rec["hidden"]:
                tag += f" [hidden={rec['hidden_meaning']}]"
            if rec["shifted"]:
                tag += " [SHIFTED]"
            sources.append(tag)

        if any(r["shifted"] for r in recs):
            flags.append("SHIFT_REVIEW")

        # status: contradiction across sheets?
        statuses = {r["status"] for r in recs}
        # a hidden 'Active' row actually means departed
        for rec in recs:
            if rec["hidden"] and rec["status"] == "Active":
                statuses.discard("Active")
                statuses.add("Former(hidden)")
        status_val = " / ".join(sorted(statuses))
        if len(statuses) > 1:
            flags.append("STATUS_REVIEW")

        # anaphylaxis: '*' on any first-name cell, or prose in any allergy cell
        for rec in recs:
            lay = rec["layout"]
            fn = norm(rec["cells"][lay["first"]]) if lay["first"] < len(rec["cells"]) else ""
            if fn.rstrip().endswith("*"):
                anaphylaxis_signal.append(f"name-asterisk ({rec['sheet']} r{rec['excel_row']})")
            if not rec["shifted"]:
                al = lay.get("allergy")
                av = norm(rec["cells"][al]) if al is not None and al < len(rec["cells"]) else ""
                if av and ANAPHYLAXIS_PROSE.search(av):
                    anaphylaxis_signal.append(f"allergy-prose ({rec['sheet']} r{rec['excel_row']})")

        # union text fields
        def union(field):
            seen, out = set(), []
            for v, src in collect(field):
                if v.lower() not in seen:
                    seen.add(v.lower())
                    out.append((v, src))
            return out

        allergy_u = union("allergy")
        areas_u = union("areas")
        if len({v.lower() for v, _ in allergy_u}) > 1:
            flags.append("ALLERGY_CONFLICT")
        if len({v.lower() for v, _ in areas_u}) > 1:
            flags.append("AREAS_CONFLICT")

        allergy_text = " || ".join(f"{v}  [{s}]" for v, s in allergy_u)
        areas_text = " || ".join(f"{v}  [{s}]" for v, s in areas_u)

        if anaphylaxis_signal:
            flags.append("ANAPHYLAXIS_REVIEW")
        # condition filed as allergy?
        if any(CONDITION_VOCAB.search(v) for v, _ in allergy_u):
            flags.append("FIELD_PLACEMENT_REVIEW")
        # blank allergy provenance
        if not allergy_u:
            allergy_text = "(empty at source — NOT verified with guardian at import)"
            flags.append("ALLERGY_BLANK")

        # dates (first usable value across rows, never day-synthesised)
        def first_date(field):
            for rec in recs:
                if rec["shifted"]:
                    continue
                lay = rec["layout"]
                idx = lay.get(field)
                if idx is None or idx >= len(rec["cells"]):
                    continue
                raw = rec["cells"][idx]
                val, state = coerce_date(raw)
                if val:
                    if state == "review":
                        return (val, True)
                    return (val, False)
            return ("", False)

        dob, dob_rev = first_date("dob")
        pos, pos_rev = first_date("pos")
        ipp, ipp_rev = first_date("ipp")
        if dob_rev or pos_rev or ipp_rev:
            flags.append("DATE_REVIEW")

        # start date — never silently default; flag if none found
        start_vals = collect("start")
        start_val = start_vals[0][0] if start_vals else "(none in source — needs a real date)"
        if not start_vals and "Active" in statuses:
            flags.append("STARTDATE_MISSING")

        # shirt: map when unambiguous, else raw into notes
        shirt_out, shirt_raw = "", ""
        sv = collect("shirt")
        if sv:
            raw = sv[0][0]
            k = re.sub(r"[^a-z0-9 ]", "", raw.lower()).strip()
            shirt_out = SHIRT_MAP.get(k, "")
            if not shirt_out:
                shirt_raw = raw  # 3XL, "M (Youth)", etc. -> notes, review by eye

        # programme(s), from the sheets this person appears on
        progs = []
        for rec in recs:
            sl = SHEET_PROGRAM.get(rec["sheet"], "")
            if sl and sl not in progs:
                progs.append(sl)
        primary = progs[0] if progs else ""
        secondary = progs[1] if len(progs) > 1 else ""
        if not primary:
            flags.append("PROGRAM_REVIEW")
        if len(progs) > 2:
            flags.append("PROGRAM_REVIEW")

        last, first = key
        notes = []
        for v, s in collect("phone"):
            notes.append(f"contact: {v} [{s}]")
        if shirt_raw:
            notes.append(f"shirt (raw, unmapped): {shirt_raw}")
        if anaphylaxis_signal:
            notes.append("anaphylaxis signals: " + "; ".join(sorted(set(anaphylaxis_signal))))

        out_rows.append({
            "flags": ";".join(flags) if flags else "",
            "last": last.title(), "first": first.title(),
            "status": status_val, "program": primary, "secondary": secondary,
            "dob": dob, "pos": pos, "ipp": ipp,
            "start": start_val,
            "allergy": allergy_text, "areas": areas_text,
            "guardian": " || ".join(f"{v} [{s}]" for v, s in union("parent")),
            "gphone":   " || ".join(f"{v} [{s}]" for v, s in union("phone")),
            "gemail":   " || ".join(f"{v} [{s}]" for v, s in union("email")),
            "sc_name":  " || ".join(f"{v} [{s}]" for v, s in union("sc_name")),
            "shirt": shirt_out,
            "notes": " | ".join(notes),
            "sources": " ; ".join(sources),
        })
        for f in flags:
            tally[f] += 1

    # ---- write the review workbook -----------------------------------------
    out = openpyxl.Workbook()
    ws = out.active
    ws.title = "Review"
    cols = [
        ("Review flags", 26), ("Last name", 16), ("First name", 14), ("Status", 18),
        ("Program", 12), ("Secondary program", 16),
        ("DOB", 12), ("POS exp", 12), ("IPP exp", 12), ("Start date", 20),
        ("Allergies", 46), ("Anaphylactic?", 14), ("Areas of concern", 40),
        ("Guardian", 30), ("Guardian phone", 28), ("Guardian email", 26),
        ("Service coordinator", 24), ("Shirt", 8), ("Notes", 50), ("Source rows", 44),
    ]
    head_fill = PatternFill("solid", fgColor="1F4E79")
    head_font = Font(color="FFFFFF", bold=True, size=10)
    flag_fill = PatternFill("solid", fgColor="F6EFE2")
    ana_fill = PatternFill("solid", fgColor="F7EAEA")
    thin = Side(style="thin", color="DDDDDD")
    border = Border(bottom=thin)
    for c, (title, w) in enumerate(cols, 1):
        cell = ws.cell(row=1, column=c, value=title)
        cell.fill, cell.font = head_fill, head_font
        cell.alignment = Alignment(vertical="top", wrap_text=True)
        ws.column_dimensions[openpyxl.utils.get_column_letter(c)].width = w
    ws.freeze_panes = "A2"

    out_rows.sort(key=lambda r: (r["status"], r["last"], r["first"]))
    for i, r in enumerate(out_rows, start=2):
        vals = [r["flags"], r["last"], r["first"], r["status"], r["program"], r["secondary"],
                r["dob"], r["pos"],
                r["ipp"], r["start"], r["allergy"],
                "REVIEW" if "ANAPHYLAXIS_REVIEW" in r["flags"] else "",
                r["areas"], r["guardian"], r["gphone"], r["gemail"], r["sc_name"],
                r["shirt"], r["notes"], r["sources"]]
        for c, v in enumerate(vals, 1):
            cell = ws.cell(row=i, column=c, value=v)
            cell.alignment = Alignment(vertical="top", wrap_text=True)
            cell.border = border
            cell.font = Font(size=10)
        if "ANAPHYLAXIS_REVIEW" in r["flags"]:
            for c in range(1, len(cols) + 1):
                ws.cell(row=i, column=c).fill = ana_fill
        elif r["flags"]:
            ws.cell(row=i, column=1).fill = flag_fill

    # summary sheet
    s = out.create_sheet("Summary")
    s.column_dimensions["A"].width = 30
    s.column_dimensions["B"].width = 10
    s.append(["Review sheet — what to check", ""])
    s.append(["People to review / enter", len(out_rows)])
    s.append(["Shifted rows exposed", len(shifted_rows)])
    s.append(["Volunteer-student rows (not imported)", volunteer_rows])
    s.append(["", ""])
    s.append(["Flag", "Count"])
    for f in sorted(tally):
        s.append([f, tally[f]])
    s.cell(row=1, column=1).font = Font(bold=True, size=12)
    s.cell(row=6, column=1).font = Font(bold=True)
    s.cell(row=6, column=2).font = Font(bold=True)

    # a tab that lays out every shifted row's raw cells for hand-realignment
    sr = out.create_sheet("Shifted rows (raw)")
    sr.append(["Sheet", "Excel row", "Raw non-empty cells, left to right"])
    for c in range(1, 4):
        sr.cell(row=1, column=c).font = Font(bold=True)
    for sheet_name, excel_row, raw in shifted_rows:
        sr.append([sheet_name, excel_row, "  |  ".join(raw)])
    sr.column_dimensions["A"].width = 22
    sr.column_dimensions["C"].width = 120

    out.save(OUT)

    # ---- counts only.  No personal values in stdout. -----------------------
    print("Review workbook written:", os.path.basename(OUT))
    print(f"  people to review:            {len(out_rows)}")
    print(f"  shifted rows exposed:        {len(shifted_rows)}")
    print(f"  volunteer rows (excluded):   {volunteer_rows}")
    print("  flags raised:")
    for f in sorted(tally):
        print(f"    {f:24} {tally[f]}")
    print()
    print("NOT imported (kept in the spreadsheet, entered by hand as people convert):")
    print("  Tracking list, No-Longer-Interested, Volunteer Students.")


if __name__ == "__main__":
    main()
