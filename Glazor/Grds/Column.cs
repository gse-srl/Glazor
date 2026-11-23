using Glazor.Grds.Decs;
using Glazor.Grds.Filters;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using System.Reflection;

namespace Glazor.Grds;

/// <summary>
/// A column declared in code, with <c>AddColumn</c> or <c>AddEFColumn</c> on the model: its label, value type, alignment, size,
/// flags and templates. <see cref="Column{TGridItem, VT}"/> takes the value type from its type argument. Without a
/// <c>PreferredSize</c> a column is as wide as a markup column of its value type; a negative one leaves it unsized.
/// </summary>
/// <param name="Label">The header text.</param>
/// <param name="ValueType">The type of the column's values, which chooses its filter, its sort and its default size.</param>
// The members other than the label are init properties, not positional ones: a member added later breaks no caller
public record Column<TGridItem>(string Label, Type ValueType) : IColumn<TGridItem>
{
    /// <summary>How the cells are aligned; <c>Default</c> aligns numbers right, dates, enums and booleans center, the rest left.</summary>
    public Alignment Alignment { get; init; } = Alignment.Default;
    /// <summary>Custom header rendering, from the column.</summary>
    public RenderFragment<HDecoratorContext<TGridItem>>? HRenderer { get; init; }
    /// <summary>Custom cell rendering, from the item, the value, the row index and the column.</summary>
    public RenderFragment<DecoratorContext<TGridItem, object>>? Renderer { get; init; }
    /// <summary>The CSS classes of the cell's <c>td</c>, from the item and the value.</summary>
    public Func<DecoratorContext<TGridItem, object>, string?>? CellClassFn { get; init; }
    /// <summary>Groups adjacent columns under a shared header, shown with the grid's <c>ShowColumnGroups</c>; the header shows this text, or what the grid's <c>GHCellTemplate</c> draws.</summary>
    public string? GroupId { get; init; }
    /// <summary>The width in display units; null takes it from the value type. 0 keeps the column out of the default set and out of the layout dialog.</summary>
    public int? PreferredSize { get; init; }
    /// <summary>Clamps the cell text to this many lines with an ellipsis; 0 or less does not clamp.</summary>
    public int LinesToClamp { get; init; } = 1;
    /// <summary>Clamps the header text to this many lines with an ellipsis; 0 or less does not clamp.</summary>
    public int HeaderLinesToClamp { get; init; } = 1;
    /// <summary>Whether the user can filter the column.</summary>
    public bool Filterable { get; init; } = true;
    /// <summary>A filter editor for this column instead of the one the factory chooses; it must take the column's values.</summary>
    public AFilterDef? FilterDef { get; init; }
    /// <summary>Pins the column to the left: always shown, out of horizontal scrolling and reordering.</summary>
    public bool Fixed { get; init; }
    /// <summary>Whether the column is shown before the user arranges the layout.</summary>
    public bool ShownByDefault { get; init; } = true;
    /// <summary>Whether the grid's row dialog lists the column; null lists it unless it is fixed or has no label.</summary>
    public bool? ShownInRowDialog { get; init; }
    /// <summary>Whether the user can sort by the column; its values must be comparable, unless the model supplies a comparer.</summary>
    public bool Sortable { get; init; } = true;
    /// <summary>The property an editor writes when the grid edits rows in place, a plain one with a public setter; null edits nothing unless <see cref="EditRenderer"/> is set.</summary>
    public PropertyInfo? EditProperty { get; init; }
    /// <summary>The column's editor in the row being edited, in place of the one chosen by the value type.</summary>
    public RenderFragment<DecoratorContext<TGridItem, object>>? EditRenderer { get; init; }

    /// <summary>A copy of what <paramref name="other"/> declares.</summary>
    public Column(IColumn<TGridItem> other) : this(other.GetLabel(), other.GetValueType())
    {
        Alignment = other.GetAlignment();
        HRenderer = other.GetHRenderer();
        Renderer = other.GetRenderer();
        CellClassFn = other.GetCellClassFn();
        GroupId = other.GetGroupId();
        PreferredSize = other.GetPreferredSize();
        LinesToClamp = other.GetLinesToClamp();
        HeaderLinesToClamp = other.GetHeaderLinesToClamp();
        Filterable = other.IsFilterable();
        FilterDef = other.GetFilterDef()?.Clone();
        Fixed = other.IsFixed();
        ShownByDefault = other.IsShownByDefault();
        ShownInRowDialog = other.IsShownInRowDialog();
        Sortable = other.IsSortable();
        EditProperty = other.GetEditProperty();
        EditRenderer = other.GetEditRenderer();
    }

    /// <inheritdoc/>
    public Alignment GetAlignment() => Alignment;

    /// <inheritdoc/>
    public RenderFragment<HDecoratorContext<TGridItem>>? GetHRenderer() => HRenderer;

    /// <inheritdoc/>
    public RenderFragment<DecoratorContext<TGridItem, object>>? GetRenderer() => Renderer;

    /// <inheritdoc/>
    public Func<DecoratorContext<TGridItem, object>, string?>? GetCellClassFn() => CellClassFn;

