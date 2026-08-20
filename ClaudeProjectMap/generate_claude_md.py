#!/usr/bin/env python3
"""
generate_claude_md.py
----------------------
Scans a Unity project and writes/updates a CLAUDE.md file describing:
  - every C# script (class name, base class, best-effort description)
  - every prefab (which scripts are attached)
  - every scene (which prefabs + scripts appear in it)

CLAUDE.md is both:
  1. A human-readable summary (counts + instructions), and
  2. A machine-readable ```json:project-data``` block that index.html reads
     to draw the visual map.

Re-running this script is safe: it re-scans the project but preserves any
descriptions, notes, or manual edits you (or the visual map) already saved
into the JSON block, matched by stable file-path-based IDs.

Usage:
    python3 generate_claude_md.py                 # run from inside the Unity project root
    python3 generate_claude_md.py /path/to/project

Restricting what gets scanned:
    By default every .cs/.prefab/.unity file under Assets/ is scanned. If your
    project mixes your own code with third-party assets (asset-store packages,
    plugins, examples, etc.), that's noisy. Edit claude-map.config.json (written
    next to this script the first time you run it) to point each type at just
    the folder(s) you actually want, e.g.:

        { "scripts_dirs": ["Assets/Scripts"],
          "prefabs_dirs": ["Assets/Prefabs"],
          "scenes_dirs":  ["Assets/Scenes"] }

    Leave a list empty ([]) to scan all of Assets for that type. You can also
    override the config for a single run with flags:
        python3 generate_claude_md.py --scripts-dir Assets/Scripts --scripts-dir Assets/Core
"""

import argparse
import json
import os
import re
import sys
from datetime import datetime, timezone

GUID_RE = re.compile(r"guid:\s*([0-9a-fA-F]{32})")
SCRIPT_REF_RE = re.compile(r"m_Script:\s*\{fileID:\s*-?\d+,\s*guid:\s*([0-9a-fA-F]{32})")
PREFAB_REF_RE = re.compile(r"m_SourcePrefab:\s*\{fileID:\s*-?\d+,\s*guid:\s*([0-9a-fA-F]{32})")
CLASS_RE = re.compile(
    r"^\s*(?:public|internal|private|protected)?\s*(?:abstract\s+|sealed\s+|static\s+|partial\s+)*class\s+"
    r"(\w+)\s*(?::\s*([\w<>,.\s]+))?"
)
FIELD_TYPE_RE = re.compile(
    r"\[SerializeField\]\s*(?:\r?\n\s*)?"
    r"(?:private|public|protected|internal)?\s*([A-Za-z_]\w*)\s+\w+\s*[;=]"
)

CARD_W, CARD_H = 220, 120
COL_X = {"scene": 80, "prefab": 460, "script": 840}
ROW_GAP = 110
ROW_START = 80

CONFIG_FILENAME = "claude-map.config.json"
DEFAULT_CONFIG = {
    "_readme": (
        "Restrict scanning to specific folders per type. Leave a list empty ([]) "
        "to scan all of Assets for that type. Paths are relative to your Unity "
        "project root (the folder that contains Assets), e.g. 'Assets/Scripts'. "
        "You can list more than one folder per type."
    ),
    "scripts_dirs": [],
    "prefabs_dirs": [],
    "scenes_dirs": [],
}


def load_or_create_config(script_dir):
    config_path = os.path.join(script_dir, CONFIG_FILENAME)
    if not os.path.exists(config_path):
        with open(config_path, "w", encoding="utf-8") as fh:
            json.dump(DEFAULT_CONFIG, fh, indent=2)
        print(f"  wrote default {CONFIG_FILENAME} (scanning all of Assets/ for now)")
        print(f"  edit it to point 'scripts_dirs' / 'prefabs_dirs' / 'scenes_dirs' at specific folders")
        return dict(DEFAULT_CONFIG), config_path
    try:
        with open(config_path, "r", encoding="utf-8") as fh:
            cfg = json.load(fh)
    except (OSError, json.JSONDecodeError) as e:
        print(f"  couldn't read {CONFIG_FILENAME} ({e}) — scanning all of Assets/")
        return dict(DEFAULT_CONFIG), config_path
    return cfg, config_path


