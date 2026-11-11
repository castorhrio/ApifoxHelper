using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 对话框的 DPI 缩放（DESIGN.md §6「DPI 适配」）。
/// <para>
/// <b>为什么要有这个文件</b>：全部对话框的 <c>ClientSize</c> 是固定的 96dpi 逻辑像素，
/// 而窗体没有 <c>AutoScaleDimensions</c>——WinForms 的 <c>AutoScaleMode.Font</c> 在
/// <c>AutoScaleDimensions</c> 为空时**什么都不做**（缩放因子 = 当前字体度量 / 设计基准，
/// 基准为空即不缩放）。于是系统把字体按缩放放大、对话框宽度一动不动：wine 实测 ≈200% 档
/// 说明行文字 1021px 挤进 988px 的可用宽度（第一百六十二轮量到），按钮、列宽、字段网格
/// 同一根因。这不是文案太长，是窗体不随缩放放大。
/// </para>
/// <para>
/// <b>为什么不用 <c>AutoScaleMode.Font</c> + <c>AutoScaleDimensions</c></b>（第一百六十三轮
/// 先按这条路做过，wine 实测否掉）：那条路在 wine 下**自我抵消**。实测（wine 10.0、
/// <c>LogPixels=192</c>）：设 <c>AutoScaleDimensions</c> 时窗体按 2 倍放大到 1608×957，
/// 但**建句柄那一刻 WinForms 又按 0.5 倍缩回去**（804×453）——因为
/// <c>CurrentAutoScaleDimensions</c> 在句柄创建前后量到的不是同一个数：建句柄前取屏幕 DC
/// （192dpi，量到 16×34），建句柄后取窗口 DC（wine 的窗口 DC 恒报 96dpi，量到 8×17），
/// 缩放因子 = 当前 / 基准 于是从 2 变成 0.5。净效果等于没缩放，200% 档照旧放不下。
/// 这不是「wine 的 DeviceDpi 恒报 96」那一条（第一百六十二轮已记），是**窗口 DC 与屏幕 DC
/// 报不同 DPI**，任何单一 <c>AutoScaleDimensions</c> 值都无法同时满足前后两次量测。
/// </para>
/// <para>
/// <b>所以怎么做</b>：缩放因子自己算（屏幕 DC 的 <c>GetDeviceCaps(LOGPIXELSY) / 96</c>，
/// 即系统字体缩放比），缩放动作仍交给框架的 <c>Control.Scale</c>——那正是 WinForms 自动缩放
/// 内部用的同一个原语（<c>PerformAutoScale</c> → <c>ScaleControl</c>），只是由我们决定
/// 「什么时候、按几倍」：<c>AutoScaleMode = None</c> 让框架不再自己动手，构造末尾按因子
/// 显式缩放一次。这样 wine 与真机走同一条路、同一个数，200% 档在 wine 下可验证。
/// </para>
/// <para>
/// <b>为什么 <c>Scale</c> 不缩放字体是对的</b>：DPI 翻倍时字号（磅）不变、像素尺寸自然翻倍，
/// 要放大的是**布局**（边距、控件边界、列宽）。实测 <c>Scale(2,2)</c> 后窗体客户区
/// 1020×740 → 2040×1480（被屏幕最大窗口尺寸截到 1604×953），标签位置 16,16 → 32,32，
/// 而字体仍是 9pt / 29px——正是要的效果。
/// </para>
/// <para>
/// <b>量不到怎么办</b>：屏幕 DPI 取不到时因子按 1 处理（= 不缩放），与修复前同一行为，
/// 不猜一个数把布局撑坏。
/// </para>
/// <para>
/// <b>挂账（第一百七十五轮把变更接上了，量到的仍是空框）</b>：创建期按<b>主屏</b>的屏幕 DC 取一次因子，
/// 变更期由基类 <see cref="DpiAwareForm"/> 覆写 <c>WndProc</c> 认出 <c>WM_DPICHANGED</c>，从
/// <c>wParam</c> 高位取<b>要去的那一块屏</b>的 DPI，跟着新因子重算——「窗口建好之后拖到另一块缩放
/// 不同的显示器」与「运行中改系统缩放比例」两种情况从此不再停在创建那一刻的尺寸。
/// 公开事件 <c>Form.DpiChanged</c> 那一条路本轮先试过、被 wine 实测否掉：消息进得到窗口过程
/// （<c>wParam</c> = 12583008，即 192 左移 16 位再并上 96），事件订阅却没被调用、框架算出的新 DPI 还是旧的 96
/// （与第一百六十二、一百六十三轮量到的「窗口 DC 与 <c>DeviceDpi</c> 恒报 96」同一族）——
/// 挂在事件上，本机连「有没有跑到」都判不出来。但这两档的<b>实测读数一个都没有</b>：
/// 本环境只有一块 Xvfb 虚拟屏、副屏 0 块，wine 也不会<b>自发</b>这条消息
/// （<c>LogPixels</c> 要重启前缀才生效，那是三档静态量测，不是运行中变更），
/// 所以「系统自己发这条消息时屏幕上放得下」这一句从没量过，随 G-D5 第 3、4 条复核。
/// 另两条如实记下：① 本实现<b>不消费系统建议的窗口矩形</b>（消息参数里那块 suggested rect），
/// 只把客户区按设计尺寸与新因子重算、位置仍由系统摆——PerMonitorV2 的完整逐屏支持（逐屏取 DPI、
/// 按建议矩形摆放）是另一件事，登记在 <c>docs/NOTES.md</c>，不混进这一条已修里；② 进程入口确实是
/// PerMonitorV2（<c>Desktop/DesktopApp.cs</c>），但那只是让系统按所在屏给出正确的像素，
/// 不等于本类在两屏之间被真机验证过。
/// </para>
/// </summary>
internal static class DpiScale
{
    /// <summary><c>GetDeviceCaps</c> 的 <c>LOGPIXELSY</c>：屏幕 DC 的纵向 DPI。</summary>
    private const int LogPixelsY = 90;