    /// <inheritdoc/>
    public string? GetGroupId() => GroupId;

    /// <inheritdoc/>
    public string GetLabel() => Label;

    /// <inheritdoc/>
    public int GetPreferredSize() => PreferredSize ?? ColumnSizes.ByType(GetValueType());

    /// <inheritdoc/>
    public int GetLinesToClamp() => LinesToClamp;

    /// <inheritdoc/>
    public int GetHeaderLinesToClamp() => HeaderLinesToClamp;

    /// <inheritdoc/>
    public Type GetValueType() => ValueType;

    /// <inheritdoc/>
    public bool IsFilterable() => Filterable;

    /// <inheritdoc/>
    public AFilterDef? GetFilterDef() => FilterDef;

    /// <inheritdoc/>
    public bool IsFixed() => Fixed;

    /// <inheritdoc/>
    public bool IsShownByDefault() => ShownByDefault;

    /// <inheritdoc/>
    public bool? IsShownInRowDialog() => ShownInRowDialog;

    /// <inheritdoc/>
    public bool IsSortable() => Sortable;

    /// <inheritdoc/>
    public PropertyInfo? GetEditProperty() => EditProperty;

    /// <inheritdoc/>
    public RenderFragment<DecoratorContext<TGridItem, object>>? GetEditRenderer() => EditRenderer;

    /// <inheritdoc/>
    public IColumn<TGridItem> Enrich<TT>(EnrichGridColumn<TGridItem, TT> gridColumn)
    {
        RenderFragment<HDecoratorContext<TGridItem>>? htr = gridColumn.HeaderDisplayTemplate;
        RenderFragment<DecoratorContext<TGridItem, TT>>? tr = gridColumn.DisplayTemplate;
        return this with  // TT types the templates only: the column's type picks its filter and sort
        {
            HRenderer = htr == null ? GetHRenderer() : pv => htr(new HDecoratorContext<TGridItem>(pv.Column)),
            Renderer = tr == null ? GetRenderer() : pv => tr(new DecoratorContext<TGridItem, TT>(pv.Item, ColumnValue.Unbox<TT>(pv.Value, Label), pv.RowIdx, pv.Column)),
        };
    }
}

// The width a column takes from its value type when none is given, in markup or in code
internal static class ColumnSizes
{
    internal static int ByType(Type valueType)
    {
        Type dataType = Nullable.GetUnderlyingType(valueType) ?? valueType;
        if (dataType.IsEnum)
            return 10;
        if (typeof(string).IsAssignableFrom(dataType))
            return 20;
        if (typeof(bool).IsAssignableFrom(dataType))
            return 6;
        if (typeof(DateTime).IsAssignableFrom(dataType) || dataType == typeof(DateTimeOffset))
            return 12;
        if (dataType.IsNumber())
            return 8;
        return 10;
    }
}

// A null reaches a column of a non-nullable value type only through a getter the registration did not check, a template on a
// wrong type for instance: the message names the column, where the unboxing cast would throw a NullReferenceException
internal static class ColumnValue
{
    internal static VT? Unbox<VT>(object? value, string label)
    {
        if (value == null && default(VT) != null)
            throw new InvalidOperationException($"Null value for column '{label}' of the non-nullable type {typeof(VT).Name}");
        return (VT?)value;
    }
}

/// <summary>
/// A column declared in code, typed by its value: <c>new Column&lt;Product, decimal&gt;("Price")</c> passed to <c>AddColumn</c> or
/// <c>AddEFColumn</c> on the model.
/// </summary>
/// <param name="Label">The header text.</param>
public record Column<TGridItem, VT>(string Label) : IColumn<TGridItem, VT>
{
    /// <summary>How the cells are aligned; <c>Default</c> aligns numbers right, dates, enums and booleans center, the rest left.</summary>
    public Alignment Alignment { get; init; } = Alignment.Default;
    /// <summary>Custom header rendering, from the column.</summary>
    public RenderFragment<HDecoratorContext<TGridItem>>? HRenderer { get; init; }
    /// <summary>Custom cell rendering, from the item, the value, the row index and the column.</summary>
    public RenderFragment<DecoratorContext<TGridItem, VT>>? Renderer { get; init; }
    /// <summary>The CSS classes of the cell's <c>td</c>, from the item and the value.</summary>
    public Func<DecoratorContext<TGridItem, VT>, string?>? CellClassFn { get; init; }
    /// <summary>Groups adjacent columns under a shared header, shown with the grid's <c>ShowColumnGroups</c>; the header shows this text, or what the grid's <c>GHCellTemplate</c> draws.</summary>
    public string? GroupId { get; init; }
    /// <summary>The width in display units; null takes it from the value type. 0 keeps the column out of the default set and out of the layout dialog.</summary>
    public int? PreferredSize { get; init; }
    /// <summary>Clamps the cell text to this many lines with an ellipsis; 0 or less does not clamp.</summary>
    public int LinesToClamp { get; init; } = 1;
    /// <summary>Clamps the header text to this many lines with an ellipsis; 0 or less does not clamp.</summary>
    public int HeaderLinesToClamp { get; init; } = 1;
    /// <summary>Whether the user can filter the column.</summary>
    public bool Filterable { get; init; } = true;
    /// <summary>A filter editor for this column instead of the one the factory chooses, typed by the column's value type.</summary>
    public AFilterDef<VT>? FilterDef { get; init; }
    /// <summary>Pins the column to the left: always shown, out of horizontal scrolling and reordering.</summary>
    public bool Fixed { get; init; }
    /// <summary>Whether the column is shown before the user arranges the layout.</summary>
    public bool ShownByDefault { get; init; } = true;
    /// <summary>Whether the grid's row dialog lists the column; null lists it unless it is fixed or has no label.</summary>
    public bool? ShownInRowDialog { get; init; }
    /// <summary>Whether the user can sort by the column; its values must be comparable, unless the model supplies a comparer.</summary>
    public bool Sortable { get; init; } = true;
    /// <summary>The property an editor writes when the grid edits rows in place, a plain one with a public setter; null edits nothing unless <see cref="EditRenderer"/> is set.</summary>
    public PropertyInfo? EditProperty { get; init; }
    /// <summary>The column's editor in the row being edited, in place of the one chosen by the value type.</summary>
    public RenderFragment<DecoratorContext<TGridItem, VT>>? EditRenderer { get; init; }

