
import re
import pandas as pd
import streamlit as st
import pdfplumber

st.set_page_config(page_title="Fuel Audit Control Centre V37", layout="wide")
st.title("Fuel Audit Control Centre")
st.caption("V37 - Executive dashboard: branch KPI cards, exposure summaries, recovery rates, review workload, and operational insights.")
st.warning("Do not click Streamlit's Deploy button. This runs locally only.")

# ---------------- Helpers ----------------
def clean_rego(value):
    if pd.isna(value):
        return ""
    return re.sub(r"[^A-Z0-9]", "", str(value).upper().strip())

def ra_digits(value):
    if pd.isna(value):
        return ""
    if isinstance(value, (int, float)):
        try:
            return str(int(value))
        except Exception:
            pass
    text = str(value).strip()
    if re.match(r"^\d+\.0$", text):
        return text.split(".")[0]
    return re.sub(r"\D", "", text)

def extract_ra(value):
    raw = ra_digits(value)
    match = re.search(r"\b(\d{7,10})\b", raw)
    return match.group(1) if match else ""

def normalise_cars_ra(value):
    raw = ra_digits(value)
    return raw[:-1] if len(raw) >= 8 else raw

def parse_amount(value):
    try:
        return float(str(value).replace("$", "").replace(",", "").strip())
    except Exception:
        return 0.0

def excel_serial_to_date(value):
    try:
        if pd.isna(value):
            return pd.NaT
        if isinstance(value, (int, float)) or str(value).strip().isdigit():
            v = float(value)
            if v > 20000:
                return pd.to_datetime(v, unit="D", origin="1899-12-30", errors="coerce")
        return pd.to_datetime(value, errors="coerce", dayfirst=True)
    except Exception:
        return pd.NaT

def read_pdf_text(file):
    chunks = []
    with pdfplumber.open(file) as pdf:
        for page in pdf.pages:
            chunks.append(page.extract_text() or "")
    return "\n".join(chunks)

def find_column(cols, keywords):
    for c in cols:
        lc = str(c).lower().strip()
        if any(k in lc for k in keywords):
            return c
    return None

def normalise_branch(value):
    """
    Normalises supplier site names and Excel worksheet tab names.
    This makes names like 'Mobile - Taupo', 'Mobil Taupo', 'Taupo' all report as Taupo.
    """
    raw = str(value).strip()
    text = raw.lower().replace("_", " ").replace("-", " ")

    if "taupo" in text or "mobile" in text or "mobil" in text:
        return "Taupo"
    if "kerikeri" in text or text.strip() == "kke":
        return "Kerikeri"
    if "whangarei" in text or text.strip() == "whn":
        return "Whangarei"
    if "rotorua" in text or "te ngae" in text:
        return "Rotorua"
    if "wanganui" in text or "whanganui" in text or "victoria ave" in text:
        return "Whanganui"
    if "mt maunganui" in text or "mount maunganui" in text or "hewletts" in text:
        return "Mt Maunganui"
    if "new plymouth" in text or "waiwhakaiho" in text:
        return "New Plymouth"

    return raw if raw else "Unknown"

def branch_options(*dfs):
    branches = set()
    for df in dfs:
        if df is not None and not df.empty and "Branch" in df.columns:
            branches.update([x for x in df["Branch"].dropna().astype(str).unique() if x and x != "nan"])
    return ["All Locations"] + sorted(branches)

def filter_branch(df, selected):
    if df is None or df.empty or selected == "All Locations" or "Branch" not in df.columns:
        return df
    return df[df["Branch"].astype(str) == selected].copy()

# ---------------- Supplier parsers ----------------
def parse_mobil_statement(file):
    text = read_pdf_text(file)
    rows = []
    current_card = ""
    current_holder = ""
    for line in text.splitlines():
        card = re.search(r"CARD NUMBER:\s*(\d+)\s*NAME:\s*(.+)", line)
        if card:
            current_card = card.group(1)
            current_holder = card.group(2).strip()
            continue

        m = re.match(r"^(\d{2}/\d{2}/\d{2})\s+(\d{2}:\d{2})\s+(.+?)\s+(\d{5,6})\s+(-?[0-9]+\.[0-9]+)L\s+", line)
        if m:
            litres = round(float(m.group(5)), 2)
            if litres <= 0:
                continue
            rows.append({
                "Supplier": "Mobil",
                "Branch": normalise_branch(current_holder or "Taupo"),
                "Cardholder": current_holder,
                "Supplier Date": pd.to_datetime(m.group(1), format="%d/%m/%y", errors="coerce"),
                "Supplier Time": m.group(2),
                "Supplier Site": m.group(3).strip(),
                "Supplier Voucher": m.group(4),
                "Supplier Litres": litres,
                "Supplier Amount $": 0.0,
                "Source File": getattr(file, "name", "")
            })
    return pd.DataFrame(rows)

def parse_farmlands_caltex_statement(file):
    """
    Parses Farmlands/Caltex PDF statement.

    V26 fix:
    - Reads fuel product lines with ANY product code, not just 1044.
    - This allows Diesel, 91, 95, 98 etc. to be parsed.
    - Ignores credit rows and negative litres.
    - De-duplicates repeated OCR/page-break lines.
    """
    text = read_pdf_text(file)
    lines = text.splitlines()
    rows = []
    seen = set()
    pending = None
    current_card = ""

    for line in lines:
        line = line.strip()
        if not line:
            continue

        # Transaction header line:
        # 01 Apr 26 100 Inv: 044313 Caltex Whangarei
        h = re.match(r"^(\d{2}\s+[A-Za-z]{3}\s+\d{2})\s+\d+\s+(Inv|Crd):\s+(\d+)\s+(.+)$", line)
        if h:
            pending = {
                "date": pd.to_datetime(h.group(1), format="%d %b %y", errors="coerce"),
                "type": h.group(2),
                "invoice": h.group(3),
                "site": h.group(4).strip()
            }
            current_card = ""
            continue

        c = re.search(r"Card number\s+(\d+)", line, flags=re.I)
        if c and pending:
            current_card = c.group(1)
            continue

        # Fuel detail line.
        # Old parser only allowed product code 1044.
        # New parser allows any 3-5 digit product code:
        # 1044 20.71 L Diesel 89.70 3.12 86.58 11.29
        # 1031 20.71 L Diesel ...
        g = re.match(
            r"^\d{3,5}\s+(-?[0-9]+\.[0-9]+)\s+L\s+(.+?)\s+(-?[0-9]+\.[0-9]+)\s+(-?[0-9]+\.[0-9]+)\s+(-?[0-9]+\.[0-9]+)\s+(-?[0-9]+\.[0-9]+)$",
            line,
            flags=re.I
        )

        if g and pending:
            litres = round(float(g.group(1)), 2)
            product = g.group(2).strip()
            gross = parse_amount(g.group(3))

            if pending["type"] != "Inv" or litres <= 0:
                pending = None
                continue

            key = (
                pending["date"],
                pending["invoice"],
                current_card,
                litres,
                gross,
                pending["site"],
                product
            )

            if key not in seen:
                seen.add(key)
                rows.append({
                    "Supplier": "Caltex/Farmlands",
                    "Branch": normalise_branch(pending["site"]),
                    "Cardholder": current_card,
                    "Supplier Date": pending["date"],
                    "Supplier Time": "",
                    "Supplier Site": pending["site"],
                    "Supplier Voucher": pending["invoice"],
                    "Supplier Product": product,
                    "Supplier Litres": litres,
                    "Supplier Amount $": gross,
                    "Source File": getattr(file, "name", "")
                })

            pending = None
            continue

        # Fallback: catches lines containing "20.71 L Diesel" even if the PDF text layout changes.
        if pending and re.search(r"\b-?[0-9]+\.[0-9]+\s+L\b", line, flags=re.I):
            m_litres = re.search(r"\b(-?[0-9]+\.[0-9]+)\s+L\b", line, flags=re.I)
            if m_litres:
                litres = round(float(m_litres.group(1)), 2)
                if pending["type"] == "Inv" and litres > 0:
                    after_l = line[m_litres.end():].strip()
                    product = after_l.split()[0] if after_l else ""
                    nums = re.findall(r"-?[0-9]+\.[0-9]+", after_l)
                    gross = parse_amount(nums[0]) if nums else 0.0

                    key = (
                        pending["date"],
                        pending["invoice"],
                        current_card,
                        litres,
                        gross,
                        pending["site"],
                        product
                    )

                    if key not in seen:
                        seen.add(key)
                        rows.append({
                            "Supplier": "Caltex/Farmlands",
                            "Branch": normalise_branch(pending["site"]),
                            "Cardholder": current_card,
                            "Supplier Date": pending["date"],
                            "Supplier Time": "",
                            "Supplier Site": pending["site"],
                            "Supplier Voucher": pending["invoice"],
                            "Supplier Product": product,
                            "Supplier Litres": litres,
                            "Supplier Amount $": gross,
                            "Source File": getattr(file, "name", "")
                        })

                pending = None

    return pd.DataFrame(rows)

def parse_supplier_file(file):
    name = getattr(file, "name", "").lower()
    if not name.endswith(".pdf"):
        return pd.DataFrame()
    # Read once and route based on text
    text_head = read_pdf_text(file)[:3000].lower()
    try:
        file.seek(0)
    except Exception:
        pass
    if "farmlands statement" in text_head or "caltex" in text_head:
        return parse_farmlands_caltex_statement(file)
    return parse_mobil_statement(file)

# ---------------- Branch workbook parser ----------------
def safe_branch_date(value):
    try:
        if pd.isna(value):
            return pd.NaT

        txt = str(value).strip()

        # Handles 1/4, 01/04 etc as 2026
        m = re.match(r"^(\d{1,2})/(\d{1,2})$", txt)
        if m:
            return pd.to_datetime(f"{m.group(1)}/{m.group(2)}/2026", dayfirst=True, errors="coerce")

        # Excel serial only in sensible range
        if isinstance(value, (int, float)) or txt.replace(".", "", 1).isdigit():
            v = float(value)
            if 30000 <= v <= 60000:
                d = pd.to_datetime(v, unit="D", origin="1899-12-30", errors="coerce")
                return d if pd.notna(d) and 2020 <= d.year <= 2035 else pd.NaT
            return pd.NaT

        d = pd.to_datetime(value, errors="coerce", dayfirst=True)
        if pd.isna(d):
            return pd.NaT
        if not (2020 <= d.year <= 2035):
            return pd.NaT
        return d
    except Exception:
        return pd.NaT

