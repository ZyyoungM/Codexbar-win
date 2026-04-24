using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexBar.Core;
using CodexBar.Runtime;

namespace CodexBar.Win;

public partial class FlyoutWindow : Window
{
    private readonly MainFlyoutViewModel _viewModel;
    private readonly DispatcherTimer _autoRefreshTimer;
    private readonly Action? _showSettingsRequested;
    private readonly Action? _toggleOverlayRequested;
    private System.Windows.Point _dragStartPoint;
    private AccountListItem? _draggedAccount;
    private ListBoxItem? _draggedContainer;
    private bool _isDraggingAccount;
    private ScrollViewer? _accountsScrollViewer;
    private System.Windows.Point _dragPreviewOffset;

    public FlyoutWindow()
        : this(new MainFlyoutViewModel())
    {
    }

    public FlyoutWindow(
        MainFlyoutViewModel viewModel,
        Action? showSettingsRequested = null,
        Action? toggleOverlayRequested = null)
    {
        _viewModel = viewModel;
        _showSettingsRequested = showSettingsRequested;
        _toggleOverlayRequested = toggleOverlayRequested;

        InitializeComponent();
        DataContext = _viewModel;
        _accountsScrollViewer = FindDescendant<ScrollViewer>(AccountsList);
        AccountsList.PreviewMouseMove += AccountsList_PreviewMouseMove;
        AccountsList.PreviewMouseLeftButtonUp += AccountsList_PreviewMouseLeftButtonUp;
        AccountsList.LostMouseCapture += AccountsList_LostMouseCapture;

        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;

        Loaded += async (_, _) =>
        {
            await _viewModel.LoadInitialAsync();
            _ = _viewModel.RefreshOfficialQuotaInBackgroundAsync();
        };
        Loaded += (_, _) => _autoRefreshTimer.Start();
        Closed += (_, _) => _autoRefreshTimer.Stop();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await _viewModel.RefreshAsync();

    private async void ProbeAll_Click(object sender, RoutedEventArgs e)
        => await _viewModel.RefreshQuotaAndApisAsync();

    private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible || !_viewModel.CanInteract)
        {
            return;
        }

