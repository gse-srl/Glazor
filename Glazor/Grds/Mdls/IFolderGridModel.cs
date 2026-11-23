namespace Glazor.Grds.Mdls;

/// <summary>
/// An <see cref="ITreeGridModel{TGridItem}"/> that expands or collapses a whole subtree at once and zooms on a folder. With one,
/// <see cref="Treegliaria{TGridItem}"/> binds Ctrl+Shift+↓ and Ctrl+Shift+↑ to expanding and collapsing all below the current
/// row, Ctrl+Shift+→ to zooming in on it and Ctrl+Shift+← to zooming out one level.
/// </summary>
public interface IFolderGridModel<TGridItem> : ITreeGridModel<TGridItem>
{
    /// <summary>
    /// Whether the members below can be used: true for a folder model, false for a wrapper whose model is none. The grid asks
    /// it before it binds the keys and shows the zoom bar.
    /// </summary>
    bool IsFolderModel();

    /// <summary>Expands the folder at the row and every folder below it; -1 for the whole tree.</summary>
    ValueTask ExpandAllAsync(int rowIndex);

    /// <summary>Collapses the folder at the row and every folder below it; -1 for the whole tree.</summary>
    ValueTask CollapseAllAsync(int rowIndex);

    /// <summary>Makes the current folder the root shown, expanded, at level 0; on a leaf, the folder it is in.</summary>
    ValueTask ZoomInAsync();

    /// <summary>
    /// Whether <see cref="ZoomInAsync"/> can act: the current folder, or a leaf's, is not the one zoomed on already. It reads
    /// nothing, as <see cref="GetZoomPath"/>: false while the current row is not read yet.
    /// </summary>
    bool CanZoomIn();

    /// <summary>Climbs back up that many levels; zero or less goes all the way out.</summary>
    ValueTask ZoomOutAsync(int levels);

    /// <summary>The depth of the folder zoomed on, 1 for a root; 0 when not zoomed.</summary>
    int GetZoomLevel();

    /// <summary>The folder zoomed on, the first row; null when not zoomed.</summary>
    TGridItem? GetZoomedItem();

    /// <summary>
    /// The folders from a root down to the one zoomed on, empty when not zoomed. It is what the last build found and reads
    /// nothing, so a grid can draw it before its fetch; <see cref="GetZoomedItem"/> reads first.
    /// </summary>
    IReadOnlyList<TGridItem> GetZoomPath();
}
