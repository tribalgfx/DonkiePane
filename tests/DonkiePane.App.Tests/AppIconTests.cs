using System.IO;
using DonkiePane.App;

namespace DonkiePane.App.Tests;

public sealed class AppIconTests
{
    [Fact]
    public void The_icon_is_built_into_the_program_and_loads_at_the_tray_size()
    {
        using var icon = TrayIconFactory.CreateAppIcon();

        Assert.True(icon.Width >= 16);
        Assert.True(icon.Height >= 16);
    }

    [Fact]
    public void The_icon_file_contains_every_size_up_to_256()
    {
        using var stream = typeof(TrayIconFactory).Assembly.GetManifestResourceStream(TrayIconFactory.ResourceName)!;
        using var reader = new BinaryReader(stream);

        Assert.Equal(0, reader.ReadUInt16());   // gereserveerd
        Assert.Equal(1, reader.ReadUInt16());   // type: icoon
        var count = reader.ReadUInt16();
        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var width = reader.ReadByte();
            reader.ReadBytes(15);               // rest van de regel
            sizes.Add(width == 0 ? 256 : width);
        }

        foreach (var needed in new[] { 16, 24, 32, 48, 256 }) Assert.Contains(needed, sizes);
    }
}
