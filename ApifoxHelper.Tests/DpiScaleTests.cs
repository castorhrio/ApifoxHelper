using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// DPI 缩放的 Linux 侧机械门（第一百六十七轮，简报 P1-1）。
/// <para>
/// <b>为什么这一条值得单独钉</b>：≈200% 档的溢出是第一百六十二轮在 wine 下量到的真实缺陷，
/// 修法是 <c>DpiScale.Apply</c>（第一百六十三轮）。但修完之后，Linux 侧没有任何一条断言看过
/// 「缩放因子怎么算」「框架自动缩放有没有被关掉」「窗体是不是还自己写死尺寸」这三件事——
/// 全部证据只有 wine 报告，而 wine 报告不区分「按因子 2 放大」与「按因子 1 什么都没做」，
/// 它只说这一档放得下。把放得下当成缩放生效，是一个可以自己把自己判绿的门。
/// </para>
/// <para>
/// 本文件把能机械判的那半判掉：算式走 <see cref="DpiScaleFactor"/>（纯算术、与测试工程共享编译，
/// 真值表在 Linux 真执行），接线走行首正则（只判代码行，注释里引用被否决的旧写法是说明理由）。
/// 剩下那半——缩放真的发生后屏幕上放得下——仍然只有构造得起来的窗口能给，随 wine 冒烟与 G-D5 真机复核。
/// </para>
/// </summary>
public class DpiScaleTests
{
    private const string ScaleFile = """
        <Compile Include="..\Desktop\Native\DpiScaleFactor.cs" Link="Shared\DpiScaleFactor.cs" />
        """;

    /// <summary>
    /// 缩放因子 = 屏幕 DPI ÷ 96（DESIGN.md §3/§4 的档位基准）。
    /// 96 / 120 / 192 就是 wine 三档实测改的 LogPixels；144 是真机上最常见的 150%。
    /// </summary>
    [Theory]
    [InlineData(96, 1f)]
    [InlineData(120, 1.25f)]
    [InlineData(144, 1.5f)]
    [InlineData(192, 2f)]
    [InlineData(48, 0.5f)]    // 算式给的就是比值；小于 1 由 ShouldScale 挡掉，不在这里改口径
    public void ScaleFactor_IsScreenDpiOverTheDesignBaseline(int dpi, float expected) =>
        Assert.Equal(expected, DpiScaleFactor.FromScreenDpi(dpi));

    /// <summary>
    /// 量不到 DPI 时回 1，不猜一个数。
    /// <para>
    /// 猜错的因子会把每一处边距、列宽、按钮尺寸一起放大或缩掉，比不缩放更难诊断；
    /// 而 <c>GetDeviceCaps</c> 失败给出的正是 0——这一条钉的是「0 与负数都不算出一个缩放比」。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void ScaleFactor_NeverGuessesWhenTheDpiIsMissing(int dpi) =>
        Assert.Equal(1f, DpiScaleFactor.FromScreenDpi(dpi));

    /// <summary>基准 DPI 只有一个值，且界面代码不得改它：字号档与间距档都按它记录。</summary>
    [Fact]
    public void ScaleFactor_DesignBaselineIs96() => Assert.Equal(96f, DpiScaleFactor.DesignDpi);

    /// <summary>
    /// 因子 1 时不缩放（DESIGN.md §6「DPI 适配」第 ④ 条）。
    /// <para>
    /// 存在的理由：<c>Scale(1,1)</c> 在数学上是恒等，但仍会重算整棵子树的布局；
    /// 「量不到 DPI 就与修复前同一行为」这句话要靠这一条成立，而不是靠看起来一样。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1f, false)]
    [InlineData(0.5f, false)]     // 缩小不是本口径要做的事（因子只可能 ≥1：量不到回 1）
    [InlineData(1.0001f, true)]
    [InlineData(1.25f, true)]
    [InlineData(2f, true)]
    public void ShouldScale_OnlyWhenTheWindowMustGrow(float factor, bool expected) =>
        Assert.Equal(expected, DpiScaleFactor.ShouldScale(factor));

    /// <summary>
    /// 变更期：只有正数 DPI 才算量到了（第一百七十五轮，简报 P0-1）。
    /// <para>
    /// 存在的理由：创建期那条「量不到回 1」在这里是<b>错的</b>——变更事件报不出 DPI，与「系统本来就是
    /// 100%」不是一件事；照创建期回落成 1，会把一屏已经按 200% 放开的边距与列宽整个收回去。
    /// 这一条钉住「读不出就不动」那个入口，后面几支算式都不该被一个空的读数驱动。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(96, true)]
    [InlineData(192, true)]
    [InlineData(0, false)]
    [InlineData(-96, false)]
    public void UnusableDpi_NeverDrivesARescale(int dpi, bool expected) =>
        Assert.Equal(expected, DpiScaleFactor.IsUsableDpi(dpi));

