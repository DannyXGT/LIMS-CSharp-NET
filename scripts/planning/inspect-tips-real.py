"""Read-only inventory of authorized TIPS examples; no workbook modifications."""
import collections
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

NS = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}


def read(path):
    with zipfile.ZipFile(path) as archive:
        strings = ["".join(si.itertext()) for si in ET.fromstring(archive.read("xl/sharedStrings.xml")).findall("m:si", NS)] if "xl/sharedStrings.xml" in archive.namelist() else []
        for part in archive.namelist():
            if not (part.startswith("xl/worksheets/") and part.endswith(".xml")):
                continue
            header = None
            result = []
            for row in ET.fromstring(archive.read(part)).findall(".//m:row", NS):
                cells = {}
                for cell in row.findall("m:c", NS):
                    value = cell.find("m:v", NS)
                    value = value.text if value is not None else "".join(cell.itertext())
                    if cell.attrib.get("t") == "s":
                        value = strings[int(value)]
                    cells["".join(ch for ch in cell.attrib["r"] if ch.isalpha())] = (value or "").strip()
                if header is None:
                    if "Report #" in cells.values() or "Report_no" in cells.values():
                        header = cells
                else:
                    result.append({name: cells.get(key, "") for key, name in header.items()})
            if header:
                return result
    raise ValueError("No verified TIPS header")


if __name__ == "__main__":
    root = Path.home()
    paths = {
        "summary13": root / "Desktop/Backup/a0_-_pro-invoice_summary_13Nov2026104743316.xlsx",
        "report13": root / "Desktop/Backup/a0_-_delay_report_list_based_on_due_date_13Nov2026103900894.xlsx",
        "summary17": root / "Downloads/a0_-_pro-invoice_summary_17Nov2026045852237.xlsx",
        "report17": root / "Downloads/a0_-_delay_report_list_based_on_due_date_17Nov2026045945888.xlsx",
    }
    data = {name: read(path) for name, path in paths.items()}
    summaries = {day: {r["Report #"].upper(): r for r in data["summary" + day] if r.get("Report #")} for day in ("13", "17")}
    reports = {day: {r["Report_no"].upper() for r in data["report" + day] if r.get("Report_no")} for day in ("13", "17")}
    both = sorted(summaries["17"].keys() & reports["17"])
    summary_only = sorted(summaries["13"].keys() - reports["13"])
    future = sorted(set(summary_only) & reports["17"])
    changed = [{"job": j, "before": summaries["13"][j]["Sample Status"], "after": summaries["17"][j]["Sample Status"]} for j in sorted(summaries["13"].keys() & summaries["17"].keys()) if summaries["13"][j]["Sample Status"] != summaries["17"][j]["Sample Status"]]
    repeats = collections.Counter((r["Report_no"], r["Test_Name"]) for r in data["report17"] if r.get("Report_no"))
    result = {
        "files": {name: {"path": str(paths[name]), "rows": len(rows), "headers": list(rows[0])} for name, rows in data.items()},
        "both": both[:5], "summaryOnly": summary_only[:5],
        "summaryOnlyThenReport": future[:5],
        "reportOnly": sorted(reports["17"] - summaries["17"].keys())[:5],
        "missingVendor": [j for j, r in summaries["17"].items() if not r.get("Vendor")][:5],
        "stateChanges": changed[:10], "stateChangeCount": len(changed),
        "repeatedRows": [{"job": j, "test": t, "count": n} for (j, t), n in repeats.items() if n > 1][:5],
        "revisionJobs": [j for j in both if "-REV" in j][:5],
    }
    target = Path(__file__).resolve().parents[2] / "artifacts/planificacion-dev/importacion-real/source-inspection.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({k: v for k, v in result.items() if k != "files"}, ensure_ascii=True, indent=2))
