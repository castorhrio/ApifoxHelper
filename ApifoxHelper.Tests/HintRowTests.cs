using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 两列字段网格里的提示行只经 <c>Ui.HintRow</c> 一处出厂（docs/DESIGN.md §6「提示行」，
/// 第二百零六轮，具名 pass：distill；出处：<c>docs/CRITIQUE.md</c> 第 1 站的「最该改的一处」）。
/// <para>
/// <b>补的是哪个缺口</b>：同一件事此前有四种写法——配置编辑对话框借 <c>AddRow(grid, "", Ui.Caption(…))</c>、
/// 「模块与模板 / 序列化与分析 / 模型服务」三个配置子对话框各带一份逐字相同的私有 <c>AddHint</c>、
/// 新建任务对话框的 <c>AddRow</c> 在空标签格里放一个占位控件。后两种放的是<b>默认尺寸的
/// <c>Panel</c>（200×100）</b>，于是每一行提示都被那个面板撑到 100px 高（三个子对话框 17 行提示、
/// 新建任务 5 行提示与离线模式那一行）。这个根因第一百四十八轮在配置页修过（<c>ProfileFormLayout.SpacerSize</c>
/// 记着理由），当时只改了那一个文件，同一族的三处没跟上——属同壳不同行为，也是「没有一处是选过的」那种脚手架感。
/// </para>
/// <para>
/// <b>本文件判什么</b>：① 工厂只交出形状（一句 Caption <b>跨两列</b>出厂、带上说明行标记、上下留白取令牌），不碰文字；
/// ② 二十六处提示行全部经工厂出厂，三份私有 <c>AddHint</c> 不再存在；③ 「空标签 + Caption」这条路不得复活；
/// ④ 行容器不得再往标签列塞占位控件；⑤ 二十六句提示文字逐字与本轮改动之前相同（distill 改的是形状不是话）；
/// ⑥ 设计基线记下这条口径；⑦ wine 侧那一段真实行高量测删不掉（长度与句数在 Linux 判得了，
/// 「这一行实际多高」只有真实控件答得出）；⑧ 那两句最长的说明<b>只有跨两列才同时放得下</b>——可用宽与需宽
/// 四个数都是 192 档那一跑的读数，标签列按 <c>ProfileGridColumns</c> 现算不写死（第三百四十九轮）；
/// ⑨ 说明行那一格上的标记只有工厂那一处会设，冒烟器只读它，旧的「按标签列有没有控件」判据不得复活；
/// ⑩ <b>逐句的归属对照表</b>（<c>docs/NOTES.md</c>「提示行 26 句的所有权归属」那一节）与源码里的工厂调用点、
/// 与 <see cref="CallSites"/> 那本登记、与 <c>docs/DESIGN.md</c> §6 登记的三个数（26 / 20 / 6）三方对账，
/// 每行「管的编辑位」按「归属」那一列在源码的行序列里真的取得到（第三百五十轮，简报 P1-1）；
/// ⑪ 上面那条抓不抓得住由注入当场验（删一行、改一个字、指错编辑位、换第六种归属写法、把 §6 那个数改小）。
/// </para>
/// <para>
/// <b>第三百四十九轮把形状从字段列改成跨两列，这一改的代价记在这里</b>：所有权通道从水平对齐换成竖向邻接——
/// 二十六句里有二十句此前靠缩进指向它上面那一个字段（逐处清点与为什么不采用「分区级跨、字段级留」那一种两形
/// 形状，都在 <c>docs/NOTES.md</c> 第三百四十九轮那一节），换来的是另外六句不再被缩进谎指最后一个字段、
/// 以及 200% 档那两条超预算的说明行归零。
/// </para>
/// <para>
/// <b>第三百五十二轮把所有权从「竖向邻接」换成「形态枚举」</b>（简报 P0-1）：竖向邻接对字段注脚够用，
/// 对分区注脚不够——那六句一次管两三个编辑位或整个分区，跨两列之后与字段注脚长得一模一样。
/// 现由调用处必须写下来的第三个实参 <c>HintRowForm</c> 承载，两种形态在同一屏上有可量化的差异
/// （字段级 <c>Padding.Left = 0</c>、分区级 = 2px 竖线 + 4px 让位 = 6px，几何在 <c>HintRowForms</c> 一处）。
/// 本文件因此多判三件事：⑫ 二十六处调用点<b>都写了</b>形态（工厂没有缺省值，少写一处编译不过、
/// 这里再判一次源码）；⑬ 两种形态的 <c>Padding.Left</c> <b>不相等</b>（判据是算式不是恒等式，
/// 且分区级那个让位与令牌源 <c>UiTokens.Xs</c> 对账）；⑭ 逐句归属表多一列「形态」，
/// 与源码第三实参、与「归属」那一列（字段级 → Field、多字段与分区级 → Section）三方对账。
/// </para>
/// </summary>
public class HintRowTests
{
    private static string UiSource => Repo.Source("Desktop", "Native", "Ui.cs");

    private static string NativeFile(string name) => Repo.Source("Desktop", "Native", name);

    private static string SmokeSource => Repo.Source("ApifoxHelper.Smoke", "Program.cs");

    /// <summary>本轮从四种写法收成一处工厂的五个文件，值 = 该文件里的提示行条数。</summary>
    private static readonly (string File, int Calls)[] CallSites =
    [
        ("ProjectsPage.cs", 4),
        ("TasksPage.cs", 5),
        ("ProfileAdvancedDialog.cs", 6),
        ("ProfileAnalysisDialog.cs", 8),
        ("GenerationDialog.cs", 3),
    ];

    /// <summary>就地字面量的提示文字（二十七句里的二十二句），逐句与本轮改动之前相同。</summary>
    private static readonly string[] LiteralHints =
    [
        // 配置编辑对话框
        "相对路径前缀，用「;」分隔；留空表示只允许导出补丁。",
        "提交号、构建号或版本描述；为空时报告显示「未绑定代码版本」。",
        "分支留空表示主分支。",
        // 新建任务对话框
        "留空则用「配置名 月-日 时:分」。",
        "勾选要检查的模块；都不勾选则检查该 API 项目的全部接口。",
        "补充接口路径前缀，用「,」「;」分隔；可与模块并用。",
        // 模块与模板
        "每行：模块名=路径前缀 1、路径前缀 2，例如 财务=/api/finance/；新建任务时可按模块选择范围。",
        "新建接口创建时的目录与公共设置；只随创建写入这些字段，不克隆脚本、用例或 Mock。",
        "留空表示未指定目录；未指定目录时新接口无法创建。",
        "用「,」分隔。",
        "JSON 对象或数组；留空表示不设置。",
        "填写接口 ID 后提取其目录、状态、服务、鉴权、安全方案、公共 Header 与标签填入上方字段；请检查后再保存。",
        // 序列化与分析
        "与 Swashbuckle SupportNonNullableReferenceTypes 一致。",
        "每行：类型名=数据属性名，例如 PNOSActionResult=Data；用于追踪 data 的实际类型（响应契约）。",
        "留空按目标框架与配置推导（DEBUG;TRACE;NET8_0…）。",
        "数字留空用默认值。",
        "可选：项目名=路径，每行一个；默认按 bin/配置/目标框架 推断。",
        "与 MvcOptions.SuppressAsyncSuffixInActionNames 一致（默认开启）。",
        "只读详情的并发数（1–8）；写入始终串行。",
        "可选；为空时自动查找 PATH 与全局 npm 目录。",
        // 模型服务
        "例如 https://api.openai.com/v1；只发送用户确认的材料。",
        "每次请求最多包含的注释位置数；数字留空用默认值。",
    ];

