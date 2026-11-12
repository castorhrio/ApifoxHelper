using System.Text.RegularExpressions;
using ApifoxHelper.Comments;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Source;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 清单列宽的单一落点与「标注」那一格的读全通路（docs/DESIGN.md §6「列表行」的列宽那一格、
/// <c>docs/CRITIQUE.md</c> 文末第 2 条：第 1、3、5 站那一族）。
/// <para>
/// <b>补的是哪个缺口</b>（第二百四十二轮，具名 pass：distill + clarify）：wine 96 档逐屏量真实控件
/// （<c>RunListColumnFit</c>）量出三件事——① 同一族内容在各屏是两个数：「标注」60 / 70 / 110、
/// 「位置（说明点）」360 / 340、「文件:行」320 / 330、「资源」260 / 240，没有一份出处；
/// ② 注释缺口那一屏「标注 ▲」「角色 ▲」排序后的表头文字 54px 加留白 12px 是 66px，
/// 而那一列当时是 60px——<b>点一次列头就把自己的表头截掉</b>；③ 带长参数名的标注是 162px
/// （「参数 cancellationToken」），三屏哪一个数都装不下，而详情区那一屏不讲它（注释缺口那屏讲
/// 符号 / 现状 / 覆盖接口 / 证据，候选那屏讲符号 / 依据 / 来源，核对那屏讲说明文字 / Swagger 位置 / 备注）——
/// 「这条注释缺在哪个参数上」在屏幕上没有一条读全的路。
/// </para>
/// <para>
/// <b>两条修法各管一件事，不混</b>：宽度收成 <see cref="ListColumnWidths"/>（一族一个数，按内容族不按表头名）；
/// 装不下的那一族把全文交给详情区（<see cref="SlotText.TagDetailLines"/>）。
/// 不加宽到「什么都装得下」是因为 ②③ 两个数 together 会把同一屏的列尾挤进横向滚动条——那一条由
/// 冒烟段按「清单可视宽减一条竖向滚动条」的预算当场判，不靠这里的算式。
/// </para>
/// </summary>
public class ListColumnWidthsTests
{
    private static string NativeFile(string name) => Repo.Source("Desktop", "Native", name);

    private static string Caliber => NativeFile("ListColumnWidths.cs");

    /// <summary>登记在册的四族表头名（就地数字这条路不得复活）；「角色」不是同名族，但被 ② 的下界顶到，一并登记。</summary>
    private static readonly string[] RegisteredHeaders = ["位置（说明点）", "标注", "角色", "文件:行", "资源"];

    /// <summary>
    /// 每一族的宽度都不低于「排序之后的表头文字 + 留白」那条下界：低于它，点一次列头就截自己的表头。
    /// 下界本身是量出来的（54 + 12），不是估的；这里判的是「没有一族掉到它下面」。
    /// </summary>
    [Fact]
    public void EveryFamily_ClearsTheSortedHeaderFloor()
    {
        Assert.True(ListColumnWidths.SortedHeaderFloor > 0);
        foreach (var (name, width) in new (string, int)[]
                 {
                     ("SlotLocation", ListColumnWidths.SlotLocation),
                     ("SlotTag", ListColumnWidths.SlotTag),
                     ("SlotRole", ListColumnWidths.SlotRole),
                     ("FileLine", ListColumnWidths.FileLine),
                     ("ResourceKey", ListColumnWidths.ResourceKey),
                 })
        {
            Assert.True(width >= ListColumnWidths.SortedHeaderFloor,
                $"{name} = {width}px 低于排序后表头那一条下界 {ListColumnWidths.SortedHeaderFloor}px——点列头会把表头截掉。");
        }
    }

