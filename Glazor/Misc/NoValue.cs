namespace Glazor.Misc;

/// <summary>An empty value, for a dictionary used as an ordered set: <c>OrderedDictionary&lt;string, NoValue&gt;</c>.</summary>
public readonly struct NoValue
{
    /// <summary>The only value.</summary>
    public static readonly NoValue Value = new();
}
