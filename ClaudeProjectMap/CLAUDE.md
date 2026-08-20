# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Claude Project Map is a standalone, three-file, dependency-free tool that generates a visual "Miro-style" map of a Unity project (scripts, prefabs, scenes, and how they reference each other), backed by a `CLAUDE.md` file that Claude Code can also read as project context. This `ClaudeProjectMap/` folder is meant to be dropped into a Unity project's root (next to `Assets/`), not built or installed.

No build step, no package manager, no dependencies beyond Python 3's standard library and a browser.

## Files

- `generate_claude_md.py` — scans a Unity project and writes/updates the *generated* `CLAUDE.md` (project map data), not this file.
- `index.html` — single-file vanilla JS/HTML/CSS app; the visual map itself, opened directly in a browser.
- `claude-map.config.json` — per-project scan scope (`scripts_dirs` / `prefabs_dirs` / `scenes_dirs`); auto-created with empty lists (scan everything) on first run.
- `README.md` — user-facing setup/usage docs; authoritative for workflow details.

Note: there are two different `CLAUDE.md` files in play. This one (hand-maintained, describes the tool's own codebase) lives with the source. The *generated* one is written into a target Unity project by `generate_claude_md.py` and contains a machine-readable project map, not this content.

## Commands

Run the scanner against a Unity project (a folder containing `Assets/`):

```bash
python3 generate_claude_md.py /path/to/UnityProject
# or, if already inside the Unity project root:
python3 generate_claude_md.py
```

Restrict scanning to specific folders for one run only (overrides `claude-map.config.json` without persisting):

```bash
python3 generate_claude_md.py . --scripts-dir Assets/Scripts --scripts-dir Assets/Core
```

Serve the map over HTTP so `index.html` auto-fetches its `CLAUDE.md` (needed on browsers that block `file://` fetches):

```bash
python3 -m http.server 8000
```

There is no test suite, linter, or build/package script in this repo.

## Architecture

**Scan pipeline (`generate_claude_md.py`)**, run in this order from `main()`:

1. `build_guid_map` — walks `Assets/`, reads every `.meta` file's `guid:` line, maps `guid -> asset path`. Unity's YAML `.meta`/`.prefab`/`.unity`/`.asset` files use GUIDs (not paths) to reference other assets, so this is the foundation for cross-linking.
2. `scan_scripts` — regex-parses every `.cs` file for a `class` declaration (preferring the class matching the filename, per Unity convention), its base class, and a best-effort description pulled from the `///`/`//`/`/* */` comment immediately above it. Also finds `[SerializeField]` fields whose type matches another scanned script's class name, turning those into `references` edges between script nodes.
3. `scan_prefabs` — regex-parses `.prefab` YAML for `m_Script: {fileID, guid}` entries, resolves each guid through the guid map to figure out which scanned scripts are attached to each prefab.
4. `scan_scenes` — regex-parses `.unity` YAML for `m_SourcePrefab` (prefab instances) and `m_Script` (loose script references) guids, resolving them to prefab/script node IDs.
5. `merge_with_existing` — loads the previous generated `CLAUDE.md`'s JSON block (if any) and carries forward descriptions, canvas positions (`pos`), and extra keys (`tags`, `color`) for nodes that still exist. Sticky notes (`type: "note"`, never produced by the scanner) and any other non-scanned node type are always carried over untouched. Scanned-type nodes (`script`/`prefab`/`scene`) that no longer show up — deleted file, or newly excluded by `claude-map.config.json` — are dropped, by design.
6. `assign_layout` — gives new nodes a default grid position (scripts/prefabs/scenes in separate columns); existing `pos` values are left alone.
7. `render_markdown` — writes human-readable prose + a fenced ` ```json:project-data ` block into the target `CLAUDE.md`.

This is a **heuristic regex scanner over Unity's YAML, not a real Unity/C# parse** — it deliberately trades completeness for zero dependencies. It will miss nested prefab variants, runtime-added components, `AddComponent<T>()` calls, and similar dynamic patterns. Treat scan results as a first draft.

Folder scoping (`scripts_dirs`/`prefabs_dirs`/`scenes_dirs` in `claude-map.config.json`, or the `--scripts-dir`/`--prefabs-dir`/`--scenes-dir` flags) is applied per-type via `path_allowed()`, independently for scripts, prefabs, and scenes — a project can, e.g., scan all scripts under `Assets/Scripts` while restricting prefabs to `Assets/Prefabs`.

**Generated `CLAUDE.md` schema**: prose above a single ` ```json:project-data ` fenced block containing `{ "project", "generatedAt", "nodes": [...] }`. Each node has a stable `id` (`"{type}:{relative/file/path}"`), a `type` (`script`/`prefab`/`scene`/`note`), and type-specific edge arrays (`references` for scripts, `scripts` for prefabs, `prefabs`+`scripts` for scenes) that point at other node `id`s — this is what `index.html` draws connecting lines from. IDs are path-based and stable across re-runs, which is what makes the merge-and-preserve logic in step 5 possible.

**`index.html`**: self-contained client app (no bundler) that fetches/parses the generated `CLAUDE.md`, renders nodes as draggable/zoomable cards on a canvas grouped by type/column, draws reference lines from the edge arrays, and lets users edit descriptions, add sticky notes, and save changes back into the `CLAUDE.md` file (via File System Access API on Chrome/Edge when opened through its "Open CLAUDE.md" button, otherwise via download) — all state also mirrors to browser local storage as a crash-safety net.
