using ApifoxHelper.Reports;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 报告导出口径断言：ReportFormats.TryDescribe 是 Web 下载路由与桌面「另存为」共用的
/// 格式 → 下载文件名/内容类型映射（ApifoxRoutes 原内联口径的单一落点）。
/// 本文件与主工程共享编译，断言直接作用于真实实现；四个格式逐一钉住，
/// 未知格式（含 preview——它走预览不下载）必须返回 null（由 ReportExportService 包装成拒绝）；
/// 删除任一映射臂后对应断言变红（变异验证）。
/// </summary>
public class ReportExportTests
{
    [Theory]
    [InlineData("json", "report.json", "application/json")]
    [InlineData("md", "report.md", "text/markdown")]
    [InlineData("html", "report.html", "text/html")]
    [InlineData("handoff", "handoff.md", "text/markdown")]
    public void TryDescribe_MatchesWebDownloadCaliber(string format, string fileName, string contentType)
    {
        Assert.Equal((fileName, contentType), ReportFormats.TryDescribe(format));
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("pdf")]
    [InlineData("")]
    public void TryDescribe_ReturnsNullForUnknownFormat(string format)
    {
        Assert.Null(ReportFormats.TryDescribe(format));
    }
}
