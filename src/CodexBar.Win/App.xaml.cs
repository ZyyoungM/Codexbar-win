using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CodexBar.Core;
using CodexBar.Runtime;
using Forms = System.Windows.Forms;

namespace CodexBar.Win;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _notifyIcon;
    private FlyoutWindow? _flyoutWindow;
    private SingleInstanceService? _singleInstance;
    private DiagnosticLogger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _logger = new DiagnosticLogger(AppPaths.Resolve());
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimary)
        {
            Shutdown();
            return;
        }

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "CodexBar"
        };
        _notifyIcon.MouseUp += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                Dispatcher.Invoke(ToggleFlyout);
            }
        };
        _notifyIcon.ContextMenuStrip = BuildContextMenu();

        Dispatcher.BeginInvoke(() =>
        {
            if (e.Args.Any(arg => string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase)))
            {
                new SettingsWindow().Show();
            }
            else if (e.Args.Any(arg => string.Equals(arg, "--tray-only", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            else if (e.Args.Any(arg => string.Equals(arg, "--open", StringComparison.OrdinalIgnoreCase)))
            {
                ShowFlyout();
            }
            else
            {
                ShowFlyout();
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private Forms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("\u6253\u5F00\u4E3B\u9762\u677F", null, (_, _) => ShowFlyout());
        menu.Items.Add("\u5237\u65B0", null, async (_, _) =>
        {
            ShowFlyout();
            if (_flyoutWindow?.DataContext is MainFlyoutViewModel viewModel)
            {
                await viewModel.RefreshAsync();
            }
        });
        menu.Items.Add("\u542F\u52A8 Codex", null, async (_, _) =>
        {
            ShowFlyout();
            if (_flyoutWindow?.DataContext is MainFlyoutViewModel viewModel)
            {
                await viewModel.LaunchCodexAsync(null);
            }
        });
        menu.Items.Add("\u8BBE\u7F6E", null, (_, _) => new SettingsWindow().Show());
        menu.Items.Add("\u9000\u51FA", null, (_, _) => Shutdown());
        return menu;
    }

    private void ToggleFlyout()
    {
        if (_flyoutWindow?.IsVisible == true)
        {
            CloseFlyout();
            return;
        }

        ShowFlyout();
    }

    private void ShowFlyout()
    {
        try
        {
            if (_flyoutWindow?.IsVisible == true)
            {
                _flyoutWindow.Activate();
                _flyoutWindow.Focus();
                return;
            }

            _flyoutWindow = new FlyoutWindow();
            _flyoutWindow.Closed += (_, _) => _flyoutWindow = null;
            _flyoutWindow.Show();
            _flyoutWindow.UpdateLayout();
            PositionFlyoutNearCursor(_flyoutWindow);
            _flyoutWindow.Activate();
            _flyoutWindow.Focus();
        }
        catch (Exception ex)
        {
            _logger?.Error("app.show_flyout_failed", ex, new { stackTrace = ex.ToString() });
            System.Windows.MessageBox.Show(
                "\u6253\u5F00\u4E3B\u9762\u677F\u5931\u8D25\u3002\u8BF7\u67E5\u770B .codexbar/logs \u4E2D\u7684\u65E5\u5FD7\u3002",
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CloseFlyout()
    {
        var window = _flyoutWindow;
        _flyoutWindow = null;
        window?.Close();
    }

    private static void PositionFlyoutNearCursor(Window window)
    {
        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor).WorkingArea;
        var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        var cursorDip = transform.Transform(new System.Windows.Point(cursor.X, cursor.Y));
        var topLeft = transform.Transform(new System.Windows.Point(screen.Left, screen.Top));
        var bottomRight = transform.Transform(new System.Windows.Point(screen.Right, screen.Bottom));
        var workArea = new Rect(topLeft, bottomRight);

        var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

        var left = cursorDip.X - width + 8;
        var top = cursorDip.Y - height - 8;

        if (left < workArea.Left)
        {
            left = workArea.Left;
        }

        if (top < workArea.Top)
        {
            top = workArea.Top;
        }

        if (left + width > workArea.Right)
        {
            left = workArea.Right - width;
        }

        if (top + height > workArea.Bottom)
        {
            top = workArea.Bottom - height;
        }

        window.Left = Math.Max(workArea.Left, left);
        window.Top = Math.Max(workArea.Top, top);
    }
}