def looks_like_rego(value):
    sx = str(value).strip().upper()
    sx = re.sub(r"[^A-Z0-9]", "", sx)
    if not sx or sx.isdigit():
        return False
    return bool(re.search(r"[A-Z]", sx) and re.search(r"\d", sx) and 3 <= len(sx) <= 10)

def known_branch_layout(sheet_name):
    """
    Known layouts from the current Litres workbook:
    Taupo:     Rego A, RA C, Litres E, Date F
    Kerikeri: RA A, Litres C, Date F
    Whangarei: Litres C, RA E, Date G
    """
    b = normalise_branch(sheet_name)
    if b == "Taupo":
        return {"rego": 0, "ra": 2, "litres": 4, "date": 5}
    if b == "Kerikeri":
        return {"rego": None, "ra": 0, "litres": 2, "date": 5}
    if b == "Whangarei":
        return {"rego": None, "ra": 4, "litres": 2, "date": 6}
    return None

def auto_detect_layout(df):
    cols = list(df.columns)

    # RA column: most rows with 7-10 digit RA.
    ra_scores = {c: df[c].apply(lambda x: bool(extract_ra(x))).sum() for c in cols}
    ra_col = max(ra_scores, key=ra_scores.get) if max(ra_scores.values()) > 0 else None

    # Date column: most valid dates using safe parser.
    date_scores = {c: df[c].apply(lambda x: pd.notna(safe_branch_date(x))).sum() for c in cols}
    date_col = max(date_scores, key=date_scores.get) if max(date_scores.values()) > 0 else None

    # Rego column: most rego-like values.
    rego_scores = {c: df[c].apply(looks_like_rego).sum() for c in cols}
    rego_col = max(rego_scores, key=rego_scores.get) if max(rego_scores.values()) > 0 else None

    # Litres column:
    # Prefer numeric columns that are decimal-heavy and between 2L and 120L.
    best_col = None
    best_score = -999
    for c in cols:
        vals = pd.to_numeric(df[c], errors="coerce")
        valid = vals[(vals > 0) & (vals < 120)]
        if len(valid) == 0:
            continue
        decimal_count = valid.apply(lambda x: abs(float(x) - int(float(x))) > 0.001).sum()
        integer_count = len(valid) - decimal_count
        # Penalise likely class columns with lots of tiny integers 1-9
        small_int_count = valid.apply(lambda x: float(x).is_integer() and 0 < float(x) <= 10).sum()
        # Penalise columns with big odometer/time values
        big_count = vals.gt(120).sum()
        score = (decimal_count * 6) + len(valid) - (integer_count * 1.5) - (small_int_count * 3) - (big_count * 4)
        if score > best_score:
            best_score = score
            best_col = c

    return {"rego": rego_col, "ra": ra_col, "litres": best_col, "date": date_col}

def parse_branch_sheet_with_layout(file, sheet, source_name):
    try:
        df = pd.read_excel(file, sheet_name=sheet, header=None)
    except Exception:
        return pd.DataFrame()

    df = df.dropna(how="all").dropna(axis=1, how="all")
    if df.empty:
        return pd.DataFrame()

    layout = known_branch_layout(sheet)
    if layout is None:
        layout = auto_detect_layout(df)

    litres_col = layout.get("litres")
    ra_col = layout.get("ra")
    date_col = layout.get("date")
    rego_col = layout.get("rego")

    if litres_col is None or litres_col not in df.columns:
        return pd.DataFrame()

    out = pd.DataFrame()
    out["Branch"] = [normalise_branch(sheet)] * len(df)
    out["Rego"] = df[rego_col].apply(clean_rego) if rego_col is not None and rego_col in df.columns else ""
    out["Branch RA / Note"] = df[ra_col].astype(str).str.strip() if ra_col is not None and ra_col in df.columns else ""
    out["Extracted Branch RA"] = out["Branch RA / Note"].apply(extract_ra)
    out["Branch Date"] = df[date_col].apply(safe_branch_date) if date_col is not None and date_col in df.columns else pd.NaT
    out["Branch Litres"] = pd.to_numeric(df[litres_col], errors="coerce").round(2)
    out["Source File"] = [source_name] * len(df)
    out["Source Sheet"] = [sheet] * len(df)
    out["Detected Layout"] = [("Known" if known_branch_layout(sheet) is not None else "Auto")] * len(df)
    out["Detected Litres Column"] = [("" if litres_col is None else str(litres_col))] * len(df)
    out["Detected RA Column"] = [("" if ra_col is None else str(ra_col))] * len(df)
    out["Detected Date Column"] = [("" if date_col is None else str(date_col))] * len(df)
    out["Detected Rego Column"] = [("" if rego_col is None else str(rego_col))] * len(df)

    # Remove headers / non-fuel rows
    out = out[(out["Branch Litres"].notna()) & (out["Branch Litres"] > 0) & (out["Branch Litres"] < 120)]

    # Useful rows only
    out = out[
        (out["Extracted Branch RA"].astype(str).str.len() > 0) |
        (out["Rego"].astype(str).str.len() > 0) |
        (out["Branch Date"].notna())
    ]

    return out.reset_index(drop=True)

def parse_branch_litres_workbook(file):
    name = getattr(file, "name", "")
    all_rows = []
    xls = pd.ExcelFile(file)
    for sheet in xls.sheet_names:
        parsed = parse_branch_sheet_with_layout(file, sheet, name)
        if not parsed.empty:
            all_rows.append(parsed)
    return pd.concat(all_rows, ignore_index=True) if all_rows else pd.DataFrame()

# ---------------- Cars+ parsers ----------------
def parse_cars_pdf(file):
    text = read_pdf_text(file)
    rows = []
    current_loc = ""
    for line in text.splitlines():
        loc_match = re.search(r"\(Loc=([A-Z0-9]+)\)", line)
        if loc_match:
            current_loc = loc_match.group(1)
        parts = line.split()
        if len(parts) < 8 or not parts[0].isdigit():
            continue
        if not re.match(r"^[A-Z0-9]{3,8}$", parts[1].upper()):
            continue
        if not re.match(r"^\d{7,10}$", parts[2]):
            continue
        try:
            date_tokens = [p for p in parts if re.match(r"^\d{2}-\d{2}$", p)]
            close_date = pd.NaT
            if date_tokens:
                close_date = pd.to_datetime(date_tokens[0] + "-2026", format="%d-%m-%Y", errors="coerce")
            raw_ra = ra_digits(parts[2])
            found_ra = normalise_cars_ra(raw_ra)
            fuel_charge = parse_amount(parts[5])
            rows.append({
                "Location": current_loc,
                "Vehicle": parts[0],
                "Rego": clean_rego(parts[1]),
                "Cars+ Raw RA#": raw_ra,
                "Cars+ Found RA": raw_ra,
                "Cars+ Trimmed RA": found_ra,
                "Cars+ Date": close_date,
                "Fuel Charged $": fuel_charge,
                "Fuel Code": "",
                "Charged Yes/No": "YES" if fuel_charge > 0 else "NO",
                "Cars+ Source Type": "PDF",
                "Source File": getattr(file, "name", "")
            })
        except Exception:
            continue
    return pd.DataFrame(rows)

def parse_cars_table(file):
    name = getattr(file, "name", "").lower()
    df = pd.read_csv(file) if name.endswith(".csv") else pd.read_excel(file)
    cols = list(df.columns)
    ra_col = find_column(cols, ["ra number", "ra#", "ra no", "rental agreement"]) or (cols[2] if len(cols) >= 3 else cols[0])
    fuel_charge_col = find_column(cols, ["fuel charges", "fuel charge", "fuel chg", "fuelcharged"]) or (cols[5] if len(cols) >= 6 else None)
    fuel_code_col = None
    for c in cols:
        if str(c).strip().lower() in ["fuel", "fuel code", "fsc", "fpo"]:
            fuel_code_col = c
            break
    date_col = find_column(cols, ["date out", "date in", "close date", "date"])
    loc_out_col = find_column(cols, ["ra loc out", "loc out"])
    loc_in_col = find_column(cols, ["ra loc in", "loc in"])
    rego_col = find_column(cols, ["rego", "license", "plate", "vehicle"])

    rows = []
    for _, r in df.iterrows():
        raw_ra_digits = ra_digits(r.get(ra_col, ""))
        if not re.match(r"^\d{7,10}$", raw_ra_digits):
            continue
        found_ra = normalise_cars_ra(raw_ra_digits)
        fuel_charge = parse_amount(r.get(fuel_charge_col, 0)) if fuel_charge_col is not None else 0.0
        fuel_code = str(r.get(fuel_code_col, "")).strip() if fuel_code_col is not None else ""
        cars_date = excel_serial_to_date(r.get(date_col, pd.NaT)) if date_col is not None else pd.NaT
        location = ""
        if loc_out_col is not None or loc_in_col is not None:
            location = f"{str(r.get(loc_out_col, '')).strip()}→{str(r.get(loc_in_col, '')).strip()}"
        rows.append({
            "Location": location,
            "Vehicle": "",
            "Rego": clean_rego(r.get(rego_col, "")) if rego_col is not None else "",
            "Cars+ Raw RA#": raw_ra_digits,
            "Cars+ Found RA": raw_ra_digits,
            "Cars+ Trimmed RA": found_ra,
            "Cars+ Date": cars_date,
            "Fuel Charged $": fuel_charge,
            "Fuel Code": fuel_code,
            "Charged Yes/No": "YES" if fuel_charge > 0 else "NO",
            "Cars+ Source Type": "Excel/CSV",
            "Source File": getattr(file, "name", "")
        })
    return pd.DataFrame(rows)

