using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ApifoxHelper.Api;

/// <summary>
/// 页面资源嵌入程序集，避免运行目录不同导致找不到静态文件；首页在响应时注入会话令牌。
/// </summary>
public static class WebAssets
{
    private const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; " +
        "font-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    public static void Map(WebApplication app, LocalSecurity security)
    {
        var index = Load("Web.index.html");
        var script = Load("Web.app.js");
        var style = Load("Web.app.css");
        var icon = Load("Assets.app-icon.svg");

        app.MapGet("/", (HttpContext context) =>
        {
            context.Response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
            var html = index.Replace("__SESSION_TOKEN__", security.Token, StringComparison.Ordinal);
            return Results.Content(html, "text/html; charset=utf-8");
        });
        app.MapGet("/app.js", (HttpContext context) =>
        {
            context.Response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
            return Results.Content(script, "text/javascript; charset=utf-8");
        });
        app.MapGet("/app.css", () => Results.Content(style, "text/css; charset=utf-8"));
        // 页面图标与侧栏标识共用应用图标母版。
        app.MapGet("/app-icon.svg", (HttpContext context) =>
        {
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
            return Results.Content(icon, "image/svg+xml; charset=utf-8");
        });
    }

    private static string Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"缺少嵌入资源 {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
