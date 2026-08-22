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
    /// Node "id" is the stable key used by edges; "name" is the display name вЂ” editing
    /// it in JSON renames the node.
    ///
    /// Field values use Unity's own serialization (EditorJsonUtility) merged on top of the
    /// current node state, so unknown fields keep their values: asset references appear as
    /// {fileID, guid, type} and survive by GUID, and [SerializeReference] fields appear as
    /// {"rid": n} plus a "references" block naming the concrete type вЂ” both round-trip.
    /// Unity wraps that payload in a single "MonoBehaviour" key, which is unwrapped here so
    /// the sidecar stays flat and readable.
    /// </summary>
    public static class GraphJson
    {
        public const string SidecarSuffix = ".graph.json";
        private const string FormatVersion = "saintsgraph/1";

        /// <summary>Engine bookkeeping and fields the sidecar represents at node level instead.</summary>
        internal static IReadOnlyList<string> InternalFieldNames => InternalFields;

        private static readonly string[] InternalFields =
        {
            "m_Enabled", "m_EditorHideFlags", "m_ObjectHideFlags", "m_Name", "m_EditorClassIdentifier",
            "m_Script", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
            "graph", "position", "dynamicPorts", "uid", "collapsed", "nodeWidth"
        };

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
            return Export(graph, graph.nodes);
        }

        /// <summary>
        /// Exports a subset of a graph: the given nodes plus the edges whose both endpoints are
        /// among them. Used for copy/paste, which shares the sidecar format — so a copied
        /// selection can be pasted as text, and text can be pasted as a selection.
        /// </summary>
        public static string Export(NodeGraph graph, IEnumerable<Node> nodes)
        {
            List<Node> subset = nodes.Where(node => node != null).ToList();
            Dictionary<Node, string> ids = BuildIds(subset);
            JsonObject root = new JsonObject
            {
                ["format"] = new JsonString(FormatVersion),
                ["graphType"] = new JsonString(TypeString(graph.GetType()))
            };

            JsonArray jsonNodes = new JsonArray();
            foreach (Node node in subset)
            {
                jsonNodes.Items.Add(ExportNode(node, ids[node]));
            }

            root["nodes"] = jsonNodes;

            JsonArray edges = new JsonArray();
            foreach (NodeEdge edge in graph.Edges)
            {
                if (edge.outputNode == null || edge.inputNode == null
                    || !ids.ContainsKey(edge.outputNode) || !ids.ContainsKey(edge.inputNode))
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

            JsonArray groups = new JsonArray();
            foreach (NodeGroup group in graph.Groups)
            {
                JsonArray members = new JsonArray();
                foreach (Node member in group.nodes)
                {
                    if (member != null && ids.TryGetValue(member, out string memberId))
                    {
                        members.Items.Add(new JsonString(memberId));
                    }
                }

                JsonArray groupPosition = new JsonArray();
                groupPosition.Items.Add(JsonNumber.From(Math.Round(group.position.x, 2)));
                groupPosition.Items.Add(JsonNumber.From(Math.Round(group.position.y, 2)));

                groups.Items.Add(new JsonObject
                {
                    ["title"] = new JsonString(group.title ?? ""),
                    ["position"] = groupPosition,
                    ["nodes"] = members
                });
            }

            if (groups.Items.Count > 0)
            {
                root["groups"] = groups;
            }

            JsonArray notes = new JsonArray();
            foreach (NodeNote note in graph.Notes)
            {
                JsonArray area = new JsonArray();
                area.Items.Add(JsonNumber.From(Math.Round(note.area.x, 2)));
                area.Items.Add(JsonNumber.From(Math.Round(note.area.y, 2)));
                area.Items.Add(JsonNumber.From(Math.Round(note.area.width, 2)));
                area.Items.Add(JsonNumber.From(Math.Round(note.area.height, 2)));

                JsonObject entry = new JsonObject
                {
                    ["title"] = new JsonString(note.title ?? ""),
                    ["text"] = new JsonString(note.text ?? ""),
                    ["area"] = area
                };

                if (note.theme != 0)
                {
                    entry["theme"] = JsonNumber.From(note.theme);
                }

                if (note.fontSize != 0)
                {
                    entry["fontSize"] = JsonNumber.From(note.fontSize);
                }

                notes.Items.Add(entry);
            }

            if (notes.Items.Count > 0)
            {
                root["notes"] = notes;
            }
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
                ["uid"] = new JsonString(node.Uid),
                ["name"] = new JsonString(node.name),
                ["type"] = new JsonString(TypeString(node.GetType()))
            };

            JsonArray position = new JsonArray();
            position.Items.Add(JsonNumber.From(Math.Round(node.position.x, 2)));
            position.Items.Add(JsonNumber.From(Math.Round(node.position.y, 2)));
            result["position"] = position;

            if (node.collapsed)
            {
                result["collapsed"] = new JsonBool(true);
            }

            if (node.nodeWidth > 0f)
            {
                result["width"] = JsonNumber.From(Math.Round(node.nodeWidth));
            }

            JsonObject payload = UnwrapPayload(node, out string _);
            JsonValue dynamicPorts = payload["dynamicPorts"];
            foreach (string key in InternalFields)
            {
                payload.Remove(key);
            }

            JsonObject fields = new JsonObject();
            foreach (KeyValuePair<string, JsonValue> entry in payload.Entries)
            {
                fields[entry.Key] = entry.Value;
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
            Dictionary<string, Node> byUid = new Dictionary<string, Node>();
            foreach (Node existing in graph.nodes)
            {
                if (existing != null)
                {
                    byUid[existing.Uid] = existing;
                }
            }

            HashSet<Node> seenNodes = new HashSet<Node>();

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

                    // Identity first, readable id second: renaming a node in the file (or in the
                    // editor) must update it, not replace it.
                    string uid = jsonNode.GetString("uid");
                    Node node = null;
                    if (!string.IsNullOrEmpty(uid))
                    {
                        byUid.TryGetValue(uid, out node);
                    }

                    if (node == null && !byId.TryGetValue(id, out node))
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
                        if (!string.IsNullOrEmpty(uid))
                        {
                            node.AdoptUid(uid);
                        }

                        if (!string.IsNullOrEmpty(assetPath))
                        {
                            AssetDatabase.AddObjectToAsset(node, graph);
                        }
                    }
                    else
                    {
                        Undo.RecordObject(node, "Import Graph JSON");
                    }

                    ApplyNode(jsonNode, node);
                    seenNodes.Add(node);
                    byId[id] = node;
                }
            }

            foreach (Node existing in currentIds.Keys)
            {
                if (existing != null && !seenNodes.Contains(existing))
                {
                    graph.RemoveNode(existing);
                    Undo.DestroyObjectImmediate(existing);
                }
            }

            ApplyEdges(graph, root["edges"] as JsonArray, byId);
            ApplyAnnotations(graph, root, byId);

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

        /// <summary>
        /// Adds the nodes and internal edges described by <paramref name="json"/> to an existing
        /// graph, without touching what is already there. Node names are made unique, so pasting
        /// the same clipboard twice yields two independent copies. Returns the created nodes.
        /// </summary>
        public static List<Node> Paste(NodeGraph graph, string json, Vector2 offset)
        {
            if (!(JsonValue.Parse(json) is JsonObject root) || !(root["nodes"] is JsonArray jsonNodes))
            {
                throw new FormatException("Clipboard JSON has no \"nodes\" array");
            }

            Undo.RegisterCompleteObjectUndo(graph, "Paste Nodes");
            string assetPath = AssetDatabase.GetAssetPath(graph);
            HashSet<string> usedNames = new HashSet<string>(
                graph.nodes.Where(node => node != null).Select(node => node.name));

            Dictionary<string, Node> byId = new Dictionary<string, Node>();
            List<Node> created = new List<Node>();

            foreach (JsonValue value in jsonNodes.Items)
            {
                if (!(value is JsonObject jsonNode))
                {
                    continue;
                }

                Type nodeType = ResolveType(jsonNode.GetString("type"));
                if (nodeType == null || !typeof(Node).IsAssignableFrom(nodeType))
                {
                    Debug.LogWarning("Cannot paste node of unknown type '" + jsonNode.GetString("type") + "'");
                    continue;
                }

                Node node = graph.AddNode(nodeType);
                Undo.RegisterCreatedObjectUndo(node, "Paste Nodes");
                ApplyNode(jsonNode, node);
                node.name = UniqueName(node.name, nodeType, usedNames);
                node.position += offset;

                if (!string.IsNullOrEmpty(assetPath))
                {
                    AssetDatabase.AddObjectToAsset(node, graph);
                }

                string id = jsonNode.GetString("id");
                if (!string.IsNullOrEmpty(id))
                {
                    byId[id] = node;
                }

                created.Add(node);
                EditorUtility.SetDirty(node);
            }

            if (root["edges"] is JsonArray jsonEdges)
            {
                foreach (JsonValue value in jsonEdges.Items)
                {
                    if (!(value is JsonArray entry) || entry.Items.Count != 4)
                    {
                        continue;
                    }

                    NodePort output = ResolvePort(byId, entry.Items[0], entry.Items[1], NodePort.IO.Output);
                    NodePort input = ResolvePort(byId, entry.Items[2], entry.Items[3], NodePort.IO.Input);
                    if (output != null && input != null && !output.IsConnectedTo(input))
                    {
                        output.Connect(input);
                    }
                }
            }

            EditorUtility.SetDirty(graph);
            return created;
        }

        /// <summary>
        /// Top-left corner of the node positions in a graph/clipboard JSON. Lets a caller place a
        /// paste at a point (mouse position) instead of a blind offset.
        /// </summary>
        public static bool TryGetTopLeft(string json, out Vector2 topLeft)
        {
            topLeft = Vector2.zero;
            if (!(JsonValue.Parse(json) is JsonObject root) || !(root["nodes"] is JsonArray nodes))
            {
                return false;
            }

            bool found = false;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            foreach (JsonValue value in nodes.Items)
            {
                if (!(value is JsonObject node) || !(node["position"] is JsonArray position)
                    || position.Items.Count != 2
                    || !(position.Items[0] is JsonNumber x) || !(position.Items[1] is JsonNumber y))
                {
                    continue;
                }

                minX = Mathf.Min(minX, x.AsFloat);
                minY = Mathf.Min(minY, y.AsFloat);
                found = true;
            }

            if (found)
            {
                topLeft = new Vector2(minX, minY);
            }

            return found;
        }

        private static string UniqueName(string preferred, Type nodeType, HashSet<string> used)
        {
            string baseName = string.IsNullOrEmpty(preferred)
                ? NodeEditorUtilities.NodeDefaultName(nodeType)
                : preferred;
            string candidate = baseName;
            int suffix = 2;
            while (!used.Add(candidate))
            {
                candidate = baseName + " " + suffix;
                suffix++;
            }

            return candidate;
        }

        private static void ApplyNode(JsonObject jsonNode, Node node)
        {
            JsonObject payload = UnwrapPayload(node, out string wrapperKey);
            bool changed = false;

            if (jsonNode["fields"] is JsonObject fields)
            {
                foreach (KeyValuePair<string, JsonValue> entry in fields.Entries)
                {
                    if (Array.IndexOf(InternalFields, entry.Key) >= 0)
                    {
                        continue;
                    }

                    payload[entry.Key] = entry.Value;
                    changed = true;
                }
            }

            if (jsonNode["dynamicPorts"] is JsonArray dynamicPorts)
            {
                payload["dynamicPorts"] = dynamicPorts;
                changed = true;
            }

            if (changed)
            {
                // Identity, ownership and layout never go through the overwrite: FromJsonOverwrite
                // restores m_Name of persistent objects (undoing renames), the graph reference
                // belongs to the model rather than the sidecar (importing into another asset must
                // not repoint it), and position is applied from the node-level field below.
                payload.Remove("m_Name");
                payload.Remove("graph");
                payload.Remove("position");

                JsonValue document = wrapperKey == null
                    ? (JsonValue)payload
                    : new JsonObject { [wrapperKey] = payload };
                EditorJsonUtility.FromJsonOverwrite(document.Write(false), node);
            }

            // Position and name are applied after the overwrite so no serialization pass clobbers them.
            if (jsonNode["position"] is JsonArray position && position.Items.Count == 2
                && position.Items[0] is JsonNumber x && position.Items[1] is JsonNumber y)
            {
                node.position = new Vector2(x.AsFloat, y.AsFloat);
            }

            node.collapsed = jsonNode["collapsed"] is JsonBool collapsed && collapsed.Value;
            node.nodeWidth = jsonNode["width"] is JsonNumber width ? width.AsFloat : 0f;

            string name = jsonNode.GetString("name");
            if (!string.IsNullOrEmpty(name) && node.name != name)
            {
                node.name = name;
            }

            node.UpdatePorts();
        }

        /// <summary>
        /// EditorJsonUtility wraps an object's fields in a single key ("MonoBehaviour" for
        /// ScriptableObjects). Returns the inner payload plus that key, so the document can be
        /// rebuilt for FromJsonOverwrite. Falls back to the root object if the shape ever changes.
        /// </summary>
        internal static JsonObject UnwrapPayload(Object target, out string wrapperKey)
        {
            JsonObject root = (JsonObject)JsonValue.Parse(EditorJsonUtility.ToJson(target));
            if (root.Entries.Count == 1 && root.Entries[0].Value is JsonObject payload)
            {
                wrapperKey = root.Entries[0].Key;
                return payload;
            }

            wrapperKey = null;
            return root;
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

        /// <summary>Groups and notes are rebuilt wholesale — they are annotation, with no identity to preserve.</summary>
        private static void ApplyAnnotations(NodeGraph graph, JsonObject root, Dictionary<string, Node> byId)
        {
            graph.Groups.Clear();
            if (root["groups"] is JsonArray groups)
            {
                foreach (JsonValue value in groups.Items)
                {
                    if (!(value is JsonObject jsonGroup))
                    {
                        continue;
                    }

                    NodeGroup group = new NodeGroup { title = jsonGroup.GetString("title") ?? "Group" };
                    if (jsonGroup["position"] is JsonArray position && position.Items.Count == 2
                        && position.Items[0] is JsonNumber x && position.Items[1] is JsonNumber y)
                    {
                        group.position = new Vector2(x.AsFloat, y.AsFloat);
                    }

                    if (jsonGroup["nodes"] is JsonArray members)
                    {
                        foreach (JsonValue member in members.Items)
                        {
                            if (member is JsonString id && byId.TryGetValue(id.Value, out Node node) && node != null)
                            {
                                group.nodes.Add(node);
                            }
                        }
                    }

                    graph.Groups.Add(group);
                }
            }

            graph.Notes.Clear();
            if (root["notes"] is JsonArray notes)
            {
                foreach (JsonValue value in notes.Items)
                {
                    if (!(value is JsonObject jsonNote))
                    {
                        continue;
                    }

                    NodeNote note = new NodeNote
                    {
                        title = jsonNote.GetString("title") ?? "",
                        text = jsonNote.GetString("text") ?? ""
                    };

                    if (jsonNote["area"] is JsonArray area && area.Items.Count == 4)
                    {
                        note.area = new Rect(
                            ((JsonNumber)area.Items[0]).AsFloat, ((JsonNumber)area.Items[1]).AsFloat,
                            ((JsonNumber)area.Items[2]).AsFloat, ((JsonNumber)area.Items[3]).AsFloat);
                    }

                    if (jsonNote["theme"] is JsonNumber theme)
                    {
                        note.theme = (int)theme.AsDouble;
                    }

                    if (jsonNote["fontSize"] is JsonNumber fontSize)
                    {
                        note.fontSize = (int)fontSize.AsDouble;
                    }

                    graph.Notes.Add(note);
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
            return BuildIds(graph.nodes);
        }

        public static Dictionary<Node, string> BuildIds(IEnumerable<Node> nodes)
        {
            Dictionary<Node, string> result = new Dictionary<Node, string>();
            HashSet<string> taken = new HashSet<string>();
            foreach (Node node in nodes)
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
    }
}
