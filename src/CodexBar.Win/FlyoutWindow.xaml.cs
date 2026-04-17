using System.Windows;
using System.Windows.Threading;

namespace CodexBar.Win;

public partial class FlyoutWindow : Window
{
    private readonly MainFlyoutViewModel _viewModel = new();
    private readonly DispatcherTimer _autoRefreshTimer;

    public FlyoutWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        Loaded += async (_, _) => await _viewModel.RefreshAsync();
        Loaded += (_, _) => _autoRefreshTimer.Start();
        Closed += (_, _) => _autoRefreshTimer.Stop();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await _viewModel.RefreshAsync();

    private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible || !_viewModel.CanInteract)
        {
            return;
        }

        await _viewModel.RefreshAsync();
    }

    private async void UseSelected_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsList.SelectedItem is AccountListItem item)
        {
            await _viewModel.UseAsync(item);
        }
    }

    private async void LaunchCodex_Click(object sender, RoutedEventArgs e)
        => await _viewModel.LaunchCodexAsync(AccountsList.SelectedItem as AccountListItem);

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsList.SelectedItem is AccountListItem item)
        {
            await _viewModel.DeleteAsync(item);
        }
    }

    private async void EditSelected_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsList.SelectedItem is not AccountListItem item)
        {
            return;
        }

        var editContext = await _viewModel.GetEditContextAsync(item);
        if (editContext is null)
        {
            return;
        }

        var dialog = new EditAccountWindow(editContext.Provider, editContext.Account);
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            await _viewModel.EditAsync(dialog.Result);
        }
    }

    private async void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsList.SelectedItem is AccountListItem item)
        {
            await _viewModel.MoveAsync(item, -1);
        }
    }

    private async void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsList.SelectedItem is AccountListItem item)
        {
            await _viewModel.MoveAsync(item, 1);
        }
    }

    private async void AddCompatible_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddCompatibleWindow();
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            await _viewModel.AddCompatibleAsync(dialog.Result);
        }
    }

    private async void OAuth_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OAuthDialog();
        if (dialog.ShowDialog() == true && dialog.Tokens is not null)
        {
            await _viewModel.AddOpenAiOAuthAsync(dialog.Tokens, dialog.AccountLabel);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
        => new SettingsWindow().Show();

    private void Quit_Click(object sender, RoutedEventArgs e)
        => System.Windows.Application.Current.Shutdown();
}
