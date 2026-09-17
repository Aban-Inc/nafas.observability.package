using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Nafas.Observability.Server;

namespace Nafas.Observability.Server.Sample;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // Injected by the Generic Host's DI container (see App.xaml.cs) -- the
    // SAME singleton instance AddNafasHttpServer registered, so
    // Start/StopAsync here control the one real listener, not a copy of it.
    private readonly NafasHttpServer _server;

    public MainWindow(NafasHttpServer server)
    {
        InitializeComponent();
        _server = server;
    }

    // Checking the box starts the listener immediately with whatever's
    // currently in the Port/Access fields; unchecking it stops the listener.
    // This is the "user sets it to true" toggle from a settings screen that
    // Nafas.Observability.Server's own NafasHttpServerOptions.Enabled can't
    // cover by itself, since that's only read once at process startup.
    private async void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (EnabledCheckBox.IsChecked == true)
        {
            await StartFromFieldsAsync();
        }
        else
        {
            await StopAsync();
        }
    }

    // Lets the user change the port/access mode while already enabled --
    // NafasHttpServer.StartAsync stops the previous listener first, so
    // calling it again is a clean restart, not a second, conflicting one.
    private async void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        if (EnabledCheckBox.IsChecked == true)
        {
            await StartFromFieldsAsync();
        }
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        if (_server.Port is int port)
        {
            Process.Start(new ProcessStartInfo($"http://localhost:{port}/nafas") { UseShellExecute = true });
        }
    }

    private async Task StartFromFieldsAsync()
    {
        if (!int.TryParse(PortTextBox.Text, out var port) || port is <= 0 or > 65535)
        {
            StatusText.Text = "Enter a valid port (1-65535).";
            EnabledCheckBox.IsChecked = false;
            return;
        }

        var accessMode = LanRadio.IsChecked == true ? NafasHttpServerAccessMode.Lan : NafasHttpServerAccessMode.LocalhostOnly;

        SetControlsEnabled(false);
        StatusText.Text = "Starting...";

        try
        {
            await _server.StartAsync(port, accessMode, dashboardPath: "/nafas");

            StatusText.Text = accessMode == NafasHttpServerAccessMode.Lan
                ? $"Running -- http://localhost:{port}/nafas (also reachable via this machine's own IP from other devices on the network, once NafasServerOptions.Authorize allows it)"
                : $"Running -- http://localhost:{port}/nafas";
            OpenButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to start: {ex.Message}";
            EnabledCheckBox.IsChecked = false;
        }
        finally
        {
            SetControlsEnabled(true);
        }
    }

    private async Task StopAsync()
    {
        SetControlsEnabled(false);
        StatusText.Text = "Stopping...";

        await _server.StopAsync();

        StatusText.Text = "Stopped.";
        OpenButton.IsEnabled = false;
        SetControlsEnabled(true);
    }

    private void SetControlsEnabled(bool enabled)
    {
        PortTextBox.IsEnabled = enabled;
        LocalhostOnlyRadio.IsEnabled = enabled;
        LanRadio.IsEnabled = enabled;
        ApplyButton.IsEnabled = enabled;
        EnabledCheckBox.IsEnabled = enabled;
    }
}
