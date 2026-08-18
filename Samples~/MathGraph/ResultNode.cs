using SaintsGraph;

namespace SaintsGraphSamples
{
    /// <summary>The graph's output. Only one is allowed per graph.</summary>
    [Node.CreateNodeMenu("Math/Result")]
    [Node.DisallowMultipleNodes]
    public class ResultNode : Node
    {
        [Input] public float result;

        /// <summary>Evaluates the whole upstream graph, on demand.</summary>
        public float GetResult()
        {
            return GetInputValue("result", result);
        }

        public override object GetValue(NodePort port)
        {
            return GetResult();
        }
    }
}
