using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using GraphViewNode = UnityEditor.Experimental.GraphView.Node;

namespace SaintsGraph.Editor
{
    /// <summary>View of one node: title with tint, body with serialized fields and inline ports.</summary>
    internal class SaintsNodeView : GraphViewNode
    {
        public readonly Node target;
        public readonly SaintsNodeEditor editor;
        public readonly Dictionary<string, Port> portViews = new Dictionary<string, Port>();

        private readonly SerializedObject _serializedObject;
        private readonly SaintsGraphView _graphView;

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

        private void BuildBody()
        {
            VisualElement body = editor.CreateBody();
            if (body == null)
            {
                body = BuildDefaultBody();
            }

            body.AddToClassList("saints-node-body");

            // Ports the body did not place (dynamic ports; all ports for custom bodies).
            foreach (NodePort port in target.Ports)
            {
                if (!portViews.ContainsKey(port.fieldName))
                {
                    body.Add(MakePortRow(port, null));
                }
            }

            extensionContainer.Add(body);
        }

        private VisualElement BuildDefaultBody()
        {
            VisualElement container = new VisualElement();
            SerializedProperty property = _serializedObject.GetIterator();
            if (property.NextVisible(true))
            {
                do
                {
                    if (property.name == "m_Script" || property.name == "graph"
                        || property.name == "position" || property.name == "dynamicPorts")
                    {
                        continue;
                    }

                    NodePort port = target.GetPort(property.name);
                    if (port == null)
                    {
                        container.Add(new PropertyField(property.Copy()));
                    }
                    else
                    {
                        container.Add(MakePortRow(port, property.Copy()));
                    }
                } while (property.NextVisible(false));
            }

            return container;
        }

        private VisualElement MakePortRow(NodePort port, SerializedProperty property)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("saints-port-row");

            Port view = Port.Create<Edge>(Orientation.Horizontal,
                port.IsInput ? Direction.Input : Direction.Output,
                port.connectionType == Node.ConnectionType.Multiple ? Port.Capacity.Multi : Port.Capacity.Single,
                port.ValueType ?? typeof(object));
            view.portName = "";
            view.userData = port;
            view.portColor = _graphView.graphEditor.GetPortColor(port);
            portViews[port.fieldName] = view;

            string niceName = ObjectNames.NicifyVariableName(port.fieldName);
            Node.ShowBackingValue backing = port.IsStatic
                ? NodeEditorUtilities.GetBackingValue(target, port.fieldName)
                : Node.ShowBackingValue.Never;
            bool showField = property != null
                             && (backing == Node.ShowBackingValue.Always
                                 || (backing == Node.ShowBackingValue.Unconnected && !port.IsConnected));

            VisualElement content;
            if (showField)
            {
                PropertyField field = new PropertyField(property, niceName);
                field.style.flexGrow = 1;
                content = field;
            }
            else
            {
                Label label = new Label(niceName);
                label.style.flexGrow = 1;
                if (port.IsOutput)
                {
                    label.style.unityTextAlign = TextAnchor.MiddleRight;
                }

                content = label;
            }

            if (port.IsInput)
            {
                row.Add(view);
                row.Add(content);
            }
            else
            {
                row.Add(content);
                row.Add(view);
            }

            return row;
        }
    }
}
