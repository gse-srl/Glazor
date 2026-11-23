namespace Glazor.Grds.Filters;

/// <summary>The editor of a <see cref="DynaValuesInSetDef{SELF, VT, KT}"/>, whose rows are the collection's items.</summary>
public class DynaValuesInSetDefRenderer<SELF, VT, KT> : ADynaValuesInSetDefRenderer<SELF, VT, VT, KT> where SELF : DynaValuesInSetDef<SELF, VT, KT> where VT : class
{
}
