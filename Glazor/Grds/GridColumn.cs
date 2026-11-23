using Glazor.Grds.Decs;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using System.Linq.Expressions;
using System.Reflection;

namespace Glazor.Grds;

/// <summary>
/// Declares one column of the grid it is written in. <see cref="EFProperty"/> lets an EF model push the column's filter and sort
/// down to the database; <see cref="Property"/> reads the value in memory. <typeparamref name="VT"/> is inferred from either.
/// Columns go in the grid's own markup, directly or under <c>@if</c> and <c>@foreach</c>. See the Columns section of
/// docs/grigliaria.md.
/// </summary>
public partial class GridColumn<TGridItem, VT> : ComponentBase, IDisposable
{
    /// <summary>The grid the column is declared in, which cascades itself.</summary>
    [CascadingParameter]
    public AGrid<TGridItem> DataGrid { get; set; } = null!;

    /// <summary>
    /// Use this or EFProperty, this is always executed in memory and never "pushed" to the db.
    /// </summary>
    [Parameter]
    public Func<TGridItem, VT>? Property { get; set; }

    /// <summary>
    /// Use this or Property, this is "pushed" to the db when used with an EF model.
    /// </summary>
    [Parameter]
    public Expression<Func<TGridItem, VT>>? EFProperty { get; set; }

    /// <summary>
    /// The property an editor writes when the grid edits rows in place, a plain one with a setter: <c>x => x.UnitPrice</c>. Alone it
    /// is also where the value is read; with <see cref="Property"/> it only names what to edit. An <see cref="EFProperty"/> on a
    /// plain property with a setter is edited without it.
    /// </summary>
    [Parameter]
    public Expression<Func<TGridItem, VT>>? EditProperty { get; set; }

    /// <summary>False keeps the column read-only when the grid edits rows in place, a key say.</summary>
    [Parameter]
    public bool Editable { get; set; } = true;

    /// <summary>
    /// The column's editor in the row being edited, in place of the one chosen by the value type; it binds what it likes, under
    /// the row's <c>EditContext</c>.
    /// </summary>
    [Parameter]
    public RenderFragment<DecoratorContext<TGridItem, VT>>? EditTemplate { get; set; }

    /// <summary>The header text; also the column's name when <see cref="Name"/> is not given.</summary>
    [Parameter, EditorRequired]
    public string Label { get; set; } = null!;

    /// <summary>
    /// The column's identity for the layout, the filters, the sort and the mementos; when not specified the Label is assumed.
    /// </summary>
    [Parameter]
    public string Name { get; set; } = null!;

    /// <summary>The width in display units, by default from the value type. 0 keeps the column out of the default set and out of the layout dialog.</summary>
    [Parameter]
    public int PreferredSize { get; set; } = -1;

    /// <summary>Clamps the cell text to this many lines with an ellipsis; 0 or less does not clamp.</summary>
    [Parameter]
    public int LinesToClamp { get; set; } = 1;

    /// <summary>Clamps the header text to this many lines with an ellipsis; 0 or less does not clamp.</summary>
    [Parameter]
    public int HeaderLinesToClamp { get; set; } = 1;

    /// <summary>
    /// Indicates whether the column supports sorting.
    /// </summary>
    /// <returns>True if the column is sortable; otherwise, false.</returns>
    [Parameter]
    public bool Sortable { get; set; } = true;

    /// <summary>
    /// Indicates whether the column supports data filtering.
    /// </summary>
    /// <returns>True if the column is filterable; otherwise, false.</returns>
    [Parameter]
    public bool Filterable { get; set; } = true;

    /// <summary>A filter editor for this column instead of the one the factory chooses, typed by the column's value type.</summary>
    [Parameter]
    public AFilterDef<VT>? FilterDef { get; set; } = null;

    /// <summary>
    /// Indicates whether the column is fixed (always displayed at the beginning and its position cannot be changed).
    /// </summary>
    /// <returns>True if the column is fixed; otherwise, false.</returns>
    [Parameter]
    public bool Fixed { get; set; }

    /// <summary>
    /// Indicates whether the column is displayed by default if not explicitly specified.
    /// </summary>
    /// <returns>True if the column is displayed by default; otherwise, false.</returns>
    [Parameter]
    public bool ShownByDefault { get; set; } = true;

    /// <summary>Whether the grid's row dialog lists the column; by default it does, unless the column is fixed or has no label.</summary>
    [Parameter]
    public bool? ShownInRowDialog { get; set; }

    /// <summary>
    /// Gets the alignment for the column.
    /// </summary>
    /// <returns>The alignment.</returns>
    [Parameter]
    public Alignment Alignment { get; set; } = Alignment.Default;

    /// <summary>
    /// Groups adjacent columns under a shared header, shown with the grid's <c>ShowColumnGroups</c>; the header shows this text,
    /// or what the grid's <c>GHCellTemplate</c> draws.
    /// </summary>
    [Parameter]
    public string? GroupId { get; set; }

    /// <summary>Custom header rendering.</summary>
    [Parameter]
    public RenderFragment<HDecoratorContext<TGridItem>>? HeaderDisplayTemplate { get; set; }

    /// <summary>Custom cell rendering, from the item, the value, the row index and the column.</summary>
    [Parameter]
    public RenderFragment<DecoratorContext<TGridItem, VT>>? DisplayTemplate { get; set; }

    /// <summary>The CSS classes of the cell's <c>td</c>, from the item and the value.</summary>
    [Parameter]
    public Func<DecoratorContext<TGridItem, VT>, string?>? CellClassFn { get; set; }

