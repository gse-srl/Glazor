using Glazor.Grds.Decs;
using Glazor.Grds.Filters;
using Microsoft.AspNetCore.Components;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace Glazor.Grds;

/// <summary>
/// What a column declares whatever its row type: label, size, alignment and flags. <see cref="IColumn{TGridItem}"/> adds what
/// depends on the row: the filter definition and the templates.
/// </summary>
public interface IColumn
{
    /// <summary>
    /// Gets the label used for the column header.
    /// </summary>
    /// <returns>The column header label.</returns>
    string GetLabel();

    /// <summary>
    /// Gets the column's preferred size, which is relative to other displayed columns' preferred sizes.
    /// </summary>
    /// <returns>The preferred size. 0 keeps the column out of the default set and out of the layout dialog; a layout or
    /// <c>SetColumnsToShow</c> naming it shows it, with no width of its own, and the dialog leaves it where it is.</returns>
    int GetPreferredSize();

    /// <summary>
    /// For data cells
    /// </summary>
    /// <returns>When greater than 0 then ellipsis are shown if required and the text is truncated</returns>
    int GetLinesToClamp();

    /// <summary>
    /// For header cells
    /// </summary>
    /// <returns>When greater than 0 then ellipsis are shown if required and the text is truncated</returns>
    int GetHeaderLinesToClamp();

    /// <summary>
    /// Indicates whether the column supports sorting.
    /// </summary>
    /// <returns>True if the column is sortable; otherwise, false.</returns>
    bool IsSortable();

    /// <summary>
    /// Indicates whether the column supports data filtering.
    /// </summary>
    /// <returns>True if the column is filterable; otherwise, false.</returns>
    bool IsFilterable();

    /// <summary>
    /// Gets the default data type for values contained in the column.
    /// </summary>
    /// <returns>The default data type.</returns>
    Type GetValueType();

    /// <summary>
    /// Indicates whether the column is fixed (always displayed at the beginning and its position cannot be changed).
    /// </summary>
    /// <returns>True if the column is fixed; otherwise, false.</returns>
    bool IsFixed();

    /// <summary>
    /// Indicates whether the column is displayed by default if not explicitly specified.
    /// </summary>
    /// <returns>True if the column is displayed by default; otherwise, false.</returns>
    bool IsShownByDefault();

    /// <summary>
    /// Indicates whether the grid's row dialog lists the column, when the column says so.
    /// </summary>
    /// <returns>True or false as the column says; null leaves it to the grid, which lists a column that is neither fixed nor without a label.</returns>
    bool? IsShownInRowDialog() => null;

    /// <summary>
    /// Gets the alignment for the column.
    /// </summary>
    /// <returns>The alignment.</returns>
    Alignment GetAlignment();

    /// <summary>
    /// Gets the column group ID.
    /// </summary>
    /// <returns>The column group ID.</returns>
    string? GetGroupId();
}

/// <summary>
/// A column of rows of type <typeparamref name="TGridItem"/>: what <see cref="IColumn"/> declares, with the filter definition, the
/// renderers and the editors, which depend on the row. <c>GetColumn</c> on a grid or a model returns it.
/// </summary>
public interface IColumn<TGridItem> : IColumn
{
    /// <summary>The column's own filter definition; null lets the grid's <c>FilterDefFactory</c> choose one by the value type.</summary>
    AFilterDef? GetFilterDef();

    /// <summary>
    /// Gets the default renderer to use for the column header.
    /// </summary>
    /// <returns>The default formatter.</returns>
    RenderFragment<HDecoratorContext<TGridItem>>? GetHRenderer();

    /// <summary>
    /// Gets the default renderer to use for cells.
    /// </summary>
    /// <returns>The default formatter.</returns>
    RenderFragment<DecoratorContext<TGridItem, object>>? GetRenderer();

    /// <summary>The CSS classes of a cell's <c>td</c>, from the item and the value; null for none.</summary>
    Func<DecoratorContext<TGridItem, object>, string?>? GetCellClassFn();