    /// <inheritdoc/>
    public Alignment GetAlignment() => Alignment;

    /// <inheritdoc/>
    public RenderFragment<HDecoratorContext<TGridItem>>? GetHRenderer() => HRenderer;

    /// <inheritdoc/>
    public RenderFragment<DecoratorContext<TGridItem, object>>? GetRenderer() => Renderer == null ? null : 
        pv => Renderer(new DecoratorContext<TGridItem, VT>(pv.Item, ColumnValue.Unbox<VT>(pv.Value, Label), pv.RowIdx, pv.Column));

    /// <inheritdoc/>
    public Func<DecoratorContext<TGridItem, object>, string?>? GetCellClassFn() => CellClassFn == null ? null : 
        pv => CellClassFn(new DecoratorContext<TGridItem, VT>(pv.Item, ColumnValue.Unbox<VT>(pv.Value, Label), pv.RowIdx, pv.Column));

    /// <inheritdoc/>
    public string? GetGroupId() => GroupId;

    /// <inheritdoc/>
    public string GetLabel() => Label;

    /// <inheritdoc/>
    public int GetPreferredSize() => PreferredSize ?? ColumnSizes.ByType(GetValueType());

    /// <inheritdoc/>
    public int GetLinesToClamp() => LinesToClamp;

    /// <inheritdoc/>
    public int GetHeaderLinesToClamp() => HeaderLinesToClamp;

    /// <inheritdoc/>
    public Type GetValueType() => typeof(VT);

    /// <inheritdoc/>
    public bool IsFilterable() => Filterable;

    AFilterDef? IColumn<TGridItem>.GetFilterDef() => FilterDef;

    /// <summary>The column's own <see cref="FilterDef"/>, typed; null lets the grid's filter factory choose.</summary>
    public AFilterDef<VT>? GetFilterDef() => FilterDef;

    /// <inheritdoc/>
    public bool IsFixed() => Fixed;

    /// <inheritdoc/>
    public bool IsShownByDefault() => ShownByDefault;

    /// <inheritdoc/>
    public bool? IsShownInRowDialog() => ShownInRowDialog;

    /// <inheritdoc/>
    public bool IsSortable() => Sortable;

    /// <inheritdoc/>
    public PropertyInfo? GetEditProperty() => EditProperty;

    /// <inheritdoc/>
    public RenderFragment<DecoratorContext<TGridItem, object>>? GetEditRenderer() => EditRenderer == null ? null :
        pv => EditRenderer(new DecoratorContext<TGridItem, VT>(pv.Item, ColumnValue.Unbox<VT>(pv.Value, Label), pv.RowIdx, pv.Column));

    /// <inheritdoc/>
    public IColumn<TGridItem> Enrich<TT>(EnrichGridColumn<TGridItem, TT> gridColumn)
    {
        RenderFragment<HDecoratorContext<TGridItem>>? htr = gridColumn.HeaderDisplayTemplate;
        RenderFragment<DecoratorContext<TGridItem, TT>>? tr = gridColumn.DisplayTemplate;
        return new Column<TGridItem>(this)   // TT types the templates only: the column's type picks its filter and sort
        {
            HRenderer = htr == null ? GetHRenderer() : pv => htr(new HDecoratorContext<TGridItem>(pv.Column)),
            Renderer = tr == null ? GetRenderer() : pv => tr(new DecoratorContext<TGridItem, TT>(pv.Item, ColumnValue.Unbox<TT>(pv.Value, Label), pv.RowIdx, pv.Column)),
            PreferredSize = PreferredSize,   // as declared: the copy resolves the size and clones the filter
            FilterDef = FilterDef,
        };
    }
}