    /// <summary>
    /// 新 DPI 取 <c>wParam</c> 的<b>高位</b>：高位是窗口进入的那块屏的 DPI，低位是它离开的那块屏的。
    /// <para>
    /// <c>0xC00060</c> 不是编出来的数——它是第一百七十五轮在 wine 里把这条消息发给一个真实窗体时，
    /// 窗体过程实际收到的那一个 <c>wParam</c>（高 192、低 96）。同一跑里 <c>DeviceDpi</c> 与
    /// <c>GetDpiForWindow</c> 都还回 96，所以取错一侧不是「算错一个像素」，而是<b>整次重算不发生</b>
    /// （因子算回 1，与修复前同一行为）——而这一条在本机上不会有任何东西报红。
    /// 最后那一档（只有低位有数）就是这一族最像「看起来对」的写法：按低位取会算出 96，
    /// 一个完全合理、却属于<b>来处</b>那一屏的数。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(0xC00060, 192)]     // wine 实测那一个样本：高 192（要去的那块屏）、低 96（来的那块屏）
    [InlineData(0xC000C0, 192)]     // 运行中改系统缩放：两半同一个数
    [InlineData(0x780078, 120)]     // 125% 档
    [InlineData(0x60, 0)]           // 只有低位：按低位取就会拿到 96 这一档
    [InlineData(0, 0)]
    public void NewDpi_ComesFromTheHighWordOfWParam(long wParam, int expected) =>
        Assert.Equal(expected, DpiScaleFactor.NewDpiFromWParam(wParam));

    /// <summary>
    /// 取错字节的后果必须落在「不重算」这一侧，而不是落进缩放里。
    /// 这一条把上一支的 <c>0</c> 与 <see cref="UnusableDpi_NeverDrivesARescale"/> 接起来：
    /// 按低位取到的那一个 0 会被挡在门口（不动布局），而按低位取到 96 的那一种写法谁也挡不住。
    /// </summary>
    [Fact]
    public void NewDpi_HighWordOnly_LeavesTheLowWordOutOfTheCaliber()
    {
        Assert.Equal(192, DpiScaleFactor.NewDpiFromWParam((192 << 16) | 96));
        Assert.False(DpiScaleFactor.IsUsableDpi(DpiScaleFactor.NewDpiFromWParam(96)));
    }

    /// <summary>
    /// 变更期要不要重排：与<b>已经应用过的那一份</b>比，不同才动；缩小也算不同。
    /// <para>
    /// 与 <see cref="ShouldScale_OnlyWhenTheWindowMustGrow"/> 的分别是真的，不是把一个判子抄两遍：
    /// 创建期那个「因子 &gt; 1」挡的是「量不到时不许凭空缩放」，而变更期每一步都有明确的来与去，
    /// 200% 回到 100% 时那一步 0.5 必须执行——套用创建期的口径它会挡掉，屏幕上就留下一屏双倍的布局。
    /// 容差那一档（1 与 1.0001）挡反向变异：差在渲染上看不出来时，也不该重排整棵子树。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1f, 1f, false)]         // 同一个因子：一条命令都不该发
    [InlineData(1f, 2f, true)]          // 100% → 200%
    [InlineData(2f, 1f, true)]          // 200% → 100%：缩小在变更期是恢复，不是猜
    [InlineData(1.25f, 2.5f, true)]     // 125% → 250%
    [InlineData(1f, 1.0001f, false)]    // 差在容差之内 = 没变
    public void ShouldRescale_WhenTheFactorDiffersFromTheAppliedOne(float applied, float target, bool expected) =>
        Assert.Equal(expected, DpiScaleFactor.ShouldRescale(applied, target));

    /// <summary>
    /// 缩放步长 = 目标 ÷ 已应用，不是目标本身。
    /// <para>
    /// <c>Control.Scale</c> 乘的是<b>此刻</b>的控件边界，而此刻那份布局已经是上一档放大过的。
    /// 拿目标因子直接乘是本族最容易写错的那一种：单看第一次缩放看不出来（100% → 200% 时两个数都是 2），
    /// 只有来回切一次才露出来——所以下一支用例把往返真走出来，不靠这里多写一句承诺。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1f, 2f, 2f)]        // 100% → 200%：比值与目标同号，这一档单独测不出错
    [InlineData(2f, 1f, 0.5f)]      // 200% → 100%：写成目标因子就乘 1，双倍布局留在屏上
    [InlineData(1.25f, 2.5f, 2f)]   // 125% → 250%
    [InlineData(2f, 1.5f, 0.75f)]   // 200% → 150%
    [InlineData(1f, 1f, 1f)]
    public void ScaleStep_IsTheRatioToTheAppliedFactor(float applied, float target, float expected) =>
        Assert.Equal(expected, DpiScaleFactor.ScaleStep(applied, target));

