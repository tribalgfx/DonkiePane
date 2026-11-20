using System.Windows.Markup;
using DonkiePane.Core;

namespace DonkiePane.App;

/// <summary>
/// Vertaalde tekst in XAML: <c>Text="{l:T 'Nieuwe taak toevoegen'}"</c> (met <c>xmlns:l="clr-namespace:DonkiePane.App"</c>).
/// De Nederlandse tekst is de sleutel, zie <see cref="Loc"/>. De taal geldt vanaf het opbouwen van het venster.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string text) : MarkupExtension
{
    public string Text { get; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
