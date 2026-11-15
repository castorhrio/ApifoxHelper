using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「声明的标识列序号」与「那一屏真的加在第 N 位的列」两侧对账（第三百一十七轮，具名 pass：harden + clarify）。
/// <para>
/// <b>补的是哪个缺口</b>：<c>ListCopy</c> 的类注释自己写着「列序被改而声明没跟上时宁可复制不到，也不把邻列的
/// 文字当成名称抄出去」——那句话只保证<b>越界</b>时不动作（<see cref="ApifoxHelper.Desktop.Native.ListCopy.IdentityColumn"/>
/// 判的是「序号落在格数之内」），<b>判不到「序号还在范围内、但那一格已经不是名称了」</b>。四屏在出厂处声明了
/// 非首列的标识列（差异确认 <c>ResourceColumn = 1</c>、任务日志 <c>SummaryColumn = 2</c>、代码证据
/// <c>EvidenceSummaryColumn = 1</c>、同步预览写入清单 <c>ActionResourceColumn = 2</c>），这些序号是<b>裸数字</b>：
/// 谁在它前面插一列（比如给差异确认加一列「风险等级」），序号照旧在范围内，Ctrl+C 从此抄出去的是邻列——
/// 用户粘进「指定接口」里认不出是谁，比复制不到更难发现。第三百一十七轮之前，这件事只有两屏在模拟器上
/// 真的按过一次（<c>RunListCopyGesture</c>），另外两屏连按都没按过。
/// </para>
/// <para>
/// <b>这一条判什么</b>（两侧对账，与 <c>SendAwayExemptions</c>、<c>ProfileJumpTests.LandingButtonTable</c> 同一形状）：
/// ① 源码侧——<c>Desktop/Native</c> 下每一处 <c>Ui.List(identityColumn: X)</c> 都要在登记表里有一行；
/// ② 登记侧——登记表里每一行都要在源码里找得到那一处声明（删了声明而登记没跟着删，这一条当场红）；
/// ③ 序号侧——<c>X</c> 那个常量必须在同一个文件里声明，且它的值必须落在「这一屏真的加了几列」之内；
/// ④ 标题侧——第 <c>X</c> 位那一列的标题必须逐字等于登记表里写的那个标题（**这一条才是真正抓「插一列」的那一条**：
/// 序号还在范围内、标题已经不是名称了）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「那一格里的文字对不对」（那是数据的事，由 <c>RunListCopyGesture</c> 在真实控件上量）、
/// 不判「这一屏该不该声明非首列」（登记的是现状）、不判默认首列那些屏（它们不声明，序号恒为 0，
/// 由 <c>ListCopy.DefaultIdentityColumn</c> 一处定义）。
/// </para>
/// <para>
/// <b>按第二百八十二轮五步法判过</b>：① 断言里出现的口径名是登记表（本文件）与 <c>Desktop/Native</c> 的源码文本；
/// ② 后者的定义不在被它判的那个对象里，答「不是」；③ 按第 ③ 步当<b>对账门</b>处理，不属第二百五十六轮收口那一族；
/// ④ 实测见 <see cref="Detector_ReportsTheBreaksItMustCatch"/> 那六发；⑤ 结论写在这一句。
/// </para>
/// </summary>
public class ListCopyIdentityColumnTests
{
    /// <summary>
    /// 登记表：哪一屏、哪一个清单字段、声明的常量叫什么、第 N 位那一列应当叫什么。
    /// <para>
    /// 加一屏声明非首列标识列，就要在这里加一行——不加，① 那一判当场红并点名是哪个文件哪一处。
    /// </para>
    /// </summary>
    private static readonly (string File, string Field, string Constant, string Title)[] Declared =
    [
        ("DecisionsDialog.cs", "_list", "ResourceColumn", "资源"),
        ("JournalDialog.cs", "_list", "SummaryColumn", "摘要"),
        ("EvidenceDialog.cs", "_list", "EvidenceSummaryColumn", "证据摘要"),
        ("SyncPreviewDialog.cs", "_actionList", "ActionResourceColumn", "资源"),
    ];

