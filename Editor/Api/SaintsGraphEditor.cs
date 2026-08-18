using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SaintsGraph.Editor
{
    /// <summary>
    /// Per-graph editor extension point, the analogue of xNode's <c>NodeGraphEditor</c>.
    /// Derive and mark with [SaintsGraphEditor.CustomNodeGraphEditor(typeof(MyGraph))].
    /// </summary>
    public class SaintsGraphEditor
    {
        [AttributeUsage(AttributeTargets.Class)]
        public class CustomNodeGraphEditorAttribute : Attribute
        {
            public Type inspectedType;

            public CustomNodeGraphEditorAttribute(Type inspectedType)
            {
                this.inspectedType = inspectedType;
            }
        }

        public NodeGraph target;
        public SerializedObject serializedObject;

        /// <summary>Called once when the graph is opened in the window.</summary>
        public virtual void OnOpen()
        {
        }

        /// <summary>Create-menu path for a node type. Null or empty hides the type from the menu.</summary>
        public virtual string GetNodeMenuName(Type type)
        {
            Node.CreateNodeMenuAttribute attribute = type.GetCustomAttribute<Node.CreateNodeMenuAttribute>();
            return attribute != null ? attribute.menuName : NodeEditorUtilities.NodeDefaultPath(type);
        }

        public virtual int GetNodeMenuOrder(Type type)
        {
            Node.CreateNodeMenuAttribute attribute = type.GetCustomAttribute<Node.CreateNodeMenuAttribute>();
            return attribute?.order ?? 0;
        }

        /// <summary>Connection policy. Default: the ports' own type constraints.</summary>
        public virtual bool CanConnect(NodePort output, NodePort input)
        {
            return output != null && input != null && output.CanConnectTo(input);
        }

        /// <summary>False for the last remaining node of a type required via [RequireNode].</summary>
        public virtual bool CanRemove(Node node)
        {
            if (node == null)
            {
                return false;
            }

            foreach (NodeGraph.RequireNodeAttribute required in
                     target.GetType().GetCustomAttributes<NodeGraph.RequireNodeAttribute>())
            {
                if (required.Requires(node.GetType())
                    && target.nodes.Count(n => n != null && n.GetType() == node.GetType()) <= 1)
                {
                    return false;
                }
            }

            return true;
        }

        public virtual Node CreateNode(Type type, Vector2 position)
        {
            Undo.RecordObject(target, "Create Node");
            Node node = target.AddNode(type);
            Undo.RegisterCreatedObjectUndo(node, "Create Node");
            node.position = position;
            if (string.IsNullOrEmpty(node.name))
            {
                node.name = NodeEditorUtilities.NodeDefaultName(type);
            }

            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(target)))
            {
                AssetDatabase.AddObjectToAsset(node, target);
            }

            EditorUtility.SetDirty(target);
            return node;
        }

        public virtual void RemoveNode(Node node)
        {
            if (!CanRemove(node))
            {
                return;
            }

            Undo.RecordObject(target, "Remove Node");
            target.RemoveNode(node);
            Undo.DestroyObjectImmediate(node);
            EditorUtility.SetDirty(target);
        }

        public virtual Color GetTypeColor(Type type)
        {
            return NodeEditorUtilities.GetTypeColor(type);
        }

        public virtual Color GetPortColor(NodePort port)
        {
            return GetTypeColor(port?.ValueType);
        }
    }
}
