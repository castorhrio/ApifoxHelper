namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 配置读取失败态的口径（第一百五十轮，具名 pass：harden）。
/// <para>
/// <b>为什么要有这个文件</b>：配置页的清单区此前只有两种状态——「还没有配置」与「筛到零行」，
/// 两种都按 DESIGN.md §7 给了「说明现在有什么可做 + 唯一行动按钮」。第三种状态没有：<b>读取失败</b>。
/// 那时 <c>LoadProfiles</c> 的 catch 只往状态行写一句红字（「读取配置失败：…」），清单区保持上一次的样子
/// （首次进入时是整块空白），屏上没有任何可点的东西——用户看到「失败了」，却不知道下一步在哪，
/// 只能去猜「刷新」按钮或者重开窗口。这与 §7「禁用不得移除下一步」是同一条纪律：
/// 失败态也是空态的一种，它同样欠用户一句「现在能做什么」和一个能点的入口。
/// </para>
/// <para>
/// 本文件只放判定与文案，不依赖 WinForms，以共享源码编入测试工程在 Linux 断言；
/// 真实控件上的「失败 → 重试 → 恢复」由冒烟器在 Windows/wine 下走（<c>RunProfileLoadFailure</c>）。
/// </para>
/// </summary>
public static class ProfileLoadState
{
    /// <summary>失败态标题：与「还没有配置」「没有匹配的配置」同一位置、同一字号，只换说法。</summary>
    public const string FailureTitle = "读取配置失败";

    /// <summary>
    /// 失败态的说明行：说清可能的原因，并指名屏上那个按钮（<see cref="RetryLabel"/>）。
    /// 提示语指名的入口必须是界面上真实存在的那一个（第四十二轮纪律），故这里只提「重试」，
    /// 不提「刷新」——工具栏那颗按钮在失败态下做的是同一件事，但一句话只给一个下一步。
    /// </summary>
    public const string FailureHint = "资料目录可能被占用或权限不足；点「重试」再读一次。";

    /// <summary>失败态的唯一行动按钮：与空态的「新建配置」「清空筛选」同一形状（ghost），动作回到同一个读取入口。</summary>
    public const string RetryLabel = "重试";

    /// <summary>
    /// 失败态是否显示「重试」按钮：只在读取失败时显示。
    /// 三种状态（没有配置 / 筛到零行 / 读取失败）同一时刻只显示一个行动按钮——
    /// 失败时给「新建配置」等于把用户往另一条路上引，而这一屏真正缺的是「再读一次」。
    /// </summary>
    public static bool RetryVisible(bool failed) => failed;

    /// <summary>
    /// 配置页状态行：失败原因（已脱敏）跟在标题后面，形状与「读取配置失败：…」一致。
    /// 状态行讲原因、清单区讲下一步，两条通道各说一件事（DESIGN.md §2/§6 双通道）。
    /// </summary>
    public static string FailureStatus(string detail) => FailureTitle + "：" + detail;

    /// <summary>
    /// 对话框里「读取项目列表」「读取分支」失败时补的一句：指名那颗按钮本身就是重试入口。
    /// 对话框不新增第二个「重试」按钮——同一屏两颗做同一件事的按钮违反「唯一行动按钮」，
    /// 而那颗按钮在失败后已经回到可用（<c>finally</c> 里按门控复位），点它就是重试。
    /// </summary>
    public static string RetryHint(string entryLabel) => "可点「" + entryLabel + "」重试。";

    /// <summary>
    /// 对话框失败态的状态行：前缀 + 失败描述 + 重试指引。
    /// 描述末尾的句号先去掉再接「；」，否则会出现「…。；可点…」这种双标点。
    /// </summary>
    public static string PickerFailureStatus(string prefix, string detail, string entryLabel) =>
        prefix + detail.TrimEnd('。', ' ') + "；" + RetryHint(entryLabel);
}
