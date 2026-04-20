using System.Windows;
using System.Windows.Media;
using CodexBar.Core;
using CodexBar.Runtime;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace CodexBar.Win;

public partial class App : System.Windows.Application
{
    private readonly MainFlyoutViewModel _flyoutViewModel = new();
    private Forms.NotifyIcon? _notifyIcon;
    private FlyoutWindow? _flyoutWindow;
    private OverlayWindow? _overlayWindow;
    private SettingsWindow? _settingsWindow;
    private SingleInstanceService? _singleInstance;
    private DiagnosticLogger? _logger;
    private Forms.ToolStripMenuItem? _overlayMenuItem;
    private System.Windows.Point? _overlayLocation;
    private double _overlayOpacity = 0.88;
    private readonly Drawing.Font _trayMenuFont = new("Microsoft YaHei UI", 9f, Drawing.FontStyle.Regular);
    private readonly Drawing.Font _trayMenuBoldFont = new("Microsoft YaHei UI", 9f, Drawing.FontStyle.Bold);
    private readonly Drawing.Font _trayMenuHeaderFont = new("Microsoft YaHei UI", 10.5f, Drawing.FontStyle.Bold);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _logger = new DiagnosticLogger(AppPaths.Resolve());
        _singleInstance = new SingleInstanceService();
        _singleInstance.ArgumentsReceived += args =>
        {
            Dispatcher.BeginInvoke(async () =>
                await HandleStartupCommandAsync(StartupCommandResolver.Resolve(args), "forwarded"));
        };
        if (!_singleInstance.IsPrimary)
        {
            if (!_singleInstance.TryNotifyPrimary(e.Args))
            {
                _logger?.Warning("app.forward_startup_command_failed", new { args = e.Args });
            }

            Shutdown();
            return;
        }

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
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

