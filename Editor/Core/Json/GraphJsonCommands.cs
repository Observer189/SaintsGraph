using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SaintsGraph.Editor
{
    internal static class GraphJsonCommands
    {
        private const string AutoExportMenu = "Tools/SaintsGraph/Auto Export Graph JSON";

        private static string PrefKey => "SaintsGraph.AutoExportJson." + PlayerSettings.productGUID;

        internal static bool AutoExport
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem(AutoExportMenu)]
        private static void ToggleAutoExport()
        {
            AutoExport = !AutoExport;
            if (AutoExport)
            {
                foreach (NodeGraph graph in SelectedGraphs())
                {
                    GraphJson.ExportToFile(graph);
                }
            }
        }

        [MenuItem(AutoExportMenu, true)]
        private static bool ToggleAutoExportValidate()
        {
            Menu.SetChecked(AutoExportMenu, AutoExport);
            return true;
        }

        [MenuItem("Assets/SaintsGraph/Export Graph JSON")]
        private static void ExportSelected()
        {
            foreach (NodeGraph graph in SelectedGraphs())
            {
                GraphJson.ExportToFile(graph);
                Debug.Log("Exported " + GraphJson.SidecarPathFor(graph), graph);
            }
        }

        [MenuItem("Assets/SaintsGraph/Export Graph JSON", true)]
        private static bool ExportSelectedValidate()
        {
            return SelectedGraphs().Any();
        }

        [MenuItem("Assets/SaintsGraph/Import Graph JSON")]
        private static void ImportSelected()
        {
            foreach (NodeGraph graph in SelectedGraphs())
            {
                string path = GraphJson.SidecarPathFor(graph);
                if (path == null || !File.Exists(path))
                {
                    Debug.LogWarning("No sidecar found for " + graph.name, graph);
                    continue;
                }

                GraphJson.Import(graph, File.ReadAllText(path));
                // Normalize the sidecar so hand edits settle into canonical formatting.
                GraphJson.ExportToFile(graph);
                Debug.Log("Imported " + path, graph);
                ReloadOpenWindows(graph);
            }
        }

        [MenuItem("Assets/SaintsGraph/Import Graph JSON", true)]
        private static bool ImportSelectedValidate()
        {
            return SelectedGraphs().Any(graph =>
            {
                string path = GraphJson.SidecarPathFor(graph);
                return path != null && File.Exists(path);
            });
        }

        private static NodeGraph[] SelectedGraphs()
        {
            return Selection.objects.OfType<NodeGraph>().ToArray();
        }

        private static void ReloadOpenWindows(NodeGraph graph)
        {
            foreach (SaintsGraphWindow window in Resources.FindObjectsOfTypeAll<SaintsGraphWindow>())
            {
                if (window.Graph == graph)
                {
                    window.ReloadViewFromModel();
                }
            }
        }
    }

    /// <summary>With auto-export enabled, every saved graph asset refreshes its JSON sidecar.</summary>
    internal class GraphJsonSaveHook : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            if (GraphJsonCommands.AutoExport)
            {
                foreach (string path in paths)
                {
                    if (!path.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    NodeGraph graph = AssetDatabase.LoadAssetAtPath<NodeGraph>(path);
                    if (graph != null)
                    {
                        try
                        {
                            GraphJson.ExportToFile(graph, importAsset: false);
                        }
                        catch (System.Exception exception)
                        {
                            Debug.LogException(exception);
                        }
                    }
                }
            }

            return paths;
        }
    }
}
