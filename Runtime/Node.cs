using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SaintsGraph
{
    /// <summary>
    /// Base class for all graph nodes. API mirrors xNode's <c>XNode.Node</c>:
    /// derive from it, mark fields with [Input]/[Output] and override <see cref="GetValue"/>.
    /// </summary>
    public abstract class Node : ScriptableObject
    {
        /// <summary>When should the backing value of a port field be shown in the editor.</summary>
        public enum ShowBackingValue
        {
            Never,
            Unconnected,
            Always
        }

        public enum ConnectionType
        {
            Multiple,
            Override
        }

        public enum TypeConstraint
        {
            None,
            /// <summary>Input's type must be assignable from output's type.</summary>
            Inherited,
            /// <summary>Types must match exactly.</summary>
            Strict,
            /// <summary>Output's type must be assignable from input's type.</summary>
            InheritedInverse,
            /// <summary>Assignable in either direction.</summary>
            InheritedAny
        }

        [AttributeUsage(AttributeTargets.Field)]
        public class InputAttribute : Attribute
        {
            public ShowBackingValue backingValue;
            public ConnectionType connectionType;
            public TypeConstraint typeConstraint;
            public bool dynamicPortList;

            public InputAttribute(ShowBackingValue backingValue = ShowBackingValue.Unconnected,
                ConnectionType connectionType = ConnectionType.Multiple,
                TypeConstraint typeConstraint = TypeConstraint.None,
                bool dynamicPortList = false)
            {
                this.backingValue = backingValue;
                this.connectionType = connectionType;
                this.typeConstraint = typeConstraint;
                this.dynamicPortList = dynamicPortList;
            }
        }

        [AttributeUsage(AttributeTargets.Field)]
        public class OutputAttribute : Attribute
        {
            public ShowBackingValue backingValue;
            public ConnectionType connectionType;
            public TypeConstraint typeConstraint;
            public bool dynamicPortList;

            public OutputAttribute(ShowBackingValue backingValue = ShowBackingValue.Never,
                ConnectionType connectionType = ConnectionType.Multiple,
                TypeConstraint typeConstraint = TypeConstraint.None,
                bool dynamicPortList = false)
            {
                this.backingValue = backingValue;
                this.connectionType = connectionType;
                this.typeConstraint = typeConstraint;
                this.dynamicPortList = dynamicPortList;
            }
        }

        /// <summary>Sets the create-menu path of this node type. Null or empty hides the node from the menu.</summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
        public class CreateNodeMenuAttribute : Attribute
        {
            public string menuName;
            public int order;

            public CreateNodeMenuAttribute(string menuName)
            {
                this.menuName = menuName;
            }

            public CreateNodeMenuAttribute(string menuName, int order)
            {
                this.menuName = menuName;
                this.order = order;
            }
        }

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
        public class NodeTintAttribute : Attribute
        {
            public Color color;

            public NodeTintAttribute(float r, float g, float b)
            {
                color = new Color(r, g, b);
            }

            public NodeTintAttribute(string hex)
            {
                ColorUtility.TryParseHtmlString(hex, out color);
            }

            public NodeTintAttribute(byte r, byte g, byte b)
            {
                color = new Color32(r, g, b, byte.MaxValue);
            }
        }

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
        public class NodeWidthAttribute : Attribute
        {
            public int width;

            public NodeWidthAttribute(int width)
            {
                this.width = width;
            }
        }

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
        public class DisallowMultipleNodesAttribute : Attribute
        {
            public int max;

            public DisallowMultipleNodesAttribute(int max = 1)
            {
                this.max = max;
            }
        }

        /// <summary>
        /// Used by <see cref="NodeGraph.AddNode"/> so that the graph reference is available
        /// during OnEnable of a freshly created node. Kept for xNode API parity.
        /// </summary>
        public static NodeGraph graphHotfix;

        public NodeGraph graph;
        public Vector2 position;

        /// <summary>Editor state kept with the node, like <see cref="position"/>: whether its body is folded away.</summary>
        [HideInInspector] public bool collapsed;

        /// <summary>Manually dragged node width, in graph units. Zero means "size to content".</summary>
        [HideInInspector] public float nodeWidth;

        [HideInInspector, SerializeField] private string uid;

        /// <summary>
        /// Identity that survives renames and reordering. The JSON sidecar matches nodes by this,
        /// so editing a node's name in the file (or in the editor) never orphans its connections.
        /// Generated on first use; copies always get a fresh one.
        /// </summary>
        public string Uid
        {
            get
            {
                if (string.IsNullOrEmpty(uid))
                {
                    uid = Guid.NewGuid().ToString("N");
                }

                return uid;
            }
        }

        /// <summary>Assigns a specific identity — used when importing a graph that already carries one.</summary>
        public void AdoptUid(string value)
        {
            uid = value;
        }

        /// <summary>Gives this node a brand new identity. Called for copies, which are not the original.</summary>
        public void ResetUid()
        {
            uid = Guid.NewGuid().ToString("N");
        }

        [SerializeField] private List<DynamicPortData> dynamicPorts = new List<DynamicPortData>();

        [Serializable]
        private class DynamicPortData
        {
            public string fieldName;
            public string typeQualifiedName;
            public NodePort.IO direction;
            public ConnectionType connectionType;
            public TypeConstraint typeConstraint;
        }

        [NonSerialized] private Dictionary<string, NodePort> _ports;

        public IEnumerable<NodePort> Ports
        {
            get
            {
                EnsurePorts();
                return _ports.Values;
            }
        }

        public IEnumerable<NodePort> Inputs => Ports.Where(port => port.IsInput);
        public IEnumerable<NodePort> Outputs => Ports.Where(port => port.IsOutput);
        public IEnumerable<NodePort> DynamicPorts => Ports.Where(port => port.IsDynamic);
        public IEnumerable<NodePort> DynamicInputs => Ports.Where(port => port.IsDynamic && port.IsInput);
        public IEnumerable<NodePort> DynamicOutputs => Ports.Where(port => port.IsDynamic && port.IsOutput);

        // Note for subclasses: do NOT declare your own OnEnable — it would hide this one.
        // Override Init() instead. Ports themselves are rebuilt lazily, so unlike xNode a
        // hidden OnEnable does not leave the node without ports; only Init() would be skipped.
        protected void OnEnable()
        {
            if (graphHotfix != null)
            {
                graph = graphHotfix;
                graphHotfix = null;
            }

            UpdatePorts();
            Init();
        }

        /// <summary>Initialization hook, called on load and on creation. Override this instead of OnEnable.</summary>
        protected virtual void Init()
        {
        }

        /// <summary>Drops the cached port list; it is rebuilt from reflection and serialized dynamic ports on next access.</summary>
        public void UpdatePorts()
        {
            _ports = null;
        }

        private void EnsurePorts()
        {
            if (_ports != null)
            {
                return;
            }

            _ports = new Dictionary<string, NodePort>();
            foreach (PortCache.PortTemplate template in PortCache.GetTemplates(GetType()))
            {
                // A dynamicPortList field has no port of its own — it spawns
                // per-element dynamic ports named "{field} {index}".
                if (template.dynamicPortList)
                {
                    continue;
                }

                _ports[template.fieldName] = new NodePort(template, this);
            }

            foreach (DynamicPortData data in dynamicPorts)
            {
                if (string.IsNullOrEmpty(data.fieldName) || _ports.ContainsKey(data.fieldName))
                {
                    continue;
                }

                Type type = string.IsNullOrEmpty(data.typeQualifiedName)
                    ? null
                    : Type.GetType(data.typeQualifiedName, false);
                NodePort port =
                    new NodePort(data.fieldName, type, data.direction, data.connectionType, data.typeConstraint, this);
                // Element ports of a dynamic port list take their metadata from the backing
                // field's attribute, so attribute edits propagate to existing ports.
                if (PortCache.TryGetDynamicListTemplate(GetType(), data.fieldName, out PortCache.PortTemplate listTemplate))
                {
                    port.ValueType = PortCache.GetListElementType(listTemplate.valueType);
                    port.direction = listTemplate.direction;
                    port.connectionType = listTemplate.connectionType;
                    port.typeConstraint = listTemplate.typeConstraint;
                }

                _ports[data.fieldName] = port;
            }
        }

        public NodePort GetPort(string fieldName)
        {
            EnsurePorts();
            return _ports.TryGetValue(fieldName, out NodePort port) ? port : null;
        }

        public bool HasPort(string fieldName)
        {
            EnsurePorts();
            return _ports.ContainsKey(fieldName);
        }

        public NodePort GetInputPort(string fieldName)
        {
            NodePort port = GetPort(fieldName);
            return port != null && port.IsInput ? port : null;
        }

        public NodePort GetOutputPort(string fieldName)
        {
            NodePort port = GetPort(fieldName);
            return port != null && port.IsOutput ? port : null;
        }

        public NodePort AddDynamicInput(Type type,
            ConnectionType connectionType = ConnectionType.Multiple,
            TypeConstraint typeConstraint = TypeConstraint.None,
            string fieldName = null)
        {
            return AddDynamicPort(type, NodePort.IO.Input, connectionType, typeConstraint, fieldName);
        }

        public NodePort AddDynamicOutput(Type type,
            ConnectionType connectionType = ConnectionType.Multiple,
            TypeConstraint typeConstraint = TypeConstraint.None,
            string fieldName = null)
        {
            return AddDynamicPort(type, NodePort.IO.Output, connectionType, typeConstraint, fieldName);
        }

        private NodePort AddDynamicPort(Type type, NodePort.IO direction, ConnectionType connectionType,
            TypeConstraint typeConstraint, string fieldName)
        {
            EnsurePorts();
            if (fieldName == null)
            {
                string prefix = direction == NodePort.IO.Input ? "dynamicInput_" : "dynamicOutput_";
                int index = 0;
                while (_ports.ContainsKey(fieldName = prefix + index))
                {
                    index++;
                }
            }
            else if (_ports.ContainsKey(fieldName))
            {
                Debug.LogWarning("Port '" + fieldName + "' already exists in " + name, this);
                return _ports[fieldName];
            }

            dynamicPorts.Add(new DynamicPortData
            {
                fieldName = fieldName,
                typeQualifiedName = type == null ? null : type.AssemblyQualifiedName,
                direction = direction,
                connectionType = connectionType,
                typeConstraint = typeConstraint
            });
            NodePort port = new NodePort(fieldName, type, direction, connectionType, typeConstraint, this);
            _ports[fieldName] = port;
            return port;
        }

        public void RemoveDynamicPort(string fieldName)
        {
            NodePort port = GetPort(fieldName);
            if (port == null || port.IsStatic)
            {
                throw new ArgumentException("Port '" + fieldName + "' does not exist or is static");
            }

            RemoveDynamicPort(port);
        }

        public void RemoveDynamicPort(NodePort port)
        {
            if (port == null)
            {
                throw new ArgumentNullException(nameof(port));
            }

            if (port.IsStatic)
            {
                throw new ArgumentException("Cannot remove static port '" + port.fieldName + "'");
            }

            port.ClearConnections();
            dynamicPorts.RemoveAll(data => data.fieldName == port.fieldName);
            _ports?.Remove(port.fieldName);
        }

        [ContextMenu("Clear Dynamic Ports")]
        public void ClearDynamicPorts()
        {
            foreach (NodePort port in DynamicPorts.ToList())
            {
                RemoveDynamicPort(port);
            }
        }

        /// <summary>Return the calculated value of the given output port. Called by connected nodes pulling data.</summary>
        public virtual object GetValue(NodePort port)
        {
            Debug.LogWarning("No GetValue(NodePort port) override defined for " + GetType());
            return null;
        }

        public T GetInputValue<T>(string fieldName, T fallback = default)
        {
            NodePort port = GetInputPort(fieldName);
            return port != null && port.IsConnected ? port.GetInputValue<T>() : fallback;
        }

        public T[] GetInputValues<T>(string fieldName, params T[] fallback)
        {
            NodePort port = GetInputPort(fieldName);
            return port != null && port.IsConnected ? port.GetInputValues<T>() : fallback;
        }

        /// <summary>Called on both endpoints after a connection is created. <paramref name="from"/> is always the output port, <paramref name="to"/> always the input port.</summary>
        public virtual void OnCreateConnection(NodePort from, NodePort to)
        {
        }

        /// <summary>Called on a node when one of its ports is disconnected. Receives the local port.</summary>
        public virtual void OnRemoveConnection(NodePort port)
        {
        }

        public void ClearConnections()
        {
            foreach (NodePort port in Ports)
            {
                port.ClearConnections();
            }
        }

        /// <summary>Removes edges whose endpoints no longer resolve to valid ports.</summary>
        public void VerifyConnections()
        {
            if (graph != null)
            {
                graph.PruneInvalidEdges();
            }
        }
    }
}
