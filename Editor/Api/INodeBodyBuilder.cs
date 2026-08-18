using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace SaintsGraph.Editor
{
    /// <summary>
    /// Pluggable builder for default node bodies. The SaintsField integration assembly
    /// registers one; without it the built-in PropertyField body is used.
    /// </summary>
    public interface INodeBodyBuilder
    {
        /// <summary>
        /// Build the body content for a node, excluding <paramref name="skipFields"/>
        /// (internal fields and port fields whose backing value is hidden).
        /// Return null to fall through to the built-in builder. <paramref name="teardown"/>
        /// is invoked when the node view is rebuilt or the graph view is closed.
        /// </summary>
        VisualElement Build(SaintsNodeEditor editor, ICollection<string> skipFields, out Action teardown);
    }

    public static class NodeBodyBuilderRegistry
    {
        /// <summary>Active builder for default node bodies. Null = built-in PropertyField body.</summary>
        public static INodeBodyBuilder builder;
    }
}