def parse_cars_file(file):
    name = getattr(file, "name", "").lower()
    if name.endswith(".pdf"):
        return parse_cars_pdf(file)
    if name.endswith((".xlsx", ".xls", ".csv")):
        return parse_cars_table(file)
    return pd.DataFrame()

# ---------------- Stage 1 ----------------
def find_possible_branch_match(s, branch_df, litre_tolerance, used_branch, date_window):
    """
    Returns the best possible branch-side match and gives enough detail to investigate.

    This is used when a supplier row does not auto-match.
    It now always returns the possible branch RA/date/litres where available.
    """
    blank = {
        "Possible Match": "NO",
        "Possible Match Reason": "No branch litres rows were parsed.",
        "Possible Branch Date": "",
        "Possible Branch Litres": "",
        "Possible Branch RA": "",
        "Possible Branch Rego": "",
        "Possible Branch": "",
        "Possible Date Difference": "",
        "Possible Litre Difference": "",
        "Possible Branch Row Used Already": "",
    }

    if branch_df.empty:
        return blank

    work = branch_df.copy()
    supplier_litres = float(s["Supplier Litres"])
    supplier_branch = str(s.get("Branch", "")).strip()

    work["Possible Litre Difference"] = (work["Branch Litres"] - supplier_litres).abs()
    if pd.notna(s["Supplier Date"]):
        work["Possible Date Difference"] = (work["Branch Date"] - s["Supplier Date"]).abs().dt.days
    else:
        work["Possible Date Difference"] = 9999

    work["Was Already Used"] = work.index.isin(used_branch)

    def pack_candidate(b, match, reason):
        return {
            "Possible Match": match,
            "Possible Match Reason": reason,
            "Possible Branch Date": b["Branch Date"].date() if pd.notna(b["Branch Date"]) else "",
            "Possible Branch Litres": round(float(b["Branch Litres"]), 2),
            "Possible Branch RA": b.get("Extracted Branch RA", ""),
            "Possible Branch Rego": b.get("Rego", ""),
            "Possible Branch": b.get("Branch", ""),
            "Possible Date Difference": "" if pd.isna(b["Possible Date Difference"]) else int(b["Possible Date Difference"]),
            "Possible Litre Difference": round(float(b["Possible Litre Difference"]), 2),
            "Possible Branch Row Used Already": "YES" if bool(b.get("Was Already Used", False)) else "NO",
        }

    same_branch = work[work["Branch"].astype(str).str.lower() == supplier_branch.lower()].copy()

    # Best case: same branch + same litres, even if already used or outside date window.
    same_branch_litres = same_branch[same_branch["Possible Litre Difference"] <= litre_tolerance].copy()
    if not same_branch_litres.empty:
        same_branch_litres = same_branch_litres.sort_values(
            ["Was Already Used", "Possible Date Difference", "Possible Litre Difference"],
            ascending=[True, True, True]
        )
        b = same_branch_litres.iloc[0]

        reasons = []
        if bool(b["Was Already Used"]):
            reasons.append("same litres branch row already used by another supplier transaction")
        if pd.notna(b["Possible Date Difference"]) and int(b["Possible Date Difference"]) > date_window:
            reasons.append(f"date difference {int(b['Possible Date Difference'])} days is outside current {date_window}-day window")
        if not reasons:
            reasons.append("same branch and same litres found but not selected by strict matching order")

        return pack_candidate(b, "YES", "; ".join(reasons))

    # Same litres in another branch/location.
    same_litres_any = work[work["Possible Litre Difference"] <= litre_tolerance].copy()
    if not same_litres_any.empty:
        same_litres_any = same_litres_any.sort_values(
            ["Was Already Used", "Possible Date Difference", "Possible Litre Difference"],
            ascending=[True, True, True]
        )
        b = same_litres_any.iloc[0]
        return pack_candidate(b, "YES", "same litres found, but under a different branch/location")

    # Same branch but closest litres outside tolerance.
    if not same_branch.empty:
        same_branch = same_branch.sort_values(
            ["Possible Litre Difference", "Possible Date Difference", "Was Already Used"],
            ascending=[True, True, True]
        )
        b = same_branch.iloc[0]
        return pack_candidate(b, "CLOSE", "same branch found, but litres are outside tolerance")

    blank["Possible Match Reason"] = "no same-branch or same-litres candidate found in branch sheet"
    return blank


def branch_ra_priority(row):
    """
    Priority for matching duplicate litre rows.
    Lower score = better.
    Real numeric RA should win over NONREV/blank notes.
    """
    ra = str(row.get("Extracted Branch RA", "")).strip()
    note = str(row.get("Branch RA / Note", "")).strip().upper()

    if ra:
        return 0
    if note and "NONREV" not in note and "NON REV" not in note and "NON-REV" not in note:
        return 1
    if "NONREV" in note or "NON REV" in note or "NON-REV" in note:
        return 2
    return 3

def is_real_branch_ra(row):
    return str(row.get("Extracted Branch RA", "")).strip() != ""

