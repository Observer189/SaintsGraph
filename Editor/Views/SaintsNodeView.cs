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
    ///
    /// Bodies are built lazily (see <see cref="EnsureBodyBuilt"/>), driven by the graph view:
    /// on a large graph only what is on screen pays for its body.
    /// </summary>
    internal class SaintsNodeView : GraphViewNode
    {
        public readonly Node target;
        public readonly SaintsNodeEditor editor;
        public readonly Dictionary<string, Port> portViews = new Dictionary<string, Port>();

        private readonly SerializedObject _serializedObject;
        private readonly SaintsGraphView _graphView;
        private readonly Dictionary<string, VisualElement> _portRows = new Dictionary<string, VisualElement>();

        /// <summary>Per port: the bound field element and the label shown when its value is hidden.</summary>
        private readonly Dictionary<string, (VisualElement field, VisualElement label)> _portContent =
            new Dictionary<string, (VisualElement, VisualElement)>();

        private List<PortCache.PortTemplate> _listTemplates = new List<PortCache.PortTemplate>();
        private Action _bodyTeardown;
        private bool _bodyBuilt;

        public bool BodyBuilt => _bodyBuilt;

        public override bool expanded
        {
            get => base.expanded;
            set
            {
                base.expanded = value;
                _graphView?.SetExpandedState(target, value);
                if (value)
                {
                    EnsureBodyBuilt();
                }

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

            CreatePortPills();
            if (!graphView.GetExpandedState(target))
            {
                expanded = false;
            }

            UpdatePortPlacement();
            SetPosition(new Rect(target.position, Vector2.zero));
            RefreshExpandedState();
        }

        public void SetCycleWarning(bool inCycle)
        {
            EnableInClassList("saints-node-in-cycle", inCycle);
            tooltip = inCycle
                ? "This node is part of a cycle. Pull evaluation (GetInputValue) would recurse forever."
                : null;
        }

        /// <summary>Releases body resources (e.g. SaintsField renderers). Called before the view is discarded.</summary>
        public void Teardown()
        {
            Action teardown = _bodyTeardown;
            _bodyTeardown = null;
            teardown?.Invoke();
        }

        /// <summary>
        /// Swaps rows between "editable field" and "label" when a port's connected state changes,
        /// without rebuilding the body — the elements are kept and only their display toggles.
        /// </summary>
        public void RefreshConnectedState()
        {
            foreach (KeyValuePair<string, (VisualElement field, VisualElement label)> entry in _portContent)
            {
                NodePort port = target.GetPort(entry.Key);
                if (port == null || entry.Value.field == null || entry.Value.label == null)
                {
                    continue;
                }

                bool showField = ShowsBackingField(port);
                entry.Value.field.style.display = showField ? DisplayStyle.Flex : DisplayStyle.None;
                entry.Value.label.style.display = showField ? DisplayStyle.None : DisplayStyle.Flex;
            }

            UpdatePortPlacement();
        }

        public void EnsureBodyBuilt()
        {
            if (_bodyBuilt || portViews == null || !expanded)
            {
                return;
            }

            _bodyBuilt = true;
            BuildBody();
            RefreshExpandedState();
            mainContainer.Bind(_serializedObject);
        }

        private void CreatePortPills()
        {
            foreach (NodePort port in target.Ports)
            {
                MakePortPill(port);
            }
        }

        private void BuildBody()
        {
            // Port fields are NOT skipped: the builder places them at their natural position, and
            // hidden backing values are toggled to a label in place — otherwise a connected port's
            // row would jump to the end of the body. Backing fields of dynamic port lists ARE
            // skipped: they render as custom list blocks with one port per element.
            List<string> skipFields = new List<string> { "m_Script", "graph", "position", "dynamicPorts" };
            _listTemplates = new List<PortCache.PortTemplate>();
            foreach (PortCache.PortTemplate template in PortCache.GetTemplates(target.GetType()))
            {
                if (template.dynamicPortList)
                {
                    _listTemplates.Add(template);
                    skipFields.Add(template.fieldName);
                }
            }

            SyncDynamicLists();

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
            if (port.IsDynamic)
            {
                return false;
            }

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
                        if (IsDynamicListField(property.name))
                        {
                            container.Add(new VisualElement { name = "saints-dpl-" + property.name });
                        }

                        continue;
                    }

                    container.Add(new PropertyField(property.Copy()));
                } while (property.NextVisible(false));
            }

            return container;
        }

        private void AttachPortPills(VisualElement body)
        {
            foreach (PortCache.PortTemplate template in _listTemplates)
            {
                VisualElement block = BuildDynamicListBlock(template);
                VisualElement placeholder = body.Q<VisualElement>("saints-dpl-" + template.fieldName);
                if (placeholder != null)
                {
                    VisualElement parent = placeholder.parent;
                    int index = parent.IndexOf(placeholder);
                    placeholder.RemoveFromHierarchy();
                    parent.Insert(index, block);
                }
                else
                {
                    body.Add(block);
                }
            }

            foreach (NodePort port in target.Ports)
            {
                if (_portRows.ContainsKey(port.fieldName))
                {
                    continue; // already placed by a dynamic list block
                }

                if (!portViews.TryGetValue(port.fieldName, out Port pill))
                {
                    pill = MakePortPill(port);
                }

                pill.portName = "";
                pill.style.display = DisplayStyle.Flex;

                VisualElement anchor = FindBoundElement(body, port.fieldName);
                if (anchor == null)
                {
                    VisualElement labelRow = MakeLabelRow(port, pill);
                    body.Add(labelRow);
                    _portRows[port.fieldName] = labelRow;
                }
                else
                {
                    _portRows[port.fieldName] = WrapWithPill(port, anchor, pill);
                }
            }

            UpdatePortPlacement();
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
                if (!(pill.userData is NodePort port))
                {
                    continue;
                }

                if (!_bodyBuilt)
                {
                    // No body rows exist yet — either the node is collapsed, or its body has not
                    // been built for the viewport yet. Connected pills live in the compact
                    // containers so edges keep a correct anchor either way.
                    if (pill.parent != inputContainer && pill.parent != outputContainer)
                    {
                        pill.portName = ObjectNames.NicifyVariableName(port.fieldName);
                        (port.IsInput ? inputContainer : outputContainer).Add(pill);
                    }

                    pill.style.display = port.IsConnected ? DisplayStyle.Flex : DisplayStyle.None;
                    continue;
                }

                if (!_portRows.TryGetValue(entry.Key, out VisualElement row))
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

                    pill.style.display = DisplayStyle.Flex;
                }
                else if (pill.parent != row)
                {
                    pill.portName = "";
                    pill.style.display = DisplayStyle.Flex;
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

        private bool IsDynamicListField(string fieldName)
        {
            foreach (PortCache.PortTemplate template in _listTemplates)
            {
                if (template.fieldName == fieldName)
                {
                    return true;
                }
            }

            return false;
        }

        private void SyncDynamicLists()
        {
            bool recorded = false;
            foreach (PortCache.PortTemplate template in _listTemplates)
            {
                SerializedProperty listProperty = _serializedObject.FindProperty(template.fieldName);
                if (listProperty == null || !listProperty.isArray
                    || listProperty.propertyType != SerializedPropertyType.Generic)
                {
                    continue;
                }

                if (DynamicPortListOps.CountElements(target, template.fieldName) == listProperty.arraySize)
                {
                    continue;
                }

                if (!recorded)
                {
                    Undo.RecordObject(target, "Sync Dynamic Ports");
                    Undo.RecordObject(_graphView.graph, "Sync Dynamic Ports");
                    recorded = true;
                }

                DynamicPortListOps.Sync(target, template, listProperty.arraySize);
            }

            if (recorded)
            {
                EditorUtility.SetDirty(target);
                EditorUtility.SetDirty(_graphView.graph);
            }
        }

        private VisualElement BuildDynamicListBlock(PortCache.PortTemplate template)
        {
            string fieldName = template.fieldName;
            SerializedProperty listProperty = _serializedObject.FindProperty(fieldName);
            bool hasBacking = listProperty != null && listProperty.isArray
                              && listProperty.propertyType == SerializedPropertyType.Generic;

            VisualElement block = new VisualElement();
            block.AddToClassList("saints-dynamic-list");

            VisualElement header = new VisualElement();
            header.AddToClassList("saints-dynamic-list__header");
            Label title = new Label(ObjectNames.NicifyVariableName(fieldName));
            title.style.flexGrow = 1;
            header.Add(title);
            Button addButton = new Button(() => AddListElement(template)) { text = "+" };
            addButton.AddToClassList("saints-dynamic-list__button");
            header.Add(addButton);
            block.Add(header);

            int count = DynamicPortListOps.CountElements(target, fieldName);
            for (int i = 0; i < count; i++)
            {
                block.Add(BuildDynamicListRow(template, listProperty, hasBacking, i, count));
            }

            return block;
        }

        private VisualElement BuildDynamicListRow(PortCache.PortTemplate template, SerializedProperty listProperty,
            bool hasBacking, int index, int count)
        {
            string portName = DynamicPortListOps.ElementName(template.fieldName, index);
            VisualElement row = new VisualElement();
            row.AddToClassList("saints-port-row");

            NodePort port = target.GetPort(portName);
            if (port == null)
            {
                return row;
            }

            if (!portViews.TryGetValue(portName, out Port pill))
            {
                pill = MakePortPill(port);
            }

            pill.portName = "";
            pill.style.display = DisplayStyle.Flex;

            bool showField = hasBacking && index < listProperty.arraySize
                             && (template.backingValue == Node.ShowBackingValue.Always
                                 || (template.backingValue == Node.ShowBackingValue.Unconnected && !port.IsConnected));

            VisualElement content;
            if (showField)
            {
                PropertyField field = new PropertyField(listProperty.GetArrayElementAtIndex(index), "Element " + index);
                field.style.flexGrow = 1;
                content = field;
            }
            else
            {
                Label label = new Label("Element " + index);
                label.style.flexGrow = 1;
                if (port.IsOutput)
                {
                    label.style.unityTextAlign = TextAnchor.MiddleRight;
                }

                content = label;
            }

            Button up = new Button(() => MoveListElement(template, index, index - 1)) { text = "▲" };
            up.SetEnabled(index > 0);
            Button down = new Button(() => MoveListElement(template, index, index + 1)) { text = "▼" };
            down.SetEnabled(index < count - 1);
            Button remove = new Button(() => RemoveListElement(template, index)) { text = "✕" };
            up.AddToClassList("saints-dynamic-list__button");
            down.AddToClassList("saints-dynamic-list__button");
            remove.AddToClassList("saints-dynamic-list__button");

            if (port.IsInput)
            {
                row.Add(pill);
                row.Add(content);
                row.Add(up);
                row.Add(down);
                row.Add(remove);
            }
            else
            {
                row.Add(up);
                row.Add(down);
                row.Add(remove);
                row.Add(content);
                row.Add(pill);
            }

            _portRows[portName] = row;
            return row;
        }

        private void AddListElement(PortCache.PortTemplate template)
        {
            Undo.RecordObject(target, "Add Port Element");
            SerializedProperty listProperty = _serializedObject.FindProperty(template.fieldName);
            if (listProperty != null && listProperty.isArray
                && listProperty.propertyType == SerializedPropertyType.Generic)
            {
                listProperty.arraySize++;
                _serializedObject.ApplyModifiedProperties();
            }

            DynamicPortListOps.AddElement(target, template);
            AfterListEdit();
        }

        private void RemoveListElement(PortCache.PortTemplate template, int index)
        {
            Undo.RecordObject(target, "Remove Port Element");
            Undo.RecordObject(_graphView.graph, "Remove Port Element");
            DynamicPortListOps.RemoveElement(target, template.fieldName, index);
            SerializedProperty listProperty = _serializedObject.FindProperty(template.fieldName);
            if (listProperty != null && listProperty.isArray
                && listProperty.propertyType == SerializedPropertyType.Generic
                && index < listProperty.arraySize)
            {
                listProperty.DeleteArrayElementAtIndex(index);
                _serializedObject.ApplyModifiedProperties();
            }

            AfterListEdit();
        }

        private void MoveListElement(PortCache.PortTemplate template, int from, int to)
        {
            Undo.RecordObject(target, "Move Port Element");
            Undo.RecordObject(_graphView.graph, "Move Port Element");
            SerializedProperty listProperty = _serializedObject.FindProperty(template.fieldName);
            if (listProperty != null && listProperty.isArray
                && listProperty.propertyType == SerializedPropertyType.Generic
                && from < listProperty.arraySize && to >= 0 && to < listProperty.arraySize)
            {
                listProperty.MoveArrayElement(from, to);
                _serializedObject.ApplyModifiedProperties();
            }

            DynamicPortListOps.MoveElement(target, template.fieldName, from, to);
            AfterListEdit();
        }

        private void AfterListEdit()
        {
            EditorUtility.SetDirty(target);
            EditorUtility.SetDirty(_graphView.graph);
            // Element count and port identities changed: this node's body has to be rebuilt,
            // but the rest of the graph does not.
            _graphView.RebuildNodeView(target);
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

            // Swap in our listener so dropping a connection on empty canvas offers a node to create.
            if (view.edgeConnector != null)
            {
                view.RemoveManipulator(view.edgeConnector);
            }

            view.AddManipulator(new EdgeConnector<Edge>(_graphView.edgeConnectorListener));

            portViews[port.fieldName] = view;
            return view;
        }

        private static VisualElement FindBoundElement(VisualElement body, string fieldName)
        {
            return body.Query<VisualElement>()
                .Where(element => element is IBindable bindable && bindable.bindingPath == fieldName)
                .First();
        }

        /// <summary>Wraps a bound field in a port row, with a label that replaces it while the value is hidden.</summary>
        private VisualElement WrapWithPill(NodePort port, VisualElement anchor, Port pill)
        {
            VisualElement parent = anchor.parent;
            int index = parent.IndexOf(anchor);
            VisualElement row = new VisualElement();
            row.AddToClassList("saints-port-row");
            anchor.RemoveFromHierarchy();
            anchor.style.flexGrow = 1;

            Label label = new Label(ObjectNames.NicifyVariableName(port.fieldName));
            label.style.flexGrow = 1;
            if (port.IsOutput)
            {
                label.style.unityTextAlign = TextAnchor.MiddleRight;
            }

            bool showField = ShowsBackingField(port);
            anchor.style.display = showField ? DisplayStyle.Flex : DisplayStyle.None;
            label.style.display = showField ? DisplayStyle.None : DisplayStyle.Flex;
            _portContent[port.fieldName] = (anchor, label);

            if (port.IsInput)
            {
                row.Add(pill);
                row.Add(anchor);
                row.Add(label);
            }
            else
            {
                row.Add(label);
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
