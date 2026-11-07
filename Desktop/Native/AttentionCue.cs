namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 长作业完成提醒的口径（方案 2.1 原生工作流要素「系统通知」在会话型形态下的落法）。
/// 本工具无常驻托盘（方案 2.2 形态边界），提醒采用 Windows 标准的任务栏闪烁，
/// 只在用户离开时唤回，不弹窗打断。这里只决定「要不要提醒」，闪烁由壳窗口执行；
/// 作业进度与结果的呈现仍在任务页与各对话框的既有状态行，不在本口径内。
/// </summary>
public static class AttentionCue
{
    /// <summary>
    /// 是否需要任务栏提醒：应用没有任何窗口持有焦点、退出未开始、且作业到达需要用户回来的终态。
    /// succeeded（结果待审阅）与 failed（需要看失败原因）提醒；canceled 不提醒——停止是用户自己的动作，结果符合预期。
    /// </summary>
    public static bool ShouldFlash(bool appFocused, string? jobState, bool shutdownStarted)
    {
        if (appFocused || shutdownStarted) return false;
        return jobState is "succeeded" or "failed";
    }
}
