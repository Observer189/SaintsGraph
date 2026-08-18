using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SaintsGraph.Editor.XNodeMigration
{
    /// <summary>
    /// While xNode is installed, an unqualified <c>[PortTypeOverride]</c> on a SaintsGraph node
    /// silently binds to xNode's global-namespace attribute (C# resolves global-namespace types
    /// before imported ones), so SaintsGraph would ignore it. Rather than let that pass
    /// unnoticed, point it out once per domain reload.
    /// </summary>
    internal static class XNodeAttributeConflictCheck
    {
        [InitializeOnLoadMethod]
        private static void Check()
        {
            List<string> offenders = new List<string>();
            foreach (FieldInfo field in TypeCache.GetFieldsWithAttribute<global::PortTypeOverrideAttribute>())
            {
                if (field.DeclaringType != null && typeof(Node).IsAssignableFrom(field.DeclaringType))
                {
                    offenders.Add(field.DeclaringType.Name + "." + field.Name);
                }
            }

            if (offenders.Count == 0)
            {
                return;
            }

            Debug.LogWarning(
                "SaintsGraph: these fields use xNode's [PortTypeOverride], which SaintsGraph ignores: "
                + string.Join(", ", offenders)
                + ". While both packages are installed, write [SaintsGraph.PortTypeOverride(...)] "
                + "explicitly — the unqualified name resolves to xNode's global-namespace attribute. "
                + "Once xNode is removed, the unqualified form works again.");
        }
    }
}
