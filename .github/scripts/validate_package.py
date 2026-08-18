#!/usr/bin/env python3
"""Package hygiene checks that need no Unity license.

Validates that package.json and every .asmdef parse as JSON, that assembly
definition names are unique, and that every asset Unity would import has a
.meta file (missing metas mean regenerated GUIDs and broken references for
everyone who installs the package).
"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
IGNORED_DIRS = {".git", ".github", ".idea", "obj"}
IGNORED_NAMES = {".gitignore", ".gitattributes"}

errors = []


def read_json(path):
    with open(path, encoding="utf-8-sig") as handle:
        try:
            return json.load(handle)
        except json.JSONDecodeError as error:
            errors.append(f"{os.path.relpath(path, ROOT)}: invalid JSON ({error})")
            return None


def walk():
    for current, dirs, files in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in IGNORED_DIRS and not d.endswith("~")]
        yield current, dirs, files


package = read_json(os.path.join(ROOT, "package.json"))
if package is not None:
    for field in ("name", "version", "displayName", "description", "unity"):
        if field not in package:
            errors.append(f"package.json: missing required field '{field}'")
    for sample in package.get("samples", []):
        path = os.path.join(ROOT, sample.get("path", ""))
        if not os.path.isdir(path):
            errors.append(f"package.json: sample path not found: {sample.get('path')}")

asmdef_names = {}
for current, dirs, files in walk():
    for name in files:
        path = os.path.join(current, name)
        relative = os.path.relpath(path, ROOT)

        if name.endswith(".asmdef"):
            data = read_json(path)
            if data is not None:
                assembly = data.get("name")
                if not assembly:
                    errors.append(f"{relative}: asmdef has no name")
                elif assembly in asmdef_names:
                    errors.append(f"{relative}: duplicate assembly name '{assembly}' "
                                  f"(also {asmdef_names[assembly]})")
                else:
                    asmdef_names[assembly] = relative

        if name.endswith(".meta") or name in IGNORED_NAMES:
            continue
        if not os.path.exists(path + ".meta"):
            errors.append(f"{relative}: missing .meta file")

    for name in dirs:
        path = os.path.join(current, name)
        if not os.path.exists(path + ".meta"):
            errors.append(f"{os.path.relpath(path, ROOT)}/: missing .meta file")

if errors:
    print("Package validation failed:\n")
    for error in errors:
        print(f"  - {error}")
    sys.exit(1)

print(f"Package validation passed ({len(asmdef_names)} assemblies).")
