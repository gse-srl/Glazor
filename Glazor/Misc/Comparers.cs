namespace Glazor.Misc;

/// <summary>Builds an <see cref="IComparer{T}"/> from others, each deciding only the ties of the one before. <see cref="EqComparers"/> does the same for equality.</summary>
public static class Comparers
{
    /// <summary>
    /// Combines two comparers in one.
    /// </summary>
    public static IComparer<T> Combo<T>(IComparer<T> c1, IComparer<T> c2)
    {
        ArgumentNullException.ThrowIfNull(c1);
        ArgumentNullException.ThrowIfNull(c2);
        return Comparer<T>.Create((v1, v2) =>
        {
            int res = c1.Compare(v1, v2);
            if (res != 0)
                return res;
            return c2.Compare(v1, v2);
        });
    }

    /// <summary>
    /// Combines two or more comparers in one.
    /// </summary>
    /// <param name="c1">The comparer consulted first.</param>
    /// <param name="c2">The comparer consulted when <paramref name="c1"/> finds the values equal.</param>
    /// <param name="comparers">Any further comparers, consulted in order after <paramref name="c1"/> and <paramref name="c2"/>.</param>
    public static IComparer<T> Combo<T>(IComparer<T> c1, IComparer<T> c2,
        params IEnumerable<IComparer<T>> comparers)
    {
        IComparer<T> comparer = Combo(c1, c2);
        List<IComparer<T>> cs = [.. comparers];
        if (cs.Count == 0)
            return comparer;
        return Comparer<T>.Create((v1, v2) =>
        {
            int res = comparer.Compare(v1, v2);
            if (res != 0)
                return res;
            foreach (var c in cs)
            {
                res = c.Compare(v1, v2);
                if (res != 0)
                    return res;
            }
            return res;
        });
    }
}