    /// <summary>源码里那一处声明：<c>private readonly ListView _list = Ui.List(identityColumn: ResourceColumn);</c>。</summary>
    private static readonly Regex Declaration = new(
        @"ListView\s+(?<field>\w+)\s*=\s*Ui\.List\([^)]*identityColumn:\s*(?<constant>\w+)\)",
        RegexOptions.Compiled);

    /// <summary>常量声明：<c>private const int ResourceColumn = 1;</c>。</summary>
    private static readonly Regex ConstantDeclaration = new(
        @"const\s+int\s+(?<name>\w+)\s*=\s*(?<value>\d+)\s*;",
        RegexOptions.Compiled);

    /// <summary>仓里当前那一份：逐条对账，把每一处「声明与实物对不上」说出来（空 = 对得上）。</summary>
    [Fact]
    public void EveryDeclaredIdentityColumn_IsTheColumnThatSitsAtThatIndex()
    {
        var problems = Problems(Sources(), Declared);
        Assert.Empty(problems);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：六发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// 六发分别是：① 登记里写的标题与实物不符；② 常量值被改成 0（序号还在范围内，但那一格是首列）；
    /// ③ 源码里新长出一处声明而登记没跟着长；④ 登记里留着一行源码里已经没有的声明；
    /// ⑤ 常量值越界（这一屏根本没加那么多列）；⑥ 合规写法（不报）。
    /// </para>
    /// </summary>
    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        // 夹具：一屏声明了第 1 位是「资源」，另一屏走默认首列（不声明）。
        const string Screen = """
            private const int ResourceColumn = 1;
            private readonly ListView _list = Ui.List(identityColumn: ResourceColumn);
            private void Build()
            {
                _list.Columns.Add("类型", 100);
                _list.Columns.Add("资源", 120);
                _list.Columns.Add("位置", 180);
            }
            """;
        const string Plain = """
            private readonly ListView _list = Ui.List();
            private void Build() { _list.Columns.Add("接口", 300); }
            """;

        var registry = new[] { ("Screen.cs", "_list", "ResourceColumn", "资源") };
        var sources = new[] { ("Screen.cs", Screen), ("Plain.cs", Plain) };

        // ⑥ 合规写法：一处声明、序号与标题都对得上——不报。
        Assert.Empty(Problems(sources, registry));

        // ① 登记里写的标题与实物不符：第 1 位是「资源」，登记说「类型」。
        var wrongTitle = Problems(sources, [("Screen.cs", "_list", "ResourceColumn", "类型")]);
        Assert.Contains(wrongTitle, p => p.Contains("资源", StringComparison.Ordinal) && p.Contains("类型", StringComparison.Ordinal));

        // ② 常量值被改成 0：序号还在范围内，但那一格是首列「类型」——这一发正是「插一列」的形状。
        var shifted = Problems([("Screen.cs", Screen.Replace("ResourceColumn = 1;", "ResourceColumn = 0;", StringComparison.Ordinal)), ("Plain.cs", Plain)], registry);
        Assert.Contains(shifted, p => p.Contains("第 0 位", StringComparison.Ordinal) && p.Contains("类型", StringComparison.Ordinal));

        // ③ 源码里新长出一处声明而登记没跟着长。
        var unregistered = Problems([.. sources, ("Extra.cs", Screen)], registry);
        Assert.Contains(unregistered, p => p.Contains("Extra.cs", StringComparison.Ordinal) && p.Contains("没有登记", StringComparison.Ordinal));

        // ④ 登记里留着一行源码里已经没有的声明。
        var stale = Problems(sources, [.. registry, ("Gone.cs", "_list", "GoneColumn", "资源")]);
        Assert.Contains(stale, p => p.Contains("Gone.cs", StringComparison.Ordinal) && p.Contains("找不到", StringComparison.Ordinal));

        // ⑤ 常量值越界：这一屏只加了 3 列，声明第 9 位。
        var outOfRange = Problems([("Screen.cs", Screen.Replace("ResourceColumn = 1;", "ResourceColumn = 9;", StringComparison.Ordinal)), ("Plain.cs", Plain)], registry);
        Assert.Contains(outOfRange, p => p.Contains("第 9 位", StringComparison.Ordinal) && p.Contains("3 列", StringComparison.Ordinal));
    }

