#!/usr/bin/env python3
"""Restore only omitted Elephant assets from the pinned local starter clone."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
STARTER = ROOT / "DigiPhantStarter"
COMMIT = "4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f"


def main():
    revision = subprocess.check_output(
        ["git", "-C", str(STARTER), "rev-parse", "HEAD"], text=True
    ).strip()
    if revision != COMMIT:
        raise SystemExit(f"Starter must be checked out at {COMMIT}; found {revision}")
    entries = json.loads((STARTER / "asset-checksums.json").read_text())
    entries = {name: digest for name, digest in entries.items()
               if name.startswith("Elephant/") or name == "Elephant.meta"}
    for name, digest in entries.items():
        if hashlib.sha256((STARTER / name).read_bytes()).hexdigest() != digest:
            raise SystemExit(f"Source checksum mismatch: {name}")
    destination = ROOT / "Assets"
    targets = [destination / "Elephant", destination / "Elephant.meta"]
    if any(path.exists() for path in targets):
        if all((destination / name).is_file() and
               hashlib.sha256((destination / name).read_bytes()).hexdigest() == digest
               for name, digest in entries.items()):
            print("Elephant assets already present and verified; nothing changed.")
            return
        raise SystemExit("Existing Elephant assets differ or are incomplete; preserve and reconcile them manually.")
    destination.mkdir(parents=True, exist_ok=True)
    shutil.copytree(STARTER / "Elephant", targets[0])
    shutil.copy2(STARTER / "Elephant.meta", targets[1])
    for name, digest in entries.items():
        if hashlib.sha256((destination / name).read_bytes()).hexdigest() != digest:
            raise SystemExit(f"Restored checksum mismatch: {name}")
    print(f"Restored and verified {len(entries)} Elephant files. DigiPhant assets were untouched.")


if __name__ == "__main__":
    main()
