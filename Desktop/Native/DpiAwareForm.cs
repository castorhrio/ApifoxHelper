using System.Drawing;
using System.Windows.Forms;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 会跟着 DPI 变更重算的那一类窗体（DESIGN.md §6「DPI 适配」现口径第 ⑥ 条，2026-11-11 第一百七十五轮）。
/// <para>
/// <b>为什么需要一个基类</b>：DPI 变更是以<b>消息</b>送到窗口过程的，而 WinForms 只有覆写
/// <c>WndProc</c> 这一条公开的路能读到它。本仓需要固定设计尺寸的对话框全是
/// <c>sealed class X : Form</c>，没有共同基类就没有一处能统一接住这条消息——
/// 逐个类各写一份覆写，就等于把同一条边界写 25 遍，第 26 个对话框漏掉时不会有任何东西报红。
/// </para>
/// <para>
/// <b>为什么不挂在公开事件上</b>（第一百七十五轮先按那条路做过，wine 实测否掉）：
/// <c>Form.DpiChanged</c> 是 net8.0 的公开事件，一处订阅即全覆盖、那 25 行类声明一个字都不用动。
/// 在 wine 里把这条消息发给一个真实窗体量了一次：窗体过程<b>收到了</b>它
/// （<c>wParam</c> = 12583008，高位 192、低位 96），而事件订阅<b>没有被调用</b>，
/// 框架自己算出的新 DPI 回的是<b>96</b>——与本仓第一百六十二、一百六十三轮量到的
/// 「窗口 DC 恒报 96」「<c>DeviceDpi</c> 恒报 96」同一族。挂在事件上，本机连「有没有跑到」都判不出来；
/// 挂在消息上，读到的是系统写进 <c>wParam</c> 高位的那个数，不经框架那一层解释。
/// 判据与算式都在 <see cref="DpiScale.RescaleOnDpiChanged"/>。
/// </para>
/// <para>
/// <b>基类只多这一件事</b>：认出这条消息、交给 <see cref="DpiScale"/>，其余一律不动。
/// 覆写排在 <c>base</c> <b>之后</b>：这条消息框架自己也要用（更新窗口边界与它自己的 DPI 账），
/// 挡在前面等于替框架决定它该收到什么；本仓全部对话框都不再有第二条 DPI 通路。
/// </para>
/// <para>
/// <b>第三百五十九轮多了一件：主动作的键盘入口</b>（<c>Ctrl+Enter</c>）。三个审阅对话框的主动作
/// （「接受并下一条」「确认」「确认纳入」）逐条清队列时要按几十次，而对话框的 <c>Enter</c> 是「关闭」，
/// 键盘上按一下就走下一条这件事此前没有入口。判定在 <see cref="PrimaryActionKeys"/> 一处，
/// 点按钮只在这里一处（<c>ProcessCmdKey</c>），对话框只登记「本屏的主动作是哪一颗按钮」
/// （<see cref="PrimaryActionButton"/>）——不各写一套按键判定，也不各写一次 <c>PerformClick</c>。
/// </para>
/// </summary>
internal abstract class DpiAwareForm : Form
{
    /// <summary>
    /// 这一屏的 DPI 台账，由 <see cref="DpiScale.Apply"/> 在出厂那一刻写下。
    /// 没写过（<c>null</c>）的窗体不参与重算——编译器管得住类型，管不住有没有人调用，
    /// 理由与后果见 <see cref="DpiScale.RescaleOnDpiChanged"/>。
    /// </summary>
    internal DpiTracking? Tracking { get; set; }

    /// <summary>
    /// 本屏的主动作按钮（<c>Ctrl+Enter</c> 的落点）。<c>null</c> = 这一屏不接这个手势
    /// （表单类对话框的主动作是「保存/确定」，那一步的入口是那颗按钮本身与它自己的校验，
    /// 不为它另开一个键盘入口——见 <c>PrimaryActionKeysTests</c> 的豁免登记）。
    /// <para>
    /// 登记的是<b>按钮本身</b>而不是「点下去做什么」：可用性与可见性由按钮自己带着，
    /// 判定读的就是它此刻的那两个值（<see cref="PrimaryActionKeys.ShouldRun"/>），
    /// 本类不另存一份「现在能不能点」的副本——两份必然漂移。
    /// </para>
    /// </summary>
    internal Button? PrimaryActionButton { get; set; }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != DpiScale.WmDpiChanged) return;
        DpiScale.RescaleOnDpiChanged(this, m.WParam.ToInt64());
    }

    /// <summary>
    /// 主动作的键盘入口（<c>Ctrl+Enter</c>）：手势成立且按钮此刻可用、可见时点它一下并吞掉这一次按键；
    /// 其余一律交回基类（对话框的 <c>Enter</c>=确认/关闭、<c>Esc</c>=取消、各控件的编辑键逐字不变）。
    /// <para>
    /// <b>为什么在 <c>ProcessCmdKey</c> 而不是 <c>ProcessDialogKey</c></b>：后者只收到「对话框键」
    /// （Tab / 方向键 / Enter / Esc 那一族），而多行输入框会先把 <c>Ctrl+Enter</c> 当成自己的编辑键吃掉；
    /// <c>ProcessCmdKey</c> 在控件拿到按键之前就被调用，于是「正在改候选文字时按 Ctrl+Enter」
    /// 与「焦点在清单上按 Ctrl+Enter」是同一件事——这正是这个手势要的（改完就接受并下一条）。
    /// </para>
    /// <para>
    /// <b>不吞键的那一支</b>：按钮禁用（没有选中行、读取失败、作业运行中）或藏起来（读取失败态里
    /// 唯一入口是「重试」）时返回 <c>false</c>，这一次按键照旧往上传——不把「什么都没发生」说成做了什么。
    /// </para>
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (PrimaryActionButton is { } primary
            && (keyData & Keys.KeyCode) == Keys.Enter
            && PrimaryActionKeys.ShouldRun(
                PrimaryActionKeys.IsPrimaryActionGesture(
                    (keyData & Keys.Shift) != 0, (keyData & Keys.Control) != 0, (keyData & Keys.Alt) != 0),
                primary.Enabled,
                primary.Visible))
        {
            primary.PerformClick();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}

/// <summary>
/// 一屏的 DPI 台账：设计尺寸 + 已经应用到哪一档。
/// <para>
/// <c>AppliedFactor</c> 可变是有意的——变更处理每走一步都要把它推进到目标那一档，
/// 下一步的比值才算得对（不推进，来回切一次缩放就把布局叠成 4 倍）。
/// 这份台账只活在窗体实例上：窗体释放即跟着释放，不留进程级的表、也不需要谁记得反注册。
/// </para>
/// </summary>
internal sealed class DpiTracking(Size designSize, float appliedFactor)
{
    public Size DesignSize { get; } = designSize;

    public float AppliedFactor { get; set; } = appliedFactor;
}
