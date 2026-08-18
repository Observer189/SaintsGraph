# SaintsGraph architecture

Decisions made 2026-08-18. This document describes the intended end state; see the README roadmap
for what exists today.

## Goals

1. xNode-compatible **API** (not asset format): porting node code is a `using` swap.
2. UI Toolkit editor with SaintsField attribute support inside node bodies.
3. LLM- and diff-friendly graph storage.
4. Standalone package: no hard dependency on xNode or SaintsField.

## Runtime

- `Node`, `NodeGraph` are `ScriptableObject`s; nodes are stored as sub-assets of the graph asset
  (created by editor code, as in xNode).
- **Edges live in the graph** (`NodeGraph.Edges`, `NodeEdge { outputNode, outputField, inputNode,
  inputField, reroutePoints }`) — single source of truth. `NodePort` keeps the full xNode surface
  (`Connect`, `Disconnect`, `GetConnections`, `GetInputValue<T>`, ...) but resolves through the
  graph.
- Static ports are reflected **lazily per node type** (`PortCache`); dynamic ports are serialized
  per node as plain data. Port objects themselves are never serialized.
- Evaluation model is unchanged from xNode: pull-based, uncached, no type conversion
  (`GetInputValue<T>` walks upstream and calls `GetValue(port)` on the producing node).
  Cycle detection is an editor feature, not a runtime one.

### Known xNode traps, designed away

| xNode | SaintsGraph |
|---|---|
| Mirrored connection lists drift out of sync | single graph-level edge list |
| Assembly-name filter silently skips ports | lazy per-type reflection, no scanning |
| Hidden `OnEnable` ⇒ node without ports | ports rebuilt lazily on access |
| `OnCreateConnection` gets un-normalized args | always `(output, input)` on both nodes |
| `MoveConnections` indexing bug | reimplemented correctly |
| Auto-named dynamic outputs called `dynamicInput_N` | direction-aware names |

## Serialization & LLM-friendliness

Native storage stays Unity YAML (undo, object references, familiar workflows). On top of it:
a per-graph/per-project setting to export a `<Graph>.graph.json` side file on save — flat node
list plus `"nodeId.port" -> "nodeId.port"` edges — and an import command that applies JSON edits
back to the asset. xNode assets are migrated by a dedicated converter tool (planned), not by
format compatibility.

## Editor

- `SaintsGraphWindow` (UI Toolkit `EditorWindow`), opened via `OnOpenAsset`.
- **GraphView is an implementation detail** behind an isolation layer: the public extension
  surface (analogues of xNode's `NodeEditor` / `NodeGraphEditor`: header/body `VisualElement`
  factories, tint, width, context menus, `CanConnect`, port/type colors) exposes no GraphView
  types, so the backend can be replaced (Unity Graph Toolkit once it stabilizes, or a custom
  canvas) without breaking user code.
- Node bodies:
  - Core path: `PropertyField` loop. SaintsField property drawers (registered via standard
    `[CustomPropertyDrawer]`) work automatically when SaintsField is installed.
  - Deep integration (satellite assembly `SaintsGraph.Editor.SaintsFieldSupport`, enabled via
    `versionDefines` on `today.comes.saintsfield` + `defineConstraints`
    `SAINTSGRAPH_SAINTSFIELD` / `!SAINTSGRAPH_SAINTSFIELD_DISABLE`): bodies built through
    `SaintsField.Editor.SaintsEditor.Setup(...)`, unlocking `[Button]`, `[ShowIf]`, layout groups
    and non-serialized member rendering inside nodes.
  - Bodies are built **lazily** and torn down on collapse: SaintsField renderers schedule
    polling updates (`.Every(100)` per renderer), which must not run for every node of a large
    graph at once.
- Known constraint: `IMGUIContainer` misbehaves under GraphView zoom — fields whose types have
  only IMGUI drawers will degrade; this is a Unity limitation, not specific to the chosen canvas.

## Package layout

Repository root = package root (installable by git URL). Optional integrations follow the
SaintsField satellite-asmdef pattern. During development the package is embedded in a Unity
project's `Packages/` folder.
