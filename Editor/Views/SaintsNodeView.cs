using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using GraphViewNode = UnityEditor.Experimental.GraphView.Node;

namespace SaintsGraph.Editor
{
    /// <summary>
    /// View of one node: title with tint, body with serialized fields and inline ports.
    /// The body comes from the node editor's CreateBody override, the registered
    /// <see cref="INodeBodyBuilder"/> (SaintsField integration), or the built-in
    /// PropertyField loop — port pills are then attached onto the matching field rows.
    /// </summary>
    internal class SaintsNodeView : GraphViewNode
    {
        public readonly Node target;
        public readonly SaintsNodeEditor editor;
        public readonly Dictionary<string, Port> portViews = new Dictionary<string, Port>();

        private readonly SerializedObject _serializedObject;
        private readonly SaintsGraphView _graphView;
        private readonly Dictionary<string, VisualElement> _portRows = new Dictionary<string, VisualElement>();
        private Action _bodyTeardown;

        /// <summary>
        /// Collapsing hides the body (extensionContainer) where port pills normally live, which
        /// would leave edges pointing into nothing. While collapsed, connected pills are moved
        /// into the standard input/output containers next to the title; on expand they return
        /// into their body rows. Guards exist because the base constructor touches this setter
        /// before our fields are initialized.
        /// </summary>
        public override bool expanded
        {
            get => base.expanded;
            set
            {
                base.expanded = value;
                _graphView?.SetExpandedState(target, value);
                UpdatePortPlacement();
            }
        }

        public SaintsNodeView(Node target, SaintsGraphView graphView, SaintsGraphEditor graphEditor)
        {
            this.target = target;
            _graphView = graphView;
            _serializedObject = new SerializedObject(target);

            editor = EditorTypeCache.CreateNodeEditor(target);
            editor.target = target;
            editor.serializedObject = _serializedObject;
            editor.graphEditor = graphEditor;
            editor.OnCreate();

            title = string.IsNullOrEmpty(target.name) ? target.GetType().Name : target.name;
            string headerTooltip = editor.GetHeaderTooltip();
            if (!string.IsNullOrEmpty(headerTooltip))
            {
                titleContainer.tooltip = headerTooltip;
            }

            titleContainer.style.backgroundColor = editor.GetTint();
            style.minWidth = editor.GetWidth();

            VisualElement customHeader = editor.CreateHeader();
            if (customHeader != null)
            {
                Label defaultTitle = titleContainer.Q<Label>("title-label");
                if (defaultTitle != null)
                {
                    defaultTitle.style.display = DisplayStyle.None;
                }

                titleContainer.Insert(0, customHeader);
            }

            BuildBody();
            SetPosition(new Rect(target.position, Vector2.zero));
            RefreshExpandedState();
            mainContainer.Bind(_serializedObject);

            if (!graphView.GetExpandedState(target))
            {
                expanded = false;
            }
        }

        /// <summary>Releases body resources (e.g. SaintsField renderers). Called before the view is discarded.</summary>
        public void Teardown()
        {
            Action teardown = _bodyTeardown;
            _bodyTeardown = null;
            teardown?.Invoke();
        }

        private void BuildBody()
        {
            // Port fields are NOT skipped: the builder places them at their natural position,
            // and hidden backing values are then replaced with a label row in place —
            // otherwise a connected port's row would jump to the end of the body.
            List<string> skipFields = new List<string> { "m_Script", "graph", "position", "dynamicPorts" };

            VisualElement body = editor.CreateBody();
            if (body == null && NodeBodyBuilderRegistry.builder != null)
            {
                body = NodeBodyBuilderRegistry.builder.Build(editor, skipFields, out _bodyTeardown);
            }

            if (body == null)
            {
                body = BuildDefaultBody(skipFields);
            }

            body.AddToClassList("saints-node-body");
            AttachPortPills(body);
            extensionContainer.Add(body);
        }

        private bool ShowsBackingField(NodePort port)
        {
            Node.ShowBackingValue backing = NodeEditorUtilities.GetBackingValue(target, port.fieldName);
            return backing == Node.ShowBackingValue.Always
                   || (backing == Node.ShowBackingValue.Unconnected && !port.IsConnected);
        }

