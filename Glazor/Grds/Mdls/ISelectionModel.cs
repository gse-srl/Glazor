namespace Glazor.Grds.Mdls;

/// <summary>
/// The rows selected in a <see cref="MultiSelGrid{TGridItem}"/>, kept apart from the data model. See the Selection models
/// section of docs/data-models.md.
/// </summary>
public interface ISelectionModel<TGridItem>
{
    /// <summary>
    /// Selects or deselects <paramref name="item"/>. Selecting an item whose selection is not enabled does nothing; deselecting
    /// it still works.
    /// </summary>
    public void Select(TGridItem item, bool isSelected);

    /// <summary>Whether <paramref name="item"/> is selected, matched by key when the model has a key function, else by the item's own equality.</summary>
    public bool IsSelected(TGridItem item);

    /// <summary>
    /// When false, <c>Select(item, true)</c> does nothing and MultiSelGrid leaves the item as it is; <c>Select(item, false)</c>
    /// still deselects it, as does <c>Clear</c> on <see cref="SetSelectionModel{TGridItem}"/> and
    /// <see cref="SingleSelectionModel{TGridItem}"/>, a single selection gives it up to the next item selected, and the select-all
    /// models take their candidates when they are built.
    /// </summary>
    /// <returns>Whether <paramref name="item"/> may be selected.</returns>
    public bool IsSelectionEnabled(TGridItem item);

    /// <summary>How many items are selected.</summary>
    public int SelectedCount();
}

/// <summary>
/// A selection model that can select or clear all its candidates at once, for the select-all box of
/// <see cref="MultiSelGrid{TGridItem}"/>.
/// </summary>
public interface ISelectionModelWithSelectAll<TGridItem> : ISelectionModel<TGridItem>
{
    /// <summary>Whether there are candidates and every one is selected: the state of the select-all box.</summary>
    public bool AllSelected();

    /// <summary>Selects every candidate, an item whose selection is enabled; any other item keeps its state.</summary>
    public void SelectAll();

    /// <summary>Deselects every candidate, an item whose selection is enabled; any other item keeps its state.</summary>
    public void DeselectAll();
}
