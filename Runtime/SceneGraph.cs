using UnityEngine;

namespace SaintsGraph
{
    /// <summary>Lets a scene object hold a reference to a graph, so graph nodes can reference scene objects through it.</summary>
    public class SceneGraph : MonoBehaviour
    {
        public NodeGraph graph;
    }

    /// <summary>Typed variant of <see cref="SceneGraph"/>.</summary>
    public class SceneGraph<T> : SceneGraph where T : NodeGraph
    {
        public new T graph
        {
            get => base.graph as T;
            set => base.graph = value;
        }
    }
}
