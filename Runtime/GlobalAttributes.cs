using System;
using UnityEngine;

// These two attributes intentionally live in the global namespace, mirroring xNode's public API.

/// <summary>Overrides the value type of the port generated for this field.</summary>
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
