# SaintsGraph

A node graph editor for Unity with an **xNode-compatible API**, a **UI Toolkit** editor and
first-class **[SaintsField](https://github.com/TylerTemp/SaintsField)** attribute support inside
node bodies.

> ⚠️ **Early development.** Runtime and editor both work — create, connect, copy/paste, undo,
> dynamic port lists, JSON sidecar, xNode migration. The API may still change until 1.0.

## Why

[xNode](https://github.com/Siccity/xNode) has a wonderfully small API, but its editor is IMGUI and
the project is dormant. SaintsGraph keeps the API you already know and rebuilds everything else:

- **xNode-shaped API** — `Node`, `NodeGraph`, `NodePort`, `[Input]`/`[Output]`,
  `[CreateNodeMenu]`, `[NodeTint]`, `[NodeWidth]`, `[DisallowMultipleNodes]`, `[RequireNode]`,
  `[PortTypeOverride]`, `[NodeEnum]`. Porting existing node code is a `using XNode;` →
  `using SaintsGraph;` swap for most projects.
- **UI Toolkit editor** (GraphView-based, behind an isolation layer) — pan/zoom canvas, searchable
  create menu, drag-to-create, copy/paste, graph search, cycle highlighting, and node bodies that
  build lazily so large graphs stay responsive.
- **SaintsField attributes in nodes** — SaintsField property drawers work out of the box; with the
  optional integration assembly, node bodies are rendered through SaintsField's engine, enabling
  `[Button]`, `[ShowIf]`, layout groups and friends inside nodes. SaintsField remains an optional
  dependency: without it everything still works with plain property fields.
- **LLM/human-friendly graphs** — a clean serialized model (single edge list per graph instead of
  xNode's mirrored per-port connections) plus a JSON side file you can export, edit and import
  back. It doubles as the clipboard format, so graphs can be reviewed in PRs, pasted as text and
  authored by tools or LLMs.

## What is intentionally different from xNode

The public API matches; the internals do not:

- Connections are stored **once, at graph level** (`NodeGraph.Edges`), not mirrored on both ports.
  The whole class of "connection lists out of sync" bugs is gone.
- Ports are rebuilt **lazily per type** — no assembly scanning, and no silent loss of ports for
  assemblies whose name starts with `Unity`, as in xNode.
- Hiding `OnEnable` in a node subclass no longer breaks ports (only your `Init()` would be skipped —
  still don't do it).
- `OnCreateConnection(from, to)` receives normalized arguments on **both** nodes: `from` is always
  the output port, `to` always the input port.
- Auto-named dynamic ports are direction-aware (`dynamicOutput_0`, not xNode's `dynamicInput_0`
  for outputs).
- `.asset` files are **not** binary-compatible with xNode. Code migrates by switching the
  namespace, assets through the bundled migration tool (see below).
- `PortTypeOverride` and `NodeEnum` live in the `SaintsGraph` namespace instead of xNode's
  global namespace, so both packages can be installed side by side during migration. Code
  with `using SaintsGraph;` compiles unchanged — but while xNode is *also* installed, write
  `[SaintsGraph.PortTypeOverride(...)]`, because C# resolves the global-namespace attribute
  first. The migration assembly warns when it finds fields hitting this.
- Obsolete xNode aliases (`InstancePorts`, `AddInstanceInput`, ...) are not carried over.

## Install

Unity **6000.0+**. In Package Manager: *Add package from git URL*:

```
https://github.com/Observer189/SaintsGraph.git
```

## Quick example

```csharp
using SaintsGraph;
using UnityEngine;

[CreateAssetMenu(menuName = "Graphs/Math Graph")]
public class MathGraph : NodeGraph { }

public class AddNode : Node
{
    [Input] public float a;
    [Input] public float b;
    [Output] public float result;

    public override object GetValue(NodePort port)
    {
        return GetInputValue("a", a) + GetInputValue("b", b);
    }
}
```

## Migrating from xNode

Code migration is a `using` swap: change `using XNode;` to `using SaintsGraph;` in your node
and graph classes. Attribute and member names match, so the rest of the class stays as it is.

Assets migrate through the JSON sidecar, which is written while xNode is still installed:

1. **Before changing any code**, right-click an xNode graph asset →
   **SaintsGraph → Migrate xNode Graph** (or **Tools → SaintsGraph → Migrate All xNode Graphs**).
   This writes `<Graph>.graph.json` next to each asset: nodes, positions, field values,
   dynamic ports and connections.
2. Switch your classes to `using SaintsGraph;`.
3. Create a new graph asset with the same name in the same folder as the JSON (the old xNode
   asset can then be deleted), right-click it → **SaintsGraph → Import Graph JSON**.

The migration assembly only compiles while xNode is installed, and disappears with it.

## JSON sidecar

`Assets → SaintsGraph → Export Graph JSON` writes a readable `MyGraph.graph.json` next to the
asset — a flat node list with field values plus an edge list:

```json
{
  "format": "saintsgraph/1",
  "nodes": [
    { "id": "Float", "name": "Float", "type": "FloatNode, Assembly-CSharp",
      "position": [-180, 40], "fields": { "value": 2 } },
    { "id": "Add", "name": "Add", "type": "AddNode, Assembly-CSharp",
      "position": [80, 40], "fields": { "a": 0, "b": 0 } }
  ],
  "edges": [ ["Float", "value", "Add", "a"] ]
}
```

Edit it by hand, in a PR review, or with an LLM, then `Import Graph JSON` applies the changes
back: field values, positions, renames (the `name` field), added/removed nodes and edges.
`Tools → SaintsGraph → Auto Export Graph JSON` keeps the sidecar fresh on every save, so graph
changes show up as readable diffs in version control.

## Authoring graphs with tools or an LLM

The sidecar format is also the clipboard format, which makes a round trip possible without any
integration:

The schema and a graph are two different documents: the schema describes which node types
*exist*, a graph document describes an actual graph. Only the latter can be pasted into a window.

1. **Tools → SaintsGraph → Copy Node Schema for Tools or LLM** — copies a machine-readable
   description of every node type in the project: assembly-qualified type names, ports (direction,
   value type, connection rules), default field values, and short instructions on the document
   format itself.
2. Paste that into your tool or model and ask for a graph.
3. Paste the JSON it returns straight into an open graph window with **Ctrl+V** — the nodes appear
   under the cursor, wired up.

For file-based workflows, write the JSON to `<Graph>.graph.json` next to the asset and use
**Import Graph JSON**, pick any file with **Import Graph JSON from File...**, or switch on
**Tools → SaintsGraph → Auto Import Graph JSON** to have Unity apply external edits as soon as it
refreshes. **Copy Graph JSON to Clipboard** goes the other way, handing you an existing graph as
text.

Nodes carry a stable `uid` alongside the readable `id`, so renaming a node — in the editor or in
the file — updates it rather than replacing it, and its connections survive.

## Roadmap

- [x] Runtime core (xNode-shaped API, graph-level edges, tests)
- [x] Graph editor window MVP (GraphView backend, UI Toolkit node bodies, create/connect/undo)
- [x] SaintsField integration assembly (`[Button]`, `[ShowIf]`, layouts in nodes)
- [x] JSON sidecar v1 (export/import commands, auto-export on save)
- [x] Dynamic port lists, lazy node bodies, cycle highlighting
- [x] xNode asset migration tool, sample, package validation CI
- [x] Copy/paste/duplicate (clipboard is the JSON format), drag-to-create, graph search
- [x] Node schema export, stable node identity, auto-import of external edits
- [x] Groups and sticky notes, persisted collapse state and camera
- [x] Connection styles, preferences window, grid snap
- [ ] Reroute points, drag-reorder for port lists
- [ ] OpenUPM release

See [Docs~/DESIGN.md](Docs~/DESIGN.md) for the architecture.

## Acknowledgements

- [xNode](https://github.com/Siccity/xNode) by Thor Brigsted — the API this project deliberately
  mirrors (MIT, see THIRD-PARTY-NOTICES.md).
- [SaintsField](https://github.com/TylerTemp/SaintsField) by TylerTemp — the inspector toolkit this
  project integrates with.

## License

[MIT](LICENSE)
