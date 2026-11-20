using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「降级答辩」一键复跑演示脚本的门（第一百六十七轮，简报 P0-1 的降级交付）。
/// <para>
/// <b>要解决的问题</b>：真机 0 台是环境供给缺口，本轮交不出真机三档截图；能交出的替代是
/// <b>可一键复跑的演示</b>——一条固定的 wine 三档冒烟命令 + 一份按逐屏表逐行对账出来的讲稿，
/// 让答辩现场能当场把「每一屏的主张」与「这一屏在这一档真的跑过、通过几条」对上。
/// </para>
/// <para>
/// <b>这一节凭什么不是又一次自述</b>：屏单不在脚本里抄一份，脚本从 <c>docs/NOTES.md</c> 的逐屏表读行；
/// 报告里每一段的标题带着它自己的方法名（<c>Section(title, nameof(RunXxx))</c>），
/// 表里引用的段若在程序里不存在，逐屏那一步就报「对不上」并非零退出。
/// 本文件钉住这三件事都还在：抄一份屏单即红、段名漂了即红、把「只有源码证据」的屏讲成 wine 量到即红。
/// </para>
/// <para>
/// 本文件不跑 wine（跑法是 <c>.winecheck/demo.sh</c>，产物在 <c>/tmp/ah-demo</c>）；
/// 它钉的是「脚本与逐屏表、与冒烟器引用的是同一批段」这一层。
/// </para>
/// </summary>
public class DemoScriptTests
{
    private const string SectionHeading = "## 降级答辩脚本";

    private static string Script => Repo.Source(".winecheck", "demo.sh");

    private static string Readme => Repo.Source(".winecheck", "README.md");

    private static string SmokeProgram => Repo.Source("ApifoxHelper.Smoke", "Program.cs");

