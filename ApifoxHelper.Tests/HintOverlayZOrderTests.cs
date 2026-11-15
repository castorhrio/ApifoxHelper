using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 叠层 z 序对账（DESIGN.md §6「清单宿主与空态叠放」的对话框那一半，第三百四十七轮普查）。
/// <para>
/// 判的是这一件事：对话框里<strong>各自手拼</strong>的清单宿主，形状都是「宿主先加 <c>Ui.Hairline(清单)</c>、
/// 随后加一句提示」。包裹面板是 <c>Dock = Fill</c> 且 <c>BackColor = Line</c>——不透明、填满宿主；
/// 提示也是 <c>Dock = Fill</c>，两者占同一块矩形，谁在上面只由 z 序决定：
/// <c>Control.Controls</c> 的索引 0 才是 z 序最前（<c>BringToFront()</c> 即 <c>SetChildIndex(this, 0)</c>），
/// 后加入的排在后面。于是「不显式提到最前」的那一屏，读失败说明与「筛到零行」那句话都被整块盖住，
/// 屏上只剩一片发丝底色。
/// </para>
/// <para>
/// 这一族的实测口径（Linux 容器内 wine 10.0 + Xvfb 探针，复刻同一形状：host(Bg) → 发丝边 → 提示，
/// 两者同 <c>Dock = Fill</c>）：不加那一句时 <c>GetChildAtPoint</c> 落在发丝边面板上、中心像素 <c>#E3E8F0</c>
/// （= <c>UiTokens.Line</c>，提示一个字都看不见）；把提示 <c>BringToFront()</c> 之后同一坐标 <c>GetChildAtPoint</c>
/// 落在提示上、中心像素 <c>#F5F7FB</c>（= <c>UiTokens.Bg</c>）。这一跑不依赖真机，因此这一族的观感
/// <strong>不再挂在 G-D5 等真机复核</strong>——此前 <c>FilteredEmptyStateTests</c> 把「筛到零行的那句话显示出来」
/// 整条留给真机，读的是 Linux 无 WinForms 运行时；wine 能量，量到的结果是它没显示。
/// </para>
/// <para>
/// 为什么不是恒等式门（<c>docs/NOTES.md</c> 五步判法第 ②③ 步）：这条门断言的三个名字——站点（哪个宿上加了哪个兄弟）、
/// 停靠边（<c>DockStyle</c> 取自那个字段的声明或赋值）、z 序那一句——定义都在<strong>被它判的产品源码</strong>里，
/// 不在本测试里；登记表的每一项都要在源码找到对应物，源码里每一处对应物也都要在登记表里，两侧对账、随实物一起长。
/// 新增一处手拼叠层而没提到最前，或把某处 <c>Dock = Top</c> 改成 <c>Fill</c> 而没跟上 z 序那一句，当场红。
/// </para>
/// </summary>
public class HintOverlayZOrderTests
{
    /// <summary>
    /// 普查口径：从 <c>host.Controls.Add(Ui.Hairline(…))</c> 起，到这台宿主自己被装进父级（或下一台宿主起头）为止，
    /// 中间每一次 <c>host.Controls.Add(兄弟)</c> 都算一处叠层站点。
    /// </summary>
    private const int ScanWindow = 40;

    /// <summary>与发丝边同矩形的叠层（<c>Dock = Fill</c>）：必须显式提到最前。</summary>
    private static readonly (string File, string Host, string Overlay)[] OverlappingSites =
    [
        ("ApplyPlanDialog.cs", "listHost", "_hint"),
        ("BuildLogDialog.cs", "outputHost", "_hint"),
        ("CandidatesDialog.cs", "listHost", "_hint"),
        ("CheckResultsDialog.cs", "listHost", "_hint"),
        ("ContractsDialog.cs", "listHost", "_hint"),
        ("DecisionsDialog.cs", "listHost", "_hint"),
        ("EndpointTraceDialog.cs", "listHost", "_hint"),
        ("EvidenceDialog.cs", "listHost", "_hint"),
        ("JournalDialog.cs", "listHost", "_hint"),
        ("PropagationDialog.cs", "listHost", "_hint"),
        ("ReportsDialog.cs", "previewHost", "_hint"),
        ("ScanResultsDialog.cs", "listHost", "_hint"),
        ("SyncPreviewDialog.cs", "listHost", "_selectHint"),
        ("SyncPreviewDialog.cs", "actionHost", "_actionHint"),
    ];

