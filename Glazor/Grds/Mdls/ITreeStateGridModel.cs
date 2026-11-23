namespace Glazor.Grds.Mdls;

/// <summary>
/// An <see cref="ITreeGridModel{TGridItem}"/> that can give and take back its tree state: the nodes expanded and, for a model
/// that zooms, the node zoomed on, each as the keys of its path from a root. With one, <see cref="Treegliaria{TGridItem}"/>
/// saves that state in a <see cref="TreeMemento"/>.
/// </summary>
public interface ITreeStateGridModel<TGridItem> : ITreeGridModel<TGridItem>
{
    /// <summary>
    /// Whether the tree state can be saved: false when the keys are not values a memento can store as JSON and read back, a
    /// composite key for instance, and for a wrapper whose model has no tree state. The grid asks it before it saves or restores
    /// the state.
    /// </summary>
    bool CanSaveTreeState();

    /// <summary>The nodes expanded and the one zoomed on: what a <see cref="TreeMemento"/> saves.</summary>
    /// <exception cref="NotSupportedException"><see cref="CanSaveTreeState"/> is false.</exception>
    TreeState GetTreeState();

    /// <summary>
    /// Puts back what <see cref="GetTreeState"/> gave. A key may be a <see cref="System.Text.Json.JsonElement"/>, as a memento read
    /// from JSON holds it. A node no longer there is skipped.
    /// </summary>
    void RestoreTreeState(TreeState state);
}