    /// <summary>逐屏表的行（# / 屏 / 证据出处 / 结论口径）。列数不是 5 的行在这里就是测试失败，不是跳过。</summary>
    private static List<(string Num, string Screen, string Evidence, string Verdict)> Rows()
    {
        var notes = Repo.Source("docs", "NOTES.md").Split('\n');
        var start = Array.FindIndex(notes, l => l.StartsWith(SectionHeading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"docs/NOTES.md 缺「{SectionHeading}」一节。");
        var end = Array.FindIndex(notes, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        var rows = new List<(string, string, string, string)>();
        foreach (var line in notes[start..(end < 0 ? notes.Length : end)])
        {
            if (!Regex.IsMatch(line, @"^\| \d")) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            Assert.True(cells.Count == 5, $"逐屏表有一行不是 5 列（{cells.Count} 列）：{line[..Math.Min(40, line.Length)]}…");
            rows.Add((cells[0], cells[1], cells[3], cells[4]));
        }
        Assert.True(rows.Count >= 12, $"逐屏表只有 {rows.Count} 行（地板 12）。");
        return rows;
    }

    /// <summary>证据出处一列里引用的冒烟段名——与 demo.sh 里的 <c>grep -oE 'Run[A-Za-z0-9]+'</c> 同一取法。</summary>
    private static List<string> CitedSegments() => Rows()
        .SelectMany(r => Regex.Matches(r.Evidence, @"Run[A-Za-z0-9]+").Select(m => m.Value))
        .Distinct()
        .OrderBy(t => t, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// 屏单只有一份：脚本读逐屏表，不抄屏名。
    /// 抄一份的后果是它会漂——加了对话框、改了段名，抄的那份不会红，答辩现场按旧单讲。
    /// </summary>
    [Fact]
    public void DemoScript_TakesItsScreenListFromTheTableInsteadOfCopyingIt()
    {
        var script = Script;
        Assert.Contains(SectionHeading, script);
        Assert.Contains("docs/NOTES.md", script);
        foreach (var (_, screen, _, _) in Rows())
        {
            Assert.False(script.Contains(screen, StringComparison.Ordinal),
                $"demo.sh 里抄了屏名「{screen}」——屏单必须从 docs/NOTES.md 的逐屏表读，否则表改了脚本不会红。");
        }
    }

    /// <summary>三档缩放在同一条命令里跑完，每档各有一份报告与一份讲稿。</summary>
    [Fact]
    public void DemoScript_RunsEveryScaleAndWritesAReportAndANarrationPerScale()
    {
        var script = Script;
        Assert.Contains("TIERS=${TIERS:-96 120 192}", script);
        Assert.Contains("LogPixels /t REG_DWORD /d \"$TIER\"", script);
        Assert.Contains("\"--report=$REPORT\"", script);
        Assert.Contains("report-$TIER.txt", script);
        Assert.Contains("narration-$TIER.txt", script);
        // 前缀误用会把环境差异读成产品回归（第一百六十六轮 115 条 Error creating window handle）。
        Assert.Contains("Error creating window handle", Readme);
    }

    /// <summary>
    /// 跑完把缩放改回 100%：同一份 wine 前缀是共享的开发环境，
    /// 留在 200% 档会让下一个用别的脚本的人量到不是他要量的那一档。
    /// </summary>
    [Fact]
    public void DemoScript_RestoresTheScaleWhenItFinishes()
    {
        var script = Script;
        var restore = script.LastIndexOf("/v LogPixels /t REG_DWORD /d 96", StringComparison.Ordinal);
        var loop = script.IndexOf("for TIER in", StringComparison.Ordinal);
        Assert.True(restore > loop && loop >= 0, "演示结束没有把 LogPixels 改回 96（或在三档循环之前就改完了）。");
    }

    /// <summary>只有源码与编译证据的那几屏不许被讲成 wine 量到：脚本有这一支，表里也真有这种行。</summary>
    [Fact]
    public void DemoScript_KeepsSourceOnlyScreensOutOfTheWineClaim()
    {
        var script = Script;
        Assert.Contains("没有 wine 冒烟段", script);
        Assert.Contains("源码与编译证据", script);
        var sourceOnly = Rows().Count(r => r.Verdict.Contains("源码 + 编译证据", StringComparison.Ordinal));
        Assert.True(sourceOnly > 0, "逐屏表里已经没有「源码 + 编译证据」这一类行了——脚本那一支成了死代码，表与讲法要对齐。");
    }

    /// <summary>
    /// 对不上就大声失败：表里引用的段在报告里找不到、或某一行不是 5 列，都必须非零退出。
    /// 「静默跳过」正是第一百六十六轮验收表那条老毛病（格式错的行不进解析、整表照样绿）在这里的同一形态。
    /// </summary>
    [Fact]
    public void DemoScript_FailsLoudlyInsteadOfSkippingWhatItCannotMatch()
    {
        var script = Script;
        Assert.Contains("报告里没有这一段", script);
        Assert.Contains("不是 5 列", script);
        // 守卫得是一个真的条件：`if (n != 7 && 0)` 这种把判断调废的写法，子串匹配照样绿。
        Assert.True(Regex.IsMatch(script, @"if \(n != 7\)\s*\{"),
            "demo.sh 里没有「切出来不是 7 段就报错」这一支守卫（或被写成恒不成立的条件）。");
        Assert.Contains("exit 1", script);
        Assert.Contains("TOTAL_MISSING", script);
    }

    /// <summary>
    /// 表引用的每个段名，在冒烟器里都必须既有一个方法、又有一段带着这个 <c>nameof</c> 的标题。
    /// 段名漂了 → 脚本在报告里找不到那一段；标题没带 id → 找不到的是全部段。两种都在这一条就红，
    /// 不必等到答辩现场。
    /// </summary>
    [Fact]
    public void EveryCitedSegment_HasADefinitionAndASectionId()
    {
        var program = SmokeProgram;
        var cited = CitedSegments();
        Assert.True(cited.Count >= 12, $"逐屏表只引用到 {cited.Count} 个冒烟段（地板 12）。");
        foreach (var segment in cited)
        {
            Assert.True(program.Contains($"private static void {segment}(", StringComparison.Ordinal),
                $"逐屏表引用了 {segment}，冒烟器里没有这个方法。");
            Assert.True(Regex.IsMatch(program, $"Section\\(\"[^\"]*\", nameof\\({segment}\\)\\)"),
                $"逐屏表引用了 {segment}，但报告标题里没有它这一段（Section 的 nameof id 没了，演示脚本找不到这一段）。");
        }
    }

    /// <summary>
    /// 逐屏表每一行的「结论口径」与它引的证据必须对得上，且引的段必须真的在冒烟器 <c>Main</c> 的注册清单里
    /// （第二百五十三轮，简报 P1-2，具名 pass：harden + clarify）。
    /// <para>
    /// <b>补的是哪个缺口</b>：<see cref="EveryCitedSegment_HasADefinitionAndASectionId"/> 判的是「引了段名 → 段有方法、
    /// 有 <c>Section(…, nameof(…))</c>」，<see cref="DemoScript_KeepsSourceOnlyScreensOutOfTheWineClaim"/> 判的是
    /// 「脚本有那一支、表里真有这种行」——<b>两条都不判「这一行自己前后一致」</b>：
    /// 一行可以写着「wine 量到」却一个段都不引（讲稿会写「这一屏没有 wine 冒烟段」，与表里的结论口径互相打脸），
    /// 也可以写着「源码 + 编译证据」却引了段（讲稿会念出那一段的通过数，与「只有源码证据」矛盾）。
    /// 这一条把「主张 ↔ 证据」逐行对上，形状沿用 <c>DefenseNarrativeTests.Narrative_EveryBlockPointsAtEvidenceThatCanActuallyRun</c>
    /// 那一份（五块答辩口径），只是对象换成逐屏表。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判「wine 量到的数对不对」（那是报告与读数记录的事）；不判真机跑没跑（真机 0 台，G-D5 仍 <c>[ ]</c>）；
    /// 也不判「这一屏该不该只有源码证据」——那是人的判断，机械门只判「说了什么就得引什么」。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryRowClaimingWineMeasurement_CitesASegmentThatActuallyRuns()
    {
        var problems = RowClaimMismatches(
            Rows().Select(row => (row.Num, row.Screen, row.Evidence, row.Verdict)).ToList(),
            Repo.SmokeSectionNames());
        Assert.Empty(problems);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：四发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异、第二百四十三轮 M4 那一发假绿）。
    /// 第一发是「说 wine 量到却不引段」，第二发是「说只有源码证据却引了段」，第三发是「引的段跑不到」，
    /// 第四发是合规行（不报）。
    /// </para>
    /// </summary>
    [Fact]
    public void RowClaimGate_ReportsTheBreaksItMustCatch()
    {
        string[] registered = ["RunScanResultsInteraction"];

        // ① 说 wine 量到，却一个段都不引：讲稿会写「这一屏没有 wine 冒烟段」。
        var wineWithoutSegment = RowClaimMismatches(
            [("1", "注释缺口对话框", "构造冒烟 + `ScanService` 用例", "wine 量到")], registered);
        Assert.Single(wineWithoutSegment);
        Assert.Contains("没有引任何冒烟段", wineWithoutSegment[0], StringComparison.Ordinal);

        // ② 说只有源码与编译证据，却引了段：讲稿会念出那一段的通过数。
        var sourceOnlyWithSegment = RowClaimMismatches(
            [("2", "生成文档核对", "wine 冒烟 `RunScanResultsInteraction` 五段", "源码 + 编译证据")], registered);
        Assert.Single(sourceOnlyWithSegment);
        Assert.Contains("却引了", sourceOnlyWithSegment[0], StringComparison.Ordinal);

        // ③ 引的段不在 Main 的注册清单里：跑不到，讲稿里不会出现这一行。
        var unregistered = RowClaimMismatches(
            [("3", "注释缺口对话框", "wine 冒烟 `RunNotRegistered` 五段", "wine 量到")], registered);
        Assert.Single(unregistered);
        Assert.Contains("RunNotRegistered", unregistered[0], StringComparison.Ordinal);
        Assert.Contains("注册清单", unregistered[0], StringComparison.Ordinal);

        // ④ 合规行：说 wine 量到、引的段在注册清单里——一处都不报。
        Assert.Empty(RowClaimMismatches(
            [("4", "注释缺口对话框", "wine 冒烟 `RunScanResultsInteraction` 五段", "wine 量到")], registered));
    }

    /// <summary>
    /// 逐屏表每一行的「结论口径 ↔ 证据」对账，返回「哪几行对不上」的说明（空 = 全对上）。
    /// <para>
    /// 写成纯数据进、问题出的函数，是为了让上面那条自测能拿<b>注入过的</b>夹具喂它——
    /// 与 <c>LayoutMeasurementGateTests.Problems</c> / <c>WiringAssertionShapeTests.Problems</c> 同一形状。
    /// </para>
    /// </summary>
    private static List<string> RowClaimMismatches(
        IReadOnlyList<(string Num, string Screen, string Evidence, string Verdict)> rows,
        IReadOnlyList<string> registered)
    {
        var problems = new List<string>();
        foreach (var (num, screen, evidence, verdict) in rows)
        {
            var cited = Regex.Matches(evidence, @"Run[A-Za-z0-9]+")
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var claimsWine = verdict.Contains("wine 量到", StringComparison.Ordinal);
            var claimsSourceOnly = verdict.Contains("源码 + 编译证据", StringComparison.Ordinal);

            if (claimsWine && cited.Count == 0)
            {
                problems.Add(
                    $"逐屏表第 {num} 行「{screen}」说「wine 量到」，却没有引任何冒烟段——"
                    + "讲稿会写「这一屏没有 wine 冒烟段——只有源码与编译证据」，与表里的结论口径互相打脸。");
            }

            if (claimsSourceOnly && cited.Count > 0)
            {
                problems.Add(
                    $"逐屏表第 {num} 行「{screen}」说「源码 + 编译证据」，却引了 {string.Join(" / ", cited)}——"
                    + "讲稿会念出那一段的通过数，与「只有源码证据」矛盾。");
            }

            if (!claimsWine && !claimsSourceOnly)
            {
                problems.Add(
                    $"逐屏表第 {num} 行「{screen}」的结论口径既不说「wine 量到」、也不说「源码 + 编译证据」——"
                    + "现场念到这一行时不知道该按哪一档交代。");
            }

            foreach (var segment in cited.Where(segment => !registered.Contains(segment, StringComparer.Ordinal)))
            {
                problems.Add(
                    $"逐屏表第 {num} 行「{screen}」引用了 {segment}，但它不在冒烟器 Main 的注册清单里——"
                    + "这一段跑不到，讲稿里不会出现这一行，照着念就是空手。");
            }
        }

        return problems;
    }

    /// <summary>报告里真的带着段名 id：否则上面那条只是源码切片，跑起来仍然对不上。</summary>
    [Fact]
    public void SmokeProgram_WritesTheSegmentIdIntoTheSectionTitle()
    {
        var start = SmokeProgram.IndexOf("private static void Section(string title, string id)", StringComparison.Ordinal);
        Assert.True(start >= 0, "冒烟器里没有 Section(title, id)。");
        // 取到第一个顶格 `}` 为止：往后数固定长度会吃到紧随其后的方法，判到的就不是这一段的接线了。
        var end = SmokeProgram.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "Section 方法体切不出边界。");
        var body = SmokeProgram[start..end];
        Assert.Contains("$\"== {title}[{id}] ==\"", body);
        // 标题行必须同时进控制台与报告：报告才是答辩现场念的那一份。
        // 按代码行判（`// _lines.Add(line);` 这种注释掉的写法不算接上——本仓第二十七轮的同一族盲区）。
        Assert.True(HasCodeLine(body, "Console.WriteLine(line);"), "Section 没有把标题行打到控制台。");
        Assert.True(HasCodeLine(body, "_lines.Add(line);"), "Section 没有把标题行写进报告——报告里没有段名 id，演示脚本找不到那一段。");
    }

    /// <summary><paramref name="source"/> 里是否存在以这行代码打头的**代码行**（注释行不算）。</summary>
    private static bool HasCodeLine(string source, string code) => CodeScan.HasLine(source, code);

    /// <summary>
    /// 上面那一行的自测（第一百六十八轮，简报 P0-2）：第一百六十七轮只把「行首 <c>//</c>」判掉了，
    /// <c>/* … */</c> 区间里的同一行代码照样骗得过子串匹配——这一族第三次犯的正是这件事。
    /// 判定不许靠「反正没人这么写」：这里就写一份给他判。
    /// </summary>
    [Fact]
    public void HasCodeLine_IgnoresBlockCommentedLines()
    {
        Assert.False(HasCodeLine("/* Console.WriteLine(line); */", "Console.WriteLine(line);"));
        Assert.False(HasCodeLine("/*\nConsole.WriteLine(line);\n*/", "Console.WriteLine(line);"));
        Assert.False(HasCodeLine("// Console.WriteLine(line);", "Console.WriteLine(line);"));
        Assert.True(HasCodeLine("Console.WriteLine(line);", "Console.WriteLine(line);"));
        // 注释收尾之后的代码仍然算代码：判掉的是那一段注释，不是这一行从此作废。
        Assert.True(HasCodeLine("/* 说明 */ _lines.Add(line);", "_lines.Add(line);"));
    }

    /// <summary>
    /// 「屏单只有一份」钉到<b>实现形状</b>（第一百六十八轮，简报 P1-1）。
    /// <para>
    /// <see cref="DemoScript_TakesItsScreenListFromTheTableInsteadOfCopyingIt"/> 判的是结果（脚本里没有第二个屏名），
    /// 屏名与表恰好一致时分不出「读表」与「抄了一份还没改」。这一条判机制：
    /// 必须真有一段 <c>awk</c> 从 <c>$NOTES</c> 读行，且必须真有一个制表符分隔的 <c>while read</c> 行循环在消费它。
    /// </para>
    /// </summary>
    [Fact]
    public void DemoScript_ActuallyReadsTheTableInsteadOfListingScreens()
    {
        var script = Script;
        var readsTheTable = 0;
        for (var open = script.IndexOf("awk '", StringComparison.Ordinal); open >= 0;
             open = script.IndexOf("awk '", open + 5, StringComparison.Ordinal))
        {
            // awk 程序是一段单引号串（本仓这两段内部都不用单引号），收尾引号之后紧跟的就是它读的文件。
            var close = script.IndexOf('\'', open + 5);
            var stop = script.IndexOf(')', close);
            if (close < 0 || stop < 0) continue;
            if (script[open..stop].Contains("$NOTES", StringComparison.Ordinal)) readsTheTable++;
        }

        Assert.True(readsTheTable >= 1, "脚本里没有任何一段 awk 是从 $NOTES 读行的——那份屏单是抄的，表改了不会红。");
        Assert.Contains(@"while IFS=$'\t' read -r", script);
        Assert.Contains("<<<\"$ROWS\"", script);
    }

    /// <summary>脚本结尾那句固定话：模拟器证据不是真机结果，到期日与 G-D5 缺口都要念出来。</summary>
    [Fact]
    public void DemoScript_EndsWithTheHonestCaliber()
    {
        var script = Script;
        Assert.Contains("不是 Windows 真机结果", script);
        Assert.Contains("2026-11-20", script);
        Assert.Contains("G-D5 未达成（环境供给缺口）", script);
    }

    /// <summary>
    /// 念出来的数必须出自当前源码——但判据用的是<b>源码内容指纹</b>，不是 mtime。
    /// <para>
    /// 本轮先按 mtime 建过一次这条门，然后当场被它自己锁死：发布产物是 obj 的硬链接，增量构建不刷新
    /// 时间戳，于是脚本打印的那句「重发再跑」做完了条件还成立——一个解不开的闸门比没有更糟，
    /// 因为它把唯一那条诚实的路（承认这份产物不属于当前代码）也一起堵成了「绕过脚本」。
    /// 教训不是「检查写错了」，而是<b>检查与它给的补救必须一起验</b>。
    /// </para>
    /// </summary>
    [Fact]
    public void DemoScript_FailsWhenTheSmokeBuildIsOlderThanTheSources()
    {
        var script = Script;

        // 判定是一份内容指纹（sha1 之 sha1），只此一处；mtime 那一种取法不得复活。
        Assert.Contains("source_fingerprint() {", script, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(script, @"source_fingerprint\(\) \{"));
        Assert.Contains("| LC_ALL=C sort -z | xargs -0 sha1sum | sha1sum", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-newer \"$SMOKE_EXE\"", script, StringComparison.Ordinal);
        // 测试工程不编进这份产物：改了它不该要求重发冒烟器（否则每轮都会被自己的测试改动挡住）。
        Assert.Contains(@"-not -path './ApifoxHelper.Tests/*'", script, StringComparison.Ordinal);
        // 第三百九十九轮：参与哈希的是**仓库内相对路径**（先 cd 到仓库根、`find .`），不是绝对路径——
        // 旧取法把 clone 路径哈希进去，同一版源码换一个目录 clone 出来就算出另一个指纹。
        // 这一条判的是「取法的形状」；「两个不同路径算出同一个值」由
        // ReadingConsistencyTests.SourceFingerprint_IsTheSameForTheSameSourceAtTwoDifferentPaths 真跑一遍判。
        Assert.Contains(@"cd ""$REPO"" && find .", script, StringComparison.Ordinal);
        Assert.DoesNotContain(@"-not -path ""$REPO/", script, StringComparison.Ordinal);
        // bin/obj 必须排掉，否则构建产物自己进指纹。
        Assert.Contains("-name bin -prune", script, StringComparison.Ordinal);
        Assert.Contains("-name obj -prune", script, StringComparison.Ordinal);

        // PUBLISH=1 那一支要真的强制重建，并把指纹写到产物旁边（不写就永远比不上一致）。
        var heal = script.IndexOf(@"if [ ""$PUBLISH"" = ""1"" ]", StringComparison.Ordinal);
        var healEnd = script.IndexOf("\nfi\n", heal, StringComparison.Ordinal);
        Assert.True(heal >= 0 && healEnd > heal, "PUBLISH=1 那一支切不出块边界。");
        var healBlock = script[heal..healEnd];
        Assert.Contains("-t:Rebuild", healBlock, StringComparison.Ordinal);
        Assert.Contains("dotnet publish", healBlock, StringComparison.Ordinal);
        Assert.Contains("source_fingerprint > \"$STAMP\"", healBlock, StringComparison.Ordinal);

        // 拦下来的两条分支都要给可执行补救，且非零退出。
        Assert.Contains(@"elif [ ! -f ""$STAMP"" ]", script, StringComparison.Ordinal);
        Assert.Contains(@"elif [ ""$(source_fingerprint)"" != ""$(cat ""$STAMP"")"" ]", script, StringComparison.Ordinal);
        var stop = script.IndexOf(@"elif [ ""$(source_fingerprint)""", StringComparison.Ordinal);
        var stopEnd = script.IndexOf("\nfi\n", stop, StringComparison.Ordinal);
        var stopBlock = script[stop..stopEnd];
        Assert.Contains("exit 1", stopBlock, StringComparison.Ordinal);
        Assert.Contains("PUBLISH=1 bash .winecheck/demo.sh", stopBlock, StringComparison.Ordinal);
        Assert.Contains("属于上一次那版代码", script, StringComparison.Ordinal);
    }

    /// <summary>使用说明在位：README 里必须有「演示与录制口径」这一节，并点到 demo.sh 与三档讲稿。</summary>
    [Fact]
    public void Readme_DocumentsTheDemoAndTheRecordingCaliber()
    {
        Assert.Contains("demo.sh", Readme);
        Assert.Contains("演示与录制口径", Readme);
        Assert.Contains("narration", Readme);
        Assert.Contains("逐屏表", Readme);
    }

    // ── 第三百八十轮（简报 P0-1）：把「降级答辩」从脚本升级为一页不跑 wine 也能照念的成品讲稿 ──

    /// <summary>成品讲稿那一页（不跑 wine 也留在仓里，随时能翻出来念）。</summary>
    private static string LecturePage => Repo.Source(".winecheck", "答辩讲稿.md");

    /// <summary>
    /// 讲稿页在位、且五块骨架齐（开场口径 / 当天四步 / 三档读数 / 逐屏一句话 / 结尾口径）。
    /// <para>
    /// <b>补的是哪个缺口</b>：此前能照念的讲稿只在运行时生成——<c>demo.sh</c> 跑一次 wine 才在
    /// <c>/tmp/ah-demo/narration-*.txt</c> 落一份，机器一关就没；而当天从 Windows 侧进来时那四步是
    /// 第一次真跑（见 NOTES「降级答辩脚本」敞口那一段），wine 或 Xvfb 不在场现场手里就没有屏序。
    /// 这一页就是那段路的替代。这条门判它<b>在不在、五块齐不齐</b>，并点名四步各自跑的脚本，
    /// 免得当天念到「第几步跑什么」时这一页给不出名字。
    /// </para>
    /// </summary>
    [Fact]
    public void LecturePage_ExistsAndCarriesTheReadAloudBlocks()
    {
        var path = System.IO.Path.Combine(Repo.Root, ".winecheck", "答辩讲稿.md");
        Assert.True(System.IO.File.Exists(path),
            "缺 .winecheck/答辩讲稿.md——真机不可得时现场没有一份不跑 wine 也能照念的成品讲稿。");
        var page = LecturePage;
        foreach (var heading in new[] { "## 固定开场口径", "## 当天四步", "## 三档读数", "## 逐屏一句话讲稿", "## 结尾固定一句" })
        {
            Assert.Contains(heading, page, StringComparison.Ordinal);
        }

        foreach (var script in new[] { "defense-day.sh", "due-date.sh", "preflight.sh", "demo.sh" })
        {
            Assert.Contains(script, page, StringComparison.Ordinal);
        }
    }

    /// <summary>README 的「演示与录制口径」要点到这一页，否则现场的人不知道有一份不跑 wine 也能念的成品。</summary>
    [Fact]
    public void Readme_PointsAtTheLecturePage()
    {
        Assert.Contains("答辩讲稿.md", Readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// 逐屏那一节的屏名与屏数，与 <c>docs/NOTES.md</c> 逐屏表<b>逐字锁死</b>：少一屏、多一屏、重名、
    /// 把源码证据屏念成 wine 量到、引了跑不到的段、某屏没交代「缺什么」——六样各报一处。
    /// <para>
    /// <b>凭什么不是又一次自述</b>：这一页是逐屏表的「念稿展开」，不是第二份屏单；「屏单只有一份」这条纪律
    /// 对脚本是靠运行时读表实现的，对静态页只能靠<b>测试时把两处的屏集合对齐</b>来兑现——表改了这页没跟上即红。
    /// </para>
    /// </summary>
    [Fact]
    public void LecturePage_ScreensStayInLockstepWithTheTable()
    {
        Assert.Empty(LectureProblems(
            LectureRows(),
            Rows().Select(r => (r.Screen, r.Verdict)).ToList(),
            Repo.SmokeSectionNames()));
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：六发注入各喂一次，每一发都必须报出自己那一处（最后一发合规、不报）。
    /// </summary>
    [Fact]
    public void LectureScreenGate_ReportsTheDriftItMustCatch()
    {
        string[] registered = ["RunScanResultsInteraction"];
        (string Screen, string Verdict)[] table =
        [
            ("注释缺口对话框", "wine 量到"),
            ("同步预览与执行", "源码 + 编译证据"),
        ];

        // ① 少一屏：表里有「同步预览与执行」，页上没有。
        var missing = LectureProblems(
            [("注释缺口对话框", "`RunScanResultsInteraction`", "真机未复核（G-D5）")], table, registered);
        Assert.Contains(missing, p => p.Contains("同步预览与执行", StringComparison.Ordinal)
                                      && p.Contains("这一页没有", StringComparison.Ordinal));

        // ② 多一屏：页上写了「检查任务页」，表里没有——凭空多写了屏单上没有的东西。
        var extra = LectureProblems(
            [("注释缺口对话框", "`RunScanResultsInteraction`", "真机未复核"), ("检查任务页", "只有源码证据", "真机未复核")],
            table, registered);
        Assert.Contains(extra, p => p.Contains("检查任务页", StringComparison.Ordinal)
                                    && p.Contains("多出", StringComparison.Ordinal));

        // ③ 重名：同一屏写两遍（屏数就对不上表了）。
        var dup = LectureProblems(
            [("注释缺口对话框", "`RunScanResultsInteraction`", "真机未复核"), ("注释缺口对话框", "x", "真机未复核"),
             ("同步预览与执行", "只有源码与编译证据", "真实端到端未跑")],
            table, registered);
        Assert.Contains(dup, p => p.Contains("写了两遍", StringComparison.Ordinal));

        // ④ 源码证据屏被引了段：把「源码 + 编译证据」念成 wine 量到。
        var overstated = LectureProblems(
            [("注释缺口对话框", "`RunScanResultsInteraction`", "真机未复核"), ("同步预览与执行", "`RunScanResultsInteraction`", "真实端到端未跑")],
            table, registered);
        Assert.Contains(overstated, p => p.Contains("源码证据屏「同步预览与执行」", StringComparison.Ordinal));

        // ⑤ 引了注册清单外的段：照着念是空手。
        var unresolved = LectureProblems(
            [("注释缺口对话框", "`RunNotRegistered`", "真机未复核"), ("同步预览与执行", "只有源码与编译证据", "真实端到端未跑")],
            table, registered);
        Assert.Contains(unresolved, p => p.Contains("RunNotRegistered", StringComparison.Ordinal));

        // ⑥ 某屏没交代「缺什么」。
        var noGap = LectureProblems(
            [("注释缺口对话框", "`RunScanResultsInteraction`", "  "), ("同步预览与执行", "只有源码与编译证据", "真实端到端未跑")],
            table, registered);
        Assert.Contains(noGap, p => p.Contains("「缺什么」是空的", StringComparison.Ordinal));

        // ⑦ 合规：两处都不报。
        Assert.Empty(LectureProblems(
            [("注释缺口对话框", "`RunScanResultsInteraction`", "真机观感未复核（G-D5）"),
             ("同步预览与执行", "只有源码与编译证据（`SyncExecutor` 用例）", "真实 Apifox 写入本环境不可得")],
            table, registered));
    }

    /// <summary>
    /// 讲稿页念的三档那个数，必须与 <c>.winecheck/reading.txt</c> 的 <c>tier= passed= failed=</c> 三行逐字对得上：
    /// 三档失败都是 0、通过数三档同一个，页上出现的「N 通过 / 0 失败 ×3」只许是那一个 N。
    /// <para>
    /// <b>为什么钉这条</b>：这一页是不跑 wine 就能念的成品，正因为不现跑，它比 <c>narration-*.txt</c> 更容易
    /// 停在上一版的数上。把它对到 <c>reading.txt</c>——那份由 <c>demo.sh</c> 写、<c>ReadingConsistencyTests</c> 已
    /// 与 README / NOTES 逐字对账的记录——等于把这页也接进「念的数出自哪一版」这条链，不另造一个没人对的死数。
    /// </para>
    /// </summary>
    [Fact]
    public void LecturePage_StatesTheSameTierCountAsTheReadingRecord()
    {
        var reading = Repo.Source(".winecheck", "reading.txt");
        var tiers = Regex.Matches(reading, @"^tier=\d+ passed=(\d+) failed=(\d+) exit=\d+", RegexOptions.Multiline)
            .Select(m => (Passed: int.Parse(m.Groups[1].Value), Failed: int.Parse(m.Groups[2].Value)))
            .ToList();
        Assert.True(tiers.Count == 3, $"reading.txt 的 tier= 行不是三档（读到 {tiers.Count} 行）——这一页没有可对账的读数出处。");
        Assert.All(tiers, t => Assert.Equal(0, t.Failed));
        var passed = tiers.Select(t => t.Passed).Distinct().ToList();
        Assert.True(passed.Count == 1, $"三档通过数不一致（{string.Join(" / ", passed)}）——不能对三档念同一个「×3」。");
        var count = passed[0];

        Assert.Contains($"{count} 通过 / 0 失败 ×3", LecturePage, StringComparison.Ordinal);
        var stated = Regex.Matches(LecturePage, @"(\d+) 通过 / 0 失败 ×3")
            .Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToList();
        Assert.Equal(new[] { count }, stated);
    }

    /// <summary>
    /// 开场与结尾两块都带齐三句诚实口径（不是真机结果 / 到期日 / G-D5 未达成），且与 <c>demo.sh</c>
    /// 的录制口径第 4 条同词——这一页不许把模拟器证据说成真机结果，也不许在某一端漏掉那一句。
    /// </summary>
    [Fact]
    public void LecturePage_KeepsTheHonestCaliberAtBothEnds()
    {
        var opening = LectureSection("## 固定开场口径", "## 当天四步");
        var closing = LectureSection("## 结尾固定一句", null);
        foreach (var block in new[] { opening, closing })
        {
            Assert.Contains("不是 Windows 真机结果", block, StringComparison.Ordinal);
            Assert.Contains("2026-11-20", block, StringComparison.Ordinal);
            Assert.Contains("G-D5 未达成（环境供给缺口）", block, StringComparison.Ordinal);
        }
    }

    /// <summary>逐屏那一节的四列行（屏 / 做什么 / 量到什么 / 缺什么），跳表头与分隔行。</summary>
    private static List<(string Screen, string Measured, string Missing)> LectureRows()
    {
        var lines = LecturePage.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("## 逐屏一句话讲稿", StringComparison.Ordinal));
        Assert.True(start >= 0, "答辩讲稿缺「## 逐屏一句话讲稿」一节。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        var rows = new List<(string, string, string)>();
        foreach (var line in lines[start..(end < 0 ? lines.Length : end)])
        {
            if (!line.StartsWith("| ", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            Assert.True(cells.Count == 4, $"逐屏那一节有一行不是 4 列（{cells.Count} 列）：{line[..Math.Min(40, line.Length)]}…");
            if (cells[0] == "屏" || cells[0].All(ch => ch is '-' or ':')) continue;
            rows.Add((cells[0], cells[2], cells[3]));
        }

        Assert.True(rows.Count >= 12, $"逐屏那一节只有 {rows.Count} 行（地板 12）。");
        return rows;
    }

    /// <summary>
    /// 讲稿页与逐屏表的对账，返回「哪几处会念歪」的说明（空 = 全对上）。写成<b>纯数据进、问题出</b>的函数，
    /// 是为了让 <see cref="LectureScreenGate_ReportsTheDriftItMustCatch"/> 能拿注入过的夹具喂它——
    /// 与 <c>RowClaimMismatches</c> / <c>LayoutMeasurementGateTests.Problems</c> 同一形状。
    /// </summary>
    private static List<string> LectureProblems(
        IReadOnlyList<(string Screen, string Measured, string Missing)> lectureRows,
        IReadOnlyList<(string Screen, string Verdict)> tableRows,
        IReadOnlyList<string> registered)
    {
        var problems = new List<string>();
        var lectureScreens = lectureRows.Select(r => r.Screen).ToList();
        var tableScreens = tableRows.Select(r => r.Screen).ToList();

        foreach (var screen in tableScreens.Where(screen => !lectureScreens.Contains(screen, StringComparer.Ordinal)))
        {
            problems.Add($"逐屏表有屏「{screen}」，这一页没有——现场按表念到它时手里这一页空着。");
        }

        foreach (var screen in lectureScreens.Where(screen => !tableScreens.Contains(screen, StringComparer.Ordinal)))
        {
            problems.Add($"这一页多出屏「{screen}」，逐屏表里没有它——等于凭空多写了一份屏单上没有的屏。");
        }

        foreach (var dup in lectureScreens.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            problems.Add($"这一页把屏「{dup}」写了两遍——逐屏表只有一行，屏数与念序都对不上。");
        }

        var sourceOnly = tableRows
            .Where(r => r.Verdict.Contains("源码 + 编译证据", StringComparison.Ordinal))
            .Select(r => r.Screen)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (screen, measured, missing) in lectureRows)
        {
            foreach (var segment in Regex.Matches(measured, @"Run[A-Za-z0-9]+").Select(m => m.Value).Distinct(StringComparer.Ordinal))
            {
                if (!registered.Contains(segment, StringComparer.Ordinal))
                {
                    problems.Add($"这一页屏「{screen}」引用了 {segment}，它不在冒烟器注册清单里——这一段跑不到，照着念就是空手。");
                }

                if (sourceOnly.Contains(screen))
                {
                    problems.Add($"这一页把源码证据屏「{screen}」引了 {segment} 念成 wine 量到——与逐屏表「源码 + 编译证据」互相打脸。");
                }
            }

            if (string.IsNullOrWhiteSpace(missing))
            {
                problems.Add($"这一页屏「{screen}」的「缺什么」是空的——每屏都要交代还差什么（G-D5 或环境不具备），不许只报喜。");
            }
        }

        return problems;
    }

    private const string ReconcileHeading = "## 逐屏念稿与登记出处对账";

    /// <summary>
    /// 讲稿页「逐屏念稿与登记出处对账」那一节的四列行（# / 屏 / 念这一句时屏上应当出现 / 登记出处）。
    /// 列数不是 4 的行在这里就是测试失败，不是跳过。
    /// </summary>
    private static List<(string Screen, string Should, string Provenance)> ReconcileRows()
    {
        var lines = LecturePage.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith(ReconcileHeading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"答辩讲稿缺「{ReconcileHeading}」一节——逐屏念稿没有一处对到 MeasuredScreens 的登记轮次上。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        var rows = new List<(string, string, string)>();
        foreach (var line in lines[start..(end < 0 ? lines.Length : end)])
        {
            if (!line.StartsWith("| ", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            Assert.True(cells.Count == 4, $"「{ReconcileHeading}」有一行不是 4 列（{cells.Count} 列）：{line[..Math.Min(40, line.Length)]}…");
            if (cells[0] == "#" || cells[0].All(ch => ch is '-' or ':')) continue;
            rows.Add((cells[1], cells[2], cells[3]));
        }

        Assert.True(rows.Count >= 12, $"「{ReconcileHeading}」只有 {rows.Count} 行（地板 12）。");
        return rows;
    }

    /// <summary>
    /// 讲稿页的「逐屏念稿与登记出处对账」与 <c>Desktop/Native/MeasuredScreens.cs</c> 那一份登记逐屏对账：
    /// 屏名与屏序与「逐屏一句话讲稿」逐字同一份、每屏都写了「念这一句时屏上应当出现什么」、
    /// 每屏引的段与那一屏念的段是同一个集合、且每一段的轮次与登记表记的<b>逐字相同</b>。
    /// <para>
    /// <b>补的是哪个缺口</b>：第四百一十六轮之前，讲稿页的 27 屏只对到「屏名 / 屏数 / 段名在不在注册清单里」，
    /// <b>没有一处对到「这一段是哪一轮量过的」</b>——于是「这一屏引的段在登记表里查得到、记的是同一轮」这件事
    /// 只能靠人翻 `MeasuredScreens.cs`。答辩现场念到某一屏时，问「哪一轮量的」这一页给不出答案。
    /// </para>
    /// <para>
    /// <b>为什么这是对账门、不是「判据判自己」</b>（第二百七十轮五步法）：断言里的名字是讲稿页那一格的字面
    /// 与 `MeasuredScreens.All` 那一份登记——两侧的定义各在自己那一头（一个在 `.winecheck/答辩讲稿.md`、
    /// 一个在 `Desktop/Native/MeasuredScreens.cs`），第 ② 步答「不是」，故按对账门处理，照常随实物一起长。
    /// </para>
    /// </summary>
    [Fact]
    public void LecturePage_ReconcilesEveryScreenWithTheMeasuredScreensRegister()
    {
        var register = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in ApifoxHelper.Desktop.Native.MeasuredScreens.All)
        {
            register.TryAdd(row.Section, row.Round);
        }

        Assert.Empty(ReconcileProblems(
            ReconcileRows(),
            LectureRows().Select(r => (r.Screen, r.Measured)).ToList(),
            register));
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：七发注入各喂一次，每一发都必须报出自己那一处（最后一发合规、不报）。
    /// </summary>
    [Fact]
    public void LectureReconcileGate_ReportsTheDriftItMustCatch()
    {
        var register = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RunScanResultsInteraction"] = "第一百三十六轮",
            ["RunScanResultsLoadFailure"] = "第三百零五轮",
        };
        (string Screen, string Measured)[] lecture =
        [
            ("注释缺口对话框", "`RunScanResultsInteraction` / `RunScanResultsLoadFailure`"),
            ("同步预览与执行", "只有源码与编译证据"),
        ];

        // ⑥ 合规：两屏都对得上——不报。
        Assert.Empty(ReconcileProblems(
            [
                ("注释缺口对话框", "计数行与清单", "`RunScanResultsInteraction` → 第一百三十六轮；`RunScanResultsLoadFailure` → 第三百零五轮"),
                ("同步预览与执行", "勾选清单与写入清单", "—（这一屏只有源码与编译证据，没有冒烟段可指）"),
            ],
            lecture, register));

        // ① 少一屏：讲稿念两屏，对账表只写了一屏。
        Assert.Contains(ReconcileProblems(
            [("注释缺口对话框", "计数行与清单", "`RunScanResultsInteraction` → 第一百三十六轮；`RunScanResultsLoadFailure` → 第三百零五轮")],
            lecture, register), p => p.Contains("少了一屏", StringComparison.Ordinal));

        // ② 屏名错位：第二行写成了别的屏。
        Assert.Contains(ReconcileProblems(
            [
                ("注释缺口对话框", "计数行与清单", "`RunScanResultsInteraction` → 第一百三十六轮；`RunScanResultsLoadFailure` → 第三百零五轮"),
                ("检查任务页", "勾选清单与写入清单", "—（这一屏只有源码与编译证据，没有冒烟段可指）"),
            ],
            lecture, register), p => p.Contains("屏名对不上", StringComparison.Ordinal));

        // ③ 轮次漂了：登记表记第三百零五轮，这一页念第一百三十六轮。
        Assert.Contains(ReconcileProblems(
            [
                ("注释缺口对话框", "计数行与清单", "`RunScanResultsInteraction` → 第一百三十六轮；`RunScanResultsLoadFailure` → 第一百三十六轮"),
                ("同步预览与执行", "勾选清单与写入清单", "—（这一屏只有源码与编译证据，没有冒烟段可指）"),
            ],
            lecture, register), p => p.Contains("第三百零五轮", StringComparison.Ordinal) && p.Contains("轮次", StringComparison.Ordinal));

        // ④ 漏了一段：这一屏念了两段，对账表只写了一段的轮次。
        Assert.Contains(ReconcileProblems(
            [
                ("注释缺口对话框", "计数行与清单", "`RunScanResultsInteraction` → 第一百三十六轮"),
                ("同步预览与执行", "勾选清单与写入清单", "—（这一屏只有源码与编译证据，没有冒烟段可指）"),
            ],
            lecture, register), p => p.Contains("RunScanResultsLoadFailure", StringComparison.Ordinal) && p.Contains("漏", StringComparison.Ordinal));

        // ⑤ 引了没登记的段却写成有轮次：登记表里没有它，这一页却给了个轮次。
        Assert.Contains(ReconcileProblems(
            [
                ("注释缺口对话框", "计数行与清单", "`RunScanResultsInteraction` → 第一百三十六轮；`RunScanResultsLoadFailure` → 第三百零五轮；`RunNotRegistered` → 第一百轮"),
                ("同步预览与执行", "勾选清单与写入清单", "—（这一屏只有源码与编译证据，没有冒烟段可指）"),
            ],
            lecture, register), p => p.Contains("RunNotRegistered", StringComparison.Ordinal) && p.Contains("未登记", StringComparison.Ordinal));

        // ⑦ 「念这一句时屏上应当出现什么」是空的。
        Assert.Contains(ReconcileProblems(
            [
                ("注释缺口对话框", "  ", "`RunScanResultsInteraction` → 第一百三十六轮；`RunScanResultsLoadFailure` → 第三百零五轮"),
                ("同步预览与执行", "勾选清单与写入清单", "—（这一屏只有源码与编译证据，没有冒烟段可指）"),
            ],
            lecture, register), p => p.Contains("屏上应当出现", StringComparison.Ordinal));
    }

    /// <summary>
    /// 讲稿页「逐屏念稿与登记出处对账」与登记表的对账，返回「哪几处会念歪」的说明（空 = 全对上）。
    /// 写成<b>纯数据进、问题出</b>的函数，是为了让 <see cref="LectureReconcileGate_ReportsTheDriftItMustCatch"/>
    /// 能拿注入过的夹具喂它——与 <see cref="LectureProblems"/> 同一形状。
    /// </summary>
    private static List<string> ReconcileProblems(
        IReadOnlyList<(string Screen, string Should, string Provenance)> reconcileRows,
        IReadOnlyList<(string Screen, string Measured)> lectureRows,
        IReadOnlyDictionary<string, string> register)
    {
        var problems = new List<string>();

        if (reconcileRows.Count != lectureRows.Count)
        {
            problems.Add($"对账表 {reconcileRows.Count} 行、逐屏讲稿 {lectureRows.Count} 行——少了一屏或多了一屏，念序对不上。");
        }

        for (var i = 0; i < Math.Min(reconcileRows.Count, lectureRows.Count); i++)
        {
            var (screen, should, provenance) = reconcileRows[i];
            var lecture = lectureRows[i];

            if (!string.Equals(screen, lecture.Screen, StringComparison.Ordinal))
            {
                problems.Add($"第 {i + 1} 行屏名对不上：对账表写「{screen}」、逐屏讲稿写「{lecture.Screen}」——念序会错位。");
                continue;
            }

            if (string.IsNullOrWhiteSpace(should))
            {
                problems.Add($"屏「{screen}」的「念这一句时，屏上应当出现」是空的——现场念到它时不知道该指屏上哪一处。");
            }

            var cited = Regex.Matches(provenance, @"Run[A-Za-z0-9]+").Select(m => m.Value).Distinct(StringComparer.Ordinal).ToList();
            var measured = Regex.Matches(lecture.Measured, @"Run[A-Za-z0-9]+").Select(m => m.Value).Distinct(StringComparer.Ordinal).ToList();

            foreach (var segment in measured.Where(s => !cited.Contains(s, StringComparer.Ordinal)))
            {
                problems.Add($"屏「{screen}」念了 {segment}，对账表漏了它的轮次——现场被问「哪一轮量的」时这一页答不出。");
            }

            foreach (var segment in cited.Where(s => !measured.Contains(s, StringComparer.Ordinal)))
            {
                problems.Add($"屏「{screen}」的对账表引了 {segment}，可这一屏的讲稿没念它——两处不是同一个段集合。");
            }

            foreach (var segment in cited)
            {
                if (register.TryGetValue(segment, out var round))
                {
                    if (!provenance.Contains($"`{segment}` → {round}", StringComparison.Ordinal))
                    {
                        problems.Add($"屏「{screen}」的 {segment} 轮次对不上：登记表记的是 {round}，这一页念的不是它。");
                    }
                }
                else if (!provenance.Contains($"`{segment}` → 未登记", StringComparison.Ordinal))
                {
                    problems.Add($"屏「{screen}」的 {segment} 在登记表里没有行，这一页却给了个轮次——未登记就得如实写「未登记」。");
                }
            }

            if (measured.Count == 0 && !provenance.StartsWith("—", StringComparison.Ordinal))
            {
                problems.Add($"屏「{screen}」只有源码与编译证据，对账表的出处却不像「没有冒烟段可指」——别把源码证据屏写成有段可念。");
            }
        }

        return problems;
    }

    /// <summary>讲稿页里从某个二级标题到下一个二级标题（或到文件末）的正文。</summary>
    private static string LectureSection(string startHeading, string? endHeading)
    {
        var lines = LecturePage.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith(startHeading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"答辩讲稿缺「{startHeading}」一节。");
        var end = endHeading is null
            ? lines.Length
            : Array.FindIndex(lines, start + 1, l => l.StartsWith(endHeading, StringComparison.Ordinal));
        Assert.True(end > start, $"答辩讲稿「{startHeading}」一节切不出边界。");
        return string.Join('\n', lines[start..end]);
    }
}
