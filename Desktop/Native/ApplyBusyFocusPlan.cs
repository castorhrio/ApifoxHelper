namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 注释回写那一屏（<c>ApplyPlanDialog</c>）「忙起来那一刻键盘落在哪」的判定（第三百九十八轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：引导横幅那两颗（第三百七十六轮，<see cref="BannerFocusPlan"/>）、任务页作业区那两颗
/// （第三百九十二轮，<see cref="JobFocusPlan"/>）、连接与写入探测那两颗（第三百九十四轮，<see cref="ProbeFocusPlan"/>）、
/// 同步预览那几颗动作钮（第三百九十六轮，<see cref="SyncBusyFocusPlan"/>）都接了「接不住键盘了就把键盘交给
/// 还在屏上的那一颗」。这一屏是<b>同一形状的第五处</b>，而它此前一处都没有：点下「确认回写」之后
/// <c>SetBusy(true)</c> 把刚按过的那一颗<b>停用</b>——停用的控件接不住键盘，框架当场把焦点挪走。
/// </para>
/// <para>
/// <b>挪到哪去了，是量出来的、不是推出来的</b>：第三百九十八轮先写冒烟段、后改产品，同一版源码在 wine 96 档
/// 当场读到「点下『确认回写』之后键盘没有落在文件清单上（焦点在 「关闭」）」——框架把焦点交给了<b>那颗「关闭」</b>，
/// 而它同时是这一屏的 <c>AcceptButton</c> 与 <c>CancelButton</c>。也就是说：<b>回写正在跑的时候，键盘停在
/// 「关闭」上，一次回车或一次空格就把窗口关掉</b>。这一屏的「确认回写」是<b>不可逆写入</b>（覆盖式写源码），
/// 而这一屏<b>没有「停止」</b>（<c>CommentPlanService</c> 不接受取消）——所以落点不是另一颗钮，
/// 而是这一屏的<b>文件清单</b>：回写结果逐行写进「回写结果」那一列，用户下一步就是在那张清单上逐行看。
/// </para>
/// <para>
/// <b>本文件只放词汇与判定</b>（不依赖 WinForms、与测试工程共享编译，真值表在 Linux 真执行）：
/// 算式本身只有一份，在 <see cref="FocusHandover"/> 里——横幅、作业区、探测区、同步预览、注释回写是同一条规则的
/// 五处落点，各带一套自己的叫法，不在这里再抄一份真值表。这一屏走 <see cref="FocusHandover.HandsAcross"/>：
/// 那一判问的正是「原本拿着键盘的那一颗接不住键盘了，而这一屏还有一颗能按的吗」——
/// 「停用」与「藏起来」在用户眼里是同一件事（键盘都不在任何能按的东西上）。
/// </para>
/// <para>
/// <b>触达控件的那一步</b>收在 <c>ApplyPlanDialog.SetBusy</c> 一处（它是这一屏「换可用性 + 交键盘」的唯一落点），
/// 顺序（先记下焦点、再换可用性、最后交键盘）也只在那一处写一次。
/// </para>
/// <para>
/// <b>为什么没有「忙完」那一支</b>（与 <see cref="SyncBusyFocusPlan"/> 的两支不同）：那一屏忙完时「停止」被藏起来、
/// 键盘正落在它身上，所以要把键盘交回主动作；这一屏忙完时<b>键盘已经在文件清单上</b>（忙起来那一刻交过去的），
/// 而用户此刻正在那张清单上逐行看「回写结果」——再交一次就是把他正在看的那一行抢走。
/// 所以这一屏只有一支判定，忙完什么都不做；这不是漏写，是这一屏的落点本来就只有一处。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「回写该不该起」「那几颗钮该不该停用」（那是这一屏自己的口径，见 <c>SetBusy</c> 与
/// <c>ShowPlan</c>）；不判「真机上焦点挪得顺不顺眼」——那要 Windows 真机（G-D5，真机 0 台）；
/// 也不判「交出去的那一颗此刻能不能按」（界面自己的 <c>Enabled</c> 说了算：停用的控件接不住键盘，那一步是空操作）。
/// <b>边界</b>：只认那三颗<b>动作钮</b>（「确认回写」「导出补丁」「重新生成计划」）。两个关键词框被停用时
/// 键盘的去向不在本口径内——那是另一件事，本轮不判、也不假装判过。
/// </para>
/// </summary>
public static class ApplyBusyFocusPlan
{
    /// <summary>不动键盘（原本聚焦的那一颗还接得住键盘，或这一屏没有可交的落点）。</summary>
    public const int None = FocusHandover.None;

    /// <summary>把键盘交给这一屏的文件清单（回写结果逐行写在那张清单的「回写结果」列里）。</summary>
    public const int List = FocusHandover.First;

    /// <summary>
    /// 忙起来（<c>SetBusy(true)</c>）之后该把键盘交给哪一颗：<see cref="List"/> / <see cref="None"/>。
    /// <para>
    /// 三个入参都是<b>换可用性之前</b>记下的焦点与<b>换完之后</b>读到的可用性：
    /// 按下去的那一颗还接得住键盘就什么都不做（它自己还拿着键盘）；接不住了就交给文件清单
    /// （那一屏此刻唯一还能按、也正是用户下一步要看的东西）；清单不在屏上（整屏停在提示态）时不猜。
    /// </para>
    /// </summary>
    public static int OnBusy(bool pressedHadFocus, bool pressedReachable, bool listReachable)
        => FocusHandover.HandsAcross(pressedHadFocus, pressedReachable, listReachable) ? List : None;
}