    /// <summary>The property an editor in the grid writes; null when only an edit renderer, or nothing, edits the column.</summary>
    PropertyInfo? GetEditProperty() => null;

    /// <summary>Draws the column's editor in the row being edited, in place of the one chosen by its type.</summary>
    RenderFragment<DecoratorContext<TGridItem, object>>? GetEditRenderer() => null;

    /// <summary>
    /// Used by the EnrichGridColumn component to add "properties" to the column, normally things that are better to be written in 
    /// a razor file rather than in a cs file.
    /// </summary>
    /// <returns>A clone with extra properties derived from the parameter</returns>
    IColumn<TGridItem> Enrich<TV>(EnrichGridColumn<TGridItem, TV> gridColumn);
}

/// <summary>
/// A column whose values are of type <typeparamref name="VT"/>, as <see cref="Column{TGridItem, VT}"/> is.
/// </summary>
public interface IColumn<TGridItem, VT> : IColumn<TGridItem>
{
}

/// <summary>The horizontal alignment of a column's cells.</summary>
public enum Alignment
{
    /// <summary>By the value: numbers right, dates, times, enums and booleans centered, the rest left.</summary>
    Default = 0,
    /// <summary>Aligned left.</summary>
    Left,
    /// <summary>Centered.</summary>
    Center,
    /// <summary>Aligned right, as numbers usually are.</summary>
    Right
}

/// <summary>
/// Equality of two columns by what they declare (label, size, clamping, flags), so a model tells a re-registered column that
/// changed from one that did not.
/// </summary>
public class ColumnEqualityComparer : IEqualityComparer<IColumn>
{
    /// <summary>The shared instance.</summary>
    public static ColumnEqualityComparer Instance { get; } = new();

    /// <summary>For a subclass; use <see cref="Instance"/> otherwise.</summary>
    protected ColumnEqualityComparer()
    {
    }

    /// <inheritdoc/>
    public bool Equals(IColumn? x, IColumn? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null)
            return false;
        return x.GetLabel() == y.GetLabel() &&
               x.GetPreferredSize() == y.GetPreferredSize() &&
               x.GetLinesToClamp() == y.GetLinesToClamp() &&
               x.GetHeaderLinesToClamp() == y.GetHeaderLinesToClamp() &&
               x.IsSortable() == y.IsSortable() &&
               x.IsFilterable() == y.IsFilterable() &&
               x.GetValueType() == y.GetValueType() &&
               x.IsFixed() == y.IsFixed() &&
               x.IsShownByDefault() == y.IsShownByDefault() &&
               x.IsShownInRowDialog() == y.IsShownInRowDialog() &&
               x.GetAlignment() == y.GetAlignment() &&
               x.GetGroupId() == y.GetGroupId();
    }

    /// <inheritdoc/>
    public int GetHashCode([DisallowNull] IColumn obj)
    {
        if (obj is null)
            return 0;
        var hash = new HashCode();
        hash.Add(obj.GetLabel());
        hash.Add(obj.GetPreferredSize());
        hash.Add(obj.GetLinesToClamp());
        hash.Add(obj.GetHeaderLinesToClamp());
        hash.Add(obj.IsSortable());
        hash.Add(obj.IsFilterable());
        hash.Add(obj.GetValueType());
        hash.Add(obj.IsFixed());
        hash.Add(obj.IsShownByDefault());
        hash.Add(obj.IsShownInRowDialog());
        hash.Add(obj.GetAlignment());
        hash.Add(obj.GetGroupId());
        return hash.ToHashCode();
    }
}

internal static class RowDialogColumns
{
    // A column that does not say is listed unless it is fixed or has no label
    internal static bool IsListedInRowDialog(this IColumn col) => col.IsShownInRowDialog() ?? (!col.IsFixed() && !string.IsNullOrEmpty(col.GetLabel()));
}
