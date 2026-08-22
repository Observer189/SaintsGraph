using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SaintsGraph.Editor
{
    internal static class GraphJsonCommands
    {
        private const string AutoExportMenu = "Tools/SaintsGraph/Auto Export Graph JSON";
        private const string AutoImportMenu = "Tools/SaintsGraph/Auto Import Graph JSON";

        private static string PrefKey => "SaintsGraph.AutoExportJson." + PlayerSettings.productGUID;
        private static string ImportPrefKey => "SaintsGraph.AutoImportJson." + PlayerSettings.productGUID;

        internal static bool AutoExport
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        /// <summary>When on, editing a sidecar outside Unity applies it to the graph on the next refresh.</summary>
        internal static bool AutoImport
        {
            get => EditorPrefs.GetBool(ImportPrefKey, false);
            set => EditorPrefs.SetBool(ImportPrefKey, value);
        }

        [MenuItem(AutoImportMenu)]
        private static void ToggleAutoImport()
        {
            AutoImport = !AutoImport;
        }

        [MenuItem(AutoImportMenu, true)]
        private static bool ToggleAutoImportValidate()
        {
            Menu.SetChecked(AutoImportMenu, AutoImport);
            return true;
        }

        [MenuItem("Tools/SaintsGraph/Copy Node Schema for Tools or LLM")]
        private static void CopySchema()
        {
            string schema = GraphSchema.Export();
            EditorGUIUtility.systemCopyBuffer = schema;
            Debug.Log($"SaintsGraph: node schema copied to clipboard ({schema.Length} chars). " +
                      "Paste it to a tool or model, then paste the graph JSON it produces into a graph window.");
        }

        [MenuItem("Tools/SaintsGraph/Export Node Schema for Tools or LLM...")]
        private static void ExportSchema()
        {
            string path = EditorUtility.SaveFilePanel("Export SaintsGraph node schema",
                Application.dataPath, "saintsgraph.schema", "json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            File.WriteAllText(path, GraphSchema.Export());
            AssetDatabase.Refresh();
            Debug.Log("SaintsGraph: node schema written to " + path);
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

        [MenuItem("Assets/SaintsGraph/Copy Graph JSON to Clipboard")]
        private static void CopyGraphJson()
        {
            NodeGraph graph = SelectedGraphs().FirstOrDefault();
            if (graph == null)
            {
                return;
            }

            string json = GraphJson.Export(graph);
            EditorGUIUtility.systemCopyBuffer = json;
            Debug.Log($"SaintsGraph: '{graph.name}' copied as JSON ({json.Length} chars). " +
                      "It can be pasted into any graph window with Ctrl+V, or shared as text.", graph);
        }

        [MenuItem("Assets/SaintsGraph/Copy Graph JSON to Clipboard", true)]
        private static bool CopyGraphJsonValidate()
        {
            return SelectedGraphs().Any();
        }

        /// <summary>Imports a document that is not named after the asset — e.g. one produced elsewhere.</summary>
        [MenuItem("Assets/SaintsGraph/Import Graph JSON from File...")]
        private static void ImportFromFile()
        {
            NodeGraph graph = SelectedGraphs().FirstOrDefault();
            if (graph == null)
            {
                return;
            }

            string path = EditorUtility.OpenFilePanel("Import graph JSON into '" + graph.name + "'",
                Application.dataPath, "json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                GraphJson.Import(graph, File.ReadAllText(path));
                Debug.Log($"SaintsGraph: imported {path} into '{graph.name}'", graph);
                ReloadOpenWindows(graph);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"SaintsGraph: could not import {path} — {exception.Message}", graph);
            }
        }

        [MenuItem("Assets/SaintsGraph/Import Graph JSON from File...", true)]
        private static bool ImportFromFileValidate()
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

        internal static void ReloadOpenWindows(NodeGraph graph)
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

    /// <summary>
    /// With auto-import enabled, a sidecar edited outside Unity is applied to its graph as soon as
    /// Unity reimports the file. Sidecars whose content already matches the graph are skipped, so
    /// our own exports never bounce back as imports.
    /// </summary>
    internal class GraphJsonImportHook : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (!GraphJsonCommands.AutoImport)
            {
                return;
            }

            foreach (string path in importedAssets)
            {
                if (!path.EndsWith(GraphJson.SidecarSuffix, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string assetPath = path.Substring(0, path.Length - GraphJson.SidecarSuffix.Length) + ".asset";
                NodeGraph graph = AssetDatabase.LoadAssetAtPath<NodeGraph>(assetPath);
                if (graph == null)
                {
                    continue;
                }

                try
                {
                    string json = File.ReadAllText(path);
                    if (json == GraphJson.Export(graph))
                    {
                        continue;
                    }

                    GraphJson.Import(graph, json);
                    GraphJsonCommands.ReloadOpenWindows(graph);
                    Debug.Log($"SaintsGraph: applied external changes from {path}", graph);
                }
                catch (System.Exception exception)
                {
                    Debug.LogError($"SaintsGraph: could not apply {path} — {exception.Message}", graph);
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
