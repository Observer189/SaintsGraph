using NUnit.Framework;
using SaintsGraph.Editor;

namespace SaintsGraph.Tests
{
    public class SchemaTests
    {
        private static JsonObject FindNodeType(JsonObject root, string typeName)
        {
            JsonArray nodeTypes = (JsonArray)root["nodeTypes"];
            foreach (JsonValue value in nodeTypes.Items)
            {
                JsonObject entry = (JsonObject)value;
                string type = entry.GetString("type");
                if (type != null && type.StartsWith(typeName + ","))
                {
                    return entry;
                }
            }

            return null;
        }

        [TearDown]
        public void TearDown()
        {
            GraphSchema.IncludeTestAssemblies = false;
        }

        [Test]
        public void Export_HasFormatAndInstructions()
        {
            JsonObject root = (JsonObject)JsonValue.Parse(GraphSchema.Export());

            Assert.AreEqual("saintsgraph-schema/1", root.GetString("format"));
            Assert.IsNotNull(root["howTo"], "schema carries usage instructions");
            Assert.IsNotNull(root["graphTypes"], "schema lists graph types");
            Assert.IsNotNull(root["nodeTypes"], "schema lists node types");
        }

        [Test]
        public void Export_LeavesOutTestFixtureTypes()
        {
            JsonObject root = (JsonObject)JsonValue.Parse(GraphSchema.Export());

            // These node types live in this very assembly, which references NUnit: they are
            // fixtures, not content a generator should ever be offered.
            Assert.IsNull(FindNodeType(root, "SaintsGraph.Tests.AddNode"));
            Assert.IsNull(FindNodeType(root, "SaintsGraph.Tests.RichNode"));

            JsonArray graphTypes = (JsonArray)root["graphTypes"];
            foreach (JsonValue value in graphTypes.Items)
            {
                string type = ((JsonObject)value).GetString("type");
                Assert.IsFalse(type != null && type.Contains("SaintsGraph.Tests."),
                    "test graph types are excluded too");
            }
        }

        [Test]
        public void Export_DescribesPortsAndDefaults()
        {
            GraphSchema.IncludeTestAssemblies = true;
            JsonObject addNode = FindNodeType((JsonObject)JsonValue.Parse(GraphSchema.Export()),
                "SaintsGraph.Tests.AddNode");
            Assert.IsNotNull(addNode, "node types are listed by assembly-qualified name");

            JsonArray ports = (JsonArray)addNode["ports"];
            Assert.AreEqual(3, ports.Items.Count, "two inputs and one output");
            foreach (JsonValue value in ports.Items)
            {
                JsonObject port = (JsonObject)value;
                if (port.GetString("name") == "a")
                {
                    Assert.AreEqual("input", port.GetString("direction"));
                    Assert.AreEqual("System.Single", port.GetString("valueType"));
                }
            }

            JsonObject defaults = (JsonObject)addNode["defaults"];
            Assert.IsNotNull(defaults["a"], "user fields are present in defaults");
            Assert.IsNull(defaults["m_Name"], "engine bookkeeping is not");
            Assert.IsNull(defaults["uid"], "identity is not a user field");
        }

        [Test]
        public void Export_MarksDynamicPortListsWithElementType()
        {
            GraphSchema.IncludeTestAssemblies = true;
            JsonObject listNode = FindNodeType((JsonObject)JsonValue.Parse(GraphSchema.Export()),
                "SaintsGraph.Tests.ListNode");
            Assert.IsNotNull(listNode);

            JsonObject itemsPort = null;
            foreach (JsonValue value in ((JsonArray)listNode["ports"]).Items)
            {
                JsonObject port = (JsonObject)value;
                if (port.GetString("name") == "items")
                {
                    itemsPort = port;
                }
            }

            Assert.IsNotNull(itemsPort, "dynamic port list fields are described");
            Assert.IsInstanceOf<JsonBool>(itemsPort["dynamicList"]);
            Assert.AreEqual("System.Single", itemsPort.GetString("valueType"),
                "element type, not the array type");
        }

        [Test]
        public void Export_ReplacesManagedReferenceIdsWithAssignableTypes()
        {
            GraphSchema.IncludeTestAssemblies = true;
            JsonObject richNode = FindNodeType((JsonObject)JsonValue.Parse(GraphSchema.Export()),
                "SaintsGraph.Tests.RichNode");
            Assert.IsNotNull(richNode);

            JsonObject defaults = (JsonObject)richNode["defaults"];
            Assert.IsNull(defaults["references"], "document-scoped reference ids are not defaults");
            Assert.IsInstanceOf<JsonNull>(defaults["payload"], "a rid default would be a trap to copy");

            JsonArray managed = (JsonArray)richNode["managedReferences"];
            Assert.IsNotNull(managed, "polymorphic fields are described instead");

            JsonObject payloadField = null;
            foreach (JsonValue value in managed.Items)
            {
                JsonObject entry = (JsonObject)value;
                if (entry.GetString("field") == "payload")
                {
                    payloadField = entry;
                }
            }

            Assert.IsNotNull(payloadField);
            Assert.AreEqual("SaintsGraph.Tests.ITestPayload", payloadField.GetString("declaredType"));

            JsonArray assignable = (JsonArray)payloadField["assignableTypes"];
            bool hasNumberPayload = false;
            foreach (JsonValue value in assignable.Items)
            {
                if (((JsonString)value).Value.StartsWith("SaintsGraph.Tests.NumberPayload,"))
                {
                    hasNumberPayload = true;
                }
            }

            Assert.IsTrue(hasNumberPayload, "concrete implementations are listed for the generator");
        }
    }
}