def normalize_dirs(dirs):
    out = []
    for d in dirs or []:
        d = str(d).replace("\\", "/").strip().strip("/")
        if d:
            out.append(d)
    return out


def path_allowed(rel_path, allowed_dirs):
    """True if rel_path (relative to project root, forward slashes) is inside
    one of allowed_dirs, or if allowed_dirs is empty (meaning: no restriction)."""
    if not allowed_dirs:
        return True
    rel_path = rel_path.replace("\\", "/")
    for d in allowed_dirs:
        if rel_path == d or rel_path.startswith(d + "/"):
            return True
    return False


def rel(path, root):
    return os.path.relpath(path, root).replace(os.sep, "/")


def build_guid_map(assets_dir):
    """Map guid -> relative asset path (the file the .meta describes)."""
    guid_map = {}
    for dirpath, _dirs, files in os.walk(assets_dir):
        for f in files:
            if f.endswith(".meta"):
                meta_path = os.path.join(dirpath, f)
                asset_path = meta_path[: -len(".meta")]
                if not os.path.exists(asset_path):
                    continue
                try:
                    with open(meta_path, "r", encoding="utf-8", errors="ignore") as fh:
                        text = fh.read()
                    m = GUID_RE.search(text)
                    if m:
                        guid_map[m.group(1).lower()] = asset_path
                except OSError:
                    continue
    return guid_map


def extract_description(lines, class_line_idx):
    """Look upward from the class declaration for a contiguous comment block."""
    comments = []
    i = class_line_idx - 1
    # skip attribute lines like [System.Serializable] directly above the class
    while i >= 0 and lines[i].strip().startswith("["):
        i -= 1
    # /* ... */ block just above
    if i >= 0 and lines[i].strip().endswith("*/"):
        j = i
        while j >= 0 and "/*" not in lines[j]:
            comments.insert(0, lines[j])
            j -= 1
        if j >= 0:
            comments.insert(0, lines[j])
        text = "\n".join(comments)
        text = re.sub(r"/\*+|\*+/", "", text)
        text = re.sub(r"^\s*\*\s?", "", text, flags=re.MULTILINE)
        return text.strip()
    # /// or // lines just above
    while i >= 0 and (lines[i].strip().startswith("///") or lines[i].strip().startswith("//")):
        comments.insert(0, lines[i].strip().lstrip("/").strip())
        i -= 1
    text = " ".join(c for c in comments if c).strip()
    text = re.sub(r"</?summary>", "", text, flags=re.IGNORECASE)
    text = re.sub(r"\s+", " ", text).strip()
    return text


def scan_scripts(assets_dir, root, allowed_dirs=None):
    scripts = {}  # id -> node
    class_to_id = {}  # class name -> id (for cross-references)
    guid_to_scriptfile = {}

    cs_files = []
    for dirpath, _dirs, files in os.walk(assets_dir):
        for f in files:
            if f.endswith(".cs"):
                full = os.path.join(dirpath, f)
                if path_allowed(rel(full, root), allowed_dirs):
                    cs_files.append(full)

    raw_texts = {}
    for path in cs_files:
        try:
            with open(path, "r", encoding="utf-8", errors="ignore") as fh:
                raw_texts[path] = fh.read()
        except OSError:
            continue

    for path, text in raw_texts.items():
        lines = text.splitlines()
        best_match_idx = None
        best_name, best_base = None, None
        for idx, line in enumerate(lines):
            m = CLASS_RE.match(line)
            if m:
                best_match_idx, best_name, best_base = idx, m.group(1), m.group(2)
                # prefer a class whose name matches the filename (Unity convention)
                if best_name == os.path.splitext(os.path.basename(path))[0]:
                    break
        if best_name is None:
            continue

        base_class = None
        if best_base:
            base_class = best_base.split(",")[0].strip()

        description = extract_description(lines, best_match_idx)
        node_id = "script:" + rel(path, root)
        node = {
            "id": node_id,
            "type": "script",
            "name": best_name,
            "file": rel(path, root),
            "baseClass": base_class or "",
            "description": description,
            "references": [],
        }
        scripts[node_id] = node
        class_to_id[best_name] = node_id

    # second pass: resolve [SerializeField] field types into reference edges
    for path, text in raw_texts.items():
        node_id = "script:" + rel(path, root)
        if node_id not in scripts:
            continue
        refs = set()
        for m in FIELD_TYPE_RE.finditer(text):
            type_name = m.group(1)
            target_id = class_to_id.get(type_name)
            if target_id and target_id != node_id:
                refs.add(target_id)
        scripts[node_id]["references"] = sorted(refs)

    return scripts


