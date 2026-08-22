using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>Time a single build batch may take. Body cost varies too much to count nodes instead.</summary>
        private const double BodyBuildBudgetMs = 4d;

        /// <summary>How long the view must be still before bodies are built, so panning never competes with them.</summary>
        private const double ViewSettleSeconds = 0.1d;

        public readonly NodeGraph graph;
        public readonly SaintsGraphEditor graphEditor;

        private readonly EditorWindow _window;
        private readonly NodeSearchWindowProvider _searchProvider;

        /// <summary>Shared by every port pill, so dropping a connection on empty canvas offers a node to create.</summary>
        internal readonly PortDropListener edgeConnectorListener;
        private readonly Dictionary<Node, SaintsNodeView> _nodeViews = new Dictionary<Node, SaintsNodeView>();
        private readonly Dictionary<NodeEdge, Edge> _edgeViews = new Dictionary<NodeEdge, Edge>();
        private readonly Dictionary<NodeGroup, Group> _groupViews = new Dictionary<NodeGroup, Group>();
        private readonly Dictionary<NodeNote, StickyNote> _noteViews = new Dictionary<NodeNote, StickyNote>();
        private bool _suspendChangeHandling;
        private bool _reloadScheduled;
        private bool _bodyBuildScheduled;
        private double _lastViewChange;
        private Vector2 _lastMouseWorld;
        private bool _hasMousePosition;

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
            serializeGraphElements = OnSerializeElements;
            canPasteSerializedData = OnCanPaste;
            unserializeAndPaste = OnPaste;
            edgeConnectorListener = new PortDropListener(this);
            _searchProvider = ScriptableObject.CreateInstance<NodeSearchWindowProvider>();
            _searchProvider.hideFlags = HideFlags.HideAndDontSave;
            _searchProvider.Initialize(this);
            nodeCreationRequest = context =>
                SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), _searchProvider);

            elementsAddedToGroup = OnElementsAddedToGroup;
            elementsRemovedFromGroup = OnElementsRemovedFromGroup;
            groupTitleChanged = OnGroupTitleChanged;
            elementResized = element =>
            {
                if (element is StickyNote resizedNote && resizedNote.userData is NodeNote noteModel)
                {
                    SaveNote(resizedNote, noteModel);
                }
            };

            viewTransformChanged = _ =>
            {
                _lastViewChange = EditorApplication.timeSinceStartup;
                SaveViewTransform();
                ScheduleBodyBuild();
            };
            RegisterCallback<GeometryChangedEvent>(_ => ScheduleBodyBuild());
            RegisterCallback<MouseMoveEvent>(evt =>
            {
                _lastMouseWorld = evt.mousePosition;
                _hasMousePosition = true;
            });

            SaintsGraphPreferences.Changed += OnPreferencesChanged;
            RegisterCallback<DetachFromPanelEvent>(_ => SaintsGraphPreferences.Changed -= OnPreferencesChanged);

            Reload();
            // After layout, so the restored transform is not overwritten by the initial framing.
            schedule.Execute(RestoreViewTransform).ExecuteLater(1);
        }

        /// <summary>
        /// Pan and zoom are per-user preference rather than graph content, so they live in
        /// EditorPrefs keyed by the asset's GUID instead of in the asset itself.
        /// </summary>
        private string ViewTransformKey
        {
            get
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(graph));
                return string.IsNullOrEmpty(guid) ? null : "SaintsGraph.ViewTransform." + guid;
            }
        }

        private void SaveViewTransform()
        {
            string key = ViewTransformKey;
            if (key != null)
            {
                Vector3 position = viewTransform.position;
                EditorPrefs.SetString(key, string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}",
                    position.x, position.y, viewTransform.scale.x));
            }
        }

        private void RestoreViewTransform()
        {
            string key = ViewTransformKey;
            string stored = key == null ? null : EditorPrefs.GetString(key, null);
            if (string.IsNullOrEmpty(stored))
            {
                return;
            }

            string[] parts = stored.Split(',');
            if (parts.Length == 3
                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float scale)
                && scale > 0f)
            {
                UpdateViewTransform(new Vector3(x, y, 0f), new Vector3(scale, scale, 1f));
            }
        }

        private void OnPreferencesChanged()
        {
            foreach (Edge edge in _edgeViews.Values)
            {
                (edge as SaintsEdge)?.RefreshStyle();
            }
        }

        /// <summary>Collapse state lives on the node, so a folded node stays folded across sessions.</summary>
        internal bool GetExpandedState(Node node)
        {
            return node == null || !node.collapsed;
        }

        internal void SetExpandedState(Node node, bool expanded)
        {
            if (node == null || node.collapsed != expanded)
            {
                return;
            }

            node.collapsed = !expanded;
            EditorUtility.SetDirty(node);
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
            _groupViews.Clear();
            _noteViews.Clear();
            graph.PruneInvalidEdges();

            foreach (Node node in graph.nodes)
            {
                if (node != null)
                {
                    CreateNodeView(node);
                }
            }

            SyncEdgeViews();
            SyncAnnotationViews();
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
            SyncAnnotationViews();

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

                SaintsEdge edge = new SaintsEdge { output = output, input = input };
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

        private void SyncAnnotationViews()
        {
            foreach (KeyValuePair<NodeGroup, Group> entry in _groupViews.ToList())
            {
                if (!graph.Groups.Contains(entry.Key))
                {
                    RemoveElement(entry.Value);
                    _groupViews.Remove(entry.Key);
                }
            }

            foreach (NodeGroup group in graph.Groups)
            {
                if (!_groupViews.ContainsKey(group))
                {
                    CreateGroupView(group);
                }
            }

            foreach (KeyValuePair<NodeNote, StickyNote> entry in _noteViews.ToList())
            {
                if (!graph.Notes.Contains(entry.Key))
                {
                    RemoveElement(entry.Value);
                    _noteViews.Remove(entry.Key);
                }
            }

            foreach (NodeNote note in graph.Notes)
            {
                if (!_noteViews.ContainsKey(note))
                {
                    CreateNoteView(note);
                }
            }
        }

        private void CreateGroupView(NodeGroup group)
        {
            Group view = new Group { title = group.title, userData = group };
            AddElement(view);
            _groupViews[group] = view;
            view.SetPosition(new Rect(group.position, Vector2.zero));

            foreach (Node node in group.nodes)
            {
                if (node != null && _nodeViews.TryGetValue(node, out SaintsNodeView nodeView))
                {
                    view.AddElement(nodeView);
                }
            }
        }

        private void CreateNoteView(NodeNote note)
        {
            StickyNote view = new StickyNote(note.area.position)
            {
                title = note.title,
                contents = note.text,
                theme = (StickyNoteTheme)note.theme,
                fontSize = (StickyNoteFontSize)note.fontSize,
                userData = note
            };
            view.SetPosition(note.area);
            SetUpNoteEditing(view, note);
            view.RegisterCallback<StickyNoteChangeEvent>(_ => SaveNote(view, note));
            AddElement(view);
            _noteViews[note] = view;
        }

        /// <summary>
        /// Sticky note text is edited through our own handler, because the built-in one cannot be
        /// reached: it starts editing from a double-click on the title or contents *label*, and a
        /// label with no text has no size to click. That is why a note is uneditable when empty —
        /// and why clearing a title makes even the title unreachable.
        ///
        /// This listens on the note itself, in the trickle-down phase, so the click is seen before
        /// any label hit-testing or drag manipulator: the top band edits the title, the rest edits
        /// the contents. The contents field is a child of the contents label in this Unity version,
        /// so that label must stay displayed while editing — its text is blanked instead.
        /// </summary>
        private void SetUpNoteEditing(StickyNote view, NodeNote note)
        {
            Label titleLabel = view.Q<Label>("title");
            TextField titleField = view.Q<TextField>("title-field");
            Label contentsLabel = view.Q<Label>("contents");
            TextField contentsField = view.Q<TextField>("contents-field");
            if (titleLabel == null || titleField == null || contentsLabel == null || contentsField == null)
            {
                return;
            }

            contentsLabel.style.flexGrow = 1;
            contentsLabel.style.minHeight = 32;
            contentsLabel.style.whiteSpace = WhiteSpace.Normal;
            contentsField.multiline = true;
            contentsField.style.flexGrow = 1;

            // Taking the title label out of the layout while editing let the contents rise into
            // its row and show through the editor. The title editor is pinned over that row
            // instead, and the label is only made invisible, so nothing moves.
            titleField.style.position = Position.Absolute;
            titleField.style.left = 0;
            titleField.style.right = 0;
            titleField.style.top = 0;

            void Commit()
            {
                if (titleField.style.display == DisplayStyle.Flex)
                {
                    titleField.style.display = DisplayStyle.None;
                    titleLabel.style.visibility = Visibility.Visible;
                    view.title = titleField.value;
                }

                if (contentsField.style.display == DisplayStyle.Flex)
                {
                    contentsField.style.display = DisplayStyle.None;
                    view.contents = contentsField.value;
                    contentsLabel.text = contentsField.value;
                }

                SaveNote(view, note);
            }

            void BeginEdit(TextField field, string value, VisualElement hide, bool blankInsteadOfHiding,
                MouseDownEvent evt)
            {
                Commit();
                field.SetValueWithoutNotify(value ?? "");
                field.style.display = DisplayStyle.Flex;
                if (blankInsteadOfHiding)
                {
                    // The field lives inside this label: hiding it would hide the editor too.
                    ((Label)hide).text = string.Empty;
                }
                else
                {
                    // Invisible, not removed: the row must keep its height while being edited.
                    hide.style.visibility = Visibility.Hidden;
                }

                VisualElement input = field.Q(TextField.textInputUssName);
                input?.Focus();
                field.SelectAll();

                evt.StopPropagation();

                // Without this the focus controller hands focus back to whatever the click landed
                // on, and the field shows up but never receives a keystroke. The built-in handler
                // guards against exactly this; missing it is what left the editor dead.
                view.focusController?.IgnoreEvent(evt);

                // And if focus still did not settle this frame, claim it on the next one.
                view.schedule.Execute(() =>
                {
                    if (field.style.display == DisplayStyle.Flex)
                    {
                        input?.Focus();
                    }
                }).ExecuteLater(0);
            }

            view.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount != 2)
                {
                    return;
                }

                Rect local = view.contentRect;
                Vector2 point = evt.localMousePosition;
                // Leave the resize border alone.
                if (point.x < 6f || point.y < 2f || point.x > local.width - 6f || point.y > local.height - 6f)
                {
                    return;
                }

                // An empty title has no height of its own, so the band is a fixed minimum.
                float titleBand = Mathf.Max(titleLabel.layout.height, 22f);
                if (point.y <= titleBand)
                {
                    BeginEdit(titleField, note.title, titleLabel, false, evt);
                }
                else
                {
                    BeginEdit(contentsField, note.text, contentsLabel, true, evt);
                }
            }, TrickleDown.TrickleDown);

            titleField.RegisterCallback<FocusOutEvent>(_ => Commit());
            contentsField.RegisterCallback<FocusOutEvent>(_ => Commit());

            void OnKey(KeyDownEvent evt)
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    Commit();
                    evt.StopPropagation();
                }
            }

            titleField.RegisterCallback<KeyDownEvent>(OnKey);
            contentsField.RegisterCallback<KeyDownEvent>(OnKey);
        }

        private void SaveNote(StickyNote view, NodeNote note)
        {
            Undo.RecordObject(graph, "Edit Note");
            note.title = view.title;
            note.text = view.contents;
            note.theme = (int)view.theme;
            note.fontSize = (int)view.fontSize;
            note.area = view.GetPosition();
            EditorUtility.SetDirty(graph);
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);

            if (evt.target != this && evt.target != contentViewContainer)
            {
                return;
            }

            Vector2 where = contentViewContainer.WorldToLocal(evt.mousePosition);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Create Group", _ => CreateGroup(where));
            evt.menu.AppendAction("Create Sticky Note", _ => CreateNote(where));
        }

        /// <summary>A group created with nodes selected adopts them, which is what one means by grouping.</summary>
        private void CreateGroup(Vector2 where)
        {
            Undo.RecordObject(graph, "Create Group");
            NodeGroup group = new NodeGroup { position = where };
            foreach (ISelectable selected in selection)
            {
                if (selected is SaintsNodeView nodeView)
                {
                    group.nodes.Add(nodeView.target);
                }
            }

            graph.Groups.Add(group);
            EditorUtility.SetDirty(graph);
            SyncAnnotationViews();
        }

        private void CreateNote(Vector2 where)
        {
            Undo.RecordObject(graph, "Create Sticky Note");
            NodeNote note = new NodeNote
            {
                title = "Note",
                area = new Rect(where, StickyNote.defaultSize)
            };
            graph.Notes.Add(note);
            EditorUtility.SetDirty(graph);
            SyncAnnotationViews();
        }

        private void OnElementsAddedToGroup(Group groupView, IEnumerable<GraphElement> elements)
        {
            if (_suspendChangeHandling || !(groupView.userData is NodeGroup group))
            {
                return;
            }

            Undo.RecordObject(graph, "Group Nodes");
            foreach (GraphElement element in elements)
            {
                if (element is SaintsNodeView nodeView && !group.nodes.Contains(nodeView.target))
                {
                    group.nodes.Add(nodeView.target);
                }
            }

            EditorUtility.SetDirty(graph);
        }

        private void OnElementsRemovedFromGroup(Group groupView, IEnumerable<GraphElement> elements)
        {
            if (_suspendChangeHandling || !(groupView.userData is NodeGroup group))
            {
                return;
            }

            Undo.RecordObject(graph, "Ungroup Nodes");
            foreach (GraphElement element in elements)
            {
                if (element is SaintsNodeView nodeView)
                {
                    group.nodes.Remove(nodeView.target);
                }
            }

            EditorUtility.SetDirty(graph);
        }

        private void OnGroupTitleChanged(Group groupView, string title)
        {
            if (groupView.userData is NodeGroup group)
            {
                Undo.RecordObject(graph, "Rename Group");
                group.title = title;
                EditorUtility.SetDirty(graph);
            }
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

        /// <summary>Builds bodies for nodes near the viewport, once the view stops moving.</summary>
        private void ScheduleBodyBuild(long delayMs = 16)
        {
            if (_bodyBuildScheduled || _nodeViews.Count == 0)
            {
                return;
            }

            _bodyBuildScheduled = true;
            schedule.Execute(BuildVisibleBodies).ExecuteLater(delayMs);
        }

        private void BuildVisibleBodies()
        {
            _bodyBuildScheduled = false;

            // Building while the user is still panning is what makes panning stutter: nodes
            // entering the viewport would each cost a body on the same frames that scroll it.
            // Wait for the view to settle; connected ports are already drawn meanwhile, so
            // edges stay anchored correctly while a body is still missing.
            if (EditorApplication.timeSinceStartup - _lastViewChange < ViewSettleSeconds)
            {
                ScheduleBodyBuild(50);
                return;
            }

            Rect visible = contentViewContainer.WorldToLocal(worldBound);
            visible = new Rect(visible.x - BodyBuildMargin, visible.y - BodyBuildMargin,
                visible.width + 2f * BodyBuildMargin, visible.height + 2f * BodyBuildMargin);
            Vector2 center = visible.center;

            List<SaintsNodeView> candidates = new List<SaintsNodeView>();
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
                if (visible.Overlaps(probe))
                {
                    candidates.Add(view);
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            // Nearest to the middle of the screen first — that is where the user is looking.
            candidates.Sort((a, b) =>
                Vector2.SqrMagnitude(a.GetPosition().center - center)
                    .CompareTo(Vector2.SqrMagnitude(b.GetPosition().center - center)));

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int built = 0;
            foreach (SaintsNodeView view in candidates)
            {
                view.EnsureBodyBuilt();
                built++;
                if (stopwatch.Elapsed.TotalMilliseconds >= BodyBuildBudgetMs)
                {
                    break;
                }
            }

            if (built < candidates.Count)
            {
                ScheduleBodyBuild();
            }
        }

        private string _searchQuery = string.Empty;
        private int _searchIndex;

        /// <summary>Highlights nodes matching the query (name or type) and dims the rest.</summary>
        internal void ApplySearch(string query)
        {
            _searchQuery = query ?? string.Empty;
            _searchIndex = 0;
            bool searching = !string.IsNullOrWhiteSpace(_searchQuery);
            foreach (KeyValuePair<Node, SaintsNodeView> entry in _nodeViews)
            {
                bool hit = searching && Matches(entry.Key, _searchQuery);
                entry.Value.EnableInClassList("saints-node-search-hit", hit);
                entry.Value.EnableInClassList("saints-node-search-miss", searching && !hit);
            }
        }

        /// <summary>Selects and frames the next node matching the current query.</summary>
        internal void FocusNextMatch()
        {
            if (string.IsNullOrWhiteSpace(_searchQuery))
            {
                return;
            }

            List<SaintsNodeView> matches = _nodeViews
                .Where(entry => Matches(entry.Key, _searchQuery))
                .Select(entry => entry.Value)
                .ToList();
            if (matches.Count == 0)
            {
                return;
            }

            _searchIndex %= matches.Count;
            SaintsNodeView match = matches[_searchIndex];
            _searchIndex++;

            ClearSelection();
            AddToSelection(match);
            FrameSelection();
        }

        private static bool Matches(Node node, string query)
        {
            if (node == null)
            {
                return false;
            }

            return node.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                   || node.GetType().Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
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
                    switch (element)
                    {
                        case SaintsNodeView view:
                            Undo.RecordObject(view.target, "Move Node");
                            Vector2 dropped = SaintsGraphPreferences.Snap(view.GetPosition().position);
                            view.target.position = dropped;
                            view.SetPosition(new Rect(dropped, Vector2.zero));
                            EditorUtility.SetDirty(view.target);
                            break;
                        case StickyNote movedNote when movedNote.userData is NodeNote noteModel:
                            SaveNote(movedNote, noteModel);
                            break;
                        case Group movedGroup when movedGroup.userData is NodeGroup groupModel:
                            Undo.RecordObject(graph, "Move Group");
                            groupModel.position = movedGroup.GetPosition().position;
                            EditorUtility.SetDirty(graph);
                            break;
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

                foreach (GraphElement element in change.elementsToRemove)
                {
                    if (element is Group removedGroup && removedGroup.userData is NodeGroup groupModel)
                    {
                        Undo.RecordObject(graph, "Delete Group");
                        graph.Groups.Remove(groupModel);
                        _groupViews.Remove(groupModel);
                        structuralChange = true;
                    }
                    else if (element is StickyNote removedNote && removedNote.userData is NodeNote noteModel)
                    {
                        Undo.RecordObject(graph, "Delete Note");
                        graph.Notes.Remove(noteModel);
                        _noteViews.Remove(noteModel);
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

        public void CreateNode(Type type, Vector2 screenMousePosition, NodePort connectTo = null)
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
                ConnectToFirstCompatiblePort(node, connectTo);
            }

            SyncFromModel();
        }

        private void ConnectToFirstCompatiblePort(Node node, NodePort source)
        {
            if (source == null)
            {
                return;
            }

            foreach (NodePort candidate in node.Ports)
            {
                if (candidate.direction == source.direction)
                {
                    continue;
                }

                NodePort output = source.IsOutput ? source : candidate;
                NodePort input = source.IsOutput ? candidate : source;
                if (graphEditor.CanConnect(output, input))
                {
                    output.Connect(input);
                    EditorUtility.SetDirty(source.node);
                    return;
                }
            }
        }

        /// <summary>Opens the create menu filtered to nodes that can accept the dragged connection.</summary>
        internal void OpenCreateMenuForPort(NodePort source, Vector2 screenPosition)
        {
            _searchProvider.pendingPort = source;
            SearchWindow.Open(new SearchWindowContext(screenPosition), _searchProvider);
        }

        /// <summary>
        /// Copy/duplicate serialize through the sidecar format, so a selection can be pasted as
        /// text elsewhere — and any valid graph JSON (hand-written or generated) can be pasted in.
        /// </summary>
        private string OnSerializeElements(IEnumerable<GraphElement> elements)
        {
            List<Node> nodes = elements.OfType<SaintsNodeView>().Select(view => view.target).ToList();
            return nodes.Count == 0 ? string.Empty : GraphJson.Export(graph, nodes);
        }

        // Any JSON object is accepted so that OnPaste can explain what is wrong with it —
        // a silently disabled Paste command teaches the user nothing.
        private bool OnCanPaste(string data)
        {
            return !string.IsNullOrWhiteSpace(data)
                   && data.TrimStart().StartsWith("{", StringComparison.Ordinal);
        }

        /// <summary>Where new content should land: the mouse if it has been over the canvas, else its centre.</summary>
        private Vector2 MouseInGraphSpace()
        {
            Vector2 world = _hasMousePosition ? _lastMouseWorld : worldBound.center;
            return contentViewContainer.WorldToLocal(world);
        }

        private void OnPaste(string operationName, string data)
        {
            // Paste lands under the cursor; duplicate (Ctrl+D) stays next to the original.
            Vector2 offset = new Vector2(40f, 40f);
            if (!string.Equals(operationName, "Duplicate", StringComparison.OrdinalIgnoreCase)
                && GraphJson.TryGetTopLeft(data, out Vector2 topLeft))
            {
                offset = MouseInGraphSpace() - topLeft;
            }

            if (data.Contains("saintsgraph-schema") || data.Contains("\"nodeTypes\""))
            {
                Debug.LogWarning("SaintsGraph: that is the node schema, not a graph. The schema describes " +
                                 "which node types exist — hand it to your tool or model, then paste back the " +
                                 "graph document it produces (the one with a \"nodes\" array).");
                return;
            }

            List<Node> created;
            try
            {
                created = GraphJson.Paste(graph, data, offset);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("SaintsGraph: could not paste graph JSON — " + exception.Message);
                return;
            }

            if (created.Count == 0)
            {
                return;
            }

            SyncFromModel();
            ClearSelection();
            foreach (Node node in created)
            {
                if (_nodeViews.TryGetValue(node, out SaintsNodeView view))
                {
                    AddToSelection(view);
                }
            }
        }
    }
}
