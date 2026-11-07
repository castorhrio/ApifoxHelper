using ApifoxHelper.Tasks;

namespace ApifoxHelper.Reports;

/// <summary>
/// 报告预览与导出的单一口径：格式 → 下载文件名、内容类型与任务目录副本。
/// Web 下载路由与桌面端「另存为」共用本服务，保证两条路径字节一致；内容全部经 ReportService 构造并脱敏。
/// </summary>
public sealed class ReportExportService(ReportService reports, TaskStore tasks)
{
    public sealed record ReportPreview(JsonObject Report, string Markdown);

    public sealed record ReportFile(string FileName, string ContentType, byte[] Bytes);

    /// <summary>报告预览（不写任务文件）：完整报告对象与 Markdown 文本，供页面与桌面对话框展示。</summary>
    public ReportPreview Preview(string taskId)
    {
        var report = reports.Build(taskId);
        return new ReportPreview(report, reports.Markdown(report));
    }

    /// <summary>按格式渲染下载内容；json/md/handoff 与 Web 下载一致，同时把副本写入任务目录。</summary>
    public ReportFile Render(string taskId, string format)
    {
        var (fileName, contentType) = Descriptor(format);
        var preview = Preview(taskId);
        switch (format)
        {
            case "json":
                tasks.Write(taskId, TaskFiles.ReportJson, preview.Report);
                return new ReportFile(fileName, contentType, Encoding.UTF8.GetBytes(preview.Report.ToJsonString(JsonDefaults.Options)));
            case "md":
                FileStore.WriteText(tasks.FilePath(taskId, TaskFiles.ReportMarkdown), preview.Markdown);
                return new ReportFile(fileName, contentType, new UTF8Encoding(false).GetBytes(preview.Markdown));
            case "html":
                return new ReportFile(fileName, contentType, new UTF8Encoding(false).GetBytes(reports.Html(preview.Markdown)));
            case "handoff":
                var handoff = reports.Handoff(preview.Report);
                FileStore.WriteText(tasks.FilePath(taskId, TaskFiles.Handoff), handoff);
                return new ReportFile(fileName, contentType, new UTF8Encoding(false).GetBytes(handoff));
            default:
                throw new UserException("报告格式无效。");
        }
    }

    /// <summary>格式 → 下载文件名与内容类型；未知格式拒绝（与冻结页面同一文案）。</summary>
    public static (string FileName, string ContentType) Descriptor(string format) =>
        ReportFormats.TryDescribe(format) ?? throw new UserException("报告格式无效。");
}
