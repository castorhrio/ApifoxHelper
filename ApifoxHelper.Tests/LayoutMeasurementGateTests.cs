using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「量测段必须逐笔对账、不许只判恒等式」这一族门，从一屏扩到全仓（第二百五十三轮，简报 P0-1，具名 pass：harden + clarify）。
/// <para>
/// <b>补的是哪个缺口</b>：第二百四十九、二百五十、二百五十一、二百五十二轮<b>连续四轮</b>撞的是同一个形态——
/// 冒烟量测段的标题念了三件事（「固定两块与口径对账、清单实测高与算式对账、装得下若干行」），
/// 而段内只判了一句<b>恒等式</b>（<c>content.Height + buttonRow.Height == client.Height</c>：
/// 内容网格 Fill、按钮行 Bottom，加起来必然等于客户区）。恒等式抓不到「常量漂了」——常量漂了窗体高跟着漂，
/// 两边一起动，那一句照样绿。第二百五十二轮把这一课补进了<b>一屏</b>的门
/// （<c>ScanWarningsLayoutTests.TheMeasurement_ReconcilesEveryMeasuredPaneAgainstTheFormula</c>），
/// 并在 commit 里如实挂账「P0-2：假门防护扩到全仓 <c>*LayoutTests.cs</c> 一族未做」。
/// <b>这一份就是那一笔挂账的收口</b>：教训此前只长在一屏的门里，下一轮在另一屏写量测段的人读不到它。
/// </para>
/// <para>
/// <b>这一条判什么</b>：对<b>登记在册</b>的每一份「切了冒烟量测段的测试文件」，切出它切的那一段
/// （<see cref="Registered"/> 里那一对「文件 → 段名」），判两件事：
/// ① 段内至少有 <see cref="MinReconciliations"/> 笔<b>「实测 vs 口径」</b>的对账——
/// 一笔对账 = 一个口径成员（<c>XxxLayout.Member</c> / <c>SyncPreviewPanes.Member</c> / <c>ListColumnWidths.Member</c>）
/// 真的被拿去和实测值比过，三种写法都认：直接比较（<c>if (实测 != 口径.Member)</c>）、
/// 先赋给局部再比（<c>var expected = 口径.Method(口径.Member); if (实测 != expected)</c>）、
/// 成对表（<c>("名字", 实测, 口径.Member)</c> 且段内真有 <c>row.Got &lt; row.Floor</c> 那种同变量两侧的比较）；
/// ② <b>不许只判恒等式</b>——段内比较一句都不沾口径、却有一堆比较，就是第二百五十二轮那一发的原样。
/// </para>
/// <para>
/// <b>它不判什么</b>：<b>不判「量到的数对不对」</b>——「对账了」与「对账对了」是两件事，后者只有人读得出来；
/// <b>不判「段标题念的每一件事都判了」</b>——标题是中文散文，机械判不了，这一条只判「段内有没有真的对账」；
/// <b>不判「这一屏该不该显示」</b>（那是 <c>MeasuredScreensTests</c> 与各屏自己的
/// <c>TheMeasurement_ShowsTheDialog…</c> 管的事）。
/// </para>
/// <para>
/// <b>为什么判据写成「文本进、问题出」的一段函数</b>：本仓的教训是「判据自己没被跑过」比「没有判据」更贵
/// （第二百零九轮那两处假变异、第二百四十三轮 M4 那一发假绿都是这一种）。所以下面那条
/// <see cref="GateDetector_ReportsTheBreaksItMustCatch"/> 拿第二百五十二轮那一发的<b>原样</b>当夹具，
/// 当场验这一条抓不抓得住。
/// </para>
/// <para>
/// <b>本族到此收尾（第二百五十六轮，简报 P0-2）</b>：这一族从第二百五十二轮起连做五轮——
/// 第二百五十二轮补一屏的形状门、第二百五十三轮把「逐笔对账」扩到全仓、第二百五十四轮把验收表分层、
/// 第二百五十五轮把「不需要形状门」的理由登记成表、本轮把最后两笔挂账收掉
/// （<see cref="KnownGaps"/> 清空 + <see cref="NoMeasurementSegment"/> 的理由变成可判）。
/// <b>下一轮若再出现「找恒等式 / 补形状门」的 commit，应判为重复路径</b>：这一族能机械判的已经判完，
/// 剩下的（「量到的数对不对」「这一屏该不该这么分」）只有人读得出来，再加门只会把「门绿」当成「做完了」。
/// 这条约束同时记在 <c>docs/NOTES.md</c>「假门防护这一族：收尾登记」一节。
/// </para>
/// </summary>
public class LayoutMeasurementGateTests
{
    /// <summary>
    /// 登记：测试文件 → 它切的那一段冒烟量测段。<b>封闭名单</b>，加一份要在这里加一行。
    /// <para>
    /// 前六份是「一屏一份竖直预算」这一族（<c>*LayoutTests.cs</c> 里切了量测段的六份）；
    /// 第七份 <c>SyncPreviewPanesTests</c> 的文件名不带 <c>LayoutTests</c>，但它切的是同一族量测段
    /// （<c>RunListColumnFit</c> 的第 ⑤ 段判同步预览四块的行数），所以按<b>内容</b>而不是按文件名收进来——
    /// 按文件名收会把它漏掉，而它正是「成对表」那一种写法的唯一实例。
    /// </para>
    /// </summary>
    private static readonly (string File, string Segment)[] Registered =
    [
        ("CapabilitiesLayoutTests.cs", "RunCapabilitiesShown"),
        ("CheckResultsLayoutTests.cs", "RunCheckResultsShown"),
        ("EndpointTraceLayoutTests.cs", "RunEndpointTraceShown"),
        ("ManualDraftLayoutTests.cs", "RunManualDraftShown"),
        ("ScanWarningsLayoutTests.cs", "RunScanWarningsShown"),
        ("SyncPreviewPanesTests.cs", "RunListColumnFit"),
    ];

    /// <summary>
    /// 名字以 <c>LayoutTests.cs</c> 结尾、但<b>没有</b>切任何冒烟量测段的测试文件：登记 + 理由。
    /// <para>
    /// 这两份判的是<b>纯算式</b>（行高预算、两列网格的列宽），没有「真的显示出来量一遍」那一段，
    /// 所以这一族门对它们无话可说。登记在这里是为了让「无话可说」变成<b>一句可判的话</b>：
    /// 下一轮谁给它们补了量测段，<see cref="TheRegistry_CoversEveryLayoutTestAndEverySegmentItNames"/>
    /// 会当场红并点名——那时要么登记进 <see cref="Registered"/>，要么把这一行连同理由一起改掉。
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> NoMeasurementSegment = new(StringComparer.Ordinal)
    {
        ["ProfileFormLayoutTests.cs"] =
            "这一份里没有「窗体高 = 各块之和」那种同源恒等式：它判的是配置表单的行高预算算式"
            + "（三档正文预算线、分区档预算线、认不出的字号不回退），每一句都是口径取值判据"
            + "（17 + RowPadding、RowPadding 的 4 + 8 + 12），不是恒等式；"
            + "量测在冒烟器 RunProfileFormLayout 里由 ProfileFormLayoutBudgetTests 那一族对账，不在本族登记范围内。",
        ["SplitLayoutTests.cs"] =
            "这一份里没有竖直预算的同源恒等式：它判的是两列网格的列宽分配（标签列吸收剩余宽度），"
            + "判据是 FixedPanel.Panel2 与 SplitterDistance 的口径取值加接线正则，"
            + "没有「显示出来量一遍」那一段，也就没有「量到的数只被一句恒等式判过」这种假门形态。",
    };

