# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- xNode asset migration: `Assets → SaintsGraph → Migrate xNode Graph` (and a Tools command
  for all graphs at once) converts an xNode graph into a SaintsGraph JSON sidecar — nodes,
  positions, field values, asset references, dynamic ports and connections — which is then
  imported onto a SaintsGraph asset after re-basing the node classes. Lives in a satellite
  assembly that only compiles while xNode is installed
  (`SAINTSGRAPH_XNODE_DISABLE` opts out).
- `Math Graph` sample: graph type, value/add/multiply nodes, a dynamic port list, a result
  node and a runner component showing evaluation from gameplay code.
- Package validation CI: checks that `package.json` and every `.asmdef` parse, assembly
  names are unique, sample paths exist and no asset is missing its `.meta`.

- Dynamic port lists (`[Input(dynamicPortList: true)]` / `[Output(...)]` on array or
  `List<T>` fields), following xNode's `"{field} {index}"` convention: the node renders
  a list block with one port per element, add/move/remove buttons keep the backing list
  and ports in lockstep, connections follow elements on reorder and shift down on
  removal. Element-port metadata (type, direction, constraints) refreshes from the
  backing field's attribute on `UpdatePorts`, the backing field itself no longer gets a
  port, and port counts self-heal against external backing-list edits (e.g. JSON sidecar
  imports).

- Cycle detection in the editor: nodes participating in a cycle (including self-loops)
  get a red border and a warning tooltip, updated on every graph change. The runtime
  still allows cycles for xNode parity — this is a visual guard against the infinite
  recursion pull evaluation would hit.
- Lazy node bodies: a node that starts (or stays) collapsed never builds its body at
  all — with the SaintsField integration this means no member renderers and no polling
  for collapsed nodes. The body is built on first expand; connected port pills are shown
  in the compact title containers meanwhile.

- JSON sidecar (v1): `Assets → SaintsGraph → Export/Import Graph JSON` writes a
  human/LLM-friendly `<Graph>.graph.json` next to the asset (flat node list with field
  values in Unity JSON shape, `$ref:guid:localId` asset references, inline edge list) and
  applies edits back — field values, positions, renames via the `name` field, added and
  removed nodes and edges. `Tools → SaintsGraph → Auto Export Graph JSON` refreshes the
  sidecar on every asset save. Open graph windows reload after import.

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

- Editing a graph no longer rebuilds the whole view. Connecting, disconnecting, creating and
  deleting now reconcile node and edge views incrementally, and rows whose backing value
  becomes hidden toggle in place instead of being rebuilt. Full reloads are reserved for
  opening a graph, undo/redo and sidecar imports.
- Node bodies are built only for nodes near the viewport instead of all at once when the graph
  opens, and only once the view has settled — so panning never competes with body building.
  Batches are bounded by time rather than node count (body cost varies widely), nodes nearest
  the middle of the screen are built first, and connected port pills are drawn even before a
  body exists, so edges stay anchored while a node fills in.
- `PortTypeOverrideAttribute` and `NodeEnumAttribute` moved from the global namespace into
  `SaintsGraph`: declaring them globally, as xNode does, made the type ambiguous
  (`CS0433`) whenever both packages were installed — which is exactly the migration state.
  Code with `using SaintsGraph;` is unaffected; while xNode is installed alongside, qualify
  as `[SaintsGraph.PortTypeOverride(...)]`, and the migration assembly warns about fields
  that silently bound to xNode's attribute instead.

- Structural edits (create/connect/move/delete) no longer write assets to disk immediately —
  that caused a visible hitch on every node creation. Edits mark objects dirty; saves happen
  on the toolbar Save button, on window close, after undo/redo, and with the project save
  (Ctrl+S).

### Fixed

- Collapsing a node no longer leaves edges pointing into empty space: while collapsed,
  connected port pills move into the standard input/output containers next to the title
  (with port names) and return to their body rows on expand. Collapse state is preserved
  across view rebuilds within the session.
- Connecting an input port no longer moves its row to the end of the node body: hidden
  backing values (`ShowBackingValue.Never`/connected `Unconnected`) are now replaced with
  a label row **in place**, keeping the field's natural position. Output port rows also
  sit at their field position now instead of being appended at the end.

- Undo of node creation/deletion no longer leaves the graph asset with destroyed or
  orphaned node sub-assets (which made the Project browser throw
  `NullReferenceException` in `ObjectListArea`). A sanitizer repairs graph assets after
  undo/redo and on load.
