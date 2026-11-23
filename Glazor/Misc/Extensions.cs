using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Glazor.Misc;

/// <summary><c>RequireNotNull</c>, a null check that throws at runtime, and an enum value's <c>GetDescription</c>.</summary>
public static class Extensions
{
    /// <summary>
    /// This is different from ! because if null an exception is thrown, so the condition is enforced at runtime.
    /// </summary>
    /// <returns>
    /// When used with var it is always ?, see:  
    /// https://jeremybytes.blogspot.com/2023/02/c-var-with-reference-types-is-always.html
    /// and: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/declarations#implicitly-typed-local-variables
    /// </returns>
    /// <exception cref="ArgumentNullException"></exception>
    public static T RequireNotNull<T>([NotNull] this T? obj, [CallerArgumentExpression(nameof(obj))] string? objExpression = null)
    {
        return obj ?? throw new ArgumentNullException(objExpression);
    }

    /// <summary>
    /// This is different from ! because if null an exception is thrown, so the condition is enforced at runtime.
    /// </summary>
    /// <returns>
    /// When used with var it is always ?, see:  
    /// https://jeremybytes.blogspot.com/2023/02/c-var-with-reference-types-is-always.html
    /// and: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/declarations#implicitly-typed-local-variables
    /// </returns>
    /// <exception cref="ArgumentNullException"></exception>
    public static T RequireNotNull<T>([NotNull] this T? obj, [CallerArgumentExpression(nameof(obj))] string? objExpression = null)
        where T : struct
    {
        return obj ?? throw new ArgumentNullException(objExpression);
    }

    /// <summary>
    /// To be used with [Description("A description")]
    /// </summary>
    public static string GetDescription(this Enum enumValue)
    {
        var field = enumValue.GetType().GetField(enumValue.ToString());
        if (field == null)
            return enumValue.ToString();
        if (Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attribute)
            return attribute.Description;
        return enumValue.ToString();
    }

    internal static bool IsNumber(this object? value)
    {
        return Utils.IsNumber(value);
    }

    internal static bool IsNumber(this Type type)
    {
        return Utils.IsNumber(type);
    }
}