    /// <summary>
    /// 同一宿主里的边缘叠层（<c>Dock = Top</c>）：占的是自己那一条，与发丝边不同矩形，不会被盖住，
    /// 也就不该出现 <c>BringToFront()</c>——它在这里既没用，又会让下一轮把「同矩形」这件事读错。
    /// </summary>
    private static readonly (string File, string Host, string Overlay)[] EdgeSites =
    [
        ("CapabilitiesDialog.cs", "resultHost", "_headline"),
        ("CapabilitiesDialog.cs", "resultHost", "_resultHint"),
        ("SyncPreviewDialog.cs", "actionHost", "actionCaption"),
    ];

    /// <summary>扫描到的一处叠层站点：文件、宿主变量、兄弟控件、以及这一行在文件里的行号（进断言消息）。</summary>
    private sealed record Site(string File, string Host, string Overlay, int Line);

    private static IEnumerable<Site> Scan()
    {
        foreach (var source in Repo.NativeSources())
        {
            // Ui.cs 里那一处是工厂本体（ListHost），z 序在工厂里定，由 ListHostTests 钉住，不在本普查范围。
            if (source.IsUi) continue;
            var lines = source.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var hairline = Regex.Match(lines[i], @"^\s*(\w+)\.Controls\.Add\((?:Ui\.)?Hairline\(.+\)\);$");
                if (!hairline.Success) continue;
                var host = hairline.Groups[1].Value;
                for (var j = i + 1; j < Math.Min(i + ScanWindow, lines.Length); j++)
                {
                    // 宿主自己被装进父级：这一块构造到此为止。
                    if (Regex.IsMatch(lines[j], @"^\s*\w*\.?Controls\.Add\(" + host + @"(,|)\);") || lines[j].Contains($"Controls.Add({host})")) break;
                    var sibling = Regex.Match(lines[j], @"^\s*" + host + @"\.Controls\.Add\((\w+)\);");
                    if (sibling.Success) yield return new Site(source.Name, host, sibling.Groups[1].Value, j + 1);
                }
            }
        }
    }

    /// <summary>
    /// 那一处叠层实际停靠在哪一边：显式赋值优先（<c>_resultHint.Dock = DockStyle.Top;</c> 这种在构造里改的），
    /// 否则取声明处的对象初始化器（<c>private readonly Label _hint = new() { Dock = DockStyle.Fill, … }</c>）。
    /// 读不到就红——「判不出停靠边」不能当成「不算叠层」放过去。
    /// </summary>
    private static string DockOf(string file, string overlay)
    {
        var text = Repo.Source("Desktop", "Native", file);
        var assigned = Regex.Match(text, @"(?m)^\s*" + Regex.Escape(overlay) + @"\.Dock\s*=\s*DockStyle\.(\w+);");
        if (assigned.Success) return assigned.Groups[1].Value;

        var declared = Regex.Match(text, @"(?m)^\s*(?:private|internal|protected)?\s*(?:readonly\s+)?[\w<>?., ]+\s" +
                                      Regex.Escape(overlay) + @"\s*=\s*new[^(]*\([^)]*\)?\s*\{");
        if (!declared.Success)
        {
            declared = Regex.Match(text, @"(?m)^\s*var\s+" + Regex.Escape(overlay) + @"\s*=\s*new[^{]*\{");
        }

        Assert.True(declared.Success, $"{file} 里读不到 {overlay} 的声明块，判不出它停靠在哪一边。");
        var block = text.Substring(declared.Index, Math.Min(1200, text.Length - declared.Index));
        var dock = Regex.Match(block, @"Dock\s*=\s*DockStyle\.(\w+)");
        Assert.True(dock.Success, $"{file} 里 {overlay} 的声明块没有 Dock，判不出它是否与发丝边同矩形。");
        return dock.Groups[1].Value;
    }

    private static string Describe(IEnumerable<Site> sites)
        => string.Join(", ", sites.Select(s => $"{s.File}:{s.Line} {s.Host}→{s.Overlay}").OrderBy(x => x, StringComparer.Ordinal));

    [Fact]
    public void Census_SitesOnDisk_MatchTheRegisteredOverlappingList()
    {
        // 两侧对账：磁盘上扫到的每一处叠层，要么在「同矩形」登记表、要么在「边缘」登记表；两张表里每一项都得在磁盘上找到。
        var found = Scan().ToList();
        var registered = OverlappingSites.Concat(EdgeSites)
            .Select(s => $"{s.File} {s.Host}→{s.Overlay}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        var scanned = found
            .Select(s => $"{s.File} {s.Host}→{s.Overlay}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(registered, scanned.Distinct(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void OverlappingSites_AreActuallyFillDocked_AndAreTheOnesCoveredByTheHairline()
    {
        // 登记表把「同矩形」这件事也钉在实物上：这一处叠层的停靠边必须真是 Fill，否则它不该要求 BringToFront，
        // 上一轮把它登记成 Fill 就是抄错的——改停靠边的人得同时改登记表，两侧一起动。
        foreach (var (file, _, overlay) in OverlappingSites)
        {
            Assert.Equal("Fill", DockOf(file, overlay));
        }
    }

    [Fact]
    public void EdgeSites_AreActuallyTopDocked_AndDoNotCarryTheFrontCall()
    {
        foreach (var (file, host, overlay) in EdgeSites)
        {
            Assert.Equal("Top", DockOf(file, overlay));
            // 边缘叠层不要求提到最前；留着这一句断言，是为了「登记表说它不重叠」这句话本身可核对。
            Assert.Contains($"{host}.Controls.Add({overlay});", Repo.Source("Desktop", "Native", file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryOverlappingSite_BringsTheOverlayToFrontAfterAddingIt()
    {
        foreach (var (file, host, overlay) in OverlappingSites)
        {
            var text = Repo.Source("Desktop", "Native", file);
            var added = text.IndexOf($"{host}.Controls.Add({overlay});", StringComparison.Ordinal);
            Assert.True(added >= 0, $"{file} 里找不到 {host}.Controls.Add({overlay})。");

            var front = text.IndexOf($"{overlay}.BringToFront();", StringComparison.Ordinal);
            Assert.True(front >= 0,
                $"{file}：{overlay} 与发丝边同矩形（Dock = Fill）却没提到最前——读失败说明与「筛到零行」那句话会被整块盖住，" +
                "屏上只剩一片 Line 底色（探针实测 #E3E8F0，理由见 Ui.HairlineCore 文档）。");
            Assert.True(front > added, $"{file}：{overlay}.BringToFront() 写在 {host}.Controls.Add({overlay}) 之前，加入顺序会把 z 序再排回后面。");

            // 提示初值收起、由 ShowHint 一类出口点亮：那句 BringToFront 必须写在收起之前，不依赖运行时的先后。
            var hidden = text.IndexOf($"{overlay}.Visible = false;", StringComparison.Ordinal);
            if (hidden >= added) Assert.True(front < hidden, $"{file}：{overlay}.BringToFront() 要写在 {overlay}.Visible = false 之前。");
        }
    }

    [Fact]
    public void CandidatesAndCheckResults_AndTheWriteBackAndPreviewScreens_AreAllInTheCensus()
    {
        // 演示第 3、4、5、6 站点开的就是这四屏（注释缺口 / 注释候选 / 注释回写 / 检查结果）：
        // 剧本要让「筛到零行」与「读取失败」那两句话在屏上看得见，这一处不能从普查里掉出去。
        foreach (var file in new[] { "ScanResultsDialog.cs", "CandidatesDialog.cs", "ApplyPlanDialog.cs", "CheckResultsDialog.cs" })
        {
            Assert.Contains(OverlappingSites, s => s.File == file);
        }

        // 第 7 站那两屏（差异确认、同步预览）同样在册：离线时它们给出的是那句拒绝与灰钮说明。
        Assert.Contains(OverlappingSites, s => s.File == "DecisionsDialog.cs");
        Assert.Contains(OverlappingSites, s => s.File == "SyncPreviewDialog.cs");
    }

    [Fact]
    public void HairlineDoc_RecordsTheRuleAndTheTwoMeasuredColors()
    {
        // 设计基线回写：理由写在造出这块不透明面板的那一处，13 个调用点不再各抄一遍。
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var start = ui.IndexOf("private static Panel HairlineCore", StringComparison.Ordinal);
        Assert.True(start >= 0, "Ui.HairlineCore 不存在");
        var docStart = ui.LastIndexOf("/// <summary>", start, StringComparison.Ordinal);
        Assert.True(docStart >= 0, "Ui.HairlineCore 上面没有文档注释块");
        var doc = ui[docStart..start];
        Assert.Contains("索引 0 才是 z 序最前", doc, StringComparison.Ordinal);
        Assert.Contains("#E3E8F0", doc, StringComparison.Ordinal);
        Assert.Contains("#F5F7FB", doc, StringComparison.Ordinal);
        Assert.Contains("HintOverlayZOrderTests", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignBaseline_RecordsTheOverlayCensusAndTheVerdict()
    {
        var design = Repo.DesignSection(6);
        Assert.Contains("叠层 z 序逐处对账", design, StringComparison.Ordinal);
        Assert.Contains("HintOverlayZOrderTests", design, StringComparison.Ordinal);
        // 探针那两个数进基线：下一轮再动这一族时，判「修没修好」有据可翻。
        Assert.Contains("#E3E8F0", design, StringComparison.Ordinal);
        Assert.Contains("#F5F7FB", design, StringComparison.Ordinal);
    }
}
