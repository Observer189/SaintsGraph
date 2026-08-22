using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace SaintsGraph.Editor
{
    /// <summary>Searchable create-node menu fed by [CreateNodeMenu] / default type paths.</summary>
    internal class NodeSearchWindowProvider : ScriptableObject, ISearchWindowProvider
    {
        private SaintsGraphView _view;

        /// <summary>When set, only nodes able to accept this port are listed, and the created node is connected to it.</summary>
        public NodePort pendingPort;

        public void Initialize(SaintsGraphView view)
        {
            _view = view;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            List<SearchTreeEntry> tree = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent(pendingPort == null
                    ? "Create Node"
                    : "Connect to " + ObjectNames.NicifyVariableName(pendingPort.fieldName)))
            };

            List<(string path, Type type)> entries = new List<(string, Type)>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<Node>())
            {
                if (type.IsAbstract)
                {
                    continue;
                }

                string menuName = _view.graphEditor.GetNodeMenuName(type);
                if (string.IsNullOrEmpty(menuName))
                {
                    continue;
                }

                if (pendingPort != null && !NodeEditorUtilities.HasCompatiblePort(type, pendingPort))
                {
                    continue;
                }

                Node.DisallowMultipleNodesAttribute disallow =
                    type.GetCustomAttribute<Node.DisallowMultipleNodesAttribute>();
                if (disallow != null
                    && _view.graph.nodes.Count(n => n != null && n.GetType() == type) >= disallow.max)
                {
                    continue;
                }

                entries.Add((menuName, type));
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.path, b.path));

            HashSet<string> knownGroups = new HashSet<string>();
            foreach ((string path, Type type) in entries)
            {
                string[] parts = path.Split('/');
                string groupPath = "";
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    groupPath += parts[i] + "/";
                    if (knownGroups.Add(groupPath))
                    {
                        tree.Add(new SearchTreeGroupEntry(new GUIContent(parts[i]), i + 1));
                    }
                }

                tree.Add(new SearchTreeEntry(new GUIContent(parts[parts.Length - 1]))
                {
                    level = parts.Length,
                    userData = type
                });
            }

            return tree;
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            _view.CreateNode((Type)entry.userData, context.screenMousePosition, pendingPort);
            pendingPort = null;
            return true;
        }
    }
}