    /// <summary>工厂只交出形状：加一行、AutoSize、Caption 跨两列出厂、带上标记、上下留白取令牌——不碰文字、不放占位控件。</summary>
    [Fact]
    public void HintRow_IsTheOnlyFactoryAndCarriesNoSpacer()
    {
        // 第三百五十二轮：第三个实参是形态，且<b>没有缺省值</b>——缺省值等于「没人选过」，
        // 下一句提示行加进来会静默变成字段注脚，而它可能讲的是整个分区。
        Assert.Single(Regex.Matches(UiSource, @"static Label HintRow\(TableLayoutPanel grid, string text, HintRowForm form\)"));
        Assert.DoesNotContain("HintRowForm form =", UiSource, StringComparison.Ordinal);
        var body = FactoryBody();
        Assert.Contains("grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));", body, StringComparison.Ordinal);
        Assert.Contains("var hint = HintRowForms.DrawsRule(form) ? SectionHint(text) : Ui.Caption(text);", body, StringComparison.Ordinal);
        // 两种形态的差异只在这一处落地，且数从 HintRowForms 取——工厂里不写死 6 或 2。
        // 第三百五十四轮：让位按当前缩放档算（ScaledLeftIndent），不是把设计档那个数直接写进 Padding。
        Assert.Contains("hint.Padding = new Padding(HintRowForms.ScaledLeftIndent(form, DpiScale.ScaleFactor()), 0, 0, 0);", body, StringComparison.Ordinal);
        // 第三百四十九轮：说明行不是字段，它要的宽度是整张网格的宽度——从字段列那一格改成跨两列。
        // 判据与逐处代价清点在 docs/NOTES.md 第三百四十九轮那一节，可用宽那笔账见下面
        // HintRow_FitsBothLongTextsOnlyWhenItSpansBothColumns。
        Assert.Contains("grid.Controls.Add(hint, 0, row);", body, StringComparison.Ordinal);
        Assert.Contains("grid.SetColumnSpan(hint, 2);", body, StringComparison.Ordinal);
        Assert.Contains("hint.Tag = HintRowMarker;", body, StringComparison.Ordinal);
        Assert.Contains("hint.Margin = new Padding(0, UiTokens.Xs, 0, UiTokens.Xs);", body, StringComparison.Ordinal);
        // 退回「只放字段列」即红：那一格在 200% 档只有 1078px，两句说明因此折成两行、超出行高预算。
        Assert.False(body.Contains("Add(hint, 1, row)", StringComparison.Ordinal),
            "Ui.HintRow 又把说明行放回字段列那一格——200% 档那两句（需宽 1220 与 1113 对可用 1078）会重新折成两行。");
        // 占位控件不得回来：默认尺寸的 Panel（200×100）会把这一行撑到 100px 高。
        Assert.False(body.Contains("new Panel", StringComparison.Ordinal),
            "Ui.HintRow 又往那一格放了占位控件——提示行的死空白正是这么来的（ProfileFormLayout 记着同一根因）。");
        // 文字是调用方交进来的：工厂不加工文字，否则各屏那句提示会被悄悄改写。
        Assert.False(Regex.IsMatch(body, @"Trim\(|Replace\(|Substring|\+ """),
            "Ui.HintRow 改写了传入的提示文字——这一处只管形状，文字口径在各屏自己那一份。");
    }

    /// <summary>
    /// 200% 档那两句最长的说明，<b>只有跨两列才同时放得下</b>（第三百四十九轮，简报 P0-1 的交付判据）。
    /// <para>
    /// <b>四个数都是读数，不是这里定义的</b>：字段列可用宽 1078px 与跨两列 1318px 出自第三百四十八轮
    /// 「重启前缀 + 单档跑」那一跑 192 档的失败消息原文（屏 1600×1000），需宽 1220 与 1113 是同一条消息里
    /// 那两行自己念出来的（出处 `.winecheck/README.md`「Xvfb 屏尺寸」那一条与 `docs/NOTES.md` 第三百四十八轮
    /// 那一节）。标签列那一格<b>不写死</b>：按 <c>ProfileGridColumns.ScaledLabelWidth</c> 现算，
    /// 跨两列的可用宽 = 字段列 + 标签列（两列之间没有额外的间距属性，间隙来自控件留白——与冒烟器
    /// <c>TallestInRow</c> 取跨列控件可用宽同一算法）。所以这一条是<b>对账门</b>：算式在产品那一头、
    /// 数在报告那一头，两头任一头变了它都红。
    /// </para>
    /// </summary>
    [Fact]
    public void HintRow_FitsBothLongTextsOnlyWhenItSpansBothColumns()
    {
        const int FieldColumnAt192 = 1078;
        const int SpanningAt192 = 1318;
        var longest = new[] { 1220, 1113 };

        var labelColumn = ApifoxHelper.Desktop.Native.ProfileGridColumns.ScaledLabelWidth(
            ApifoxHelper.Desktop.Native.ProfileGridColumns.LabelWidth, 2f);
        Assert.True(FieldColumnAt192 + labelColumn == SpanningAt192,
            $"按口径现算的跨两列可用宽是 {FieldColumnAt192 + labelColumn}px（字段列 {FieldColumnAt192} + 标签列 {labelColumn}），"
            + $"而 192 档那一跑念到的是 {SpanningAt192}px：列宽口径与读数不是同一件事了，先回报告看清是哪一个变了，"
            + "别把这里的数改大了事。");

        Assert.True(longest.All(w => w <= SpanningAt192),
            $"跨两列也放不下那两句说明（需宽 {string.Join(" 与 ", longest)} 对可用 {SpanningAt192}px）："
            + "形状改了不等于问题解了——这一片要的正是 192 档 hint 归零。");

        // 两句都在字段列那一格之外：这就是「分区级跨两列、字段级留在字段列」那一种两形形状救不了这一族的原因
        // （需宽 1113 那一句在冻结基线里恰恰是挂在「通用响应封装」那一格上的字段级说明）。
        Assert.True(longest.All(w => w > FieldColumnAt192),
            $"那两句如今在字段列那一格里也都放得下了（最宽 {longest.Max()} 对 {FieldColumnAt192}）："
            + "上面那条「必须跨两列」的读数已经过期，这一片的成因不再是可用宽——先重量，再决定这条门留不留。");
    }

    /// <summary>
    /// 两种形态在同一屏上<b>有可量化的差异</b>（第三百五十二轮，简报 P0-1 的成功判据）。
    /// <para>
    /// 判据是「两种形态的 <c>Padding.Left</c> 不相等」——不是恒等式，是<b>算式</b>：字段级 0、
    /// 分区级 = 竖线宽 + 让位。两个数都从 <c>HintRowForms</c> 现读，产品与门读的是同一个数；
    /// 让位那一档再与令牌源 <c>UiTokens.Xs</c> 对账（D1：界面不得就地发明间距值）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判真机上 6px 够不够一眼可辨——那是观感，随 G-D5 复核；也不判那条竖线的颜色
    /// （取 <c>UiTokens.Line</c>，灰阶里已有的发丝边色，不新造颜色）。
    /// </para>
    /// </summary>
    [Fact]
    public void HintRowForms_DifferVisiblyOnTheSameScreen()
    {
        var field = ApifoxHelper.Desktop.Native.HintRowForms.LeftIndent(ApifoxHelper.Desktop.Native.HintRowForm.Field);
        var section = ApifoxHelper.Desktop.Native.HintRowForms.LeftIndent(ApifoxHelper.Desktop.Native.HintRowForm.Section);

        Assert.Equal(0, field);
        Assert.NotEqual(field, section);
        Assert.Equal(
            ApifoxHelper.Desktop.Native.HintRowForms.RuleWidth + ApifoxHelper.Desktop.Native.HintRowForms.TextGap,
            section);

        Assert.False(ApifoxHelper.Desktop.Native.HintRowForms.DrawsRule(ApifoxHelper.Desktop.Native.HintRowForm.Field));
        Assert.True(ApifoxHelper.Desktop.Native.HintRowForms.DrawsRule(ApifoxHelper.Desktop.Native.HintRowForm.Section));

        // 让位那一档取令牌，不就地发明：UiTokens.Xs 的值从令牌源现读
        // （本工程不编译 UiTokens——它依赖 System.Drawing，Linux 上构造 FontFamily 会抛）。
        var tokens = Repo.Source("Desktop", "Native", "UiTokens.cs");
        var xs = Regex.Match(tokens, @"public const int Xs = (\d+);");
        Assert.True(xs.Success, "UiTokens.cs 里读不到 Xs 那一档：让位与令牌源的对账退化了，这条门会在空对空上绿。");
        Assert.Equal(ApifoxHelper.Desktop.Native.HintRowForms.TextGap, int.Parse(xs.Groups[1].Value));
    }

