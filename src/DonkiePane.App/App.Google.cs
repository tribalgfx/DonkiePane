using System.Windows;
using DonkiePane.Core;
using DonkiePane.Tasks;

namespace DonkiePane.App;

/// <summary>Takenpanes met Google Taken als bron.</summary>
public partial class App
{
    private ITaskProvider TaskProviderFor(Pane pane) =>
        pane.TaskSource == TaskSources.Google ? _google.Provider : _localTasks;

    private bool _addingGooglePane;

    /// <summary>
    /// Logt zo nodig in bij Google (in de browser), laat een lijst kiezen en maakt er een pane van.
    /// Elke stap kan mislukken (geen client-ID, geweigerd, geen verbinding); dat wordt gemeld, niets gaat stuk.
    /// </summary>
    private async Task AddGoogleTaskPaneAsync()
    {
        if (_addingGooglePane) return; // niet twee keer tegelijk inloggen
        _addingGooglePane = true;
        try
        {
            if (!_google.IsSignedIn)
            {
                if (string.IsNullOrWhiteSpace(_google.ClientId))
                {
                    MessageBox.Show("Vul eerst het Google client-ID in bij Instellingen.", "DonkiePane",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    ShowSettings();
                    return;
                }

                var go = MessageBox.Show(
                    "DonkiePane opent nu de inlogpagina van Google in je browser. Log daar in met je werkaccount en geef toestemming voor Google Taken.",
                    "Inloggen bij Google", MessageBoxButton.OKCancel, MessageBoxImage.Information);
                if (go != MessageBoxResult.OK) return;

                await _google.SignInAsync();
            }

            var lists = await _google.GetListsAsync();
            var picker = new ListPickerWindow(lists);
            if (picker.ShowDialog() == true && picker.Selected is { } chosen)
                AddPane(Pane.NewGoogleTaskPane(chosen.Id, chosen.Title));
        }
        catch (TaskProviderException ex)
        {
            MessageBox.Show(ex.Message, "Google Taken", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _addingGooglePane = false;
        }
    }
}
