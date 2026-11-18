namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 作业区那两颗钮（「开始扫描」与「停止」）「换显隐之后键盘落在哪一颗」的判定（第三百九十二轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：引导横幅那两颗按钮在第三百七十六轮接了「藏起来就把键盘交给还在屏上的那一颗」
/// （<see cref="BannerFocusPlan"/>）。作业区这一对是<b>同一形状</b>：作业起来时「开始扫描」被藏起来、
/// 「停止」显出来；作业结束时「停止」被藏起来、「开始扫描」显回来——而这一对此前没有落点纪律。
/// 焦点正落在被藏起来的那颗上时，框架当场把焦点挪走（实测落在父面那一行上），于是
/// 「按完开始扫描，键盘不在任何能按的东西上」：想停掉这一支作业，得先 Tab 一轮找回那颗「停止」。
/// </para>
/// <para>
/// <b>本文件只放词汇与判定</b>（不依赖 WinForms、与测试工程共享编译，真值表在 Linux 真执行）：
/// 算式本身只有一份，在 <see cref="FocusHandover"/> 里——横幅与作业区是同一条规则的两处落点，
/// 各带一套自己的叫法，不在这里再抄一份真值表。
/// 触达控件的那一步收在 <c>TasksPage.ShowJobState</c> 一处（它是这一行「换显隐 + 交键盘」的唯一落点），
/// 顺序（先记下焦点、再换显隐、最后交键盘）也只在那一处写一次。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「作业该不该起」（那是各屏自己的口径）；不判「真机上焦点挪得顺不顺眼」
/// ——那要 Windows 真机（G-D5，真机 0 台）；也不判「停止」那颗此刻能不能按（界面自己的 <c>Enabled</c> 说了算：
/// 停用的控件接不住键盘，那一步是空操作）。
/// </para>
/// </summary>
public static class JobFocusPlan
{
    /// <summary>不动键盘（原本聚焦的那一颗还在屏上，或两颗都没有焦点）。</summary>
    public const int None = FocusHandover.None;

    /// <summary>把键盘交给「开始扫描」那颗。</summary>
    public const int Scan = FocusHandover.First;

    /// <summary>把键盘交给「停止」那颗。</summary>
    public const int Stop = FocusHandover.Second;

    /// <summary>
    /// 换完显隐之后该把键盘交给哪一颗：<see cref="Scan"/> / <see cref="Stop"/> / <see cref="None"/>。
    /// <para>
    /// 四个入参都是<b>换显隐之前</b>记下的焦点与<b>换显隐之后</b>读到的可见性：
    /// 原本聚焦的那一颗还在屏上就什么都不做（它自己还拿着键盘）；被藏起来了就交给另一颗
    /// （那一颗此刻是这一行上唯一还能按的东西）；两颗都不在屏上时不猜。
    /// </para>
    /// </summary>
    public static int Landing(bool scanHadFocus, bool stopHadFocus, bool scanVisible, bool stopVisible)
        => FocusHandover.Landing(scanHadFocus, stopHadFocus, scanVisible, stopVisible);
}
