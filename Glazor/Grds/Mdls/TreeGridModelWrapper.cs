
namespace Glazor.Grds.Mdls;

/// <summary>
/// A tree model that forwards every member to the one it wraps, the folder and tree-state ones too: <see cref="IsFolderModel"/>
/// and <see cref="CanSaveTreeState"/> say whether the wrapped model has them.
/// </summary>
public class TreeGridModelWrapper<TGridItem> : DataGridModelWrapper<TGridItem>, IFolderGridModel<TGridItem>, ITreeStateGridModel<TGridItem>
{
    /// <summary>The model wrapped.</summary>
    protected internal readonly ITreeGridModel<TGridItem> _treeGridModel;

    /// <summary>Wraps <paramref name="wrapped"/>, forwarding every member to it.</summary>
    protected TreeGridModelWrapper(ITreeGridModel<TGridItem> wrapped) : base(wrapped)
    {
        _treeGridModel = wrapped ?? throw new ArgumentNullException(nameof(wrapped));
    }

    /// <inheritdoc/>
    public virtual ITreeNode? GetCurrentTreeNode() => _treeGridModel.GetCurrentTreeNode();

    /// <inheritdoc/>
    public virtual ITreeNode GetTreeNode(int rowIndex) => _treeGridModel.GetTreeNode(rowIndex);

    /// <inheritdoc/>
    public virtual ValueTask SetExpandedAsync(int rowIndex, bool expanded) => _treeGridModel.SetExpandedAsync(rowIndex, expanded);

    // ---- folders

    private IFolderGridModel<TGridItem> Folders => _treeGridModel as IFolderGridModel<TGridItem>
        ?? throw new NotSupportedException("The wrapped model is no folder model");

    /// <inheritdoc/>
    public virtual bool IsFolderModel() => _treeGridModel is IFolderGridModel<TGridItem> folders && folders.IsFolderModel();

    /// <inheritdoc/>
    public virtual ValueTask ExpandAllAsync(int rowIndex) => Folders.ExpandAllAsync(rowIndex);

    /// <inheritdoc/>
    public virtual ValueTask CollapseAllAsync(int rowIndex) => Folders.CollapseAllAsync(rowIndex);

    /// <inheritdoc/>
    public virtual ValueTask ZoomInAsync() => Folders.ZoomInAsync();

    /// <inheritdoc/>
    public virtual bool CanZoomIn() => Folders.CanZoomIn();

    /// <inheritdoc/>
    public virtual ValueTask ZoomOutAsync(int levels) => Folders.ZoomOutAsync(levels);

    /// <inheritdoc/>
    public virtual int GetZoomLevel() => Folders.GetZoomLevel();

    /// <inheritdoc/>
    public virtual TGridItem? GetZoomedItem() => Folders.GetZoomedItem();

    /// <inheritdoc/>
    public virtual IReadOnlyList<TGridItem> GetZoomPath() => Folders.GetZoomPath();

    // ---- tree state

    private ITreeStateGridModel<TGridItem> Stateful => _treeGridModel as ITreeStateGridModel<TGridItem>
        ?? throw new NotSupportedException("The wrapped model has no tree state");

    /// <inheritdoc/>
    public virtual bool CanSaveTreeState() => _treeGridModel is ITreeStateGridModel<TGridItem> stateful && stateful.CanSaveTreeState();

    /// <inheritdoc/>
    public virtual TreeState GetTreeState() => Stateful.GetTreeState();

    /// <inheritdoc/>
    public virtual void RestoreTreeState(TreeState state) => Stateful.RestoreTreeState(state);
}