    /// <summary>
    /// DPI 变更消息号：Win32 里那条消息写作 <c>WM_DPICHANGED</c>，值是 736。窗口进入另一块不同缩放的屏、
    /// 或系统缩放被改时，系统给<b>每一个</b>顶层窗口发这一条。这里按十进制写：本仓的门要求
    /// 十六进制字面量只许出现在 <c>UiTokens.cs</c>（那是颜色唯一定义位，<c>TokenWiringTests</c> 判），
    /// 而消息号与颜色共用同一个形状——为了一个号去放宽那条门，代价比省下一个括号大。
    /// </summary>
    internal const int WmDpiChanged = 736;

    /// <summary>
    /// 给对话框装上 DPI 缩放：设计尺寸按 96dpi 逻辑像素给，窗体随系统字体缩放等比放大。
    /// <para>
    /// 字体与缩放基准取同一份（<see cref="UiTokens.Body"/>）：窗体字体必须与设计基准一致，
    /// 否则 100% 档就会凭空缩放。故本方法一并把窗体字体设成它，调用点不必再自己保证两处一致。
    /// </para>
    /// <para>
    /// 缩放因子与「要不要真的缩放」都不在本文件算：那是纯算术，取
    /// <see cref="DpiScaleFactor"/>（与测试工程共享编译，Linux 侧有真值表）。
    /// </para>
    /// <para>
    /// 参数类型是 <see cref="DpiAwareForm"/> 而不是 <see cref="Form"/>：需要固定设计尺寸的窗体
    /// 必须同时是「会跟着 DPI 变更重算」的窗体。这两件事原先分开写着就会漂移——某一屏接了缩放
    /// 却没登记台账，在 200% 副屏上停在创建那一刻的尺寸，而这种屏不会在任何读数里被认出来。
    /// </para>
    /// </summary>
    public static void Apply(DpiAwareForm form, Size designSize)
    {
        form.Font = UiTokens.Body.Value;
        // 关掉框架自己的自动缩放：见类注释——wine 下它按窗口 DC 重算基准，会把刚放大的窗体缩回去。
        // 缩放由本方法末尾显式做一次，wine 与真机因此走同一条路。
        form.AutoScaleMode = AutoScaleMode.None;
        form.ClientSize = designSize;
        var factor = ScaleFactor();
        if (DpiScaleFactor.ShouldScale(factor)) form.Scale(new SizeF(factor, factor));
        form.Tracking = new DpiTracking(designSize, factor);
    }