    /// <summary>
    /// 连续三次缩放变化之后，累计步长必须等于最后那次的目标因子——不叠加。
    /// <para>
    /// 这一条是 <see cref="ScaleStep_IsTheRatioToTheAppliedFactor"/> 的实物那一半：把
    /// 「100% → 200% → 100% → 200%」按变更处理那一步一步走出来（每一步都先把已应用因子推进到目标，
    /// 与 <c>DpiScale</c> 里那两行的先后同一）。漏掉推进，累计值会是 8 倍而不是 2 倍。
    /// 客户区那一寸同时核：它按设计尺寸绝对重算，所以永远落在 2 倍上，与中间走了几步无关。
    /// </para>
    /// </summary>
    [Fact]
    public void SuccessiveDpiChanges_ComposeToTheNewFactorAndNeverCompound()
    {
        var applied = 1f;
        var cumulative = 1f;
        foreach (var dpi in new[] { 192, 96, 192 })
        {
            var target = DpiScaleFactor.FromScreenDpi(dpi);
            if (!DpiScaleFactor.ShouldRescale(applied, target)) continue;
            cumulative *= DpiScaleFactor.ScaleStep(applied, target);
            applied = target;
        }

        Assert.Equal(2f, cumulative);
        Assert.Equal(2f, applied);
        Assert.Equal(2040, DpiScaleFactor.ScaledExtent(1020, applied));
    }

    /// <summary>
    /// 客户区尺寸按因子放大，并远离零取整。
    /// <para>
    /// 1025 × 1.5 = 1537.5 是这一族里唯一落在半像素上的那一档：<c>(int)</c> 的截断给 1537，
    /// 于是每一屏每一档都系统性偏小半个像素，而这一族要答的问题正是「放得下放不下」——
    /// 差的那半像素落在错误一侧时，仓内算出的可用宽与真机量到的就对不上
    /// （第一百六十二轮量到的 1021 挤进 988，正是这种「差一点」的形态）。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1020, 1f, 1020)]     // 100%：设计尺寸原样
    [InlineData(1020, 2f, 2040)]     // 200%：wine 实测那一档
    [InlineData(680, 1.25f, 850)]    // 125%：「新建配置」那一屏
    [InlineData(740, 1.5f, 1110)]    // 150%：真机上最常见的一档
    [InlineData(1025, 1.5f, 1538)]   // 半像素：远离零取整，不是截断
    public void ScaledClientExtent_GrowsWithTheFactorAndRoundsAwayFromZero(int designExtent, float factor, int expected) =>
        Assert.Equal(expected, DpiScaleFactor.ScaledExtent(designExtent, factor));

    /// <summary>
    /// <c>DpiScale.Apply</c> 的接线：字体与设计基准同源、关掉框架自动缩放、按设计尺寸给客户区、
    /// 末尾按因子显式缩放一次——且顺序不能反（先设字体再算尺寸，否则 100% 档就凭空缩放一次）。
    /// </summary>
    [Fact]
    public void Apply_TurnsOffFrameworkAutoScaleAndScalesOnce()
    {
        var body = ApplyBody();
        Assert.True(
            Regex.IsMatch(body, @"^\s*form\.Font = UiTokens\.Body\.Value;$", RegexOptions.Multiline),
            "Apply 没有把窗体字体设成设计基准那一份（UiTokens.Body）：字体与缩放基准不同源时，100% 档也会缩放。");
        Assert.True(
            Regex.IsMatch(body, @"^\s*form\.AutoScaleMode = AutoScaleMode\.None;$", RegexOptions.Multiline),
            "Apply 没有关掉框架自己的自动缩放：留着 Font/Dpi 模式，wine 下它会按窗口 DC 把刚放大的窗体再缩回去。");
        Assert.True(
            Regex.IsMatch(body, @"^\s*form\.ClientSize = designSize;$", RegexOptions.Multiline),
            "Apply 没有把设计尺寸交给窗体：窗体停在默认大小，缩放因子算得再对也没有东西可放大。");
        Assert.True(
            Regex.IsMatch(body, @"^\s*var factor = ScaleFactor\(\);$", RegexOptions.Multiline),
            "Apply 没有走 ScaleFactor 取因子（本文件或别处另算了一份缩放比——缩放比只应有一处算法）。");
        Assert.True(
            Regex.IsMatch(body, @"^\s*if \(DpiScaleFactor\.ShouldScale\(factor\)\) form\.Scale\(new SizeF\(factor, factor\)\);$", RegexOptions.Multiline),
            "Apply 末尾没有按因子显式缩放一次，或不再经 ShouldScale 判该不该缩放。");

        // 顺序：字体 → 基准关 → 设计尺寸 → 缩放（缩放必须在尺寸之后，否则放大的是一次空白布局）。
        var font = IndexOfCode(body, "form.Font = UiTokens.Body.Value;");
        var autoScale = IndexOfCode(body, "form.AutoScaleMode = AutoScaleMode.None;");
        var clientSize = IndexOfCode(body, "form.ClientSize = designSize;");
        var scale = IndexOfCode(body, "form.Scale(new SizeF(factor, factor))");
        Assert.True(font >= 0 && font < autoScale && autoScale < clientSize && clientSize < scale,
            $"Apply 里这四步的顺序错了（字体 {font} → 关自动缩放 {autoScale} → 设计尺寸 {clientSize} → 缩放 {scale}）。");
    }

