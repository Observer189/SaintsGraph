using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace SaintsGraph.Editor
{
    /// <summary>
    /// Replaces GraphView's default edge connector listener so that dropping a connection on
    /// empty canvas opens the create menu filtered to nodes that can accept it, and creates the
    /// connection right away. Dropping on a port keeps the standard behaviour.
    /// </summary>
    internal class PortDropListener : IEdgeConnectorListener
    {
        private readonly SaintsGraphView _view;

        public PortDropListener(SaintsGraphView view)
        {
            _view = view;
        }

        public void OnDropOutsidePort(Edge edge, Vector2 position)
        {
            Port sourcePort = edge.output ?? edge.input;
            if (sourcePort?.userData is NodePort source)
            {
                Vector2 screenPosition = Event.current != null
                    ? GUIUtility.GUIToScreenPoint(Event.current.mousePosition)
                    : GUIUtility.GUIToScreenPoint(position);
                _view.OpenCreateMenuForPort(source, screenPosition);
            }
        }

        /// <summary>
        /// Mirrors GraphView's default drop handling: single-capacity ports drop their existing
        /// edges, the change goes through graphViewChanged, and surviving edges are added.
        /// </summary>
        public void OnDrop(GraphView graphView, Edge edge)
        {
            List<GraphElement> toDelete = new List<GraphElement>();
            if (edge.input != null && edge.input.capacity == Port.Capacity.Single)
            {
                toDelete.AddRange(edge.input.connections.Where(existing => existing != edge));
            }

            if (edge.output != null && edge.output.capacity == Port.Capacity.Single)
            {
                toDelete.AddRange(edge.output.connections.Where(existing => existing != edge));
            }

            if (toDelete.Count > 0)
            {
                graphView.DeleteElements(toDelete);
            }

            List<Edge> edgesToCreate = new List<Edge> { edge };
            if (graphView.graphViewChanged != null)
            {
                edgesToCreate = graphView.graphViewChanged(new GraphViewChange { edgesToCreate = edgesToCreate })
                    .edgesToCreate;
            }

            foreach (Edge created in edgesToCreate)
            {
                graphView.AddElement(created);
                created.input?.Connect(created);
                created.output?.Connect(created);
            }
        }
    }
}
