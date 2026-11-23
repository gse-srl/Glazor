using System.Linq.Expressions;

namespace Glazor.Grds.Props;

/// <summary>
/// Reads a property of a row.
/// </summary>
/// <typeparam name="PT">The poco type</typeparam>
/// <typeparam name="TProp">The poco property type</typeparam>
public interface IGetter<PT, TProp>  // TProp? just in the GetterFunc
{
    /// <summary>The type the value is read from.</summary>
    Type ObjectType { get; }
    /// <summary>The type of the value read.</summary>
    Type PropertyType { get; }
    /// <summary>
    /// The function to read the property value
    /// </summary>
    Func<PT, TProp?> GetterFunc { get; }
}

internal record Getter<PT, TProp>(Func<PT, TProp?> GetterFunc) : IGetter<PT, TProp>
{
    public static readonly Getter<PT, TProp> Default = new(x => default);

    public Type ObjectType { get => typeof(PT); }

    public Type PropertyType { get => typeof(TProp); }
}

/// <summary>
/// A getter with an expression usable by EF core
/// </summary>
public interface IExprGetter<PT, TProp> : IGetter<PT, TProp>
{
    /// <summary>
    /// The expression to read the property value
    /// </summary>
    Expression<Func<PT, TProp?>> GetterExpr { get; }
}

internal record ExprGetter<PT, TProp>(Expression<Func<PT, TProp?>> GetterExpr, Func<PT, TProp?> GetterFunc) : Getter<PT, TProp?>(GetterFunc), IExprGetter<PT, TProp>
{
}