    /// <summary>
    /// DPI 变更那一次重算的接线（第一百七十五轮，简报 P0-1）。
    /// <para>
    /// <b>为什么必须钉接线</b>：上面那几支真值表判的是算式，而算式没人调用时它们照样全绿——
    /// 挂账那两档（拖到副屏、运行中改缩放）缺的从来不是「会不会算」，是「系统报来的新 DPI 有没有人接着算」。
    /// 本仓同族的前例是第一百六十七轮那句「把放得下当成缩放生效，是一个可以自己把自己判绿的门」。
    /// 判据取自 <c>DpiScale.cs</c> 与 <c>DpiAwareForm.cs</c> 的代码行：消息号、取 DPI 的那一侧、
    /// 五个纯算式的调用、台账的登记与推进，以及「先按比值缩放控件树、再按设计尺寸绝对设客户区」的先后。
    /// </para>
    /// <para>
    /// <b>反向断言各挡一种写法</b>：① 处理里再取一次<b>主屏</b>因子（<c>ScaleFactor()</c> /
    /// <c>ScreenDpi()</c>）——那正是这一档要修的缺陷本身：窗口已经在副屏上，读主屏会永远算回主屏那一档；
    /// ② 在窗体代码里 <c>/ 96</c> 自己算缩放比（缩放比只应有一处算法）；③ 按<b>目标因子</b>而不是比值
    /// 缩放控件树——第一次看不出问题，来回切一次就把布局叠成 4 倍；④ 读框架那一份 DPI
    /// （<c>DeviceDpi</c> / <c>GetDpiForWindow</c>）——本轮 wine 实测这两处在这条消息里都还回旧值 96，
    /// 拿它们当新 DPI 等于永不重算，而「永不重算」在这台机器上不会有任何东西报红。
    /// </para>
    /// </summary>
    [Fact]
    public void DpiChangeHandler_RescalesFromTheMessageNotThePrimaryScreen()
    {
        var apply = ApplyBody();
        Assert.True(
            Regex.IsMatch(apply, @"^\s*public static void Apply\(DpiAwareForm form, Size designSize\)$", RegexOptions.Multiline),
            "Apply 的参数类型不再是 DpiAwareForm：需要固定设计尺寸的窗体与会重算的窗体又分成两件事了。");
        Assert.True(
            Regex.IsMatch(apply, @"^\s*form\.Tracking = new DpiTracking\(designSize, factor\);$", RegexOptions.Multiline),
            "Apply 末尾没登记台账：重算那一段拿不到设计尺寸与已应用因子，挂账那两档照旧停在创建那一刻。");

        var body = RescaleBody();
        Assert.True(
            Regex.IsMatch(body, @"^\s*var newDpi = DpiScaleFactor\.NewDpiFromWParam\(wParam\);$", RegexOptions.Multiline),
            "新 DPI 不是从消息的 wParam 取的：换成框架那一份读数，wine 实测它会滞后在旧屏那一档。");
        Assert.True(
            Regex.IsMatch(body, @"^\s*if \(!DpiScaleFactor\.IsUsableDpi\(newDpi\)\) return;$", RegexOptions.Multiline),
            "没有先挡掉读不出的 DPI：一个空的读数会把已经放大的布局整个收回去。");
        Assert.True(
            Regex.IsMatch(body, @"^\s*var target = DpiScaleFactor\.FromScreenDpi\(newDpi\);$", RegexOptions.Multiline),
            "变更期没有经 FromScreenDpi 算因子（缩放比只应有一处算法）。");
        Assert.True(
            Regex.IsMatch(body, @"^\s*if \(!DpiScaleFactor\.ShouldRescale\(track\.AppliedFactor, target\)\) return;$", RegexOptions.Multiline),
            "没有与台账上那一档比就该不该重排：因子没变也会重排整棵子树。");
        Assert.True(
            Regex.IsMatch(body, @"^\s*var step = DpiScaleFactor\.ScaleStep\(track\.AppliedFactor, target\);$", RegexOptions.Multiline),
            "没有按比值算缩放步长：控件树此刻已经是上一档放大过的那一份。");
        Assert.True(
            Regex.IsMatch(body, @"^\s*form\.Scale\(new SizeF\(step, step\)\);$", RegexOptions.Multiline),
            "缩放控件树用的不是那个比值步长。");
        Assert.True(
            Regex.IsMatch(body, @"^\s*form\.ClientSize = new Size\(DpiScaleFactor\.ScaledExtent\(track\.DesignSize\.Width, target\),$", RegexOptions.Multiline),
            "没有按设计尺寸与新因子绝对设客户区（绝对那一句是让最终尺寸与框架先做了什么无关的那一步）。");
        Assert.Contains("DpiScaleFactor.ScaledExtent(track.DesignSize.Height, target)", body, StringComparison.Ordinal);
        Assert.True(
            Regex.IsMatch(body, @"^\s*track\.AppliedFactor = target;$", RegexOptions.Multiline),
            "台账没有推进到目标那一档：下一次变更会拿旧因子算比值。");

        // 顺序：算步长 → 缩放控件树 → 绝对设客户区 → 推进台账。
        // 推进排在算步长之前等于把旧因子丢了（下一档的比值算成 1）；绝对那一句排在缩放之前，
        // 等于把刚乘完的边界又按设计尺寸覆掉一半。
        var step = IndexOfCode(body, "var step = DpiScaleFactor.ScaleStep(track.AppliedFactor, target);");
        var scale = IndexOfCode(body, "form.Scale(new SizeF(step, step))");
        var clientSize = IndexOfCode(body, "form.ClientSize = new Size(DpiScaleFactor.ScaledExtent(track.DesignSize.Width");
        var advance = IndexOfCode(body, "track.AppliedFactor = target;");
        Assert.True(step >= 0 && step < scale && scale < clientSize && clientSize < advance,
            $"变更处理这四步的顺序错了（算步长 {step} → 缩放 {scale} → 设客户区 {clientSize} → 推进台账 {advance}）。");

        // ① 不许再回主屏取一次因子。
        foreach (var primaryScreen in new[] { "ScaleFactor()", "ScreenDpi()" })
        {
            Assert.False(CodeScan.HasLine(body, primaryScreen),
                $"变更处理里又去取主屏的因子（{primaryScreen}）：窗口已经在副屏上时，这一档永远算不回副屏那一档。");
        }

        // ② 不许在窗体代码里另算一份缩放比。
        Assert.False(Regex.IsMatch(CodeScan.Text(body), @"/\s*96"),
            "变更处理里直接除了一个 96：缩放比只应有一处算法（DpiScaleFactor.FromScreenDpi）。");

        // ③ 不许按目标因子缩放控件树（只有比值那一种写法）。
        Assert.False(CodeScan.HasLine(body, "form.Scale(new SizeF(target, target)"),
            "按目标因子缩放控件树：第一次看不出问题，来回切一次布局就叠成 4 倍。");

        // ④ 不许读框架那一份 DPI 读数（wine 实测在这条消息里滞后）。
        foreach (var late in new[] { "DeviceDpi", "GetDpiForWindow" })
        {
            Assert.False(CodeScan.HasLine(body, late),
                $"变更处理里读了 {late}：本轮 wine 实测这条消息到达时它还停在旧屏那一档，读了就等于永不重算。");
        }
    }

