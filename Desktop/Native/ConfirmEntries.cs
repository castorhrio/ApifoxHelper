using System.Globalization;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 原生确认框正文的单一口径（第一百四十六轮，具名 pass：harden）。
/// <para>
/// 存在理由：全原生界面有六处原生确认框（注释回写、写入探测、模型生成、定向构建、同步执行、退出），
/// 其中四处早已把正文收进共享口径（<see cref="ApplyPlanEntries.ConfirmApplyText"/>、
/// <see cref="CompatEntries.ProbeConfirmText"/>、<see cref="DraftEntries.ModelConfirmText"/>、
/// <see cref="BuildStatus.ConfirmText"/>），只有退出确认与同步确认这两处仍就地拼在窗体里。
/// 就地拼的那两段字在 Linux 上构造不出来（WinForms 窗体），于是「正文说了什么」这件事
/// 没有任何机械检查：有人把「未完成的操作下次打开后请重新核对」删掉、把「本地锁不能阻止其他用户的修改」
/// 改成「同步期间不会有别人改」，测试全绿——而这两句正是用户按下「确定退出」「确认同步」之前
/// 读到的唯一一段话。
/// </para>
/// <para>
/// 本文件只依赖 BCL（不引用 WinForms），因此与测试工程共享编译，正文本身可被真执行断言。
/// 数字一律按不变文化格式化：换一台机器（区域设置不同）不会把「15 秒」写成「15,0 秒」。
/// </para>
/// </summary>
public static class ConfirmEntries
{
    /// <summary>
    /// 退出确认的正文：说清「谁还在跑」「等多久」「没做完的下次怎么办」。
    /// <para>
    /// 三件事缺一不可：<paramref name="kinds"/> 是仍在执行的作业类型（用户要知道退出会打断什么），
    /// <paramref name="graceSeconds"/> 是请求停止后最多等多久（不是「立刻退出」），
    /// 末句指向下次打开后的重新核对（未完成的操作不会自动重放，方案 §8.6）。
    /// </para>
    /// </summary>
    public static string ExitConfirmText(string kinds, int graceSeconds) =>
        "以下操作仍在执行：" + kinds + "。\n\n退出时会请求停止，最多等待 "
        + graceSeconds.ToString(CultureInfo.InvariantCulture)
        + " 秒让当前步骤结束。未完成的操作下次打开后请重新核对。\n\n确定退出吗？";

    /// <summary>
    /// 同步确认的标题（原生确认框）：与正文同处一份，界面不就地拼。
    /// </summary>
    public const string SyncConfirmTitle = "确认同步";

    /// <summary>
    /// 同步确认的正文：写哪个项目/分支、写多少、影响面、以及「本地锁挡不住别人」这句。
    /// <para>
    /// <paramref name="outOfScope"/> 为 0 时整句影响面不出现——写「会影响范围外 0 个接口」
    /// 是一句空话，而这一屏的每一句都要能据以决定按不按「确认同步」。
    /// 末句「本地锁不能阻止其他用户的修改」是方案 §8.4 的边界在界面上的唯一表述，不得删。
    /// </para>
    /// </summary>
    public static string SyncConfirmText(string projectId, string branch, int endpoints, int models, int outOfScope) =>
        "将写入 Apifox 项目 " + projectId + " / 分支 " + branch + "："
        + endpoints.ToString(CultureInfo.InvariantCulture) + " 个接口、"
        + models.ToString(CultureInfo.InvariantCulture) + " 个模型。"
        + (outOfScope > 0
            ? "\n共享模型更新会影响范围外 " + outOfScope.ToString(CultureInfo.InvariantCulture) + " 个接口的文档表现。"
            : "")
        + "\n同步期间请避免在 Apifox 中修改相同资源；本地锁不能阻止其他用户的修改。\n\n确认同步？";
}
