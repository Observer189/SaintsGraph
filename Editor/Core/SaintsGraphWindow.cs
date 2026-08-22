using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SaintsGraph.Editor
{
    /// <summary>The graph editor window. Opens on double-clicking a <see cref="NodeGraph"/> asset.</summary>
    public class SaintsGraphWindow : EditorWindow
    {
        [SerializeField] private NodeGraph graph;

        private SaintsGraphView _view;
        private VisualElement _viewContainer;
        private Label _titleLabel;
        private ToolbarSearchField _searchField;

        [OnOpenAsset(0)]
        private static bool OnOpenAsset(int instanceID, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceID) is NodeGraph nodeGraph)
            {
                Open(nodeGraph);
                return true;
            }

            return false;
        }

        public static SaintsGraphWindow Open(NodeGraph graph)
        {
            SaintsGraphWindow window = GetWindow<SaintsGraphWindow>();
            window.titleContent = new GUIContent("SaintsGraph");
            window.SetGraph(graph);
            window.Focus();
            return window;
        }

        public NodeGraph Graph => graph;

        /// <summary>Re-syncs the view with the model (e.g. after a JSON sidecar import).</summary>
        public void ReloadViewFromModel()
        {
            _view?.ScheduleReload();
        }

        public void SetGraph(NodeGraph value)
        {
            graph = value;
            RebuildView();
        }

        private void CreateGUI()
        {
            Toolbar toolbar = new Toolbar();
            _titleLabel = new Label("No graph selected");
            _titleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            toolbar.Add(_titleLabel);
            VisualElement spacer = new VisualElement { style = { flexGrow = 1 } };
            toolbar.Add(spacer);
            _searchField = new ToolbarSearchField();
            _searchField.style.width = 180;
            _searchField.RegisterValueChangedCallback(evt => _view?.ApplySearch(evt.newValue));
            _searchField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    _view?.FocusNextMatch();
                    evt.StopPropagation();
                }
            });
            toolbar.Add(_searchField);
            toolbar.Add(new ToolbarButton(() => _view?.FrameAll()) { text = "Frame All" });
            toolbar.Add(new ToolbarButton(SaveGraph) { text = "Save" });
            rootVisualElement.Add(toolbar);

            // Ctrl/Cmd+F jumps to the search field, Enter cycles through matches.
            rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.F && (evt.ctrlKey || evt.commandKey))
                {
                    _searchField.Q<TextField>()?.Focus();
                    evt.StopPropagation();
                }
            });

            _viewContainer = new VisualElement { style = { flexGrow = 1 } };
            rootVisualElement.Add(_viewContainer);
            RebuildView();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            SaveGraph();
        }

        private void OnUndoRedo()
        {
            if (graph != null)
            {
                GraphAssetSanitizer.Sanitize(graph);
                // Rewrites the asset file so destroyed sub-assets (undone creation) leave it
                // before the Project browser tries to draw them.
                AssetDatabase.SaveAssets();
            }

            _view?.ScheduleReload();
        }

        private void RebuildView()
        {
            if (_viewContainer == null)
            {
                return;
            }

            _view?.TeardownNodeViews();
            _viewContainer.Clear();
            _view = null;
            if (graph == null)
            {
                _titleLabel.text = "No graph selected";
                return;
            }

            _titleLabel.text = graph.name;
            _view = new SaintsGraphView(graph, this) { style = { flexGrow = 1 } };
            _viewContainer.Add(_view);
        }

        private void SaveGraph()
        {
            if (graph == null)
            {
                return;
            }

            EditorUtility.SetDirty(graph);
            foreach (Node node in graph.nodes)
            {
                if (node != null)
                {
                    EditorUtility.SetDirty(node);
                }
            }

            AssetDatabase.SaveAssets();
        }
    }
}
