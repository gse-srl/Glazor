using Glazor.Grds.Props;
using Glazor.Misc;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Glazor.Grds.Mdls;

/// <summary>A column as the model registered it, from <see cref="IGridModel{TGridItem}.GetColumnInfo"/>.</summary>
public interface IColumnInfo<TGridItem>
{
    /// <summary>The column's name, its identity in the layout, filters, sort and mementos.</summary>
    public string Name { get; }

    /// <summary>What the column declares: label, type, size, flags and templates.</summary>
    public IColumn<TGridItem> Column { get; }

    /// <summary>
    /// Reads the column's value from a row, in memory. It is typed as <see cref="object"/>, so its <c>PropertyType</c> says
    /// nothing about the column: the column's own type is <see cref="IColumn.GetValueType"/> on <see cref="Column"/>.
    /// </summary>
    public IGetter<TGridItem, object> Property { get; }

    /// <summary>The value as an expression, for a column added with <c>EFProperty</c> or <c>AddEFColumn</c>; null otherwise.</summary>
    public Expression<Func<TGridItem, object?>>? PropertyExpression { get; }

    /// <summary>Whether an EF model can push the column's filter and sort down to the database: it has a <see cref="PropertyExpression"/>.</summary>
    public bool QueryPushDownEnabled { get; }
}

internal record ColumnInfo<TGridItem>(string Name, IColumn<TGridItem> Column,
    IGetter<TGridItem, object> Property, // No object? because it's an IGetter
    Expression<Func<TGridItem, object?>>? PropertyExpression) : IColumnInfo<TGridItem>
{
    public bool QueryPushDownEnabled { get => PropertyExpression != null; }

    internal static ColumnInfo<TGridItem> Of<TProp>(string columnName, IColumn<TGridItem> column, Func<TGridItem, TProp> fprop)
    {
        Getter<TGridItem, TProp> prop = new(fprop);
        Type objClazz = typeof(TGridItem);
        if (!objClazz.IsAssignableFrom(prop.ObjectType))
            throw new ArgumentException($"Column: {columnName} of: {objClazz} not assignable from object type: {prop.ObjectType}");
        Type clazz = column.GetValueType();
        if (!clazz.IsAssignableFrom(prop!.PropertyType))
            throw new ArgumentException($"Column: {columnName} of type: {clazz} not assignable from property type: {prop.PropertyType}");
        Getter<TGridItem, object> oprop = new(o => prop.GetterFunc(o));
        return new ColumnInfo<TGridItem>(columnName, column, oprop, null);
    }

    internal static ColumnInfo<TGridItem> Of<TProp>(string columnName, IColumn<TGridItem> column, Expression<Func<TGridItem, TProp>> fexpr)
    {
        // in-memory getter only; oexpr below is built from the raw fexpr for push-down.
        Expression<Func<TGridItem, TProp>> nullFexpr = ExprUtils.PropagateNull(fexpr);
        Getter<TGridItem, TProp> prop = new(nullFexpr.Compile());
        Type objClazz = typeof(TGridItem);
        if (!objClazz.IsAssignableFrom(prop.ObjectType))
            throw new ArgumentException($"Column: {columnName} of: {objClazz} not assignable from object type: {prop.ObjectType}");
        Type clazz = column.GetValueType();
        if (!clazz.IsAssignableFrom(prop!.PropertyType))
            throw new ArgumentException($"Column: {columnName} of type: {clazz} not assignable from property type: {prop.PropertyType}");
        // Remember this is the path used to push queries down to the database
        var paramO = Expression.Parameter(typeof(object), "o");
        var oexpr = Expression.Lambda<Func<TGridItem, object?>>(
            Expression.Convert(Expression.Invoke(fexpr, Expression.Convert(paramO, typeof(TGridItem))), typeof(object)), paramO);
        ExprGetter<TGridItem, object> oprop = new(oexpr, o => prop.GetterFunc(o));
        return new ColumnInfo<TGridItem>(columnName, column, oprop, oexpr);
    }

    internal static ColumnInfo<TGridItem> Of<TProp>(string columnName, IColumn<TGridItem> column, GridColumn<TGridItem, TProp> gcol)
    {
        if (gcol.EFProperty != null)
            return Of(columnName, column, gcol.EFProperty);
        return Of(columnName, column, gcol.ReadFn.RequireNotNull());
    }
}

/// <summary>
/// Just compares the Name and Column properties using ColumnEqualityComparer
/// </summary>
internal class ColumnInfoEqualityComparer<TGridItem> : IEqualityComparer<ColumnInfo<TGridItem>>
{
    private static readonly ColumnEqualityComparer columnEqualityComparer = ColumnEqualityComparer.Instance;

    public static ColumnInfoEqualityComparer<TGridItem> Instance { get; } = new();

    protected ColumnInfoEqualityComparer()
    {
    }

    public bool Equals(ColumnInfo<TGridItem>? x, ColumnInfo<TGridItem>? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null)
            return false;
        if (x.Name != y.Name)
            return false;
        return columnEqualityComparer.Equals(x.Column, y.Column);
    }

    public int GetHashCode([DisallowNull] ColumnInfo<TGridItem> obj)
    {
        if (obj is null)
            return 0;
        var hash = new HashCode();
        hash.Add(obj.Name);
        hash.Add(columnEqualityComparer.GetHashCode(obj.Column));
        return hash.ToHashCode();
    }
}

