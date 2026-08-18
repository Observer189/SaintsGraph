# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- SaintsField integration assembly (`SaintsGraph.Editor.SaintsFieldSupport`): when the
  SaintsField package (≥ 5.25.0) is installed, node bodies are rendered through
  SaintsField's member-renderer engine — `[Button]`, `[ShowIf]`, layout groups,
  `[ShowInInspector]` and the rest of the SaintsEditor feature set work inside nodes.
  Auto-enabled via version defines; opt out with `SAINTSGRAPH_SAINTSFIELD_DISABLE`.
  Core gains a pluggable `INodeBodyBuilder` hook and unified port-pill attachment that
  works with any body (custom, SaintsField, built-in).

- Graph editor window (MVP): opens on double-clicking a `NodeGraph` asset. GraphView-backed
  canvas with pan/zoom/selection, searchable create-node menu (`[CreateNodeMenu]`,
  `[DisallowMultipleNodes]`), node bodies with serialized fields and inline ports
  (`ShowBackingValue` honored), connect/disconnect with type constraints, move, delete
  (`[RequireNode]` guard), undo/redo, sub-asset persistence and autosave.
- Public editor extension API without GraphView types: `SaintsNodeEditor`
  (`[CustomNodeEditor]`: header/body `VisualElement` factories, tint, width, tooltip) and
  `SaintsGraphEditor` (`[CustomNodeGraphEditor]`: menu names, `CanConnect`, `CanRemove`,
  node lifecycle, port/type colors).

- Runtime core with an xNode-shaped API: `Node`, `NodeGraph`, `NodePort`,
  `[Input]`/`[Output]`, `[CreateNodeMenu]`, `[NodeTint]`, `[NodeWidth]`,
  `[DisallowMultipleNodes]`, `[RequireNode]`, `[PortTypeOverride]`, `[NodeEnum]`.
- Graph-level single edge storage (`NodeEdge`) instead of xNode's mirrored
  per-port connection lists.
- Lazy, per-type port reflection — no assembly scanning, no assembly-name
  filtering pitfalls.
- Edit-mode test suite for the runtime core.

### Changed

- Structural edits (create/connect/move/delete) no longer write assets to disk immediately —
  that caused a visible hitch on every node creation. Edits mark objects dirty; saves happen
  on the toolbar Save button, on window close, after undo/redo, and with the project save
  (Ctrl+S).

### Fixed

- Undo of node creation/deletion no longer leaves the graph asset with destroyed or
  orphaned node sub-assets (which made the Project browser throw
  `NullReferenceException` in `ObjectListArea`). A sanitizer repairs graph assets after
  undo/redo and on load.
