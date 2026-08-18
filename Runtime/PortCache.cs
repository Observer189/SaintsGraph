using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SaintsGraph
{
    /// <summary>
    /// Per-type cache of static port definitions reflected from [Input]/[Output] fields.
    /// Types are cached lazily on first access — no assembly scanning, so unlike xNode
    /// there is no assembly-name filter that can silently skip node types.
    /// </summary>
    internal static class PortCache
    {
        internal class PortTemplate
        {
            public string fieldName;
            public Type valueType;
            public NodePort.IO direction;
            public Node.ConnectionType connectionType;
            public Node.TypeConstraint typeConstraint;
            public Node.ShowBackingValue backingValue;
            public bool dynamicPortList;
        }

        private static readonly Dictionary<Type, List<PortTemplate>> Cache =
            new Dictionary<Type, List<PortTemplate>>();

        public static IReadOnlyList<PortTemplate> GetTemplates(Type nodeType)
        {
            if (Cache.TryGetValue(nodeType, out List<PortTemplate> cached))
            {
                return cached;
            }

            List<PortTemplate> templates = new List<PortTemplate>();
            foreach (FieldInfo field in GetNodeFields(nodeType))
            {
                Node.InputAttribute input = field.GetCustomAttribute<Node.InputAttribute>();
                Node.OutputAttribute output = field.GetCustomAttribute<Node.OutputAttribute>();
                if (input == null && output == null)
                {
                    continue;
                }

                if (input != null && output != null)
                {
                    Debug.LogError(nodeType.Name + "." + field.Name + " cannot be both input and output");
                    continue;
                }

                Type valueType = field.FieldType;
                PortTypeOverrideAttribute typeOverride = field.GetCustomAttribute<PortTypeOverrideAttribute>();
                if (typeOverride != null)
                {
                    valueType = typeOverride.type;
                }

                templates.Add(new PortTemplate
                {
                    fieldName = field.Name,
                    valueType = valueType,
                    direction = input != null ? NodePort.IO.Input : NodePort.IO.Output,
                    connectionType = input?.connectionType ?? output.connectionType,
                    typeConstraint = input?.typeConstraint ?? output.typeConstraint,
                    backingValue = input?.backingValue ?? output.backingValue,
                    dynamicPortList = input?.dynamicPortList ?? output.dynamicPortList
                });
            }

            Cache[nodeType] = templates;
            return templates;
        }

        /// <summary>T[] and List&lt;T&gt; → T; anything else unchanged. Element type of dynamic port list fields.</summary>
        public static Type GetListElementType(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (type.IsArray)
            {
                return type.GetElementType();
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                return type.GetGenericArguments()[0];
            }

            return type;
        }

        /// <summary>
        /// Matches xNode's dynamic port list naming convention "{fieldName} {index}":
        /// true when the port name parses as an element of a [Input/Output(dynamicPortList: true)] field.
        /// </summary>
        public static bool TryGetDynamicListTemplate(Type nodeType, string portName, out PortTemplate template)
        {
            template = null;
            if (string.IsNullOrEmpty(portName))
            {
                return false;
            }

            string[] parts = portName.Split(' ');
            if (parts.Length != 2 || !int.TryParse(parts[1], out _))
            {
                return false;
            }

            foreach (PortTemplate candidate in GetTemplates(nodeType))
            {
                if (candidate.dynamicPortList && candidate.fieldName == parts[0])
                {
                    template = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// All instance fields of a node type, including private fields inherited from base classes
        /// (which <see cref="Type.GetFields()"/> alone would miss).
        /// </summary>
        public static List<FieldInfo> GetNodeFields(Type nodeType)
        {
            List<FieldInfo> fields = new List<FieldInfo>(
                nodeType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
            Type parent = nodeType.BaseType;
            while (parent != null && parent != typeof(Node))
            {
                foreach (FieldInfo field in parent.GetFields(
                             BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (field.IsPrivate && fields.All(existing => existing.Name != field.Name))
                    {
                        fields.Add(field);
                    }
                }

                parent = parent.BaseType;
            }

            return fields;
        }
    }
}
