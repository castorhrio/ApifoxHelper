using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 第二百七十二轮那两处「只有屏幕上看得见」的呈现，本轮起各有一扇源码门钉住
/// （第二百七十五轮，简报 P0-1，具名 pass：harden）。
/// <para>
/// <b>补的是哪一笔账</b>：第二百七十二轮把第 2 站那一屏真的开起来逐像素量，改了两行
/// （<c>Desktop/Native/TasksPage.cs</c> 里 <c>_stepper</c> 的 <c>WrapContents</c> 由 <c>false</c> 改 <c>true</c>、
/// <c>_detailHint</c> 补一句 <c>AutoSize = false</c>），并<b>自己把代价写在仓里</b>：「把 <c>WrapContents</c>
/// 改回 <c>false</c>、或把 <c>AutoSize</c> 改回 <c>true</c>，<c>dotnet test</c> 一个字节都不会红」
/// （<c>docs/CRITIQUE.md</c> 第二百七十二轮那一节的「门」那条、<c>docs/NOTES.md</c> 同轮账本
/// 「本轮不发门的代价」）。第二百七十四轮简报把它列成 P2-1 时也没做，理由是那次要立的是<b>量测段</b>那一形
/// （真读那一行的右边界），会同时动三档总数、逐屏表那一行与 <c>MeasuredScreens.cs</c> 的登记，是一整次切片。
/// <b>那一形本轮仍不做</b>，见下面「它不判什么」第 ① 项。
/// </para>
/// <para>
/// <b>本轮立的是哪一种门</b>：仓库已有的成熟形状——源码切片 + 行首 <c>^\s*</c> 锚定 + 判「有几处」，
/// 被注释掉的那一行等同删除（去注释只走 <see cref="CodeScan"/> 那一份口径）。它抓的就是简报点名的那一种：
/// <b>改回上一版而测试全绿</b>。不需要新读数、不动冒烟器、不改三档总数。
/// </para>
/// <para>
/// <b>为什么这不算第二百五十六轮那一族的重复路径</b>：那一族停的是「为已有判据找恒等式」——那种门判的是
/// <b>判据自己</b>（<c>DialogHeight</c> 由 <c>DialogHeightFor</c> 定义，于是「各块之和 == DialogHeight」永远为真）。
/// 本类判的是<b>被承诺的实物</b>：界面上那一行、那一格各自的布局属性，正是第二百七十轮划开的那条边界留给
/// 「照常随实物一起长」的那一类。把 <c>TasksPage.cs</c> 里那两行删掉，本类当场红——与「恒等式永远绿」是相反的形状。
/// </para>
/// <para>
/// <b>它不判什么</b>：① <b>不判那一行在真机上排成几行</b>、也不判那句提示是不是真的落在栏中间——那要读像素，
/// 是挂着的量测段那一形，随 G-D5；② <b>不判进度条挂在详情网格的第几行</b>（「进度条在横幅之前」已由
/// <c>StageStepsTests</c> 判住，本类不抄第二份口径）；③ <b>不判另外七处</b> <c>WrapContents = false</c>
/// 该不该也折行——第二百七十二轮的 critique 结论是「其余四行放得下是因为它们各只有两三颗按钮，
/// 不是因为那几句被逐行选过」，那几处没量过，本轮不为它们改判据。
/// 但 ③ 有一个副产品：这一族里「唯一被量过、因此唯一该折行」的那一处若悄悄变成两处，
/// 下面那条计数会红，逼着改的人先交代另一处的读数从哪来。
/// </para>
/// </summary>
public class WrapAndFillTests
{
    /// <summary>
    /// 三格进度条那一行的字段声明。行首要锚定、初值块要整个收进 <c>body</c>：
    /// 「这句话在文件的某个地方出现过」不是这一条要判的东西——判的是<b>那一行</b>自己写的是什么。
    /// </summary>
    private static readonly Regex StepperDeclaration = new(
        @"^\s*private readonly FlowLayoutPanel _stepper = new\(\) \{(?<body>[^}]*)\};",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(2));

    /// <summary>这一族里被量过、因而该折行的那一处——只此一处。</summary>
    private const string WrapsCode = "WrapContents = true";

    /// <summary>那句空态提示的三件套：铺满那一格、把内容居中、并且这一格真的挂在右栏上。</summary>
    private const string HintFillsTheCell = "_detailHint.Dock = DockStyle.Fill;";

    private const string HintIsCentered = "_detailHint.TextAlign = ContentAlignment.MiddleCenter;";
    private const string HintSitsInTheRightPane = "_split.Panel2.Controls.Add(_detailHint);";

    /// <summary>第二百七十二轮补的那一句：关掉 autosize，前两句才有对象。</summary>
    private const string HintStopsAutoSize = "_detailHint.AutoSize = false;";

    private const string HintAutoSizeBackOn = "_detailHint.AutoSize = true";

