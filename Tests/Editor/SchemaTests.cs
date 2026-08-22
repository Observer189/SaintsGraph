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

        [Test]
        public void Export_DescribesNodeTypesWithPortsAndDefaults()
        {
            JsonObject root = (JsonObject)JsonValue.Parse(GraphSchema.Export());

            Assert.AreEqual("saintsgraph-schema/1", root.GetString("format"));
            Assert.IsNotNull(root["howTo"], "schema carries usage instructions");
            Assert.IsNotNull(root["graphTypes"], "schema lists graph types");

            JsonObject addNode = FindNodeType(root, "SaintsGraph.Tests.AddNode");
            Assert.IsNotNull(addNode, "the schema lists node types by assembly-qualified name");

            JsonArray ports = (JsonArray)addNode["ports"];
            Assert.AreEqual(3, ports.Items.Count, "two inputs and one output");

            bool foundInput = false;
            bool foundOutput = false;
            foreach (JsonValue value in ports.Items)
            {
                JsonObject port = (JsonObject)value;
                if (port.GetString("name") == "a")
                {
                    foundInput = true;
                    Assert.AreEqual("input", port.GetString("direction"));
                    Assert.AreEqual("System.Single", port.GetString("valueType"));
                }

                if (port.GetString("name") == "result")
                {
                    foundOutput = true;
                    Assert.AreEqual("output", port.GetString("direction"));
                }
            }

            Assert.IsTrue(foundInput, "input port described");
            Assert.IsTrue(foundOutput, "output port described");

            JsonObject defaults = (JsonObject)addNode["defaults"];
            Assert.IsNotNull(defaults, "defaults let a generator copy a valid fields block");
            Assert.IsNotNull(defaults["a"], "user fields are present in defaults");
            Assert.IsNull(defaults["m_Name"], "engine bookkeeping is not");
            Assert.IsNull(defaults["uid"], "identity is not a user field");
        }

        [Test]
        public void Export_MarksDynamicPortLists()
        {
            JsonObject root = (JsonObject)JsonValue.Parse(GraphSchema.Export());
            JsonObject listNode = FindNodeType(root, "SaintsGraph.Tests.ListNode");
            Assert.IsNotNull(listNode);

            JsonArray ports = (JsonArray)listNode["ports"];
            JsonObject itemsPort = null;
            foreach (JsonValue value in ports.Items)
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
    }
}
