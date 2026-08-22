using NUnit.Framework;
using SaintsGraph.Editor;
using UnityEngine;

namespace SaintsGraph.Tests
{
    public class AnnotationTests
    {
        private TestGraph _graph;
        private FloatNode _float;
        private AddNode _add;

        [SetUp]
        public void SetUp()
        {
            _graph = ScriptableObject.CreateInstance<TestGraph>();
            _float = _graph.AddNode<FloatNode>();
            _float.name = "Float";
            _add = _graph.AddNode<AddNode>();
            _add.name = "Add";
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Node node in _graph.nodes.ToArray())
            {
                if (node != null)
                {
                    Object.DestroyImmediate(node);
                }
            }

            Object.DestroyImmediate(_graph);
        }

        [Test]
        public void RemoveNode_TakesItOutOfItsGroups()
        {
            _graph.Groups.Add(new NodeGroup { title = "Inputs", nodes = { _float, _add } });

            _graph.RemoveNode(_float);

            Assert.AreEqual(1, _graph.Groups[0].nodes.Count, "a deleted node cannot stay in a group");
            Assert.AreSame(_add, _graph.Groups[0].nodes[0]);
        }

        [Test]
        public void RoundTrip_KeepsGroupsNotesAndCollapseState()
        {
            _graph.Groups.Add(new NodeGroup
            {
                title = "Inputs",
                position = new Vector2(10f, 20f),
                nodes = { _float }
            });
            _graph.Notes.Add(new NodeNote
            {
                title = "Why",
                text = "This half feeds the discriminant.",
                area = new Rect(5f, 6f, 210f, 170f),
                theme = 1,
                fontSize = 2
            });
            _add.collapsed = true;

            string json = GraphJson.Export(_graph);

            _graph.Groups.Clear();
            _graph.Notes.Clear();
            _add.collapsed = false;

            GraphJson.Import(_graph, json);

            Assert.AreEqual(1, _graph.Groups.Count, "group restored");
            Assert.AreEqual("Inputs", _graph.Groups[0].title);
            Assert.AreEqual(new Vector2(10f, 20f), _graph.Groups[0].position);
            Assert.AreEqual(1, _graph.Groups[0].nodes.Count, "membership restored");
            Assert.AreSame(_float, _graph.Groups[0].nodes[0], "membership points at the same node");

            Assert.AreEqual(1, _graph.Notes.Count, "note restored");
            Assert.AreEqual("Why", _graph.Notes[0].title);
            Assert.AreEqual("This half feeds the discriminant.", _graph.Notes[0].text);
            Assert.AreEqual(new Rect(5f, 6f, 210f, 170f), _graph.Notes[0].area);
            Assert.AreEqual(1, _graph.Notes[0].theme);
            Assert.AreEqual(2, _graph.Notes[0].fontSize);

            Assert.IsTrue(_add.collapsed, "collapse state travels with the node");
            Assert.IsFalse(_float.collapsed);
        }

        [Test]
        public void Export_OmitsEmptyAnnotationSections()
        {
            string json = GraphJson.Export(_graph);

            StringAssert.DoesNotContain("\"groups\"", json, "no groups, no noise");
            StringAssert.DoesNotContain("\"notes\"", json);
            StringAssert.DoesNotContain("\"collapsed\"", json, "collapsed is only written when true");
        }
    }
}
