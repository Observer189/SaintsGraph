using SaintsGraph;

namespace SaintsGraphSamples
{
    /// <summary>Node appearance attributes, plus a strict type constraint on the inputs.</summary>
    [Node.CreateNodeMenu("Math/Multiply")]
    [Node.NodeTint(70, 90, 120)]
    [Node.NodeWidth(190)]
    public class MultiplyNode : Node
    {
        [Input(typeConstraint: TypeConstraint.Strict)] public float a;
        [Input(typeConstraint: TypeConstraint.Strict)] public float b;
        [Output] public float product;

        public override object GetValue(NodePort port)
        {
            return GetInputValue("a", a) * GetInputValue("b", b);
        }
    }
}
