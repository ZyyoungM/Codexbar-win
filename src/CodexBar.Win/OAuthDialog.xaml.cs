using System.Windows;
using CodexBar.Auth;
using CodexBar.Core;
using CodexBar.Runtime;
using System.Windows.Media;

namespace CodexBar.Win;

public partial class OAuthDialog : Window
{
    private readonly OpenAIOAuthClient _client = new();
    private readonly LoopbackCallbackServer _loopback = new();
    private OAuthPendingFlow? _flow;
    private OAuthTokens? _pendingWorkspaceTokens;
    private IReadOnlyList<OpenAiWorkspaceDescriptor> _workspaceChoices = [];
    private string? _allowedWorkspaceId;
    private bool _isListening;
    private bool _isInitialized;

    public OAuthTokens? Tokens { get; private set; }
    public OpenAiWorkspaceDescriptor? SelectedWorkspaceHint { get; private set; }
    public string AccountLabel => string.IsNullOrWhiteSpace(LabelBox.Text) ? "OpenAI" : LabelBox.Text.Trim();

    public OAuthDialog(string? suggestedLabel = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(suggestedLabel))
        {
            LabelBox.Text = suggestedLabel.Trim();
        }

        _isInitialized = true;
        BeginNewFlow();
        SetStatus("\u7B49\u5F85\u6388\u6743", "\u70B9\u51FB\u201C\u6253\u5F00\u6D4F\u89C8\u5668\u201D\u5F00\u59CB OAuth \u767B\u5F55\u3002");
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RefreshFlowFromWorkspaceInput();
            StartLoopbackListener();
            _client.OpenSystemBrowser(_flow!.AuthorizationUrl);
            SetStatus("\u5DF2\u6253\u5F00\u6D4F\u89C8\u5668", "\u6B63\u5728\u7B49\u5F85 OpenAI \u6388\u6743\u56DE\u8C03\u3002");
        }
        catch (Exception ex)
        {
            SetStatus("\u6253\u5F00\u6D4F\u89C8\u5668\u5931\u8D25", DiagnosticLogger.Redact(ex.Message), isError: true);
        }
    }

    private void Listen_Click(object sender, RoutedEventArgs e)
        => StartLoopbackListener();

    private void StartLoopbackListener()
    {
        if (_isListening)
        {
            return;
        }

        RefreshFlowFromWorkspaceInput();
        _isListening = true;
        SetBusy(true);
        _ = ListenForCallbackAsync();
    }

    private async Task ListenForCallbackAsync()
    {
        try
        {
            SetStatus("\u6B63\u5728\u76D1\u542C localhost:1455", "\u5DF2\u542F\u52A8\u56DE\u8C03\u76D1\u542C\uFF0C\u6388\u6743\u6210\u529F\u540E\u4F1A\u81EA\u52A8\u5B8C\u6210\u767B\u5F55\u3002");
            var callback = await _loopback.WaitForCallbackAsync(_flow!.State, TimeSpan.FromMinutes(5));
            var tokens = await _client.ExchangeCodeAsync(_flow!, callback.Code);
            if (await PrepareWorkspaceSelectionAsync(tokens))
            {
                return;
            }

            Tokens = tokens;
            SetStatus("\u6388\u6743\u6210\u529F", "\u5DF2\u83B7\u53D6 OpenAI OAuth \u4EE4\u724C\uFF0C\u6B63\u5728\u5173\u95ED\u7A97\u53E3\u3002", isSuccess: true);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            SetStatus("\u76D1\u542C\u56DE\u8C03\u5931\u8D25", DiagnosticLogger.Redact(ex.Message), isError: true);
        }
        finally
        {
            _isListening = false;
            SetBusy(false);
        }
    }

    private async void Complete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_pendingWorkspaceTokens is not null)
            {
                CommitSelectedWorkspace();
                return;
            }

            SetBusy(true);
            SetStatus("\u6B63\u5728\u5B8C\u6210\u767B\u5F55", "\u6B63\u5728\u89E3\u6790\u624B\u5DE5\u56DE\u8C03\u5185\u5BB9\u2026");
            var tokens = await _client.CompleteManualInputAsync(_flow!, CallbackBox.Text);
            _loopback.CancelPendingWait();
            if (await PrepareWorkspaceSelectionAsync(tokens))
            {
                return;
            }

            Tokens = tokens;
            SetStatus("\u767B\u5F55\u6210\u529F", "\u5DF2\u89E3\u6790 callback \u5E76\u83B7\u53D6 OAuth \u4EE4\u724C\u3002", isSuccess: true);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            SetStatus("\u5B8C\u6210\u767B\u5F55\u5931\u8D25", DiagnosticLogger.Redact(ex.Message), isError: true);
        }
        finally
        {
            if (!_isListening)
            {
                SetBusy(false);
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _loopback.CancelPendingWait();
        DialogResult = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        _loopback.CancelPendingWait();
        base.OnClosed(e);
    }

    private void SetBusy(bool busy)
    {
        OpenBrowserButton.IsEnabled = !busy;
        ListenButton.IsEnabled = !busy;
        CompleteButton.IsEnabled = !busy;
    }

    private void AllowedWorkspaceBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_isInitialized || _isListening || _pendingWorkspaceTokens is not null)
        {
            return;
        }

        _allowedWorkspaceId = ReadAllowedWorkspaceBox();
        BeginNewFlow();
    }

    private void RefreshFlowFromWorkspaceInput()
    {
        var allowedWorkspaceId = CurrentAllowedWorkspaceId();
        if (!string.Equals(_flow?.Options.AllowedWorkspaceId, allowedWorkspaceId, StringComparison.Ordinal))
        {
            BeginNewFlow();
        }
    }

    private void BeginNewFlow()
    {
        _flow = _client.BeginLogin(new OAuthOptions
        {
            AllowedWorkspaceId = CurrentAllowedWorkspaceId()
        });
        UrlBox.Text = _flow.AuthorizationUrl.ToString();
    }

    private string? CurrentAllowedWorkspaceId()
        => _allowedWorkspaceId;

    private string? ReadAllowedWorkspaceBox()
        => string.IsNullOrWhiteSpace(AllowedWorkspaceBox.Text) ? null : AllowedWorkspaceBox.Text.Trim();

    private async Task<bool> PrepareWorkspaceSelectionAsync(OAuthTokens tokens)
    {
        var identity = OAuthIdentityExtractor.Extract(tokens);
        var expectedWorkspaceId = CurrentAllowedWorkspaceId();
        if (!string.IsNullOrWhiteSpace(expectedWorkspaceId) &&
            !string.Equals(tokens.AccountId, expectedWorkspaceId, StringComparison.OrdinalIgnoreCase))
        {
            var actual = string.IsNullOrWhiteSpace(tokens.AccountId) ? "(none)" : tokens.AccountId.Trim();
            SetStatus(
                "Workspace mismatch",
                $"OAuth returned workspace {actual}, expected {expectedWorkspaceId}. Check the workspace id and try again.",
                isError: true);
            return true;
        }

        SetStatus("Checking workspaces", "Reading ChatGPT/Codex workspaces for this OpenAI login...");
        _workspaceChoices = await OpenAiWorkspaceDiscovery.DiscoverAsync(tokens, identity);
        if (!string.IsNullOrWhiteSpace(expectedWorkspaceId))
        {
            var expectedWorkspace = _workspaceChoices.FirstOrDefault(item =>
                string.Equals(item.WorkspaceId, expectedWorkspaceId, StringComparison.OrdinalIgnoreCase));
            if (expectedWorkspace is not null)
            {
                SelectedWorkspaceHint ??= expectedWorkspace;
                Tokens = expectedWorkspace.ApplyTo(tokens);
                return false;
            }
        }

        if (_workspaceChoices.Count <= 1)
        {
            Tokens = _workspaceChoices.FirstOrDefault()?.ApplyTo(tokens) ?? tokens;
            return false;
        }

        _pendingWorkspaceTokens = tokens;
        WorkspacePanel.Visibility = Visibility.Visible;
        WorkspaceBox.ItemsSource = _workspaceChoices;
        WorkspaceBox.SelectedItem = _workspaceChoices.FirstOrDefault(item => item.IsCurrent) ?? _workspaceChoices[0];
        CompleteButton.Content = "Save workspace";
        SetStatus("Choose workspace", "Select the ChatGPT/Codex workspace to save, then click Save workspace.", isSuccess: true);
        return true;
    }

    private void CommitSelectedWorkspace()
    {
        var selected = WorkspaceBox.SelectedItem as OpenAiWorkspaceDescriptor
                       ?? _workspaceChoices.FirstOrDefault()
                       ?? throw new InvalidOperationException("No workspace is selected.");
        if (!selected.IsCurrent &&
            !string.Equals(_pendingWorkspaceTokens!.AccountId, selected.WorkspaceId, StringComparison.OrdinalIgnoreCase))
        {
            SelectedWorkspaceHint = selected;
            _allowedWorkspaceId = selected.WorkspaceId;
            AllowedWorkspaceBox.Text = selected.WorkspaceId;
            _pendingWorkspaceTokens = null;
            _workspaceChoices = [];
            WorkspacePanel.Visibility = Visibility.Collapsed;
            CompleteButton.Content = "\u5B8C\u6210\u767B\u5F55";
            BeginNewFlow();
            StartLoopbackListener();
            _client.OpenSystemBrowser(_flow!.AuthorizationUrl);
            SetStatus("Opening workspace", $"Reopening OpenAI OAuth for {selected.DisplayLabel}.", isSuccess: true);
            return;
        }

        SelectedWorkspaceHint = selected;
        Tokens = selected.ApplyTo(_pendingWorkspaceTokens!);
        _loopback.CancelPendingWait();
        SetStatus("Workspace saved", $"Selected {selected.DisplayLabel}.", isSuccess: true);
        DialogResult = true;
    }

    private void SetStatus(string title, string message, bool isError = false, bool isSuccess = false)
    {
        var accent = isError ? "#C42B1C" : isSuccess ? "#107C10" : "#0F6CBD";
        var border = isError ? "#F1B9B9" : isSuccess ? "#B7E0B8" : "#CFE4F9";
        var background = isError ? "#FEF6F6" : isSuccess ? "#F3FBF3" : "#F7FAFF";
        StatusPanel.Background = CreateBrush(background);
        StatusPanel.BorderBrush = CreateBrush(border);
        StatusDot.Fill = CreateBrush(accent);
        StatusTitleText.Foreground = CreateBrush(accent);
        StatusTitleText.Text = title;
        StatusBodyText.Text = message;
        StatusBodyText.Foreground = CreateBrush(isError ? "#7A2E24" : "#605E5C");
    }

    private static SolidColorBrush CreateBrush(string hex)
        => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);
}
