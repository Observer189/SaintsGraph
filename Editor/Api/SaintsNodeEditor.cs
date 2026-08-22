using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SaintsGraph.Editor
{
    /// <summary>
    /// Per-node editor extension point, the analogue of xNode's <c>NodeEditor</c>.
    /// Derive and mark with [SaintsNodeEditor.CustomNodeEditor(typeof(MyNode))].
    /// Deliberately exposes no GraphView types, so the canvas backend can change
    /// without breaking user code.
    /// </summary>
    public class SaintsNodeEditor
    {
        [AttributeUsage(AttributeTargets.Class)]
        public class CustomNodeEditorAttribute : Attribute
        {
            public Type inspectedType;

            public CustomNodeEditorAttribute(Type inspectedType)
            {
                this.inspectedType = inspectedType;
            }
        }

        public Node target;
        public SerializedObject serializedObject;
        public SaintsGraphEditor graphEditor;

        /// <summary>Called once after target/serializedObject/graphEditor are assigned.</summary>
        public virtual void OnCreate()
        {
        }

        /// <summary>Custom header content. Return null to use the default title label.</summary>
        public virtual VisualElement CreateHeader()
        {
            return null;
        }

        /// <summary>
        /// Custom body content. Return null to use the default body (serialized fields with
        /// inline ports). When overridden, ports are appended below the returned element so
        /// connections stay accessible.
        /// </summary>
        public virtual VisualElement CreateBody()
        {
            return null;
        }

        /// <summary>Fixed width for this node type, or 0 to size to content (the default).</summary>
        public virtual int GetWidth()
        {
            Node.NodeWidthAttribute attribute = target.GetType().GetCustomAttribute<Node.NodeWidthAttribute>();
            return attribute?.width ?? 0;
        }

        public virtual Color GetTint()
        {
            Node.NodeTintAttribute attribute = target.GetType().GetCustomAttribute<Node.NodeTintAttribute>();
            return attribute?.color ?? (Color)new Color32(90, 97, 105, 255);
        }

        public virtual string GetHeaderTooltip()
        {
            return null;
        }
    }
}
