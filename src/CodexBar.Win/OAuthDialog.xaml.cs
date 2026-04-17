using System.Windows;
using CodexBar.Auth;
using CodexBar.Core;
using CodexBar.Runtime;

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
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartLoopbackListener();
            _client.OpenSystemBrowser(_flow!.AuthorizationUrl);
            StatusText.Text = "\u5DF2\u6253\u5F00\u6D4F\u89C8\u5668\u3002";
        }
        catch (Exception ex)
        {
            StatusText.Text = DiagnosticLogger.Redact(ex.Message);
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
        _ = ListenForCallbackAsync();
    }

    private async Task ListenForCallbackAsync()
    {
        try
        {
            StatusText.Text = "\u6B63\u5728\u76D1\u542C localhost:1455 ...";
            var callback = await _loopback.WaitForCallbackAsync(_flow!.State, TimeSpan.FromMinutes(5));
            Tokens = await _client.ExchangeCodeAsync(_flow!, callback.Code);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = DiagnosticLogger.Redact(ex.Message);
        }
        finally
        {
            _isListening = false;
        }
    }

    private async void Complete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Tokens = await _client.CompleteManualInputAsync(_flow!, CallbackBox.Text);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = DiagnosticLogger.Redact(ex.Message);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
