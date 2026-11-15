using System.Windows;
using System.Windows.Input;
using DonkiePane.Tasks.Plugins;

namespace DonkiePane.App;

/// <summary>Een eenvoudige keuzelijst: een lijst bij een bron, of het soort bron dat je toevoegt.</summary>
public partial class ListPickerWindow : Window
{
    public ListPickerWindow(string title, string intro, IReadOnlyList<TaskListInfo> items)
    {
        InitializeComponent();
        Title = title;
        Intro.Text = intro;
        Items.ItemsSource = items;
        if (items.Count > 0) Items.SelectedIndex = 0;
        OkButton.IsEnabled = items.Count > 0;
    }

    public TaskListInfo? Selected => Items.SelectedItem as TaskListInfo;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        DialogResult = true;
    }

    private void Items_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not null && e.OriginalSource is FrameworkElement) DialogResult = true;
    }
}
