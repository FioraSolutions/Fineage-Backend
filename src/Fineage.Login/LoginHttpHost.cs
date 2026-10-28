using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fineage.Login;

public sealed record AuthOkBody(bool Ok, string Username, string Mode);

public sealed record AuthErrBody(bool Ok, string Error);

/// <summary>Kestrel surface for the play-landing auth contract. Same <see cref="LoginService"/> as TCP login.</summary>
public sealed class LoginHttpHost : IAsyncDisposable
{
    readonly WebApplication _app;

    LoginHttpHost(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public string BaseUrl { get; }

    public static async Task<LoginHttpHost> StartAsync(
        LoginService service,
        IPAddress address,
        int port,
        CancellationToken ct = default)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = "Fineage.Login"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(Url(address, port));
        var app = builder.Build();
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            await next().ConfigureAwait(false);
        });
        app.MapPost("/api/auth/register", (HttpRequest request, CancellationToken token) =>
            HandleAsync(request, token, (user, email, password) => service.RegisterLanding(user, email, password), successStatus: 201));
        app.MapPost("/api/auth/login", (HttpRequest request, CancellationToken token) =>
            HandleAsync(request, token, (user, _, password) => service.LoginLanding(user, password), successStatus: 200));

        try
        {
            await app.StartAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        string bound = app.Urls.FirstOrDefault()
            ?? app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("HTTP listener did not bind.");
        return new LoginHttpHost(app, bound.TrimEnd('/'));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    static string Url(IPAddress address, int port)
    {
        string host = address.Equals(IPAddress.IPv6Any) ? "[::]"
            : address.Equals(IPAddress.Any) ? "0.0.0.0"
            : address.ToString();
        return $"http://{host}:{port}";
    }

    static async Task<IResult> HandleAsync(
        HttpRequest request,
        CancellationToken ct,
        Func<string?, string?, string?, LoginResult<string>> call,
        int successStatus)
    {
        JsonDocument? doc = await ReadObjectAsync(request, ct).ConfigureAwait(false);
        if (doc == null)
            return Err(400, "invalid_body");

        using (doc)
        {
            string? username = Field(doc.RootElement, "username");
            string? email = Field(doc.RootElement, "email");
            string? password = Field(doc.RootElement, "password");
            return From(call(username, email, password), successStatus);
        }
    }

    static async Task<JsonDocument?> ReadObjectAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength is > 8192)
            return null;
        try
        {
            var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct).ConfigureAwait(false);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                doc.Dispose();
                return null;
            }

            return doc;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string? Field(JsonElement obj, string name)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (!prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
        }

        return null;
    }

    static IResult From(LoginResult<string> result, int successStatus)
    {
        if (result.Ok && result.Value != null)
            return Results.Json(new AuthOkBody(true, result.Value, LandingAuth.Mode), statusCode: successStatus);
        int status = result.Code switch
        {
            "username_taken" or "email_taken" => 409,
            "invalid_credentials" => 401,
            _ => 400
        };
        string code = string.IsNullOrEmpty(result.Code) ? "invalid_body" : result.Code;
        return Results.Json(new AuthErrBody(false, code), statusCode: status);
    }

    static IResult Err(int status, string code) =>
        Results.Json(new AuthErrBody(false, code), statusCode: status);
}