def scan_prefabs(assets_dir, root, guid_map, script_nodes, allowed_dirs=None):
    prefabs = {}
    guid_to_script_id = {}

    # build guid -> script node id using guid_map (guid -> asset path) reversed
    path_to_guid = {}
    for g, p in guid_map.items():
        path_to_guid[os.path.normpath(p)] = g
    for node_id, node in script_nodes.items():
        full_path = os.path.join(root, node["file"])
        g = path_to_guid.get(os.path.normpath(full_path))
        if g:
            guid_to_script_id[g] = node_id

    for dirpath, _dirs, files in os.walk(assets_dir):
        for f in files:
            if f.endswith(".prefab"):
                path = os.path.join(dirpath, f)
                if not path_allowed(rel(path, root), allowed_dirs):
                    continue
                try:
                    with open(path, "r", encoding="utf-8", errors="ignore") as fh:
                        text = fh.read()
                except OSError:
                    continue
                attached = set()
                for m in SCRIPT_REF_RE.finditer(text):
                    guid = m.group(1).lower()
                    sid = guid_to_script_id.get(guid)
                    if sid:
                        attached.add(sid)
                node_id = "prefab:" + rel(path, root)
                prefabs[node_id] = {
                    "id": node_id,
                    "type": "prefab",
                    "name": os.path.splitext(f)[0],
                    "file": rel(path, root),
                    "description": "",
                    "scripts": sorted(attached),
                }
    return prefabs, guid_to_script_id


def scan_scenes(assets_dir, root, guid_map, prefab_nodes, guid_to_script_id, allowed_dirs=None):
    scenes = {}
    path_to_guid = {}
    for g, p in guid_map.items():
        path_to_guid[os.path.normpath(p)] = g
    guid_to_prefab_id = {}
    for node_id, node in prefab_nodes.items():
        full_path = os.path.join(root, node["file"])
        g = path_to_guid.get(os.path.normpath(full_path))
        if g:
            guid_to_prefab_id[g] = node_id

    for dirpath, _dirs, files in os.walk(assets_dir):
        for f in files:
            if f.endswith(".unity"):
                path = os.path.join(dirpath, f)
                if not path_allowed(rel(path, root), allowed_dirs):
                    continue
                try:
                    with open(path, "r", encoding="utf-8", errors="ignore") as fh:
                        text = fh.read()
                except OSError:
                    continue
                prefabs_used = set()
                for m in PREFAB_REF_RE.finditer(text):
                    guid = m.group(1).lower()
                    pid = guid_to_prefab_id.get(guid)
                    if pid:
                        prefabs_used.add(pid)
                scripts_used = set()
                for m in SCRIPT_REF_RE.finditer(text):
                    guid = m.group(1).lower()
                    sid = guid_to_script_id.get(guid)
                    if sid:
                        scripts_used.add(sid)
                node_id = "scene:" + rel(path, root)
                scenes[node_id] = {
                    "id": node_id,
                    "type": "scene",
                    "name": os.path.splitext(f)[0],
                    "file": rel(path, root),
                    "description": "",
                    "prefabs": sorted(prefabs_used),
                    "scripts": sorted(scripts_used),
                }
    return scenes


