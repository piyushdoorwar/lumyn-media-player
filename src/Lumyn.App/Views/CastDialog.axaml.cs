using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lumyn.App.ViewModels;
using Lumyn.Core.Services;

namespace Lumyn.App.Views;

public partial class CastDialog : Window
{
    private readonly MainViewModel? _viewModel;
    private ChromecastDevice? _selectedDevice;

    public CastDialog()
    {
        AvaloniaXamlLoader.Load(this);
        UpdateStateText();
    }

    public CastDialog(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);

        Opened += async (_, _) => await RefreshDevicesAsync();
        UpdateStateText();
    }

    private async Task RefreshDevicesAsync()
    {
        if (_viewModel is null) return;

        SetBusy(true, "Searching for devices...");
        await _viewModel.RefreshCastDevicesAsync();

        var list = this.FindControl<ListBox>("DevicesList");
        if (list is not null && _viewModel.CastDevices.Count > 0 && list.SelectedItem is null)
            list.SelectedIndex = 0;

        SetBusy(false, _viewModel.CastDevices.Count == 0
            ? "No cast devices found."
            : $"{_viewModel.CastDevices.Count} device{(_viewModel.CastDevices.Count == 1 ? "" : "s")} found.");
        UpdateStateText();
    }

    private async Task CastSelectedAsync()
    {
        if (_viewModel is null) return;

        if (_selectedDevice is null)
        {
            SetFooter("Choose a destination first.");
            return;
        }

        SetBusy(true, $"Connecting to {_selectedDevice.Name}...");
        await _viewModel.CastToDeviceAsync(_selectedDevice);
        SetBusy(false, _viewModel.IsCasting
            ? $"Casting to {_selectedDevice.Name}."
            : _viewModel.CastStatusText ?? "Cast failed.");
        UpdateStateText();
    }

    private void DevicesList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedDevice = (sender as ListBox)?.SelectedItem as ChromecastDevice;
        UpdateButtons();
    }

    private async void DevicesList_DoubleTapped(object? sender, TappedEventArgs e)
        => await CastSelectedAsync();

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
        => await RefreshDevicesAsync();

    private async void CastButton_Click(object? sender, RoutedEventArgs e)
        => await CastSelectedAsync();

    private async void DisconnectButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        SetBusy(true, "Disconnecting...");
        await _viewModel.StopCastingAsync();
        SetBusy(false, "Disconnected.");
        UpdateStateText();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
        => Close();

    private void SetBusy(bool busy, string message)
    {
        var refresh = this.FindControl<Button>("RefreshButton");
        var cast = this.FindControl<Button>("CastButton");
        var list = this.FindControl<ListBox>("DevicesList");
        if (refresh is not null) refresh.IsEnabled = !busy;
        if (cast is not null) cast.IsEnabled = !busy && _selectedDevice is not null && _viewModel?.HasMedia == true;
        if (list is not null) list.IsEnabled = !busy;
        SetFooter(message);
    }

    private void SetFooter(string message)
    {
        var footer = this.FindControl<TextBlock>("FooterStatusText");
        if (footer is not null)
            footer.Text = message;

        var devicesStatus = this.FindControl<TextBlock>("DevicesStatusText");
        if (devicesStatus is not null)
            devicesStatus.Text = message;
    }

    private void UpdateStateText()
    {
        var state = this.FindControl<TextBlock>("CastStateText");
        var detail = this.FindControl<TextBlock>("CastDetailText");
        var disconnect = this.FindControl<Button>("DisconnectButton");

        if (state is not null)
            state.Text = _viewModel?.IsCasting == true ? _viewModel.CastStatusText ?? "Casting" : "Not casting";

        if (detail is not null)
        {
            detail.Text = _viewModel?.IsCasting == true
                ? "Choose another destination to switch, or disconnect the current session."
                : _viewModel?.HasMedia == true
                    ? "Choose a destination to cast the current media."
                    : "Open a media file, then choose a device to start casting.";
        }

        if (disconnect is not null)
            disconnect.IsEnabled = _viewModel?.IsCasting == true;

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var cast = this.FindControl<Button>("CastButton");
        if (cast is not null)
            cast.IsEnabled = _selectedDevice is not null && _viewModel?.HasMedia == true;
    }
}
