using System.Net.Mime;
using System.Text.Json.Serialization;
using CodexBar.Api;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://127.0.0.1:5057");
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(TrustedFrontendCors.Apply);
});
builder.Services.AddSingleton<ProbeStatusStore>();
builder.Services.AddSingleton<FrontendBackendService>();
builder.Services.AddSingleton<OAuthSessionManager>();

var app = builder.Build();
app.UseCors();

app.MapGet("/api/dashboard", async (FrontendBackendService service, CancellationToken cancellationToken)
    => Results.Ok(await service.GetDashboardAsync(cancellationToken)));

app.MapGet("/api/settings", async (FrontendBackendService service, CancellationToken cancellationToken)
    => Results.Ok(await service.GetSettingsAsync(cancellationToken)));

app.MapPost("/api/settings/save", async (FrontendSettingsSaveRequest request, FrontendBackendService service, CancellationToken cancellationToken)
    => ToResult(await service.SaveSettingsAsync(request, cancellationToken)));

app.MapPost("/api/settings/detect-desktop", (FrontendPathDetectRequest request, FrontendBackendService service)
    => ToResult(service.DetectDesktop(request.Path)));

app.MapPost("/api/settings/detect-cli", async (FrontendPathDetectRequest request, FrontendBackendService service, CancellationToken cancellationToken)
    => ToResult(await service.DetectCliAsync(request.Path, cancellationToken)));

app.MapPost("/api/settings/launch", async (FrontendLaunchRequest request, FrontendBackendService service, CancellationToken cancellationToken)
    => ToResult(await service.LaunchTargetAsync(request, cancellationToken)));

app.MapGet("/api/settings/export", async (bool includeSecrets, FrontendBackendService service, CancellationToken cancellationToken) =>
{
    var (fileName, content) = await service.ExportAccountsCsvAsync(includeSecrets, cancellationToken);
    return Results.File(
        System.Text.Encoding.UTF8.GetBytes(content),
        MediaTypeNames.Text.Plain,
        fileName);
});

app.MapPost("/api/settings/import", async (HttpRequest request, FrontendBackendService service, CancellationToken cancellationToken) =>
{
    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files["file"];
    if (file is null)
    {
        return Results.BadRequest(new FrontendCommandResult(false, "未收到导入文件。"));
    }

    using var reader = new StreamReader(file.OpenReadStream());
    var content = await reader.ReadToEndAsync(cancellationToken);
    return ToResult(await service.ImportAccountsCsvAsync(content, cancellationToken));
});

app.MapGet("/api/history/export", async (bool? includeArchived, FrontendBackendService service, CancellationToken cancellationToken) =>
{
    var (fileName, content) = await service.ExportHistoryZipAsync(includeArchived != false, cancellationToken);
    return Results.File(content, "application/zip", fileName);
});

app.MapPost("/api/history/import", async (HttpRequest request, FrontendBackendService service, CancellationToken cancellationToken) =>
{
    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files["file"];
    if (file is null)
    {
        return Results.BadRequest(new FrontendCommandResult(false, "未收到历史会话 ZIP 文件。"));
    }

    await using var content = file.OpenReadStream();
    return ToResult(await service.ImportHistoryZipAsync(content, cancellationToken));
});

app.MapPost("/api/accounts/activate", async (FrontendAccountActionRequest request, FrontendBackendService service, CancellationToken cancellationToken)
    => ToResult(await service.ActivateAccountAsync(request.ProviderId, request.AccountId, forceLaunch: false, cancellationToken)));

app.MapPost("/api/accounts/launch", async (FrontendAccountActionRequest request, FrontendBackendService service, CancellationToken cancellationToken)
    => ToResult(await service.ActivateAccountAsync(request.ProviderId, request.AccountId, forceLaunch: true, cancellationToken)));

app.MapPost("/api/accounts/probe", async (FrontendAccountActionRequest? request, FrontendBackendService service, CancellationToken cancellationToken)
    => ToResult(await service.ProbeCompatibleAccountsAsync(request?.ProviderId, request?.AccountId, cancellationToken)));

app.MapPost("/api/accounts/edit", async (FrontendEditAccountRequest request, FrontendBackendService service, CancellationToken cancellationToken)
    => ToResult(await service.EditAccountAsync(request, cancellationToken)));

app.MapDelete("/api/accounts/{providerId}/{accountId}", async (string providerId, string accountId, FrontendBackendService service, CancellationToken cancellationToken)
    => ToResult(await service.DeleteAccountAsync(providerId, accountId, cancellationToken)));

app.MapPost("/api/accounts/reorder", async (FrontendReorderAccountsRequest request, FrontendBackendService service, CancellationToken cancellationToken)
    => ToResult(await service.ReorderAccountsAsync(request.OrderedKeys, cancellationToken)));

app.MapPost("/api/providers/compatible", async (FrontendCompatibleProviderRequest request, FrontendBackendService service, CancellationToken cancellationToken)
    => ToResult(await service.AddCompatibleProviderAsync(request, cancellationToken)));

app.MapPost("/api/providers/compatible/probe", async (FrontendCompatibleProviderRequest request, FrontendBackendService service, CancellationToken cancellationToken)
    => ToResult(await service.ProbeDraftCompatibleProviderAsync(request, cancellationToken)));

app.MapGet("/api/oauth/state", async (OAuthSessionManager sessionManager)
    => Results.Ok(await sessionManager.GetStateAsync()));

app.MapPost("/api/oauth/open-browser", async (OAuthSessionManager sessionManager)
    => Results.Ok(await sessionManager.OpenBrowserAsync()));

app.MapPost("/api/oauth/listen", async (OAuthSessionManager sessionManager)
    => Results.Ok(await sessionManager.ListenAsync()));

app.MapPost("/api/oauth/complete", async (FrontendOAuthCompleteRequest request, OAuthSessionManager sessionManager, FrontendBackendService service, CancellationToken cancellationToken)
    => ToResult(await sessionManager.CompleteAsync(request, service.SaveOpenAiOAuthAsync, cancellationToken)));

app.Run();

static IResult ToResult(FrontendCommandResult result)
    => result.Ok ? Results.Ok(result) : Results.BadRequest(result);
