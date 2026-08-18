using SaintsGraph;

namespace SaintsGraphSamples
{
    /// <summary>
    /// Pull evaluation: GetInputValue walks upstream and asks the connected node for its
    /// value, falling back to the backing field when the port is not connected.
    /// </summary>
    [Node.CreateNodeMenu("Math/Add")]
    public class AddNode : Node
    {
        [Input] public float a;
        [Input] public float b;
        [Output] public float sum;

        public override object GetValue(NodePort port)
        {
            return GetInputValue("a", a) + GetInputValue("b", b);
        }
    }
}