    /// <summary>
    /// 分区注脚那条竖线的宽度<b>在设计基线里登记过</b>，且登记的位置不是间距五档（第三百五十三轮，简报 P0-2）。
    /// <para>
    /// <b>补的是哪个缺口</b>：第三百五十二轮把 <c>RuleWidth = 2</c> 写成 <c>HintRowForms</c> 里的一个就地常量，
    /// 而 <c>docs/DESIGN.md</c> §4 是<b>封闭五档</b>（4 / 8 / 12 / 16 / 24，4px 基准）、§5 的发丝边是 <b>1px</b>——
    /// 2px 在这两份表里都没有落点。设计基线开头那句「组件内不得出现档外数值」因此在这一处不成立，
    /// 而它不成立的方式是<b>静默的</b>：没有任何一条门会红，下一个人读 §4 也读不出这个 2 是从哪来的。
    /// </para>
    /// <para>
    /// <b>为什么登记在 §5 而不是 §4</b>：§4 那五档是<b>留白</b>，这条 2px 是<b>描边</b>——把它塞进 §4 会让
    /// 「4px 基准」这句话当场不成立，而 <c>TokenWiringTests.SpacingTiers_MatchDesignDoc</c> 按 §4 那张表
    /// 逐行解析（`` `名` | 数 | ``），塞进去还会要求 <c>UiTokens</c> 里多一个间距字段。§5 是「形状与描边」，
    /// 1px 发丝边本来就在那里，2px 内容分隔线与它并列才是同一件事的两种宽度。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判 2px 在真机上够不够一眼可辨——那是观感，随 G-D5 复核；也不判 §5 那张表的
    /// 其余行（圆角两行各有各的出处）。
    /// </para>
    /// </summary>
    [Fact]
    public void SectionRuleWidth_IsRegisteredInTheShapeSection_AndNotAsASpacingTier()
    {
        var shape = Repo.DesignSection(5);
        var rule = Regex.Match(shape, @"\|\s*内容分隔线\s*\|\s*(\d+)px\s*`Line`\s*\|");
        Assert.True(rule.Success,
            "DESIGN.md §5 里读不到「内容分隔线 | Npx `Line`」那一行：分区注脚那条竖线的宽度没有出处，"
            + "而 §4 是封闭五档、§5 的发丝边是 1px——2px 在这两份表里都没有落点。");
        Assert.Equal(ApifoxHelper.Desktop.Native.HintRowForms.RuleWidth, int.Parse(rule.Groups[1].Value));

        // 发丝边仍是 1px：这条 2px 不是把 §5 那一行改宽了，是另立一行。
        var hairline = Regex.Match(shape, @"\|\s*边框\s*\|\s*(\d+)px\s*`Line`\s*\|");
        Assert.True(hairline.Success, "DESIGN.md §5 里读不到「边框 | Npx `Line`」那一行：发丝边那一档没有出处了。");
        Assert.Equal(1, int.Parse(hairline.Groups[1].Value));
        Assert.NotEqual(ApifoxHelper.Desktop.Native.HintRowForms.RuleWidth, int.Parse(hairline.Groups[1].Value));

        // 它也不许混进 §4 的间距五档：那五档是留白，这条是描边。
        var spacing = Regex.Matches(Repo.DesignSection(4), @"`(\w+)`\s*\|\s*(\d+)\s*\|")
            .Select(m => int.Parse(m.Groups[2].Value)).ToHashSet();
        Assert.DoesNotContain(ApifoxHelper.Desktop.Native.HintRowForms.RuleWidth, spacing);
        Assert.Contains(ApifoxHelper.Desktop.Native.HintRowForms.TextGap, spacing);
    }

    /// <summary>
    /// 三档（96 / 125% / 200%）的让位：<c>RuleWidthAt</c> 的入参就是这三个数。
    /// <para>
    /// <b>为什么写成一处</b>（第三百五十四轮，简报 P0-2）：这三个数此前散在断言里当字面量，
    /// 而它们其实是<b>设计档让位 × 三档因子</b>算出来的——写成一处之后，下面那条
    /// <see cref="TierIndents_AreTheDesignIndentScaledByTheThreeTierFactors"/> 能把「这三个数从哪来」
    /// 与「框架实际给的是不是这三个」分开判：前者在 Linux 上算，后者只有 wine 三档量得到。
    /// </para>
    /// </summary>
    private static readonly int[] TierIndents = [6, 8, 12];

    /// <summary>
    /// 三档让位<b>不是手写的三个数</b>：它们是设计档让位 × 三档因子（1 / 1.25 / 2），
    /// 由出厂那一处（<c>HintRowForms.ScaledLeftIndent</c>）算出来（第三百五十四轮，简报 P0-2）。
    /// <para>
    /// <b>补的是哪个缺口</b>：<c>RuleWidthAt</c> 的算式前提是「控件上的让位是 6 / 8 / 12」，
    /// 而这三个数此前只以字面量出现在断言里——算式改了、设计档让位改了，那三个字面量不会跟着动，
    /// 于是「算式前提」与「断言里的数」可以各说一套而全绿。这一条把三个数接回设计档与三档因子，
    /// 且<b>走的是产品出厂时用的同一个函数</b>：断言里的数与控件上那个数因此不可能各算一套。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判控件上真的是不是这三个数——那要跑 wine 三档（冒烟器那一段，见
    /// <see cref="Smoke_ReconcilesTheFrameworkIndentWithTheInputsRuleWidthAtIsPinnedTo"/>）。
    /// 第三百五十四轮第一次跑那一段时，控件上恒为 6px：这一条当时是绿的，红的是那一段——
    /// 「算式对」与「控件上对」是两件事，两条门各判一件。
    /// </para>
    /// </summary>
    [Fact]
    public void TierIndents_AreTheDesignIndentScaledByTheThreeTierFactors()
    {
        var factors = new[] { 1f, 1.25f, 2f };
        var derived = factors
            .Select(f => ApifoxHelper.Desktop.Native.HintRowForms.ScaledLeftIndent(
                ApifoxHelper.Desktop.Native.HintRowForm.Section, f))
            .ToArray();
        Assert.Equal(TierIndents, derived);
        // 三档必须互不相等：相等就等于「缩放档对让位没有影响」，那 RuleWidthAt 的入参也就没有三档可言。
        Assert.Equal(TierIndents.Length, TierIndents.Distinct().Count());
        // 字段级那一形三档都是 0：让位放大只对分区注脚成立，字段注脚不许凭空长出一条缩进。
        foreach (var factor in factors)
        {
            Assert.Equal(0, ApifoxHelper.Desktop.Native.HintRowForms.ScaledLeftIndent(
                ApifoxHelper.Desktop.Native.HintRowForm.Field, factor));
        }
    }

    /// <summary>
    /// 出厂那一处<b>真的按缩放档算让位</b>，而不是把设计档那个数直接写进 <c>Padding.Left</c>
    /// （第三百五十四轮，简报 P0-2）。
    /// <para>
    /// <b>补的是哪个缺口</b>：三档复跑量到 <c>Padding.Left</c> 恒为 6px。根因不是算式，是<b>接线</b>——
    /// 四个子对话框「先 <c>DpiScale.Apply</c>、后加控件」，说明行在 <c>Form.Scale</c> 那一刻还不存在，
    /// 框架一次都没碰过它。所以让位必须在出厂处自己按因子算；这一条判的就是那一句接线在不在。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判算出来的数对不对（上一条判）、也不判控件上真的是不是那个数（冒烟器判）。
    /// </para>
    /// </summary>
    [Fact]
    public void HintRowFactory_ScalesTheIndentByTheSameFactorAsTheLabelColumn()
    {
        // ① 出厂处按因子算：函数与因子都从产品现读，不在这里写死 6 / 8 / 12。
        Assert.Contains(
            "hint.Padding = new Padding(HintRowForms.ScaledLeftIndent(form, DpiScale.ScaleFactor()), 0, 0, 0);",
            UiSource,
            StringComparison.Ordinal);
        // ② 设计档那个数不得再直接进 Padding：写死它就等于「缩放档对让位没有影响」（三档实测 6 / 6 / 6）。
        Assert.DoesNotContain(
            "hint.Padding = new Padding(HintRowForms.LeftIndent(form), 0, 0, 0);",
            UiSource,
            StringComparison.Ordinal);
        // ③ 因子只有一处出处：与左列宽用的是同一个数（ProfileGridColumns.ScaledLabelWidth 的入参）。
        Assert.Contains("ProfileGridColumns.ScaledLabelWidth(_labelWidth, DpiScale.ScaleFactor())", UiSource, StringComparison.Ordinal);
        // ④ 取整不另写一份：ScaledLeftIndent 走 DpiScaleFactor.ScaledExtent。
        var forms = NativeFile("HintRowForm.cs");
        Assert.Contains("DpiScaleFactor.ScaledExtent(LeftIndent(form), factor)", forms, StringComparison.Ordinal);
    }

