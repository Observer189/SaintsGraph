using System;
using UnityEditor;
using UnityEngine;

namespace SaintsGraph.Editor
{
    public static class NodeEditorUtilities
    {
        /// <summary>Default display name of a node type: "AddValueNode" → "Add Value".</summary>
        public static string NodeDefaultName(Type type)
        {
            string name = type.Name;
            if (name.EndsWith("Node") && name.Length > 4)
            {
                name = name.Substring(0, name.Length - 4);
            }

            return ObjectNames.NicifyVariableName(name);
        }

        /// <summary>Default create-menu path of a node type: namespace segments + default name.</summary>
        public static string NodeDefaultPath(Type type)
        {
            string name = NodeDefaultName(type);
            return string.IsNullOrEmpty(type.Namespace)
                ? name
                : type.Namespace.Replace('.', '/') + "/" + name;
        }

        /// <summary>Deterministic per-type color used for ports.</summary>
        public static Color GetTypeColor(Type type)
        {
            if (type == null)
            {
                return Color.gray;
            }

            int hash = 17;
            unchecked
            {
                foreach (char c in type.FullName)
                {
                    hash = hash * 31 + c;
                }
            }

            float hue = Mathf.Abs(hash % 360) / 360f;
            return Color.HSVToRGB(hue, 0.65f, 0.9f);
        }

        /// <summary>
        /// True when a node type declares a static port that could accept <paramref name="source"/>.
        /// Used to filter the create menu while dragging a connection; the exact policy is still
        /// enforced by <see cref="SaintsGraphEditor.CanConnect"/> when the edge is made.
        /// </summary>
        internal static bool HasCompatiblePort(Type nodeType, NodePort source)
        {
            foreach (PortCache.PortTemplate template in PortCache.GetTemplates(nodeType))
            {
                if (template.dynamicPortList || (template.direction == NodePort.IO.Input) == source.IsInput)
                {
                    continue;
                }

                Type outputType = source.IsOutput ? source.ValueType : template.valueType;
                Type inputType = source.IsOutput ? template.valueType : source.ValueType;
                if (outputType == null || inputType == null
                    || inputType.IsAssignableFrom(outputType) || outputType.IsAssignableFrom(inputType))
                {
                    return true;
                }
            }

            return false;
        }

        internal static Node.ShowBackingValue GetBackingValue(Node node, string fieldName)
        {
            foreach (PortCache.PortTemplate template in PortCache.GetTemplates(node.GetType()))
            {
                if (template.fieldName == fieldName)
                {
                    return template.backingValue;
                }
            }

            return Node.ShowBackingValue.Never;
        }
    }
}
