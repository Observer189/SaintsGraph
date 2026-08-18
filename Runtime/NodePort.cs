using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SaintsGraph
{
    /// <summary>
    /// One input or output on a node. Port objects are rebuilt from reflection and
    /// serialized dynamic-port data; connections live in <see cref="NodeGraph"/> and
    /// are looked up through it, so there is nothing to go out of sync.
    /// </summary>
    public class NodePort
    {
        public enum IO
        {
            Input,
            Output
        }

        private readonly string _fieldName;
        private readonly Node _node;
        private readonly bool _dynamic;
        private Type _valueType;

        public string fieldName => _fieldName;
        public Node node => _node;
        public IO direction { get; internal set; }
        public Node.ConnectionType connectionType { get; internal set; }
        public Node.TypeConstraint typeConstraint { get; internal set; }

        public Type ValueType
        {
            get => _valueType;
            set => _valueType = value;
        }

        public bool IsDynamic => _dynamic;
        public bool IsStatic => !_dynamic;
        public bool IsInput => direction == IO.Input;
        public bool IsOutput => direction == IO.Output;

        public bool IsConnected => Graph != null && Graph.GetEdges(this).Any();
        public int ConnectionCount => Graph == null ? 0 : Graph.GetEdges(this).Count();

        /// <summary>First connected port, or null.</summary>
        public NodePort Connection => GetConnections().FirstOrDefault();

        private NodeGraph Graph => _node == null ? null : _node.graph;

        /// <summary>Creates a dynamic port. Prefer <see cref="Node.AddDynamicInput"/>/<see cref="Node.AddDynamicOutput"/>.</summary>
        public NodePort(string fieldName, Type type, IO direction, Node.ConnectionType connectionType,
            Node.TypeConstraint typeConstraint, Node node)
        {
            _fieldName = fieldName;
            _valueType = type;
            this.direction = direction;
            this.connectionType = connectionType;
            this.typeConstraint = typeConstraint;
            _node = node;
            _dynamic = true;
        }

        internal NodePort(PortCache.PortTemplate template, Node node)
        {
            _fieldName = template.fieldName;
            _valueType = template.valueType;
            direction = template.direction;
            connectionType = template.connectionType;
            typeConstraint = template.typeConstraint;
            _node = node;
            _dynamic = false;
        }

        public List<NodePort> GetConnections()
        {
            List<NodePort> result = new List<NodePort>();
            NodeGraph graph = Graph;
            if (graph == null)
            {
                return result;
            }

            foreach (NodeEdge edge in graph.GetEdges(this))
            {
                NodePort other = edge.GetOtherPort(this);
                if (other != null)
                {
                    result.Add(other);
                }
            }

            return result;
        }

        public NodePort GetConnection(int i)
        {
            return GetConnections()[i];
        }

        public int GetConnectionIndex(NodePort port)
        {
            List<NodePort> connections = GetConnections();
            for (int i = 0; i < connections.Count; i++)
            {
                if (ReferenceEquals(connections[i].node, port.node) && connections[i].fieldName == port.fieldName)
                {
                    return i;
                }
            }

            return -1;
        }

        public bool IsConnectedTo(NodePort port)
        {
            return Graph != null && Graph.FindEdge(this, port) != null;
        }

        public void Connect(NodePort port)
        {
            if (port == null)
            {
                Debug.LogWarning("Cannot connect to null port");
                return;
            }

            if (port == this)
            {
                Debug.LogWarning("Cannot connect port to self");
                return;
            }

            if (direction == port.direction)
            {
                Debug.LogWarning("Cannot connect two " + direction + " ports");
                return;
            }

            if (IsConnectedTo(port))
            {
                Debug.LogWarning("Ports '" + fieldName + "' and '" + port.fieldName + "' are already connected");
                return;
            }

            NodeGraph graph = Graph;
            NodeGraph otherGraph = port.node == null ? null : port.node.graph;
            if (graph == null || otherGraph == null)
            {
                Debug.LogWarning("Cannot connect ports of nodes that are not part of a graph");
                return;
            }

            if (graph != otherGraph)
            {
                Debug.LogWarning("Cannot connect ports across different graphs");
                return;
            }

#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(graph, "Connect Port");
            UnityEditor.Undo.RecordObject(node, "Connect Port");
            UnityEditor.Undo.RecordObject(port.node, "Connect Port");
#endif
            if (connectionType == Node.ConnectionType.Override && ConnectionCount > 0)
            {
                ClearConnections();
            }

            if (port.connectionType == Node.ConnectionType.Override && port.ConnectionCount > 0)
            {
                port.ClearConnections();
            }

            NodePort output = IsOutput ? this : port;
            NodePort input = IsOutput ? port : this;
            graph.AddEdge(output, input);
            output.node.OnCreateConnection(output, input);
            input.node.OnCreateConnection(output, input);
        }

        public void Disconnect(NodePort port)
        {
            if (port == null)
            {
                return;
            }

            NodeGraph graph = Graph;
            if (graph == null)
            {
                return;
            }

#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(graph, "Disconnect Port");
            UnityEditor.Undo.RecordObject(node, "Disconnect Port");
            if (port.node != null)
            {
                UnityEditor.Undo.RecordObject(port.node, "Disconnect Port");
            }
#endif
            if (!graph.RemoveEdge(this, port))
            {
                return;
            }

            if (node != null)
            {
                node.OnRemoveConnection(this);
            }

            if (port.node != null)
            {
                port.node.OnRemoveConnection(port);
            }
        }

        public void Disconnect(int i)
        {
            Disconnect(GetConnections()[i]);
        }

        public void ClearConnections()
        {
            List<NodePort> connections = GetConnections();
            foreach (NodePort connection in connections)
            {
                Disconnect(connection);
            }
        }

        public bool CanConnectTo(NodePort port)
        {
            if (port == null || port.direction == direction)
            {
                return false;
            }

            NodePort input = IsInput ? this : port;
            NodePort output = IsInput ? port : this;
            return CheckConstraint(input, output, input.typeConstraint)
                   && CheckConstraint(input, output, output.typeConstraint);
        }

        private static bool CheckConstraint(NodePort input, NodePort output, Node.TypeConstraint constraint)
        {
            if (constraint == Node.TypeConstraint.None)
            {
                return true;
            }

            Type inputType = input.ValueType;
            Type outputType = output.ValueType;
            if (inputType == null || outputType == null)
            {
                return false;
            }

            switch (constraint)
            {
                case Node.TypeConstraint.Inherited:
                    return inputType.IsAssignableFrom(outputType);
                case Node.TypeConstraint.Strict:
                    return inputType == outputType;
                case Node.TypeConstraint.InheritedInverse:
                    return outputType.IsAssignableFrom(inputType);
                case Node.TypeConstraint.InheritedAny:
                    return inputType.IsAssignableFrom(outputType) || outputType.IsAssignableFrom(inputType);
                default:
                    return false;
            }
        }

        public object GetOutputValue()
        {
            return direction == IO.Input || node == null ? null : node.GetValue(this);
        }

        public object GetInputValue()
        {
            NodePort connection = Connection;
            return connection?.GetOutputValue();
        }

        public object[] GetInputValues()
        {
            List<NodePort> connections = GetConnections();
            object[] values = new object[connections.Count];
            for (int i = 0; i < connections.Count; i++)
            {
                values[i] = connections[i].GetOutputValue();
            }

            return values;
        }

        public T GetInputValue<T>()
        {
            object value = GetInputValue();
            return value is T typed ? typed : default;
        }

        public T[] GetInputValues<T>()
        {
            object[] values = GetInputValues();
            T[] typed = new T[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] is T value)
                {
                    typed[i] = value;
                }
            }

            return typed;
        }

        public bool TryGetInputValue<T>(out T value)
        {
            object raw = GetInputValue();
            if (raw is T typed)
            {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }

        public float GetInputSum(float fallback)
        {
            return IsConnected ? GetInputValues<float>().Sum() : fallback;
        }

        public int GetInputSum(int fallback)
        {
            return IsConnected ? GetInputValues<int>().Sum() : fallback;
        }

        public List<Vector2> GetReroutePoints(int index)
        {
            NodeGraph graph = Graph;
            if (graph == null)
            {
                return null;
            }

            NodeEdge edge = graph.GetEdges(this).ElementAtOrDefault(index);
            return edge?.reroutePoints;
        }

        /// <summary>Swaps the connections of this port with those of <paramref name="targetPort"/>.</summary>
        public void SwapConnections(NodePort targetPort)
        {
            if (targetPort == null || Graph == null)
            {
                return;
            }

            Graph.SwapEdges(this, targetPort);
        }

        /// <summary>Copies all connections of <paramref name="targetPort"/> onto this port as well.</summary>
        public void AddConnections(NodePort targetPort)
        {
            if (targetPort == null)
            {
                return;
            }

            foreach (NodePort connection in targetPort.GetConnections())
            {
                Connect(connection);
            }
        }

        /// <summary>Moves all connections of this port onto <paramref name="targetPort"/>.</summary>
        public void MoveConnections(NodePort targetPort)
        {
            if (targetPort == null)
            {
                return;
            }

            List<NodePort> connections = GetConnections();
            ClearConnections();
            foreach (NodePort connection in connections)
            {
                targetPort.Connect(connection);
            }
        }

        /// <summary>Removes edges of this port whose counterpart no longer resolves.</summary>
        public void VerifyConnections()
        {
            Graph?.PruneInvalidEdges();
        }
    }
}