        private VisualElement BuildDefaultBody(ICollection<string> skipFields)
        {
            VisualElement container = new VisualElement();
            SerializedProperty property = _serializedObject.GetIterator();
            if (property.NextVisible(true))
            {
                do
                {
                    if (skipFields.Contains(property.name))
                    {
                        continue;
                    }

                    container.Add(new PropertyField(property.Copy()));
                } while (property.NextVisible(false));
            }

            return container;
        }

        private void AttachPortPills(VisualElement body)
        {
            foreach (NodePort port in target.Ports)
            {
                Port pill = MakePortPill(port);
                VisualElement anchor = FindBoundElement(body, port.fieldName);
                VisualElement row;
                if (anchor == null)
                {
                    row = MakeLabelRow(port, pill);
                    body.Add(row);
                }
                else if (port.IsStatic && !ShowsBackingField(port))
                {
                    row = ReplaceWithLabelRow(anchor, port, pill);
                }
                else
                {
                    row = WrapWithPill(anchor, pill, port.IsInput);
                }

                _portRows[port.fieldName] = row;
            }
        }

        private void UpdatePortPlacement()
        {
            if (_portRows == null || portViews == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Port> entry in portViews)
            {
                Port pill = entry.Value;
                if (!(pill.userData is NodePort port) || !_portRows.TryGetValue(entry.Key, out VisualElement row))
                {
                    continue;
                }

                bool showCollapsed = !expanded && port.IsConnected;
                if (showCollapsed)
                {
                    if (pill.parent != inputContainer && pill.parent != outputContainer)
                    {
                        pill.portName = ObjectNames.NicifyVariableName(port.fieldName);
                        pill.RemoveFromHierarchy();
                        (port.IsInput ? inputContainer : outputContainer).Add(pill);
                    }
                }
                else if (pill.parent != row)
                {
                    pill.portName = "";
                    pill.RemoveFromHierarchy();
                    if (port.IsInput)
                    {
                        row.Insert(0, pill);
                    }
                    else
                    {
                        row.Add(pill);
                    }
                }
            }

            RefreshPorts();
        }

        private static VisualElement ReplaceWithLabelRow(VisualElement anchor, NodePort port, Port pill)
        {
            VisualElement parent = anchor.parent;
            int index = parent.IndexOf(anchor);
            anchor.RemoveFromHierarchy();
            VisualElement row = MakeLabelRow(port, pill);
            parent.Insert(index, row);
            return row;
        }

        private Port MakePortPill(NodePort port)
        {
            Port view = Port.Create<Edge>(Orientation.Horizontal,
                port.IsInput ? Direction.Input : Direction.Output,
                port.connectionType == Node.ConnectionType.Multiple ? Port.Capacity.Multi : Port.Capacity.Single,
                port.ValueType ?? typeof(object));
            view.portName = "";
            view.userData = port;
            view.portColor = _graphView.graphEditor.GetPortColor(port);
            portViews[port.fieldName] = view;
            return view;
        }

        private static VisualElement FindBoundElement(VisualElement body, string fieldName)
        {
            return body.Query<VisualElement>()
                .Where(element => element is IBindable bindable && bindable.bindingPath == fieldName)
                .First();
        }

        private static VisualElement WrapWithPill(VisualElement anchor, Port pill, bool isInput)
        {
            VisualElement parent = anchor.parent;
            int index = parent.IndexOf(anchor);
            VisualElement row = new VisualElement();
            row.AddToClassList("saints-port-row");
            anchor.RemoveFromHierarchy();
            anchor.style.flexGrow = 1;
            if (isInput)
            {
                row.Add(pill);
                row.Add(anchor);
            }
            else
            {
                row.Add(anchor);
                row.Add(pill);
            }

            parent.Insert(index, row);
            return row;
        }

        private static VisualElement MakeLabelRow(NodePort port, Port pill)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("saints-port-row");
            Label label = new Label(ObjectNames.NicifyVariableName(port.fieldName));
            label.style.flexGrow = 1;
            if (port.IsOutput)
            {
                label.style.unityTextAlign = TextAnchor.MiddleRight;
            }

            if (port.IsInput)
            {
                row.Add(pill);
                row.Add(label);
            }
            else
            {
                row.Add(label);
                row.Add(pill);
            }

            return row;
        }
    }
}
