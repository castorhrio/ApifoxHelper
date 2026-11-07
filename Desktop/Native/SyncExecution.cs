namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 同步执行入口的可用性与结果口径（与冻结页面同一规则），不依赖 WinForms，
/// 以共享源码编入测试工程在 Linux 断言：「确认同步」只在计划待确认且任务内容未变化时可用，
/// 过期计划不能再执行；「重新核对（只读）」在待确认（尚无可核对的动作）与执行中（不能同时核对）之外都可用；
/// 执行或核对结束后的状态行按计划状态如实报告，部分完成不显示成全部成功。
/// 写入本身仍由既有 SyncExecutor 单一口径执行，本文件不复制写入边界。
/// </summary>
public static class SyncExecution
{
    /// <summary>「确认同步」可用：计划待确认且生成计划后任务内容没有变化（与冻结页面同一过期规则）。</summary>
    public static bool CanExecute(string status, bool outdated) => status == "awaitingConfirm" && !outdated;

    /// <summary>「重新核对（只读）」可用：待确认时没有可核对的动作，执行中不能同时核对。</summary>
    public static bool CanVerify(string status) => status is not ("awaitingConfirm" or "executing");

    /// <summary>
    /// 执行或核对结束后的状态行文字与语义色调（ok / warn / bad）。
    /// verified/partial/failed/canceled 来自执行结束，interrupted 来自中断恢复，其余状态（含未知值）按计划状态如实回退。
    /// </summary>
    public static (string Text, string Tone) ResultText(string status, int verified, int failed, int notRun)
    {
        var unfinished = failed + notRun;
        return status switch
        {
            "verified" => ("已写入并回读核对通过：" + verified + " 个动作全部核对。建议重新检查，确认远端不再显示差异。", "ok"),
            "partial" => ("部分完成：已核对 " + verified + " 个，未完成 " + unfinished + " 个，逐动作结果见写入清单。已写入的部分不回滚。", "warn"),
            "failed" => ("同步失败：没有动作完成核对，原因见写入清单。", "bad"),
            "canceled" => ("已停止：没有动作完成核对，逐动作状态见写入清单。", "warn"),
            "interrupted" => ("执行已中断：远端实际状态未知，请先重新核对（只读）。", "warn"),
            _ => ("同步未完成，计划状态：" + TaskDisplay.SyncPlanStatusText(status) + "。请查看写入清单后重新核对。", "warn"),
        };
    }
}
