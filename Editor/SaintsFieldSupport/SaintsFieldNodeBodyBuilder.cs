using System;
using System.Collections.Generic;
using SaintsField.Editor;
using SaintsField.Editor.Playa;
using SaintsField.Editor.Playa.Renderer.BaseRenderer;
using UnityEditor;
using UnityEngine.UIElements;

namespace SaintsGraph.Editor.SaintsFieldSupport
{
    /// <summary>
    /// Renders node bodies through SaintsField's member-renderer engine, enabling
    /// [Button], [ShowIf], layout groups, [ShowInInspector] and the rest of the
    /// SaintsEditor feature set inside nodes. This assembly only compiles when the
    /// SaintsField package (≥ 5.25.0) is installed; define
    /// SAINTSGRAPH_SAINTSFIELD_DISABLE to opt out.
    /// </summary>
    internal class SaintsFieldNodeBodyBuilder : INodeBodyBuilder, IMakeRenderer
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            NodeBodyBuilderRegistry.builder = new SaintsFieldNodeBodyBuilder();
        }

        public VisualElement Build(SaintsNodeEditor editor, ICollection<string> skipFields, out Action teardown)
        {
            IReadOnlyList<ISaintsRenderer> renderers = SaintsEditor.Setup(
                skipFields, editor.serializedObject, this, new object[] { editor.target });

            VisualElement root = new VisualElement();
            foreach (ISaintsRenderer renderer in renderers)
            {
                VisualElement element = renderer.CreateVisualElement(root);
                if (element != null)
                {
                    root.Add(element);
                }
            }

            teardown = () =>
            {
                foreach (ISaintsRenderer renderer in renderers)
                {
                    renderer.OnDestroy();
                }
            };
            return root;
        }

        public IEnumerable<IReadOnlyList<AbsRenderer>> MakeRenderer(SerializedObject serializedObject,
            SaintsFieldWithInfo fieldWithInfo)
        {
            return SaintsEditor.HelperMakeRenderer(serializedObject, fieldWithInfo);
        }
    }
}
