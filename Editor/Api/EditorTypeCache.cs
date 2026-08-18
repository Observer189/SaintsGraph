using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace SaintsGraph.Editor
{
    /// <summary>
    /// Resolves custom editors by [CustomNodeEditor]/[CustomNodeGraphEditor] attribute,
    /// walking up the target's base-type chain (same lookup contract as xNode).
    /// Editor instances are owned by their views — no static per-target cache to leak.
    /// </summary>
    internal static class EditorTypeCache
    {
        private static Dictionary<Type, Type> _nodeEditorTypes;
        private static Dictionary<Type, Type> _graphEditorTypes;

        public static SaintsNodeEditor CreateNodeEditor(Node target)
        {
            _nodeEditorTypes = _nodeEditorTypes ?? Build<SaintsNodeEditor>(editorType =>
                editorType.GetCustomAttribute<SaintsNodeEditor.CustomNodeEditorAttribute>()?.inspectedType);
            Type editor = Resolve(_nodeEditorTypes, target.GetType(), typeof(Node));
            return editor != null ? (SaintsNodeEditor)Activator.CreateInstance(editor) : new SaintsNodeEditor();
        }

        public static SaintsGraphEditor CreateGraphEditor(NodeGraph target)
        {
            _graphEditorTypes = _graphEditorTypes ?? Build<SaintsGraphEditor>(editorType =>
                editorType.GetCustomAttribute<SaintsGraphEditor.CustomNodeGraphEditorAttribute>()?.inspectedType);
            Type editor = Resolve(_graphEditorTypes, target.GetType(), typeof(NodeGraph));
            return editor != null ? (SaintsGraphEditor)Activator.CreateInstance(editor) : new SaintsGraphEditor();
        }

        private static Dictionary<Type, Type> Build<TEditor>(Func<Type, Type> inspectedTypeSelector)
        {
            Dictionary<Type, Type> result = new Dictionary<Type, Type>();
            foreach (Type editorType in TypeCache.GetTypesDerivedFrom<TEditor>())
            {
                if (editorType.IsAbstract)
                {
                    continue;
                }

                Type inspected = inspectedTypeSelector(editorType);
                if (inspected != null)
                {
                    result[inspected] = editorType;
                }
            }

            return result;
        }

        private static Type Resolve(Dictionary<Type, Type> editors, Type targetType, Type stopAt)
        {
            for (Type type = targetType; type != null && stopAt.IsAssignableFrom(type); type = type.BaseType)
            {
                if (editors.TryGetValue(type, out Type editorType))
                {
                    return editorType;
                }
            }

            return null;
        }
    }
}
