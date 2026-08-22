using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SaintsGraph.Editor
{
    /// <summary>
    /// Machine-readable description of what can appear in a graph: every node type with its ports,
    /// default field values and menu path. Paired with the sidecar format, it is everything a tool
    /// (or a language model) needs to author a valid graph instead of guessing type names.
    /// </summary>
    public static class GraphSchema
    {
        private const string SchemaVersion = "saintsgraph-schema/1";

        private static readonly string[] HowTo =
        {
            "A graph document is {\"format\":\"saintsgraph/1\", \"nodes\":[...], \"edges\":[...]}.",
            "Each node is {\"id\", \"name\", \"type\", \"position\":[x,y], \"fields\":{...}}; \"type\" must be copied verbatim from this schema.",
            "\"id\" is a handle used only inside the document — any unique string works. \"uid\" is optional and identifies an existing node across renames.",
            "\"fields\" uses Unity's own JSON shape; copy \"defaults\" from this schema and change what you need. Omitted fields keep their current value.",
            "Each edge is [outputNodeId, outputPortName, inputNodeId, inputPortName] and must connect an output port to an input port of compatible type.",
            "Ports marked \"dynamicList\": true are per-element ports named \"<field> <index>\", e.g. \"terms 0\"; add the elements to the backing array field too.",
            "A whole document can be pasted directly into an open graph window with Ctrl+V, or imported over a graph asset via Assets/SaintsGraph/Import Graph JSON."
        };

        public static string Export()
        {
            JsonObject root = new JsonObject
            {
                ["format"] = new JsonString(SchemaVersion),
                ["graphFormat"] = new JsonString("saintsgraph/1")
            };

            JsonArray howTo = new JsonArray();
            foreach (string line in HowTo)
            {
                howTo.Items.Add(new JsonString(line));
            }

            root["howTo"] = howTo;

            JsonArray graphTypes = new JsonArray();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<NodeGraph>())
            {
                if (!type.IsAbstract)
                {
                    graphTypes.Items.Add(DescribeGraph(type));
                }
            }

            root["graphTypes"] = graphTypes;

            JsonArray nodeTypes = new JsonArray();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<Node>())
            {
                if (!type.IsAbstract)
                {
                    nodeTypes.Items.Add(DescribeNode(type));
                }
            }

            root["nodeTypes"] = nodeTypes;
            return root.Write() + "\n";
        }

        private static JsonObject DescribeGraph(Type type)
        {
            JsonObject result = new JsonObject
            {
                ["type"] = new JsonString(TypeString(type))
            };

            JsonArray required = new JsonArray();
            foreach (NodeGraph.RequireNodeAttribute attribute in
                     type.GetCustomAttributes<NodeGraph.RequireNodeAttribute>())
            {
                foreach (Type requiredType in new[] { attribute.type0, attribute.type1, attribute.type2 })
                {
                    if (requiredType != null)
                    {
                        required.Items.Add(new JsonString(TypeString(requiredType)));
                    }
                }
            }

            if (required.Items.Count > 0)
            {
                result["requiresNodes"] = required;
            }

            return result;
        }

        private static JsonObject DescribeNode(Type type)
        {
            JsonObject result = new JsonObject
            {
                ["type"] = new JsonString(TypeString(type))
            };

            Node.CreateNodeMenuAttribute menu = type.GetCustomAttribute<Node.CreateNodeMenuAttribute>();
            string menuName = menu != null ? menu.menuName : NodeEditorUtilities.NodeDefaultPath(type);
            if (!string.IsNullOrEmpty(menuName))
            {
                result["menu"] = new JsonString(menuName);
            }
            else
            {
                result["hiddenFromMenu"] = new JsonBool(true);
            }

            Node.DisallowMultipleNodesAttribute disallow =
                type.GetCustomAttribute<Node.DisallowMultipleNodesAttribute>();
            if (disallow != null)
            {
                result["maxInstances"] = JsonNumber.From(disallow.max);
            }

            JsonArray ports = new JsonArray();
            foreach (PortCache.PortTemplate template in PortCache.GetTemplates(type))
            {
                JsonObject port = new JsonObject
                {
                    ["name"] = new JsonString(template.fieldName),
                    ["direction"] = new JsonString(template.direction == NodePort.IO.Input ? "input" : "output"),
                    ["valueType"] = new JsonString(template.valueType == null
                        ? "unknown"
                        : PortCache.GetListElementType(template.valueType).FullName),
                    ["connection"] = new JsonString(template.connectionType.ToString().ToLowerInvariant()),
                    ["constraint"] = new JsonString(template.typeConstraint.ToString().ToLowerInvariant()),
                    ["showBackingValue"] = new JsonString(template.backingValue.ToString().ToLowerInvariant())
                };

                if (template.dynamicPortList)
                {
                    port["dynamicList"] = new JsonBool(true);
                }

                ports.Items.Add(port);
            }

            result["ports"] = ports;

            JsonObject defaults = DescribeDefaults(type);
            if (defaults != null && defaults.Entries.Count > 0)
            {
                result["defaults"] = defaults;
            }

            return result;
        }

        /// <summary>Default field values, taken from a throwaway instance — the exact shape "fields" expects.</summary>
        private static JsonObject DescribeDefaults(Type type)
        {
            Node instance = null;
            try
            {
                instance = (Node)ScriptableObject.CreateInstance(type);
                JsonObject payload = GraphJson.UnwrapPayload(instance, out string _);
                foreach (string key in GraphJson.InternalFieldNames)
                {
                    payload.Remove(key);
                }

                return payload;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("SaintsGraph: could not read defaults of " + type.Name + " — " + exception.Message);
                return null;
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        private static string TypeString(Type type)
        {
            return type.FullName + ", " + type.Assembly.GetName().Name;
        }
    }
}
