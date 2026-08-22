# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Preferences (**Preferences → SaintsGraph**): connection style, grid snapping, and the JSON
  sidecar auto-export/auto-import toggles in one place. Open graph windows follow changes
  immediately.
- Connection styles: **Rounded** (GraphView's own shape, still the default), **Curvy** (bezier),
  **Angled** (right-angle routing) and **Straight**. SaintsGraph draws connections itself, since
  Unity keeps its render points private, and hit-testing follows the drawn shape.
- Grid snapping, measured in cells of the grid actually drawn on the canvas: nodes snap while
  being dragged, not on drop, so they follow the lines you can see.
- Nodes can be renamed by double-clicking their title, and the title tooltip names the node's
  type — the title itself is the node's own name, which need not match its class.

- Groups and sticky notes: right-click the canvas to create either. A group made with nodes
  selected adopts them, dragging nodes in and out updates membership, and both are stored in the
  graph asset and in the JSON sidecar (groups by node id, notes with text, size and theme), so
  documentation travels with the graph.
- Node collapse state is stored on the node, so folded nodes stay folded after reopening the
  graph, and pan/zoom is restored per graph asset (kept in EditorPrefs, since it is a per-user
  view preference rather than graph content).

- Node schema export (`Tools → SaintsGraph → Copy Node Schema to Clipboard` /
  `Export Node Schema...`): a machine-readable description of every node type — ports with
  direction, element type and connection rules, default field values, menu path, instance
  limits — plus instructions describing the graph document itself. Together with paste, a tool
  or model can author a graph and it can be dropped straight into a window.
- Stable node identity: nodes carry a `uid` that the sidecar records alongside the readable
  `id`. Imports match on it first, so renaming a node in the editor or in the file updates the
  node instead of replacing it, and its connections survive. Copies always get a fresh identity.
- `Tools → SaintsGraph → Auto Import Graph JSON`: sidecars edited outside Unity are applied to
  their graph on the next refresh. Files already matching the graph are skipped, so exports
  never bounce back as imports.

- Copy, cut, paste and duplicate (Ctrl+C/X/V/D) for node selections. Clipboard data *is* the
  JSON sidecar format, so a selection can be pasted as text into a file or chat, and any valid
  graph JSON — hand-written or generated — can be pasted straight into a graph. Pasted content
  lands under the mouse cursor (duplicate stays beside the original), gets unique names, keeps
  its internal connections and ends up selected.
- Dropping a dragged connection on empty canvas opens the create menu filtered to node types
  that can accept it, then wires the new node up automatically.
- Graph search in the toolbar (Ctrl+F): matching nodes are highlighted and the rest dimmed,
  Enter cycles through matches and frames them.

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

- Pasting something that is not a graph now says so. The node schema in particular is easy to
  confuse with a graph document, so pasting it explains the difference instead of the Paste
  command being silently unavailable. Schema menu items are named "for Tools or LLM" to make
  the distinction obvious.
- New `Assets → SaintsGraph → Copy Graph JSON to Clipboard` (a graph as pasteable text) and
  `Import Graph JSON from File...` (import a document that is not named after the asset).

- Node schema no longer lists types from test assemblies (anything referencing NUnit): fixtures
  are not content a generator should be offered.
- Schema defaults no longer carry `[SerializeReference]` bookkeeping. Managed reference ids are
  only meaningful inside the document that defines them, so such fields now show as `null` and a
  new `managedReferences` block names each polymorphic field with the concrete types that may be
  assigned to it — which is what a generator actually needs.

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

- Node bodies showed `uid` as an editable text field and `collapsed` as a checkbox. Both are
  bookkeeping rather than content: they are now hidden from inspectors and skipped by the body
  builders. Editing a uid by hand could have broken the identity the sidecar matches on.

- Sticky note text could not be edited. GraphView starts note editing from a double-click on the
  title or contents *label*, and a label with no text has no size to be clicked — so an empty note
  is unreachable, and clearing a title makes even the title unreachable. SaintsGraph now handles
  the double-click on the note itself and decides by region: the top band edits the title, the
  rest edits the contents, focus is claimed the way the built-in handler does it (the focus
  controller must be told to ignore the click, or it hands focus straight back), and Escape
  or clicking away commits. The title editor is pinned over its own row rather than replacing
  it, so the note's text no longer rises into the title while it is being renamed.
- Node bodies showed `uid` as an editable text field and `collapsed` as a checkbox. Both are
  bookkeeping rather than content: they are now hidden from inspectors and skipped by the body
  builders. Editing a uid by hand could have broken the identity the sidecar matches on.

- Sticky note text could not be edited. GraphView's own StickyNote starts contents editing by
  showing its text field and hiding the contents label — but in this Unity version that field is
  a child of the label, so hiding the label hides the editor along with it (the title works only
  because its field is a sibling). SaintsGraph now drives contents editing itself: the label stays
  visible with its text blanked while the field inside it is focused, Escape or clicking away
  commits. The contents area also fills the note body, since an empty label has no height to
  double-click.

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
