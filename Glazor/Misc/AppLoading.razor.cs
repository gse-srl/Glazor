namespace Glazor.Misc;

/// <summary>
/// The wheel a page shows, placed in <c>App.razor</c> outside <c>Routes</c>, until the first render brings the layout:
/// <c>glazor.css</c> hides it once the layout's <see cref="Spinner"/> is in the page, or once Blazor shows its
/// <c>#blazor-error-ui</c>, which for a failure of that first render must be in <c>App.razor</c> too.
/// </summary>
public partial class AppLoading;
