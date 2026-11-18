using Microsoft.Win32;

namespace DonkiePane.App.Tests;

/// <summary>Werkt op een eigen testsleutel, nooit op de echte Run-sleutel.</summary>
[Collection("AutoStart")] // AutoStart.Root is gedeeld: niet tegelijk draaien
public sealed class AutoStartTests : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string Exe = @"C:\Programma\DonkiePane.exe";

    private readonly string _testPath = @"Software\DonkiePane.Tests." + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;
    private readonly RegistryKey _previous;

    public AutoStartTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_testPath);
        _previous = AutoStart.Root;
        AutoStart.Root = _root;
    }

    public void Dispose()
    {
        AutoStart.Root = _previous;
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_testPath, throwOnMissingSubKey: false);
    }

    private byte[]? Approval()
    {
        using var key = _root.OpenSubKey(ApprovedKey);
        return key?.GetValue("DonkiePane") as byte[];
    }

    private void SetApproval(params byte[] value)
    {
        using var key = _root.CreateSubKey(ApprovedKey);
        key.SetValue("DonkiePane", value, RegistryValueKind.Binary);
    }

    private void SetRunOnly()
    {
        using var key = _root.CreateSubKey(RunKey);
        key.SetValue("DonkiePane", $"\"{Exe}\"");
    }

    [Fact]
    public void Turning_it_on_writes_the_run_value_and_marks_it_approved()
    {
        AutoStart.SetEnabled(true, Exe);

        Assert.True(AutoStart.IsEnabled());
        Assert.Equal(Exe, AutoStart.RegisteredPath());
        Assert.Equal(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, Approval());
    }

    [Fact]
    public void Turning_it_off_removes_both_values()
    {
        AutoStart.SetEnabled(true, Exe);
        AutoStart.SetEnabled(false);

        Assert.False(AutoStart.IsEnabled());
        Assert.Null(AutoStart.RegisteredPath());
        Assert.Null(Approval());
    }

    [Fact]
    public void Disabled_in_task_manager_counts_as_off()
    {
        AutoStart.SetEnabled(true, Exe);
        SetApproval(3, 0, 0, 0, 0xA3, 0x03, 0x2F, 0x3B, 0x04, 0x76, 0xDD, 0x01);

        Assert.False(AutoStart.IsEnabled());
    }

    [Fact]
    public void Turning_it_on_again_overrides_a_task_manager_disable()
    {
        AutoStart.SetEnabled(true, Exe);
        SetApproval(3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        AutoStart.SetEnabled(true, Exe);

        Assert.True(AutoStart.IsEnabled());
        Assert.Equal(2, Approval()![0]);
    }

    [Fact]
    public void Repair_adds_the_missing_approval_of_an_older_version()
    {
        SetRunOnly();

        Assert.True(AutoStart.RepairApproval());
        Assert.Equal(2, Approval()![0]);
        Assert.False(AutoStart.RepairApproval()); // daarna niets meer te doen
    }

    [Fact]
    public void Repair_leaves_a_task_manager_disable_alone()
    {
        SetRunOnly();
        SetApproval(3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.False(AutoStart.RepairApproval());
        Assert.Equal(3, Approval()![0]);
    }

    [Fact]
    public void Repair_does_nothing_when_autostart_is_off()
    {
        Assert.False(AutoStart.RepairApproval());
        Assert.Null(Approval());
    }
}
