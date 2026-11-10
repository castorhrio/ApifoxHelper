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
                await WriteError(context, StatusCodes.Status400BadRequest, ErrorCopy.BadRequest(Redactor.Redact(ex.Message)));
            }
            catch (JsonException ex)
            {
                await WriteError(context, StatusCodes.Status400BadRequest, ErrorCopy.InvalidJson(Redactor.Redact(ex.Message)));
            }
            catch (Exception ex)
            {
                // 未知异常只回固定文案（ErrorCopy.InternalMessage），详情进日志：
                // 异常消息可能带内部路径、类型名或凭据片段，而它会被界面原样显示在错误行里。
                logger.LogError(ex, "请求处理失败 {Path}", context.Request.Path);
                await WriteError(context, StatusCodes.Status500InternalServerError, ErrorCopy.InternalMessage);
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
