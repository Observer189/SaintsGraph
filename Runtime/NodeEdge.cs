using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaintsGraph
{
    /// <summary>
    /// One connection between an output port and an input port.
    /// Unlike xNode, where every connection is stored twice (once per endpoint),
    /// edges are stored exactly once, in <see cref="NodeGraph"/>.
    /// </summary>
    [Serializable]
    public class NodeEdge
    {
        public Node outputNode;
        public string outputField;
        public Node inputNode;
        public string inputField;
        public List<Vector2> reroutePoints = new List<Vector2>();

        public NodePort OutputPort => outputNode == null ? null : outputNode.GetPort(outputField);
        public NodePort InputPort => inputNode == null ? null : inputNode.GetPort(inputField);

        internal bool MatchesOutput(Node node, string field)
        {
            return ReferenceEquals(outputNode, node) && outputField == field;
        }

        internal bool MatchesInput(Node node, string field)
        {
            return ReferenceEquals(inputNode, node) && inputField == field;
        }

        internal bool Matches(NodePort port)
        {
            return port.IsOutput
                ? MatchesOutput(port.node, port.fieldName)
                : MatchesInput(port.node, port.fieldName);
        }

        internal NodePort GetOtherPort(NodePort port)
        {
            return port.IsOutput ? InputPort : OutputPort;
        }
    }
}
