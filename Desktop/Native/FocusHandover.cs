namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「原本聚焦的那一颗被藏起来了，键盘交给还在屏上的那一颗」这一条规则的<b>唯一算式</b>（第三百九十二轮，具名 pass：harden）。
/// <para>
/// <b>为什么抽出来</b>：这条规则此前只写在引导横幅那两颗钮上（<see cref="BannerFocusPlan"/>）。作业区那两颗钮
/// （「开始扫描」与「停止」）是<b>同一形状</b>：一颗被藏起来、另一颗显出来，而被藏起来的那颗此刻正拿着键盘。
/// 两处各写一份真值表就是两份可能各自漂移的算式——所以算式只有这一份，两处各带一套自己的词汇
/// （横幅叫「主动作 / 次动作」，作业区叫「开始扫描 / 停止」）。
/// </para>
/// <para>
/// <b>规则本身</b>：焦点原本在哪一颗、换完显隐之后哪几颗还在屏上，答「该把键盘交给哪一颗」。
/// 原本聚焦的那一颗还在屏上 → <b>什么都不做</b>（它自己还拿着键盘；白交一次会让「焦点原本在哪」这件事不再被读，
/// 下一轮有人把另一颗也接上时就会静默抢走键盘）；原本聚焦的那一颗被藏起来了 → <b>交给另一颗</b>
/// （那一颗此刻是这一行上唯一还能按的东西）；两颗都不在屏上 → <b>不猜</b>（这一行没有可交的落点，
/// 硬塞给别处会让下一步 Tab 从别的地方起手）。
/// </para>
/// <para>
/// <b>为什么顺序是判据</b>：<c>Visible = false</c> 一落下去，框架当场就把焦点挪走了——之后再读
/// <c>Focused</c> 读到的是「已经不在它身上」，那一判永远答「不用交」。所以焦点必须在换显隐<b>之前</b>记下来。
/// 触达控件的那一步收在各屏自己的落点处（横幅是 <c>TasksPage.RefreshNextBanner</c>，作业区是
/// <c>TasksPage.ShowJobState</c>），顺序（先记下焦点、再换显隐、最后交键盘）也只在那一处写一次。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「该不该换显隐」（那是各屏自己的口径）；不判「真机上焦点挪得顺不顺眼」
/// ——那要 Windows 真机（G-D5，真机 0 台）；也不判「交出去的那一颗此刻能不能按」（那由界面自己的
/// <c>Enabled</c> 决定：停用的控件接不住键盘，那一步是空操作，键盘不落也不丢）。
/// </para>
/// </summary>
public static class FocusHandover
{
    /// <summary>不动键盘（原本聚焦的那一颗还在屏上，或两颗都没有焦点）。</summary>
    public const int None = -1;

    /// <summary>把键盘交给这一行的第一颗（横幅那边是主动作，作业区那边是「开始扫描」）。</summary>
    public const int First = 0;

    /// <summary>把键盘交给这一行的第二颗（横幅那边是次动作，作业区那边是「停止」）。</summary>
    public const int Second = 1;

    /// <summary>
    /// 换完显隐之后该把键盘交给哪一颗：<see cref="First"/> / <see cref="Second"/> / <see cref="None"/>。
    /// <para>
    /// 四个入参都是<b>换显隐之前</b>记下的焦点与<b>换显隐之后</b>读到的可见性：
    /// 原本聚焦的那一颗还在屏上就什么都不做；被藏起来了就交给另一颗；两颗都不在屏上时不猜。
    /// </para>
    /// </summary>
    public static int Landing(bool firstHadFocus, bool secondHadFocus, bool firstVisible, bool secondVisible)
    {
        if (firstHadFocus) return firstVisible ? None : (secondVisible ? Second : None);
        if (secondHadFocus) return secondVisible ? None : (firstVisible ? First : None);
        return None;
    }
}
