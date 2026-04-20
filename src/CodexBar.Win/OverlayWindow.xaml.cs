using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace CodexBar.Win;

public partial class OverlayWindow : Window
{
    private readonly MainFlyoutViewModel _viewModel;
    private readonly DispatcherTimer _autoRefreshTimer;
    private readonly Action? _showFlyoutRequested;
    private readonly Action<double>? _overlayOpacityChanged;
    private readonly Action<System.Windows.Point>? _overlayMoved;
    private readonly double[] _opacitySteps = [0.96, 0.88, 0.8, 0.72, 0.64];
    private bool _expanded;

    public OverlayWindow(
        MainFlyoutViewModel viewModel,
        Action? showFlyoutRequested = null,
        Action<double>? overlayOpacityChanged = null,
        Action<System.Windows.Point>? overlayMoved = null,
        double initialOpacity = 0.88)
    {
        _viewModel = viewModel;
        _showFlyoutRequested = showFlyoutRequested;
        _overlayOpacityChanged = overlayOpacityChanged;
        _overlayMoved = overlayMoved;
        InitializeComponent();
        DataContext = _viewModel;
        Opacity = Math.Clamp(initialOpacity, 0.35, 1.0);
        UpdateOpacityButton();

        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;

        Loaded += async (_, _) =>
        {
            ApplyExpandedState();
            await _viewModel.LoadInitialAsync();
            _ = _viewModel.RefreshOfficialQuotaInBackgroundAsync();
            _autoRefreshTimer.Start();
        };
        Closed += (_, _) => _autoRefreshTimer.Stop();
    }

    private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible || !_viewModel.CanInteract)
        {
            return;
        }

        await _viewModel.RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await _viewModel.RefreshAsync();

    private async void Launch_Click(object sender, RoutedEventArgs e)
        => await _viewModel.LaunchCodexAsync(null);

    private void ShowFlyout_Click(object sender, RoutedEventArgs e)
        => _showFlyoutRequested?.Invoke();

    private void ToggleExpanded_Click(object sender, RoutedEventArgs e)
    {
        _expanded = !_expanded;
        ApplyExpandedState();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    private void OverlaySurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed ||
            FindAncestor<System.Windows.Controls.Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        DragMove();
        _overlayMoved?.Invoke(new System.Windows.Point(Left, Top));
    }

    private void ApplyExpandedState()
    {
        Width = _expanded ? 272 : 236;
        ExpandButton.Content = _expanded ? "\uE76B" : "\uE70D";
        ExpandButton.ToolTip = _expanded ? "\u6536\u8D77" : "\u5C55\u5F00";
        ExpandedPanel.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        WeeklyQuotaRow.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        WeeklyQuotaBar.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Opacity_Click(object sender, RoutedEventArgs e)
    {
        var currentIndex = Array.FindIndex(_opacitySteps, step => Math.Abs(step - Opacity) < 0.01);
        var nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % _opacitySteps.Length;
        Opacity = _opacitySteps[nextIndex];
        UpdateOpacityButton();
        _overlayOpacityChanged?.Invoke(Opacity);
    }

    private void UpdateOpacityButton()
    {
        if (OpacityButton is null)
        {
            return;
        }

        var percent = (int)Math.Round(Opacity * 100);
        OpacityButton.Content = $"{percent}%";
        OpacityButton.ToolTip = $"\u900F\u660E\u5EA6 {percent}%\uFF0C\u70B9\u51FB\u5207\u6362";
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
