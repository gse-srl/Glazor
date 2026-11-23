namespace Glazor.Grds.Mdls;

/// <summary>
/// Represents a tree node with properties for expansion, nesting level, and child relationships.
/// </summary>
public interface ITreeNode
{
    /// <summary>
    /// Indicates whether the node is expanded. This is relevant only if <see cref="IsLeaf"/> is false.
    /// </summary>
    /// <returns>True if the node is expanded; otherwise, false.</returns>
    bool IsExpanded();

    /// <summary>
    /// Gets the nesting level of the current node. The value 0 is reserved for root nodes.
    /// </summary>
    /// <returns>The nesting level of the node.</returns>
    int GetLevel();

    /// <summary>
    /// Indicates whether the node is a leaf, meaning it cannot have children.
    /// </summary>
    /// <returns>True if the node is a leaf; otherwise, false.</returns>
    bool IsLeaf();

    /// <summary>
    /// Indicates whether the node is a special leaf case containing sub-totals.
    /// </summary>
    /// <returns>True if the node is a sub-total; otherwise, false.</returns>
    bool IsSubTotal();

    /// <summary>
    /// Indicates whether the node has children. If it is not known yet, this should return true.
    /// </summary>
    /// <returns>True if the node has children or if it is not yet known; otherwise, false.</returns>
    bool HasChildren();
}
