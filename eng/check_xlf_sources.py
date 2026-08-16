#!/usr/bin/env python3
"""CI guard: every <trans-unit> in every .xlf file must have a non-empty <source>.

Translation reconstruction depends entirely on <source> staying populated in
every language file. A tooling change that silently strips it would be
irreversible data loss, so this check is deliberately strict and fails loudly
on anything unexpected (no files found, unparsable XML, etc).
"""

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

MAX_LISTED = 50


def local_name(tag):
    # Strip the "{namespace}" prefix ElementTree adds, if present.
    return tag.rsplit("}", 1)[-1]


def check_file(path):
    """Return a list of trans-unit ids in `path` with a missing/empty <source>."""
    try:
        tree = ET.parse(path)
    except ET.ParseError as e:
        raise SystemExit(f"FATAL: failed to parse {path}: {e}")

    root = tree.getroot()
    violations = []
    unit_count = 0
    for elem in root.iter():
        if local_name(elem.tag) != "trans-unit":
            continue
        unit_count += 1
        unit_id = elem.get("id", "<no id>")
        source = None
        for child in elem:
            if local_name(child.tag) == "source":
                source = child
                break
        if source is None:
            violations.append(unit_id)
            continue
        text = "".join(source.itertext()).strip()
        if not text:
            violations.append(unit_id)
    return unit_count, violations


def main():
    if len(sys.argv) > 2:
        raise SystemExit(f"usage: {sys.argv[0]} [scan-dir]")

    if len(sys.argv) == 2:
        scan_dir = Path(sys.argv[1])
    else:
        repo_root = Path(__file__).resolve().parent.parent
        scan_dir = repo_root / "src" / "app" / "GitUI" / "Translation"

    files = sorted(scan_dir.glob("*.xlf"))
    if not files:
        raise SystemExit(f"FATAL: no .xlf files found in {scan_dir}")

    total_units = 0
    all_violations = []  # list of (file, id)
    for f in files:
        unit_count, violations = check_file(f)
        total_units += unit_count
        for unit_id in violations:
            all_violations.append((f, unit_id))

    if all_violations:
        print(f"FAIL: {len(all_violations)} trans-unit(s) with missing/empty <source>:", file=sys.stderr)
        for f, unit_id in all_violations[:MAX_LISTED]:
            print(f"  {f}: id={unit_id!r}", file=sys.stderr)
        remaining = len(all_violations) - MAX_LISTED
        if remaining > 0:
            print(f"  ...and {remaining} more", file=sys.stderr)
        sys.exit(1)

    if total_units == 0:
        raise SystemExit(f"FATAL: {len(files)} .xlf file(s) but no <trans-unit> elements found in {scan_dir}")

    print(f"OK: {len(files)} .xlf file(s), {total_units} trans-unit(s) all have non-empty <source>.")


if __name__ == "__main__":
    main()
