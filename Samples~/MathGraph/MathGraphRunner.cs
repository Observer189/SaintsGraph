using SaintsGraph;
using UnityEngine;

namespace SaintsGraphSamples
{
    /// <summary>
    /// Evaluating a graph from gameplay code: find the result node and ask for its value.
    /// Evaluation is pull-based and uncached — each call walks the graph upstream.
    /// </summary>
    public class MathGraphRunner : MonoBehaviour
    {
        public MathGraph graph;

        [ContextMenu("Evaluate")]
        public void Evaluate()
        {
            if (graph == null)
            {
                Debug.LogWarning("No graph assigned", this);
                return;
            }

            foreach (Node node in graph.nodes)
            {
                if (node is ResultNode result)
                {
                    Debug.Log($"{graph.name} = {result.GetResult()}", this);
                    return;
                }
            }

            Debug.LogWarning($"{graph.name} has no Result node", this);
        }

        private void Start()
        {
            Evaluate();
        }
    }
}
