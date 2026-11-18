namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 连接与写入探测那一屏（<c>CapabilitiesDialog</c>）那两颗钮（「运行写入探测」与「停止」）
/// 「换显隐之后键盘落在哪一颗」的判定（第三百九十四轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：引导横幅那两颗（第三百七十六轮，<see cref="BannerFocusPlan"/>）与任务页作业区那两颗
/// （第三百九十二轮，<see cref="JobFocusPlan"/>）都接了「被藏起来就把键盘交给还在屏上的那一颗」。
/// 这一屏是<b>同一形状的第三处</b>：探测一起来，「运行写入探测」被藏起来、「停止」显出来；
/// 探测结束之后反过来（<c>CapabilitiesDialog.ShowProbeRunning</c>）——而这一对至今没有落点纪律。
/// 焦点正落在被藏起来的那颗上时，框架当场把焦点挪走（挪到父面那一行上），于是
/// 「按完『运行写入探测』，键盘不在任何能按的东西上」：想停掉这一次探测，得先 Tab 一轮找回那颗「停止」。
/// 这一屏在产品故事里是「写入前先探测」的入口（F02），而探测本身要往返写三段内容再逐项回读，
/// 中途停下是<b>正常操作</b>——找不到那颗「停止」的代价比别的屏更高。
/// </para>
/// <para>
/// <b>本文件只放词汇与判定</b>（不依赖 WinForms、与测试工程共享编译，真值表在 Linux 真执行）：
/// 算式本身只有一份，在 <see cref="FocusHandover"/> 里——横幅、作业区、探测区是同一条规则的三处落点，
/// 各带一套自己的叫法，不在这里再抄一份真值表。
/// 触达控件的那一步收在 <c>CapabilitiesDialog.ShowProbeRunning</c> 一处（它是这一行「换显隐 + 交键盘」的唯一落点），
/// 顺序（先记下焦点、再换显隐、最后交键盘）也只在那一处写一次。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「探测该不该起」「那颗钮该不该停用」（那是这一屏自己的口径，见 <c>RefreshRunState</c>）；
/// 不判「真机上焦点挪得顺不顺眼」——那要 Windows 真机（G-D5，真机 0 台）；
/// 也不判「交出去的那一颗此刻能不能按」（界面自己的 <c>Enabled</c> 说了算：停用的控件接不住键盘，那一步是空操作）。
/// </para>
/// </summary>
public static class ProbeFocusPlan
{
    /// <summary>不动键盘（原本聚焦的那一颗还在屏上，或两颗都没有焦点）。</summary>
    public const int None = FocusHandover.None;

    /// <summary>把键盘交给「运行写入探测」那颗。</summary>
    public const int Run = FocusHandover.First;

    /// <summary>把键盘交给「停止」那颗。</summary>
    public const int Stop = FocusHandover.Second;

    /// <summary>
    /// 换完显隐之后该把键盘交给哪一颗：<see cref="Run"/> / <see cref="Stop"/> / <see cref="None"/>。
    /// <para>
    /// 四个入参都是<b>换显隐之前</b>记下的焦点与<b>换显隐之后</b>读到的可见性：
    /// 原本聚焦的那一颗还在屏上就什么都不做（它自己还拿着键盘）；被藏起来了就交给另一颗
    /// （那一颗此刻是这一行上唯一还能按的东西）；两颗都不在屏上时不猜。
    /// </para>
    /// </summary>
    public static int Landing(bool runHadFocus, bool stopHadFocus, bool runVisible, bool stopVisible)
        => FocusHandover.Landing(runHadFocus, stopHadFocus, runVisible, stopVisible);
}
