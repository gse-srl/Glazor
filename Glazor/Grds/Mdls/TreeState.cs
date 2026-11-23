using System.Text.Json;
using System.Text.Json.Serialization;

namespace Glazor.Grds.Mdls;

/// <summary>
/// A node's place in a tree: the keys of the nodes from a root down to it. A node under two parents has a place under each.
/// As JSON it is a bare array of keys, <c>[1, 7]</c>, and one read back holds <see cref="JsonElement"/>s for its keys, so two paths
/// are equal when their keys write the same JSON.
/// </summary>
/// <param name="Keys">The keys of the nodes on the path, the root's first and the node's own last.</param>
[JsonConverter(typeof(NodePathConverter))]
public sealed record NodePath(IReadOnlyList<object> Keys)
{
    private readonly IReadOnlyList<object> _keys = Keys ?? throw new ArgumentNullException(nameof(Keys));
    private string? _text;

    /// <summary>The keys of the nodes on the path, the root's first and the node's own last.</summary>
    // Setting the keys, with a with expression too, drops the cached text
    public IReadOnlyList<object> Keys { get => _keys; init { _keys = value ?? throw new ArgumentNullException(nameof(Keys)); _text = null; } }

    private string Text => _text ??= JsonSerializer.Serialize(Keys);

    /// <inheritdoc/>
    public bool Equals(NodePath? other) => other is not null && Text == other.Text;

    /// <inheritdoc/>
    public override int GetHashCode() => Text.GetHashCode();
}

/// <summary>
/// What an <see cref="ITreeStateGridModel{TGridItem}"/> gives and takes back: the nodes expanded, in no order, and the node
/// zoomed on, null when not zoomed or for a model that does not zoom.
/// </summary>
/// <param name="Expanded">The paths of the nodes expanded, in no order.</param>
/// <param name="Zoom">The path of the node zoomed on; null when not zoomed.</param>
// Always written: strict options require it, and options that skip nulls would leave it out
public sealed record TreeState(IReadOnlyList<NodePath> Expanded, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] NodePath? Zoom)
{
    // A null here comes from a malformed store, which the docs say fails as JSON; written first, as the positional property was
    /// <summary>The paths of the nodes expanded, in no order.</summary>
    [JsonPropertyOrder(-1)]
    public IReadOnlyList<NodePath> Expanded { get; init; } = Expanded ?? throw new JsonException("A tree state needs its expanded nodes, empty for none");

    /// <inheritdoc/>
    public bool Equals(TreeState? other) => other is not null && Equals(Zoom, other.Zoom) && Expanded.ToHashSet().SetEquals(other.Expanded);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Zoom, Expanded.Distinct().Count());   // Equals compares them as a set
}

// The keys go with the default options both ways, as the model reads them back and as Equals compares them: the host's own
// would write numbers as strings or read keys as JsonNodes
internal sealed class NodePathConverter : JsonConverter<NodePath>
{
    public override NodePath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(JsonSerializer.Deserialize<List<object>>(ref reader) ?? throw new JsonException("A node path is an array of keys"));

    public override void Write(Utf8JsonWriter writer, NodePath value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value.Keys);
}