    /// <summary>
    /// 四族在各屏是同一个数：接线点逐文件逐族对上（Present 型断言，缺一条即红）。
    /// 「状态」「依据」「来源」这类本屏独有的一族不在其中——把它们也搬进口径等于给没有第二个用户的数字造词汇表。
    /// </summary>
    [Fact]
    public void EveryRegisteredColumn_TakesItsWidthFromTheCaliber()
    {
        var scan = NativeFile("ScanResultsDialog.cs");
        Assert.Equal(1, Count(scan, @"Columns\.Add\(""位置（说明点）"", ListColumnWidths\.SlotLocation\)"));
        Assert.Equal(1, Count(scan, @"Columns\.Add\(""标注"", ListColumnWidths\.SlotTag\)"));
        Assert.Equal(1, Count(scan, @"Columns\.Add\(""角色"", ListColumnWidths\.SlotRole\)"));
        Assert.Equal(1, Count(scan, @"Columns\.Add\(""文件:行"", ListColumnWidths\.FileLine\)"));

        var candidates = NativeFile("CandidatesDialog.cs");
        Assert.Equal(1, Count(candidates, @"Columns\.Add\(""位置（说明点）"", ListColumnWidths\.SlotLocation\)"));
        Assert.Equal(1, Count(candidates, @"Columns\.Add\(""标注"", ListColumnWidths\.SlotTag\)"));

        var propagation = NativeFile("PropagationDialog.cs");
        Assert.Equal(1, Count(propagation, @"Columns\.Add\(""标注"", ListColumnWidths\.SlotTag\)"));

        var evidence = NativeFile("EvidenceDialog.cs");
        Assert.Equal(1, Count(evidence, @"Columns\.Add\(""文件:行"", ListColumnWidths\.FileLine\)"));

        var decisions = NativeFile("DecisionsDialog.cs");
        Assert.Equal(1, Count(decisions, @"Columns\.Add\(""资源"", ListColumnWidths\.ResourceKey\)"));

        // 同一个对话框里的两张清单：同一族不能一张经口径、另一张就地写。
        var sync = NativeFile("SyncPreviewDialog.cs");
        Assert.Equal(2, Count(sync, @"Columns\.Add\(""资源"", ListColumnWidths\.ResourceKey\)"));
    }