def supplier_branch_reconciliation(supplier_df, branch_df, litre_tolerance, date_window, branch_match=True):
    """
    Stage 1 matching with duplicate trace.

    Matching order:
    1. Strict match: same branch + litres + date window.
    2. Relaxed match: same branch + litres, outside date window allowed.
    3. Remaining branch rows check supplier same branch/litres and show:
       - supplier voucher/date they wanted
       - branch RA/date/litres that already consumed that voucher
    """
    rows = []
    used_branch = set()
    used_supplier = set()

    # supplier index -> branch row details that consumed it
    supplier_used_by = {}

    # branch/litres lookup to find possible supplier candidates later
    supplier_match_lookup = {}

    def supplier_key(branch, litres):
        try:
            return (str(branch).lower().strip(), round(float(litres), 2))
        except Exception:
            return (str(branch).lower().strip(), "")

    for sidx, s in supplier_df.iterrows():
        key = supplier_key(s.get("Branch", ""), s.get("Supplier Litres", ""))
        supplier_match_lookup.setdefault(key, []).append((sidx, s))

    def build_candidates(s, allow_date_outside_window=False):
        candidates = branch_df.copy()

        if branch_match and str(s.get("Branch", "")).strip() and not candidates.empty:
            same_branch = candidates["Branch"].astype(str).str.lower() == str(s["Branch"]).lower()
            if same_branch.any():
                candidates = candidates[same_branch].copy()

        if candidates.empty:
            return candidates

        candidates["Litre Difference"] = (candidates["Branch Litres"] - float(s["Supplier Litres"])).abs()

        if pd.notna(s["Supplier Date"]):
            candidates["Date Difference"] = (candidates["Branch Date"] - s["Supplier Date"]).abs().dt.days
        else:
            candidates["Date Difference"] = 9999

        candidates = candidates[
            (candidates["Litre Difference"] <= litre_tolerance) &
            (~candidates.index.isin(used_branch))
        ].copy()

        if not candidates.empty:
            candidates["RA Match Priority"] = candidates.apply(branch_ra_priority, axis=1)

        if not allow_date_outside_window:
            candidates = candidates[candidates["Date Difference"] <= date_window].copy()

        return candidates

    def blank_duplicate_trace():
        return {
            "Already Used By Branch Date": "",
            "Already Used By Branch Litres": "",
            "Already Used By Branch RA": "",
            "Already Used By Branch Rego": "",
            "Already Used By Stage 1 Decision": "",
            "Already Used Match Date Difference": "",
        }

    def record_supplier_used(sidx, s, b, stage_decision, date_diff):
        supplier_used_by[sidx] = {
            "Already Used By Branch Date": b["Branch Date"].date() if pd.notna(b["Branch Date"]) else "",
            "Already Used By Branch Litres": round(float(b["Branch Litres"]), 2),
            "Already Used By Branch RA": b.get("Extracted Branch RA", ""),
            "Already Used By Branch Rego": b.get("Rego", ""),
            "Already Used By Stage 1 Decision": stage_decision,
            "Already Used Match Date Difference": date_diff,
        }

    # Supplier-driven matching first
    for sidx, s in supplier_df.iterrows():
        candidates = build_candidates(s, allow_date_outside_window=False)
        stage_decision = "✅ Supplier ↔ Branch Matched"
        match_note = "Supplier litres matched to branch recorded litres."

        if candidates.empty:
            candidates = build_candidates(s, allow_date_outside_window=True)
            stage_decision = "✅ Supplier ↔ Branch Matched - Date Review"
            match_note = "Supplier litres matched to branch litres, but date is outside the selected date window. Review date difference."

        if candidates.empty:
            possible = find_possible_branch_match(s, branch_df, litre_tolerance, used_branch, date_window) if "find_possible_branch_match" in globals() else {
                "Possible Match": "",
                "Possible Match Reason": "",
                "Possible Branch": "",
                "Possible Branch Date": "",
                "Possible Branch Litres": "",
                "Possible Branch RA": "",
                "Possible Branch Rego": "",
                "Possible Date Difference": "",
                "Possible Litre Difference": "",
            }

            trace = blank_duplicate_trace()

            rows.append({
                "Stage 1 Decision": (
                    "🟣 Supplier Recorded - Duplicate / Already Used"
                    if str(possible.get("Possible Match", "")).upper() in ["YES", "CLOSE"]
                    else "🔴 Supplier Not Recorded By Branch"
                ),
                "Branch": s["Branch"],
                "Supplier": s["Supplier"],
                "Supplier Site": s["Supplier Site"],
                "Supplier Date": s["Supplier Date"].date() if pd.notna(s["Supplier Date"]) else "",
                "Supplier Litres": s["Supplier Litres"],
                "Supplier Voucher": s["Supplier Voucher"],
                "Supplier Amount $": s.get("Supplier Amount $", 0.0),
                "Branch Rego": "",
                "Branch Date": "",
                "Branch Litres": "",
                "Branch RA / Note": "",
                "Extracted Branch RA": "",
                "Litre Difference": "",
                "Date Difference": "",
                "Possible Match": possible.get("Possible Match", ""),
                "Possible Match Reason": possible.get("Possible Match Reason", ""),
                "Possible Branch": possible.get("Possible Branch", ""),
                "Possible Branch Date": possible.get("Possible Branch Date", ""),
                "Possible Branch Litres": possible.get("Possible Branch Litres", ""),
                "Possible Branch RA": possible.get("Possible Branch RA", ""),
                "Possible Branch Rego": possible.get("Possible Branch Rego", ""),
                "Possible Branch Row Used Already": possible.get("Possible Branch Row Used Already", ""),
                "Possible Supplier Voucher": "",
                "Possible Supplier Date": "",
                "Possible Date Difference": possible.get("Possible Date Difference", ""),
                "Possible Litre Difference": possible.get("Possible Litre Difference", ""),
                **trace,
                "Stage 1 Note": (
                    "Fuel appears on supplier statement and a branch-side possible match was found, but it has already been used or needs duplicate review."
                    if str(possible.get("Possible Match", "")).upper() in ["YES", "CLOSE"]
                    else "Fuel appears on supplier statement but no matching branch litres entry was found."
                )
            })
        else:
            candidates = candidates.sort_values(["RA Match Priority", "Date Difference", "Litre Difference"])
            bidx = candidates.index[0]
            used_branch.add(bidx)
            used_supplier.add(sidx)
            b = branch_df.loc[bidx]
            date_diff = int(abs((b["Branch Date"] - s["Supplier Date"]).days)) if pd.notna(b["Branch Date"]) and pd.notna(s["Supplier Date"]) else ""

            record_supplier_used(sidx, s, b, stage_decision, date_diff)
            trace = blank_duplicate_trace()

            rows.append({
                "Stage 1 Decision": stage_decision,
                "Branch": b["Branch"],
                "Supplier": s["Supplier"],
                "Supplier Site": s["Supplier Site"],
                "Supplier Date": s["Supplier Date"].date() if pd.notna(s["Supplier Date"]) else "",
                "Supplier Litres": s["Supplier Litres"],
                "Supplier Voucher": s["Supplier Voucher"],
                "Supplier Amount $": s.get("Supplier Amount $", 0.0),
                "Branch Rego": b["Rego"],
                "Branch Date": b["Branch Date"].date() if pd.notna(b["Branch Date"]) else "",
                "Branch Litres": round(float(b["Branch Litres"]), 2),
                "Branch RA / Note": b["Branch RA / Note"],
                "Extracted Branch RA": b["Extracted Branch RA"],
                "Litre Difference": round(abs(float(b["Branch Litres"]) - float(s["Supplier Litres"])), 2),
                "Date Difference": date_diff,
                "Possible Match": "",
                "Possible Match Reason": "",
                "Possible Branch": "",
                "Possible Branch Date": "",
                "Possible Branch Litres": "",
                "Possible Branch RA": "",
                "Possible Branch Rego": "",
                "Possible Branch Row Used Already": "",
                "Possible Supplier Voucher": "",
                "Possible Supplier Date": "",
                "Possible Date Difference": "",
                "Possible Litre Difference": "",
                **trace,
                "Stage 1 Note": match_note
            })

    # Remaining branch rows - identify duplicates/already-used
    for bidx, b in branch_df.iterrows():
        if bidx in used_branch:
            continue

        key = supplier_key(b.get("Branch", ""), b.get("Branch Litres", ""))
        possible_suppliers = supplier_match_lookup.get(key, [])

        if possible_suppliers:
            best_sidx, best_s = None, None
            best_diff = 999999
            for sidx, s in possible_suppliers:
                try:
                    diff = abs((b["Branch Date"] - s["Supplier Date"]).days) if pd.notna(b["Branch Date"]) and pd.notna(s["Supplier Date"]) else 999999
                except Exception:
                    diff = 999999
                if diff < best_diff:
                    best_sidx, best_s, best_diff = sidx, s, diff

            if best_s is not None:
                already_trace = supplier_used_by.get(best_sidx, blank_duplicate_trace())
                if is_real_branch_ra(b):
                    decision = "✅ Supplier ↔ Branch Matched - Allocated RA Review"
                    note = (
                        "Same branch and same litres exist on the supplier statement. "
                        "This row has a real allocated branch RA, so the allocated RA is treated as the active match. "
                        "Review only because another same-litre row may have been matched earlier."
                    )
                    already_trace = {
                        "Already Used By Branch Date": "",
                        "Already Used By Branch Litres": "",
                        "Already Used By Branch RA": "",
                        "Already Used By Branch Rego": "",
                        "Already Used By Stage 1 Decision": "",
                        "Already Used Match Date Difference": "",
                    }
                elif best_sidx in used_supplier:
                    decision = "🟣 Possible Duplicate / Already Used Match"
                    note = (
                        "Same branch and same litres exist on supplier statement. "
                        "The duplicate trace columns show which branch row already consumed that supplier voucher."
                    )
                else:
                    decision = "🟣 Possible Supplier Match"
                    note = "Same branch and same litres exist on supplier statement but were not selected by the main matching order."

                rows.append({
                    "Stage 1 Decision": decision,
                    "Branch": b["Branch"],
                    "Supplier": best_s["Supplier"],
                    "Supplier Site": best_s["Supplier Site"],
                    "Supplier Date": best_s["Supplier Date"].date() if pd.notna(best_s["Supplier Date"]) else "",
                    "Supplier Litres": best_s["Supplier Litres"],
                    "Supplier Voucher": best_s["Supplier Voucher"],
                    "Supplier Amount $": best_s.get("Supplier Amount $", 0.0),
                    "Branch Rego": b["Rego"],
                    "Branch Date": b["Branch Date"].date() if pd.notna(b["Branch Date"]) else "",
                    "Branch Litres": round(float(b["Branch Litres"]), 2),
                    "Branch RA / Note": b["Branch RA / Note"],
                    "Extracted Branch RA": b["Extracted Branch RA"],
                    "Litre Difference": round(abs(float(b["Branch Litres"]) - float(best_s["Supplier Litres"])), 2),
                    "Date Difference": best_diff if best_diff != 999999 else "",
                    "Possible Match": "YES",
                    "Possible Match Reason": "same branch and same litres exist on supplier statement; check duplicate trace",
                    "Possible Branch": b["Branch"],
                    "Possible Branch Date": b["Branch Date"].date() if pd.notna(b["Branch Date"]) else "",
                    "Possible Branch Litres": round(float(b["Branch Litres"]), 2),
                    "Possible Branch RA": b.get("Extracted Branch RA", ""),
                    "Possible Branch Rego": b.get("Rego", ""),
                    "Possible Branch Row Used Already": "YES" if best_sidx in used_supplier else "NO",
                    "Possible Supplier Voucher": best_s.get("Supplier Voucher", ""),
                    "Possible Supplier Date": best_s["Supplier Date"].date() if pd.notna(best_s["Supplier Date"]) else "",
                    "Possible Date Difference": best_diff if best_diff != 999999 else "",
                    "Possible Litre Difference": 0,
                    **already_trace,
                    "Stage 1 Note": note
                })
                continue

        trace = blank_duplicate_trace()

        rows.append({
            "Stage 1 Decision": "🟠 Branch Entry Not On Supplier Statement",
            "Branch": b["Branch"],
            "Supplier": "",
            "Supplier Site": "",
            "Supplier Date": "",
            "Supplier Litres": "",
            "Supplier Voucher": "",
            "Supplier Amount $": "",
            "Branch Rego": b["Rego"],
            "Branch Date": b["Branch Date"].date() if pd.notna(b["Branch Date"]) else "",
            "Branch Litres": round(float(b["Branch Litres"]), 2),
            "Branch RA / Note": b["Branch RA / Note"],
            "Extracted Branch RA": b["Extracted Branch RA"],
            "Litre Difference": "",
            "Date Difference": "",
            "Possible Match": "",
            "Possible Match Reason": "",
            "Possible Branch": "",
            "Possible Branch Date": "",
            "Possible Branch Litres": "",
            "Possible Branch RA": "",
            "Possible Branch Rego": "",
            "Possible Branch Row Used Already": "",
            "Possible Supplier Voucher": "",
            "Possible Supplier Date": "",
            "Possible Date Difference": "",
            "Possible Litre Difference": "",
            **trace,
            "Stage 1 Note": "Branch recorded litres, but no matching supplier statement transaction was found."
        })

    return pd.DataFrame(rows)

# ---------------- Stage 2 ----------------
# ---------------- Stage 2 ----------------
# ---------------- Stage 2 ----------------
# ---------------- Stage 2 ----------------
# ---------------- Stage 2 ----------------
def match_cars_by_ra(branch_ra, cars_df):
    searched_ra = str(branch_ra).strip()

    if cars_df.empty:
        return {
            "Cars+ Match Result": "NO CARS+ FILE",
            "Cars+ Searched RA": searched_ra,
            "Cars+ Found RA": "",
            "Cars+ Raw RA#": "",
            "Cars+ Date": "",
            "Charged Yes/No": "NO",
            "Fuel Charged $": 0.0,
            "Fuel Code": "",
            "Cars+ Source Type": "",
        }

    if not searched_ra:
        return {
            "Cars+ Match Result": "NO BRANCH RA TO SEARCH",
            "Cars+ Searched RA": "",
            "Cars+ Found RA": "",
            "Cars+ Raw RA#": "",
            "Cars+ Date": "",
            "Charged Yes/No": "NO",
            "Fuel Charged $": 0.0,
            "Fuel Code": "",
            "Cars+ Source Type": "",
        }

    # 1. Exact match first.
    exact = cars_df[cars_df["Cars+ Found RA"].astype(str) == searched_ra].copy()
    match_type = "FOUND - EXACT RA"

    # 2. If exact fails, try the trimmed RA version for reports where Cars+ has an extra final digit.
    if exact.empty and "Cars+ Trimmed RA" in cars_df.columns:
        exact = cars_df[cars_df["Cars+ Trimmed RA"].astype(str) == searched_ra].copy()
        match_type = "FOUND - TRIMMED RA"

    if exact.empty:
        return {
            "Cars+ Match Result": "NOT FOUND",
            "Cars+ Searched RA": searched_ra,
            "Cars+ Found RA": "",
            "Cars+ Raw RA#": "",
            "Cars+ Date": "",
            "Charged Yes/No": "NO",
            "Fuel Charged $": 0.0,
            "Fuel Code": "",
            "Cars+ Source Type": "",
        }

    chosen = exact.iloc[0]
    return {
        "Cars+ Match Result": match_type,
        "Cars+ Searched RA": searched_ra,
        "Cars+ Found RA": chosen["Cars+ Found RA"],
        "Cars+ Raw RA#": chosen["Cars+ Raw RA#"],
        "Cars+ Date": chosen["Cars+ Date"].date() if pd.notna(chosen["Cars+ Date"]) else "",
        "Charged Yes/No": chosen["Charged Yes/No"],
        "Fuel Charged $": round(float(chosen["Fuel Charged $"]), 2),
        "Fuel Code": chosen.get("Fuel Code", ""),
        "Cars+ Source Type": chosen.get("Cars+ Source Type", ""),
    }


