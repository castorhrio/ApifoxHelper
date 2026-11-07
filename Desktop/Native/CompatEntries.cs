namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 连接检查与写入兼容探测（F02）的共享口径：探测入口可用性、确认文案、结果文案与徽标文字/色调。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// 探测的写入边界（往返写入、逐项回读、按 CLI 版本登记）仍由既有 CompatService 单一口径执行，本文件不复制。
/// </summary>
public static class CompatEntries
{
    /// <summary>
    /// 「运行写入探测」可用：测试项目 ID 与测试接口 ID 都已填写且为数字，并已勾选确认
    /// （与冻结页面同一校验顺序：输入校验内联展示 + 主按钮灰掉，不弹窗）。
    /// </summary>
    public static bool CanRun(string projectId, string endpointIdText, bool confirmed) =>
        projectId.Length > 0 && projectId.All(char.IsDigit)
        && endpointIdText.Length > 0 && endpointIdText.All(char.IsDigit)
        && confirmed;

    /// <summary>
    /// 探测确认文案：副作用如实说明（原样写回说明、临时修改说明并恢复、原样写回参数/请求体/响应），
    /// 指明目标项目/分支/接口；确认对话框默认按钮在「取消」。
    /// </summary>
    public static string ProbeConfirmText(string projectId, string? branch, string endpointIdText) =>
        "写入兼容探测会对测试接口执行：原样写回说明、临时修改说明并恢复、原样写回参数/请求体/响应，并核对其余字段未变化。\n"
        + "目标：项目 " + projectId + "、" + (string.IsNullOrWhiteSpace(branch) ? "主分支" : "分支 " + branch) + "、接口 ID " + endpointIdText + "。\n\n"
        + "请确认这是专用测试项目或测试分支中可修改的测试接口。确认执行？";

    /// <summary>探测结束的状态行：通过/未通过如实显示，未通过不显示成通过（与冻结页面同一文案口径）。</summary>
    public static (string Text, string Tone) ResultHeadline(bool passed, string cliVersion) => passed
        ? ("CLI " + cliVersion + " 写入探测通过，已开放同步写入。", "ok")
        : ("探测未通过，写入保持关闭。", "bad");

    /// <summary>逐项检查结果的徽标文字与色调（颜色 + 文字双通道，错误不铺底色）。</summary>
    public static (string Text, string Tone) CheckBadge(bool passed) => passed ? ("通过", "ok") : ("失败", "bad");

    /// <summary>连接检查四项的徽标文字与色调。</summary>
    public static (string Text, string Tone) CliBadge(bool found) => found ? ("可用", "ok") : ("不可用", "bad");

    public static (string Text, string Tone) LoginBadge(bool loggedIn) => loggedIn ? ("已登录", "ok") : ("未登录", "bad");

    public static (string Text, string Tone) ProjectBadge(bool readable) => readable ? ("可读取", "ok") : ("失败", "bad");

    /// <summary>写入兼容徽标：仅只读是门禁状态而非故障（只读检查完全可用），用 warn 不用 bad。</summary>
    public static (string Text, string Tone) WriteCompatBadge(bool verified) => verified ? ("已通过探测", "ok") : ("仅只读", "warn");

    /// <summary>探测作业的作用域：与冻结页面 write-probe 路由同一键（profile:{id}），同一配置同时只允许一个探测作业。</summary>
    public static string JobScope(string profileId) => "profile:" + profileId;
}
