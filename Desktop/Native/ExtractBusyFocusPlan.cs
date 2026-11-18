namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「模块与模板」那一屏（<c>ProfileAdvancedDialog</c>）点下「从接口提取」之后键盘落在哪（第四百轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：引导横幅那两颗（第三百七十六轮，<see cref="BannerFocusPlan"/>）、任务页作业区那两颗
/// （第三百九十二轮，<see cref="JobFocusPlan"/>）、连接与写入探测那两颗（第三百九十四轮，<see cref="ProbeFocusPlan"/>）、
/// 同步预览那几颗动作钮（第三百九十六轮，<see cref="SyncBusyFocusPlan"/>）、注释回写那一屏（第三百九十八轮，
/// <see cref="ApplyBusyFocusPlan"/>）都接了「接不住键盘了就把键盘交给还在屏上的那一颗」。这一屏是<b>同一形状的第六处</b>，
/// 而它此前一处都没有：点下「从接口提取」之后 <c>_extractButton.Enabled = false</c> 把刚按过的那一颗<b>停用</b>
/// ——停用的控件接不住键盘，框架当场把焦点挪走。
/// </para>
/// <para>
/// <b>挪到哪去了，是量出来的、不是推出来的</b>：第四百轮先写冒烟段、后改产品，同一版源码在 wine 96 档当场读到
/// 「点下『从接口提取』之后键盘落在「取消」上」——框架把焦点交给了那颗「取消」，而它是这一屏的 <c>CancelButton</c>。
/// 这一屏的「取消」是一颗<b>按钮</b>：键盘停在按钮上时一次回车就是<b>点那一颗</b>（按钮自己接住回车），
/// 于是「提取正在跑的时候按一下回车」= 把整屏取消掉，刚填的接口 ID 与这一屏上所有改动一起丢。
/// </para>
/// <para>
/// <b>为什么落点是「模块」那一格而不是别的</b>：这一屏没有清单、也没有「停止」（<c>ExtractAsync</c> 不接受取消），
/// 能接住键盘又不带副作用的落点只有<b>多行输入框</b>——单行框里回车会触发这一屏的 <c>AcceptButton</c>（「确定」），
/// 忙起来时一次回车就把还没检查的回填字段应用掉，那是比「取消」更贵的一种误触。
/// 「模块」是这一屏的第一格（<c>_modules</c>，多行、可滚动），也是用户回填之后从上往下逐项检查的起点：
/// 键盘落在这一格，按 Tab 就顺着往下走，回车只是换行。
/// </para>
/// <para>
/// <b>本文件只放词汇与判定</b>（不依赖 WinForms、与测试工程共享编译，真值表在 Linux 真执行）：
/// 算式本身只有一份，在 <see cref="FocusHandover"/> 里——横幅、作业区、探测区、同步预览、注释回写与这一屏
/// 是同一条规则的六处落点，各带一套自己的叫法，不在这里再抄一份真值表。这一屏走
/// <see cref="FocusHandover.HandsAcross"/>：那一判问的正是「原本拿着键盘的那一颗接不住键盘了，
/// 而这一屏还有一颗能按的吗」——「停用」与「藏起来」在用户眼里是同一件事（键盘都不在任何能按的东西上）。
/// </para>
/// <para>
/// <b>触达控件的那一步</b>收在 <c>ProfileAdvancedDialog.ExtractAsync</c> 一处（它是这一屏「换可用性 + 交键盘」的
/// 唯一落点），顺序（先记下焦点、再换可用性、最后交键盘）也只在那一处写一次。
/// </para>
/// <para>
/// <b>为什么没有「忙完」那一支</b>（与 <see cref="SyncBusyFocusPlan"/> 的两支不同）：那一屏忙完时「停止」被藏起来、
/// 键盘正落在它身上，所以要把键盘交回主动作；这一屏忙完时<b>键盘已经在「模块」那一格上</b>（忙起来那一刻交过去的），
/// 而用户此刻要做的正是从上往下检查回填的字段——再交一次就是把他正在看的那一格抢走。
/// 所以这一屏只有一支判定，忙完什么都不做；这不是漏写，是这一屏的落点本来就只有一处。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「提取该不该起」「那颗钮该不该停用」（那是这一屏自己的口径，见 <c>ExtractAsync</c>）；
/// 不判「真机上焦点挪得顺不顺眼」——那要 Windows 真机（G-D5，真机 0 台）；
/// 也不判「交出去的那一格此刻能不能按」（界面自己的 <c>Enabled</c> 说了算：停用的控件接不住键盘，那一步是空操作）。
/// <b>边界</b>：只认那一颗<b>动作钮</b>（「从接口提取」）。这一屏其余输入框被停用时键盘的去向不在本口径内
/// ——那是另一件事，本轮不判、也不假装判过。
/// </para>
/// </summary>
public static class ExtractBusyFocusPlan
{
    /// <summary>不动键盘（原本聚焦的那一颗还接得住键盘，或这一屏没有可交的落点）。</summary>
    public const int None = FocusHandover.None;

    /// <summary>把键盘交给这一屏的第一格（「模块」，多行输入框——回车只是换行，不会触发「确定」）。</summary>
    public const int FirstField = FocusHandover.First;

    /// <summary>
    /// 忙起来（<c>_extractButton.Enabled = false</c>）之后该把键盘交给哪一格：<see cref="FirstField"/> / <see cref="None"/>。
    /// <para>
    /// 三个入参都是<b>换可用性之前</b>记下的焦点与<b>换完之后</b>读到的可用性：
    /// 按下去的那一颗还接得住键盘就什么都不做（它自己还拿着键盘）；接不住了就交给第一格
    /// （这一屏此刻唯一能接住键盘又不带副作用的地方）；第一格不在屏上时不猜。
    /// </para>
    /// </summary>
    public static int OnBusy(bool pressedHadFocus, bool pressedReachable, bool firstFieldReachable)
        => FocusHandover.HandsAcross(pressedHadFocus, pressedReachable, firstFieldReachable) ? FirstField : None;
}
