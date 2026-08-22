using NUnit.Framework;
using SaintsGraph.Editor;
using UnityEditor;
using UnityEngine;

namespace SaintsGraph.Tests
{
    public class GraphJsonTests
    {
        private const string TempDir = "Assets/SaintsGraphJsonTests_Temp";

        private string _assetPath;
        private TestGraph _graph;
        private FloatNode _float;
        private AddNode _add;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
            {
                AssetDatabase.CreateFolder("Assets", "SaintsGraphJsonTests_Temp");
            }

            _assetPath = TempDir + "/JsonTestGraph.asset";
            _graph = ScriptableObject.CreateInstance<TestGraph>();
            AssetDatabase.CreateAsset(_graph, _assetPath);

            _float = _graph.AddNode<FloatNode>();
            _float.name = "Float";
            _float.value = 2f;
            AssetDatabase.AddObjectToAsset(_float, _graph);

            _add = _graph.AddNode<AddNode>();
            _add.name = "Add";
            _add.a = 1f;
            AssetDatabase.AddObjectToAsset(_add, _graph);

            _float.GetOutputPort("value").Connect(_add.GetInputPort("a"));
            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_assetPath);
            AssetDatabase.DeleteAsset(TempDir);
        }

        [Test]
        public void ExportImport_RestoresValuesAndEdges()
        {
            string json = GraphJson.Export(_graph);
            StringAssert.Contains("\"Float\"", json);

            _float.value = 999f;
            _float.GetOutputPort("value").ClearConnections();
            Assert.AreEqual(0, _graph.Edges.Count);

            GraphJson.Import(_graph, json);

            Assert.AreEqual(2f, _float.value);
            Assert.AreEqual(1, _graph.Edges.Count);
            Assert.IsTrue(_float.GetOutputPort("value").IsConnectedTo(_add.GetInputPort("a")));
        }

        [Test]
        public void Import_RecreatesDeletedNodeWithValuesAndEdge()
        {
            string json = GraphJson.Export(_graph);

            _graph.RemoveNode(_float);
            Object.DestroyImmediate(_float, true);
            AssetDatabase.SaveAssets();
            Assert.AreEqual(1, _graph.nodes.Count);

            GraphJson.Import(_graph, json);

            Assert.AreEqual(2, _graph.nodes.Count);
            Node recreated = _graph.nodes.Find(node => node != null && node.name == "Float");
            Assert.NotNull(recreated);
            Assert.AreEqual(2f, ((FloatNode)recreated).value);
            Assert.AreEqual(1, _graph.Edges.Count);
            Assert.IsTrue(recreated.GetOutputPort("value").IsConnectedTo(_add.GetInputPort("a")));
        }

        [Test]
        public void Import_AppliesRenameFromNameField()
        {
            string json = GraphJson.Export(_graph);
            json = json.Replace("\"name\": \"Add\"", "\"name\": \"Sum\"");

            GraphJson.Import(_graph, json);

            Assert.AreEqual("Sum", _add.name);
        }

        [Test]
        public void Paste_AddsIndependentCopiesKeepingInternalEdges()
        {
            string clipboard = GraphJson.Export(_graph, new Node[] { _float, _add });

            System.Collections.Generic.List<Node> created =
                GraphJson.Paste(_graph, clipboard, new Vector2(50f, 50f));

            Assert.AreEqual(2, created.Count, "two nodes pasted");
            Assert.AreEqual(4, _graph.nodes.Count, "originals kept, copies added");
            Assert.AreEqual(2, _graph.Edges.Count, "the internal edge came along");

            foreach (Node node in created)
            {
                Assert.AreNotEqual("Float", node.name);
                Assert.AreNotEqual("Add", node.name);
            }

            Node pastedFloat = created.Find(node => node is FloatNode);
            Node pastedAdd = created.Find(node => node is AddNode);
            Assert.AreEqual(2f, ((FloatNode)pastedFloat).value, "field values copied");
            Assert.IsTrue(pastedFloat.GetOutputPort("value").IsConnectedTo(pastedAdd.GetInputPort("a")),
                "pasted edge connects the copies, not the originals");
            Assert.IsTrue(_float.GetOutputPort("value").IsConnectedTo(_add.GetInputPort("a")),
                "original edge untouched");
        }
    }
}
