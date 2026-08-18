using System;

namespace SaintsGraph.Editor
{
    /// <summary>
    /// Model-side operations for dynamic port lists ("{field} {index}" element ports),
    /// mirroring xNode's ReorderableList semantics: connections follow elements on
    /// reorder, and removal shifts later connections down to keep indices contiguous.
    /// The caller is responsible for the backing array/List and for undo records.
    /// </summary>
    internal static class DynamicPortListOps
    {
        public static string ElementName(string fieldName, int index)
        {
            return fieldName + " " + index;
        }

        public static int CountElements(Node node, string fieldName)
        {
            int count = 0;
            while (node.HasPort(ElementName(fieldName, count)))
            {
                count++;
            }

            return count;
        }

        public static NodePort AddElement(Node node, PortCache.PortTemplate template)
        {
            int index = CountElements(node, template.fieldName);
            Type elementType = PortCache.GetListElementType(template.valueType);
            string name = ElementName(template.fieldName, index);
            return template.direction == NodePort.IO.Input
                ? node.AddDynamicInput(elementType, template.connectionType, template.typeConstraint, name)
                : node.AddDynamicOutput(elementType, template.connectionType, template.typeConstraint, name);
        }

        /// <summary>Removes the element port at <paramref name="index"/>, shifting later connections down one.</summary>
        public static void RemoveElement(Node node, string fieldName, int index)
        {
            int count = CountElements(node, fieldName);
            if (index < 0 || index >= count)
            {
                return;
            }

            node.GetPort(ElementName(fieldName, index))?.ClearConnections();
            for (int i = index + 1; i < count; i++)
            {
                NodePort source = node.GetPort(ElementName(fieldName, i));
                NodePort destination = node.GetPort(ElementName(fieldName, i - 1));
                if (source != null && destination != null)
                {
                    source.MoveConnections(destination);
                }
            }

            NodePort last = node.GetPort(ElementName(fieldName, count - 1));
            if (last != null)
            {
                node.RemoveDynamicPort(last);
            }
        }

        /// <summary>Moves an element between indices; connections travel with their elements.</summary>
        public static void MoveElement(Node node, string fieldName, int from, int to)
        {
            int count = CountElements(node, fieldName);
            if (from == to || from < 0 || to < 0 || from >= count || to >= count)
            {
                return;
            }

            int step = to > from ? 1 : -1;
            for (int i = from; i != to; i += step)
            {
                NodePort a = node.GetPort(ElementName(fieldName, i));
                NodePort b = node.GetPort(ElementName(fieldName, i + step));
                if (a != null && b != null)
                {
                    a.SwapConnections(b);
                }
            }
        }

        /// <summary>
        /// Aligns the element-port count with the backing list size — heals external edits
        /// such as a JSON sidecar import changing the array. Returns true when anything changed.
        /// </summary>
        public static bool Sync(Node node, PortCache.PortTemplate template, int size)
        {
            bool changed = false;
            while (CountElements(node, template.fieldName) < size)
            {
                AddElement(node, template);
                changed = true;
            }

            int current;
            while ((current = CountElements(node, template.fieldName)) > size)
            {
                NodePort last = node.GetPort(ElementName(template.fieldName, current - 1));
                if (last == null)
                {
                    break;
                }

                node.RemoveDynamicPort(last);
                changed = true;
            }

            return changed;
        }
    }
}
