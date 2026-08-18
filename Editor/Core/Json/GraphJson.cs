using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SaintsGraph.Editor
{
    /// <summary>
    /// Human/LLM-friendly JSON sidecar for graph assets: a flat node list with field
    /// values plus an edge list. Export writes <c>&lt;Graph&gt;.graph.json</c> next to
    /// the asset; import applies edits (field values, positions, renames, added and
    /// removed nodes and edges) back onto the asset.
    ///
    /// Node "id" is the stable key used by edges; "name" is the display name — editing
    /// it in JSON renames the node. Asset references serialize as "$ref:guid:localId".
    /// Field values use the same shape as Unity's own JSON (EditorJsonUtility), merged
    /// on top of the current node state, so unknown fields keep their values.
    /// </summary>
    public static class GraphJson
    {
        public const string SidecarSuffix = ".graph.json";
        private const string FormatVersion = "saintsgraph/1";
        private static readonly string[] InternalFields = { "m_Name", "m_EditorClassIdentifier", "graph", "position", "dynamicPorts" };

        public static string SidecarPathFor(NodeGraph graph)
        {
            string assetPath = AssetDatabase.GetAssetPath(graph);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            return Path.ChangeExtension(assetPath, null) + SidecarSuffix;
        }

        public static string Export(NodeGraph graph)
        {
            Dictionary<Node, string> ids = BuildIds(graph);
            JsonObject root = new JsonObject
            {
                ["format"] = new JsonString(FormatVersion),
                ["graphType"] = new JsonString(TypeString(graph.GetType()))
            };

            JsonArray nodes = new JsonArray();
            foreach (Node node in graph.nodes)
            {
                if (node == null)
                {
                    continue;
                }

                nodes.Items.Add(ExportNode(node, ids[node]));
            }

            root["nodes"] = nodes;

            JsonArray edges = new JsonArray();
            foreach (NodeEdge edge in graph.Edges)
            {
                if (edge.outputNode == null || edge.inputNode == null)
                {
                    continue;
                }

                JsonArray entry = new JsonArray();
                entry.Items.Add(new JsonString(ids[edge.outputNode]));
                entry.Items.Add(new JsonString(edge.outputField));
                entry.Items.Add(new JsonString(ids[edge.inputNode]));
                entry.Items.Add(new JsonString(edge.inputField));
                edges.Items.Add(entry);
            }

            root["edges"] = edges;
            return root.Write() + "\n";
        }

        public static void ExportToFile(NodeGraph graph, bool importAsset = true)
        {
            string path = SidecarPathFor(graph);
            if (path == null)
            {
                Debug.LogWarning("Cannot export a graph that is not saved as an asset", graph);
                return;
            }

            File.WriteAllText(path, Export(graph));
            if (importAsset)
            {
                AssetDatabase.ImportAsset(path);
            }
        }

        private static JsonObject ExportNode(Node node, string id)
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
            JsonValue dynamicPorts = raw["dynamicPorts"];
            foreach (string key in InternalFields)
            {
                raw.Remove(key);
            }

            JsonObject fields = new JsonObject();
            foreach (KeyValuePair<string, JsonValue> entry in raw.Entries)
            {
                fields[entry.Key] = ObjectRefsToStrings(entry.Value);
            }

            if (fields.Entries.Count > 0)
            {
                result["fields"] = fields;
            }

            if (dynamicPorts is JsonArray dynamicArray && dynamicArray.Items.Count > 0)
            {
                result["dynamicPorts"] = dynamicPorts;
            }

            return result;
        }

        public static void Import(NodeGraph graph, string json)
        {
            if (!(JsonValue.Parse(json) is JsonObject root))
            {
                throw new FormatException("Sidecar root must be a JSON object");
            }

            Undo.RegisterCompleteObjectUndo(graph, "Import Graph JSON");
            string assetPath = AssetDatabase.GetAssetPath(graph);

            Dictionary<Node, string> currentIds = BuildIds(graph);
            Dictionary<string, Node> byId = currentIds.ToDictionary(pair => pair.Value, pair => pair.Key);
            HashSet<string> seenIds = new HashSet<string>();

            if (root["nodes"] is JsonArray jsonNodes)
            {
                foreach (JsonValue value in jsonNodes.Items)
                {
                    if (!(value is JsonObject jsonNode))
                    {
                        continue;
                    }

                    string id = jsonNode.GetString("id");
                    if (string.IsNullOrEmpty(id))
                    {
                        Debug.LogWarning("Sidecar node without id skipped");
                        continue;
                    }

                    seenIds.Add(id);
                    if (!byId.TryGetValue(id, out Node node))
                    {
                        Type nodeType = ResolveType(jsonNode.GetString("type"));
                        if (nodeType == null)
                        {
                            Debug.LogWarning("Cannot resolve node type '" + jsonNode.GetString("type") + "' for '" + id + "'");
                            continue;
                        }

                        node = graph.AddNode(nodeType);
                        Undo.RegisterCreatedObjectUndo(node, "Import Graph JSON");
                        node.name = id;
                        if (!string.IsNullOrEmpty(assetPath))
                        {
                            AssetDatabase.AddObjectToAsset(node, graph);
                        }

                        byId[id] = node;
                    }
                    else
                    {
                        Undo.RecordObject(node, "Import Graph JSON");
                    }

                    ApplyNode(jsonNode, node);
                }
            }

            foreach (KeyValuePair<Node, string> pair in currentIds)
            {
                if (!seenIds.Contains(pair.Value) && pair.Key != null)
                {
                    graph.RemoveNode(pair.Key);
                    Undo.DestroyObjectImmediate(pair.Key);
                }
            }

            ApplyEdges(graph, root["edges"] as JsonArray, byId);

            graph.PruneInvalidEdges();
            EditorUtility.SetDirty(graph);
            foreach (Node node in graph.nodes)
            {
                if (node != null)
                {
                    EditorUtility.SetDirty(node);
                }
            }

            AssetDatabase.SaveAssets();
        }

        private static void ApplyNode(JsonObject jsonNode, Node node)
        {
            if (jsonNode["position"] is JsonArray position && position.Items.Count == 2
                && position.Items[0] is JsonNumber x && position.Items[1] is JsonNumber y)
            {
                node.position = new Vector2(x.AsFloat, y.AsFloat);
            }

            JsonObject current = (JsonObject)JsonValue.Parse(EditorJsonUtility.ToJson(node));
            bool changed = false;

            if (jsonNode["fields"] is JsonObject fields)
            {
                foreach (KeyValuePair<string, JsonValue> entry in fields.Entries)
                {
                    if (Array.IndexOf(InternalFields, entry.Key) >= 0)
                    {
                        continue;
                    }

                    current[entry.Key] = StringsToObjectRefs(entry.Value);
                    changed = true;
                }
            }

            if (jsonNode["dynamicPorts"] is JsonArray dynamicPorts)
            {
                current["dynamicPorts"] = dynamicPorts;
                changed = true;
            }

            if (changed)
            {
                // Identity and ownership must never go through the overwrite: FromJsonOverwrite
                // restores m_Name of persistent objects to its serialized value, which would
                // undo renames, and the graph reference belongs to the model, not the sidecar.
                current.Remove("m_Name");
                current.Remove("graph");
                EditorJsonUtility.FromJsonOverwrite(current.Write(false), node);
            }

            // Rename last so no serialization pass can clobber it.
            string name = jsonNode.GetString("name");
            if (!string.IsNullOrEmpty(name) && node.name != name)
            {
                node.name = name;
            }

            node.UpdatePorts();
        }

        private static void ApplyEdges(NodeGraph graph, JsonArray jsonEdges, Dictionary<string, Node> byId)
        {
            List<(NodePort output, NodePort input)> desired = new List<(NodePort, NodePort)>();
            if (jsonEdges != null)
            {
                foreach (JsonValue value in jsonEdges.Items)
                {
                    if (!(value is JsonArray entry) || entry.Items.Count != 4)
                    {
                        continue;
                    }

                    NodePort output = ResolvePort(byId, entry.Items[0], entry.Items[1], NodePort.IO.Output);
                    NodePort input = ResolvePort(byId, entry.Items[2], entry.Items[3], NodePort.IO.Input);
                    if (output != null && input != null)
                    {
                        desired.Add((output, input));
                    }
                }
            }

            foreach (NodeEdge edge in graph.Edges.ToList())
            {
                NodePort output = edge.OutputPort;
                NodePort input = edge.InputPort;
                bool wanted = output != null && input != null && desired.Any(pair =>
                    ReferenceEquals(pair.output.node, output.node) && pair.output.fieldName == output.fieldName
                    && ReferenceEquals(pair.input.node, input.node) && pair.input.fieldName == input.fieldName);
                if (!wanted && output != null)
                {
                    output.Disconnect(input);
                }
            }

            foreach ((NodePort output, NodePort input) in desired)
            {
                if (!output.IsConnectedTo(input))
                {
                    output.Connect(input);
                }
            }
        }

        private static NodePort ResolvePort(Dictionary<string, Node> byId, JsonValue nodeId, JsonValue fieldName,
            NodePort.IO direction)
        {
            if (!(nodeId is JsonString id) || !(fieldName is JsonString field))
            {
                return null;
            }

            if (!byId.TryGetValue(id.Value, out Node node) || node == null)
            {
                Debug.LogWarning("Sidecar edge references unknown node '" + id.Value + "'");
                return null;
            }

            NodePort port = node.GetPort(field.Value);
            if (port == null || port.direction != direction)
            {
                Debug.LogWarning("Sidecar edge references unknown " + direction + " port '" + id.Value + "." + field.Value + "'");
                return null;
            }

            return port;
        }

        /// <summary>Stable, human-readable node ids: node names, uniquified with #2, #3... in graph order.</summary>
        public static Dictionary<Node, string> BuildIds(NodeGraph graph)
        {
            Dictionary<Node, string> result = new Dictionary<Node, string>();
            HashSet<string> taken = new HashSet<string>();
            foreach (Node node in graph.nodes)
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

        private static Type ResolveType(string typeString)
        {
            return string.IsNullOrEmpty(typeString) ? null : Type.GetType(typeString, false);
        }

        /// <summary>{"instanceID": n} → "$ref:guid:localId" (or null for none/unpersistable).</summary>
        internal static JsonValue ObjectRefsToStrings(JsonValue value)
        {
            switch (value)
            {
                case JsonObject obj when obj.Entries.Count == 1 && obj.Entries[0].Key == "instanceID":
                {
                    if (!(obj.Entries[0].Value is JsonNumber number) || (int)number.AsDouble == 0)
                    {
                        return JsonNull.Instance;
                    }

                    Object target = EditorUtility.InstanceIDToObject((int)number.AsDouble);
                    if (target == null
                        || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out string guid, out long localId))
                    {
                        return JsonNull.Instance;
                    }

                    return new JsonString("$ref:" + guid + ":" + localId);
                }
                case JsonObject obj:
                {
                    JsonObject copy = new JsonObject();
                    foreach (KeyValuePair<string, JsonValue> entry in obj.Entries)
                    {
                        copy[entry.Key] = ObjectRefsToStrings(entry.Value);
                    }

                    return copy;
                }
                case JsonArray array:
                {
                    JsonArray copy = new JsonArray();
                    foreach (JsonValue item in array.Items)
                    {
                        copy.Items.Add(ObjectRefsToStrings(item));
                    }

                    return copy;
                }
                default:
                    return value;
            }
        }

        /// <summary>"$ref:guid:localId" → {"instanceID": n}; null in a ref position stays null (Unity treats it as none).</summary>
        private static JsonValue StringsToObjectRefs(JsonValue value)
        {
            switch (value)
            {
                case JsonString text when text.Value.StartsWith("$ref:", StringComparison.Ordinal):
                {
                    string[] parts = text.Value.Split(':');
                    if (parts.Length != 3 || !long.TryParse(parts[2], out long localId))
                    {
                        return MakeInstanceId(0);
                    }

                    string path = AssetDatabase.GUIDToAssetPath(parts[1]);
                    if (string.IsNullOrEmpty(path))
                    {
                        Debug.LogWarning("Sidecar reference to missing asset guid " + parts[1]);
                        return MakeInstanceId(0);
                    }

                    foreach (Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        if (candidate != null
                            && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out string _, out long candidateId)
                            && candidateId == localId)
                        {
                            return MakeInstanceId(candidate.GetInstanceID());
                        }
                    }

                    Object main = AssetDatabase.LoadMainAssetAtPath(path);
                    return MakeInstanceId(main == null ? 0 : main.GetInstanceID());
                }
                case JsonNull _:
                    return MakeInstanceId(0);
                case JsonObject obj:
                {
                    JsonObject copy = new JsonObject();
                    foreach (KeyValuePair<string, JsonValue> entry in obj.Entries)
                    {
                        copy[entry.Key] = StringsToObjectRefs(entry.Value);
                    }

                    return copy;
                }
                case JsonArray array:
                {
                    JsonArray copy = new JsonArray();
                    foreach (JsonValue item in array.Items)
                    {
                        copy.Items.Add(StringsToObjectRefs(item));
                    }

                    return copy;
                }
                default:
                    return value;
            }
        }

        private static JsonObject MakeInstanceId(int id)
        {
            return new JsonObject { ["instanceID"] = JsonNumber.From(id) };
        }
    }
}
