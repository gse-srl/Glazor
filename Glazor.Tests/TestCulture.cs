using System.Globalization;

namespace Glazor.Tests;

/// <summary>
/// The suite asserts the components' English texts, so it pins the UI culture whatever the machine's is;
/// <see cref="LocalizationTests"/> switches it around its own asserts.
/// </summary>
[SetUpFixture]
public class TestCulture
{
    [OneTimeSetUp]
    public void PinEnglish()
    {
        CultureInfo english = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = english;
        CultureInfo.CurrentUICulture = english;
    }
}
