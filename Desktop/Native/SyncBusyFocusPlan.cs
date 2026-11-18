namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 同步预览那一屏（<c>SyncPreviewDialog</c>）「作业一起来 / 作业一结束，键盘落在哪一颗」的判定
/// （第三百九十六轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：引导横幅那两颗（第三百七十六轮，<see cref="BannerFocusPlan"/>）、任务页作业区那两颗
/// （第三百九十二轮，<see cref="JobFocusPlan"/>）、连接与写入探测那两颗（第三百九十四轮，<see cref="ProbeFocusPlan"/>）
/// 都接了「接不住键盘了就把键盘交给还在屏上的那一颗」。这一屏是<b>同一形状的第四处</b>，而它此前一处都没有：
/// 点下「生成同步预览」或「确认同步」之后，<c>SetBusy(true)</c> 把刚按过的那一颗<b>停用</b>、
/// 把「停止」显出来——停用的控件接不住键盘，框架当场把焦点挪走（挪到父面那一行上），于是
/// 「按完『确认同步』，键盘不在任何能按的东西上」：想停掉这一次同步得先 Tab 一轮找回那颗「停止」。
/// </para>
/// <para>
/// <b>为什么这一屏的代价比别的屏更高</b>：确认同步是<b>不可逆写入</b>，而这一屏的停止是<b>正常操作</b>
/// （执行说明那一行自己写着「可在动作之间停止」）。找不到那颗「停止」的代价不是多按几次 Tab，
/// 是那一次写入没人拦得住。
/// </para>
/// <para>
/// <b>本文件只放词汇与判定</b>（不依赖 WinForms、与测试工程共享编译，真值表在 Linux 真执行）：
/// 算式本身只有一份，在 <see cref="FocusHandover"/> 里——横幅、作业区、探测区、同步预览是同一条规则的四处落点，
/// 各带一套自己的叫法，不在这里再抄一份真值表。这一屏的两支（忙起来 / 忙完）都走
/// <see cref="FocusHandover.HandsAcross"/>：那一判问的正是「原本拿着键盘的那一颗接不住键盘了，
/// 而这一行还有一颗能按的吗」——「停用」与「藏起来」在用户眼里是同一件事（键盘都不在任何能按的东西上）。
/// </para>
/// <para>
/// <b>触达控件的那一步</b>收在 <c>SyncPreviewDialog.SetBusy</c> 一处（它是这一屏「换可用性 + 换显隐 + 交键盘」
/// 的唯一落点），顺序（先记下焦点、再换可用性与显隐、最后交键盘）也只在那一处写一次。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「作业该不该起」「那几颗钮该不该停用」（那是这一屏自己的口径，见 <c>RefreshPlanActionButtons</c>）；
/// 不判「真机上焦点挪得顺不顺眼」——那要 Windows 真机（G-D5，真机 0 台）；
/// 也不判「交出去的那一颗此刻能不能按」（界面自己的 <c>Enabled</c> 说了算：停用的控件接不住键盘，那一步是空操作）。
/// <b>边界</b>：只认那几颗<b>动作钮</b>（「生成同步预览」「确认同步」「重新核对」「查看已有计划」「重新选择接口」）。
/// 关键词框被停用时键盘的去向不在本口径内——那是另一件事，本轮不判、也不假装判过。
/// </para>
/// </summary>
public static class SyncBusyFocusPlan
{
    /// <summary>不动键盘（原本聚焦的那一颗还接得住键盘，或这一行没有可交的落点）。</summary>
    public const int None = -1;

    /// <summary>把键盘交给那颗「停止」。</summary>
    public const int Stop = 0;

    /// <summary>把键盘交回这一页的主动作（预览页是「确认同步」，勾选页是「生成同步预览」）。</summary>
    public const int Primary = 1;

    /// <summary>
    /// 作业一起来（<c>SetBusy(true)</c>）之后该把键盘交给哪一颗：<see cref="Stop"/> / <see cref="None"/>。
    /// <para>
    /// 三个入参都是<b>换可用性之前</b>记下的焦点与<b>换完之后</b>读到的可用性：
    /// 按下去的那一颗还接得住键盘就什么都不做（它自己还拿着键盘）；接不住了就交给「停止」
    /// （那一颗此刻是这一屏唯一还能按的东西）；「停止」也不在屏上时不猜。
    /// </para>
    /// </summary>
    public static int OnBusy(bool pressedHadFocus, bool pressedReachable, bool stopReachable)
        => FocusHandover.HandsAcross(pressedHadFocus, pressedReachable, stopReachable) ? Stop : None;

    /// <summary>
    /// 作业结束（<c>SetBusy(false)</c>）之后该把键盘交给哪一颗：<see cref="Primary"/> / <see cref="None"/>。
    /// <para>
    /// 「停止」被藏起来时它接不住键盘，而键盘正落在它身上（用户刚点过它、或者上一支刚把它交过来）——
    /// 这时交给这一页的主动作；主动作此刻不可用（例如一行都没勾）就不猜，键盘留在框架给的位置上。
    /// </para>
    /// </summary>
    public static int OnIdle(bool stopHadFocus, bool stopVisible, bool primaryReachable)
        => FocusHandover.HandsAcross(stopHadFocus, stopVisible, primaryReachable) ? Primary : None;
}
