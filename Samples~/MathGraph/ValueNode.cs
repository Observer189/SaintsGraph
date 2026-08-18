using SaintsGraph;

namespace SaintsGraphSamples
{
    /// <summary>A constant. ShowBackingValue.Always keeps the field editable in the node.</summary>
    [Node.CreateNodeMenu("Math/Value")]
    public class ValueNode : Node
    {
        [Output(ShowBackingValue.Always)] public float value;

        public override object GetValue(NodePort port)
        {
            return value;
        }
    }
}
