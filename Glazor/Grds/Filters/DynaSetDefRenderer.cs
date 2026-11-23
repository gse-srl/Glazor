namespace Glazor.Grds.Filters;

/// <summary>The editor of a <see cref="DynaSetDef{SELF, VT, KT}"/>, whose rows are the column's values.</summary>
public class DynaSetDefRenderer<SELF, VT, KT> : ADynaSetDefRenderer<SELF, VT, VT, KT> where SELF : DynaSetDef<SELF, VT, KT> where VT : class
{
}
