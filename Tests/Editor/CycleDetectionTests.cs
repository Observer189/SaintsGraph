using System.Collections.Generic;
using NUnit.Framework;
using SaintsGraph.Editor;
using UnityEngine;

namespace SaintsGraph.Tests
{
    public class CycleDetectionTests
    {
        private readonly List<Object> _created = new List<Object>();

        private TestGraph NewGraph()
        {
            TestGraph graph = ScriptableObject.CreateInstance<TestGraph>();
            _created.Add(graph);
            return graph;
        }

        private CallbackNode NewNode(NodeGraph graph)
        {
            CallbackNode node = graph.AddNode<CallbackNode>();
            _created.Add(node);
            return node;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null)
                {
                    Object.DestroyImmediate(obj);
                }
            }

            _created.Clear();
        }

        [Test]
        public void Chain_HasNoCycle()
        {
            TestGraph graph = NewGraph();
            CallbackNode a = NewNode(graph);
            CallbackNode b = NewNode(graph);
            CallbackNode c = NewNode(graph);
            a.GetOutputPort("output").Connect(b.GetInputPort("input"));
            b.GetOutputPort("output").Connect(c.GetInputPort("input"));

            Assert.IsEmpty(GraphCycleDetector.FindCycleNodes(graph));
        }

        [Test]
        public void Loop_FlagsExactlyTheNodesInTheCycle()
        {
            TestGraph graph = NewGraph();
            CallbackNode a = NewNode(graph);
            CallbackNode b = NewNode(graph);
            CallbackNode standalone = NewNode(graph);
            a.GetOutputPort("output").Connect(b.GetInputPort("input"));
            b.GetOutputPort("output").Connect(a.GetInputPort("input"));

            HashSet<Node> cycle = GraphCycleDetector.FindCycleNodes(graph);

            Assert.AreEqual(2, cycle.Count);
            Assert.IsTrue(cycle.Contains(a));
            Assert.IsTrue(cycle.Contains(b));
            Assert.IsFalse(cycle.Contains(standalone));
        }

        [Test]
        public void SelfLoop_IsFlagged()
        {
            TestGraph graph = NewGraph();
            CallbackNode node = NewNode(graph);
            node.GetOutputPort("output").Connect(node.GetInputPort("input"));

            HashSet<Node> cycle = GraphCycleDetector.FindCycleNodes(graph);

            Assert.AreEqual(1, cycle.Count);
            Assert.IsTrue(cycle.Contains(node));
        }
    }
}