    /// <summary>
    /// 把一批源码与一份登记喂给判据，返回「哪几处对不上」的说明（空 = 全对得上）。
    /// <para>
    /// 写成纯文本进、问题出的函数，是为了让上面那条自测能拿<b>注入过的</b>夹具喂它——
    /// 与 <c>MeasuredScreensTests.Problems</c>、<c>WiringAssertionShapeTests.Problems</c> 同一形状。
    /// </para>
    /// </summary>
    private static List<string> Problems(
        IReadOnlyList<(string File, string Text)> sources,
        IReadOnlyList<(string File, string Field, string Constant, string Title)> registry)
    {
        var problems = new List<string>();
        var found = new List<(string File, string Field, string Constant)>();

        foreach (var (file, text) in sources)
        {
            foreach (Match match in Declaration.Matches(text))
            {
                found.Add((file, match.Groups["field"].Value, match.Groups["constant"].Value));
            }
        }

        // ① 源码里每一处声明都要在登记里有一行。
        foreach (var (file, field, constant) in found)
        {
            if (!registry.Any(row => row.File == file && row.Field == field && row.Constant == constant))
            {
                problems.Add($"{file} 里 {field} 声明了标识列 {constant}，而登记表里没有登记这一处："
                    + "新加一屏声明非首列标识列，要在登记表里写清第几位那一列应当叫什么。");
            }
        }

        // ② 登记里每一行都要在源码里找得到那一处声明。
        foreach (var row in registry)
        {
            if (!found.Any(entry => entry.File == row.File && entry.Field == row.Field && entry.Constant == row.Constant))
            {
                problems.Add($"登记表里 {row.File} 的 {row.Field}（{row.Constant}）在源码里找不到："
                    + "声明被删了而登记没跟着删，这一行就是死条目。");
            }
        }

        // ③④ 序号与标题：常量值必须落在这一屏真的加了几列之内，且第 N 位那一列的标题要逐字对得上。
        foreach (var (file, field, constant) in found)
        {
            var text = sources.First(source => source.File == file).Text;
            var declared = registry.FirstOrDefault(row => row.File == file && row.Field == field && row.Constant == constant);
            if (declared.Title is null) continue; // ① 已经报过「没登记」，这里不再重复报一次。

            var constantMatch = ConstantDeclaration.Matches(text)
                .Cast<Match>()
                .FirstOrDefault(match => match.Groups["name"].Value == constant);
            if (constantMatch is null)
            {
                problems.Add($"{file} 里 {field} 声明了标识列 {constant}，而同一个文件里找不到 `const int {constant} = N;`："
                    + "序号从哪来读不出来，这一处判不了。");
                continue;
            }

            var index = int.Parse(constantMatch.Groups["value"].Value);
            var titles = ColumnTitles(text, field);
            if (index >= titles.Count)
            {
                problems.Add($"{file} 的 {field} 声明标识列是第 {index} 位，而这一屏只加了 {titles.Count} 列："
                    + "序号越界时 Ctrl+C 什么都不抄（ListCopy.IdentityColumn 返回 null），这一屏的复制等于没有。");
                continue;
            }

            if (titles[index] != declared.Title)
            {
                problems.Add($"{file} 的 {field} 声明标识列是第 {index} 位，登记说那一列叫「{declared.Title}」，"
                    + $"而实物第 {index} 位是「{titles[index]}」：列序被改而声明没跟上，Ctrl+C 抄出去的是邻列。");
            }
        }

        return problems;
    }

    /// <summary>某一屏那个清单字段按加入次序的列标题（<c>_list.Columns.Add("类型", 100)</c> 里的第一个参数）。</summary>
    private static List<string> ColumnTitles(string text, string field)
    {
        var pattern = new Regex(
            @"^\s*" + Regex.Escape(field) + @"\.Columns\.Add\(""(?<title>[^""]*)""",
            RegexOptions.Compiled | RegexOptions.Multiline);
        return pattern.Matches(text).Select(match => match.Groups["title"].Value).ToList();
    }

    /// <summary>仓里当前那一份 <c>Desktop/Native</c> 源码（文件名 + 全文）。</summary>
    private static List<(string File, string Text)> Sources() =>
        Repo.NativeSources()
            .Select(source => (source.Name, source.Text))
            .ToList();
}
