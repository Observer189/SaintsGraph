using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaintsGraph.Editor;
using UnityEngine;

namespace SaintsGraph.Tests
{
    public class ListNode : Node
    {
        [Input(dynamicPortList = true)] public float[] items;
        [Output] public float total;

        public override object GetValue(NodePort port)
        {
            return 0f;
        }
    }

    public class DynamicPortListTests
    {
        private readonly List<Object> _created = new List<Object>();
        private TestGraph _graph;
        private ListNode _node;
        private PortCache.PortTemplate _template;

        [SetUp]
        public void SetUp()
        {
            _graph = ScriptableObject.CreateInstance<TestGraph>();
            _created.Add(_graph);
            _node = _graph.AddNode<ListNode>();
            _created.Add(_node);
            _template = PortCache.GetTemplates(typeof(ListNode)).First(t => t.dynamicPortList);
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

        private FloatNode Source(float value)
        {
            FloatNode node = _graph.AddNode<FloatNode>();
            node.value = value;
            _created.Add(node);
            return node;
        }

        [Test]
        public void BackingField_HasNoPortOfItsOwn()
        {
            Assert.IsNull(_node.GetPort("items"));
        }

        [Test]
        public void AddElement_CreatesSequentiallyNamedInputPorts()
        {
            DynamicPortListOps.AddElement(_node, _template);
            DynamicPortListOps.AddElement(_node, _template);

            Assert.AreEqual(2, DynamicPortListOps.CountElements(_node, "items"));
            NodePort port = _node.GetPort("items 1");
            Assert.NotNull(port);
            Assert.IsTrue(port.IsInput);
            Assert.IsTrue(port.IsDynamic);
            Assert.AreEqual(typeof(float), port.ValueType);
        }

        [Test]
        public void RemoveElement_ShiftsLaterConnectionsDown()
        {
            for (int i = 0; i < 3; i++)
            {
                DynamicPortListOps.AddElement(_node, _template);
            }

            FloatNode a = Source(1f);
            FloatNode b = Source(2f);
            FloatNode c = Source(3f);
            a.GetOutputPort("value").Connect(_node.GetPort("items 0"));
            b.GetOutputPort("value").Connect(_node.GetPort("items 1"));
            c.GetOutputPort("value").Connect(_node.GetPort("items 2"));

            DynamicPortListOps.RemoveElement(_node, "items", 0);

            Assert.AreEqual(2, DynamicPortListOps.CountElements(_node, "items"));
            Assert.AreSame(b, _node.GetPort("items 0").Connection.node);
            Assert.AreSame(c, _node.GetPort("items 1").Connection.node);
            Assert.IsFalse(a.GetOutputPort("value").IsConnected);
        }

        [Test]
        public void MoveElement_ConnectionsFollowElements()
        {
            DynamicPortListOps.AddElement(_node, _template);
            DynamicPortListOps.AddElement(_node, _template);
            FloatNode a = Source(1f);
            FloatNode b = Source(2f);
            a.GetOutputPort("value").Connect(_node.GetPort("items 0"));
            b.GetOutputPort("value").Connect(_node.GetPort("items 1"));

            DynamicPortListOps.MoveElement(_node, "items", 0, 1);

            Assert.AreSame(b, _node.GetPort("items 0").Connection.node);
            Assert.AreSame(a, _node.GetPort("items 1").Connection.node);
        }

        [Test]
        public void Sync_AlignsPortCountWithBackingSize()
        {
            Assert.IsTrue(DynamicPortListOps.Sync(_node, _template, 3));
            Assert.AreEqual(3, DynamicPortListOps.CountElements(_node, "items"));

            FloatNode a = Source(1f);
            a.GetOutputPort("value").Connect(_node.GetPort("items 2"));

            Assert.IsTrue(DynamicPortListOps.Sync(_node, _template, 1));
            Assert.AreEqual(1, DynamicPortListOps.CountElements(_node, "items"));
            Assert.IsFalse(a.GetOutputPort("value").IsConnected);
            Assert.AreEqual(0, _graph.Edges.Count);
        }

        [Test]
        public void UpdatePorts_RefreshesElementMetadataFromAttribute()
        {
            // Simulate a stale serialized element port with wrong type/constraints.
            _node.AddDynamicInput(typeof(string), Node.ConnectionType.Multiple, Node.TypeConstraint.None, "items 0");
            _node.UpdatePorts();

            NodePort port = _node.GetPort("items 0");
            Assert.NotNull(port);
            Assert.AreEqual(typeof(float), port.ValueType);
            Assert.IsTrue(port.IsInput);
        }
    }
}
