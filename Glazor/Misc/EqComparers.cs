namespace Glazor.Misc;

/// <summary>
/// Builds <see cref="IEqualityComparer{T}"/>s: for collections compared by their elements, for an object compared by a
/// property, and from others combined. <see cref="Comparers"/> does the same for ordering.
/// </summary>
/// <remarks>
/// Read this first, especially if you came from Java:
/// https://sasan-salem.medium.com/mystery-of-equality-in-c-iequatable-t-iequalitycomparer-t-icomparable-t-icomparer-t-ab98bd2fe541<br/>
/// and remember that Collection.Equals() does not check its elements in c#.
/// </remarks>
public static class EqComparers
{
    /// <summary>
    /// Compares all items of an enumerable, dictionaries included, using <paramref name="itemComparer"/>.
    /// </summary>
    /// <param name="first">The first enumerable.</param>
    /// <param name="second">The second enumerable.</param>
    /// <param name="itemComparer">when null the default comparer</param>
    private static bool ItemsEqual<T>(/*this*/ IEnumerable<T> first, IEnumerable<T> second, IEqualityComparer<T>? itemComparer)
    {
        if (first == second)
            return true;
        if (first.Count() != second.Count())
            return false;
        return new HashSet<T>(first, itemComparer).SetEquals(second);   // both ways: an Except one way misses what the other holds
    }

    /// <summary>Compares two hash sets by the items they hold, in any order: <see cref="HashSet{T}.CreateSetComparer"/>.</summary>
    public static IEqualityComparer<HashSet<T>> ForHashSet<T>()
    {
        return HashSet<T>.CreateSetComparer();
    }

    /// <summary>Compares two sorted sets by the items they hold: <see cref="SortedSet{T}.CreateSetComparer()"/>.</summary>
    public static IEqualityComparer<SortedSet<T>> ForSortedSet<T>()
    {
        return SortedSet<T>.CreateSetComparer();
    }

    /// <summary>Compares two sorted sets by the items they hold, each compared with <paramref name="itemsComparer"/>.</summary>
    public static IEqualityComparer<SortedSet<T>> ForSortedSet<T>(IEqualityComparer<T> itemsComparer)
    {
        return SortedSet<T>.CreateSetComparer(itemsComparer);
    }

    /// <summary>
    /// A propComparer that compares all the items in the enumerable
    /// </summary>
    public static IEqualityComparer<IEnumerable<T>> ForEnumerable<T>()
    {
        return ForEnumerable<T>(null);
    }

    /// <summary>
    /// A propComparer that applies the itemComparer to all the objects in the enumerable
    /// </summary>
    public static IEqualityComparer<IEnumerable<T>> ForEnumerable<T>(IEqualityComparer<T>? itemComparer)
    {
        return EqualityComparer<IEnumerable<T>>.Create((x, y) =>
        {
            if (ReferenceEquals(x, y))  // both null also
                return true;
            if (x == null || y == null)
                return false;
            return x.SequenceEqual(y, itemComparer);
        },
        xx =>
        {
            HashCode hc = new();
            if (xx != null)
            {
                if (itemComparer == null)
                {
                    foreach (var p in xx)
                        hc.Add(p);
                }
                else
                {
                    foreach (var p in xx)
                        hc.Add(p, itemComparer);
                }
            }
            return hc.ToHashCode();
        });
    }

    /// <summary>
    /// A propComparer for a member that is a set: membership decides equality, the order the elements happen
    /// to come out in does not. Use this rather than <see cref="ForEnumerable{T}()"/> whenever the member is a
    /// HashSet or an IReadOnlySet, because the enumeration order of a hash set follows its insertion and
    /// removal history, so two sets holding the same elements can enumerate differently.
    /// </summary>
    public static IEqualityComparer<IEnumerable<T>> ForSet<T>()
    {
        return ForSet<T>(null);
    }

    /// <summary>
    /// A propComparer that applies the itemComparer to the elements of a set, ignoring their order.
    /// </summary>
    public static IEqualityComparer<IEnumerable<T>> ForSet<T>(IEqualityComparer<T>? itemComparer)
    {
        return EqualityComparer<IEnumerable<T>>.Create((x, y) =>
        {
            if (ReferenceEquals(x, y))  // both null also
                return true;
            if (x == null || y == null)
                return false;
            return new HashSet<T>(x, itemComparer).SetEquals(y);
        },
        xx =>
        {
            if (xx == null)
                return 0;
            // Xor so the result does not depend on the order, over the distinct elements so it stays
            // consistent with the SetEquals above, which ignores repetitions.
            int hash = 0;
            foreach (T item in new HashSet<T>(xx, itemComparer))
            {
                if (item == null)
                    continue;
                hash ^= itemComparer == null ? item.GetHashCode() : itemComparer.GetHashCode(item);
            }
            return hash;
        });
    }