    /// <summary>
    /// 就地数字这条路不得复活：界面源码里再出现 <c>Columns.Add("&lt;登记族表头&gt;", &lt;数字&gt;)</c> 即红。
    /// 判据按行首取，注释里的引用不算（这一族的老毛病：子串断言被注释行骗过）。
    /// </summary>
    [Fact]
    public void RegisteredFamilies_NeverTakeAnInPlaceWidthAgain()
    {
        var offenders = new List<string>();
        foreach (var path in Directory.GetFiles(Path.Combine(Repo.Root, "Desktop", "Native"), "*.cs"))
        {
            var name = Path.GetFileName(path);
            if (name == "ListColumnWidths.cs") continue;
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var match = Regex.Match(lines[index], @"^\s*\w+\.Columns\.Add\(""([^""]+)"", \d+\)");
                if (!match.Success) continue;
                if (!RegisteredHeaders.Contains(match.Groups[1].Value, StringComparer.Ordinal)) continue;
                offenders.Add($"{name}:{index + 1} {lines[index].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "登记在册的族又被就地写了一次列宽——一族一个数只在 ListColumnWidths 那一处（DESIGN.md §6「列宽」）：\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// 同名不等于同族：这两条路都要挡住。
    /// ① 口径里不得出现「按表头名查宽」的落点（那会把「审阅状态」「分支」「接口」「状态」这些
    ///    同名不同族的一起折掉，挤掉某一屏那一格的真实内容）；
    /// ② 「审阅状态」在两屏仍各是各自的数（候选最长是「证据已变化」五字、契约最长三字），
    ///    「分支」在配置侧是分支名、在契约清单里是分支数——本轮一个字都没动它们。
    /// </summary>
    [Fact]
    public void SameNameDifferentFamily_IsNotFolded()
    {
        Assert.False(Regex.IsMatch(Caliber, @"WidthOf\(|Dictionary<string, int>"),
            "ListColumnWidths 里出现了按表头名查宽的路径——同名不同族的列会被顺手折掉，宽度只由「这一格装哪一族」决定。");

        Assert.Equal(1, Count(NativeFile("CandidatesDialog.cs"), @"Columns\.Add\(""审阅状态"", 110\)"));
        Assert.Equal(1, Count(NativeFile("ContractsDialog.cs"), @"Columns\.Add\(""审阅状态"", 90\)"));
        Assert.Equal(1, Count(NativeFile("ProjectsPage.cs"), @"Columns\.Add\(""分支"", 100\)"));
        Assert.Equal(1, Count(NativeFile("ContractsDialog.cs"), @"Columns\.Add\(""分支"", 56\)"));
    }

    /// <summary>
    /// 「标注」那一格放不下带长参数名的标注，全文由详情区那一行交出：三种取法各对一次，
    /// 保证详情那一行与清单那一格是同一串字（两屏说两种话是这一族反复踩过的坑）。
    /// </summary>
    [Fact]
    public void TagDetailLine_HandsOverTheSameTextTheCellShows()
    {
        var slot = new DocSlot
        {
            Id = "s",
            SymbolId = "M:A.B(C)",
            Display = "GET /api/probe",
            Tag = "param",
            ParameterName = "cancellationToken",
            Status = "missing",
            File = "Probe.Api/Controllers/ProbeController.cs",
            Line = 12,
            Role = "endpoint",
        };
        var line = SlotText.TagDetailLine(SlotText.Tag(slot));
        Assert.Equal("标注：参数 cancellationToken", line);
        // 被清单那一格裁掉的正是参数名：详情这一行必须整串交出，不是只交「参数」两个字。
        Assert.Contains("cancellationToken", line, StringComparison.Ordinal);

        var candidate = new CommentCandidate { Id = "c", Tag = "param", ParameterName = "pageSize" };
        Assert.Equal("标注：参数 pageSize", SlotText.TagDetailLine(QueueText.CandidateTag(candidate)));

        Assert.Equal("标注：返回", SlotText.TagDetailLine(PropagationEntries.TagText("returns", null)));
        Assert.Equal("标注：说明", SlotText.TagDetailLine(SlotText.Tag(new DocSlot { Id = "s2", Tag = "summary" })));
    }

    /// <summary>
    /// 那一格本身没有内容时详情区一行都不给：写一句「标注：」后面跟着空，是把「这一条没有标注」
    /// 说成「读到了但读不出」（与模块行只给名称、不给空括号「订单（）」同一纪律）。
    /// </summary>
    [Fact]
    public void TagDetailLines_GivesNoLineWhenTheCellHasNothing()
    {
        foreach (var empty in new[] { "", "   ", "\t" })
        {
            Assert.Equal("", SlotText.TagDetailLine(empty));
            Assert.Empty(SlotText.TagDetailLines(empty));
        }

        Assert.Equal(["标注：参数 id"], SlotText.TagDetailLines("参数 id").ToArray());
    }

    /// <summary>
    /// 三屏的详情区接线：每一屏经同一处口径、用那一屏自己那一格的取法。
    /// 少了任何一屏，那一屏就仍只有一句被裁的清单格；取法换错（比如候选那屏改用 SlotText.Tag）也会红——
    /// 那等于详情讲的不是这一行在清单上的那句话。
    /// </summary>
    [Fact]
    public void ThreeScreens_HandTheTagOverToTheirDetailArea()
    {
        Assert.Equal(1, Count(NativeFile("ScanResultsDialog.cs"),
            @"SlotText\.TagDetailLines\(SlotText\.Tag\(slot\)\)"));
        Assert.Equal(1, Count(NativeFile("CandidatesDialog.cs"),
            @"SlotText\.TagDetailLines\(QueueText\.CandidateTag\(candidate\)\)"));
        Assert.Equal(1, Count(NativeFile("PropagationDialog.cs"),
            @"SlotText\.TagDetailLines\(PropagationEntries\.TagText\(entry\.Tag, entry\.ParameterName\)\)"));
        // 前缀字面只许在口径里出现一次：三屏不各拼一遍「标注：」。
        Assert.Equal(1, Count(NativeFile("SlotText.cs"), @"const string TagDetailCaption"));
        foreach (var file in new[] { "ScanResultsDialog.cs", "CandidatesDialog.cs", "PropagationDialog.cs" })
        {
            Assert.False(NativeFile(file).Contains("\"标注：", StringComparison.Ordinal),
                $"{file} 自己拼了一次「标注」前缀——前缀只在 SlotText.TagDetailCaption 那一处（两处各拼一份必然有一处先漂）。");
        }
    }

    /// <summary>
    /// 量测那一段删不掉：注册在 <c>Main</c> 里、按真实控件读 <c>ColumnHeader.Width</c>、
    /// 预算取「清单可视宽减一条系统给的竖向滚动条」（本段构造的是空清单，那一刻滚动条还没出现，
    /// 拿满宽当预算会把「有行之后列尾被藏」读成「放得下」）。
    /// </summary>
    [Fact]
    public void TheMeasurementSegment_IsRegisteredAndJudgesTheBudgetAfterTheScrollbar()
    {
        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        Assert.Contains("Section(\"同名表头列宽（四族列在各屏的声明宽、列宽合计对清单可视宽、表头与样例单元格文字，真实控件）\", nameof(RunListColumnFit));",
            smoke, StringComparison.Ordinal);
        Assert.Contains("list.Columns.Cast<ColumnHeader>().Select(column => column.Width)", smoke, StringComparison.Ordinal);
        Assert.Contains("SystemInformation.VerticalScrollBarWidth", smoke, StringComparison.Ordinal);
        Assert.Contains("sum > budget", smoke, StringComparison.Ordinal);
        // 不许悄悄把基准换回满宽：那条判据写的是 budget，出现「对可视宽」的旧写法即红。
        Assert.DoesNotContain("sum > visible", smoke, StringComparison.Ordinal);
    }

    /// <summary>
    /// 设计基线那一格与 §8 的实现差距在位：条款在文档里、由本轮那条门看过（这一族的老规矩，防止代码改了、文档没跟上）。
    /// </summary>
    [Fact]
    public void DesignDoc_KeepsTheFamilyWidthClause()
    {
        Assert.Contains("同一族内容只许一个数", Repo.DesignSection(6), StringComparison.Ordinal);
        Assert.Contains("ListColumnWidths", Repo.DesignSection(6), StringComparison.Ordinal);
        Assert.Contains("ListColumnWidths", Repo.DesignSection(8), StringComparison.Ordinal);
    }

    /// <summary>
    /// 本轮未扩面（不是漏掉）：口径只登记五族加一条下界，共六个 <c>public const</c>；
    /// 一屏独用、又没有跨屏同名族的其他列仍留在各屏自己那一句里。把它们一并搬进来等于给
    /// 没有第二个用户的数字造一份词汇表——那正是 §1「没有一处是选过的」那种脚手架感。
    /// 抽样判几处就地数字仍在：被顺手「统一」进口径即红。
    /// </summary>
    [Fact]
    public void SingleScreenColumns_StayOutOfTheCaliber()
    {
        Assert.Equal(6, Regex.Matches(Caliber, @"public const int \w+ =").Count);

        Assert.Equal(1, Count(NativeFile("CapabilitiesDialog.cs"), @"Columns\.Add\(""检查项"", 200\)"));
        Assert.Equal(1, Count(NativeFile("ApplyPlanDialog.cs"), @"Columns\.Add\(""源码文件"", 400\)"));
        Assert.Equal(1, Count(NativeFile("EvidenceDialog.cs"), @"Columns\.Add\(""证据摘要"", 470\)"));
        // 核对传播那一屏首列「位置」与注释缺口的「位置（说明点）」是两个名字：本轮不折它们（改名是另一件事，见 CRITIQUE 第 7 站）。
        Assert.Equal(1, Count(NativeFile("PropagationDialog.cs"), @"Columns\.Add\(""位置"", 330\)"));
        // 注释缺口那一屏的「状态」列同理：本屏独有的一族，留在原地，它也是那一屏预算里唯一不被口径管的那一列。
        Assert.Equal(1, Count(NativeFile("ScanResultsDialog.cs"), @"Columns\.Add\(""状态"", 110\)"));
    }

    private static int Count(string text, string pattern) => Regex.Matches(text, pattern).Count;
}
