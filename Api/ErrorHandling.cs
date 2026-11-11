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
                // 只回固定文案：框架消息是英文内部描述、绑定失败时还带请求体片段（第一百五十四轮）。
                logger.LogWarning(ex, "请求格式无法解析 {Path}", context.Request.Path);
                await WriteError(context, StatusCodes.Status400BadRequest, ErrorCopy.BadRequestMessage);
            }
            catch (JsonException ex)
            {
                // 只回结构信息（第几行第几个字节）：消息体会带用户原文片段（第一百五十四轮；
                // 第一百五十七轮把「列」改成「字节」——BytePositionInLine 是行内 UTF-8 字节位，
                // 第一百九十六轮把这条注释与文案对齐，口径本体在 Infrastructure/JsonErrorCopy）。
                logger.LogWarning(ex, "请求 JSON 无法解析 {Path}", context.Request.Path);
                await WriteError(context, StatusCodes.Status400BadRequest, ErrorCopy.InvalidJson(ex));
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