    private string? _lastName = null;
    private Func<TGridItem, VT>? _lastProp = null;
    private Expression<Func<TGridItem, VT>>? _lastEFProp = null;
    private Expression<Func<TGridItem, VT>>? _lastEditProp = null;
    private Func<TGridItem, VT>? _editPropFn;   // compiled once, read when EditProperty stands alone
    private Column<TGridItem, VT>? _lastColumn;
    private AFilterDef<VT>? _lastFilterDef;
    private IGridModel<TGridItem>? _lastModel;   // a new model instance must get the column too

    // Name may be omitted, and then the first Label is the name; one passed must be a name, or every render would take the label
    /// <inheritdoc/>
    public override Task SetParametersAsync(ParameterView parameters)
    {
        if (parameters.TryGetValue(nameof(Name), out string? name) && string.IsNullOrEmpty(name))
            throw new ArgumentException($"{nameof(Name)} is passed with no value for column: {(parameters.TryGetValue(nameof(Label), out string? label) ? label : Label)}");
        return base.SetParametersAsync(parameters);
    }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (string.IsNullOrEmpty(Name))
            Name = Label;
        if (string.IsNullOrEmpty(Name))
            throw new ArgumentException($"Neither: {nameof(Name)} nor: {nameof(Label)} has been specified for a column");
        if (Property != null && EFProperty != null)
            throw new ArgumentException($"Cannot specify both: {nameof(Property)} and {nameof(EFProperty)} for column: {Name}");
        if (EditProperty != null && EFProperty != null)
            throw new ArgumentException($"Cannot specify both: {nameof(EditProperty)} and {nameof(EFProperty)} for column: {Name}");
        if (Property == null && EFProperty == null && EditProperty == null)
            throw new ArgumentException($"Neither: {nameof(Property)} nor: {nameof(EFProperty)} nor: {nameof(EditProperty)} has been specified for column: {Name}");
        if (!ExpressionComparer.Equals(_lastEditProp, EditProperty))
            _editPropFn = EditProperty == null ? null
                : WritableProperty(EditProperty) != null ? EditProperty.Compile()
                : throw new ArgumentException($"{nameof(EditProperty)} of column: {Name} is not a property with a setter");
        if (PreferredSize < 0)
        {
            PreferredSize = DefaultPreferredSize();
        }
        // Speed up inspired by QuickGrid
        Column<TGridItem, VT>? ncol = NewColumn();  // it's a record
        if (_lastName != Name || _lastProp != Property || !ExpressionComparer.Equals(_lastEFProp, EFProperty) // EFProperty is compared structurally
            || !ExpressionComparer.Equals(_lastEditProp, EditProperty)
            || ncol != _lastColumn || _lastFilterDef != FilterDef || !ReferenceEquals(_lastModel, DataGrid?.GetModel()))
        {
            if (DataGrid == null)
                throw new InvalidOperationException($"Column '{Name}' is not inside a grid of {typeof(TGridItem)} (valueType: {typeof(VT)})");
            // Blazor reuses a component for another column by position, under @foreach without @key
            if (_lastName != null && _lastName != Name)
                DataGrid.ColumnUnregistered(_lastName, this);
            _lastName = Name;
            _lastProp = Property;
            _lastEFProp = EFProperty;
            _lastEditProp = EditProperty;
            _lastColumn = ncol;
            _lastFilterDef = FilterDef;
            _lastModel = DataGrid.GetModel();
            DataGrid.GetModel().SetColumn<VT>(DataGrid, this);
            if (_lastName != null)
                DataGrid.ColumnRegistered(_lastName, this);
        }
    }

    // An @if turning false takes the column out of the grid
    /// <inheritdoc/>
    public void Dispose()
    {
        if (_lastName != null)
            DataGrid?.ColumnUnregistered(_lastName, this);
    }

    /// <summary>The width a column takes without <see cref="PreferredSize"/>, by its value type.</summary>
    protected virtual int DefaultPreferredSize() => ColumnSizes.ByType(typeof(VT));

    internal IColumn<TGridItem, VT> GetColumn() => _lastColumn.RequireNotNull();

    // What reads the value in memory: Property, or EditProperty standing alone
    internal Func<TGridItem, VT>? ReadFn => Property ?? _editPropFn;

    // x => x.Member on a property with a public setter, not an init one, else null
    private static PropertyInfo? WritableProperty(Expression<Func<TGridItem, VT>>? expr) =>
        expr?.Body is MemberExpression { Member: PropertyInfo { SetMethod: { IsPublic: true } set } p } m && m.Expression == expr.Parameters[0]
            && !set.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)) ? p : null;

    /// <summary>The column the model registers, built from the parameters.</summary>
    protected virtual Column<TGridItem, VT> NewColumn()
    {
        return new Column<TGridItem, VT>(Label)
        {
            PreferredSize = PreferredSize,
            LinesToClamp = LinesToClamp,
            HeaderLinesToClamp = HeaderLinesToClamp,
            Sortable = Sortable,
            Filterable = Filterable,
            FilterDef = FilterDef,
            Fixed = Fixed,
            ShownByDefault = ShownByDefault,
            ShownInRowDialog = ShownInRowDialog,
            Alignment = Alignment,
            GroupId = GroupId,
            HRenderer = HeaderDisplayTemplate,
            Renderer = DisplayTemplate,
            CellClassFn = CellClassFn,
            EditProperty = Editable ? WritableProperty(EditProperty ?? EFProperty) : null,
            EditRenderer = Editable ? EditTemplate : null,
        };
    }
}