def load_existing(claude_md_path):
    if not os.path.exists(claude_md_path):
        return None
    try:
        with open(claude_md_path, "r", encoding="utf-8") as fh:
            text = fh.read()
    except OSError:
        return None
    m = re.search(r"```json:project-data\s*\n(.*?)\n```", text, re.DOTALL)
    if not m:
        return None
    try:
        return json.loads(m.group(1))
    except json.JSONDecodeError:
        return None


def assign_layout(nodes_by_id):
    counters = {"script": 0, "prefab": 0, "scene": 0}
    for node in nodes_by_id.values():
        if node.get("pos"):
            continue
        col = COL_X.get(node["type"])
        if col is None:
            continue
        idx = counters.get(node["type"], 0)
        node["pos"] = {"x": col, "y": ROW_START + idx * ROW_GAP}
        counters[node["type"]] = idx + 1


def merge_with_existing(new_nodes, old_data):
    """Carry over descriptions/positions/notes from a previous CLAUDE.md.

    Scanned types (script/prefab/scene) that no longer show up this run are
    dropped — either the file is gone, or a folder filter in
    claude-map.config.json now excludes it, and either way the current scan
    should win. Anything the scanner never produces itself (sticky notes,
    or any other custom node type) is always carried over untouched.
    """
    if not old_data:
        return list(new_nodes.values())

    SCANNED_TYPES = {"script", "prefab", "scene"}
    old_by_id = {n["id"]: n for n in old_data.get("nodes", [])}
    merged = []

    for node_id, node in new_nodes.items():
        old = old_by_id.get(node_id)
        if old:
            if not node.get("description") and old.get("description"):
                node["description"] = old["description"]
            if old.get("pos"):
                node["pos"] = old["pos"]
            for extra_key in ("tags", "color"):
                if extra_key in old:
                    node[extra_key] = old[extra_key]
        merged.append(node)

    # carry over anything the scanner never produces itself (sticky notes, or
    # any other custom node type) — but NOT script/prefab/scene nodes that
    # weren't found this run, since those are meant to disappear when a file
    # is deleted or a folder filter now excludes them.
    new_ids = set(new_nodes.keys())
    for old_id, old_node in old_by_id.items():
        if old_id in new_ids:
            continue
        if old_node.get("type") in SCANNED_TYPES:
            continue
        merged.append(old_node)

    return merged


def render_markdown(project_name, nodes, generated_at):
    counts = {"script": 0, "prefab": 0, "scene": 0, "note": 0}
    for n in nodes:
        counts[n["type"]] = counts.get(n["type"], 0) + 1

    data = {
        "project": project_name,
        "generatedAt": generated_at,
        "nodes": nodes,
    }

    lines = []
    lines.append(f"# Claude Project Map — {project_name}")
    lines.append("")
    lines.append(
        f"_Auto-generated by `generate_claude_md.py` on {generated_at}. "
        "Re-run it any time after adding/renaming scripts, prefabs, or scenes — "
        "descriptions and sticky notes below are preserved across re-runs._"
    )
    lines.append("")
    lines.append("## How to use this file")
    lines.append(
        "Open `index.html` (in this same folder) in a browser to see this data "
        "as a draggable, zoomable visual map — like a Miro board for your Unity "
        "project. You can add sticky notes to brainstorm, edit descriptions, and "
        "save changes straight back into this file."
    )
    lines.append("")
    lines.append("## Summary")
    lines.append(f"- **Scripts:** {counts.get('script', 0)}")
    lines.append(f"- **Prefabs:** {counts.get('prefab', 0)}")
    lines.append(f"- **Scenes:** {counts.get('scene', 0)}")
    if counts.get("note", 0):
        lines.append(f"- **Sticky notes:** {counts.get('note', 0)}")
    lines.append("")
    lines.append(
        "## Project data\n"
        "_(machine-readable — index.html reads this block; edit it by hand only if you're comfortable with JSON)_"
    )
    lines.append("")
    lines.append("```json:project-data")
    lines.append(json.dumps(data, indent=2))
    lines.append("```")
    lines.append("")
    return "\n".join(lines)


