namespace ApifoxHelper.Reports;

/// <summary>
/// 报告下载格式的固定口径：格式 → 下载文件名与内容类型。
/// 本文件只依赖 BCL，被主工程与 ApifoxHelper.Tests 共享编译（与 TaskDisplay.cs 同法），
/// 口径断言直接作用于真实实现；Web 下载与桌面「另存为」共用本表，防两条路径漂移。
/// </summary>
public static class ReportFormats
{
    /// <summary>未知格式（含 preview——它走预览不下载）返回 null，由调用方决定拒绝方式。</summary>
    public static (string FileName, string ContentType)? TryDescribe(string format) => format switch
    {
        "json" => ("report.json", "application/json"),
        "md" => ("report.md", "text/markdown"),
        "html" => ("report.html", "text/html"),
        "handoff" => ("handoff.md", "text/markdown"),
        _ => null,
    };
}
