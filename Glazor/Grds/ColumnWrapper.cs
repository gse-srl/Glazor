using Glazor.Grds.Decs;
using Glazor.Grds.Filters;
using Microsoft.AspNetCore.Components;
using System.Reflection;

namespace Glazor.Grds;

/// <summary>
/// A column that forwards every member to the one it wraps, to derive from and override what changes, a size or the fixed
/// flag for instance.
/// </summary>
public abstract class ColumnWrapper<TGridItem> : IColumn<TGridItem>
{
    private readonly IColumn<TGridItem> _wrapped;

    /// <summary>Wraps <paramref name="wrapped"/>, forwarding every member to it.</summary>
    protected ColumnWrapper(IColumn<TGridItem> wrapped)
    {
        _wrapped = wrapped ?? throw new ArgumentNullException(nameof(wrapped));
    }

    /// <summary>The column wrapped.</summary>
    protected IColumn<TGridItem> GetWrapped() => _wrapped;

    /// <inheritdoc/>
    public virtual RenderFragment<HDecoratorContext<TGridItem>>? GetHRenderer() => _wrapped.GetHRenderer();

    /// <inheritdoc/>
    public virtual RenderFragment<DecoratorContext<TGridItem, object>>? GetRenderer() => _wrapped.GetRenderer();

    /// <inheritdoc/>
    public virtual Func<DecoratorContext<TGridItem, object>, string?>? GetCellClassFn() => _wrapped.GetCellClassFn();

    /// <inheritdoc/>
    public virtual string GetLabel() => _wrapped.GetLabel();

    /// <inheritdoc/>
    public virtual int GetPreferredSize() => _wrapped.GetPreferredSize();

    /// <inheritdoc/>
    public virtual int GetLinesToClamp() => _wrapped.GetLinesToClamp();

    /// <inheritdoc/>
    public virtual int GetHeaderLinesToClamp() => _wrapped.GetHeaderLinesToClamp();

    /// <inheritdoc/>
    public virtual Type GetValueType() => _wrapped.GetValueType();

    /// <inheritdoc/>
    public virtual bool IsFilterable() => _wrapped.IsFilterable();

    /// <inheritdoc/>
    public virtual AFilterDef? GetFilterDef() => _wrapped.GetFilterDef();

    /// <inheritdoc/>
    public virtual bool IsSortable() => _wrapped.IsSortable();

    /// <inheritdoc/>
    public virtual bool IsFixed() => _wrapped.IsFixed();

    /// <inheritdoc/>
    public virtual bool IsShownByDefault() => _wrapped.IsShownByDefault();

    /// <inheritdoc/>
    public virtual bool? IsShownInRowDialog() => _wrapped.IsShownInRowDialog();

    /// <inheritdoc/>
    public virtual PropertyInfo? GetEditProperty() => _wrapped.GetEditProperty();

    /// <inheritdoc/>
    public virtual RenderFragment<DecoratorContext<TGridItem, object>>? GetEditRenderer() => _wrapped.GetEditRenderer();

    /// <inheritdoc/>
    public virtual Alignment GetAlignment() => _wrapped.GetAlignment();

    /// <inheritdoc/>
    public virtual string? GetGroupId() => _wrapped.GetGroupId();

    /// <inheritdoc/>
    public virtual IColumn<TGridItem> Enrich<T>(EnrichGridColumn<TGridItem, T> gridColumn)
    {
        return NewWrapper(_wrapped.Enrich(gridColumn));
    }

    /// <summary>A wrapper like this one around <paramref name="wrapped"/>, so that an enriched column keeps what this one changes.</summary>
    protected abstract ColumnWrapper<TGridItem> NewWrapper(IColumn<TGridItem> wrapped);
}
