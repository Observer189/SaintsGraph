# Math Graph sample

A minimal, self-contained SaintsGraph example — no dependencies beyond the package itself.

1. Import this sample (Package Manager → SaintsGraph → Samples → Import).
2. Create a graph asset: **Assets → Create → SaintsGraph Samples → Math Graph**.
3. Double-click it to open the editor.
4. Right-click the canvas → **Create Node** and build something like
   `Value → Add ← Value`, ending in a **Result** node.
5. Put `MathGraphRunner` on a GameObject, assign the graph, and press Play (or use its
   *Evaluate* context menu) to see the computed value in the console.

What each script shows:

| Script | Demonstrates |
|---|---|
| `MathGraph.cs` | A graph asset type — all it needs is `: NodeGraph` |
| `ValueNode.cs` | An output port with an editable backing value |
| `AddNode.cs` | Inputs with fallbacks, pull evaluation via `GetInputValue` |
| `MultiplyNode.cs` | `[NodeTint]`, `[NodeWidth]`, strict type constraints |
| `SumListNode.cs` | A dynamic port list (`dynamicPortList: true`) |
| `ResultNode.cs` | `[DisallowMultipleNodes]`, evaluating the graph on demand |
| `MathGraphRunner.cs` | Reading a graph's result from gameplay code |

Two things worth trying afterwards:

- **JSON sidecar** — right-click the graph asset → *SaintsGraph → Export Graph JSON*, edit the
  file, then *Import Graph JSON*.
- **SaintsField attributes** — if SaintsField is installed, add `[Button]`, `[ShowIf]` or a
  layout group to a node and it renders right inside the node body.