    /// <summary>
    /// 三格进度条那一行按可用宽换行：不折行时那一行读不全，而它是「三个阶段各到哪一步」的唯一凭据。
    /// </summary>
    [Fact]
    public void StepperRow_StillWrapsWithinTheDetailColumn()
    {
        Assert.Empty(StepperProblems(Code()));
    }

    /// <summary>
    /// 右栏那句空态提示仍然自己关掉 autosize，而且 <c>Dock = Fill</c> / <c>MiddleCenter</c> /
    /// 「挂在右栏那一格」三个前提都还在——前提被拆掉时，本类要的是一条红，不是一条判空气的绿。
    /// </summary>
    [Fact]
    public void DetailEmptyHint_StillStopsAutoSizeAndStillFillsItsCell()
    {
        Assert.Empty(HintProblems(Code()));
    }

    /// <summary>
    /// 「<c>AutoSize = false</c> 是必要的一句」这件事取决于工厂出厂的那一侧：<c>Ui.Caption</c> 走的
    /// <c>Ui.Label</c> 若哪天改成默认不 autosize，这一句就该由工厂承担，本类要跟着改，
    /// 而不是留一条「判自己那句是否存在」的空门。这一条就是那句前提的实物。
    /// </summary>
    [Fact]
    public void CaptionFactory_StillShipsAnAutoSizingLabel()
    {
        var factory = SourceSlice.BodyOf(Repo.Source("Desktop", "Native", "Ui.cs"), "private static Label Label(");
        Assert.Equal(1, CodeScan.CountContaining(factory, "AutoSize = true,"));
    }

    /// <summary>
    /// 判据<b>自己</b>被抓得住：八发注入各喂一次，每一发都只报自己那一处。
    /// <para>
    /// ②③ 是简报点名的两发（改回 <c>false</c> / 改回 <c>true</c>）；④ 是本仓第三次为它立形状的骗法
    /// （整句注释掉）；⑤⑥⑦ 判的是「这句话凭什么还有意义」——值挪到别的控件上、那句提示不再挂进右栏、
    /// 字段改名。⑧ 是顺手把另一处也改成折行。与 <c>GateDetector_ReportsTheBreaksItMustCatch</c> 同一纪律：
    /// 判据没被跑过，比没有判据更贵。
    /// </para>
    /// </summary>
    [Fact]
    public void Gate_ReportsTheBreaksItMustCatch()
    {
        var current = Code();

        // ① 前提：磁盘上那一份两半都合规（后面每一发之所以能红，只可能来自注入的破坏）。
        Assert.Empty(StepperProblems(current));
        Assert.Empty(HintProblems(current));

        // ② 第二百七十二轮之前那一版：三格那一行不折行。
        var unwrapped = StepperProblems(current.Replace($"{WrapsCode}, Margin", "WrapContents = false, Margin"));
        Assert.Single(unwrapped);
        Assert.Contains("不折行", unwrapped[0], StringComparison.Ordinal);
        Assert.DoesNotContain("两处", unwrapped[0], StringComparison.Ordinal);

        // ③ 同一版的另一半：autosize 没关。
        var autosizeBackOn = HintProblems(current.Replace(HintStopsAutoSize, HintAutoSizeBackOn + ";"));
        Assert.Single(autosizeBackOn);
        Assert.Contains("AutoSize = true", autosizeBackOn[0], StringComparison.Ordinal);

        // ④ 整句注释掉：与删掉同一件事，第三次于本仓立形（去注释只走 CodeScan 一份）。
        var commented = HintProblems(current.Replace(
            $"        {HintStopsAutoSize}",
            $"        // {HintStopsAutoSize}（先留着看看）"));
        Assert.Single(commented);
        Assert.Contains("被注释掉", commented[0], StringComparison.Ordinal);

        // ⑤ 值挪走：关的是另一个控件的 autosize，这一格照旧缩成文字那么大。
        var moved = HintProblems(current.Replace(HintStopsAutoSize, "_nextHint.AutoSize = false;"));
        Assert.Single(moved);
        Assert.Contains("0 处", moved[0], StringComparison.Ordinal);

        // ⑥ 前提被拆：那句提示不再挂进右栏（Dock = Fill 铺的不再是这一格，量到的栏宽不属这一屏）。
        var orphaned = HintProblems(current.Replace(HintSitsInTheRightPane, "// 挂到别处去了"));
        Assert.Single(orphaned);
        Assert.Contains("Panel2", orphaned[0], StringComparison.Ordinal);

        // ⑦ 字段改名：判的就不再是第二百七十二轮量过的那一行。
        var renamed = StepperProblems(current.Replace("_stepper = new()", "_stageRow = new()"));
        Assert.Single(renamed);
        Assert.Contains("0 处", renamed[0], StringComparison.Ordinal);

        // ⑧ 顺手把另一处也折行：那一处本轮没量过，改的人得先交代读数从哪来。
        var secondWrap = StepperProblems(current.Replace(
            "FlowLayoutPanel _nextActions = new() { AutoSize = true, WrapContents = false",
            $"FlowLayoutPanel _nextActions = new() {{ AutoSize = true, {WrapsCode}"));
        Assert.Single(secondWrap);
        Assert.Contains("2 处", secondWrap[0], StringComparison.Ordinal);
    }

