using System;
using System.Collections.Generic;
using NUnit.Framework;
using SaintsGraph.Editor;
using UnityEditor;
using UnityEngine;

namespace SaintsGraph.Tests
{
    public interface ITestPayload
    {
    }

    [Serializable]
    public class NumberPayload : ITestPayload
    {
        public float number = 1.5f;
    }

    [Serializable]
    public class TextPayload : ITestPayload
    {
        public string text = "payload-text";
    }

    [Serializable]
    public class NestedData
    {
        public int count = 3;
        public string label = "nested";
    }

    /// <summary>Covers the serialization shapes the sidecar has to survive.</summary>
    public class RichNode : Node
    {
        [Input] public float input;
        [Output] public float output;

        public int intValue = 7;
        public Color color = Color.red;
        public NestedData nested = new NestedData();
        public List<NestedData> nestedList = new List<NestedData> { new NestedData() };
        public ScriptableObject assetReference;

        [SerializeReference] public ITestPayload payload = new NumberPayload();
        [SerializeReference] public List<ITestPayload> payloads = new List<ITestPayload> { new TextPayload() };

        public override object GetValue(NodePort port)
        {
            return 0f;
        }
    }

    public class SidecarFieldTests
    {
        private const string Folder = "Assets/SaintsGraphSidecarTests_Temp";
        private const string GraphPath = Folder + "/SidecarGraph.asset";
        private const string TargetPath = Folder + "/RefTarget.asset";

        private TestGraph _graph;
        private RichNode _node;
        private ScriptableObject _refTarget;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets", "SaintsGraphSidecarTests_Temp");
            }

            _refTarget = ScriptableObject.CreateInstance<TestGraph>();
            AssetDatabase.CreateAsset(_refTarget, TargetPath);

            _graph = ScriptableObject.CreateInstance<TestGraph>();
            AssetDatabase.CreateAsset(_graph, GraphPath);
            _node = _graph.AddNode<RichNode>();
            _node.name = "Rich";
            _node.assetReference = _refTarget;
            AssetDatabase.AddObjectToAsset(_node, _graph);
            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(GraphPath);
            AssetDatabase.DeleteAsset(TargetPath);
            AssetDatabase.DeleteAsset(Folder);
        }

        private static JsonObject FieldsOf(string json, int nodeIndex = 0)
        {
            JsonObject root = (JsonObject)JsonValue.Parse(json);
            JsonArray nodes = (JsonArray)root["nodes"];
            return (JsonObject)((JsonObject)nodes.Items[nodeIndex])["fields"];
        }

        [Test]
        public void Export_UnwrapsUnityPayloadAndDropsInternalFields()
        {
            JsonObject fields = FieldsOf(GraphJson.Export(_graph));

            // Unity nests everything under a "MonoBehaviour" key; the sidecar must be flat.
            Assert.IsNull(fields["MonoBehaviour"], "payload should be unwrapped");
            Assert.IsNotNull(fields["intValue"], "user fields should be at the top level");

            foreach (string internalField in new[]
                     { "m_Name", "m_EditorClassIdentifier", "m_Enabled", "graph", "position", "dynamicPorts" })
            {
                Assert.IsNull(fields[internalField], internalField + " should not leak into fields");
            }
        }

        [Test]
        public void RoundTrip_RestoresSerializeReferenceFields()
        {
            string json = GraphJson.Export(_graph);

            _node.payload = new TextPayload { text = "clobbered" };
            _node.payloads = new List<ITestPayload>();

            GraphJson.Import(_graph, json);

            Assert.IsInstanceOf<NumberPayload>(_node.payload, "managed reference type");
            Assert.AreEqual(1.5f, ((NumberPayload)_node.payload).number, "managed reference value");
            Assert.AreEqual(1, _node.payloads.Count, "managed reference list count");
            Assert.IsInstanceOf<TextPayload>(_node.payloads[0], "managed reference list element type");
        }

        [Test]
        public void RoundTrip_RestoresAssetReferenceAndNestedData()
        {
            string json = GraphJson.Export(_graph);

            _node.assetReference = null;
            _node.nested = new NestedData { count = 99, label = "clobbered" };
            _node.nestedList.Clear();
            _node.color = Color.green;

            GraphJson.Import(_graph, json);

            Assert.AreSame(_refTarget, _node.assetReference, "asset reference");
            Assert.AreEqual(3, _node.nested.count, "nested value");
            Assert.AreEqual("nested", _node.nested.label, "nested string");
            Assert.AreEqual(1, _node.nestedList.Count, "nested list");
            Assert.AreEqual(Color.red, _node.color, "color");
        }

        [Test]
        public void RoundTrip_RestoresDynamicPortsAndTheirConnections()
        {
            FloatNode source = _graph.AddNode<FloatNode>();
            source.name = "Source";
            AssetDatabase.AddObjectToAsset(source, _graph);
            NodePort dynamicPort = _node.AddDynamicInput(typeof(float), fieldName: "extra");
            source.GetOutputPort("value").Connect(dynamicPort);

            string json = GraphJson.Export(_graph);
            StringAssert.Contains("dynamicPorts", json, "dynamic ports must be exported");

            _node.RemoveDynamicPort("extra");
            Assert.IsFalse(_node.HasPort("extra"));

            GraphJson.Import(_graph, json);

            Assert.IsTrue(_node.HasPort("extra"), "dynamic port restored");
            Assert.AreEqual(typeof(float), _node.GetPort("extra").ValueType, "dynamic port type restored");
            Assert.IsTrue(_node.GetPort("extra").IsConnected, "dynamic port connection restored");
        }

        [Test]
        public void Import_KeepsNodeOwnedByItsOwnGraph()
        {
            string json = GraphJson.Export(_graph);

            GraphJson.Import(_graph, json);

            Assert.AreSame(_graph, _node.graph, "import must not repoint the graph reference");
        }
    }
}