    /// <summary>
    /// A propComparer that compares all the items in the list
    /// </summary>
    public static IEqualityComparer<IReadOnlyList<T>> ForList<T>()
    {
        return ForList<T>(null);
    }

    /// <summary>
    /// A propComparer that applies the itemComparer to all the objects in the list
    /// </summary>
    public static IEqualityComparer<IReadOnlyList<T>> ForList<T>(IEqualityComparer<T>? itemComparer)
    {
        return ForEnumerable(itemComparer);
    }

    /// <summary>
    /// A propComparer that compares all the KeyValuePairs in the dictionary, their position is not considered.
    /// </summary>
    public static IEqualityComparer<IReadOnlyDictionary<K, V>> ForDictionary<K, V>()
    {
        return ForDictionary<K, V>(null);
    }

    /// <summary>
    /// A propComparer that applies the itemComparer to all the KeyValuePairs in the dictionary, their position is not considered.
    /// </summary>
    public static IEqualityComparer<IReadOnlyDictionary<K,V>> ForDictionary<K,V>(IEqualityComparer<KeyValuePair<K, V>>? itemComparer)
    {
        return EqualityComparer<IReadOnlyDictionary<K,V>>.Create((x, y) =>
        {
            if (ReferenceEquals(x, y))  // both null also
                return true;
            if (x == null || y == null)
                return false;
            // KeyValuePair are compared here 
            return ItemsEqual(x, y, itemComparer);
        },
        xx =>
        {
            if (xx == null)
                return 0;
            // Xor so the result does not depend on the order, over the entries distinct under itemComparer so it stays
            // consistent with ItemsEqual, which ignores repetitions.
            IEnumerable<KeyValuePair<K, V>> entries = itemComparer == null ? xx : new HashSet<KeyValuePair<K, V>>(xx, itemComparer);
            int hash = 0;
            foreach (var item in entries)
            {
                hash ^= itemComparer == null ? (item.Key?.GetHashCode() ?? 0) : itemComparer.GetHashCode(item);
            }
            return hash;
        });
    }

    /// <summary>
    /// For a KeyValuePair, compares both the key and the value
    /// </summary>
    public static IEqualityComparer<KeyValuePair<K, V>> KeyValuePair<K, V>(IEqualityComparer<K> kcomp, IEqualityComparer<V> vcomp)
    {
        ArgumentNullException.ThrowIfNull(kcomp);
        ArgumentNullException.ThrowIfNull(vcomp);
        return EqualityComparer<KeyValuePair<K, V>>.Create((x, y) => kcomp.Equals(x.Key, y.Key) && vcomp.Equals(x.Value, y.Value),
            xx => (xx.Key == null ? 0 : kcomp.GetHashCode(xx.Key)) ^ (xx.Value == null ? 0 : vcomp.GetHashCode(xx.Value)));
    }

    /// <summary>
    /// Uses the default <see cref="EqualityComparer{T}"/>.
    /// </summary>
    public static IEqualityComparer<P> Property<P, T>(Func<P?, T?> getProp)
    {
        return Property(getProp, EqualityComparer<T>.Default);
    }

    /// <summary>
    /// For records and classes with special needs (ie. a collection property), first extracts the property and then applies the propComparer on it
    /// </summary>
    public static IEqualityComparer<P> Property<P, T>(Func<P?, T?> getProp, IEqualityComparer<T> propComparer)
    {
        ArgumentNullException.ThrowIfNull(getProp);
        ArgumentNullException.ThrowIfNull(propComparer);
        return EqualityComparer<P>.Create((x, y) => propComparer.Equals(getProp(x), getProp(y)),
            x => getProp(x) switch { T o => propComparer.GetHashCode(o), null => 0 });
    }

    /// <summary>What <see cref="Combo{P}(Action{PropsComparers{P}})"/> hands its initializer: <c>Add</c> one property after another.</summary>
    public class PropsComparers<P>
    {
        internal List<IEqualityComparer<P>> propComps = [];

