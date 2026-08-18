using System;
using UnityEngine;

namespace SaintsGraph
{
    /// <summary>
    /// Overrides the value type of the port generated for this field.
    /// </summary>
    /// <remarks>
    /// xNode declares its counterpart in the global namespace; this one lives in
    /// <c>SaintsGraph</c> so both packages can be installed side by side during migration.
    /// Code that already has <c>using SaintsGraph;</c> keeps compiling unchanged — but while
    /// xNode is also installed, an unqualified <c>[PortTypeOverride]</c> binds to xNode's
    /// global-namespace attribute (the global namespace wins over imported ones), so write
    /// <c>[SaintsGraph.PortTypeOverride(...)]</c> until xNode is removed. The migration
    /// assembly warns when it finds such fields.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field)]
    public class PortTypeOverrideAttribute : Attribute
    {
        public Type type;

        public PortTypeOverrideAttribute(Type type)
        {
            this.type = type;
        }
    }

    /// <summary>Marks an enum field to be drawn with a graph-friendly enum popup.</summary>
    public class NodeEnumAttribute : PropertyAttribute
    {
    }
}
