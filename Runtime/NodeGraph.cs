using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaintsGraph
{
    /// <summary>
    /// Base class for node graph assets. Owns the node list and, unlike xNode,
    /// also the single authoritative list of edges.
    /// </summary>
    public abstract class NodeGraph : ScriptableObject
    {
        /// <summary>Declares node types this graph must always contain. Applies to the graph class.</summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
        public class RequireNodeAttribute : Attribute
        {
            public Type type0;
            public Type type1;
            public Type type2;

            public RequireNodeAttribute(Type type)
            {
                type0 = type;
            }

            public RequireNodeAttribute(Type type, Type type2)
            {
                type0 = type;
                type1 = type2;
            }

            public RequireNodeAttribute(Type type, Type type2, Type type3)
            {
                type0 = type;
                type1 = type2;
                this.type2 = type3;
            }

            public bool Requires(Type type)
            {
                return type != null && (type == type0 || type == type1 || type == type2);
            }
        }

        public List<Node> nodes = new List<Node>();

        [SerializeField] private List<NodeEdge> edges = new List<NodeEdge>();

        /// <summary>All connections of this graph. Single source of truth.</summary>
        public IReadOnlyList<NodeEdge> Edges => edges;

        public T AddNode<T>() where T : Node
        {
            return (T)AddNode(typeof(T));
        }

        public virtual Node AddNode(Type type)
        {
            Node.graphHotfix = this;
            Node node = (Node)CreateInstance(type);
            node.graph = this;
            nodes.Add(node);
            return node;
        }

        /// <summary>Creates a copy of the given node inside this graph. The copy has no connections.</summary>
        public virtual Node CopyNode(Node original)
        {
            Node.graphHotfix = this;
            Node node = Instantiate(original);
            node.graph = this;
            node.UpdatePorts();
            nodes.Add(node);
            return node;
        }

        /// <summary>Removes the node and all its edges. The node object is destroyed only in play mode, mirroring xNode.</summary>
        public virtual void RemoveNode(Node node)
        {
            node.ClearConnections();
            RemoveEdges(node);
            nodes.Remove(node);
            if (Application.isPlaying)
            {
                Destroy(node);
            }
        }

        public virtual void Clear()
        {
            if (Application.isPlaying)
            {
                foreach (Node node in nodes)
                {
                    if (node != null)
                    {
                        Destroy(node);
                    }
                }
            }

            nodes.Clear();
            edges.Clear();
        }

        /// <summary>Creates a deep runtime copy of this graph: nodes are cloned and edges remapped onto the clones.</summary>
        public virtual NodeGraph Copy()
        {
            NodeGraph graph = Instantiate(this);
            Dictionary<Node, Node> cloneByOriginal = new Dictionary<Node, Node>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] == null)
                {
                    continue;
                }

                Node.graphHotfix = graph;
                Node clone = Instantiate(nodes[i]);
                clone.name = nodes[i].name;
                clone.graph = graph;
                graph.nodes[i] = clone;
                cloneByOriginal[nodes[i]] = clone;
            }

            foreach (NodeEdge edge in graph.edges)
            {
                if (edge.outputNode != null && cloneByOriginal.TryGetValue(edge.outputNode, out Node newOutput))
                {
                    edge.outputNode = newOutput;
                }

                if (edge.inputNode != null && cloneByOriginal.TryGetValue(edge.inputNode, out Node newInput))
                {
                    edge.inputNode = newInput;
                }
            }

            HashSet<Node> clones = new HashSet<Node>(cloneByOriginal.Values);
            graph.edges.RemoveAll(edge =>
                edge.outputNode == null || edge.inputNode == null ||
                !clones.Contains(edge.outputNode) || !clones.Contains(edge.inputNode));
            return graph;
        }

        protected virtual void OnDestroy()
        {
            Clear();
        }

        internal IEnumerable<NodeEdge> GetEdges(NodePort port)
        {
            foreach (NodeEdge edge in edges)
            {
                if (edge.Matches(port))
                {
                    yield return edge;
                }
            }
        }

        internal NodeEdge FindEdge(NodePort a, NodePort b)
        {
            if (a == null || b == null || a.direction == b.direction)
            {
                return null;
            }

            NodePort output = a.IsOutput ? a : b;
            NodePort input = a.IsOutput ? b : a;
            return edges.Find(edge =>
                edge.MatchesOutput(output.node, output.fieldName) && edge.MatchesInput(input.node, input.fieldName));
        }

        internal NodeEdge AddEdge(NodePort output, NodePort input)
        {
            NodeEdge edge = new NodeEdge
            {
                outputNode = output.node,
                outputField = output.fieldName,
                inputNode = input.node,
                inputField = input.fieldName
            };
            edges.Add(edge);
            return edge;
        }

        internal bool RemoveEdge(NodePort a, NodePort b)
        {
            NodeEdge edge = FindEdge(a, b);
            return edge != null && edges.Remove(edge);
        }

        internal void RemoveEdges(Node node)
        {
            edges.RemoveAll(edge => ReferenceEquals(edge.outputNode, node) || ReferenceEquals(edge.inputNode, node));
        }

        internal void SwapEdges(NodePort a, NodePort b)
        {
            foreach (NodeEdge edge in edges)
            {
                if (edge.Matches(a))
                {
                    Retarget(edge, a, b);
                }
                else if (edge.Matches(b))
                {
                    Retarget(edge, b, a);
                }
            }
        }

        private static void Retarget(NodeEdge edge, NodePort from, NodePort to)
        {
            if (from.IsOutput)
            {
                edge.outputNode = to.node;
                edge.outputField = to.fieldName;
            }
            else
            {
                edge.inputNode = to.node;
                edge.inputField = to.fieldName;
            }
        }

        /// <summary>Removes edges whose endpoints no longer resolve to valid ports of nodes in this graph.</summary>
        public void PruneInvalidEdges()
        {
            edges.RemoveAll(edge =>
            {
                if (edge.outputNode == null || edge.inputNode == null)
                {
                    return true;
                }

                if (!nodes.Contains(edge.outputNode) || !nodes.Contains(edge.inputNode))
                {
                    return true;
                }

                NodePort output = edge.OutputPort;
                NodePort input = edge.InputPort;
                return output == null || input == null || !output.IsOutput || !input.IsInput;
            });
        }
    }
}
