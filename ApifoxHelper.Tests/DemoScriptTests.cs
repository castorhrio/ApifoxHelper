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
        Assert.Contains(@"-not -path ""$REPO/ApifoxHelper.Tests/*""", script, StringComparison.Ordinal);
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
}
