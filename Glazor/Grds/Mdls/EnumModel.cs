using Glazor.Misc;

namespace Glazor.Grds.Mdls;

/// <summary>A model over the members of an enum, one row per value with its description, for pick lists.</summary>
public class EnumModel : DataGridModel<Enum>
{
    /// <param name="enumType">The enum, nullable or not.</param>
    /// <param name="texts">The texts for the column label; the built-in ones without.</param>
    public EnumModel(Type enumType, IGlazorTexts? texts = null)
    {
        Type etype = Nullable.GetUnderlyingType(enumType) ?? enumType;
        if (!etype.IsEnum)
            throw new ArgumentException($"Not an enum: {enumType}");
        ItemKeyFn = x => x != null ? x : NoValue.Value;
        foreach (Enum x in Enum.GetValues(etype).Cast<Enum>().Distinct())   // one row per value: an alias is another name for one
        {
            GetData().Add(x);
        }
        AddColumn("description", new Column<Enum, string?>((texts ?? GlazorTexts.Default).Get(TextKeys.Value))
        {
            Alignment = Alignment.Center,
            PreferredSize = 20,
        }, v => v?.GetDescription());
    }
}
