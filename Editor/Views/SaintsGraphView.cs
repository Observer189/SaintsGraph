using System;
using System.Collections.Generic;
using System.Linq;
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
    ///
    /// The model is the source of truth, but it is synced incrementally — a full
    /// <see cref="Reload"/> only happens on open, undo and sidecar import, so editing a
    /// large graph does not rebuild every node view on every connection.
    /// </summary>
    internal class SaintsGraphView : GraphView
    {
        /// <summary>How far outside the viewport node bodies are built ahead of time, in graph units.</summary>
        private const float BodyBuildMargin = 600f;

        /// <summary>Bodies built per scheduled batch, so building never blocks a frame for long.</summary>
        private const int BodyBuildBudget = 6;

        public readonly NodeGraph graph;
        public readonly SaintsGraphEditor graphEditor;

        private readonly EditorWindow _window;
        private readonly NodeSearchWindowProvider _searchProvider;
        private readonly Dictionary<Node, SaintsNodeView> _nodeViews = new Dictionary<Node, SaintsNodeView>();
        private readonly Dictionary<NodeEdge, Edge> _edgeViews = new Dictionary<NodeEdge, Edge>();
        private readonly Dictionary<Node, bool> _expandedStates = new Dictionary<Node, bool>();
        private bool _suspendChangeHandling;
        private bool _reloadScheduled;
        private bool _bodyBuildScheduled;

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

            viewTransformChanged = _ => ScheduleBodyBuild();
            RegisterCallback<GeometryChangedEvent>(_ => ScheduleBodyBuild());

            Reload();
        }

        /// <summary>Collapse state per node, preserved across view reloads (session only).</summary>
        internal bool GetExpandedState(Node node)
        {
            return !_expandedStates.TryGetValue(node, out bool expanded) || expanded;
        }

        internal void SetExpandedState(Node node, bool expanded)
        {
            _expandedStates[node] = expanded;
        }

        /// <summary>Releases per-node body resources. Call before discarding this view.</summary>
        public void TeardownNodeViews()
        {
            foreach (SaintsNodeView view in _nodeViews.Values)
            {
                view.Teardown();
            }
        }

        /// <summary>Rebuilds every view from the model. Used on open, undo/redo and JSON import.</summary>
        public void Reload()
        {
            _suspendChangeHandling = true;
            TeardownNodeViews();
            DeleteElements(graphElements.ToList());
            _nodeViews.Clear();
            _edgeViews.Clear();
            graph.PruneInvalidEdges();

            foreach (Node node in graph.nodes)
            {
                if (node != null)
                {
                    CreateNodeView(node);
                }
            }

            SyncEdgeViews();
            RefreshCycleWarnings();
            _suspendChangeHandling = false;
            ScheduleBodyBuild();
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

        /// <summary>Rebuilds a single node's view, leaving the rest of the graph untouched.</summary>
        public void RebuildNodeView(Node node)
        {
            if (node == null || !_nodeViews.TryGetValue(node, out SaintsNodeView view))
            {
                return;
            }

            _suspendChangeHandling = true;
            foreach (KeyValuePair<NodeEdge, Edge> entry in _edgeViews.ToList())
            {
                if (ReferenceEquals(entry.Key.outputNode, node) || ReferenceEquals(entry.Key.inputNode, node))
                {
                    RemoveEdgeView(entry.Key, entry.Value);
                }
            }

            view.Teardown();
            RemoveElement(view);
            _nodeViews.Remove(node);
            CreateNodeView(node);
            SyncEdgeViews();
            RefreshCycleWarnings();
            _suspendChangeHandling = false;
            ScheduleBodyBuild();
        }

        /// <summary>Reconciles views with the model without rebuilding anything that already matches.</summary>
        private void SyncFromModel()
        {
            _suspendChangeHandling = true;

            foreach (Node node in graph.nodes)
            {
                if (node != null && !_nodeViews.ContainsKey(node))
                {
                    CreateNodeView(node);
                }
            }

            foreach (KeyValuePair<Node, SaintsNodeView> entry in _nodeViews.ToList())
            {
                if (entry.Key == null || !graph.nodes.Contains(entry.Key))
                {
                    entry.Value.Teardown();
                    RemoveElement(entry.Value);
                    _nodeViews.Remove(entry.Key);
                }
            }

            SyncEdgeViews();

            foreach (SaintsNodeView view in _nodeViews.Values)
            {
                view.RefreshConnectedState();
            }

            RefreshCycleWarnings();
            _suspendChangeHandling = false;
            ScheduleBodyBuild();
        }

        private SaintsNodeView CreateNodeView(Node node)
        {
            SaintsNodeView view = new SaintsNodeView(node, this, graphEditor);
            _nodeViews[node] = view;
            AddElement(view);
            return view;
        }

        private void SyncEdgeViews()
        {
            HashSet<NodeEdge> modelEdges = new HashSet<NodeEdge>(graph.Edges);

            foreach (KeyValuePair<NodeEdge, Edge> entry in _edgeViews.ToList())
            {
                if (!modelEdges.Contains(entry.Key))
                {
                    RemoveEdgeView(entry.Key, entry.Value);
                }
            }

            foreach (NodeEdge modelEdge in graph.Edges)
            {
                if (_edgeViews.ContainsKey(modelEdge))
                {
                    continue;
                }

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
                _edgeViews[modelEdge] = edge;
            }
        }

        private void RemoveEdgeView(NodeEdge modelEdge, Edge edge)
        {
            edge.output?.Disconnect(edge);
            edge.input?.Disconnect(edge);
            RemoveElement(edge);
            _edgeViews.Remove(modelEdge);
        }

        private void RefreshCycleWarnings()
        {
            HashSet<Node> cycleNodes = GraphCycleDetector.FindCycleNodes(graph);
            foreach (KeyValuePair<Node, SaintsNodeView> pair in _nodeViews)
            {
                pair.Value.SetCycleWarning(cycleNodes.Contains(pair.Key));
            }
        }

        private Port FindPortView(Node node, string fieldName)
        {
            if (node == null || !_nodeViews.TryGetValue(node, out SaintsNodeView view))
            {
                return null;
            }

            return view.portViews.TryGetValue(fieldName, out Port port) ? port : null;
        }

        /// <summary>Builds bodies for nodes near the viewport, a few per batch, so panning stays smooth.</summary>
        private void ScheduleBodyBuild()
        {
            if (_bodyBuildScheduled || _nodeViews.Count == 0)
            {
                return;
            }

            _bodyBuildScheduled = true;
            schedule.Execute(BuildVisibleBodies).ExecuteLater(16);
        }

        private void BuildVisibleBodies()
        {
            _bodyBuildScheduled = false;
            Rect visible = contentViewContainer.WorldToLocal(worldBound);
            visible = new Rect(visible.x - BodyBuildMargin, visible.y - BodyBuildMargin,
                visible.width + 2f * BodyBuildMargin, visible.height + 2f * BodyBuildMargin);

            int budget = BodyBuildBudget;
            bool moreToDo = false;
            foreach (SaintsNodeView view in _nodeViews.Values)
            {
                if (view.BodyBuilt || !view.expanded)
                {
                    continue;
                }

                Rect placed = view.GetPosition();
                // A body-less node measures small; probe with a typical node size instead.
                Rect probe = new Rect(placed.position,
                    new Vector2(Mathf.Max(placed.width, 260f), Mathf.Max(placed.height, 220f)));
                if (!visible.Overlaps(probe))
                {
                    continue;
                }

                if (budget <= 0)
                {
                    moreToDo = true;
                    break;
                }

                budget--;
                view.EnsureBodyBuilt();
            }

            if (moreToDo)
            {
                ScheduleBodyBuild();
            }
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_suspendChangeHandling)
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
                        NodeEdge modelEdge = graph.FindEdge(output, input);
                        output.Disconnect(input);
                        if (modelEdge != null)
                        {
                            _edgeViews.Remove(modelEdge);
                        }

                        EditorUtility.SetDirty(output.node);
                        EditorUtility.SetDirty(input.node);
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
                            view.Teardown();
                            _nodeViews.Remove(view.target);
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
                        NodeEdge modelEdge = graph.FindEdge(output, input);
                        if (modelEdge != null)
                        {
                            // Adopt the edge GraphView just created instead of making a duplicate.
                            _edgeViews[modelEdge] = edge;
                        }

                        EditorUtility.SetDirty(output.node);
                        EditorUtility.SetDirty(input.node);
                        structuralChange = true;
                    }
                }
            }

            if (structuralChange)
            {
                EditorUtility.SetDirty(graph);
                // Reconcile only what actually differs: Override ports drop old connections,
                // ShowBackingValue.Unconnected rows flip, vetoed removals come back.
                schedule.Execute(SyncFromModel).ExecuteLater(0);
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

            Node node = graphEditor.CreateNode(type, graphPosition);
            EditorUtility.SetDirty(graph);
            if (node != null)
            {
                EditorUtility.SetDirty(node);
            }

            SyncFromModel();
        }
    }
}