        Dispatcher.BeginInvoke(async () =>
            await HandleStartupCommandAsync(StartupCommandResolver.Resolve(e.Args), "cold-start"));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        _singleInstance?.Dispose();
        _trayMenuFont.Dispose();
        _trayMenuBoldFont.Dispose();
        _trayMenuHeaderFont.Dispose();
        base.OnExit(e);
    }

    private Forms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Padding = new Forms.Padding(8),
            BackColor = Drawing.Color.White,
            ForeColor = Drawing.Color.FromArgb(28, 28, 28),
            Font = _trayMenuFont,
            Renderer = new TrayMenuRenderer()
        };
        menu.Opening += (_, _) => UpdateContextMenuState();

        menu.Items.Add(new Forms.ToolStripLabel("CodexBar")
        {
            AutoSize = false,
            Width = 228,
            Height = 32,
            Margin = new Forms.Padding(4, 2, 4, 6),
            Padding = new Forms.Padding(6, 2, 6, 4),
            Font = _trayMenuHeaderFont,
            ForeColor = Drawing.Color.FromArgb(0, 103, 192)
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateMenuItem("\u6253\u5F00\u4E3B\u6D6E\u7A97", (_, _) => ShowFlyout()));
        _overlayMenuItem = CreateMenuItem("", (_, _) => ToggleOverlay());
        menu.Items.Add(_overlayMenuItem);
        menu.Items.Add(CreateMenuItem("\u5237\u65B0\u989D\u5EA6 / API", async (_, _) => await _flyoutViewModel.RefreshQuotaAndApisAsync()));
        menu.Items.Add(CreateMenuItem("\u542F\u52A8 Codex", async (_, _) => await _flyoutViewModel.LaunchCodexAsync(null)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateMenuItem("\u8BBE\u7F6E", (_, _) => ShowSettingsWindow()));
        menu.Items.Add(CreateMenuItem("\u9000\u51FA", (_, _) => Shutdown()));
        UpdateContextMenuState();
        return menu;
    }

    private static Forms.ToolStripMenuItem CreateMenuItem(string text, EventHandler onClick)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            AutoSize = false,
            Width = 228,
            Height = 38,
            Margin = new Forms.Padding(2),
            Padding = new Forms.Padding(12, 8, 12, 8)
        };
        item.Click += onClick;
        return item;
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

    private void ToggleOverlay()
    {
        if (IsOverlayVisible)
        {
            CloseOverlay();
            return;
        }

        ShowOverlay();
    }

    private bool IsOverlayVisible => _overlayWindow?.IsVisible == true;

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

            _flyoutWindow = new FlyoutWindow(_flyoutViewModel, ShowSettingsWindow, ToggleOverlay);
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
                "\u6253\u5F00\u4E3B\u6D6E\u7A97\u5931\u8D25\u3002\u8BF7\u67E5\u770B .codexbar/logs \u4E2D\u7684\u65E5\u5FD7\u3002",
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ShowOverlay()
    {
        try
        {
            if (_overlayWindow?.IsVisible == true)
            {
                _overlayWindow.Activate();
                return;
            }

            _overlayWindow = new OverlayWindow(
                _flyoutViewModel,
                ShowFlyout,
                opacity =>
                {
                    _overlayOpacity = opacity;
                    UpdateContextMenuState();
                },
                position => _overlayLocation = position,
                _overlayOpacity);
            _overlayWindow.Closed += (_, _) =>
            {
                _overlayLocation = _overlayWindow is null ? _overlayLocation : new System.Windows.Point(_overlayWindow.Left, _overlayWindow.Top);
                _overlayWindow = null;
                _settingsWindow?.SyncOverlayState(false);
                UpdateContextMenuState();
            };
            _overlayWindow.Show();
            _overlayWindow.UpdateLayout();
            if (_overlayLocation.HasValue)
            {
                _overlayWindow.Left = _overlayLocation.Value.X;
                _overlayWindow.Top = _overlayLocation.Value.Y;
            }
            else
            {
                PositionOverlayInCorner(_overlayWindow);
            }
            _overlayWindow.Activate();
            _settingsWindow?.SyncOverlayState(true);
            UpdateContextMenuState();
        }
        catch (Exception ex)
        {
            _logger?.Error("app.show_overlay_failed", ex, new { stackTrace = ex.ToString() });
            System.Windows.MessageBox.Show(
                "\u6253\u5F00 Overlay \u5931\u8D25\u3002\u8BF7\u67E5\u770B .codexbar/logs \u4E2D\u7684\u65E5\u5FD7\u3002",
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow?.IsVisible == true)
        {
            _settingsWindow.Activate();
            _settingsWindow.Focus();
            _settingsWindow.SyncOverlayState(IsOverlayVisible);
            return;
        }

        _settingsWindow = new SettingsWindow(
            () => IsOverlayVisible,
            SetOverlayVisibleAsync);
        if (_flyoutWindow?.IsVisible == true)
        {
            _settingsWindow.Owner = _flyoutWindow;
        }

        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
        _settingsWindow.Focus();
        _settingsWindow.SyncOverlayState(IsOverlayVisible);
    }

    private void CloseFlyout()
    {
        var window = _flyoutWindow;
        _flyoutWindow = null;
        window?.Close();
    }

    private void CloseOverlay()
    {
        var window = _overlayWindow;
        if (window is not null)
        {
            _overlayLocation = new System.Windows.Point(window.Left, window.Top);
        }
        _overlayWindow = null;
        window?.Close();
        _settingsWindow?.SyncOverlayState(false);
        UpdateContextMenuState();
    }

    private Task SetOverlayVisibleAsync(bool isVisible)
    {
        if (isVisible)
        {
            ShowOverlay();
        }
        else
        {
            CloseOverlay();
        }

        return Task.CompletedTask;
    }

    private void UpdateContextMenuState()
    {
        if (_overlayMenuItem is null)
        {
            return;
        }

        _overlayMenuItem.Text = IsOverlayVisible ? "\u5173\u95ED\u5C0F\u6D6E\u7A97" : "\u6253\u5F00\u5C0F\u6D6E\u7A97";
        _overlayMenuItem.ForeColor = IsOverlayVisible
            ? Drawing.Color.FromArgb(0, 103, 192)
            : Drawing.Color.FromArgb(28, 28, 28);
        _overlayMenuItem.Font = IsOverlayVisible ? _trayMenuBoldFont : _trayMenuFont;
    }

    private async Task HandleStartupCommandAsync(StartupCommand command, string source)
    {
        _logger?.Info("app.startup_command", new { source, command = command.ToString() });

        switch (command)
        {
            case StartupCommand.Settings:
                ShowSettingsWindow();
                break;
            case StartupCommand.Overlay:
                ShowOverlay();
                break;
            case StartupCommand.TrayOnly:
                if (!string.Equals(source, "forwarded", StringComparison.OrdinalIgnoreCase))
                {
                    await _flyoutViewModel.LoadInitialAsync();
                    _ = _flyoutViewModel.RefreshOfficialQuotaInBackgroundAsync();
                }

                break;
            case StartupCommand.Open:
            default:
                ShowFlyout();
                break;
        }
    }

    private static void PositionFlyoutNearCursor(Window window)
    {
        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor).WorkingArea;
        var workArea = ToDipRect(window, screen);

        var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

        var cursorDip = ToDipPoint(window, cursor.X, cursor.Y);
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

    private static void PositionOverlayInCorner(Window window)
    {
        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor).WorkingArea;
        var workArea = ToDipRect(window, screen);

        var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var left = workArea.Right - width - 24;
        var top = workArea.Top + 24;

        window.Left = Math.Max(workArea.Left, left);
        window.Top = Math.Max(workArea.Top, top);
    }

    private static Rect ToDipRect(Visual visual, Drawing.Rectangle bounds)
    {
        var transform = PresentationSource.FromVisual(visual)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new System.Windows.Point(bounds.Left, bounds.Top));
        var bottomRight = transform.Transform(new System.Windows.Point(bounds.Right, bounds.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private static System.Windows.Point ToDipPoint(Visual visual, int x, int y)
    {
        var transform = PresentationSource.FromVisual(visual)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return transform.Transform(new System.Windows.Point(x, y));
    }
}

internal sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    public TrayMenuRenderer() : base(new TrayMenuColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        using var pen = new Drawing.Pen(Drawing.Color.FromArgb(216, 220, 225));
        var bounds = new Drawing.Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawRectangle(pen, bounds);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Bounds.Top + (e.Item.Height / 2);
        using var pen = new Drawing.Pen(Drawing.Color.FromArgb(234, 236, 239));
        e.Graphics.DrawLine(pen, 14, y, e.Item.Width - 14, y);
    }
}

internal sealed class TrayMenuColorTable : Forms.ProfessionalColorTable
{
    public override Drawing.Color ToolStripDropDownBackground => Drawing.Color.White;
    public override Drawing.Color MenuItemSelected => Drawing.Color.FromArgb(232, 242, 252);
    public override Drawing.Color MenuItemBorder => Drawing.Color.FromArgb(201, 224, 246);
    public override Drawing.Color MenuBorder => Drawing.Color.FromArgb(216, 220, 225);
    public override Drawing.Color SeparatorDark => Drawing.Color.FromArgb(234, 236, 239);
    public override Drawing.Color SeparatorLight => Drawing.Color.FromArgb(234, 236, 239);
    public override Drawing.Color ImageMarginGradientBegin => Drawing.Color.White;
    public override Drawing.Color ImageMarginGradientMiddle => Drawing.Color.White;
    public override Drawing.Color ImageMarginGradientEnd => Drawing.Color.White;
}