# ---------------- Phase 1 Intelligence Engine ----------------
def exposure_category(value):
    try:
        v = float(value)
    except Exception:
        return "Unknown"

    if v <= 0:
        return "No Exposure"
    if v <= 100:
        return "Low"
    if v <= 300:
        return "Medium"
    return "High"

def row_has_nonrev(row):
    note = str(row.get("Branch RA / Note", "")).upper()
    return any(x in note for x in ["NONREV", "NON REV", "NON-REV", "REPO", "INTERNAL"])

def confidence_engine(stage1_decision, cars_result, charged_yes_no, branch_ra, supplier_litres, branch_litres, date_diff, nonrev):
    """
    Returns confidence score, reason and review flag.
    """
    stage1 = str(stage1_decision)
    cars = str(cars_result)
    charged = str(charged_yes_no)
    has_ra = bool(str(branch_ra).strip())

    try:
        litre_gap = abs(float(branch_litres) - float(supplier_litres)) if supplier_litres not in ["", None] and branch_litres not in ["", None] else None
    except Exception:
        litre_gap = None

    try:
        dd = int(date_diff) if str(date_diff).strip() != "" else None
    except Exception:
        dd = None

    # Best case
    if stage1.startswith("✅") and cars.startswith("FOUND") and charged == "YES" and has_ra:
        if dd is not None and dd <= 2 and (litre_gap is None or litre_gap <= 0.05):
            return 100, "Exact/strong match: supplier, branch, allocated RA and Cars+ charge found.", "NO"
        return 95, "Strong match: allocated RA and Cars+ charge found; date/litre variance may need light review.", "NO"

    if stage1.startswith("✅") and cars.startswith("FOUND") and charged == "NO":
        return 90, "Supplier and branch matched, RA found in Cars+, but no fuel charge was recorded.", "YES"

    if stage1.startswith("✅") and "Date Review" in stage1:
        return 85, "Supplier and branch litres matched, but date is outside the selected window.", "YES"

    if stage1.startswith("✅") and "Allocated RA Review" in stage1:
        return 85, "Allocated branch RA has been protected, but duplicate litre conflict needs review.", "YES"

    if nonrev and stage1.startswith("✅"):
        return 80, "Internal/NONREV style fuel entry matched to supplier/branch; operational review recommended.", "YES"

    if str(stage1).startswith("🟣"):
        return 70, "Duplicate or already-used supplier/branch evidence found.", "YES"

    if cars == "NO BRANCH RA TO SEARCH":
        return 60, "Branch litres exist but no searchable branch RA was found.", "YES"

    if cars == "NOT FOUND":
        return 55, "Branch RA was available but not found in Cars+ charge audit.", "YES"

    if str(stage1).startswith("🟠"):
        return 40, "Branch litres exist but no supplier statement match was confirmed.", "YES"

    if str(stage1).startswith("🔴"):
        return 25, "Supplier statement exists but no branch entry was confirmed.", "YES"

    return 50, "Partial evidence found but final matching confidence is uncertain.", "YES"

def operational_decision_engine(stage1_decision, cars_result, charged_yes_no, branch_ra, nonrev, exposure):
    """
    Converts technical matching into operational outcome.
    """
    stage1 = str(stage1_decision)
    cars = str(cars_result)
    charged = str(charged_yes_no)
    has_ra = bool(str(branch_ra).strip())

    if nonrev:
        return "🚗 Repo / Internal Fuel"

    if stage1.startswith("✅") and cars.startswith("FOUND") and charged == "YES":
        return "✅ Correctly Processed"

    if stage1.startswith("✅") and cars.startswith("FOUND") and charged == "NO":
        return "🔥 Revenue Leakage"

    if stage1.startswith("✅") and cars in ["NOT FOUND", "NO CARS+ FILE"]:
        return "🔥 Revenue Leakage"

    if "Allocated RA Review" in stage1 or (has_ra and stage1.startswith("🟣")):
        return "⚠ Allocated RA Review"

    if stage1.startswith("🔴"):
        return "⚠ Branch Missing Fuel Entry"

    if stage1.startswith("🟠"):
        return "⚠ Supplier Review Required"

    if stage1.startswith("🟣"):
        return "🔄 Duplicate Fuel Entry"

    if cars == "NO BRANCH RA TO SEARCH":
        return "⚠ Needs RA"

    return "⚠ Needs Review"

def final_review_bucket(confidence, operational_decision):
    if operational_decision == "✅ Correctly Processed" and confidence >= 90:
        return "No Review Required"
    if "Revenue Leakage" in operational_decision:
        return "High Priority Review"
    if confidence < 70:
        return "Manual Review"
    return "Operational Review"

def stage2_charge_report(stage1_df, cars_df, avg_price):
    # Only check actual branch entries in Stage 2.
    branch_rows = stage1_df[stage1_df["Branch Litres"].astype(str).str.strip() != ""].copy()
    rows = []

    for _, r in branch_rows.iterrows():
        branch_ra = str(r.get("Extracted Branch RA", "")).strip()
        branch_litres = r.get("Branch Litres", "")
        supplier_litres = r.get("Supplier Litres", "")
        date_diff = r.get("Date Difference", "")

        cars = match_cars_by_ra(branch_ra, cars_df)

        if str(cars["Cars+ Match Result"]).startswith("FOUND") and cars["Charged Yes/No"] == "YES":
            final = "✅ Recovered"
        elif str(cars["Cars+ Match Result"]).startswith("FOUND") and cars["Charged Yes/No"] == "NO":
            final = "❌ Missed Charge"
        elif cars["Cars+ Match Result"] == "NO BRANCH RA TO SEARCH":
            final = "🟡 Needs RA"
        elif cars["Cars+ Match Result"] in ["NOT FOUND", "NO CARS+ FILE"]:
            final = "🟠 RA Not Found"
        else:
            final = "⚠️ Review"

        exposure = 0.0
        if final != "✅ Recovered":
            try:
                exposure = round(float(branch_litres) * avg_price, 2)
            except:
                exposure = 0.0

        nonrev = row_has_nonrev(r)

        confidence, confidence_reason, review_required = confidence_engine(
            stage1_decision=r.get("Stage 1 Decision", ""),
            cars_result=cars["Cars+ Match Result"],
            charged_yes_no=cars["Charged Yes/No"],
            branch_ra=branch_ra,
            supplier_litres=supplier_litres,
            branch_litres=branch_litres,
            date_diff=date_diff,
            nonrev=nonrev
        )

        operational_decision = operational_decision_engine(
            stage1_decision=r.get("Stage 1 Decision", ""),
            cars_result=cars["Cars+ Match Result"],
            charged_yes_no=cars["Charged Yes/No"],
            branch_ra=branch_ra,
            nonrev=nonrev,
            exposure=exposure
        )

        review_bucket = final_review_bucket(confidence, operational_decision)

        rows.append({
            "Final Decision": final,
            "Final Operational Decision": operational_decision,
            "Match Confidence %": confidence,
            "Confidence Reason": confidence_reason,
            "Review Required": review_required,
            "Review Bucket": review_bucket,
            "Exposure Category": exposure_category(exposure),
            "Fuel Recovery Rate Used": avg_price,
            "Stage 1 Decision": r["Stage 1 Decision"],
            "Branch": r.get("Branch", ""),
            "Rego": r.get("Branch Rego", ""),
            "Branch Date": r.get("Branch Date", ""),
            "Branch Litres": branch_litres,
            "Branch RA / Note": r.get("Branch RA / Note", ""),
            "Extracted Branch RA": branch_ra,
            "Supplier Voucher": r.get("Supplier Voucher", ""),
            "Supplier Litres": supplier_litres,
            "Supplier Date": r.get("Supplier Date", ""),
            "Cars+ Match Result": cars["Cars+ Match Result"],
            "Cars+ Searched RA": cars["Cars+ Searched RA"],
            "Cars+ Found RA": cars["Cars+ Found RA"],
            "Cars+ Raw RA#": cars["Cars+ Raw RA#"],
            "Cars+ Date": cars["Cars+ Date"],
            "Cars+ Source Type": cars["Cars+ Source Type"],
            "Fuel Code": cars["Fuel Code"],
            "Charged Yes/No": cars["Charged Yes/No"],
            "Fuel Charged $": cars["Fuel Charged $"],
            "Estimated Exposure $": exposure,
            "Date Difference": date_diff,
        })

    return pd.DataFrame(rows)


# ---------------- V37 Executive Dashboard Helpers ----------------
def safe_numeric(series):
    return pd.to_numeric(series, errors="coerce").fillna(0)