    /// <summary>
    /// 冒烟器在三档里把<b>框架实际给出的让位</b>念出来，并与 <c>RuleWidthAt</c> 的期望输入对账
    /// （第三百五十四轮，简报 P0-2）。
    /// <para>
    /// <b>补的是哪个缺口</b>：第三百五十三轮那条比例恒等式只在 Linux 算式上成立——「控件上的让位是
    /// 6 / 8 / 12」这个前提<b>一次都没有在真实控件上量过</b>。第三百五十四轮第一次跑就红了：
    /// 三档实测恒为 <b>6px</b>（说明行在 <c>Form.Scale</c> 那一刻还不存在），修法见
    /// <see cref="HintRowFactory_ScalesTheIndentByTheSameFactorAsTheLabelColumn"/>。
    /// </para>
    /// <para>
    /// <b>这一条判什么</b>：冒烟器那一段真的读 <c>Padding.Left</c>、期望值从 <c>HintRowForms</c> 现读、
    /// 因子从 <c>DpiScale</c> 现读、对不上就当场失败。**它判不到「量到的数是多少」**——那要跑 wine 三档，
    /// 读数记在 <c>docs/NOTES.md</c> 第三百五十四轮那一节与 <c>.winecheck/reading.txt</c> 的三档读数里。
    /// </para>
    /// </summary>
    [Fact]
    public void Smoke_ReconcilesTheFrameworkIndentWithTheInputsRuleWidthAtIsPinnedTo()
    {
        // ① 真的读框架给出的那个数（不是只念行高）。
        Assert.Contains("hint.Padding.Left", SmokeSource, StringComparison.Ordinal);
        // ② 期望值从产品几何现读、按框架自己的缩放因子算——冒烟器里不写死 6 / 8 / 12。
        Assert.Contains("var factor = DpiScale.ScaleFactor();", SmokeSource, StringComparison.Ordinal);
        Assert.Contains(
            "var expected = HintRowForms.ScaledLeftIndent(HintRowForm.Section, factor);",
            SmokeSource,
            StringComparison.Ordinal);
        // ③ 对不上就当场失败（不是只印一行读数）。
        Assert.Contains("if (sectionIndents.Any(v => v != expected))", SmokeSource, StringComparison.Ordinal);
        // ④ 两种形态都要量到：字段级那一形必须是 0，分区级那一形必须至少有一条。
        Assert.Contains("if (fieldIndents.Any(v => v != 0))", SmokeSource, StringComparison.Ordinal);
        Assert.Contains("if (sectionIndents.Count == 0)", SmokeSource, StringComparison.Ordinal);
        // ⑤ 认提示行仍只有一处：拆出来的取控件方法与判据是同一个出口，不许长出第二种认法。
        Assert.Single(Regex.Matches(SmokeSource, @"private static Label\? HintRowControl\(TableLayoutPanel grid, int row\)").Cast<Match>());
    }

    /// <summary>
    /// 那条竖线的宽度<b>跟着缩放档走</b>，且三档都是同一个比例（第三百五十三轮）。
    /// <para>
    /// <b>补的是哪个缺口</b>：让位随缩放档放大（出厂时按因子算，第三百五十四轮），而竖线是
    /// <c>OnPaint</c> 里画出来的，框架不会替它放大。写死 <c>RuleWidth</c> 的话，200% 档上竖线仍是 2px、
    /// 让位已经 12px——「一条线 + 一段让位」被拉成 1 : 6，与 96 档归档的 1 : 2 不是同一个形状。
    /// 这条门判的就是「三档落在同一个比例附近」。
    /// </para>
    /// <para>
    /// <b>入参是让位而不是缩放因子</b>：让位是控件上此刻真的那个数（含当前缩放档、也含运行中
    /// <c>WM_DPICHANGED</c> 那一次重算），再取一次屏幕 DPI 等于把同一件事算两遍。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判真机上那条线画出来是什么样（本环境没有 Windows GUI 运行时，
    /// <c>OnPaint</c> 一次都跑不到）——这里判的是<b>算式</b>，观感随 G-D5 复核。
    /// </para>
    /// </summary>
    [Fact]
    public void RuleWidthAt_FollowsTheScaledIndent_AndKeepsTheOneToTwoRatio()
    {
        var rule = ApifoxHelper.Desktop.Native.HintRowForms.RuleWidth;
        var gap = ApifoxHelper.Desktop.Native.HintRowForms.TextGap;
        var total = rule + gap;

        // ① 96 档那一格是恒等式：设计档的让位反算回来必须还是设计档的线宽。
        var designIndent = ApifoxHelper.Desktop.Native.HintRowForms.LeftIndent(
            ApifoxHelper.Desktop.Native.HintRowForm.Section);
        Assert.Equal(rule, ApifoxHelper.Desktop.Native.HintRowForms.RuleWidthAt(designIndent));

        // ② 三档的让位（96 / 125% / 200%）逐个钉死：线宽 2 / 3 / 4。
        //    这三个数不是手写的：它们是设计档让位 × 三档因子（1 / 1.25 / 2）四舍五入，
        //    由 TierIndents_AreTheDesignIndentScaledByTheThreeTierFactors 现算对账；
        //    框架实际给出的那三个数由冒烟器在三档里量（第三百五十四轮，简报 P0-2）。
        Assert.Equal(2, ApifoxHelper.Desktop.Native.HintRowForms.RuleWidthAt(TierIndents[0]));
        Assert.Equal(3, ApifoxHelper.Desktop.Native.HintRowForms.RuleWidthAt(TierIndents[1]));
        Assert.Equal(4, ApifoxHelper.Desktop.Native.HintRowForms.RuleWidthAt(TierIndents[2]));

        // ③ 比例：线宽 / 让位 ≈ 线宽档 / 让位档，即 width × total ≈ indent × rule，
        //    误差不超过半个让位档（四舍五入的界）。让位 1px 那一格不在此列：理想线宽 0.33px
        //    被下限抬到 1px，那是「画得出来」的取舍，不是比例漂了。
        foreach (var indent in new[] { 2, 3, 4, 6, 7, 8, 9, 12, 16, 24, 48 })
        {
            var width = ApifoxHelper.Desktop.Native.HintRowForms.RuleWidthAt(indent);
            Assert.True(width >= 1, $"让位 {indent}px 反算出线宽 {width}px：画出来的线宽不得小于 1px。");
            Assert.True(Math.Abs(width * total - indent * rule) <= total / 2,
                $"让位 {indent}px 反算出线宽 {width}px，比例是 1 : {(double)indent / width:0.##}，"
                + $"而设计档是 1 : {(double)gap / rule:0.##}——线宽没有跟着让位走。");
        }

        // ④ 单调不减：让位变大，线宽不许变小（否则 200% 档会比 96 档还细）。
        var previous = 0;
        for (var indent = 1; indent <= 48; indent++)
        {
            var width = ApifoxHelper.Desktop.Native.HintRowForms.RuleWidthAt(indent);
            Assert.True(width >= previous, $"让位 {indent}px 的线宽 {width}px 比让位 {indent - 1}px 的 {previous}px 还细。");
            previous = width;
        }

        // ⑤ 让位为 0（字段级那一形，或有人把让位改没了）答 0：不画线，也不猜一个最小宽度。
        Assert.Equal(0, ApifoxHelper.Desktop.Native.HintRowForms.RuleWidthAt(0));
        Assert.Equal(0, ApifoxHelper.Desktop.Native.HintRowForms.RuleWidthAt(-6));
    }

