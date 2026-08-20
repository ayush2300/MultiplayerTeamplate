# Claude Project Map

A visual, Miro-style map of your Unity project: every script, prefab, and
scene, what references what, plus sticky notes for brainstorming. It's
built from a `CLAUDE.md` file, so it doubles as project context you can
also hand to Claude Code.

Three files, no build step, no dependencies:

```
ClaudeProjectMap/
  generate_claude_md.py   <- scans your Unity project, writes CLAUDE.md
  CLAUDE.md                <- generated (not included yet — see below)
  index.html               <- the visual map itself, open this in a browser
  README.md                <- this file
```

## 1. Put this folder in your Unity project

Drop the whole `ClaudeProjectMap` folder anywhere in your project — the
project **root** (next to `Assets/`) is the cleanest spot, so Unity's
importer never touches it. It's just `.py`, `.md`, and `.html` files;
Unity won't do anything with them either way.

## 2. Generate CLAUDE.md

You need Python 3 (no extra packages). From inside the project root:

```bash
python3 ClaudeProjectMap/generate_claude_md.py .
```

(Or `cd` into your project root first and run
`python3 ClaudeProjectMap/generate_claude_md.py` with no argument.)

This scans every `.cs`, `.prefab`, and `.unity` file under `Assets/` and
writes `ClaudeProjectMap/CLAUDE.md`, containing:

- Every **script**: class name, base class (`MonoBehaviour`, etc.), and a
  best-effort description pulled from the `///` or `//` comment right
  above the class.
- Every **prefab**: which of your scripts are attached to it.
- Every **scene**: which prefabs and scripts appear in it.

It's a heuristic scanner (regex over Unity's YAML, not a real Unity
parse), so it'll miss anything unusual — nested prefab variants, scripts
added purely at runtime, add-component-by-code, etc. Treat it as a strong
first draft, not ground truth, and fill in the gaps by hand in the visual
map.

**Re-run it any time** after adding, renaming, or deleting
scripts/prefabs/scenes. It merges into the existing `CLAUDE.md` rather
than overwriting it — any descriptions you've written and any sticky
notes you've added are preserved.

### Only scanning specific folders

Most projects mix your own code with third-party stuff — asset-store
packages, plugins, an `Examples` folder from some asset you imported —
and by default all of it gets scanned, which gets noisy fast.

The first time you run the script, it writes `claude-map.config.json`
next to it:

```json
{
  "scripts_dirs": [],
  "prefabs_dirs": [],
  "scenes_dirs": []
}
```

An empty list means "scan all of `Assets/` for this type." Fill in the
folder(s) you actually want (relative to your project root), for example:

```json
{
  "scripts_dirs": ["Assets/Scripts"],
  "prefabs_dirs": ["Assets/Prefabs"],
  "scenes_dirs": ["Assets/Scenes"]
}
```

You can list more than one folder per type. Save the file and re-run the
script — anything outside those folders drops out of the map (any
description you'd written for it goes with it, so it's worth double
checking the folder names before you run it for real).

For a one-off run without touching the config file, use flags instead:

```bash
python3 generate_claude_md.py . --scripts-dir Assets/Scripts --scripts-dir Assets/Core
```

(Flags override the config file for that run only; they don't save back
into it.)

## 3. Open the map

Just double-click `index.html` to open it in your browser.

- If your browser blocks a local file from loading `CLAUDE.md`
  automatically (some do, over `file://`), click **Open CLAUDE.md** in
  the toolbar, or drag the file straight onto the canvas.
- If you'd rather it load automatically every time, serve the folder over
  local HTTP instead of double-clicking it:

  ```bash
  cd ClaudeProjectMap
  python3 -m http.server 8000
  ```

  then open `http://localhost:8000` in your browser. `index.html` will
  fetch `CLAUDE.md` on its own.

## Using the map

- **Pan** by dragging the empty background. **Zoom** with the scroll
  wheel.
- **Drag** any card to reposition it.
- **Click** a script/prefab/scene card to open a side panel where you can
  write or edit its description, and see what references it / what it
  references.
- **+ Sticky note** drops a freeform note anywhere on the board — for
  brainstorming, TODOs, "what if" ideas, whatever. Notes aren't touched by
  the scanner, so they survive every re-run.
- **Search** filters and highlights matching cards by name, path, or
  description.
- The colored chips in the toolbar toggle whole categories (Scripts /
  Prefabs / Scenes / Notes) on and off.
- **Tidy layout** snaps scripts/prefabs/scenes back into neat columns
  (sticky notes are left where you put them).

## Saving your edits

- **Chrome / Edge**: clicking **Open CLAUDE.md** (instead of just letting
  it auto-load) grants the page permission to write the file directly.
  After that, **Save** writes straight back into `CLAUDE.md` — no
  download step.
- **Other browsers**, or if you loaded via drag-and-drop / auto-fetch:
  **Save** downloads an updated `CLAUDE.md` — just move it into
  `ClaudeProjectMap/`, replacing the old one.
- Either way, edits are also auto-saved to your browser's local storage
  as you work, so a refresh or accidental tab close won't lose anything —
  you'll be offered a chance to restore it next time you open the page.

## Notes on the schema

`CLAUDE.md` is a normal Markdown file with one fenced code block:

    ```json:project-data
    { "project": "...", "nodes": [ ... ] }
    ```

Everything above that block is just human-readable prose (safe to edit
freely). Everything inside it is what `index.html` actually reads. Each
node has a `type` (`script` / `prefab` / `scene` / `note`), a stable `id`,
and — depending on type — `references`, `scripts`, or `prefabs` arrays
that point at other node IDs. That's what draws the connecting lines.

Because it's plain Markdown + JSON, it's also a genuinely useful file to
keep around as context for Claude Code or Claude in any other tool — it's
already a structured map of what your project contains and how the
pieces connect.