def build_executive_dashboard(stage2_df):
    if stage2_df is None or stage2_df.empty:
        return pd.DataFrame()

    work = stage2_df.copy()
    work["Estimated Exposure $"] = safe_numeric(work.get("Estimated Exposure $", 0))
    work["Match Confidence %"] = safe_numeric(work.get("Match Confidence %", 0))
    work["Branch Litres"] = safe_numeric(work.get("Branch Litres", 0))

    summary = work.groupby("Branch", dropna=False).agg(
        Total_Transactions=("Branch", "count"),
        Correctly_Processed=("Final Operational Decision", lambda s: (s == "✅ Correctly Processed").sum()),
        Revenue_Leakage=("Final Operational Decision", lambda s: (s == "🔥 Revenue Leakage").sum()),
        Internal_NONREV=("Final Operational Decision", lambda s: (s == "🚗 Repo / Internal Fuel").sum()),
        Review_Required=("Review Required", lambda s: (s == "YES").sum()),
        Total_Litres=("Branch Litres", "sum"),
        Exposure=("Estimated Exposure $", "sum"),
        Average_Confidence=("Match Confidence %", "mean"),
        High_Exposure_Count=("Exposure Category", lambda s: (s == "High").sum()),
    ).reset_index()

    summary["Recovery Rate %"] = (
        summary["Correctly_Processed"] / summary["Total_Transactions"].replace(0, pd.NA) * 100
    ).fillna(0).round(1)

    summary["Review Rate %"] = (
        summary["Review_Required"] / summary["Total_Transactions"].replace(0, pd.NA) * 100
    ).fillna(0).round(1)

    summary["Average_Confidence"] = summary["Average_Confidence"].fillna(0).round(1)
    summary["Total_Litres"] = summary["Total_Litres"].round(2)
    summary["Exposure"] = summary["Exposure"].round(2)
    return summary.sort_values(["Exposure", "Review_Required"], ascending=[False, False])

def build_stage1_dashboard(stage1_df):
    if stage1_df is None or stage1_df.empty:
        return pd.DataFrame()
    work = stage1_df.copy()
    work["Branch Litres Numeric"] = pd.to_numeric(work.get("Branch Litres", 0), errors="coerce").fillna(0)
    summary = work.groupby("Branch", dropna=False).agg(
        Stage1_Rows=("Branch", "count"),
        Matched=("Stage 1 Decision", lambda s: s.astype(str).str.startswith("✅").sum()),
        Supplier_Not_Recorded=("Stage 1 Decision", lambda s: (s == "🔴 Supplier Not Recorded By Branch").sum()),
        Branch_Not_On_Statement=("Stage 1 Decision", lambda s: (s == "🟠 Branch Entry Not On Supplier Statement").sum()),
        Duplicate_Review=("Stage 1 Decision", lambda s: s.astype(str).str.startswith("🟣").sum()),
        Branch_Litres=("Branch Litres Numeric", "sum"),
    ).reset_index()
    summary["Stage1_Match_Rate %"] = (
        summary["Matched"] / summary["Stage1_Rows"].replace(0, pd.NA) * 100
    ).fillna(0).round(1)
    summary["Branch_Litres"] = summary["Branch_Litres"].round(2)
    return summary

def top_review_items(stage2_df, selected_branch=None, limit=25):
    if stage2_df is None or stage2_df.empty:
        return pd.DataFrame()
    work = stage2_df.copy()
    if selected_branch and selected_branch != "All Locations" and "Branch" in work.columns:
        work = work[work["Branch"].astype(str) == selected_branch]
    work["Estimated Exposure $"] = safe_numeric(work.get("Estimated Exposure $", 0))
    work["Match Confidence %"] = safe_numeric(work.get("Match Confidence %", 0))
    review = work[
        (work["Review Required"] == "YES") |
        (work["Final Operational Decision"] != "✅ Correctly Processed")
    ].copy()
    if review.empty:
        return review
    return review.sort_values(["Estimated Exposure $", "Match Confidence %"], ascending=[False, True]).head(limit)

def decision_chart_data(stage2_df):
    if stage2_df is None or stage2_df.empty:
        return pd.DataFrame()
    return stage2_df.groupby("Final Operational Decision").size().reset_index(name="Count").set_index("Final Operational Decision")

def exposure_chart_data(stage2_df):
    if stage2_df is None or stage2_df.empty:
        return pd.DataFrame()
    work = stage2_df.copy()
    work["Estimated Exposure $"] = safe_numeric(work.get("Estimated Exposure $", 0))
    return work.groupby("Branch")["Estimated Exposure $"].sum().sort_values(ascending=False).to_frame()

def review_chart_data(stage2_df):
    if stage2_df is None or stage2_df.empty:
        return pd.DataFrame()
    return stage2_df.groupby(["Branch", "Review Required"]).size().reset_index(name="Count")

# ---------------- UI ----------------

# ---------------- V37 Executive Dashboard Helpers ----------------
def safe_numeric(series):
    return pd.to_numeric(series, errors="coerce").fillna(0)

def build_executive_dashboard(stage2_df):
    if stage2_df is None or stage2_df.empty:
        return pd.DataFrame()

    work = stage2_df.copy()
    work["Estimated Exposure $"] = safe_numeric(work.get("Estimated Exposure $", 0))
    work["Match Confidence %"] = safe_numeric(work.get("Match Confidence %", 0))
    work["Branch Litres"] = safe_numeric(work.get("Branch Litres", 0))

    summary = work.groupby("Branch", dropna=False).agg(
        Total_Transactions=("Branch", "count"),
        Correctly_Processed=("Final Operational Decision", lambda s: (s == "✅ Correctly Processed").sum()),
        Revenue_Leakage=("Final Operational Decision", lambda s: (s == "🔥 Revenue Leakage").sum()),
        Internal_NONREV=("Final Operational Decision", lambda s: (s == "🚗 Repo / Internal Fuel").sum()),
        Review_Required=("Review Required", lambda s: (s == "YES").sum()),
        Total_Litres=("Branch Litres", "sum"),
        Exposure=("Estimated Exposure $", "sum"),
        Average_Confidence=("Match Confidence %", "mean"),
        High_Exposure_Count=("Exposure Category", lambda s: (s == "High").sum()),
    ).reset_index()

    summary["Recovery Rate %"] = (
        summary["Correctly_Processed"] / summary["Total_Transactions"].replace(0, pd.NA) * 100
    ).fillna(0).round(1)

    summary["Review Rate %"] = (
        summary["Review_Required"] / summary["Total_Transactions"].replace(0, pd.NA) * 100
    ).fillna(0).round(1)

    summary["Average_Confidence"] = summary["Average_Confidence"].fillna(0).round(1)
    summary["Total_Litres"] = summary["Total_Litres"].round(2)
    summary["Exposure"] = summary["Exposure"].round(2)
    return summary.sort_values(["Exposure", "Review_Required"], ascending=[False, False])

def build_stage1_dashboard(stage1_df):
    if stage1_df is None or stage1_df.empty:
        return pd.DataFrame()
    work = stage1_df.copy()
    work["Branch Litres Numeric"] = pd.to_numeric(work.get("Branch Litres", 0), errors="coerce").fillna(0)
    summary = work.groupby("Branch", dropna=False).agg(
        Stage1_Rows=("Branch", "count"),
        Matched=("Stage 1 Decision", lambda s: s.astype(str).str.startswith("✅").sum()),
        Supplier_Not_Recorded=("Stage 1 Decision", lambda s: (s == "🔴 Supplier Not Recorded By Branch").sum()),
        Branch_Not_On_Statement=("Stage 1 Decision", lambda s: (s == "🟠 Branch Entry Not On Supplier Statement").sum()),
        Duplicate_Review=("Stage 1 Decision", lambda s: s.astype(str).str.startswith("🟣").sum()),
        Branch_Litres=("Branch Litres Numeric", "sum"),
    ).reset_index()
    summary["Stage1_Match_Rate %"] = (
        summary["Matched"] / summary["Stage1_Rows"].replace(0, pd.NA) * 100
    ).fillna(0).round(1)
    summary["Branch_Litres"] = summary["Branch_Litres"].round(2)
    return summary

def top_review_items(stage2_df, selected_branch=None, limit=25):
    if stage2_df is None or stage2_df.empty:
        return pd.DataFrame()
    work = stage2_df.copy()
    if selected_branch and selected_branch != "All Locations" and "Branch" in work.columns:
        work = work[work["Branch"].astype(str) == selected_branch]
    work["Estimated Exposure $"] = safe_numeric(work.get("Estimated Exposure $", 0))
    work["Match Confidence %"] = safe_numeric(work.get("Match Confidence %", 0))
    review = work[
        (work["Review Required"] == "YES") |
        (work["Final Operational Decision"] != "✅ Correctly Processed")
    ].copy()
    if review.empty:
        return review
    return review.sort_values(["Estimated Exposure $", "Match Confidence %"], ascending=[False, True]).head(limit)

def decision_chart_data(stage2_df):
    if stage2_df is None or stage2_df.empty:
        return pd.DataFrame()
    return stage2_df.groupby("Final Operational Decision").size().reset_index(name="Count").set_index("Final Operational Decision")

def exposure_chart_data(stage2_df):
    if stage2_df is None or stage2_df.empty:
        return pd.DataFrame()
    work = stage2_df.copy()
    work["Estimated Exposure $"] = safe_numeric(work.get("Estimated Exposure $", 0))
    return work.groupby("Branch")["Estimated Exposure $"].sum().sort_values(ascending=False).to_frame()

def review_chart_data(stage2_df):
    if stage2_df is None or stage2_df.empty:
        return pd.DataFrame()
    return stage2_df.groupby(["Branch", "Review Required"]).size().reset_index(name="Count")

