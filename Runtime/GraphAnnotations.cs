using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaintsGraph
{
    /// <summary>
    /// A named frame around a set of nodes. Membership is what defines a group — its bounds are
    /// derived from the nodes it holds, so only an empty group needs a position of its own.
    /// </summary>
    [Serializable]
    public class NodeGroup
    {
        public string title = "Group";
        public Vector2 position;
        public List<Node> nodes = new List<Node>();
    }

    /// <summary>A free-floating note on the canvas: documentation that lives with the graph.</summary>
    [Serializable]
    public class NodeNote
    {
        public string title = "";
        public string text = "";
        public Rect area = new Rect(0f, 0f, 200f, 160f);

        /// <summary>Matches UnityEditor.Experimental.GraphView.StickyNoteTheme.</summary>
        public int theme;

        /// <summary>Matches UnityEditor.Experimental.GraphView.StickyNoteFontSize.</summary>
        public int fontSize;
    }
}
