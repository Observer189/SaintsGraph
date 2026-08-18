using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using XNodeGraph = XNode.NodeGraph;

namespace SaintsGraph.Editor.XNodeMigration
{
    internal static class XNodeMigrationCommands
    {
        [MenuItem("Assets/SaintsGraph/Migrate xNode Graph")]
        private static void MigrateSelected()
        {
            StringBuilder report = new StringBuilder();
            foreach (XNodeGraph graph in SelectedXNodeGraphs())
            {
                try
                {
                    string path = XNodeGraphMigrator.WriteSidecar(graph);
                    report.AppendLine("• " + path);
                }
                catch (System.Exception exception)
                {
                    Debug.LogException(exception, graph);
                }
            }

            if (report.Length == 0)
            {
                return;
            }

            Debug.Log("SaintsGraph: migrated xNode graph(s) to JSON:\n" + report +
                      "\nNext steps:\n" +
                      "1. Switch your node and graph classes from 'using XNode;' to 'using SaintsGraph;'.\n" +
                      "2. Create a new SaintsGraph asset with the SAME name in the SAME folder as the JSON " +
                      "(the old xNode asset can be deleted or moved away).\n" +
                      "3. Right-click that asset → SaintsGraph → Import Graph JSON.");
        }

        [MenuItem("Assets/SaintsGraph/Migrate xNode Graph", true)]
        private static bool MigrateSelectedValidate()
        {
            return SelectedXNodeGraphs().Any();
        }

        [MenuItem("Tools/SaintsGraph/Migrate All xNode Graphs")]
        private static void MigrateAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(XNodeGraph));
            int migrated = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                XNodeGraph graph = AssetDatabase.LoadAssetAtPath<XNodeGraph>(path);
                if (graph == null)
                {
                    continue;
                }

                try
                {
                    XNodeGraphMigrator.WriteSidecar(graph);
                    migrated++;
                }
                catch (System.Exception exception)
                {
                    Debug.LogException(exception, graph);
                }
            }

            Debug.Log($"SaintsGraph: migrated {migrated} xNode graph(s) to JSON sidecars. " +
                      "Re-base your node classes on SaintsGraph, then import the sidecars into new graph assets.");
        }

        private static XNodeGraph[] SelectedXNodeGraphs()
        {
            return Selection.objects.OfType<XNodeGraph>().ToArray();
        }
    }
}