# ---------------- UI ----------------
with st.sidebar:
    st.header("1. Upload files")
    supplier_files = st.file_uploader("Fuel statements - Mobil or Caltex/Farmlands PDF", type=["pdf"], accept_multiple_files=True)
    branch_files = st.file_uploader("Branch fuel litres workbook - each tab = branch", type=["xlsx", "xls"], accept_multiple_files=True)
    cars_files = st.file_uploader("Cars+ charge audit - PDF, Excel or CSV", type=["pdf", "xlsx", "xls", "csv"], accept_multiple_files=True)

    st.header("2. Match settings")
    litre_tolerance = st.number_input("Supplier ↔ Branch litre tolerance", min_value=0.0, value=0.05, step=0.01)
    supplier_date_window = st.number_input("Supplier ↔ Branch date window days", min_value=0, value=2, step=1)
    avg_price = st.number_input("Estimated fuel $ per litre", min_value=0.0, value=3.00, step=0.10)
    branch_match = st.checkbox("Match supplier to same branch only where possible", value=True)

supplier_df_list = []
for f in supplier_files or []:
    try:
        parsed = parse_supplier_file(f)
        if not parsed.empty:
            supplier_df_list.append(parsed)
    except Exception as e:
        st.error(f"Could not read supplier file {f.name}: {e}")
supplier_df = pd.concat(supplier_df_list, ignore_index=True) if supplier_df_list else pd.DataFrame()

branch_df_list = []
for f in branch_files or []:
    try:
        parsed = parse_branch_litres_workbook(f)
        if not parsed.empty:
            branch_df_list.append(parsed)
    except Exception as e:
        st.error(f"Could not read branch file {f.name}: {e}")
branch_df = pd.concat(branch_df_list, ignore_index=True) if branch_df_list else pd.DataFrame()

cars_df_list = []
for f in cars_files or []:
    try:
        parsed = parse_cars_file(f)
        if not parsed.empty:
            cars_df_list.append(parsed)
    except Exception as e:
        st.error(f"Could not read Cars+ file {f.name}: {e}")
cars_df = pd.concat(cars_df_list, ignore_index=True) if cars_df_list else pd.DataFrame()

# Branch selector after parsing
selected_branch = st.sidebar.selectbox("3. View location", branch_options(supplier_df, branch_df), index=0)

supplier_view = filter_branch(supplier_df, selected_branch)
branch_view = filter_branch(branch_df, selected_branch)

st.subheader("1. Fuel statement check")
if not supplier_df.empty:
    c1,c2,c3,c4 = st.columns(4)
    c1.metric("Supplier transactions", len(supplier_view))
    c2.metric("Supplier litres", f"{supplier_view['Supplier Litres'].sum():,.2f}")
    c3.metric("Suppliers", supplier_view["Supplier"].nunique())
    c4.metric("Branches found", supplier_view["Branch"].nunique())
    supplier_branch_summary = supplier_view.groupby(["Supplier","Branch"], dropna=False).agg(Transactions=("Supplier Litres","count"), Litres=("Supplier Litres","sum"), Amount=("Supplier Amount $","sum")).reset_index()
    st.dataframe(supplier_branch_summary, use_container_width=True)
else:
    st.info("Upload Mobil or Caltex/Farmlands fuel statement.")

st.subheader("2. Branch fuel litres check")
if not branch_df.empty:
    c1,c2,c3 = st.columns(3)
    c1.metric("Branch entries", len(branch_view))
    c2.metric("Branch litres", f"{branch_view['Branch Litres'].sum():,.2f}")
    c3.metric("Rows with RA", int((branch_view["Extracted Branch RA"].astype(str).str.len()>0).sum()))
    branch_summary = branch_view.groupby("Branch", dropna=False).agg(Entries=("Branch Litres","count"), Litres=("Branch Litres","sum"), Rows_With_RA=("Extracted Branch RA", lambda s: (s.astype(str).str.len()>0).sum())).reset_index()
    st.dataframe(branch_summary, use_container_width=True)
else:
    st.info("Upload branch fuel litres workbook. Each worksheet tab will be treated as a branch.")

st.subheader("3. Cars+ charge audit check")
if not cars_df.empty:
    c1,c2,c3,c4 = st.columns(4)
    c1.metric("Cars+ rows parsed", len(cars_df))
    c2.metric("Fuel charged rows", int((cars_df["Fuel Charged $"] > 0).sum()))
    c3.metric("Zero charge rows", int((cars_df["Fuel Charged $"] <= 0).sum()))
    c4.metric("Source types", cars_df["Cars+ Source Type"].nunique())
    with st.expander("Cars+ rows - verify RA parsing"):
        st.dataframe(cars_df, use_container_width=True)
else:
    st.info("Upload Cars+ charge audit as PDF, Excel, or CSV.")

if not supplier_df.empty and not branch_df.empty:
    st.markdown("---")
    st.subheader(f"Stage 1 Report — Fuel Statement vs Branch Recorded Litres ({selected_branch})")
    stage1_all = supplier_branch_reconciliation(supplier_df, branch_df, float(litre_tolerance), int(supplier_date_window), branch_match=branch_match)
    stage1 = filter_branch(stage1_all, selected_branch)

    matched = stage1[stage1["Stage 1 Decision"].astype(str).str.startswith("✅ Supplier ↔ Branch Matched")]
    supplier_missing = stage1[stage1["Stage 1 Decision"] == "🔴 Supplier Not Recorded By Branch"]
    branch_extra = stage1[stage1["Stage 1 Decision"] == "🟠 Branch Entry Not On Supplier Statement"]
    duplicates = stage1[
        stage1["Stage 1 Decision"].astype(str).str.startswith("🟣 Possible") |
        stage1["Stage 1 Decision"].astype(str).str.startswith("🟣 Supplier Recorded")
    ]

    k1,k2,k3,k4 = st.columns(4)
    k1.metric("Matched", len(matched))
    k2.metric("Supplier not recorded", len(supplier_missing))
    k3.metric("Branch not on statement", len(branch_extra))
    k4.metric("Duplicate / Review", len(duplicates))

    st.subheader("Stage 1 Branch Stats")
    stage1_summary = stage1_all.groupby(["Branch","Stage 1 Decision"], dropna=False).agg(Count=("Stage 1 Decision","count")).reset_index()
    if selected_branch != "All Locations":
        stage1_summary = stage1_summary[stage1_summary["Branch"] == selected_branch]
    st.dataframe(stage1_summary, use_container_width=True)

    tab_a,tab_b,tab_c,tab_dup,tab_d = st.tabs(["✅ Matched / Allocated RA Review","🔴 Supplier Not Recorded","🟠 Branch Not On Statement","🟣 Duplicate / Review","📊 Stage 1 Full Report"])
    with tab_a: 
        st.dataframe(matched, use_container_width=True)
    with tab_b: 
        st.dataframe(supplier_missing, use_container_width=True)
    with tab_c: 
        st.dataframe(branch_extra, use_container_width=True)
    with tab_dup:
        st.caption("These rows have supplier/branch evidence, but the same litres have already been used or need duplicate review. They are not hard missing records.")
        duplicate_cols = [
            "Stage 1 Decision",
            "Branch",
            "Supplier Date",
            "Supplier Litres",
            "Supplier Voucher",
            "Possible Match",
            "Possible Match Reason",
            "Possible Branch",
            "Possible Branch Date",
            "Possible Branch Litres",
            "Possible Branch RA",
            "Possible Branch Rego",
            "Possible Branch Row Used Already",
            "Possible Date Difference",
            "Possible Litre Difference",
            "Branch Date",
            "Branch Litres",
            "Branch RA / Note",
            "Extracted Branch RA",
            "Possible Supplier Date",
            "Possible Supplier Voucher",
            "Already Used By Branch Date",
            "Already Used By Branch Litres",
            "Already Used By Branch RA",
            "Already Used By Branch Rego",
            "Already Used By Stage 1 Decision",
            "Already Used Match Date Difference",
            "Stage 1 Note"
        ]
        available_cols = [c for c in duplicate_cols if c in duplicates.columns]
        st.dataframe(duplicates[available_cols] if available_cols else duplicates, use_container_width=True)
        st.download_button("Download Possible Duplicates CSV", duplicates.to_csv(index=False).encode("utf-8"), f"stage1_possible_duplicates_{selected_branch}.csv", "text/csv")
    with tab_d:
        st.dataframe(stage1, use_container_width=True)
        st.download_button("Download Stage 1 CSV", stage1.to_csv(index=False).encode("utf-8"), f"stage1_{selected_branch}.csv", "text/csv")
else:
    stage1_all = pd.DataFrame()
    stage1 = pd.DataFrame()
    st.markdown("---")
    st.info("Upload both supplier statement and branch litres workbook to produce Stage 1 report.")