    /// <summary>
    /// 门扫出来、按简报<b>只挂账不改量测段</b>的缺口：文件 → 缺口说明。
    /// <para>
    /// <b>为什么挂账而不是顺手补</b>：简报明令「不要在同一轮里既加门又改量测段」——改门改到门绿，
    /// 门就永远不知道自己该抓什么。所以挂账那一轮只把缺口<b>写成一句可判的话</b>：
    /// <see cref="KnownGaps_AreStillGaps"/> 判「登记为缺口的这一份<b>现在仍然</b>不达形态」，
    /// 谁把它补好了，那一条当场红并点名「登记过期，删掉这一行」——挂账不会烂在仓里。
    /// </para>
    /// <para>
    /// <b>第二百五十六轮起这张表是空的</b>：第二百五十三轮挂的那一笔（<c>CapabilitiesLayoutTests.cs</c> 的
    /// <c>RunCapabilitiesShown</c>）本轮补上了三笔「实测 vs 口径」对账，按上面那条规矩删掉了那一行。
    /// 空表是<b>结果</b>不是<b>豁免</b>：<see cref="EveryRegisteredMeasurementSegment_ReconcilesMeasuredAgainstTheCaliber"/>
    /// 现在真的在判 <c>CapabilitiesLayoutTests.cs</c>（此前它被这张表跳过）。
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> KnownGaps = new(StringComparer.Ordinal)
    {
        // 第二百五十六轮起为空：唯一那一笔（CapabilitiesLayoutTests.cs 的 RunCapabilitiesShown）
        // 已由本轮补上三笔「实测 vs 口径」对账（固定三块 / 结果区算式 / 窗体客户区），
        // 按挂账自己的规矩删掉了那一行。**空表不等于这一条门可以删**：它是「显式挂账 + 理由」这个机制本身，
        // 由 GateDetector_ReportsTheBreaksItMustCatch 的第 ⑦ 发当场验（登记了就不报）。
        // 下一轮再扫出缺口，照原样往这里加一行，别把缺口写成散文。
    };

    /// <summary>
    /// 一段量测段至少要有的对账笔数。<b>2 是下限，不是目标</b>：一屏的竖直预算至少有两笔
    /// （固定区合计、主内容区），只判一笔的段等于把「哪一块漂了」这句话交给运气。
    /// </summary>
    private const int MinReconciliations = 2;

