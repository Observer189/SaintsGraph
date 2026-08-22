using System;
using UnityEditor;
using UnityEngine;

namespace SaintsGraph.Editor
{
    /// <summary>How connections are drawn between ports.</summary>
    public enum NoodleStyle
    {
        /// <summary>Horizontal stubs at both ports joined by a diagonal, corners rounded — GraphView's own look.</summary>
        Rounded,

        /// <summary>Bezier curve leaving each port horizontally.</summary>
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
        private const string SnapKey = "SaintsGraph.SnapCells";

        /// <summary>
        /// Size of one grid cell, in graph units. The canvas draws its grid at this spacing
        /// (see SaintsGraphView.uss), so snapping in whole cells lands nodes on the lines you see.
        /// </summary>
        public const float GridCell = 20f;

        /// <summary>Raised when a preference changes, so open graph windows can follow along.</summary>
        public static event Action Changed;

        public static NoodleStyle NoodleStyle
        {
            get => (NoodleStyle)EditorPrefs.GetInt(StyleKey, (int)NoodleStyle.Rounded);
            set => Set(StyleKey, (int)value, (int)NoodleStyle);
        }

        /// <summary>How many grid cells a dragged node snaps to. Zero means no snapping.</summary>
        public static int SnapCells
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(SnapKey, 0), 0, 8);
            set => Set(SnapKey, Mathf.Clamp(value, 0, 8), SnapCells);
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
            int cells = SnapCells;
            if (cells <= 0)
            {
                return position;
            }

            float step = GridCell * cells;
            return new Vector2(Mathf.Round(position.x / step) * step, Mathf.Round(position.y / step) * step);
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

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Canvas", EditorStyles.boldLabel);
                    SnapCells = EditorGUILayout.IntSlider(
                        new GUIContent("Snap to grid (cells)",
                            "Dragged nodes snap to this many cells of the grid drawn on the canvas. 0 disables snapping."),
                        SnapCells, 0, 8);

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
