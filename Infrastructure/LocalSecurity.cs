using Microsoft.AspNetCore.Http;

namespace ApifoxHelper.Infrastructure;

/// <summary>
/// 本地访问控制：只接受回环 Host（防 DNS 重绑定）、同源 Origin，并要求每个 /api 请求携带进程级会话令牌，
/// 防止其他网页借浏览器触发源码回写或远端同步。
/// </summary>
public sealed class LocalSecurity
{
    public const string HeaderName = "X-Helper-Session";

    private readonly byte[] _tokenBytes;

    public LocalSecurity()
    {
        Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _tokenBytes = Encoding.ASCII.GetBytes(Token);
    }

    public string Token { get; }

    public int Port { get; set; }

    public bool IsAllowedHost(HostString host)
    {
        if (!host.HasValue || host.Port != Port) return false;
        return host.Host is "127.0.0.1" or "localhost";
    }

    public bool IsAllowedOrigin(string origin) =>
        string.Equals(origin, $"http://127.0.0.1:{Port}", StringComparison.Ordinal)
        || string.Equals(origin, $"http://localhost:{Port}", StringComparison.Ordinal);

    public bool CheckToken(string? provided)
    {
        if (string.IsNullOrEmpty(provided)) return false;
        var bytes = Encoding.ASCII.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(bytes, _tokenBytes);
    }

    public async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        if (!IsAllowedHost(context.Request.Host))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("forbidden host");
            return;
        }

        var origin = context.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && !IsAllowedOrigin(origin))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("forbidden origin");
            return;
        }

        if (string.Equals(context.Request.Headers["Sec-Fetch-Site"].ToString(), "cross-site", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("forbidden cross-site request");
            return;
        }

        if (context.Request.Path.StartsWithSegments("/api") && !CheckToken(context.Request.Headers[HeaderName].ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("missing or invalid session");
            return;
        }

        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
        headers["Cache-Control"] = "no-store";
        await next();
    }
}
