using System.Windows;
using System.Windows.Input;
using DonkiePane.Tasks.Google;

namespace DonkiePane.App;

/// <summary>Laat de Google Taken-lijsten zien en laat er één kiezen.</summary>
public partial class ListPickerWindow : Window
{
    public ListPickerWindow(IReadOnlyList<GoogleTaskList> lists)
    {
        InitializeComponent();
        Lists.ItemsSource = lists;
        if (lists.Count > 0) Lists.SelectedIndex = 0;
        OkButton.IsEnabled = lists.Count > 0;
    }

    public GoogleTaskList? Selected => Lists.SelectedItem as GoogleTaskList;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        DialogResult = true;
    }

    private void Lists_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not null && e.OriginalSource is FrameworkElement) DialogResult = true;
    }
}
