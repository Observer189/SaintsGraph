using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using XNodeGraph = XNode.NodeGraph;
using XNodeNode = XNode.Node;
using XNodePort = XNode.NodePort;

namespace SaintsGraph.Editor.XNodeMigration
{
    /// <summary>
    /// Converts xNode graph assets into SaintsGraph JSON sidecars, which
    /// <see cref="GraphJson.Import"/> then applies to a SaintsGraph asset.
    ///
    /// The JSON is the migration format on purpose: it is written while xNode is still
    /// installed and the node classes still derive from <c>XNode.Node</c>, and it is read
    /// back after the classes have been re-based on <c>SaintsGraph.Node</c> — type names
    /// and field names survive that switch untouched, so no bridging types are needed.
    ///
    /// This assembly only compiles when xNode is installed; define
    /// SAINTSGRAPH_XNODE_DISABLE to opt out.
    /// </summary>
    public static class XNodeGraphMigrator
    {
        private static readonly string[] SkipFields =
            { "m_Name", "m_EditorClassIdentifier", "m_Script", "graph", "position", "ports" };

        public static string ToSidecarJson(XNodeGraph graph)
        {
            Dictionary<XNodeNode, string> ids = BuildIds(graph);

            JsonObject root = new JsonObject
            {
                ["format"] = new JsonString("saintsgraph/1"),
                ["graphType"] = new JsonString(TypeString(graph.GetType()))
            };

            JsonArray nodes = new JsonArray();
            foreach (XNodeNode node in graph.nodes)
            {
                if (node != null)
                {
                    nodes.Items.Add(ExportNode(node, ids[node]));
                }
            }

            root["nodes"] = nodes;

            JsonArray edges = new JsonArray();
            foreach (XNodeNode node in graph.nodes)
            {
                if (node == null)
                {
                    continue;
                }

                foreach (XNodePort port in node.Outputs)
                {
                    foreach (XNodePort connection in port.GetConnections())
                    {
                        if (connection?.node == null || !ids.TryGetValue(connection.node, out string targetId))
                        {
                            continue;
                        }

                        JsonArray edge = new JsonArray();
                        edge.Items.Add(new JsonString(ids[node]));
                        edge.Items.Add(new JsonString(port.fieldName));
                        edge.Items.Add(new JsonString(targetId));
                        edge.Items.Add(new JsonString(connection.fieldName));
                        edges.Items.Add(edge);
                    }
                }
            }

            root["edges"] = edges;
            return root.Write() + "\n";
        }

        /// <summary>Writes &lt;Graph&gt;.graph.json next to the xNode asset and returns its path.</summary>
        public static string WriteSidecar(XNodeGraph graph)
        {
            string assetPath = AssetDatabase.GetAssetPath(graph);
            if (string.IsNullOrEmpty(assetPath))
            {
                throw new InvalidOperationException("Graph is not saved as an asset");
            }

            string path = Path.ChangeExtension(assetPath, null) + GraphJson.SidecarSuffix;
            File.WriteAllText(path, ToSidecarJson(graph));
            AssetDatabase.ImportAsset(path);
            return path;
        }

        private static JsonObject ExportNode(XNodeNode node, string id)
        {
            JsonObject result = new JsonObject
            {
                ["id"] = new JsonString(id),
                ["name"] = new JsonString(node.name),
                ["type"] = new JsonString(TypeString(node.GetType()))
            };

            JsonArray position = new JsonArray();
            position.Items.Add(JsonNumber.From(Math.Round(node.position.x, 2)));
            position.Items.Add(JsonNumber.From(Math.Round(node.position.y, 2)));
            result["position"] = position;

            JsonObject raw = (JsonObject)JsonValue.Parse(EditorJsonUtility.ToJson(node));
            foreach (string key in SkipFields)
            {
                raw.Remove(key);
            }

            JsonObject fields = new JsonObject();
            foreach (KeyValuePair<string, JsonValue> entry in raw.Entries)
            {
                fields[entry.Key] = GraphJson.ObjectRefsToStrings(entry.Value);
            }

            if (fields.Entries.Count > 0)
            {
                result["fields"] = fields;
            }

            JsonArray dynamicPorts = new JsonArray();
            foreach (XNodePort port in node.DynamicPorts)
            {
                dynamicPorts.Items.Add(new JsonObject
                {
                    ["fieldName"] = new JsonString(port.fieldName),
                    ["typeQualifiedName"] =
                        new JsonString(port.ValueType == null ? "" : port.ValueType.AssemblyQualifiedName),
                    ["direction"] = JsonNumber.From(MapEnum<NodePort.IO>(port.direction)),
                    ["connectionType"] = JsonNumber.From(MapEnum<Node.ConnectionType>(port.connectionType)),
                    ["typeConstraint"] = JsonNumber.From(MapEnum<Node.TypeConstraint>(port.typeConstraint))
                });
            }

            if (dynamicPorts.Items.Count > 0)
            {
                result["dynamicPorts"] = dynamicPorts;
            }

            return result;
        }

        /// <summary>Maps an xNode enum value onto the SaintsGraph equivalent by name, not by ordinal.</summary>
        private static int MapEnum<T>(object xNodeValue) where T : struct, Enum
        {
            return Enum.TryParse(xNodeValue.ToString(), out T parsed) ? Convert.ToInt32(parsed) : 0;
        }

        private static Dictionary<XNodeNode, string> BuildIds(XNodeGraph graph)
        {
            Dictionary<XNodeNode, string> result = new Dictionary<XNodeNode, string>();
            HashSet<string> taken = new HashSet<string>();
            foreach (XNodeNode node in graph.nodes)
            {
                if (node == null)
                {
                    continue;
                }

                string baseId = string.IsNullOrEmpty(node.name) ? node.GetType().Name : node.name;
                string id = baseId;
                int suffix = 2;
                while (!taken.Add(id))
                {
                    id = baseId + "#" + suffix;
                    suffix++;
                }

                result[node] = id;
            }

            return result;
        }

        private static string TypeString(Type type)
        {
            return type.FullName + ", " + type.Assembly.GetName().Name;
        }
    }
}