    /// <summary>
    /// 消息这一侧的接线（<c>DpiAwareForm.cs</c>）：认出的是那一条消息、排在 <c>base</c> 之后、
    /// 交出去的是 <c>wParam</c> 这一个数。
    /// <para>
    /// 存在的理由：<see cref="DpiChangeHandler_RescalesFromTheMessageNotThePrimaryScreen"/> 判的是
    /// 算式那一段被怎么调用，而<b>谁把它调起来</b>这一段如果错了（消息号写歪、把消息吞了不再交给 base、
    /// 或者传 <c>lParam</c> 那个建议矩形过去），前面所有断言照样全绿。
    /// 排在 <c>base</c> 之后这条不是偏好：框架自己也要用这条消息更新它那一层的账。
    /// </para>
    /// </summary>
    [Fact]
    public void TheBaseClass_RecognizesTheMessageAndStillLetsFrameworkHaveIt()
    {
        var source = Repo.Source("Desktop", "Native", "DpiAwareForm.cs");
        var start = source.IndexOf("protected override void WndProc(", StringComparison.Ordinal);
        Assert.True(start >= 0, "DpiAwareForm.cs 里没有 WndProc 覆写（那条消息就没人认）。");
        var tracking = source.IndexOf("internal sealed class DpiTracking", start, StringComparison.Ordinal);
        Assert.True(tracking > start, "WndProc 与 DpiTracking 之间切不出那一段。");
        var wndProc = source[start..tracking];
        Assert.Contains("base.WndProc(ref m);", wndProc, StringComparison.Ordinal);
        Assert.True(
            Regex.IsMatch(wndProc, @"^\s*if \(m\.Msg != DpiScale\.WmDpiChanged\) return;$", RegexOptions.Multiline),
            "基类没有按那条消息号筛（只认一条 DPI 变更消息，其余一律原样交出去）。");
        Assert.True(
            Regex.IsMatch(wndProc, @"^\s*DpiScale\.RescaleOnDpiChanged\(this, m\.WParam\.ToInt64\(\)\);$", RegexOptions.Multiline),
            "基类没有把 wParam 交给重算那一段（换成 lParam 就是换成了系统建议的矩形，不是新 DPI）。");
        Assert.True(IndexOfCode(wndProc, "base.WndProc(ref m);") < IndexOfCode(wndProc, "if (m.Msg != DpiScale.WmDpiChanged)"),
            "基类在 base 之前就拦了这条消息：框架那一层的 DPI 账不会再更新。");
        Assert.DoesNotContain("DefWndProc", wndProc, StringComparison.Ordinal);

        var scaleSource = Repo.Source("Desktop", "Native", "DpiScale.cs");
        Assert.True(
            Regex.IsMatch(scaleSource, @"internal const int WmDpiChanged = 736;", RegexOptions.Multiline),
            "DPI 变更那条消息的号不再是 736（WM_DPICHANGED；号写错，这一族就整族失效）。");
    }

