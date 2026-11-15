using System.Windows.Media;

namespace DonkiePane.App;

/// <summary>Wat een pane van één bureaubladitem laat zien.</summary>
public sealed record ItemView(string Id, string Path, string DisplayName, ImageSource? Icon);
