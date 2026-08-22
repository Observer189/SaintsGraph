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

        /// <summary>Lets this package's own tests assert against their fixture node types.</summary>
        internal static bool IncludeTestAssemblies { get; set; }

        private static readonly string[] HowTo =
        {
            "A graph document is {\"format\":\"saintsgraph/1\", \"nodes\":[...], \"edges\":[...]}.",
            "Each node is {\"id\", \"name\", \"type\", \"position\":[x,y], \"fields\":{...}}; \"type\" must be copied verbatim from this schema.",
            "\"id\" is a handle used only inside the document — any unique string works. \"uid\" is optional and identifies an existing node across renames.",
            "\"fields\" uses Unity's own JSON shape; copy \"defaults\" from this schema and change what you need. Omitted fields keep their current value.",
            "Each edge is [outputNodeId, outputPortName, inputNodeId, inputPortName] and must connect an output port to an input port of compatible type.",
            "Ports marked \"dynamicList\": true are per-element ports named \"<field> <index>\", e.g. \"terms 0\"; add the elements to the backing array field too.",
            "Fields listed under \"managedReferences\" are polymorphic ([SerializeReference]). To set one, write {\"rid\": N} with any unique number N plus a sibling \"references\" block {\"version\":2, \"RefIds\":[{\"rid\":N, \"type\":{\"class\":\"...\",\"ns\":\"...\",\"asm\":\"...\"}, \"data\":{...}}]}; leave the field null to keep it empty.",
            "A whole document can be pasted directly into an open graph window with Ctrl+V, or imported over a graph asset via Assets/SaintsGraph/Import Graph JSON."
        };

        /// <summary>
        /// Test assemblies are loaded in the editor, so their fixture node types would otherwise
        /// appear as usable content. Anything referencing NUnit is a test assembly.
        /// </summary>
        private static bool IsTestAssembly(Assembly assembly)
        {
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name == "nunit.framework")
                {
                    return true;
                }
            }

            return false;
        }

        public static string Export()
        {
            Dictionary<Assembly, bool> testAssemblies = new Dictionary<Assembly, bool>();

            bool Include(Type type)
            {
                if (type.IsAbstract)
                {
                    return false;
                }

                if (IncludeTestAssemblies)
                {
                    return true;
                }

                if (!testAssemblies.TryGetValue(type.Assembly, out bool isTest))
                {
                    testAssemblies[type.Assembly] = isTest = IsTestAssembly(type.Assembly);
                }

                return !isTest;
            }

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
                if (Include(type))
                {
                    graphTypes.Items.Add(DescribeGraph(type));
                }
            }

            root["graphTypes"] = graphTypes;

            JsonArray nodeTypes = new JsonArray();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<Node>())
            {
                if (Include(type))
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

            JsonArray managedReferences = DescribeManagedReferences(type);
            if (managedReferences.Items.Count > 0)
            {
                result["managedReferences"] = managedReferences;
            }

            JsonObject defaults = DescribeDefaults(type);
            if (defaults != null && defaults.Entries.Count > 0)
            {
                result["defaults"] = defaults;
            }

            return result;
        }

        /// <summary>
        /// Polymorphic ([SerializeReference]) fields with the concrete types that may be assigned.
        /// Without this a generator cannot know what a null reference field is allowed to become.
        /// </summary>
        private static JsonArray DescribeManagedReferences(Type nodeType)
        {
            JsonArray result = new JsonArray();
            foreach (FieldInfo field in PortCache.GetNodeFields(nodeType))
            {
                if (field.GetCustomAttribute<SerializeReference>() == null)
                {
                    continue;
                }

                Type declared = PortCache.GetListElementType(field.FieldType);
                JsonObject entry = new JsonObject
                {
                    ["field"] = new JsonString(field.Name),
                    ["declaredType"] = new JsonString(declared.FullName)
                };

                if (field.FieldType != declared)
                {
                    entry["list"] = new JsonBool(true);
                }

                JsonArray candidates = new JsonArray();
                foreach (Type candidate in TypeCache.GetTypesDerivedFrom(declared))
                {
                    if (!candidate.IsAbstract && !candidate.IsInterface
                        && candidate.GetCustomAttribute<SerializableAttribute>() != null)
                    {
                        candidates.Items.Add(new JsonString(TypeString(candidate)));
                    }
                }

                entry["assignableTypes"] = candidates;
                result.Items.Add(entry);
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

                // Managed reference ids only mean something inside the document that defines them,
                // so a default rid would be a trap to copy. Such fields are shown as null, and
                // "managedReferences" says what may go there instead.
                payload.Remove("references");
                JsonObject cleaned = new JsonObject();
                foreach (KeyValuePair<string, JsonValue> entry in payload.Entries)
                {
                    cleaned[entry.Key] = StripManagedReferenceIds(entry.Value);
                }

                return cleaned;
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

        private static JsonValue StripManagedReferenceIds(JsonValue value)
        {
            switch (value)
            {
                case JsonObject obj when obj.Entries.Count == 1 && obj.Entries[0].Key == "rid":
                    return JsonNull.Instance;
                case JsonObject obj:
                {
                    JsonObject copy = new JsonObject();
                    foreach (KeyValuePair<string, JsonValue> entry in obj.Entries)
                    {
                        copy[entry.Key] = StripManagedReferenceIds(entry.Value);
                    }

                    return copy;
                }
                case JsonArray array:
                {
                    JsonArray copy = new JsonArray();
                    foreach (JsonValue item in array.Items)
                    {
                        copy.Items.Add(StripManagedReferenceIds(item));
                    }

                    return copy;
                }
                default:
                    return value;
            }
        }

        private static string TypeString(Type type)
        {
            return type.FullName + ", " + type.Assembly.GetName().Name;
        }
    }
}