    /// <summary>去掉注释的界面源码：注释里点名 <c>WrapContents</c> 是在说明理由，不是在接线。</summary>
    private static string Code() => CodeScan.Text(Repo.Source("Desktop", "Native", "TasksPage.cs"));

    /// <summary>
    /// 三格进度条那一处不达形态的说明（空 = 合规）。
    /// <para>
    /// <b>两个分支为什么互斥</b>：「这一行不折行」与「折行的不止一处」说的是两件事，
    /// 而把唯一那一处改成不折行时后者也顺带成立——那种写法会让简报点名的那一发报两条，
    /// 读失败的人分不出哪一条是正题。所以先看那一行自己，再看总数。
    /// </para>
    /// </summary>
    private static List<string> StepperProblems(string tasks)
    {
        var problems = new List<string>();
        var declarations = StepperDeclaration.Matches(tasks);
        if (declarations.Count != 1)
        {
            problems.Add(
                $"三格进度条那一行的声明在这一份源码里是 {declarations.Count} 处（应是 1 处）："
                + "字段改名、拆成两处、或整段挪走时，本类判的就不是第二百七十二轮量过的那一行了——"
                + "要么把声明改回来，要么把这一条连同理由一起改。");
            return problems;
        }

        var body = declarations[0].Groups["body"].Value;
        if (!body.Contains(WrapsCode, StringComparison.Ordinal))
        {
            problems.Add(
                "三格进度条那一行现在不折行。按可用宽折行是量出来的结论，不是猜的：wine 96 档逐像素扫到"
                + "第二格从 272px 起被 402px 的网格右边界截断（「未核」只剩半个字）、第三格整格在可视区之外，"
                + "详情区因此挂出一条横向滚动条——那一行读不全时，「三个阶段各到哪一步」就没有凭据了（第二百七十二轮）。"
                + "要改回不折行，先给这一屏一份新的右边界读数。");
            return problems;
        }

        var wrapped = CodeScan.CountContaining(tasks, WrapsCode);
        if (wrapped != 1)
        {
            problems.Add(
                $"这一份源码里有 {wrapped} 处「{WrapsCode}」，而被量过因而该折行的只有三格进度条那一处："
                + "其余几处各只有两三颗按钮，放得下不是因为那几句被逐行选过（第二百七十二轮 critique 那一节）。"
                + "另一处确实要折行，先量过再把这一条改成两处，并写明那一处的读数出自哪一跑。");
        }

        return problems;
    }

    /// <summary>右栏空态提示那一处不达形态的说明（空 = 合规）。</summary>
    private static List<string> HintProblems(string tasks)
    {
        var problems = new List<string>();

        var on = CodeScan.CountContaining(tasks, HintAutoSizeBackOn);
        if (on != 0)
        {
            problems.Add(
                $"「{HintAutoSizeBackOn}」出现了 {on} 次：这一句就是第二百七十二轮那一发的原样改回去——"
                + "AutoSize 盖掉 Dock 给的尺寸，那句提示会缩成文字本身那么大、停在栏的左上角"
                + "（像素读数 y≈88..93、x≈7..141；关掉之后是 y≈463..468、x≈134..268）。");
        }
        else
        {
            var off = CodeScan.CountContaining(tasks, HintStopsAutoSize);
            if (off != 1)
            {
                problems.Add(
                    $"「{HintStopsAutoSize}」在这一份源码里是 {off} 处（应是 1 处；被注释掉算没有）："
                    + "少了这一句，Dock = Fill 铺不满那一格、MiddleCenter 也就没有可居中的东西（第二百七十二轮）。");
            }
        }

        // 这一句的意义来自另外三句：前提不在时宁可红，不留一条判空气的绿断言。
        foreach (var (line, why) in new[]
        {
            (HintFillsTheCell, "那一句 autosize 是给 Dock = Fill 让路的：这一格的大小不再由文字决定"),
            (HintIsCentered, "居中判的是那一格，不是那串文字——铺不满时 MiddleCenter 什么也没居中"),
            (HintSitsInTheRightPane, "这一句铺的是分割面板右栏那一格；不再挂在那里时，量到的栏宽就不属这一屏"),
        })
        {
            var count = CodeScan.CountContaining(tasks, line);
            if (count != 1)
            {
                problems.Add($"「{line}」是 {count} 处（应是 1 处）：{why}。");
            }
        }

        return problems;
    }
}
