using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Fortnox.Console.Infrastructure;

namespace Fortnox.Console.Auth;

/// <summary>
/// The authorization-code flow from a terminal: open the consent page in a browser, catch the
/// redirect on a loopback listener, and hand back the code. The redirect URI must be registered
/// on the integration in Fortnox's developer portal exactly as given here, port and path included.
///
/// Everything human-facing goes to stderr, so stdout stays a clean payload.
/// </summary>
public static class BrowserLogin
{
    public sealed record Result(string Code, string CodeVerifier);

    public static async Task<Result> RunAsync(
        string clientId, string redirectUri, string scopes, bool service, bool openBrowser, TimeSpan timeout)
    {
        var redirect = new Uri(redirectUri);
        if (!redirect.IsLoopback || redirect.Scheme != Uri.UriSchemeHttp)
            throw FortnoxException.Invalid(
                $"--redirect-uri must be an http://localhost (or 127.0.0.1) address to catch the redirect here; got {redirectUri}.",
                "Register e.g. http://localhost:8765/callback on the integration in the Fortnox developer portal and pass it as --redirect-uri.");

        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var url = FortnoxOAuth.BuildAuthorizeUrl(clientId, redirectUri, scopes, state, challenge, service);

        using var listener = new HttpListener();
        var path = redirect.AbsolutePath.EndsWith('/') ? redirect.AbsolutePath : redirect.AbsolutePath + "/";
        listener.Prefixes.Add($"http://{redirect.Host}:{redirect.Port}{path}");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new FortnoxException(ErrorCode.Error, $"Could not listen on {redirectUri}.", ex.Message,
                "Pick a free port with --redirect-uri (and register it on the integration).");
        }

        var err = System.Console.Error;
        err.WriteLine("Open this URL to authorize the integration in Fortnox:");
        err.WriteLine();
        err.WriteLine("  " + url);
        err.WriteLine();
        if (service)
            err.WriteLine("Sign in as a system administrator of the company - service-account consent needs one.");
        err.WriteLine($"Waiting for the redirect to {redirectUri} ...");

        if (openBrowser) TryOpen(url);

        var contextTask = listener.GetContextAsync();
        if (await Task.WhenAny(contextTask, Task.Delay(timeout)) != contextTask)
            throw FortnoxException.Invalid($"No redirect arrived within {timeout.TotalMinutes:0} minutes.", "Run the command again.");

        var context = await contextTask;
        var query = System.Web.HttpUtility.ParseQueryString(context.Request.Url?.Query ?? "");

        var error = query["error"];
        var code = query["code"];
        var ok = error is null && code is not null && query["state"] == state;

        await Respond(context.Response, ok
            ? "Fortnox authorized. You can close this tab and return to the terminal."
            : "Authorization failed. Return to the terminal for details.");

        if (error is not null)
            throw FortnoxException.Auth(
                $"Fortnox declined the authorization: {error}.",
                error.Contains("scope", StringComparison.OrdinalIgnoreCase)
                    ? $"{query["error_description"]}\n\nRequested: {scopes}\n\nEvery requested scope must be enabled on the integration in the Fortnox developer portal."
                    : query["error_description"],
                error.Contains("scope", StringComparison.OrdinalIgnoreCase)
                    ? "Enable the missing scopes on the integration, or pass --scopes \"...\" with only the ones it has."
                    : null);

        if (query["state"] != state)
            throw FortnoxException.Auth("The redirect carried the wrong state parameter; refusing it.", null, "Run the command again.");

        if (code is null)
            throw FortnoxException.Auth("The redirect carried no authorization code.", context.Request.Url?.ToString());

        return new Result(code, verifier);
    }

    private static async Task Respond(HttpListenerResponse response, string message)
    {
        var html = $"<!doctype html><meta charset=utf-8><title>fortnox</title><body style=\"font-family:sans-serif;padding:2em\"><p>{WebUtility.HtmlEncode(message)}</p>";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static void TryOpen(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
        catch (Exception)
        {
            // The URL is printed; a missing browser is not a reason to fail.
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