        internal PropsComparers()
        {
        }

        /// <summary>
        /// Uses the default <see cref="EqualityComparer{T}"/>.
        /// </summary>
        public PropsComparers<P> Add<T>(Func<P?, T?> getProp)
        {
            ArgumentNullException.ThrowIfNull(getProp);
            propComps.Add(Property(getProp));
            return this;
        }

        /// <summary>
        /// Uses <paramref name="propComparer"/>.
        /// </summary>
        public PropsComparers<P> Add<T>(Func<P?, T?> getProp, IEqualityComparer<T> propComparer)
        {
            ArgumentNullException.ThrowIfNull(getProp);
            ArgumentNullException.ThrowIfNull(propComparer);
            propComps.Add(Property(getProp, propComparer));
            return this;
        }
    }

    /// <summary>
    /// Combines more Property compares of the same containing object.
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public static IEqualityComparer<P> Combo<P>(Action<PropsComparers<P>> initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer);
        PropsComparers<P> pcomps = new();
        initializer.Invoke(pcomps);
        switch (pcomps.propComps.Count)
        {
            case 0: throw new ArgumentException("No property comparers provided");
            case 1: return pcomps.propComps[0];
            case 2: return Combo(pcomps.propComps[0], pcomps.propComps[1]);
            default:
                return Combo(pcomps.propComps[0], pcomps.propComps[1], pcomps.propComps.Skip(2));
        }
    }

    /// <summary>
    /// Combines more Property compares of the same containing object.
    /// </summary>
    /// <param name="c1">The comparer the property comparers are combined onto; returned unchanged if <paramref name="initializer"/> adds none.</param>
    /// <param name="initializer">Adds the property comparers, one <c>Add</c> each.</param>
    /// <exception cref="ArgumentException"></exception>
    public static IEqualityComparer<P> Combo<P>(IEqualityComparer<P> c1, Action<PropsComparers<P>> initializer)
    {
        ArgumentNullException.ThrowIfNull(c1);
        ArgumentNullException.ThrowIfNull(initializer);
        PropsComparers<P> pcomps = new();
        initializer.Invoke(pcomps);
        switch (pcomps.propComps.Count)
        {
            case 0: return c1;
            case 1: return Combo(c1, pcomps.propComps[0]);
            default:
                return Combo(c1, pcomps.propComps[0], pcomps.propComps.Skip(1));
        }
    }

    /// <summary>
    /// Combines two comparers in one, mostly used with <c>Property</c>.
    /// </summary>
    public static IEqualityComparer<T> Combo<T>(IEqualityComparer<T> c1, IEqualityComparer<T> c2)
    {
        ArgumentNullException.ThrowIfNull(c1);
        ArgumentNullException.ThrowIfNull(c2);
        return EqualityComparer<T>.Create((x, y) => c1.Equals(x, y) && c2.Equals(x, y),
            xx =>
            {
                HashCode hc = new();
                hc.Add(xx, c1);
                hc.Add(xx, c2);
                return hc.ToHashCode();
            });
    }

    /// <summary>
    /// Combines two or more comparers in one, mostly used with <c>Property</c>.
    /// </summary>
    /// <param name="c1">The first comparer; the values are equal only if every comparer finds them equal.</param>
    /// <param name="c2">The second comparer.</param>
    /// <param name="comparers">Any further comparers, consulted in order after <paramref name="c1"/> and <paramref name="c2"/>.</param>
    public static IEqualityComparer<T> Combo<T>(IEqualityComparer<T> c1, IEqualityComparer<T> c2,
        params IEnumerable<IEqualityComparer<T>> comparers)
    {
        IEqualityComparer<T> comparer = Combo(c1, c2);
        List<IEqualityComparer<T>> cs = [.. comparers];
        if (cs.Count == 0)
            return comparer;
        return EqualityComparer<T>.Create((x, y) =>
        {
            if (!comparer.Equals(x, y))
                return false;
            foreach (var c in cs)
                if (!c.Equals(x, y))
                    return false;
            return true;
        },
        xx =>
        {
            HashCode hc = new();
            hc.Add(xx, c1);
            hc.Add(xx, c2);
            foreach (var c in cs)
                hc.Add(xx, c);
            return hc.ToHashCode();
        });
    }
}

