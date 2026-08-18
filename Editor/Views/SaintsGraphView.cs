using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace SaintsGraph.Editor
{
    /// <summary>
    /// GraphView backend. Internal on purpose: the public extension surface lives in
    /// <see cref="SaintsNodeEditor"/>/<see cref="SaintsGraphEditor"/> and must not leak
    /// GraphView types.
    /// </summary>
    internal class SaintsGraphView : GraphView
    {
        public readonly NodeGraph graph;
        public readonly SaintsGraphEditor graphEditor;

        private readonly EditorWindow _window;
        private readonly NodeSearchWindowProvider _searchProvider;
        private readonly Dictionary<Node, SaintsNodeView> _nodeViews = new Dictionary<Node, SaintsNodeView>();
        private bool _reloading;
        private bool _reloadScheduled;

        public SaintsGraphView(NodeGraph graph, EditorWindow window)
        {
            this.graph = graph;
            _window = window;
            graphEditor = EditorTypeCache.CreateGraphEditor(graph);
            graphEditor.target = graph;
            graphEditor.serializedObject = new SerializedObject(graph);
            graphEditor.OnOpen();

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            GridBackground grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            StyleSheet sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.observer189.saintsgraph/Editor/SaintsGraphView.uss");
            if (sheet != null)
            {
                styleSheets.Add(sheet);
            }

            graphViewChanged = OnGraphViewChanged;
            _searchProvider = ScriptableObject.CreateInstance<NodeSearchWindowProvider>();
            _searchProvider.hideFlags = HideFlags.HideAndDontSave;
            _searchProvider.Initialize(this);
            nodeCreationRequest = context =>
                SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), _searchProvider);

            Reload();
        }

        /// <summary>Releases per-node body resources. Call before discarding this view.</summary>
        public void TeardownNodeViews()
        {
            foreach (SaintsNodeView view in _nodeViews.Values)
            {
                view.Teardown();
            }
        }

        /// <summary>Rebuilds all views from the model. The model is always the source of truth.</summary>
        public void Reload()
        {
            _reloading = true;
            TeardownNodeViews();
            DeleteElements(graphElements.ToList());
            _nodeViews.Clear();
            graph.PruneInvalidEdges();

            foreach (Node node in graph.nodes)
            {
                if (node == null)
                {
                    continue;
                }

                SaintsNodeView view = new SaintsNodeView(node, this, graphEditor);
                _nodeViews[node] = view;
                AddElement(view);
            }

            foreach (NodeEdge modelEdge in graph.Edges)
            {
                Port output = FindPortView(modelEdge.outputNode, modelEdge.outputField);
                Port input = FindPortView(modelEdge.inputNode, modelEdge.inputField);
                if (output == null || input == null)
                {
                    continue;
                }

                Edge edge = new Edge { output = output, input = input };
                output.Connect(edge);
                input.Connect(edge);
                AddElement(edge);
            }

            _reloading = false;
        }

        public void ScheduleReload()
        {
            if (_reloadScheduled)
            {
                return;
            }

            _reloadScheduled = true;
            schedule.Execute(() =>
            {
                _reloadScheduled = false;
                Reload();
            });
        }

        private Port FindPortView(Node node, string fieldName)
        {
            if (node == null || !_nodeViews.TryGetValue(node, out SaintsNodeView view))
            {
                return null;
            }

            return view.portViews.TryGetValue(fieldName, out Port port) ? port : null;
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_reloading)
            {
                return change;
            }

            bool structuralChange = false;

            if (change.movedElements != null)
            {
                foreach (GraphElement element in change.movedElements)
                {
                    if (element is SaintsNodeView view)
                    {
                        Undo.RecordObject(view.target, "Move Node");
                        view.target.position = view.GetPosition().position;
                        EditorUtility.SetDirty(view.target);
                    }
                }
            }

            if (change.elementsToRemove != null)
            {
                foreach (GraphElement element in change.elementsToRemove)
                {
                    if (element is Edge edge
                        && edge.output?.userData is NodePort output
                        && edge.input?.userData is NodePort input)
                    {
                        output.Disconnect(input);
                        structuralChange = true;
                    }
                }

                List<GraphElement> vetoed = null;
                foreach (GraphElement element in change.elementsToRemove)
                {
                    if (element is SaintsNodeView view)
                    {
                        if (graphEditor.CanRemove(view.target))
                        {
                            graphEditor.RemoveNode(view.target);
                            structuralChange = true;
                        }
                        else
                        {
                            (vetoed = vetoed ?? new List<GraphElement>()).Add(element);
                        }
                    }
                }

                if (vetoed != null)
                {
                    foreach (GraphElement element in vetoed)
                    {
                        change.elementsToRemove.Remove(element);
                    }
                }
            }

            if (change.edgesToCreate != null)
            {
                foreach (Edge edge in change.edgesToCreate)
                {
                    if (edge.output?.userData is NodePort output
                        && edge.input?.userData is NodePort input
                        && !output.IsConnectedTo(input)
                        && graphEditor.CanConnect(output, input))
                    {
                        output.Connect(input);
                        structuralChange = true;
                    }
                }
            }

            if (structuralChange)
            {
                MarkDirty();
                // Re-sync views with the model: Override ports auto-clear old connections,
                // ShowBackingValue.Unconnected fields appear/disappear, vetoed removals restore.
                ScheduleReload();
            }

            return change;
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            List<Port> compatible = new List<Port>();
            if (!(startPort.userData is NodePort startModel))
            {
                return compatible;
            }

            ports.ForEach(port =>
            {
                if (port == startPort || port.direction == startPort.direction)
                {
                    return;
                }

                if (!(port.userData is NodePort model))
                {
                    return;
                }

                NodePort output = startModel.IsOutput ? startModel : model;
                NodePort input = startModel.IsOutput ? model : startModel;
                if (graphEditor.CanConnect(output, input))
                {
                    compatible.Add(port);
                }
            });
            return compatible;
        }

        public void CreateNode(Type type, Vector2 screenMousePosition)
        {
            VisualElement windowRoot = _window.rootVisualElement;
            Vector2 windowPosition = windowRoot.ChangeCoordinatesTo(windowRoot.parent,
                screenMousePosition - _window.position.position);
            Vector2 graphPosition = contentViewContainer.WorldToLocal(windowPosition);

            graphEditor.CreateNode(type, graphPosition);
            MarkDirty();
            ScheduleReload();
        }

        // Writing to disk on every structural change causes a visible hitch, so edits only
        // mark objects dirty. Actual saves happen on the toolbar button, window close and
        // after undo/redo (where the file must match memory for the Project browser).
        private void MarkDirty()
        {
            EditorUtility.SetDirty(graph);
            foreach (Node node in graph.nodes)
            {
                if (node != null)
                {
                    EditorUtility.SetDirty(node);
                }
            }
        }
    }
}
