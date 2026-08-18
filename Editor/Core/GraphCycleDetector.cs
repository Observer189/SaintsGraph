using System;
using System.Collections.Generic;

namespace SaintsGraph.Editor
{
    /// <summary>
    /// Finds nodes participating in at least one cycle (Tarjan strongly connected
    /// components; self-loops count). The runtime deliberately allows cycles for xNode
    /// parity, but pull evaluation on one recurses forever — so the editor warns.
    /// </summary>
    internal static class GraphCycleDetector
    {
        public static HashSet<Node> FindCycleNodes(NodeGraph graph)
        {
            Dictionary<Node, List<Node>> adjacency = new Dictionary<Node, List<Node>>();
            HashSet<Node> result = new HashSet<Node>();
            foreach (NodeEdge edge in graph.Edges)
            {
                if (edge.outputNode == null || edge.inputNode == null)
                {
                    continue;
                }

                if (ReferenceEquals(edge.outputNode, edge.inputNode))
                {
                    result.Add(edge.outputNode);
                    continue;
                }

                if (!adjacency.TryGetValue(edge.outputNode, out List<Node> list))
                {
                    adjacency[edge.outputNode] = list = new List<Node>();
                }

                list.Add(edge.inputNode);
            }

            Dictionary<Node, int> index = new Dictionary<Node, int>();
            Dictionary<Node, int> lowLink = new Dictionary<Node, int>();
            HashSet<Node> onStack = new HashSet<Node>();
            Stack<Node> stack = new Stack<Node>();
            int nextIndex = 0;

            void StrongConnect(Node v)
            {
                index[v] = lowLink[v] = nextIndex++;
                stack.Push(v);
                onStack.Add(v);

                if (adjacency.TryGetValue(v, out List<Node> neighbors))
                {
                    foreach (Node w in neighbors)
                    {
                        if (!index.ContainsKey(w))
                        {
                            StrongConnect(w);
                            lowLink[v] = Math.Min(lowLink[v], lowLink[w]);
                        }
                        else if (onStack.Contains(w))
                        {
                            lowLink[v] = Math.Min(lowLink[v], index[w]);
                        }
                    }
                }

                if (lowLink[v] == index[v])
                {
                    List<Node> component = new List<Node>();
                    Node w;
                    do
                    {
                        w = stack.Pop();
                        onStack.Remove(w);
                        component.Add(w);
                    } while (!ReferenceEquals(w, v));

                    if (component.Count > 1)
                    {
                        foreach (Node member in component)
                        {
                            result.Add(member);
                        }
                    }
                }
            }

            foreach (Node node in graph.nodes)
            {
                if (node != null && !index.ContainsKey(node))
                {
                    StrongConnect(node);
                }
            }

            return result;
        }
    }
}