    /// <summary>
    /// 二十六处调用点<b>都写了</b>形态，且分区注脚恰好是 §6 登记的那六句（第三百五十二轮）。
    /// <para>
    /// 工厂没有缺省值（上面那条门判着），所以「少写一处」在编译期就红；这一条判的是<b>源码里真的写着</b>——
    /// 有人给工厂加回一个 <c>= HintRowForm.Field</c> 的缺省值时，编译照样过，而下一句讲整个分区的说明行
    /// 会静默变成字段注脚。总数与分区数都不在这里写死：总数取 <see cref="CallSites"/> 那本登记，
    /// 分区数取 <c>docs/DESIGN.md</c> §6 那一格登记的「另外 N 句」。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryCallSite_DeclaresItsForm_AndTheSectionOnesAreTheSix()
    {
        var problems = new List<string>();
        var total = 0;
        var section = 0;
        foreach (var source in Repo.NativeSources())
        {
            foreach (Match match in Regex.Matches(source.Text, @"(?m)^\s*Ui\.HintRow\(grid, .*\);$"))
            {
                total++;
                var form = Regex.Match(match.Value, @"HintRowForm\.(Field|Section)\);$");
                if (!form.Success)
                {
                    problems.Add($"{source.Name}：{match.Value.Trim()} 没写形态——工厂没有缺省值，这一处要么编译不过，要么有人给它加了缺省值");
                    continue;
                }

                if (form.Groups[1].Value == "Section") section++;
            }
        }

        Assert.True(problems.Count == 0, "提示行的形态没有逐处写下来：" + string.Join("；", problems) + "。");
        Assert.Equal(CallSites.Sum(s => s.Calls), total);

        var rest = Regex.Match(Repo.DesignSection(6), @"另外 (\d+) 句");
        Assert.True(rest.Success, "DESIGN.md §6「提示行」那一格里读不到「另外 N 句」：分区注脚那个数没有出处了。");
        Assert.Equal(int.Parse(rest.Groups[1].Value), section);
    }

    /// <summary>
    /// 说明行的标记<b>只有工厂那一处会设</b>，冒烟器只读不写；旧的「按标签列有没有控件」判据不得复活。
    /// <para>
    /// 为什么这条要单独判：跨两列之后说明行与分区标题同列、同跨度、同是 Label，唯一能分开它们的就是这一格上的
    /// 标记。冒烟器 ⑧ 那一段「没有一条提示行」是硬失败，可要是判据认错了对象——把分区标题当成提示行——它不会说
    /// 「认不出」，它会照量照报，只是量的是另一族行。
    /// </para>
    /// </summary>
    [Fact]
    public void HintRowMarker_IsWrittenByTheFactoryAloneAndOnlyReadByTheSmoke()
    {
        var writers = Repo.NativeSources()
            .SelectMany(s => Regex.Matches(s.Text, @"\.Tag\s*=\s*HintRowMarker").Cast<Match>().Select(_ => s.Name))
            .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(["Ui.cs"], writers);

        Assert.Single(Regex.Matches(SmokeSource, @"Tag as string\) == Ui\.HintRowMarker").Cast<Match>());
        Assert.False(SmokeSource.Contains(".Tag = Ui.HintRowMarker", StringComparison.Ordinal),
            "冒烟器自己给一格写上说明行标记——那一段量的就是它自己造出来的行，不再是产品出厂的那二十六处。");
        Assert.False(SmokeSource.Contains("if (grid.GetColumn(control) == 0) return false;", StringComparison.Ordinal),
            "旧判据回来了：跨两列之后说明行就在列 0，那一句会把每一行的分区标题都算成提示行。");
    }

    /// <summary>
    /// 三份私有 <c>AddHint</c> 不得复活——这一条只管「是否经工厂出厂」，<b>不看条数</b>。
    /// 第二百零七轮（简报 P1-1）把它与下面的登记比对分开：写在一起时，
    /// 下一轮<b>正经新增一屏提示行</b>会被报成「有的地方退回就地拼」，那是把人对着错的方向改代码。
    /// </summary>
    [Fact]
    public void EveryHintRow_GoesThroughTheFactory()
    {
        foreach (var source in Repo.NativeSources())
        {
            Assert.False(Regex.IsMatch(source.Text, @"(?m)^\s*private void AddHint\("),
                $"{source.Name} 又写回一份私有 AddHint——同一件事四种写法就是从这里长回来的。");
        }
    }

    /// <summary>
    /// 工厂调用点的<b>登记</b>要与源码对得上，且对不上时分方向点名（第二百零七轮，简报 P1-1）。
    /// <para>
    /// 原来这里写死 <c>Assert.Equal(26, total)</c> 加逐文件条数：新增一屏提示行时它一定红，
    /// 而红话说的是「退回就地拼形状」——一句把下一位引向错方向的诊断。现在拆成三种情况各自说：
    /// 多了（该登记一处）、少了（该查是不是退回了就地拼）、以及**整个文件没登记 / 登记了一个没有工厂调用的文件**。
    /// 登记本身就是那份名单的权威：<b>总数不再单独写死一份</b>（26 这个数只从登记表算出来）。
    /// </para>
    /// </summary>
    [Fact]
    public void CallSites_RegistryMatchesTheSource_AndSaysWhichDirection()
    {
        var discovered = Repo.NativeSources()
            .Select(s => (Name: s.Name, Calls: Regex.Matches(s.Text, @"(?m)^\s*Ui\.HintRow\(grid, ").Count))
            .Where(s => s.Calls > 0)
            .ToDictionary(s => s.Name, s => s.Calls, StringComparer.Ordinal);
        var registered = CallSites.ToDictionary(s => s.File, s => s.Calls, StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var (file, calls) in registered)
        {
            var found = discovered.TryGetValue(file, out var n) ? n : 0;
            if (found == calls) continue;
            problems.Add(found > calls
                ? $"{file} 有 {found} 处经 Ui.HintRow 出厂，登记表 {nameof(CallSites)} 只记了 {calls} 处——新增的这一处确实经工厂出厂的话，把登记改到 {found}（这条门不许把工厂调用删掉来变绿）"
                : $"{file} 只剩 {found} 处经 Ui.HintRow 出厂，登记的是 {calls} 处——少的那几处要么退回了就地拼形状，要么这一屏的提示行被删了而登记没跟上");
        }

        foreach (var file in discovered.Keys.Except(registered.Keys, StringComparer.Ordinal))
        {
            problems.Add($"{file} 有 {discovered[file]} 处 Ui.HintRow 却没在 {nameof(CallSites)} 里登记——提示行的口径没进这本账，下一轮就没人知道它是第几族的写法");
        }

        foreach (var file in registered.Keys.Except(discovered.Keys, StringComparer.Ordinal))
        {
            problems.Add($"{nameof(CallSites)} 登记了 {file}，可它现在已经没有一处 Ui.HintRow——登记指向一屏不再存在的写法，这本账就空转了");
        }

        Assert.True(problems.Count == 0,
            "提示行的工厂调用点登记与源码对不上：" + string.Join("；", problems) + "。");
    }

    /// <summary>
    /// 「空标签 + Caption」这种借字段行放提示的写法不得复活（配置页此前的写法）。
    /// 离线模式那一行是勾选框、不是提示行，允许走 <c>AddRow(grid, "", _offline)</c>。
    /// </summary>
    [Fact]
    public void HintRows_AreNotSneakedInAsEmptyLabelFieldRows()
    {
        foreach (var source in Repo.NativeSources())
        {
            foreach (Match match in Regex.Matches(source.Text, @"(?m)^\s*AddRow\([^,]+, ""\s*"", Ui\.Caption\("))
            {
                Assert.Fail($"{source.Name}:提示行又借字段行出厂（{match.Value.Trim()}）——一律走 Ui.HintRow（DESIGN.md §6「提示行」）。");
            }
        }
    }