    /// <summary>
    /// DPI 变更那一次重算（第一百七十五轮，简报 P0-1）。基类的 <c>WndProc</c> 在 <c>base</c> 之后调进来。
    /// <para>
    /// <b>读消息，不读那个公开事件</b>：<c>Form.DpiChanged</c> 是 net8.0 的公开事件，本轮先在 wine 里
    /// 把这条消息发给一个真实窗体量过一次——窗体过程收到了它（<c>wParam</c> = 12583008，高 192 低 96），
    /// 而事件订阅那一次<b>没有被调用</b>、框架自己算出来的新 DPI 回的是 <b>96</b>（屏上的数在 wine 里滞后，
    /// 与第一百六十二、一百六十三轮量到的「窗口 DC 恒报 96」「<c>DeviceDpi</c> 恒报 96」同一族）。
    /// 挂在事件上，这一档在真机上到底走不走得通本机判不出来；挂在消息上，读到的是系统写进
    /// <c>wParam</c> 高位的那个「要去的那一块屏的 DPI」，不经过框架那一层解释。
    /// </para>
    /// <para>
    /// <b>判断与算术都不在本方法里</b>（第一百八十六轮收口）：该不该重算、按几倍缩放控件树、
    /// 客户区多大、台账推进到哪一档，全部由 <see cref="DpiScaleFactor.PlanRescale"/> 一次给出；
    /// 本方法只剩三个动作——缩放控件树、设客户区、推进台账。抽出去的理由是「四处判断各写一份」这一族：
    /// 改一处漏一处不会有东西报红，而漏掉的那一处在真机上表现为「拖到副屏没重算」这种没人量得到的样子。
    /// 现在真值表（含新 DPI 高于 / 低于 / 等于当前、DPI 为 0 或负数、台账因子为 0 或负数五类输入）
    /// 在 Linux 上跑的就是这一份判断。
    /// </para>
    /// <para>
    /// <b>两个数各按一种口径算，不能混</b>：控件树走<b>相对</b>步长（<see cref="DpiScaleFactor.ScaleStep"/>）——
    /// 它此刻的边界已经是按 <c>applied</c> 放大过的那一份，直接乘目标因子会让
    /// 100%→200%→100%→200% 一档一档叠上去；客户区走<b>绝对</b>重算
    /// （<see cref="DpiScaleFactor.ScaledExtent"/>：设计尺寸 × 新因子）。绝对那一句不是重复劳动：
    /// 系统在同一条消息里也会按建议矩形改窗口边界，而那一段在本机既收不到也量不到，
    /// 把最终尺寸定成「只由 (designSize, 新因子) 决定」，框架在那之前动了什么就不影响这一屏的结论。
    /// 这是设计选择，不是实测结论——实测仍随 G-D5 第 3、4 条。
    /// </para>
    /// <para>
    /// <c>Tracking</c> 为空是可能的：<see cref="DpiAwareForm"/> 只保证「会重算」，不保证这一屏真经
    /// <see cref="Apply"/> 出厂过（编译器管得住类型，管不住有没有人调用）。没登记就不重算，
    /// 与修复前同一行为——而不是拿一个空的设计尺寸去缩放。
    /// </para>
    /// </summary>
    internal static void RescaleOnDpiChanged(DpiAwareForm form, long wParam)
    {
        var track = form.Tracking;
        if (track is null) return;
        var plan = DpiScaleFactor.PlanRescale(
            DpiScaleFactor.NewDpiFromWParam(wParam),
            track.AppliedFactor,
            track.DesignSize.Width,
            track.DesignSize.Height);
        if (plan is null) return;
        form.Scale(new SizeF(plan.Step, plan.Step));
        form.ClientSize = new Size(plan.ClientWidth, plan.ClientHeight);
        track.AppliedFactor = plan.AppliedFactor;
    }

    /// <summary>
    /// 系统字体缩放比 = 屏幕 DPI / 96（算式在 <see cref="DpiScaleFactor.FromScreenDpi"/>）。
    /// <para>
    /// 不用 <c>Control.DeviceDpi</c>：wine 下它恒报 96，而字体确实按 <c>LogPixels</c> 放大
    /// （第一百六十二轮实测），拿它当缩放比会让因子恒为 1。
    /// </para>
    /// </summary>
    public static float ScaleFactor() => DpiScaleFactor.FromScreenDpi(ScreenDpi());

    /// <summary>
    /// 屏幕 DC 的纵向 DPI（<c>GetDeviceCaps(LOGPIXELSY)</c>）。取不到时回 0。
    /// </summary>
    public static int ScreenDpi()
    {
        using var screen = Graphics.FromHwnd(IntPtr.Zero);
        var hdc = screen.GetHdc();
        try
        {
            return GetDeviceCaps(hdc, LogPixelsY);
        }
        finally
        {
            screen.ReleaseHdc(hdc);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);
}
