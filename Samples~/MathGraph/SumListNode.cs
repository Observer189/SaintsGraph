using SaintsGraph;

namespace SaintsGraphSamples
{
    /// <summary>
    /// A dynamic port list: the node shows one port per element, with add / move / remove
    /// buttons. Element ports are named "{field} {index}", matching xNode's convention.
    /// </summary>
    [Node.CreateNodeMenu("Math/Sum List")]
    public class SumListNode : Node
    {
        [Input(dynamicPortList: true)] public float[] terms = new float[0];
        [Output] public float sum;

        public override object GetValue(NodePort port)
        {
            float total = 0f;
            for (int i = 0; i < terms.Length; i++)
            {
                total += GetInputValue("terms " + i, terms[i]);
            }

            return total;
        }
    }
}