    /// <summary>口径成员：<c>XxxLayout.Member</c> / <c>SyncPreviewPanes.Member</c> / <c>ListColumnWidths.Member</c>。</summary>
    private static readonly Regex CaliberReference = new(
        @"\b(?<type>[A-Z]\w*(?:Layout|Panes|Widths))\.(?<member>\w+)",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    /// <summary>
    /// 比较运算符。<c>&lt;</c> / <c>&gt;</c> 只认<b>两侧带空格</b>的那一种：
    /// 本仓的比较都写成 <c>capacity &lt; ScanWarningsLayout.MainRows</c>，而泛型实参写成 <c>FindAll&lt;TextBox&gt;</c>
    /// 不带空格——不分开就会把每一行泛型都数成一次比较（本轮第一版就是这样，恒等式计数虚高）。
    /// </summary>
    private static readonly Regex Comparison = new(
        @"!=|==|<=|>=| < | > ",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    /// <summary>成对表的一行：<c>("名字", 实测表达式, 口径.Member),</c>——最后一个元素是口径成员。</summary>
    private static readonly Regex TupleRow = new(
        @"^\s*\(.*,\s*[A-Z]\w*(?:Layout|Panes|Widths)\.\w+\),?\s*$",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    /// <summary>
    /// 成对表真的被比过的证据：同一个变量两侧各取一个字段再比（<c>row.Got &lt; row.Floor</c>）。
    /// <para>
    /// <b>为什么成对表要额外判这一句</b>：只写一张 <c>("名字", 实测, 口径)</c> 的表、后面从不比较，
    /// 与「只判恒等式」是同一个意思的两种写法——表在那儿，判据不在。这一句是「表真的被拿去比了」的最小证据。
    /// </para>
    /// </summary>
    private static readonly Regex PairedComparison = new(
        @"\b(?<v>\w+)\.\w+\s*(?:<|>|<=|>=|!=|==)\s*\k<v>\.\w+",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    /// <summary>局部变量从口径取值：<c>var expected = 口径.Method(口径.Member);</c>。</summary>
    private static readonly Regex LocalFromCaliber = new(
        @"\bvar\s+(?<name>\w+)\s*=\s*(?<expr>.*)$",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    /// <summary>一段量测段的对账账本：参与过对账的口径成员，以及「一句都不沾口径」的比较条数。</summary>
    private readonly record struct Ledger(IReadOnlySet<string> Members, int IdentityComparisons);

    /// <summary>
    /// 仓里当前那一份：逐份判，把每一处不达形态说出来（空 = 全都在对账）。
    /// </summary>
    [Fact]
    public void EveryRegisteredMeasurementSegment_ReconcilesMeasuredAgainstTheCaliber()
    {
        var problems = Problems(Registered.Select(pair => (
            pair.File,
            pair.Segment,
            SegmentBody(Repo.SmokeProgram, pair.Segment))), KnownGaps);
        Assert.Empty(problems);
    }

    /// <summary>
    /// 登记表本身不许烂：每一份 <c>*LayoutTests.cs</c> 要么登记了量测段、要么登记了「没有量测段」的理由；
    /// 登记了「没有」的那一份<b>真的</b>没有；登记了段名的那一份<b>真的</b>切得到那一段，且那一段<b>真的</b>在冒烟器
    /// <c>Main</c> 的注册清单里（没注册的段跑不到，量测等于没写）。
    /// </summary>
    [Fact]
    public void TheRegistry_CoversEveryLayoutTestAndEverySegmentItNames()
    {
        var directory = Path.Combine(Repo.Root, "ApifoxHelper.Tests");
        var layoutTests = Directory.GetFiles(directory, "*LayoutTests.cs")
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(layoutTests);

        var registeredFiles = Registered.Select(pair => pair.File).ToHashSet(StringComparer.Ordinal);
        var unregistered = layoutTests
            .Where(name => !registeredFiles.Contains(name) && !NoMeasurementSegment.ContainsKey(name))
            .ToList();
        Assert.True(
            unregistered.Count == 0,
            $"这些 *LayoutTests.cs 既没登记量测段、也没登记「没有量测段」的理由：{string.Join("、", unregistered)}——"
            + "新增一份竖直预算的测试就要在 Registered 里加一行（或写清为什么它没有量测段）。");

        // 登记了「没有量测段」的那一份，真的没有：补了段而不改登记，这一条当场红。
        foreach (var (file, reason) in NoMeasurementSegment)
        {
            Assert.True(File.Exists(Path.Combine(directory, file)), $"{file} 在 NoMeasurementSegment 里登记着，但磁盘上没有这一份。");
            Assert.False(string.IsNullOrWhiteSpace(reason), $"{file} 的「没有量测段」理由空着——空理由等于没登记。");
            Assert.DoesNotContain("private static void Run", Repo.Source("ApifoxHelper.Tests", file), StringComparison.Ordinal);
            Assert.False(registeredFiles.Contains(file), $"{file} 同时出现在 Registered 与 NoMeasurementSegment 里：两份登记互相矛盾。");
        }

        // 登记了段名的那一份，真的切得到那一段，且那一段真的在冒烟器的注册清单里。
        var sections = Repo.SmokeSectionNames();
        foreach (var (file, segment) in Registered)
        {
            Assert.True(File.Exists(Path.Combine(directory, file)), $"{file} 在 Registered 里登记着，但磁盘上没有这一份。");
            Assert.Contains(
                $"private static void {segment}(",
                Repo.Source("ApifoxHelper.Tests", file),
                StringComparison.Ordinal);
            Assert.Contains(
                $"private static void {segment}(",
                Repo.SmokeProgram,
                StringComparison.Ordinal);
            Assert.True(
                sections.Contains(segment, StringComparer.Ordinal),
                $"{segment} 在冒烟器里没有注册（Section(…, nameof({segment}))）：跑不到的段量不到任何东西。");
        }
    }

    /// <summary>
    /// 挂账的那一份<b>现在仍然</b>不达形态；补好了就红，点名删掉登记。
    /// <para>
    /// <b>这一条挡的是「挂账烂在仓里」</b>：只写一句「下一轮补」而不判它，下一轮补完之后那一行会一直留着，
    /// 再下一轮的人读到的就是「这一屏还没对账」——一句过期的话比没有这句话更贵。
    /// </para>
    /// </summary>
    [Fact]
    public void KnownGaps_AreStillGaps()
    {
        foreach (var (file, reason) in KnownGaps)
        {
            Assert.False(string.IsNullOrWhiteSpace(reason), $"{file} 的缺口理由空着——空理由等于没挂账。");
            var pair = Registered.SingleOrDefault(entry => entry.File == file);
            Assert.True(pair.File is not null, $"{file} 在 KnownGaps 里登记着，但 Registered 里没有它：挂账的必须是被门扫过的那一份。");

            var ledger = Reconciliations(SegmentBody(Repo.SmokeProgram, pair.Segment));
            Assert.True(
                ledger.Members.Count < MinReconciliations,
                $"{file} 的缺口已经补上了（{pair.Segment} 现在有 {ledger.Members.Count} 笔对账，"
                + $"下限是 {MinReconciliations}）：删掉 KnownGaps 里那一行，让这一份按正常形态被门判。");
        }
    }

    // ---------- 「没有量测段」的理由也要有形状（第二百五十六轮，简报 P0-2，本族收尾轮）----------

    /// <summary>
    /// <see cref="NoMeasurementSegment"/> 的每一条理由必须写成<b>可判的形态</b>：含「恒等式」或「口径」
    /// 二词之一，且不短于 <see cref="MinReasonLength"/> 字。
    /// <para>
    /// <b>补的是哪一笔挂账</b>：第二百五十五轮把「不需要形状门」的理由从散文升级成登记
    /// （<see cref="NoIdentityGuard"/>），但「没有量测段」这一份（<see cref="NoMeasurementSegment"/>）
    /// 的理由仍是一句散文——「判的是纯算式」「没有那一段」都成立，却<b>没有一句话说明那一份里
    /// 到底有没有同源恒等式、有的话为什么不算假门</b>。下一轮读到它的人只能自己再判一次，
    /// 而「再判一次」正是这一族四轮以来一直在重复的那件事。
    /// </para>
    /// <para>
    /// <b>为什么是这两个词</b>：这一族要防的假门只有一种形态——<b>同源恒等式</b>被当成判据
    /// （<c>DialogHeight</c> 由 <c>DialogHeightFor</c> 定义，所以「各块之和 == DialogHeight」永远为真）。
    /// 一条理由只要说清了「这一份里有没有那种恒等式」或「它的判据是哪个口径的取值」，就答完了这个问题；
    /// 两个词都不沾的理由，答的是别的问题。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判理由说得对不对（那是人读的）；不判「这一份真的没有恒等式」——
    /// 判据是中文散文与算式，机械判不了。它判的是「每一条理由都被逼着回答过那个问题」。
    /// </para>
    /// </summary>
    [Fact]
    public void NoMeasurementSegment_Reasons_SayWhyTheIdentityIsNotAFakeGate()
    {
        var problems = ReasonProblems(NoMeasurementSegment);
        // 第二百五十六轮那一节（NOTES 顶部）承诺「停」这个决定要落在仓里——本轮起真的读它一遍。
        problems.AddRange(FakeGateBoundaryProblems(FakeGateRegistration()));
        Assert.Empty(problems);
    }

    // ---------- 「假门防护」与「表 ↔ 源码对账」的边界（第二百七十轮，简报 P0-2）----------

    /// <summary>NOTES 顶部那一行的起始字面（唯一取法；那一行本身就是这一族的收口登记）。</summary>
    private const string FakeGateMarker = "**假门防护这一族：收尾登记";

    /// <summary>
    /// 从 <c>docs/NOTES.md</c> 读出第二百五十六轮那一行。
    /// <para>
    /// <b>本轮为什么第一次去读它</b>：那一行末尾写着「判这一行在不在……是
    /// <see cref="NoMeasurementSegment_Reasons_SayWhyTheIdentityIsNotAFakeGate"/>」——而第二百五十六轮到
    /// 第二百六十九轮之间，那一条门<b>只看登记表 <see cref="NoMeasurementSegment"/>，从不读 NOTES</b>。
    /// 那句承诺当时没有实物：把那一行整段删掉，本族所有门照旧全绿。这正是这一族一直在防的形状
    /// （写在纸上的判据没人跑），本轮把它接上，并顺手把「表 ↔ 源码对账」的边界一起判住。
    /// </para>
    /// </summary>
    private static string FakeGateRegistration()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var line = notes.Split('\n').FirstOrDefault(l => l.Contains(FakeGateMarker, StringComparison.Ordinal));
        Assert.True(line is not null,
            $"docs/NOTES.md 顶部找不到「{FakeGateMarker}」那一行：第二百五十六轮「这一族收口」那个决定"
            + "又只活在 commit message 里了——那一行自己写着「判这一行在不在」的是本文件，它得真的可判。");
        return line;
    }

    /// <summary>
    /// 那一行必须回答的三件事（判据本体：登记行进、问题清单出）：
    /// ① <b>停</b>——第二百五十六轮那句「本轮是这一族的最后一轮」；
    /// ② <b>边界</b>——第二百七十轮划开的「表 ↔ 源码对账」，且区分口径必须写成
    ///    「判据在判自己」与「判据在判被承诺的实物」两个短语都在；
    /// ③ <b>可照做</b>——第二百八十二轮（简报 P0-1）把上面那句边界从散文收成五步判定步骤，
    ///    三处字面必须都在：小节标题「怎么判一条新门属不属于本族（可照做」、
    ///    第 ② 步那句「只引用被它判的那个对象自己的定义」、第 ④ 步那句「删掉它之后本族其余各条照旧全绿」。
    /// <para>
    /// <b>为什么 ② 要两个短语都在</b>：只写「那一族不属本族」是一句<b>可以被自证的空话</b>——
    /// 下一轮任何人给自己那条恒等式门补一句「我这是对账不是恒等式」就过关了。
    /// 两个短语把判法钉住：门判的对象是<b>判据自己</b>还是<b>被承诺的实物</b>，读的人一眼能验。
    /// </para>
    /// <para>
    /// <b>为什么 ③ 要三处字面都在</b>：② 只说了「判什么」，没说「怎么判」——读到它的人仍要自己再判一次，
    /// 而「再判一次」正是这一族四轮以来一直在重复的那件事。③ 要的是<b>可照做的两步</b>：
    /// 先看断言引用的名字是不是只指向被它判的那个对象自己的定义（<c>DialogHeight</c> 由
    /// <c>DialogHeightFor</c> 定义，所以「各块之和 == DialogHeight」永远为真），
    /// 再把这条门删掉跑一次 <c>dotnet test</c>——本族其余各条照旧全绿就是本族要停的那一类。
    /// 三处字面各钉一环：标题在不在（这一节有没有被写出来）、第 ② 步在不在（判据的形状）、
    /// 第 ④ 步在不在（判据的验法）。缺第 ④ 步时这一节退化成「再写一句停在这里」。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判下一轮的人会不会真的照做——那是纪律，不是判据；
    /// 也不判那五步写得对不对（那是人读的）。它判的是「这一节里那三处字面还在」，
    /// 与 ② 同一形状：把「停」这个决定钉在仓里，而不是只活在某一轮的 commit message 里。
    /// </para>
    /// </summary>
    private static List<string> FakeGateBoundaryProblems(string registration)
    {
        var problems = new List<string>();

        if (!registration.Contains("这一族的最后一轮", StringComparison.Ordinal))
        {
            problems.Add("那一行没写「本轮是这一族的最后一轮」：收口决定被写丢了，下一轮读不到「该停在哪」。");
        }

        if (!registration.Contains("表 ↔ 源码对账", StringComparison.Ordinal))
        {
            problems.Add("那一行没点「表 ↔ 源码对账」这一族（第二百五十九轮起的 "
                + "ProfileJumpTests.LandingButtonTable_CoversEveryButtonOnTheConfigPage 那一种）："
                + "两族边界没人划，下一轮既可能以「同族」为名撤掉对账门，也可能以「再补一处」为名补恒等式门。");
        }

        if (!registration.Contains("判据在判自己", StringComparison.Ordinal)
            || !registration.Contains("判据在判被承诺的实物", StringComparison.Ordinal))
        {
            problems.Add("那一行没把区分口径写成「判据在判自己」与「判据在判被承诺的实物」两个短语："
                + "只说「不属本族」不给判法，任何一条门都能自称是对账门而过关。");
        }

        // ③ 可照做（第二百八十二轮，简报 P0-1）：边界从散文收成五步判定步骤，三处字面各钉一环。
        if (!registration.Contains("怎么判一条新门属不属于本族（可照做", StringComparison.Ordinal))
        {
            problems.Add("那一行没有「怎么判一条新门属不属于本族（可照做」这一小节："
                + "边界只说了「判什么」、没说「怎么判」，读到它的人仍要自己再判一次——"
                + "而「再判一次」正是这一族四轮以来一直在重复的那件事。");
        }

        if (!registration.Contains("只引用被它判的那个对象自己的定义", StringComparison.Ordinal))
        {
            problems.Add("那一行没写第 ② 步「只引用被它判的那个对象自己的定义」："
                + "「判据在判自己」这句话的形状没有被说出来（DialogHeight 由 DialogHeightFor 定义，"
                + "所以「各块之和 == DialogHeight」永远为真），下一轮仍认不出自己那条门属不属于本族。");
        }

        if (!registration.Contains("删掉它之后本族其余各条照旧全绿", StringComparison.Ordinal))
        {
            problems.Add("那一行没写第 ④ 步「删掉它之后本族其余各条照旧全绿」："
                + "这是这一族唯一可当场做的验法——缺了它，这一节退化成「再写一句停在这里」，"
                + "而第二百五十六轮反对的正是那种写法。");
        }

        return problems;
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：四发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// 与 <see cref="GateDetector_ReportsTheBreaksItMustCatch"/> 同一纪律：判据自己没被跑过，
    /// 比没有判据更贵（第二百零九轮那两处假变异、第二百四十三轮 M4 那一发假绿都是这一种）。
    /// </para>
    /// </summary>
    [Fact]
    public void ReasonDetector_ReportsTheBreaksItMustCatch()
    {
        // ① 合规：两条理由都点了「恒等式」或「口径」，且够长。
        Assert.Empty(ReasonProblems(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AaaLayoutTests.cs"] = "这一份里没有同源恒等式，判的是口径取值（17 + RowPadding），量测在冒烟器那一段里对账。",
            ["BbbLayoutTests.cs"] = "这一份判的是列宽口径（FixedPanel.Panel2），没有「显示出来量一遍」那一段，也就没有恒等式。",
        }));

        // ② 第二百五十五轮之前那一句的原样：散文成立，但没回答「有没有恒等式」。
        var prose = ReasonProblems(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SplitLayoutTests.cs"] = "判的是两列网格的列宽分配（标签列吸收剩余宽度），没有「显示出来量一遍」那一段，也没有别的判据。",
        });
        Assert.Single(prose);
        Assert.Contains("恒等式", prose[0], StringComparison.Ordinal);
        Assert.Contains("口径", prose[0], StringComparison.Ordinal);

        // ③ 点了词但太短：一句话糊过去。
        var tooShort = ReasonProblems(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CccLayoutTests.cs"] = "没有恒等式。",
        });
        Assert.Single(tooShort);
        Assert.Contains("字", tooShort[0], StringComparison.Ordinal);

        // ④ 空理由：空理由等于没登记。
        var empty = ReasonProblems(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DddLayoutTests.cs"] = "   ",
        });
        Assert.Single(empty);
        Assert.Contains("空着", empty[0], StringComparison.Ordinal);

        // ⑤ 前提：仓里现在那一行两处都答过（⑥⑦ 之所以能红，只可能来自注入的破坏）。
        Assert.Empty(FakeGateBoundaryProblems(FakeGateRegistration()));

        // ⑥ 第二百五十六轮那一行的原样（第二百七十轮之前）：「停」写着，两族的边界一个字没划。
        var noBoundary = FakeGateBoundaryProblems(
            "**假门防护这一族：收尾登记（第二百五十六轮，简报 P0-2）**（判据）：本轮是这一族的最后一轮，"
            + "下一轮若再出现「找恒等式 / 补形状门」的 commit，应判为重复路径并停在这里。");
        Assert.Contains(noBoundary, p => p.Contains("表 ↔ 源码对账", StringComparison.Ordinal));
        Assert.Contains(noBoundary, p => p.Contains("两个短语", StringComparison.Ordinal));
        Assert.DoesNotContain(noBoundary, p => p.Contains("收口决定被写丢", StringComparison.Ordinal));

        // ⑦ 只补半句「不属本族」、不给区分口径：仍然报——那种登记下一轮能自证。
        var half = FakeGateBoundaryProblems(
            "本轮是这一族的最后一轮；表 ↔ 源码对账 不属本族。");
        Assert.Contains(half, p => p.Contains("判据在判自己", StringComparison.Ordinal));

        // ⑧ 第二百七十轮那一行的原样（第二百八十二轮之前）：停写着、边界两个短语都在，
        //    但「怎么判」一个字没有——这一发就是本轮开工的那一发，它必须报在 ③ 那三处上。
        var noSteps = FakeGateBoundaryProblems(
            "**假门防护这一族：收尾登记（第二百五十六轮，简报 P0-2）**（判据）：本轮是这一族的最后一轮。"
            + "**边界（第二百七十轮）**：本族停止的范围只有一句——为已有判据找恒等式、补形状门。"
            + "第二百五十九轮起的「表 ↔ 源码对账」不属本族。区分口径就这一句："
            + "**判据在判自己 = 本族，停在这里；判据在判被承诺的实物 = 对账门，照常随实物一起长**。");
        Assert.Equal(3, noSteps.Count);
        Assert.Contains(noSteps, p => p.Contains("怎么判一条新门属不属于本族（可照做", StringComparison.Ordinal));
        Assert.Contains(noSteps, p => p.Contains("只引用被它判的那个对象自己的定义", StringComparison.Ordinal));
        Assert.Contains(noSteps, p => p.Contains("删掉它之后本族其余各条照旧全绿", StringComparison.Ordinal));
        Assert.DoesNotContain(noSteps, p => p.Contains("这一族的最后一轮", StringComparison.Ordinal));
        Assert.DoesNotContain(noSteps, p => p.Contains("表 ↔ 源码对账", StringComparison.Ordinal));
    }

    /// <summary>一条「没有量测段」的理由至少要有的字数。<b>40 是下限，不是目标</b>：一句话答不完那个问题。</summary>
    private const int MinReasonLength = 40;

    /// <summary>
    /// 把一份「没有量测段」的登记喂给判据，返回「哪几条理由不达形态」的说明（空 = 每条都答过那个问题）。
    /// <para>
    /// 写成「登记表进、问题出」的纯函数，是为了让上面那条自测能拿<b>注入过的</b>夹具喂它——
    /// 与 <see cref="Problems"/> / <see cref="IdentityGuardProblems"/> 同一形状。
    /// </para>
    /// </summary>
    private static List<string> ReasonProblems(IReadOnlyDictionary<string, string> reasons)
    {
        var problems = new List<string>();
        foreach (var (file, reason) in reasons)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                problems.Add($"{file} 的「没有量测段」理由空着——空理由等于没登记。");
                continue;
            }

            var text = reason.Trim();
            if (!text.Contains("恒等式", StringComparison.Ordinal) && !text.Contains("口径", StringComparison.Ordinal))
            {
                problems.Add(
                    $"{file} 的「没有量测段」理由既没点「恒等式」也没点「口径」：这一族要防的假门只有一种形态"
                    + "（同源恒等式被当成判据），理由必须回答「这一份里有没有那种恒等式」或「它的判据是哪个口径的取值」，"
                    + "否则下一轮读到它的人只能自己再判一次。");
            }

            if (text.Length < MinReasonLength)
            {
                problems.Add(
                    $"{file} 的「没有量测段」理由只有 {text.Length} 字（下限 {MinReasonLength} 字）："
                    + "一句话答不完「有没有恒等式、为什么不算假门」这个问题。");
            }
        }

