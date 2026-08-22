using System;
using UnityEditor;
using UnityEngine;

namespace SaintsGraph.Editor
{
    /// <summary>How connections are drawn between ports.</summary>
    public enum NoodleStyle
    {
        /// <summary>Bezier curve leaving each port horizontally. The familiar node-graph look.</summary>
        Curvy,

        /// <summary>Right-angle routing with softened corners.</summary>
        Angled,

        /// <summary>A direct line from port to port.</summary>
        Straight
    }

    /// <summary>
    /// User preferences for the graph editor, in Preferences → SaintsGraph. These are per-user
    /// view settings, so they live in EditorPrefs rather than in the graph asset.
    /// </summary>
    public static class SaintsGraphPreferences
    {
        private const string StyleKey = "SaintsGraph.NoodleStyle";
        private const string ThicknessKey = "SaintsGraph.NoodleThickness";
        private const string SnapKey = "SaintsGraph.GridSnap";

        /// <summary>Raised when a preference changes, so open graph windows can follow along.</summary>
        public static event Action Changed;

        public static NoodleStyle NoodleStyle
        {
            get => (NoodleStyle)EditorPrefs.GetInt(StyleKey, (int)NoodleStyle.Curvy);
            set => Set(StyleKey, (int)value, (int)NoodleStyle);
        }

        public static float NoodleThickness
        {
            get => Mathf.Clamp(EditorPrefs.GetFloat(ThicknessKey, 2f), 1f, 10f);
            set
            {
                float clamped = Mathf.Clamp(value, 1f, 10f);
                if (!Mathf.Approximately(clamped, NoodleThickness))
                {
                    EditorPrefs.SetFloat(ThicknessKey, clamped);
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>Grid step nodes snap to while being dragged. Zero disables snapping.</summary>
        public static float GridSnap
        {
            get => Mathf.Max(0f, EditorPrefs.GetFloat(SnapKey, 0f));
            set
            {
                float clamped = Mathf.Max(0f, value);
                if (!Mathf.Approximately(clamped, GridSnap))
                {
                    EditorPrefs.SetFloat(SnapKey, clamped);
                    Changed?.Invoke();
                }
            }
        }

        private static void Set(string key, int value, int current)
        {
            if (value != current)
            {
                EditorPrefs.SetInt(key, value);
                Changed?.Invoke();
            }
        }

        /// <summary>Applies the grid snap, if any, to a node position.</summary>
        public static Vector2 Snap(Vector2 position)
        {
            float step = GridSnap;
            return step <= 0f
                ? position
                : new Vector2(Mathf.Round(position.x / step) * step, Mathf.Round(position.y / step) * step);
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Preferences/SaintsGraph", SettingsScope.User)
            {
                label = "SaintsGraph",
                keywords = new[] { "graph", "node", "noodle", "edge", "grid", "snap", "saintsgraph" },
                guiHandler = _ =>
                {
                    EditorGUIUtility.labelWidth = 180f;
                    EditorGUILayout.LabelField("Connections", EditorStyles.boldLabel);
                    NoodleStyle = (NoodleStyle)EditorGUILayout.EnumPopup("Style", NoodleStyle);
                    NoodleThickness = EditorGUILayout.Slider("Thickness", NoodleThickness, 1f, 10f);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Canvas", EditorStyles.boldLabel);
                    GridSnap = EditorGUILayout.Slider(
                        new GUIContent("Grid snap", "Step nodes snap to when dragged. 0 disables snapping."),
                        GridSnap, 0f, 100f);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("JSON sidecar", EditorStyles.boldLabel);
                    GraphJsonCommands.AutoExport = EditorGUILayout.Toggle(
                        new GUIContent("Auto export on save", "Refresh <Graph>.graph.json whenever the asset is saved."),
                        GraphJsonCommands.AutoExport);
                    GraphJsonCommands.AutoImport = EditorGUILayout.Toggle(
                        new GUIContent("Auto import external edits",
                            "Apply a sidecar edited outside Unity as soon as it is reimported."),
                        GraphJsonCommands.AutoImport);
                }
            };
        }
    }
}
