using Glazor.Misc;

namespace Glazor.Grds.Mdls;

/// <summary>
/// A comparer which reads the column values through the model's <c>GetValue</c>.<br/>
/// If provided, a delegated comparator is used to compare columns values otherwise
/// values must implement the IComparable interface.
/// </summary>
public class ColumnComparer<T> : Comparer<T>
{
    private readonly IGridModel<T> _model;
    private readonly IComparer<object>? _delegated;
    private readonly bool _ascending;
    private readonly int _columnIdx;

    /// <summary>Compares rows by the value of column <paramref name="columnIdx"/> of <paramref name="model"/>, nulls first when <paramref name="ascending"/>; by <paramref name="delegated"/>, or as <see cref="IComparable"/> without it.</summary>
    public ColumnComparer(IGridModel<T> model, IComparer<object>? delegated, bool ascending, int columnIdx)
    {
        _model = model.RequireNotNull();
        ArgumentOutOfRangeException.ThrowIfNegative(columnIdx);
        _delegated = delegated;
        _ascending = ascending;
        _columnIdx = columnIdx;
    }

    /// <inheritdoc/>
    public override int Compare(T? x, T? y)
    {
        if (Object.ReferenceEquals(x, y))
            return 0;
        if (x == null)
            return _ascending ? -1 : 1;
        if (y == null) 
            return _ascending ? 1 : - 1;
        object? f1 = _model.GetValue(x, _columnIdx);
        object? f2 = _model.GetValue(y, _columnIdx);
        if (Object.ReferenceEquals(f1, f2))
            return 0;
        if (f1 == null)
            return _ascending ? -1 : 1;
        if (f2 == null) 
            return _ascending ? 1 : -1;
        int res = _delegated == null ? ((IComparable)f1).CompareTo(f2) 
            : _delegated.Compare(f1, f2);
        return _ascending ? res : -Math.Sign(res);   // -int.MinValue is int.MinValue
    }
}

/// <summary>
/// Compares rows by a column in memory: <see cref="GetComparerForColumn"/> and <see cref="GetComparerByType"/> give the comparer a
/// sortable column uses, <see cref="CheckCanSort"/> refuses a sortable column without one, <see cref="BinarySearch"/> and
/// <see cref="ContainsSearch"/> run the searches over a list.
/// </summary>
public static class PocoColumnComparer
{
    // Without a comparer the values go through Comparer<T>.Default, so the type implements IComparable<T> or IComparable; an
    // object column's are known only one by one
    internal static bool CanCompare(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(object)
            || typeof(IComparable).IsAssignableFrom(type)
            || typeof(IComparable<>).MakeGenericType(type).IsAssignableFrom(type);
    }

    /// <summary>
    /// Throws when a sortable column's values cannot be compared in memory and no comparer is given; the built-in models call it
    /// from <c>ValidateStoredColumn</c> for each sortable column, and a model of your own that sorts does the same.
    /// </summary>
    /// <param name="columnName">The column, for the message.</param>
    /// <param name="type">The type of the column's values.</param>
    /// <param name="comparer">The column's own comparer, or null for the type's default one.</param>
    /// <exception cref="InvalidOperationException">The values cannot be compared.</exception>
    public static void CheckCanSort(string columnName, Type type, IComparer<object>? comparer)
    {
        if (comparer == null && !CanCompare(type))
            throw new InvalidOperationException($"Values of type {type} cannot be compared, for column: {columnName}. "
                + "Implement IComparable<T> or IComparable, set Sortable to false, or supply a comparer through GetComparerForColumn");
    }

    /// <param name="list">The rows, sorted on the column.</param>
    /// <param name="model">The model that reads the column's value from a row.</param>
    /// <param name="columnIdx">The sorted column.</param>
    /// <param name="value">The value to find.</param>
    /// <param name="ascending">Whether <paramref name="list"/> is sorted ascending.</param>
    /// <param name="comparer">optional, in that case the value must be IComparable</param>
    /// <returns>the index where the value is or if &lt; 0 then where it should be ...</returns>
    public static int BinarySearch<T>(IReadOnlyList<T> list,
                                       IGridModel<T> model,
                                       int columnIdx,
                                       object? value,
                                       bool ascending,
                                       IComparer<object>? comparer)
    {
        int low = 0;
        int high = list.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) >> 1;
            T midPoco = list[mid];
            object? midVal = model.GetValue(midPoco, columnIdx);
            int cmp;
            if (Object.ReferenceEquals(midVal, value))
                cmp = 0;
            else if (midVal == null)
                cmp = -1;
            else if (value == null)
                cmp = 1;
            else if (comparer != null)
                cmp = comparer.Compare(midVal, value);
            else
                cmp = ((IComparable)midVal).CompareTo(value);
            if (cmp < 0)
            {
                if (ascending)
                    low = mid + 1;
                else
                    high = mid - 1;
            }
            else if (cmp > 0)
            {
                if (ascending)
                    high = mid - 1;
                else
                    low = mid + 1;
            }
            else
                return mid;  // found
        }
        return -(low + 1); // not found
    }

    /// <summary>The index of the first row from <paramref name="startFrom"/>, onwards or backwards, whose value in the column contains <paramref name="value"/> ignoring case, or equals it; -1 when none does.</summary>
    public static int ContainsSearch<T>(IReadOnlyList<T> list,
                                       IGridModel<T> model,
                                       int columnIdx,
                                       string? value,
                                       bool forward,
                                       int startFrom)
    {
        if (forward)
        {
            for (int i = Math.Max(0, startFrom); i < list.Count; i++)
            {
                T midPojo = list[i];
                string? val = model.GetValue(midPojo, columnIdx)?.ToString();
                if (value == val)  // both null too
                    return i;
                if (value != null && val != null && val.Contains(value, StringComparison.CurrentCultureIgnoreCase))
                    return i;
            }
        }
        else
        {
            for (int i = Math.Min(startFrom, list.Count - 1); i >= 0; i--)
            {
                T midPojo = list[i];
                string? val = model.GetValue(midPojo, columnIdx)?.ToString();
                if (value == val)  // both null too
                    return i;
                if (value != null && val != null && val.Contains(value, StringComparison.CurrentCultureIgnoreCase))
                    return i;
            }
        }
        return -1;
    }

    /// <summary>The comparer <see cref="GetComparerByType"/> gives for the column's value type.</summary>
    public static IComparer<object>? GetComparerForColumn<TGridItem>(IColumn<TGridItem> column) 
    {
        return GetComparerByType(column.GetValueType());
    }

    /// <summary><see cref="Comparer{T}.Default"/> for <paramref name="dataType"/>, its nullable unwrapped; null for <see cref="object"/> and for a type whose values cannot be compared.</summary>
    public static IComparer<object>? GetComparerByType(Type dataType)
    {
        Type type = Nullable.GetUnderlyingType(dataType) ?? dataType;
        if (type == typeof(object) || !CanCompare(type))
            return null;   // object stays on the per-value cast; a type that cannot be compared is refused at registration
        return (IComparer<object>)Activator.CreateInstance(typeof(DefaultComparer<>).MakeGenericType(type))!;
    }

    // The values arrive boxed, nulls already taken by the caller; Comparer<T>.Default picks IComparable<T> over IComparable
    private sealed class DefaultComparer<T> : IComparer<object>
    {
        public int Compare(object? x, object? y) => Comparer<T>.Default.Compare((T)x!, (T)y!);
    }
}