if not stage1_all.empty and not cars_df.empty:
    st.markdown("---")
    st.subheader(f"Stage 2 Report — Branch Litres vs Cars+ Charge Audit ({selected_branch})")
    stage2_all = stage2_charge_report(stage1_all, cars_df, float(avg_price))
    stage2 = filter_branch(stage2_all, selected_branch)

    recovered = stage2[stage2["Final Decision"] == "✅ Recovered"]
    missed = stage2[stage2["Final Decision"] == "❌ Missed Charge"]
    needs_ra = stage2[stage2["Final Decision"] == "🟡 Needs RA"]
    ra_not_found = stage2[stage2["Final Decision"] == "🟠 RA Not Found"]
    revenue_leakage = stage2[stage2["Final Operational Decision"] == "🔥 Revenue Leakage"]
    confidence_reviews = stage2[stage2["Review Required"] == "YES"]
    correctly_processed = stage2[stage2["Final Operational Decision"] == "✅ Correctly Processed"]

    c1,c2,c3,c4,c5 = st.columns(5)
    c1.metric("Correctly Processed", len(correctly_processed))
    c2.metric("Revenue Leakage", len(revenue_leakage))
    c3.metric("Review Required", len(confidence_reviews))
    c4.metric("Average Confidence", f"{stage2['Match Confidence %'].mean():.1f}%" if not stage2.empty else "0%")
    c5.metric("Exposure", f"${stage2['Estimated Exposure $'].sum():,.2f}")

    st.caption("Phase 1 Intelligence active: confidence scoring, operational decisions, exposure categories and review buckets.")

    st.subheader("Branch Intelligence Summary")
    if not stage2_all.empty:
        branch_intel = stage2_all.groupby(["Branch"], dropna=False).agg(
            Total_Rows=("Branch", "count"),
            Correctly_Processed=("Final Operational Decision", lambda s: (s == "✅ Correctly Processed").sum()),
            Revenue_Leakage=("Final Operational Decision", lambda s: (s == "🔥 Revenue Leakage").sum()),
            Review_Required=("Review Required", lambda s: (s == "YES").sum()),
            Average_Confidence=("Match Confidence %", "mean"),
            Exposure=("Estimated Exposure $", "sum")
        ).reset_index()
        branch_intel["Recovery Rate %"] = (branch_intel["Correctly_Processed"] / branch_intel["Total_Rows"] * 100).round(1)
        branch_intel["Average_Confidence"] = branch_intel["Average_Confidence"].round(1)

        if selected_branch != "All Locations":
            branch_intel = branch_intel[branch_intel["Branch"] == selected_branch]

        st.dataframe(branch_intel, use_container_width=True)

    operational_summary = stage2.groupby(["Final Operational Decision"], dropna=False).agg(
        Count=("Final Operational Decision", "count"),
        Litres=("Branch Litres", "sum"),
        Exposure=("Estimated Exposure $", "sum"),
        Avg_Confidence=("Match Confidence %", "mean")
    ).reset_index()
    if not operational_summary.empty:
        operational_summary["Avg_Confidence"] = operational_summary["Avg_Confidence"].round(1)

    st.subheader("Operational Decision Summary")
    st.dataframe(operational_summary, use_container_width=True)

    # ---------------- V37 Executive Dashboard ----------------
    st.markdown("---")
    st.header("Executive Dashboard")

    exec_summary_all = build_executive_dashboard(stage2_all)
    stage1_summary_all = build_stage1_dashboard(stage1_all)

    if not exec_summary_all.empty:
        exec_summary = exec_summary_all.copy()
        if selected_branch != "All Locations":
            exec_summary = exec_summary[exec_summary["Branch"] == selected_branch]

        total_rows = int(exec_summary["Total_Transactions"].sum()) if not exec_summary.empty else 0
        total_correct = int(exec_summary["Correctly_Processed"].sum()) if not exec_summary.empty else 0
        total_reviews = int(exec_summary["Review_Required"].sum()) if not exec_summary.empty else 0
        total_leakage = int(exec_summary["Revenue_Leakage"].sum()) if not exec_summary.empty else 0
        total_exposure = float(exec_summary["Exposure"].sum()) if not exec_summary.empty else 0.0
        avg_conf = float(exec_summary["Average_Confidence"].mean()) if not exec_summary.empty else 0.0
        recovery_rate = (total_correct / total_rows * 100) if total_rows else 0

        d1,d2,d3,d4,d5,d6 = st.columns(6)
        d1.metric("Total Fuel Events", f"{total_rows:,}")
        d2.metric("Recovery Rate", f"{recovery_rate:.1f}%")
        d3.metric("Review Required", f"{total_reviews:,}")
        d4.metric("Revenue Leakage", f"{total_leakage:,}")
        d5.metric("Exposure", f"${total_exposure:,.2f}")
        d6.metric("Avg Confidence", f"{avg_conf:.1f}%")

        dash_tab1, dash_tab2, dash_tab3, dash_tab4, dash_tab5 = st.tabs([
            "🏠 Branch KPI Dashboard",
            "🔥 Exposure Dashboard",
            "🧠 Review Workload",
            "📊 Operational Charts",
            "🚨 Top Review Items"
        ])

        with dash_tab1:
            st.caption("Branch-level management view showing recovery, confidence, exposure, and review workload.")
            st.dataframe(exec_summary, use_container_width=True)

            if not stage1_summary_all.empty:
                st.subheader("Stage 1 Supplier ↔ Branch Health")
                s1 = stage1_summary_all.copy()
                if selected_branch != "All Locations":
                    s1 = s1[s1["Branch"] == selected_branch]
                st.dataframe(s1, use_container_width=True)

        with dash_tab2:
            st.caption("Exposure is calculated using the estimated recovery rate and unresolved/missed fuel rows.")
            exposure_cols = [
                "Branch",
                "Exposure",
                "Revenue_Leakage",
                "High_Exposure_Count",
                "Review_Required",
                "Total_Litres",
                "Recovery Rate %",
                "Average_Confidence"
            ]
            st.dataframe(exec_summary[[c for c in exposure_cols if c in exec_summary.columns]], use_container_width=True)

            exp_chart = exposure_chart_data(stage2 if selected_branch != "All Locations" else stage2_all)
            if not exp_chart.empty:
                st.bar_chart(exp_chart)

        with dash_tab3:
            st.caption("Rows requiring operational review, by decision and confidence.")
            workload = stage2.copy()
            if not workload.empty:
                workload_summary = workload.groupby(["Review Bucket", "Final Operational Decision"], dropna=False).agg(
                    Count=("Review Bucket", "count"),
                    Exposure=("Estimated Exposure $", "sum"),
                    Avg_Confidence=("Match Confidence %", "mean")
                ).reset_index()
                workload_summary["Exposure"] = pd.to_numeric(workload_summary["Exposure"], errors="coerce").fillna(0).round(2)
                workload_summary["Avg_Confidence"] = pd.to_numeric(workload_summary["Avg_Confidence"], errors="coerce").fillna(0).round(1)
                st.dataframe(workload_summary, use_container_width=True)

        with dash_tab4:
            st.caption("High-level operational distribution.")
            decision_data = decision_chart_data(stage2 if selected_branch != "All Locations" else stage2_all)
            if not decision_data.empty:
                st.subheader("Operational Decision Counts")
                st.bar_chart(decision_data)

            review_data = review_chart_data(stage2 if selected_branch != "All Locations" else stage2_all)
            if not review_data.empty:
                st.subheader("Review Required by Branch")
                pivot = review_data.pivot(index="Branch", columns="Review Required", values="Count").fillna(0)
                st.bar_chart(pivot)

        with dash_tab5:
            st.caption("Highest priority rows based on exposure and low confidence.")
            top_items = top_review_items(stage2_all, selected_branch, limit=25)
            top_cols = [
                "Final Operational Decision",
                "Review Bucket",
                "Branch",
                "Branch Date",
                "Branch Litres",
                "Extracted Branch RA",
                "Supplier Voucher",
                "Cars+ Match Result",
                "Charged Yes/No",
                "Estimated Exposure $",
                "Exposure Category",
                "Match Confidence %",
                "Confidence Reason"
            ]
            st.dataframe(top_items[[c for c in top_cols if c in top_items.columns]], use_container_width=True)
            st.download_button(
                "Download Top Review Items CSV",
                top_items.to_csv(index=False).encode("utf-8"),
                f"top_review_items_{selected_branch}.csv",
                "text/csv"
            )

    internal_nonrev = stage2[stage2["Final Operational Decision"] == "🚗 Repo / Internal Fuel"]

    tab1,tab_internal,tab2,tab3,tab4,tab5,tab6,tab7,tab8 = st.tabs([
        "✅ Correctly Processed",
        "🚗 Internal / NONREV",
        "🔥 Revenue Leakage",
        "🧠 Confidence Reviews",
        "⚠ Operational Decisions",
        "❌ Missed Charge",
        "🟡 Needs RA",
        "🟠 RA Not Found",
        "📊 Full Report"
    ])

    with tab1:
        st.dataframe(correctly_processed, use_container_width=True)

    with tab_internal:
        st.caption("Internal / NONREV fuel rows are treated as operationally passed where no customer Cars+ charge is required.")
        st.dataframe(internal_nonrev, use_container_width=True)
        st.download_button(
            "Download Internal NONREV CSV",
            internal_nonrev.to_csv(index=False).encode("utf-8"),
            f"internal_nonrev_{selected_branch}.csv",
            "text/csv"
        )

    with tab2:
        leakage_cols = [
            "Final Operational Decision",
            "Branch",
            "Branch Date",
            "Branch Litres",
            "Extracted Branch RA",
            "Supplier Date",
            "Supplier Voucher",
            "Cars+ Match Result",
            "Charged Yes/No",
            "Fuel Charged $",
            "Estimated Exposure $",
            "Exposure Category",
            "Match Confidence %",
            "Confidence Reason"
        ]
        st.dataframe(revenue_leakage[[c for c in leakage_cols if c in revenue_leakage.columns]], use_container_width=True)
        st.download_button("Download Revenue Leakage CSV", revenue_leakage.to_csv(index=False).encode("utf-8"), f"revenue_leakage_{selected_branch}.csv", "text/csv")

    with tab3:
        review_cols = [
            "Review Bucket",
            "Final Operational Decision",
            "Match Confidence %",
            "Confidence Reason",
            "Branch",
            "Branch Date",
            "Branch Litres",
            "Extracted Branch RA",
            "Supplier Voucher",
            "Cars+ Match Result",
            "Estimated Exposure $",
            "Exposure Category"
        ]
        st.dataframe(confidence_reviews[[c for c in review_cols if c in confidence_reviews.columns]], use_container_width=True)
        st.download_button("Download Confidence Reviews CSV", confidence_reviews.to_csv(index=False).encode("utf-8"), f"confidence_reviews_{selected_branch}.csv", "text/csv")

    with tab4:
        st.dataframe(stage2.sort_values(["Final Operational Decision", "Match Confidence %"], ascending=[True, True]), use_container_width=True)

    with tab5:
        st.dataframe(missed, use_container_width=True)

    with tab6:
        st.dataframe(needs_ra, use_container_width=True)

    with tab7:
        st.dataframe(ra_not_found, use_container_width=True)

    with tab8:
        st.dataframe(stage2, use_container_width=True)
        st.download_button("Download Stage 2 Phase 1 CSV", stage2.to_csv(index=False).encode("utf-8"), f"stage2_phase1_{selected_branch}.csv", "text/csv")
elif not stage1_all.empty:
    st.info("Upload Cars+ charge audit as PDF, Excel, or CSV to produce Stage 2 report.")