        return problems;
    }

    // ---------- 「假门防护」扩到全仓 *LayoutTests.cs 一族（第二百五十五轮，简报 P1-1）----------

    /// <summary>
    /// 有<b>专门的「不许退回恒等式」门</b>的 <c>*LayoutTests.cs</c>：文件 → 门方法名。
    /// <para>
    /// <b>补的是哪一笔挂账</b>：第二百五十二轮在 commit 里如实挂账「P0-2：假门防护扩到全仓
    /// <c>*LayoutTests.cs</c> 一族未做」；第二百五十三轮把「逐笔对账」扩到了全仓一族
    /// （<see cref="Registered"/> 那一份），但<b>「判它不是恒等式」这一半仍只长在一屏的门里</b>
    /// （<c>EndpointTraceLayoutTests.TheHalfDialogGate_IsNotAnIdentity</c>）。
    /// 第二百五十五轮只读核对全仓七份 <c>*LayoutTests.cs</c>，结论是：<b>同源恒等式还剩一处</b>——
    /// <c>ManualDraftLayoutTests.TheFixedAreaShare_UsesTheSameCaliberAsTheOtherScreens</c> 里那句
    /// <c>Assert.Equal(InputAreaHeight(MainRows), DialogHeight − fixedArea)</c>，注释念的是
    /// 「主内容拿得到判据行数那么多行」，而那一句由 <c>DialogHeight</c> 的定义推出、永远为真。
    /// 本轮把它删掉并补上形状门，于是这一族有两份。
    /// </para>
    /// <para>
    /// <b>这一份登记判什么</b>：登记的门方法<b>真的在文件里</b>、且它<b>真的在判形状</b>
    /// （段内至少一句 <c>Assert.DoesNotContain</c>——判的是「那个记号不在」），
    /// 同时<b>该在的那一条真判据还在</b>（段内至少一句 <c>Assert.Contains</c> 或 <c>Assert.True</c>，
    /// 否则把整条门删空也能过）。
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> IdentityGuards = new(StringComparer.Ordinal)
    {
        ["EndpointTraceLayoutTests.cs"] = "TheHalfDialogGate_IsNotAnIdentity",
        ["ManualDraftLayoutTests.cs"] = "TheFixedAreaShareGate_IsNotAnIdentity",
    };

    /// <summary>
    /// <b>没有</b>专门形状门的 <c>*LayoutTests.cs</c>：文件 → 理由（为什么这一份不需要）。
    /// <para>
    /// <b>这一份登记要诚实</b>：其中三份<b>确实还有同源恒等式</b>（<c>DialogHeight</c> 由
    /// <c>DialogHeightFor</c> 定义，所以「各块之和 == DialogHeight」永远为真）。本轮<b>不为它们补形状门</b>，
    /// 理由写在每一条里：那一句的注释没有替它宣称一件它判不到的事，而同一方法里另有实测口径的取值判据。
    /// 第二百五十一轮补形状门，是因为那一屏<b>只剩</b>恒等式；这三份不是。
    /// </para>
    /// <para>
    /// <b>登记在这里是为了让「不需要」变成一句可判的话</b>：新增一份 <c>*LayoutTests.cs</c> 时，
    /// <see cref="EveryLayoutTestFile_HasItsIdentityGuardRegistered"/> 会当场红并点名——
    /// 那时要么补形状门登记进 <see cref="IdentityGuards"/>，要么把这一份为什么没有假门形态写清楚。
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> NoIdentityGuard = new(StringComparer.Ordinal)
    {
        ["CapabilitiesLayoutTests.cs"] =
            "这一份里确实还有同源恒等式（ResultPaneHeight_IsRowsTimesRowHeightPlusHeaderAndChrome 末尾那句"
            + "「FixedPanesHeight + ResultPaneHeight(ProbeRows) == DialogHeight」，右边就是 DialogHeightFor 的定义）。"
            + "本轮不为它补形状门：那一句的注释没有替它宣称一件它判不到的事，而同一方法里 7 * 21 + 24 + 43 与 21 "
            + "是实测口径的取值判据、DialogHeightFor_TracksEveryRow 再判 6 / 7 / 8 三个行数点——"
            + "恒等式不是这一份唯一的判据。第二百五十一轮补形状门，是因为那一屏只剩恒等式；这一份不是。",
        ["CheckResultsLayoutTests.cs"] =
            "同 Capabilities 那一份：MainListHeight_IsRowsTimesRowHeightPlusHeader 末尾那句"
            + "「FixedPanesHeight + MainListHeight(MainRows) + ListHostChrome == DialogHeight」是恒等式，"
            + "但同一方法里 8 * 21 + 24 与 21 是取值判据，DialogHeightFor_TracksEveryRow 再判 7 / 8 / 9 三个行数点；"
            + "FixedPanesHeight_IsTheSumOfTheSixNamedPanes 里那句「和是加出来的」同样是恒等式，"
            + "但六块各自钉死（31 / 39 / 124 / 168 / 148 / 60）且总和钉死 570。恒等式不是这一份唯一的判据。",
        ["ScanWarningsLayoutTests.cs"] =
            "同前两份：ListHostHeight_IsRowsTimesLineHeightPlusTheTwoChromes 末尾那句是恒等式，"
            + "但同一方法里 16 * 21、16 * 21 + 10、17 * 21 + 10 与 21 是取值判据，"
            + "DialogHeightFor_TracksEveryRow 再判 15 / 16 / 17 三个行数点；"
            + "FixedPanesHeight_IsTheSumOfTheTwoNamedPanes 里两块各自钉死（25 / 60）且总和钉死 85。",
        ["ProfileFormLayoutTests.cs"] =
            "判的是纯算式（MaxRowHeight 的 17 + RowPadding 与 41、RowPadding 的 4 + 8 + 12），"
            + "没有「窗体高 = 各块之和」这种同源恒等式；量测在冒烟器 RunProfileFormLayout 里由"
            + " ProfileFormLayoutBudgetTests 那一族对账，不在本族登记范围内。",
        ["SplitLayoutTests.cs"] =
            "判的是列宽分配（FixedPanel.Panel2、SplitterDistance 的可行性门控）与接线正则，"
            + "没有竖直预算的同源恒等式。",
    };

    /// <summary>
    /// 全仓每一份 <c>*LayoutTests.cs</c> 都要在 <see cref="IdentityGuards"/> 或
    /// <see cref="NoIdentityGuard"/> 里出现一次，且登记的那条门真的在判形状。
    /// <para>
    /// <b>它不判什么</b>：不判「这一份还有没有别的假门」——判据是中文散文与算式，机械判不了；
    /// 它判的是「每一份都被做过一次决定」，以及「登记为有形状门的那一份，那条门真的在判形状」。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryLayoutTestFile_HasItsIdentityGuardRegistered()
    {
        var directory = Path.Combine(Repo.Root, "ApifoxHelper.Tests");
        var layoutTests = Directory.GetFiles(directory, "*LayoutTests.cs")
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(layoutTests);

        var problems = IdentityGuardProblems(
            layoutTests,
            IdentityGuards,
            NoIdentityGuard,
            file => Repo.Source("ApifoxHelper.Tests", file));
        Assert.Empty(problems);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：七发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// 与 <see cref="GateDetector_ReportsTheBreaksItMustCatch"/> 同一纪律：判据自己没被跑过，
    /// 比没有判据更贵（第二百零九轮那两处假变异、第二百四十三轮 M4 那一发假绿都是这一种）。
    /// </para>
    /// </summary>
    [Fact]
    public void IdentityGuardDetector_ReportsTheBreaksItMustCatch()
    {
        var files = new[] { "AaaLayoutTests.cs", "BbbLayoutTests.cs" };
        var compliant = """
            public class AaaLayoutTests
            {
                [Fact]
                public void TheGate_IsNotAnIdentity()
                {
                    Assert.DoesNotContain("DialogHeight - fixedArea", gate, StringComparison.Ordinal);
                    Assert.Contains("fixedArea * 2 < ", gate, StringComparison.Ordinal);
                }
            }
            """;
        string Read(string file) => compliant;

        // ① 合规：一份有形状门、一份登记了「为什么不需要」。
        Assert.Empty(IdentityGuardProblems(
            files,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["AaaLayoutTests.cs"] = "TheGate_IsNotAnIdentity" },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["BbbLayoutTests.cs"] = "夹具：这一份没有竖直预算。" },
            Read));

        // ② 一份都没登记：新增的 *LayoutTests.cs 悄悄绕过这一族门。
        var second = IdentityGuardProblems(files, EmptyRegistry(), EmptyRegistry(), Read);
        Assert.Equal(2, second.Count);
        Assert.Contains("既没有专门的「不许退回恒等式」门", second[0], StringComparison.Ordinal);

        // ③ 登记的门方法不存在（改名或删掉）。
        var third = IdentityGuardProblems(
            ["AaaLayoutTests.cs"],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["AaaLayoutTests.cs"] = "TheGate_ThatIsGone" },
            EmptyRegistry(),
            Read);
        Assert.Single(third);
        Assert.Contains("在文件里找不到", third[0], StringComparison.Ordinal);

        // ④ 形状门退化成取值门：一句 DoesNotContain 都没有。
        var valueOnly = """
            public class AaaLayoutTests
            {
                [Fact]
                public void TheGate_IsNotAnIdentity()
                {
                    Assert.Equal(187, fixedArea);
                    Assert.Contains("fixedArea * 2 < ", gate, StringComparison.Ordinal);
                }
            }
            """;
        var fourth = IdentityGuardProblems(
            ["AaaLayoutTests.cs"],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["AaaLayoutTests.cs"] = "TheGate_IsNotAnIdentity" },
            EmptyRegistry(),
            _ => valueOnly);
        Assert.Single(fourth);
        Assert.Contains("Assert.DoesNotContain", fourth[0], StringComparison.Ordinal);

        // ⑤ 只判「不在」、不判「该在的还在」：整条门删空也能过。
        var onlyAbsent = """
            public class AaaLayoutTests
            {
                [Fact]
                public void TheGate_IsNotAnIdentity()
                {
                    Assert.DoesNotContain("DialogHeight - fixedArea", gate, StringComparison.Ordinal);
                }
            }
            """;
        var fifth = IdentityGuardProblems(
            ["AaaLayoutTests.cs"],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["AaaLayoutTests.cs"] = "TheGate_IsNotAnIdentity" },
            EmptyRegistry(),
            _ => onlyAbsent);
        Assert.Single(fifth);
        Assert.Contains("只判「不在」", fifth[0], StringComparison.Ordinal);

        // ⑥ 两份登记互相矛盾：同一份既说有形状门、又说不需要。
        var sixth = IdentityGuardProblems(
            ["AaaLayoutTests.cs"],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["AaaLayoutTests.cs"] = "TheGate_IsNotAnIdentity" },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["AaaLayoutTests.cs"] = "夹具：矛盾登记。" },
            Read);
        Assert.Single(sixth);
        Assert.Contains("互相矛盾", sixth[0], StringComparison.Ordinal);

        // ⑦ 理由空着：空理由等于没登记。
        var seventh = IdentityGuardProblems(
            ["BbbLayoutTests.cs"],
            EmptyRegistry(),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["BbbLayoutTests.cs"] = "  " },
            Read);
        Assert.Single(seventh);
        Assert.Contains("理由空着", seventh[0], StringComparison.Ordinal);
    }

    /// <summary>自测用的空登记表（不借用产品那一份，免得产品改了把自测带红）。</summary>
    private static Dictionary<string, string> EmptyRegistry() => new(StringComparer.Ordinal);

    /// <summary>
    /// 把一批 <c>*LayoutTests.cs</c> 喂给判据，返回「哪几处不达形态」的说明（空 = 每一份都被做过一次决定）。
    /// <para>
    /// 写成「文件名进、问题出」的纯函数，是为了让上面那条自测能拿<b>注入过的</b>夹具喂它——
    /// 与 <see cref="Problems"/> / <c>WiringAssertionShapeTests.Problems</c> 同一形状。
    /// </para>
    /// </summary>
    private static List<string> IdentityGuardProblems(
        IReadOnlyList<string> layoutTests,
        IReadOnlyDictionary<string, string> guards,
        IReadOnlyDictionary<string, string> noGuard,
        Func<string, string> readFile)
    {
        var problems = new List<string>();
        foreach (var file in layoutTests)
        {
            var hasGuard = guards.ContainsKey(file);
            var hasReason = noGuard.ContainsKey(file);

            if (hasGuard && hasReason)
            {
                problems.Add($"{file} 同时登记在 IdentityGuards 与 NoIdentityGuard 里：两份登记互相矛盾，"
                    + "读的人不知道这一份到底有没有形状门。");
                continue;
            }

            if (!hasGuard && !hasReason)
            {
                problems.Add($"{file} 既没有专门的「不许退回恒等式」门、也没登记「为什么不需要」："
                    + "新增一份 *LayoutTests.cs 就要在这里做一次决定——补形状门（登记进 IdentityGuards），"
                    + "或写清这一份为什么没有假门形态（登记进 NoIdentityGuard）。");
                continue;
            }

            if (hasGuard)
            {
                var method = guards[file];
                var signature = $"public void {method}()";
                var body = MethodBodyOrNull(readFile(file), signature);
                if (body is null)
                {
                    problems.Add($"{file} 登记的形状门 {method} 在文件里找不到：门改名或删掉，这一份就再没人判形状。");
                    continue;
                }

                if (!body.Contains("Assert.DoesNotContain", StringComparison.Ordinal))
                {
                    problems.Add($"{file} 的 {method} 一句 Assert.DoesNotContain 都没有："
                        + "形状门判的是「那个记号不在」，只判取值等于多少是判不到「假门回来了」的。");
                }

                if (!body.Contains("Assert.Contains", StringComparison.Ordinal)
                    && !body.Contains("Assert.True", StringComparison.Ordinal))
                {
                    problems.Add($"{file} 的 {method} 只判「不在」、不判「该在的还在」："
                        + "把整条门删空也能过——要留的那一条真判据必须同时被点到。");
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(noGuard[file]))
            {
                problems.Add($"{file} 的「为什么不需要形状门」理由空着——空理由等于没登记。");
            }
        }

        return problems;
    }

    /// <summary>
    /// 取一个方法体：从签名那一行起，到第一个「四空格缩进的收尾大括号」为止；取不到返回 <c>null</c>。
    /// <para>
    /// <b>为什么不在这里断言</b>：这一支要被上面那条自测拿「方法不存在」的夹具喂，
    /// 断言会把「该报的问题」变成「测试自己炸了」——报问题由调用方做。
    /// </para>
    /// </summary>
    private static string? MethodBodyOrNull(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return null;
        var body = new List<string>();
        foreach (var line in source[start..].Split('\n'))
        {
            body.Add(line);
            if (line.TrimEnd() == "    }") break;
        }

        return string.Join('\n', body);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：六发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// 第一发就是第二百五十二轮那一发的<b>原样</b>（只判 <c>sum == client.Height</c> 这一句恒等式）——
    /// 它当时是绿的，这一条必须红。第二发是「一笔对账都没有」（本轮扫出来的 Capabilities 那一发）。
    /// 第三发是合规写法（两笔直接比较）。第四发是成对表形态（合规）。第五发是「成对表写了但从不比较」（假门）。
    /// 第六发是挂账机制本身（登记了就不报）。
    /// </para>
    /// </summary>
    [Fact]
    public void GateDetector_ReportsTheBreaksItMustCatch()
    {
        // ① 第二百五十二轮那一发的原样：只判一句恒等式，标题念的三件事一件没判。
        var identityOnly = """
            var sum = content.Height + buttonRow.Height;
            if (sum != client.Height)
            {
                throw new InvalidOperationException("两块停靠区加起来不等于客户区。");
            }
            """;
        var first = Problems([("ScanWarningsLayoutTests.cs", "RunScanWarningsShown", identityOnly)], NoGaps());
        Assert.Single(first);
        Assert.Contains("一笔「实测 vs 口径」的对账都没有", first[0], StringComparison.Ordinal);
        Assert.Contains("只判恒等式", first[0], StringComparison.Ordinal);

        // ② 一笔对账都没有：口径只被当行数用，判的全是「装不装得下」。
        var noReconciliation = """
            const int ProbeChecks = CapabilitiesLayout.ProbeRows;
            var lastBottom = list.Items[^1].Bounds.Bottom;
            if (vertical || lastBottom > list.ClientSize.Height)
            {
                throw new InvalidOperationException("结果清单出了竖向滚动条。");
            }
            """;
        var second = Problems([("CapabilitiesLayoutTests.cs", "RunCapabilitiesShown", noReconciliation)], NoGaps());
        Assert.Single(second);
        Assert.Contains("一笔「实测 vs 口径」的对账都没有", second[0], StringComparison.Ordinal);

        // ③ 合规写法：两笔直接比较（实测 vs 口径）。
        var reconciled = """
            if (fixedPanes != ScanWarningsLayout.FixedPanesHeight)
            {
                throw new InvalidOperationException("固定两块对不上。");
            }
            if (capacity < ScanWarningsLayout.MainRows)
            {
                throw new InvalidOperationException("装不下。");
            }
            """;
        Assert.Empty(Problems([("ScanWarningsLayoutTests.cs", "RunScanWarningsShown", reconciled)], NoGaps()));

        // ④ 合规写法：先赋给局部再比（ManualDraft 那一屏的形态）。
        var viaLocal = """
            var expectedInput = ManualDraftLayout.InputAreaHeight(ManualDraftLayout.MainRows);
            if (host.Height != expectedInput)
            {
                throw new InvalidOperationException("输入区对不上。");
            }
            if (fixedPanes != ManualDraftLayout.FixedPanesHeight)
            {
                throw new InvalidOperationException("固定四块对不上。");
            }
            """;
        Assert.Empty(Problems([("ManualDraftLayoutTests.cs", "RunManualDraftShown", viaLocal)], NoGaps()));

        // ⑤ 合规写法：成对表 + 同变量两侧的比较（RunListColumnFit 第 ⑤ 段的形态）。
        var pairedTable = """
            var rows = new (string Name, int Got, int Floor)[]
            {
                ("写入清单", action.ClientSize.Height / SyncPreviewPanes.RowHeight, SyncPreviewPanes.ActionRows),
                ("未纳入清单", excluded.ClientSize.Height / SyncPreviewPanes.RowHeight, SyncPreviewPanes.ExcludedRows),
            };
            var shortOf = rows.Where(row => row.Got < row.Floor).ToList();
            """;
        Assert.Empty(Problems([("SyncPreviewPanesTests.cs", "RunListColumnFit", pairedTable)], NoGaps()));

        // ⑥ 假门：成对表写了，但段内从不比较——表在那儿，判据不在。
        var tableNeverCompared = """
            var rows = new (string Name, int Got, int Floor)[]
            {
                ("写入清单", action.ClientSize.Height / SyncPreviewPanes.RowHeight, SyncPreviewPanes.ActionRows),
                ("未纳入清单", excluded.ClientSize.Height / SyncPreviewPanes.RowHeight, SyncPreviewPanes.ExcludedRows),
            };
            _lines.Add(string.Join("、", rows.Select(row => $"{row.Name} {row.Got} 行（判据 {row.Floor}）")));
            """;
        var sixth = Problems([("SyncPreviewPanesTests.cs", "RunListColumnFit", tableNeverCompared)], NoGaps());
        Assert.Single(sixth);
        Assert.Contains("一笔「实测 vs 口径」的对账都没有", sixth[0], StringComparison.Ordinal);

        // ⑦ 挂账机制本身：登记了就不报（否则「显式挂账 + 理由」这句话是空的）。
        var registered = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CapabilitiesLayoutTests.cs"] = "夹具：这一条判的是挂账机制，不是产品。",
        };
        Assert.Empty(Problems([("CapabilitiesLayoutTests.cs", "RunCapabilitiesShown", noReconciliation)], registered));
    }

    /// <summary>自测用的空挂账表（不借用产品那一份，免得产品补好了把自测带红）。</summary>
    private static Dictionary<string, string> NoGaps() => new(StringComparer.Ordinal);

    /// <summary>
    /// 把一批量测段喂给判据，返回「哪几处不达形态」的说明（空 = 全都在对账）。
    /// <para>
    /// 写成纯文本进、问题出的函数，是为了让上面那条自测能拿<b>注入过的</b>夹具喂它——
    /// 与 <c>WiringAssertionShapeTests.Problems</c> / <c>MutationVerificationTests.Mismatches</c> 同一形状。
    /// </para>
    /// </summary>
    private static List<string> Problems(
        IEnumerable<(string File, string Segment, string Body)> segments,
        IReadOnlyDictionary<string, string> knownGaps)
    {
        var problems = new List<string>();
        foreach (var (file, segment, body) in segments)
        {
            if (knownGaps.ContainsKey(file)) continue;

            var ledger = Reconciliations(body);
            if (ledger.Members.Count >= MinReconciliations) continue;

            problems.Add(
                $"{file} 的 {segment} 一笔「实测 vs 口径」的对账都没有（下限 {MinReconciliations} 笔）"
                + (ledger.IdentityComparisons > 0
                    ? $"：段内 {ledger.IdentityComparisons} 句比较一句都不沾口径——只判恒等式抓不到「常量漂了」，"
                      + "常量漂了窗体高跟着漂、两边一起动，那一句照样绿（第二百五十二轮那一发的原样）。"
                      + "要判的是「实测 vs 口径里的那个数」，逐笔对账。"
                    : "：段内连一句比较都没有，量到的数一个都没拿去撞口径。"));
        }

        return problems;
    }

    /// <summary>
    /// 一段量测段的对账账本：哪些口径成员真的被拿去和实测值比过，以及「一句都不沾口径」的比较条数。
    /// <para>
    /// 三种写法都认（本仓现有的三种，见类注释）：直接比较、先赋给局部再比、成对表（且表真的被比过）。
    /// 注释先按 <see cref="CodeScan"/> 去掉——注释里点名一个口径成员是在说明理由，不是在判它。
    /// </para>
    /// </summary>
    private static Ledger Reconciliations(string segment)
    {
        var lines = CodeScan.Lines(segment).ToList();

        // 先收「从口径取值的局部变量」：`var expected = 口径.Method(口径.Member);`
        var locals = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var match = LocalFromCaliber.Match(line);
            if (!match.Success) continue;
            var members = CaliberMembers(match.Groups["expr"].Value);
            if (members.Count > 0) locals[match.Groups["name"].Value] = members;
        }

        // 成对表只有在「表真的被拿去比了」时才算数。
        var tableIsCompared = PairedComparison.IsMatch(segment);

        var reconciled = new HashSet<string>(StringComparer.Ordinal);
        var identityComparisons = 0;
        foreach (var line in lines)
        {
            var members = CaliberMembers(line);
            if (TupleRow.IsMatch(line))
            {
                if (tableIsCompared) reconciled.UnionWith(members);
                continue;
            }

            if (!Comparison.IsMatch(line)) continue;

            var hit = new HashSet<string>(members, StringComparer.Ordinal);
            foreach (var (name, fromCaliber) in locals)
            {
                if (Regex.IsMatch(line, $@"\b{Regex.Escape(name)}\b", RegexOptions.None, TimeSpan.FromSeconds(2)))
                {
                    hit.UnionWith(fromCaliber);
                }
            }

            if (hit.Count > 0) reconciled.UnionWith(hit);
            else identityComparisons++;
        }

        return new Ledger(reconciled, identityComparisons);
    }

    /// <summary>一段文本里出现的口径成员（<c>XxxLayout.Member</c> 这种全名，去重）。</summary>
    private static HashSet<string> CaliberMembers(string text) =>
        CaliberReference.Matches(text)
            .Select(match => $"{match.Groups["type"].Value}.{match.Groups["member"].Value}")
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// 取一个方法体：从签名那一行起，到第一个「四空格缩进的收尾大括号」为止（冒烟器里方法都是这个缩进）。
    /// <para>取不到即红：段改名或搬家，这一族门就再也管不住它。</para>
    /// </summary>
    private static string SegmentBody(string source, string segment)
    {
        var signature = $"private static void {segment}(";
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} 在冒烟器里找不到了——量测段改名或搬家，这一族门就再也管不住它。");
        var body = new List<string>();
        foreach (var line in source[start..].Split('\n'))
        {
            body.Add(line);
            if (line.TrimEnd() == "    }") break;
        }

        return string.Join('\n', body);
    }
}
