using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SaintsGraph.Tests
{
    public class TestGraph : NodeGraph
    {
    }

    public class FloatNode : Node
    {
        [Output] public float value;

        public override object GetValue(NodePort port)
        {
            return value;
        }
    }

    public class AddNode : Node
    {
        [Input] public float a;
        [Input] public float b;
        [Output] public float result;

        public override object GetValue(NodePort port)
        {
            return GetInputValue("a", a) + GetInputValue("b", b);
        }
    }

    public class OverrideInputNode : Node
    {
        [Input(ShowBackingValue.Unconnected, ConnectionType.Override)]
        public float input;

        [Output] public float output;

        public override object GetValue(NodePort port)
        {
            return GetInputValue("input", 0f);
        }
    }

    public class CallbackNode : Node
    {
        [Input] public float input;
        [Output] public float output;

        public int createdCalls;
        public int removedCalls;
        public NodePort lastFrom;
        public NodePort lastTo;

        public override object GetValue(NodePort port)
        {
            return 0f;
        }

        public override void OnCreateConnection(NodePort from, NodePort to)
        {
            createdCalls++;
            lastFrom = from;
            lastTo = to;
        }

        public override void OnRemoveConnection(NodePort port)
        {
            removedCalls++;
        }
    }

    public class RuntimeCoreTests
    {
        private readonly List<Object> _created = new List<Object>();

        private TestGraph NewGraph()
        {
            TestGraph graph = ScriptableObject.CreateInstance<TestGraph>();
            _created.Add(graph);
            return graph;
        }

        private T NewNode<T>(NodeGraph graph) where T : Node
        {
            T node = graph.AddNode<T>();
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
        public void AddNode_SetsGraphAndRegistersNode()
        {
            TestGraph graph = NewGraph();
            FloatNode node = NewNode<FloatNode>(graph);

            Assert.AreSame(graph, node.graph);
            Assert.Contains(node, graph.nodes);
        }

        [Test]
        public void StaticPorts_AreBuiltFromAttributes()
        {
            TestGraph graph = NewGraph();
            AddNode node = NewNode<AddNode>(graph);

            NodePort a = node.GetInputPort("a");
            Assert.NotNull(a);
            Assert.IsTrue(a.IsInput);
            Assert.IsTrue(a.IsStatic);
            Assert.AreEqual(typeof(float), a.ValueType);
            Assert.NotNull(node.GetOutputPort("result"));
            Assert.IsNull(node.GetPort("missing"));
            Assert.AreEqual(2, node.Inputs.Count());
            Assert.AreEqual(1, node.Outputs.Count());
        }

        [Test]
        public void Connect_StoresSingleEdgeAtGraphLevel()
        {
            TestGraph graph = NewGraph();
            FloatNode source = NewNode<FloatNode>(graph);
            AddNode target = NewNode<AddNode>(graph);

            source.GetOutputPort("value").Connect(target.GetInputPort("a"));

            Assert.AreEqual(1, graph.Edges.Count);
            Assert.IsTrue(source.GetOutputPort("value").IsConnected);
            Assert.IsTrue(target.GetInputPort("a").IsConnected);
            Assert.AreSame(source, target.GetInputPort("a").Connection.node);
        }

        [Test]
        public void Connect_TwoPortsOfSameDirection_IsRejected()
        {
            TestGraph graph = NewGraph();
            AddNode node = NewNode<AddNode>(graph);

            node.GetInputPort("a").Connect(node.GetInputPort("b"));

            Assert.AreEqual(0, graph.Edges.Count);
        }

        [Test]
        public void Connect_FiresNormalizedCallbacksOnBothNodes()
        {
            TestGraph graph = NewGraph();
            CallbackNode source = NewNode<CallbackNode>(graph);
            CallbackNode target = NewNode<CallbackNode>(graph);

            // Connect from the INPUT side to prove arguments are normalized to (output, input).
            target.GetInputPort("input").Connect(source.GetOutputPort("output"));

            Assert.AreEqual(1, source.createdCalls);
            Assert.AreEqual(1, target.createdCalls);
            Assert.AreSame(source.GetOutputPort("output"), source.lastFrom);
            Assert.AreSame(target.GetInputPort("input"), source.lastTo);
            Assert.AreSame(source.GetOutputPort("output"), target.lastFrom);
            Assert.AreSame(target.GetInputPort("input"), target.lastTo);
        }

        [Test]
        public void OverridePort_ReplacesExistingConnection()
        {
            TestGraph graph = NewGraph();
            FloatNode first = NewNode<FloatNode>(graph);
            FloatNode second = NewNode<FloatNode>(graph);
            OverrideInputNode target = NewNode<OverrideInputNode>(graph);

            first.GetOutputPort("value").Connect(target.GetInputPort("input"));
            second.GetOutputPort("value").Connect(target.GetInputPort("input"));

            Assert.AreEqual(1, graph.Edges.Count);
            Assert.AreEqual(1, target.GetInputPort("input").ConnectionCount);
            Assert.AreSame(second, target.GetInputPort("input").Connection.node);
            Assert.IsFalse(first.GetOutputPort("value").IsConnected);
        }

        [Test]
        public void GetInputValue_PullsValuesThroughTheGraph()
        {
            TestGraph graph = NewGraph();
            FloatNode first = NewNode<FloatNode>(graph);
            FloatNode second = NewNode<FloatNode>(graph);
            AddNode add = NewNode<AddNode>(graph);
            first.value = 2f;
            second.value = 3f;

            first.GetOutputPort("value").Connect(add.GetInputPort("a"));
            second.GetOutputPort("value").Connect(add.GetInputPort("b"));

            Assert.AreEqual(5f, (float)add.GetValue(add.GetOutputPort("result")));
        }

        [Test]
        public void GetInputValue_FallsBackToBackingValueWhenUnconnected()
        {
            TestGraph graph = NewGraph();
            AddNode add = NewNode<AddNode>(graph);
            add.a = 10f;

            Assert.AreEqual(10f, (float)add.GetValue(add.GetOutputPort("result")));
            Assert.AreEqual(42f, add.GetInputValue("a", 42f));
        }

        [Test]
        public void Disconnect_RemovesEdgeAndFiresCallbacks()
        {
            TestGraph graph = NewGraph();
            CallbackNode source = NewNode<CallbackNode>(graph);
            CallbackNode target = NewNode<CallbackNode>(graph);
            source.GetOutputPort("output").Connect(target.GetInputPort("input"));

            source.GetOutputPort("output").Disconnect(target.GetInputPort("input"));

            Assert.AreEqual(0, graph.Edges.Count);
            Assert.AreEqual(1, source.removedCalls);
            Assert.AreEqual(1, target.removedCalls);
            Assert.IsFalse(source.GetOutputPort("output").IsConnected);
        }

        [Test]
        public void RemoveNode_RemovesItsEdges()
        {
            TestGraph graph = NewGraph();
            FloatNode source = NewNode<FloatNode>(graph);
            AddNode target = NewNode<AddNode>(graph);
            source.GetOutputPort("value").Connect(target.GetInputPort("a"));

            graph.RemoveNode(source);

            Assert.AreEqual(0, graph.Edges.Count);
            Assert.IsFalse(graph.nodes.Contains(source));
            Assert.IsFalse(target.GetInputPort("a").IsConnected);
        }

        [Test]
        public void Copy_RemapsEdgesOntoClonedNodes()
        {
            TestGraph graph = NewGraph();
            FloatNode source = NewNode<FloatNode>(graph);
            AddNode add = NewNode<AddNode>(graph);
            source.value = 7f;
            source.GetOutputPort("value").Connect(add.GetInputPort("a"));

            TestGraph copy = (TestGraph)graph.Copy();
            _created.Add(copy);
            foreach (Node node in copy.nodes)
            {
                _created.Add(node);
            }

            Assert.AreEqual(1, copy.Edges.Count);
            Assert.IsTrue(copy.nodes.Contains(copy.Edges[0].outputNode));
            Assert.IsFalse(graph.nodes.Contains(copy.Edges[0].outputNode));

            source.value = 100f;
            AddNode copiedAdd = copy.nodes.OfType<AddNode>().First();
            Assert.AreEqual(7f, (float)copiedAdd.GetValue(copiedAdd.GetOutputPort("result")));
        }

        [Test]
        public void DynamicPorts_SurviveUpdatePortsAndDisconnectOnRemoval()
        {
            TestGraph graph = NewGraph();
            FloatNode source = NewNode<FloatNode>(graph);
            AddNode node = NewNode<AddNode>(graph);

            NodePort dynamic = node.AddDynamicInput(typeof(float), fieldName: "dyn");
            Assert.IsTrue(dynamic.IsDynamic);
            source.GetOutputPort("value").Connect(dynamic);
            Assert.AreEqual(1, graph.Edges.Count);

            node.UpdatePorts();
            Assert.IsTrue(node.HasPort("dyn"));
            Assert.IsTrue(node.GetInputPort("dyn").IsConnected);

            node.RemoveDynamicPort("dyn");
            Assert.IsFalse(node.HasPort("dyn"));
            Assert.AreEqual(0, graph.Edges.Count);
        }

        [Test]
        public void AddDynamicOutput_GeneratesDirectionAwareNames()
        {
            TestGraph graph = NewGraph();
            AddNode node = NewNode<AddNode>(graph);

            NodePort output = node.AddDynamicOutput(typeof(float));
            NodePort input = node.AddDynamicInput(typeof(float));

            Assert.AreEqual("dynamicOutput_0", output.fieldName);
            Assert.AreEqual("dynamicInput_0", input.fieldName);
        }
    }
}
