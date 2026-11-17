namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 引导横幅那两颗按钮「刷新之后键盘落在哪一颗」的判定（第三百七十六轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：第三百六十八轮把横幅那两颗按钮改成「出厂一次、刷新只换字与显隐」，
/// 当时的理由是「藏起来的控件不参与 Tab 序、也不占位，与摘掉在屏上一样，但焦点不会因此丢」。
/// 那句话只对<b>没被聚焦</b>的那一颗成立：用户 Tab 到次动作那颗、按空格之后，那一次动作会改任务状态，
/// 紧接着的 <c>RefreshDetail</c> 把次动作<b>藏起来</b>——而此刻键盘正落在它身上。控件被藏起来时
/// 框架会把焦点挪走，挪到哪一颗不由这一页决定（实测落在父面那一行上），于是「按完一次就得重新 Tab 回来」。
/// </para>
/// <para>
/// <b>本文件只放判定</b>（不依赖 WinForms、与测试工程共享编译，真值表在 Linux 真执行）：
/// 焦点原本在哪一颗、刷新之后哪几颗还在屏上，答「该把键盘交给哪一颗」。
/// 触达控件的那一步收在 <c>TasksPage.RefreshNextBanner</c> 一处——它是横幅这一行「换字 + 显隐 + 交键盘」
/// 的唯一落点，顺序（先记下焦点、再换显隐、最后交键盘）也只在那一处写一次。
/// </para>
/// <para>
/// <b>为什么顺序是判据</b>：<c>Visible = false</c> 一落下去，框架当场就把焦点挪走了——之后再读
/// <c>Focused</c> 读到的是「已经不在它身上」，那一判永远答「不用交」。所以焦点必须在换显隐<b>之前</b>记下来。
/// </para>
/// <para>
/// <b>为什么不是「每次都交给主动作」</b>：焦点原本在主动作上、主动作也还在屏上时，交一次是白交
/// （<c>Focus()</c> 对已聚焦的控件是空操作，但「每次都交」会让「焦点原本在哪」这件事不再被读，
/// 下一轮有人把次动作那颗也接上时就会静默抢走键盘）。所以只有「原本聚焦的那一颗被藏起来了」才交。
/// </para>
/// </summary>
public static class BannerFocusPlan
{
    /// <summary>不动键盘（原本聚焦的那一颗还在屏上，或两颗都没有焦点）。</summary>
    public const int None = -1;

    /// <summary>把键盘交给主动作那一颗。</summary>
    public const int Primary = 0;

    /// <summary>把键盘交给次动作那一颗。</summary>
    public const int Secondary = 1;

    /// <summary>
    /// 刷新之后该把键盘交给哪一颗：<see cref="Primary"/> / <see cref="Secondary"/> / <see cref="None"/>。
    /// <para>
    /// 四个入参都是<b>换显隐之前</b>记下的焦点与<b>换显隐之后</b>读到的可见性：
    /// 原本聚焦的那一颗还在屏上就什么都不做（它自己还拿着键盘）；
    /// 被藏起来了就交给另一颗——那一颗此刻是这一行上唯一还能按的东西，也是「下一步」本身；
    /// 两颗都不在屏上时不猜（这一行没有可交的落点，硬塞给别处会让下一步 Tab 从别的地方起手）。
    /// </para>
    /// </summary>
    public static int Landing(bool primaryHadFocus, bool secondaryHadFocus, bool primaryVisible, bool secondaryVisible)
    {
        if (primaryHadFocus) return primaryVisible ? None : (secondaryVisible ? Secondary : None);
        if (secondaryHadFocus) return secondaryVisible ? None : (primaryVisible ? Primary : None);
        return None;
    }
}
