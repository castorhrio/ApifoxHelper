using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ApifoxHelper.Api;

public static class ErrorHandling
{
    public static void Use(WebApplication app)
    {
        var logger = app.Logger;
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (UserException ex)
            {
                await WriteError(context, ex.StatusCode, ex.Message);
            }
            catch (BadHttpRequestException ex)
            {
                await WriteError(context, StatusCodes.Status400BadRequest, "请求格式无效：" + Redactor.Redact(ex.Message));
            }
            catch (JsonException ex)
            {
                await WriteError(context, StatusCodes.Status400BadRequest, "JSON 格式无效：" + Redactor.Redact(ex.Message));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "请求处理失败 {Path}", context.Request.Path);
                await WriteError(context, StatusCodes.Status500InternalServerError, "内部错误：" + Redactor.Redact(ex.Message));
            }
        });
    }

    private static async Task WriteError(HttpContext context, int status, string message)
    {
        if (context.Response.HasStarted) return;
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }, JsonDefaults.Compact));
    }
}
