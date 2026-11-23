using Glazor.Grds.Decs;
using Glazor.Grds.Mdls;
using Microsoft.AspNetCore.Components;

namespace Glazor.Grds;

// [CascadingTypeParameter] does not reach a component with two type parameters, so the markup gives TGridItem
/// <summary>
/// Attaches templates from markup to a column the model already declares in code, with <c>AddColumn</c> or <c>AddEFColumn</c>,
/// found by its <c>Name</c>. Give <typeparamref name="TGridItem"/> and <typeparamref name="VT"/> in the markup. See docs/grigliaria.md.
/// </summary>
public partial class EnrichGridColumn<TGridItem, VT> : ComponentBase
{
    /// <summary>The grid the column is declared in, which cascades itself.</summary>
    [CascadingParameter]
    public AGrid<TGridItem> DataGrid { get; set; } = null!;

    /// <summary>
    /// The Name of the model column it is bound to.
    /// </summary>
    [Parameter, EditorRequired]
    public string Name { get; set; } = null!;

    /// <summary>Replaces the header rendering of the column it enriches.</summary>
    [Parameter]
    public RenderFragment<HDecoratorContext<TGridItem>>? HeaderDisplayTemplate { get; set; }

    /// <summary>Replaces the cell rendering of the column it enriches.</summary>
    [Parameter]
    public RenderFragment<DecoratorContext<TGridItem, VT>>? DisplayTemplate { get; set; }

    private string? _lastName = null;
    private RenderFragment<DecoratorContext<TGridItem, VT>>? _lastDisplayTemplate;
    private RenderFragment<HDecoratorContext<TGridItem>>? _lastHeaderDisplayTemplate;
    private IGridModel<TGridItem>? _lastModel;   // a new model instance must get the enrichment too
    private IColumn<TGridItem>? _lastEnriched;   // a GridColumn registering again replaces it

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        // Speed up inspired by QuickGrid
        if (_lastName != Name || _lastDisplayTemplate != DisplayTemplate || _lastHeaderDisplayTemplate != HeaderDisplayTemplate
            || !ReferenceEquals(_lastModel, DataGrid?.GetModel()) || !ReferenceEquals(_lastEnriched, ModelColumn()))
        {
            if (DataGrid == null)
                throw new InvalidOperationException($"Column '{Name}' is not inside a grid of {typeof(TGridItem)} (valueType: {typeof(VT)})");
            _lastName = Name;
            _lastDisplayTemplate = DisplayTemplate;
            _lastHeaderDisplayTemplate = HeaderDisplayTemplate;
            _lastModel = DataGrid.GetModel();
            DataGrid.GetModel().EnrichColumn<VT>(DataGrid, this);
            _lastEnriched = ModelColumn();
        }
    }

    private IColumn<TGridItem>? ModelColumn()
    {
        IGridModel<TGridItem>? model = DataGrid?.GetModel();
        int idx = model?.GetColumnIndex(Name) ?? -1;
        return idx < 0 ? null : model!.GetColumn(idx);
    }
}
