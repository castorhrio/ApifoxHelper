using System.Globalization;
using System.Text.Json.Nodes;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 定向构建入口的口径（与冻结页面同一规则），不依赖 WinForms，
/// 以共享源码编入测试工程在 Linux 断言：工具默认不构建、不 restore、不启动服务，
/// 构建是用户确认后才执行的动作，确认文案如实说明将执行的命令与副作用；
/// 上次构建的状态行按退出码与超时如实报告，未完成不显示成完成。
/// 构建本身仍由既有 BuildService 单一口径执行，本文件不复制构建边界。
/// </summary>
public static class BuildStatus
{
    /// <summary>确认对话框正文：将执行的命令与副作用如实说明（与冻结页面同一口径）。</summary>
    public static string ConfirmText(string command) =>
        "将在工作区执行：" + command + "。会生成 bin/obj 输出，不会还原依赖或启动服务。";

    /// <summary>「执行定向构建」可用：命令来自任务配置快照的 API 项目路径，需要已选任务；作业执行中由任务页统一禁用。</summary>
    public static bool CanStart(bool hasTask) => hasTask;

    /// <summary>
    /// 上次构建的状态行文字与语义色调（ok / warn / bad / muted）。
    /// 数据来自任务目录 build.json（BuildService 落盘）：零退出码=完成；超时或非零退出码=未完成，
    /// 如实指出并指向「查看构建输出」；记录缺少退出码时不猜测结果，显示为不可判断。
    /// </summary>
    public static (string Text, string Tone) LastText(JsonNode? record)
    {
        if (record is null) return ("尚未构建", "muted");
        var exitNode = record["exitCode"];
        if (exitNode is null)
        {
            return ("上次构建记录缺少退出码，无法判断结果；可重新执行构建覆盖。", "bad");
        }
        var exitCode = exitNode.GetValue<int>();
        var seconds = FormatSeconds(record["seconds"]);
        if (record["timedOut"]?.GetValue<bool>() == true)
        {
            return ("上次构建：超时被终止（执行 " + seconds + " 秒），XML 文档可能没有刷新，输出尾部见「查看构建输出」。", "bad");
        }
        if (exitCode != 0)
        {
            return ("上次构建：退出码 " + exitCode + "，" + seconds + " 秒，构建未成功，原因见「查看构建输出」。", "bad");
        }
        return ("上次构建：退出码 0，" + seconds + " 秒。", "ok");
    }

    private static string FormatSeconds(JsonNode? seconds) =>
        seconds is null ? "—" : Math.Round(seconds.GetValue<double>(), 1).ToString("0.#", CultureInfo.InvariantCulture);
}
