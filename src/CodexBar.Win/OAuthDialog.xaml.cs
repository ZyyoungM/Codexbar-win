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
    private bool _isListening;

    public OAuthTokens? Tokens { get; private set; }
    public string AccountLabel => string.IsNullOrWhiteSpace(LabelBox.Text) ? "OpenAI" : LabelBox.Text.Trim();

    public OAuthDialog()
    {
        InitializeComponent();
        _flow = _client.BeginLogin();
        UrlBox.Text = _flow.AuthorizationUrl.ToString();
        SetStatus("\u7B49\u5F85\u6388\u6743", "\u70B9\u51FB\u201C\u6253\u5F00\u6D4F\u89C8\u5668\u201D\u5F00\u59CB OAuth \u767B\u5F55\u3002");
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
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
            Tokens = await _client.ExchangeCodeAsync(_flow!, callback.Code);
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
            SetBusy(true);
            SetStatus("\u6B63\u5728\u5B8C\u6210\u767B\u5F55", "\u6B63\u5728\u89E3\u6790\u624B\u5DE5\u56DE\u8C03\u5185\u5BB9\u2026");
            Tokens = await _client.CompleteManualInputAsync(_flow!, CallbackBox.Text);
            SetStatus("\u767B\u5F55\u6210\u529F", "\u5DF2\u89E3\u6790 callback \u5E76\u83B7\u53D6 OAuth \u4EE4\u724C\u3002", isSuccess: true);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            SetStatus("\u5B8C\u6210\u767B\u5F55\u5931\u8D25", DiagnosticLogger.Redact(ex.Message), isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void SetBusy(bool busy)
    {
        OpenBrowserButton.IsEnabled = !busy;
        ListenButton.IsEnabled = !busy;
        CompleteButton.IsEnabled = !busy;
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
