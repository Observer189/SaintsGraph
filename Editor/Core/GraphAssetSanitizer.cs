using UnityEditor;
using UnityEngine;

namespace SaintsGraph.Editor
{
    /// <summary>
    /// Keeps graph assets and their node sub-assets consistent. Undo can leave a graph in
    /// states the Project browser chokes on (NullReferenceException in ObjectListArea):
    /// undone creation leaves a destroyed node in the .asset file until the next save;
    /// undone deletion resurrects a node object that is no longer part of the file.
    /// This runs after domain reload and after every undo/redo in the graph window.
    /// </summary>
    internal static class GraphAssetSanitizer
    {
        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            EditorApplication.delayCall += SanitizeAllGraphs;
        }

        private static void SanitizeAllGraphs()
        {
            bool anyChanged = false;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(NodeGraph)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                NodeGraph graph = AssetDatabase.LoadAssetAtPath<NodeGraph>(path);
                if (graph != null)
                {
                    anyChanged |= Sanitize(graph);
                }
            }

            if (anyChanged)
            {
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>Returns true when something had to be repaired.</summary>
        public static bool Sanitize(NodeGraph graph)
        {
            bool changed = graph.nodes.RemoveAll(node => node == null) > 0;

            int edgesBefore = graph.Edges.Count;
            graph.PruneInvalidEdges();
            changed |= graph.Edges.Count != edgesBefore;

            string path = AssetDatabase.GetAssetPath(graph);
            if (!string.IsNullOrEmpty(path))
            {
                // Undone deletion: the node object exists again but is no longer part of the file.
                foreach (Node node in graph.nodes)
                {
                    if (node != null && !AssetDatabase.Contains(node))
                    {
                        AssetDatabase.AddObjectToAsset(node, graph);
                        changed = true;
                    }
                }

                // Orphaned node sub-assets (crash/undo leftovers): re-adopt instead of deleting user data.
                foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (sub is Node subNode && !graph.nodes.Contains(subNode))
                    {
                        graph.nodes.Add(subNode);
                        subNode.graph = graph;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(graph);
            }

            return changed;
        }
    }
}