def build_arg_parser():
    p = argparse.ArgumentParser(description="Scan a Unity project and write CLAUDE.md")
    p.add_argument("project_root", nargs="?", default=None, help="Unity project root (contains Assets/). Defaults to current directory.")
    p.add_argument("--scripts-dir", action="append", default=None, help="Only scan scripts under this folder (relative to project root). Repeatable.")
    p.add_argument("--prefabs-dir", action="append", default=None, help="Only scan prefabs under this folder. Repeatable.")
    p.add_argument("--scenes-dir", action="append", default=None, help="Only scan scenes under this folder. Repeatable.")
    return p


def main():
    args = build_arg_parser().parse_args()
    root = os.path.abspath(args.project_root) if args.project_root else os.getcwd()
    assets_dir = os.path.join(root, "Assets")
    if not os.path.isdir(assets_dir):
        print(f"Couldn't find an 'Assets' folder inside {root}.")
        print("Run this script from your Unity project root, or pass the path as an argument:")
        print("    python3 generate_claude_md.py /path/to/UnityProject")
        sys.exit(1)

    script_dir = os.path.dirname(os.path.abspath(__file__))
    config, config_path = load_or_create_config(script_dir)

    scripts_dirs = normalize_dirs(args.scripts_dir if args.scripts_dir is not None else config.get("scripts_dirs"))
    prefabs_dirs = normalize_dirs(args.prefabs_dir if args.prefabs_dir is not None else config.get("prefabs_dirs"))
    scenes_dirs = normalize_dirs(args.scenes_dir if args.scenes_dir is not None else config.get("scenes_dirs"))

    def describe(label, dirs):
        return f"{label}: {', '.join(dirs)}" if dirs else f"{label}: all of Assets/"

    print(f"Scanning {assets_dir} ...")
    print("  " + describe("scripts", scripts_dirs))
    print("  " + describe("prefabs", prefabs_dirs))
    print("  " + describe("scenes", scenes_dirs))

    guid_map = build_guid_map(assets_dir)
    scripts = scan_scripts(assets_dir, root, scripts_dirs)
    prefabs, guid_to_script_id = scan_prefabs(assets_dir, root, guid_map, scripts, prefabs_dirs)
    scenes = scan_scenes(assets_dir, root, guid_map, prefabs, guid_to_script_id, scenes_dirs)

    print(f"  found {len(scripts)} scripts, {len(prefabs)} prefabs, {len(scenes)} scenes")
    if not (scripts_dirs or prefabs_dirs or scenes_dirs):
        print(f"  tip: too much noise? edit {config_path} to point each type at a specific folder.")

    all_nodes = {}
    all_nodes.update(scripts)
    all_nodes.update(prefabs)
    all_nodes.update(scenes)

    claude_md_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "CLAUDE.md")
    old_data = load_existing(claude_md_path)
    if old_data:
        print("  found existing CLAUDE.md — preserving descriptions, notes, and layout")

    merged_nodes = merge_with_existing(all_nodes, old_data)

    nodes_by_id = {n["id"]: n for n in merged_nodes}
    assign_layout(nodes_by_id)

    project_name = os.path.basename(root)
    generated_at = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    markdown = render_markdown(project_name, list(nodes_by_id.values()), generated_at)

    with open(claude_md_path, "w", encoding="utf-8") as fh:
        fh.write(markdown)

    print(f"Wrote {claude_md_path}")
    print("Open index.html in this folder (in a browser) to see the visual map.")


if __name__ == "__main__":
    main()
