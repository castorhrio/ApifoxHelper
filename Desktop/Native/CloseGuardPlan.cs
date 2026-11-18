namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「作业正在跑的时候，这一次关窗该不该拦下」的判定（第四百零二轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：连接与写入探测那一屏（<c>CapabilitiesDialog</c>）的「运行写入探测」会在用户的
/// 测试接口上做<b>三类往返写入</b>，而那颗「停止」是这一屏<b>唯一的停止入口</b>。此前 Esc 在
/// <c>ProcessDialogKey</c> 里直接 <c>Close()</c>、标题栏 × 走同一条路——探测正在跑的时候按一下 Esc，
/// 窗口关掉、那颗「停止」跟着消失，而作业在服务层继续跑（<c>JobRunner</c> 不因窗口关闭而取消）。
/// 用户以为「我取消了」，实际是「我把唯一的停止入口关掉了」。
/// </para>
/// <para>
/// <b>为什么不是「Esc 改成请求停止」</b>：Esc 与 × 在这一屏是<b>同一个语义</b>（关窗），
/// 把 Esc 单独改成「停止」会让同一个按键在「跑着」与「没跑」两种状态下做两件不同的事，
/// 而 × 又没法跟着改（× 就是关窗）。所以这一屏只做一件事：<b>跑着的时候不关，并说清为什么</b>——
/// 那颗「停止」就在屏上，用户下一步按得出来。
/// </para>
/// <para>
/// <b>两个入参为什么要分开</b>：<c>busy</c> 是「有没有作业在跑」，<c>userInitiated</c> 是「这一次关窗是不是
/// 用户按出来的」（Esc / × / <c>CancelButton</c> 都是 <c>CloseReason.UserClosing</c>）。
/// 系统关机、任务管理器结束进程、父窗口关闭（<c>CloseReason.WindowsShutDown</c> /
/// <c>TaskManagerClosing</c> / <c>FormOwnerClosing</c>）<b>不拦</b>：那些不是「用户想关这一屏」，
/// 拦下来只会让系统关不掉一个进程，而作业照样会随进程结束——拦它没有意义，也不该拦。
/// </para>
/// <para>
/// <b>触达控件的那一步</b>收在 <c>CapabilitiesDialog.OnFormClosing</c> 一处：它是这一屏
/// 「Esc / × / CancelButton」三条路唯一的汇合点（<c>CancelButton</c> 的点击最终也走 <c>Close()</c>），
/// 所以判定只在那里接一次，不在 <c>ProcessDialogKey</c> 里另写一份。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「探测该不该起」「那颗『停止』该不该显出来」（那是这一屏自己的口径，
/// 见 <c>RunProbe</c> 与 <c>ShowProbeRunning</c>）；不判「真机上按 Esc 的手感」（G-D5，真机 0 台）。
/// <b>边界（本轮不接的第二处）</b>：同步预览那一屏（<c>SyncPreviewDialog</c>）是同一形状的第二处
/// ——作业跑着的时候关窗同样会把那颗「停止」一起关掉。本轮只接探测这一屏（它写的是用户的测试接口、
/// 且没有「重开可查」以外的恢复路径），同步预览那一屏如实挂在这里，下一轮要接时按同一形状接，
/// 不在这里假装已经接上。
/// </para>
/// </summary>
public static class CloseGuardPlan
{
    /// <summary>
    /// 探测正在跑的时候关窗被拦下，状态行说的那一句（<b>唯一落点</b>：界面与冒烟段都读它，不各写一份）。
    /// <para>
    /// 写的是「先按『停止』再关闭窗口」而不是一句让人干等的等待话：这一屏的下一步在屏上按得出来
    /// （那颗「停止」），而「稍候」是一句没有落点的话（§7「禁用不得移除下一步」同一口径）。
    /// </para>
    /// </summary>
    public const string ProbeBusyMessage = "写入兼容探测正在进行，先按「停止」再关闭窗口；关掉窗口就没有停止入口了。";

    /// <summary>
    /// 这一次关窗该不该拦下：作业在跑、且这一次关窗是用户按出来的（Esc / × / <c>CancelButton</c>）。
    /// <para>
    /// 系统关机、任务管理器、父窗口关闭不拦——理由见类注释「两个入参为什么要分开」。
    /// </para>
    /// </summary>
    public static bool RefuseWhileBusy(bool busy, bool userInitiated) => busy && userInitiated;
}