    /// <summary>
    /// 行容器不得再往标签列塞占位控件。配置页那份由 <c>ProfileFormLayoutTests.AddRow_DoesNotAddDefaultSizedSpacer</c>
    /// 钉着，这里补新建任务对话框那一份（它此前放的就是默认尺寸 200×100 的 Panel，离线模式那一行被撑高）。
    /// </summary>
    [Fact]
    public void RowContainers_NeverAddASpacerToTheLabelColumn()
    {
        var body = MethodBody(NativeFile("TasksPage.cs"), "private void AddRow(TableLayoutPanel grid, string label, Control field)");
        Assert.False(body.Contains("new Panel", StringComparison.Ordinal),
            "新建任务对话框的 AddRow 又往空标签格放占位 Panel——那一行会被撑到 100px 高（同一根因见 ProfileFormLayout）。");
        Assert.Contains("if (label.Length > 0)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 二十六句提示文字逐字与本轮改动之前相同：distill 收的是形状，不是话。
    /// 字面量按集合比（多一句、少一句、改一个字都红）；取共享常量的那五处按调用点比。
    /// </summary>
    [Fact]
    public void TheHintTexts_AreWordForWordUnchanged()
    {
        var all = string.Join("\n", CallSites.Select(s => NativeFile(s.File)));
        var literals = Regex.Matches(all, @"Ui\.HintRow\(grid, ""([^""]*)"", HintRowForm\.(?:Field|Section)\)").Cast<Match>()
            .Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(LiteralHints.Length, literals.Count);
        Assert.Equal(LiteralHints.OrderBy(t => t, StringComparer.Ordinal), literals.OrderBy(t => t, StringComparer.Ordinal));

        // 取共享口径的那五处：文字来自一份常量，界面不另抄一遍。
        foreach (var (file, call) in new[]
        {
            ("TasksPage.cs", "Ui.HintRow(grid, TaskCreation.KeysHint, HintRowForm.Field);"),
            ("TasksPage.cs", "Ui.HintRow(grid, TaskCreation.NoteFallbackHint, HintRowForm.Field);"),
            ("ProjectsPage.cs", "Ui.HintRow(grid, ProfileFormLayout.ProjectListHint(_editing is not null), HintRowForm.Field);"),
            ("GenerationDialog.cs", "Ui.HintRow(grid, GenerationEntries.KeyHint + \"。\", HintRowForm.Field);"),
        })
        {
            Assert.Contains(call, NativeFile(file), StringComparison.Ordinal);
        }
    }

    /// <summary>设计基线记下这条口径与它的唯一落点，界面引用它而不是引用一段散文。</summary>
    [Fact]
    public void DesignDoc_DeclaresTheHintRowClause()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("Ui.HintRow", section, StringComparison.Ordinal);
        Assert.Contains("提示行", section, StringComparison.Ordinal);
        // §8 记下这一族修到了哪几屏、哪一族还没量（按钮行的占位控件本轮未动）。
        var gap = Repo.DesignSection(8);
        Assert.Contains("Ui.HintRow", gap, StringComparison.Ordinal);
        Assert.Contains("按钮行", gap, StringComparison.Ordinal);
    }

    /// <summary>
    /// 「这一行到底多高」在 Linux 判不了，由冒烟器在 wine 下逐条量（第一百六十轮那条同一判据：
    /// 长度门管「别写成段落」，行高归真实控件）。这一段被删掉、或不再覆盖那四个对话框，本条即红。
    /// </summary>
    [Fact]
    public void Smoke_MeasuresTheHintRowHeightsOfThoseFourDialogs()
    {
        Assert.Contains("四个子对话框的提示行没有死空白", SmokeSource, StringComparison.Ordinal);
        // 第三百五十四轮把「认提示行」与「取那条提示行本体」拆成两处（`IsHintRow` 转调 `HintRowControl`），
        // 判据仍是同一个出口：这里只判「那个判据还在、签名没变」，不判它是不是表达式体。
        Assert.Matches(@"(?m)^\s*private static bool IsHintRow\(TableLayoutPanel grid, int row\)", SmokeSource);
        foreach (var screen in new[] { "新建任务", "模块与模板", "序列化与分析", "模型服务" })
        {
            Assert.Contains($"(\"{screen}\"", SmokeSource, StringComparison.Ordinal);
        }

        // 阈值取同一份口径，不在冒烟器里另写一个数。
        Assert.Contains("ProfileFormLayout.MaxRowHeight(UiTokens.Body.Value.Height)", SmokeSource, StringComparison.Ordinal);
    }

    // ── 第三百五十轮（简报 P1-1）：把那 20 / 6 的分类落成逐句表，并把这张表接进调用点与 §6 那三个数 ──

    /// <summary>归属对照表在 <c>docs/NOTES.md</c> 里的那一节标题前缀。按标题现读，表本体不在这里抄第二份。</summary>
    private const string OwnershipHeading = "### 提示行 26 句的所有权归属";

    /// <summary>
    /// 「归属」那一列的封闭取值：每个取值都是一条能在源码行序列里核对的关系。
    /// <para>
    /// 写成封闭名单而不是「从表里抓出来的那些值」：抓出来的名单会跟着表漂，而这一条要防的恰恰是漂——
    /// 第六种写法出现即红，本仓不猜新写法的含义（猜一次，往后每一行都按各自的猜法判）。
    /// </para>
    /// </summary>
    private static readonly string[] OwnershipKinds =
    [
        "字段级·紧邻上一行", "字段级·紧邻下一行", "多字段·上方连续", "多字段·上方不连续", "分区级·标题之后",
    ];

    private sealed record OwnershipRow(string File, string Arg, string Kind, string Form, string[] Owners);

    private enum RowKind { Section, Field, Hint }

    /// <summary>
    /// 网格里出厂的一行：字段行与分区标题那一格的 <see cref="Text"/> 是标签列（或标题）的字面，
    /// 说明行那一格是工厂收到的实参，<see cref="Form"/> 是工厂收到的第三个实参（非说明行为空串）。
    /// </summary>
    private sealed record SourceRow(RowKind Kind, string Text, string Form = "");

    /// <summary>
    /// 「归属」那一列与「形态」那一列的对应（第三百五十二轮）：字段级两种 → <c>Field</c>，
    /// 多字段两种与分区级 → <c>Section</c>。这不是读感，是这一族「一眼可辨」的承诺本身——
    /// 形态与归属各说一套，屏幕上那条竖线就在说谎。
    /// </summary>
    private static string FormOfKind(string kind) =>
        kind is "字段级·紧邻上一行" or "字段级·紧邻下一行" ? "Field" : "Section";

    /// <summary>
    /// 逐句归属表 ↔ 源码调用点 ↔ <c>CallSites</c> 登记 ↔ §6 那三个数，四方一条链。
    /// <para>
    /// <b>补的是哪一个洞</b>：第三百四十九轮登记的是<b>按文件</b>的数（序列化与分析 8 里 6、模块与模板 6 里 3、
    /// 模型服务 3 里 2、新建任务 5、项目设置 4），G-D5 真机复核要的是<b>按句</b>的对象——「这一句现在靠什么
    /// 被读成属于哪个编辑位」。按文件的数只能数、找不到对象，那张表若只活在文档里，下一句提示行加进来它就
    /// 悄悄过期。本条把表接回实物：表里每一行都得在源码里指得到那一行，指不到就红。
    /// </para>
    /// <para><b>它不判什么</b>：不判真机上那一句读起来像字段注脚还是分区注脚（那是观感，只有人眼答得出，
    /// 这张表是给那只眼睛用的清单），也不判这 26 句该不该各有不同形状（两形的三条取舍在 NOTES 第三百四十九轮）。</para>
    /// </summary>
    [Fact]
    public void HintOwnershipTable_CoversEveryCallSiteBothWays()
    {
        var problems = OwnershipProblems(OwnershipTableLines(), Repo.DesignSection(6));
        Assert.True(problems.Count == 0,
            $"提示行的归属对照表（{OwnershipHeading} 那一节）与实物对不上：{string.Join("；", problems)}。"
            + "这张表是第三百四十九轮那笔「随 G-D5 复核」的前置材料——表与实物各说一套，真机到手那天就照着一套不存在的话逐句看。");
    }

    /// <summary>
    /// 上面那条门自己被抓得住：七发注入各喂一次，每一发都必须报出自己那一处，合规那发不报。
    /// <para>与本仓同一族做法（<c>..._ReportsTheBreaksItMustCatch</c>）：判据没被跑过，比没有判据更贵。</para>
    /// </summary>
    [Fact]
    public void HintOwnershipTable_DetectorReportsTheBreaksItMustCatch()
    {
        var real = OwnershipTableLines();
        var design = Repo.DesignSection(6);

        // ① 合规：一张表都不报。
        Assert.Empty(OwnershipProblems(real, design));

        // ② 少一行（新增一屏提示行而表没跟上）→ 该文件的行数与登记对不上。
        var dropped = real.Where((_, i) => i != 0).ToList();
        Assert.Contains(OwnershipProblems(dropped, design), p => p.Contains("ProjectsPage.cs", StringComparison.Ordinal));

        // ③ 说明句改一个字 → 逐字对不上。
        var reworded = real.Select(l => l.Contains("分支留空表示主分支") ? l.Replace("主分支", "缺省分支") : l).ToList();
        Assert.Contains(OwnershipProblems(reworded, design), p => p.Contains("缺省分支", StringComparison.Ordinal));

        // ④ 编辑位指错一行（把「标签」那一句说成管「服务 ID」）→ 紧邻那一行不是它。
        var mislabeled = real.Select(l => l.Contains("`用「,」分隔。`") ? l.Replace("`标签`", "`服务 ID`") : l).ToList();
        Assert.Contains(OwnershipProblems(mislabeled, design), p => p.Contains("服务 ID", StringComparison.Ordinal));

        // ⑤ 第六种归属写法 → 封闭名单不猜含义。
        var newKind = real.Select(l => l.Contains("字段级·紧邻上一行", StringComparison.Ordinal)
            ? l.Replace("字段级·紧邻上一行", "字段级·旁边", StringComparison.Ordinal) : l).ToList();
        Assert.Contains(OwnershipProblems(newKind, design), p => p.Contains("旁边", StringComparison.Ordinal));

        // ⑥ §6 那个 20 改成 19（登记与表各说一套）→ 三个数对不上。
        Assert.Contains(
            OwnershipProblems(real, design.Replace("26 句里有 20 句", "26 句里有 19 句", StringComparison.Ordinal)),
            p => p.Contains("19", StringComparison.Ordinal));

        // ⑦ 形态与归属对不上（把「用「,」分隔。」那一句的形态改成 Section）→ 屏幕上那条竖线在说谎。
        var wrongForm = real.Select(l => l.Contains("`用「,」分隔。`", StringComparison.Ordinal)
            ? l.Replace("| Field |", "| Section |", StringComparison.Ordinal) : l).ToList();
        Assert.Contains(OwnershipProblems(wrongForm, design), p => p.Contains("用「,」分隔。", StringComparison.Ordinal));

        // ⑧ 形态与源码第三实参对不上（表里把「分支留空表示主分支。」那一句的归属与形态一起改成
        //    多字段·上方连续 / Section——归属与形态自洽，只有源码那一头说它是 Field）→ 表与实物各说一套。
        var wrongSourceForm = real.Select(l => l.Contains("`分支留空表示主分支。`", StringComparison.Ordinal)
            ? l.Replace("| 字段级·紧邻上一行 | Field |", "| 多字段·上方连续 | Section |", StringComparison.Ordinal) : l).ToList();
        Assert.Contains(OwnershipProblems(wrongSourceForm, design), p => p.Contains("分支留空表示主分支", StringComparison.Ordinal));
    }

    /// <summary>整条判据：表文本 + §6 那一格 → 问题清单（空 = 四方一致）。注入走同一条路，不开第二份判据。</summary>
    private static List<string> OwnershipProblems(IReadOnlyList<string> tableLines, string designSection)
    {
        var problems = new List<string>();
        if (tableLines.Count == 0)
        {
            problems.Add($"{OwnershipHeading} 那一节读不出任何一行表：第三百四十九轮那句「20 / 6」就只剩散文，真机那天没有可逐句看的对象。");
            return problems;
        }

        var rows = new List<OwnershipRow>();
        foreach (var line in tableLines)
        {
            var cells = OwnershipCells(line);
            if (cells.Count != 5)
            {
                problems.Add($"表有一行是 {cells.Count} 格（应为 5 格：屏 / 说明句 / 归属 / 形态 / 管的编辑位）：{line.Trim()}");
                continue;
            }

            // 首格写的是仓内全路径（本节在 G-D5 那一节里，反引号点到的文件必须能落到实物）；
            // 与 CallSites、NativeFile 对账用的键是那一串的文件名。
            rows.Add(new OwnershipRow(cells[0].Split('/', '\\').Last(), cells[1], cells[2], cells[3],
                cells[4].Split('、').Select(s => s.Trim().Trim('`')).Where(s => s.Length > 0).ToArray()));
        }

        if (problems.Count > 0) return problems;

        // 归属取值封闭。
        foreach (var kind in rows.Select(r => r.Kind).Where(k => !OwnershipKinds.Contains(k, StringComparer.Ordinal)).Distinct())
        {
            problems.Add($"表里出现没登记的归属写法「{kind}」：{nameof(OwnershipKinds)} 之外的写法不猜含义——一个取值一条源码关系，猜错的那一行整行都不可信。");
        }

        if (problems.Count > 0) return problems;

        // 形态取值封闭，且与「归属」那一列按同一条对应走（第三百五十二轮）。
        foreach (var row in rows)
        {
            if (row.Form is not ("Field" or "Section"))
            {
                problems.Add($"{row.File}「{row.Arg}」的形态写成「{row.Form}」：只认 Field 与 Section 两个取值——"
                    + "第三个取值等于界面上多出一种没人定义过的形状。");
                continue;
            }

            if (row.Form != FormOfKind(row.Kind))
            {
                problems.Add($"{row.File}「{row.Arg}」的归属是「{row.Kind}」，形态却写成 {row.Form}（按归属应为 {FormOfKind(row.Kind)}）："
                    + "形态与归属各说一套，屏幕上那条竖线就在说谎——它标的是「这一句管一块」，而这一句其实只管一行。");
            }
        }

        if (problems.Count > 0) return problems;

        // 逐文件：表里的行数与顺序 ↔ 登记 ↔ 源码里的工厂调用点，再逐行核对「管的编辑位」。
        var tableFiles = rows.Select(r => r.File).Distinct(StringComparer.Ordinal).ToList();
        foreach (var file in tableFiles)
        {
            var registered = CallSites.FirstOrDefault(s => string.Equals(s.File, file, StringComparison.Ordinal));
            if (registered.File is null)
            {
                problems.Add($"表点到 {file}，可它不在提示行调用点登记 {nameof(CallSites)} 里：这本账没跟上，下一轮没人知道这一屏的说明行是第几族的写法。");
                continue;
            }

            var sourceRows = GridRows(NativeFile(file));
            var hints = sourceRows
                .Select((row, index) => (row.Kind, row.Text, row.Form, index))
                .Where(x => x.Kind == RowKind.Hint)
                .ToList();
            var tableRows = rows.Where(r => r.File == file).ToList();

            if (tableRows.Count != registered.Calls)
            {
                problems.Add($"{file} 在表里 {tableRows.Count} 行，登记的是 {registered.Calls} 处工厂调用（源码现读 {hints.Count} 处）：少一行是真机那天没人看的句子，多一行是表指不到源码的说明。");
                continue;
            }

            for (var i = 0; i < tableRows.Count; i++)
            {
                if (tableRows[i].Arg != hints[i].Text)
                {
                    problems.Add($"{file} 第 {i + 1} 行说的不是源码里那一处出厂的实参：表写「{tableRows[i].Arg}」，源码那一处是「{hints[i].Text}」——逐句表按出厂顺序排，跳序与改字在这里都露出来。");
                    continue;
                }

                if (tableRows[i].Form != hints[i].Form)
                {
                    problems.Add($"{file} 第 {i + 1} 行（「{tableRows[i].Arg}」）表里记的形态是 {tableRows[i].Form}，"
                        + $"源码那一处工厂收到的第三个实参是 HintRowForm.{hints[i].Form}——表与实物各说一套，"
                        + "真机那天按表看的是另一种形状。");
                    continue;
                }

                if (OwnershipMismatch(tableRows[i], hints[i].index, sourceRows) is { } mismatch) problems.Add(mismatch);
            }
        }

        foreach (var site in CallSites.Where(s => !tableFiles.Contains(s.File, StringComparer.Ordinal)))
        {
            problems.Add($"{nameof(CallSites)} 登记了 {site.File}（{site.Calls} 处说明行），逐句归属表里没有它：那一屏的说明行在真机那天没有对象可逐句看。");
        }

        // §6 那三个数 ↔ 表自己数出来的数。
        var claim = System.Text.RegularExpressions.Regex.Match(designSection, @"(\d+) 句里有 (\d+) 句");
        var rest = System.Text.RegularExpressions.Regex.Match(designSection, @"另外 (\d+) 句");
        if (!claim.Success || !rest.Success)
        {
            problems.Add("DESIGN.md §6「提示行」那一格里读不出「26 句里有 20 句」与「另外 6 句」这两个数：取法退化了，这条门会在空对空上绿。");
        }
        else
        {
            var fieldLevel = rows.Count(r => r.Kind is "字段级·紧邻上一行" or "字段级·紧邻下一行");
            var other = rows.Count - fieldLevel;
            if (int.Parse(claim.Groups[1].Value) != rows.Count)
            {
                problems.Add($"§6 说这一族有 {claim.Groups[1].Value} 句说明行，表里数出来是 {rows.Count} 句：登记与表各说一套，真机那天按哪个数逐句看？");
            }

            if (int.Parse(claim.Groups[2].Value) != fieldLevel)
            {
                problems.Add($"§6 说 {claim.Groups[1].Value} 句里有 {claim.Groups[2].Value} 句靠缩进指向紧邻那一个字段，表里「字段级」两类数出来是 {fieldLevel} 句：那一句代价的描述与这张表不再是一件事。");
            }

            if (int.Parse(rest.Groups[1].Value) != other)
            {
                problems.Add($"§6 说另外 {rest.Groups[1].Value} 句管两三个编辑位或整个分区，表里数出来是 {other} 句：这一族最贵的那笔代价被改小或改大了。");
            }
        }

        // §6 点名的那一处最贵的代价：owner 在下面那一行，只有 `模块与模板` 那一句。
        var below = rows.Where(r => r.Kind == "字段级·紧邻下一行").ToList();
        if (below.Count != 1 || below[0].File != "ProfileAdvancedDialog.cs")
        {
            problems.Add($"§6 把「owner 在它下面那一行、跨两列后会读成分区注脚」登记成这一族最贵的一处、且只那一处；表里现在数出来 {below.Count} 行{(below.Count == 1 ? $"（{below[0].File}）" : string.Empty)}：要么表漂了，要么 §6 那句代价描述过期了。");
        }

        return problems;
    }

    /// <summary>一行表的「归属」+「管的编辑位」在源码行序列里核对；null = 对得上。</summary>
    private static string? OwnershipMismatch(OwnershipRow row, int hintIndex, IReadOnlyList<SourceRow> all)
    {
        switch (row.Kind)
        {
            case "字段级·紧邻上一行":
            case "字段级·紧邻下一行":
            {
                if (row.Owners.Length != 1)
                {
                    return $"{row.File}：{row.Kind} 要求只点一个编辑位，表里点了 {row.Owners.Length} 个（那一句管的不是紧邻一行）。";
                }

                var want = row.Kind.EndsWith("上一行", StringComparison.Ordinal) ? hintIndex - 1 : hintIndex + 1;
                if (want < 0 || want >= all.Count)
                {
                    return $"{row.File}「{row.Arg}」：{row.Kind} 指的是网格外面那一行（这一句在网格边上）。";
                }

                var neighbour = all[want];
                if (neighbour.Kind != RowKind.Field || neighbour.Text != row.Owners[0])
                {
                    return $"{row.File}「{row.Arg}」：{row.Kind} 说的那一行其实是{KindText(neighbour.Kind)}「{neighbour.Text}」，不是编辑位「{row.Owners[0]}」。";
                }

                return null;
            }

            case "多字段·上方连续":
            {
                if (row.Owners.Length < 2)
                {
                    return $"{row.File}「{row.Arg}」：{row.Kind} 要求至少两个编辑位，表里只点了 {row.Owners.Length} 个。";
                }

                for (var k = 0; k < row.Owners.Length; k++)
                {
                    var want = hintIndex - row.Owners.Length + k;
                    var rowAt = want >= 0 ? all[want] : null;
                    if (rowAt is null || rowAt.Kind != RowKind.Field || rowAt.Text != row.Owners[k])
                    {
                        return $"{row.File}「{row.Arg}」：上面第 {row.Owners.Length - k} 行按表应该是编辑位「{row.Owners[k]}」，实际是"
                            + (rowAt is null ? "网格之外" : $"{KindText(rowAt.Kind)}「{rowAt.Text}」") + "（这一族说的必须是紧挨着的那几行）。";
                    }
                }

                return null;
            }

            case "多字段·上方不连续":
            {
                if (row.Owners.Length < 2)
                {
                    return $"{row.File}「{row.Arg}」：{row.Kind} 要求至少两个编辑位，表里只点了 {row.Owners.Length} 个。";
                }

                var above = all.Take(hintIndex).Where(r => r.Kind == RowKind.Field).Select(r => r.Text).ToList();
                var missing = row.Owners.Where(o => !above.Contains(o, StringComparer.Ordinal)).ToList();
                return missing.Count == 0
                    ? null
                    : $"{row.File}「{row.Arg}」：表说这一句管「{string.Join("、", missing)}」，可那些编辑位在源码里不在它那上面（这句「填入上方字段」指的字段有一个不存在或排在下面）。";
            }

            case "分区级·标题之后":
            {
                if (row.Owners.Length != 1)
                {
                    return $"{row.File}：{row.Kind} 要求点名一个分区，表里点了 {row.Owners.Length} 个。";
                }

                var want = hintIndex - 1;
                var above = want >= 0 ? all[want] : null;
                if (above is null || above.Kind != RowKind.Section || above.Text != row.Owners[0])
                {
                    return $"{row.File}「{row.Arg}」：{row.Kind} 说它紧跟在分区「{row.Owners[0]}」的标题之后，实际上一行是"
                        + (above is null ? "网格之外" : $"{KindText(above.Kind)}「{above.Text}」") + "。";
                }

                return null;
            }

            default:
                return $"{row.File}：没登记的归属写法「{row.Kind}」。";
        }
    }

    private static string KindText(RowKind kind) => kind switch
    {
        RowKind.Field => "字段行",
        RowKind.Section => "分区标题",
        _ => "另一句说明行",
    };

    /// <summary>按标题现读那一节的表行（切到下一个二级 / 三级标题之前；首格是反引号文件名的才算数据行）。</summary>
    private static List<string> OwnershipTableLines()
    {
        var lines = Repo.Source("docs", "NOTES.md").ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith(OwnershipHeading, StringComparison.Ordinal));
        Assert.True(start >= 0,
            $"docs/NOTES.md 里找不到以「{OwnershipHeading}」开头的那一节：第三百五十轮那张逐句归属表不在了，"
            + "G-D5 那天只剩「20 / 6」那两个数、没有可逐句看的对象。");

        return lines[start..]
            .Skip(1)
            .TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal) && !l.StartsWith("### ", StringComparison.Ordinal))
            .Where(l => Regex.IsMatch(l, @"^\| `[^`]+` \|"))
            .ToList();
    }

    /// <summary>一行表的格（去掉 markdown 的分隔竖线两端与行内代码的反引号）。</summary>
    private static List<string> OwnershipCells(string line) =>
        line.Split('|').Skip(1).Take(line.Split('|').Length - 2).Select(c => c.Trim().Trim('`')).ToList();

    /// <summary>按源码行序枚举网格里出厂的每一行（字段行 / 分区标题 / 说明行；注释行与语句都不算）。</summary>
    private static List<SourceRow> GridRows(string source)
    {
        var rows = new List<SourceRow>();
        foreach (var raw in source.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            var hint = Regex.Match(line, @"^Ui\.HintRow\(grid, (?<arg>.+), HintRowForm\.(?<form>Field|Section)\);$");
            if (hint.Success)
            {
                rows.Add(new SourceRow(RowKind.Hint, Unquote(hint.Groups["arg"].Value), hint.Groups["form"].Value));
                continue;
            }

            var field = Regex.Match(line, @"^(?:Ui\.FieldRow|AddRow)\(grid, (?<label>""[^""]*"")[,)]");
            if (field.Success)
            {
                rows.Add(new SourceRow(RowKind.Field, Unquote(field.Groups["label"].Value)));
                continue;
            }

            var section = Regex.Match(line, @"^AddSection\(grid, (?<title>.+)\);$");
            if (section.Success)
            {
                rows.Add(new SourceRow(RowKind.Section, Unquote(section.Groups["title"].Value)));
            }
        }

        return rows;
    }

    /// <summary>成对反引号或双引号包住的那一串取中间的字面；没包住的（取共享常量的表达式）原样返回。</summary>
    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;

    private static string FactoryBody()
    {
        var start = UiSource.IndexOf("static Label HintRow(TableLayoutPanel grid, string text, HintRowForm form)", StringComparison.Ordinal);
        Assert.True(start >= 0, "Ui.cs 里没有 HintRow。");
        var end = UiSource.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "Ui.HintRow 切不出方法体边界。");
        return UiSource[start..end];
    }

    /// <summary>方法体切到下一个顶格 <c>private</c> 声明之前（段体里有 lambda，花括号配对会被字符串骗）。</summary>
    private static string MethodBody(string text, string signature)
    {
        var start = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"找不到方法 {signature}。");
        var end = text.IndexOf("\n    private", start + signature.Length, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
