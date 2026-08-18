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
        private Action _bodyTeardown;

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
            expanded = true;
            RefreshExpandedState();
            mainContainer.Bind(_serializedObject);
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
                if (anchor == null)
                {
                    body.Add(MakeLabelRow(port, pill));
                }
                else if (port.IsStatic && !ShowsBackingField(port))
                {
                    ReplaceWithLabelRow(anchor, port, pill);
                }
                else
                {
                    WrapWithPill(anchor, pill, port.IsInput);
                }
            }
        }

        private static void ReplaceWithLabelRow(VisualElement anchor, NodePort port, Port pill)
        {
            VisualElement parent = anchor.parent;
            int index = parent.IndexOf(anchor);
            anchor.RemoveFromHierarchy();
            parent.Insert(index, MakeLabelRow(port, pill));
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

        private static void WrapWithPill(VisualElement anchor, Port pill, bool isInput)
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