    /// <summary>
    /// 需要固定设计尺寸的窗体，必须同时是会重算的那一类窗体（覆盖面的封闭门）。
    /// <para>
    /// 存在的理由：编译器已经能挡住「经 <c>DpiScale.Apply</c> 出厂但不接消息」那一类（<c>Apply</c>
    /// 的参数类型是 <see cref="DpiAwareForm"/>），挡不住反过来的漂移——新加一屏直接
    /// <c>: Form</c> 并自己写死尺寸，<see cref="FixedDesignSize_GoesThroughTheDpiGateOnly"/>
    /// 会红，但把它换成「不设 ClientSize、也不接消息」就两扇门都不响，而那一屏在 200% 副屏上
    /// 停在创建那一刻的尺寸，正是本轮修掉的那一档。所以这里把「<c>Desktop/Native</c> 下还有谁
    /// 直接继承 <c>Form</c>」收成一份封闭清单。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryTopLevelForm_EitherDerivesFromTheDpiAwareBaseOrIsRegistered()
    {
        // 登记的两份例外各有理由：DpiAwareForm 是本族唯一的实现（它必须直接继承 Form）；
        // NativeShellForm 是主窗口，用户可拖拽、初始 1400×900 + MinimumSize，没有「96dpi 设计尺寸 × 因子」
        // 这一说，布局由 Dock/AutoSize 给——它不经 Apply 出厂，也就不属这一族要重算的屏
        // （缩放换档时主窗口的观感随 G-D5 第 2 条那一屏量）。
        string[] allowed = ["DpiAwareForm", "NativeShellForm"];
        var offenders = Repo.NativeSources()
            .SelectMany(src => CodeLines(src.Text)
                .Where(line => Regex.IsMatch(line, @"class\s+[A-Za-z0-9_]+\s*:\s*Form\b"))
                .Select(line => $"{src.Name}：{line.Trim()}"))
            .Where(line => !allowed.Any(name => line.Contains($"class {name} : Form", StringComparison.Ordinal)))
            .ToList();
        Assert.True(offenders.Count == 0,
            "这些窗体直接继承 Form、不经 DpiAwareForm 出厂（要么改基类，要么在这里登记例外并写清它为什么不需要重算）："
            + string.Join(" / ", offenders));

        // 反向那一半：基类自己必须是 abstract，否则「有台账但没人接消息」的写法又能回来。
        var baseSource = Repo.Source("Desktop", "Native", "DpiAwareForm.cs");
        Assert.Contains("internal abstract class DpiAwareForm : Form", baseSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed class DpiAwareForm", baseSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// 被 wine 否掉的第二条路也不许回来：不订阅 <c>Form.DpiChanged</c>、不读那个事件参数。
    /// <para>
    /// 与 <see cref="TheRejectedAutoScalePath_StaysOutOfTheCode"/> 同一族、同一个理由：本轮在 wine 里
    /// 量到这条消息进得到窗口过程（<c>wParam = 0xC00060</c>）而事件订阅没被调用、框架算出的新 DPI
    /// 回的是旧的 96。挂上去的后果不是崩，是「本机看不出、真机上也许永远不跑」——
    /// 而那正是这一族最擅长伪装成已修的那一种形态。
    /// </para>
    /// </summary>
    [Fact]
    public void TheRejectedEventPath_StaysOutOfTheCode()
    {
        var offenders = Repo.NativeSources()
            .SelectMany(src => CodeLines(src.Text)
                .Where(line => line.Contains("DpiChanged += ", StringComparison.Ordinal)
                               || line.Contains("DeviceDpiNew", StringComparison.Ordinal)
                               || line.Contains("GetDpiForWindow", StringComparison.Ordinal))
                .Select(line => $"{src.Name}：{line.Trim()}"))
            .ToList();
        Assert.True(offenders.Count == 0,
            "这些代码行回到了本轮被 wine 实测否掉的那条路（订阅框架事件 / 读框架的 DPI 读数）："
            + string.Join(" / ", offenders));
    }

    /// <summary>
    /// 被 wine 否掉的那条路不许回来：任何界面源码的代码行都不设 <c>AutoScaleDimensions</c>、
    /// 也不把 <c>AutoScaleMode</c> 设成 Font/Dpi（第一百六十三轮实测：屏幕 DC 与窗口 DC 报不同 DPI，
    /// 任何单一基准值都会被建句柄后的那一次重算缩回去）。
    /// <para>
    /// 只判代码行——注释里引用这两个名字是在交代为什么不走这条路，不是第二份实现。
    /// </para>
    /// </summary>
    [Fact]
    public void TheRejectedAutoScalePath_StaysOutOfTheCode()
    {
        var offenders = Repo.NativeSources()
            .SelectMany(src => CodeLines(src.Text)
                .Where(line => line.Contains("AutoScaleDimensions", StringComparison.Ordinal)
                    || Regex.IsMatch(line, @"AutoScaleMode\s*=\s*(AutoScaleMode\.)?(Font|Dpi)"))
                .Select(line => $"{src.Name}：{line.Trim()}"))
            .ToList();
        Assert.True(offenders.Count == 0,
            "这些代码行回到了被 wine 实测否掉的 AutoScaleDimensions 路线：" + string.Join(" / ", offenders));
    }

    /// <summary>
    /// 设计尺寸只有一个落点：界面代码不得自己写 <c>ClientSize = </c> 绕过 <c>DpiScale.Apply</c>。
    /// <para>
    /// 存在的理由：第一百六十二轮量到的缺陷就是「ClientSize 固定逻辑像素且不随缩放放大」。
    /// 写死尺寸本身不是错（对话框要有设计尺寸），错的是绕过 Apply——绕过的那一屏在 ≈200% 档照样溢出，
    /// 而它不会在任何 wine 报告里被认出来（那一屏根本没人给它算过因子）。
    /// </para>
    /// </summary>
    [Fact]
    public void FixedDesignSize_GoesThroughTheDpiGateOnly()
    {
        var offenders = Repo.NativeSources()
            .Where(src => src.Name != "DpiScale.cs")
            .SelectMany(src => CodeLines(src.Text)
                .Where(line => Regex.IsMatch(line, @"^\s*([A-Za-z_][A-Za-z0-9_]*\.)?ClientSize\s*="))
                .Select(line => $"{src.Name}：{line.Trim()}"))
            .ToList();
        Assert.True(offenders.Count == 0,
            "这些代码行直接设 ClientSize、没有经 DpiScale.Apply：" + string.Join(" / ", offenders)
            + "（写死尺寸的那一屏在 ≈200% 档不会放大，第一百六十二轮量到的正是这一族）。");
    }

    /// <summary>
    /// DESIGN.md §6 必须有「DPI 适配」这一条，并且它把挂账写成<b>可被引用的两档</b>。
    /// <para>
    /// 第一百六十九轮补的后半（简报 P1-1 的实底）：条款里现口径那五项用 ①…⑤ 记，
    /// 而挂账当时是一段散文、没有自己的标签，<c>docs/NOTES.md</c> 里四处「挂账第①档/第②档」
    /// 按字面找到的其实是现口径的 ①②（「窗体一律经 `DpiScale.Apply` 出厂」与「因子算式」）——
    /// 引用看着精确，指向的却是错的条目。这正是第一百六十七轮「§6 从来没有这一条」那一族，
    /// 只是这一次的形态是「有条款、没标签」。原先这条门只查 <c>WM_DPICHANGED</c> 与
    /// 「从没量过」两个子串在不在，散文形态照样绿。
    /// </para>
    /// </summary>
    [Fact]
    public void DesignDoc_DeclaresTheDpiRuleAndItsGap()
    {
        var section6 = Repo.DesignSection(6);
        Assert.Contains("| **DPI 适配**", section6);
        Assert.Contains("AutoScaleMode", section6, StringComparison.Ordinal);
        Assert.Contains("DpiScaleFactor", section6, StringComparison.Ordinal);
        // 挂账不许被写成已解决：多屏与运行中改缩放这两档必须点名。
        Assert.Contains("WM_DPICHANGED", section6, StringComparison.Ordinal);
        Assert.Contains("从没量过", section6, StringComparison.Ordinal);

        // 两档必须有标签，且各说清是哪一种情况（标签是 NOTES 那四处引用的落点）。
        Assert.Contains("**第 1 档 多显示器混合缩放**", section6, StringComparison.Ordinal);
        Assert.Contains("**第 2 档 运行中改系统缩放**", section6, StringComparison.Ordinal);
        Assert.Contains("两套序号不共用", section6, StringComparison.Ordinal);

        // 引用侧逐处核：只取「挂账第…档 / 同一挂账的第…档」这一种**引用形状**，
        // 序号必须是本条款里真实存在的第 1 档或第 2 档。
        // 不要改成「禁掉某个子串」：第一百六十九轮第一版就是这么写的，结果它把本轮记录里
        // 「原先写成了什么样子」那句原话一起判红——分不清真引用与对旧写法的说明的门，
        // 下一轮只会被人放宽成没门。圈号那一形取到即红，因为它与本条款现口径的 ①…⑤ 撞号。
        var notes = Repo.Source("docs", "NOTES.md");
        var references = Regex.Matches(notes, @"(?:挂账|同一挂账的)第 ?([0-9①②③④⑤]) ?档");
        Assert.True(references.Count >= 4,
            $"docs/NOTES.md 只有 {references.Count} 处按「第 N 档」引用这条挂账（动作单两行 + B 段两条）。");
        foreach (Match m in references)
        {
            var tier = m.Groups[1].Value;
            Assert.True(tier is "1" or "2",
                $"{m.Value}：挂账只有第 1 档（多显示器混合缩放）与第 2 档（运行中改系统缩放），"
                + "写成 ①② 那一种会落进本条款现口径的 ①…⑤，指向的是别的条目。");
            Assert.Contains($"**第 {tier} 档 ", section6, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 真机复核清单的第 3、4 行必须逐字对上挂账那两档（简报 P1-1 要的那一件事）。
    /// 挂账第 1 档讲的是显示器、第 2 档讲的是运行中改缩放——两行里各引一个标签，
    /// 且第 3 行的动作确实发生在「拖到另一块屏」上、第 4 行确实发生在「不重开窗口改缩放」上。
    /// </summary>
    [Fact]
    public void RealMachineChecklist_PointsWithTheGapTierTheyMeasure()
    {
        var notes = Repo.Source("docs", "NOTES.md").Split('\n');
        var rows = notes.Where(l => l.StartsWith("| [ ] | 3. ", StringComparison.Ordinal)
                                    || l.StartsWith("| [ ] | 4. ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, rows.Count);

        var mixed = rows.Single(l => l.StartsWith("| [ ] | 3. ", StringComparison.Ordinal));
        Assert.Contains("挂账第 1 档", mixed, StringComparison.Ordinal);
        Assert.Contains("多显示器混合缩放", mixed, StringComparison.Ordinal);
        Assert.Contains("拖到副屏", mixed, StringComparison.Ordinal);

        var runtime = rows.Single(l => l.StartsWith("| [ ] | 4. ", StringComparison.Ordinal));
        Assert.Contains("第 2 档", runtime, StringComparison.Ordinal);
        Assert.Contains("运行中改系统缩放", runtime, StringComparison.Ordinal);
        Assert.Contains("不重开窗口", runtime, StringComparison.Ordinal);
    }

    /// <summary>纯算式那一份必须真的被测试工程编进来，否则上面的真值表是对着一个不存在的类型跑。</summary>
    [Fact]
    public void ScaleFactor_IsSharedCompiledIntoTheTestProject() =>
        Assert.Contains(ScaleFile.Trim(), Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj"), StringComparison.Ordinal);

    /// <summary><c>DpiScale.Apply</c> 的方法体（从声明行到变更重算声明之前——重算那一段另切一份）。</summary>
    private static string ApplyBody()
    {
        var source = DpiScaleSource();
        var start = source.IndexOf("public static void Apply(", StringComparison.Ordinal);
        Assert.True(start >= 0, "找不到 DpiScale.Apply。");
        var end = source.IndexOf("internal static void RescaleOnDpiChanged(", start, StringComparison.Ordinal);
        Assert.True(end > start, "Apply 与变更重算之间切不出方法体。");
        return source[start..end];
    }

    /// <summary>
    /// DPI 变更重算那一段（从声明行到 <c>ScaleFactor</c> 之前）。
    /// 单独切一份的理由：接线判据只在有界区间上成立——整份文件判会把 Apply 里那一句
    /// <c>form.Scale(new SizeF(factor, factor))</c> 也判进来，「不许按目标因子缩放控件树」那条
    /// 反向断言就永远只能看着创建期那一行说话。
    /// </summary>
    private static string RescaleBody()
    {
        var source = DpiScaleSource();
        var start = source.IndexOf("internal static void RescaleOnDpiChanged(", StringComparison.Ordinal);
        Assert.True(start >= 0, "找不到 DpiScale 的 DPI 变更重算（RescaleOnDpiChanged）。");
        var end = source.IndexOf("public static float ScaleFactor", start, StringComparison.Ordinal);
        Assert.True(end > start, "变更重算与 ScaleFactor 之间切不出方法体。");
        return source[start..end];
    }

    private static string DpiScaleSource() => Repo.Source("Desktop", "Native", "DpiScale.cs");

    private static int IndexOfCode(string body, string code) => CodeScan.IndexOfLine(body, code);

    private static IEnumerable<string> CodeLines(string source) => CodeScan.Lines(source);
}