        await _viewModel.RefreshAsync();
    }

    private async void ManualRouting_Click(object sender, RoutedEventArgs e)
        => await _viewModel.SetRoutingModeAsync(OpenAiAccountMode.ManualSwitch);

    private async void AutomaticRouting_Click(object sender, RoutedEventArgs e)
        => await _viewModel.SetRoutingModeAsync(OpenAiAccountMode.AggregateGateway);

    private async void UseAccount_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveAccountItem(sender) is { } item)
        {
            await _viewModel.UseAsync(item);
        }
    }

    private async void LaunchAccount_Click(object sender, RoutedEventArgs e)
        => await LaunchOrRestartCodexAsync(ResolveAccountItem(sender));

    private async void ActiveLaunch_Click(object sender, RoutedEventArgs e)
        => await LaunchOrRestartCodexAsync(null);

    private void Overlay_Click(object sender, RoutedEventArgs e)
        => _toggleOverlayRequested?.Invoke();

    private async Task LaunchOrRestartCodexAsync(AccountListItem? item)
    {
        var desktopStatus = await _viewModel.GetCodexDesktopStatusAsync();
        if (desktopStatus.IsRunning)
        {
            if (!await ConfirmRestartCodexDesktopAsync())
            {
                return;
            }

            if (item is not null)
            {
                await _viewModel.SwitchAndRestartCodexDesktopAsync(item);
            }
            else
            {
                await _viewModel.RestartActiveCodexDesktopAsync();
            }

            return;
        }

        await _viewModel.LaunchCodexAsync(item);
    }

    private async Task<bool> ConfirmRestartCodexDesktopAsync()
    {
        if (await _viewModel.IsRestartConfirmationSuppressedAsync())
        {
            return true;
        }

        var dialog = new RestartCodexConfirmationDialog
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        if (dialog.DoNotAskAgain)
        {
            await _viewModel.SetRestartConfirmationSuppressedAsync(true);
        }

        return true;
    }

    private async void ProbeAccount_Click(object sender, RoutedEventArgs e)
        => await _viewModel.ProbeCompatibleApisAsync(ResolveAccountItem(sender));

    private async void RefreshOfficialQuota_Click(object sender, RoutedEventArgs e)
        => await _viewModel.RefreshOfficialQuotaAsync(ResolveAccountItem(sender));

    private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveAccountItem(sender) is { } item)
        {
            await _viewModel.DeleteAsync(item);
        }
    }

    private async void EditAccount_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveAccountItem(sender) is not AccountListItem item)
        {
            return;
        }

        await OpenEditDialogAsync(item);
    }

    private void AccountCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<System.Windows.Controls.Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        BeginAccountDragCandidate(sender as DependencyObject, e);
    }

    private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginAccountDragCandidate(sender as DependencyObject, e);
        e.Handled = true;
    }

    private void DragHandle_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        => HandleAccountDragMove(e);

    private void AccountsList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        => HandleAccountDragMove(e);

    private async void AccountsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingAccount)
        {
            await CompleteAccountDragAsync();
            e.Handled = true;
            return;
        }

        ResetAccountDragState();
    }

    private void AccountsList_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingAccount)
        {
            ResetAccountDragState();
        }
    }

    private void HandleAccountDragMove(System.Windows.Input.MouseEventArgs e)
    {
        if (_draggedAccount is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetAccountDragState();
            return;
        }

        var position = e.GetPosition(AccountsList);
        if (!_isDraggingAccount)
        {
            if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _isDraggingAccount = true;
            Mouse.Capture(AccountsList);
            AccountsList.Cursor = System.Windows.Input.Cursors.SizeAll;
            UpdateDraggedContainerReference();
            if (_draggedContainer is not null)
            {
                _draggedContainer.Opacity = 0.55;
            }

            ShowDragPreview(e.GetPosition(RootGrid));
        }

        UpdateDragPreviewPosition(e.GetPosition(RootGrid));
        AutoScrollAccounts(position);

        var currentIndex = AccountsList.Items.IndexOf(_draggedAccount);
        var targetIndex = GetAccountInsertIndex(position);
        if (currentIndex < 0 || targetIndex < 0 || currentIndex == targetIndex)
        {
            return;
        }

        _viewModel.Accounts.Move(currentIndex, targetIndex);
        UpdateDraggedContainerReference();
        if (_draggedContainer is not null)
        {
            _draggedContainer.Opacity = 0.55;
        }
        AccountsList.SelectedItem = _draggedAccount;
        AccountsList.ScrollIntoView(_draggedAccount);
        e.Handled = true;
    }

    private async Task CompleteAccountDragAsync()
    {
        if (!_isDraggingAccount)
        {
            ResetAccountDragState();
            return;
        }

        var orderedItems = _viewModel.Accounts.ToList();
        ResetAccountDragState();
        await _viewModel.PersistAccountOrderAsync(orderedItems, "\u8D26\u53F7\u987A\u5E8F\u5DF2\u66F4\u65B0\u3002");
    }

    private void ResetAccountDragState()
    {
        _isDraggingAccount = false;
        _draggedAccount = null;
        if (_draggedContainer is not null)
        {
            _draggedContainer.Opacity = 1;
            _draggedContainer = null;
        }

        DragPreviewLayer.Visibility = Visibility.Collapsed;
        if (Mouse.Captured == AccountsList)
        {
            Mouse.Capture(null);
        }

        AccountsList.Cursor = System.Windows.Input.Cursors.Arrow;
    }

    private int GetAccountInsertIndex(System.Windows.Point position)
    {
        if (AccountsList.Items.Count == 0)
        {
            return -1;
        }

        for (var index = 0; index < AccountsList.Items.Count; index++)
        {
            if (AccountsList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container)
            {
                continue;
            }

            var topLeft = container.TranslatePoint(new System.Windows.Point(0, 0), AccountsList);
            var midpoint = topLeft.Y + (container.ActualHeight / 2d);
            if (position.Y < midpoint)
            {
                return index;
            }
        }

        return AccountsList.Items.Count - 1;
    }

    private void AutoScrollAccounts(System.Windows.Point position)
    {
        if (_accountsScrollViewer is null)
        {
            _accountsScrollViewer = FindDescendant<ScrollViewer>(AccountsList);
        }

        if (_accountsScrollViewer is null)
        {
            return;
        }

        const double edgeThreshold = 28d;
        if (position.Y < edgeThreshold)
        {
            _accountsScrollViewer.ScrollToVerticalOffset(Math.Max(0, _accountsScrollViewer.VerticalOffset - 10));
        }
        else if (position.Y > AccountsList.ActualHeight - edgeThreshold)
        {
            _accountsScrollViewer.ScrollToVerticalOffset(_accountsScrollViewer.VerticalOffset + 10);
        }
    }

    private void BeginAccountDragCandidate(DependencyObject? source, MouseButtonEventArgs e)
    {
        _draggedAccount = ResolveAccountItem(source ?? e.OriginalSource);
        if (_draggedAccount is null)
        {
            return;
        }

        _dragStartPoint = e.GetPosition(AccountsList);
        _draggedContainer = FindAncestor<ListBoxItem>(source) ?? FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        _dragPreviewOffset = _draggedContainer is null
            ? new System.Windows.Point(24, 16)
            : e.GetPosition(_draggedContainer);
        _isDraggingAccount = false;
        AccountsList.SelectedItem = _draggedAccount;
    }

    private void UpdateDraggedContainerReference()
        => _draggedContainer = _draggedAccount is null
            ? null
            : AccountsList.ItemContainerGenerator.ContainerFromItem(_draggedAccount) as ListBoxItem;

    private void ShowDragPreview(System.Windows.Point position)
    {
        if (_draggedAccount is null)
        {
            return;
        }

        DragPreviewNameText.Text = _draggedAccount.Name;
        DragPreviewProviderText.Text = _draggedAccount.ProviderBadge;
        DragPreviewProviderBadge.Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(_draggedAccount.IsOpenAi ? "#605E5C" : "#8764B8")!;
        DragPreviewTierText.Text = _draggedAccount.TierBadgeText;
        DragPreviewTierBadge.Visibility = _draggedAccount.HasTierBadge ? Visibility.Visible : Visibility.Collapsed;
        DragPreviewSubtitleText.Text = _draggedAccount.Subtitle;
        DragPreviewSubtitleText.Visibility = _draggedAccount.HasSubtitle ? Visibility.Visible : Visibility.Collapsed;
        DragPreviewStatusDot.Fill = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(_draggedAccount.StatusBrush)!;
        DragPreviewCard.Width = _draggedContainer is not null && _draggedContainer.ActualWidth > 0
            ? _draggedContainer.ActualWidth
            : Math.Max(280, AccountsList.ActualWidth - 12);
        DragPreviewLayer.Visibility = Visibility.Visible;
        UpdateDragPreviewPosition(position);
    }

    private void UpdateDragPreviewPosition(System.Windows.Point position)
    {
        if (DragPreviewLayer.Visibility != Visibility.Visible)
        {
            return;
        }

        var left = Math.Max(0, Math.Min(RootGrid.ActualWidth - DragPreviewCard.ActualWidth, position.X - _dragPreviewOffset.X));
        var top = Math.Max(0, Math.Min(RootGrid.ActualHeight - DragPreviewCard.ActualHeight, position.Y - _dragPreviewOffset.Y));
        Canvas.SetLeft(DragPreviewCard, left);
        Canvas.SetTop(DragPreviewCard, top);
    }

    private async void AddCompatible_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddCompatibleWindow
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            await _viewModel.AddCompatibleAsync(dialog.Result);
        }
    }

    private async void OAuth_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OAuthDialog
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true && dialog.Tokens is not null)
        {
            await _viewModel.AddOpenAiOAuthAsync(dialog.Tokens, dialog.AccountLabel);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_showSettingsRequested is not null)
        {
            _showSettingsRequested();
            return;
        }

        new SettingsWindow
        {
            Owner = this
        }.Show();
    }

    private async Task OpenEditDialogAsync(AccountListItem item)
    {
        var editContext = await _viewModel.GetEditContextAsync(item);
        if (editContext is null)
        {
            return;
        }

        var dialog = new EditAccountWindow(editContext.Provider, editContext.Account)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            await _viewModel.EditAsync(dialog.Result);
        }
    }

    private static AccountListItem? ResolveAccountItem(object sender)
        => (sender as FrameworkElement)?.DataContext as AccountListItem;

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? current) where T : DependencyObject
    {
        if (current is null)
        {
            return null;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
        {
            var child = VisualTreeHelper.GetChild(current, index);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
