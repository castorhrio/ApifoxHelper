using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 说明行是「一行门控提示」，不是段落（第一百五十五轮，具名 pass：distill）。
/// <para>
/// <b>为什么要有这个文件</b>：任务详情区（<c>TasksPage.BuildDetail</c>）的五个分区各挂了一段说明行，
/// 长度分别是 179 / 153 / 91 / 370 / 79 字——而全原生界面其余 40 多处说明行最长 61 字。
/// 那五段把「哪个按钮什么时候可用、为什么、替代路径是什么」写成教程，读起来像文档而不是界面：
/// ① 模板脸：每段都是「动作清单 + 分号串联 + 兜底说明」的同一句式，与内容本身抢注意力；
/// ② 视觉焦点：详情区是主工作区，五段长文把真正的状态行与动作按钮往下推（第三十一轮为此不得不
/// 给详情区加滚动），用户要先读一段说明才看得见下一步；
/// ③ 泛 AI 文案：句子本身没有空话禁词，但「工具默认不构建、不 restore、不启动服务」这类
/// 机制自述属于「说机制不说结果」，与 §6.2 #12 同侧。
/// </para>
/// <para>
/// 本轮把它们蒸馏成一行门控提示（保留「什么时候可用」与一条安全承诺，删掉教程与替代路径清单），
/// 并把「说明行不得是段落」落成机械门：长度上限与句数上限各一条，任何一屏再写回长文即红。
/// 上限取 80 字 / 2 句——现有最长 61 字、最多 2 句，留出余量但不给段落留位置。
/// </para>
/// <para>
/// <b>第一百六十轮（具名 pass：harden）：门只认一种写法，等于只钉住一种写法。</b>
/// 上面那道门此前只提取 <c>Ui.Caption("…")</c> 这一种形态。而说明行在源码里有三种写法：
/// ① 就地字面量；② 底部固定区的 <c>BuildBottomHost(inner, "…", height)</c> 调用点
/// （同步预览的两处说明行就是这样传进去的）；③ <c>Ui.Caption(共享常量)</c>
/// （代码证据的片段区说明行取 <c>EvidenceEntries.SnippetCaption</c>）。
/// 后两种写法在门的视野之外——门是绿的，但它没看过那三行。这与前几轮抓到的机械检查盲区同族
/// （子串匹配被注释行骗过、属性声明形式绕过输入框工厂）：门钉住的是一种写法，不是一条规则。
/// 本轮把提取口径扩到三种写法，并把「没有说明行能绕过门」落成断言：任何 <c>Ui.Caption(变量)</c>
/// 要么解析得到共享常量、要么在封闭清单里登记了「文字来自数据」的理由，否则即红。
/// </para>
/// <para>
/// <b>长度门不等于「放得下」</b>：80 字在 12px 字号下约 960px，比报告与交接对话框（920）与
/// 代码证据对话框（960）的可用宽度都宽——写到上限的说明行会被静默裁掉。长度门管的是「别写成段落」，
/// 「放得下」由冒烟器在真实控件上量（<c>ApifoxHelper.Smoke</c> 的 <c>RunCaptionFit</c> 段）。
/// </para>
/// </summary>
public class CaptionBrevityTests
{
    /// <summary>说明行长度上限（字）。现有最长 61 字，段落从 79 字起。</summary>
    private const int MaxCaptionLength = 80;

    /// <summary>说明行句数上限（按句号计）。一句讲这块框是什么，第二句讲什么时候可用。</summary>
    private const int MaxCaptionSentences = 2;

    /// <summary>说明行在源码里的四种写法（第一百六十轮三种，第二百零六轮起加提示行工厂这一种）。</summary>
    internal enum CaptionForm
    {
        /// <summary><c>Ui.Caption("…")</c>：就地字面量。</summary>
        Literal,

        /// <summary><c>BuildBottomHost(inner, "…", height)</c> 调用点：底部固定区的说明行。</summary>
        BottomHostCallSite,

        /// <summary><c>Ui.Caption(共享常量)</c>：文字在共享口径文件里（如 <c>EvidenceEntries.SnippetCaption</c>）。</summary>
        SharedConstant,

        /// <summary>
        /// <c>Ui.HintRow(grid, "…")</c> 调用点：两列字段网格里的提示行（第二百零六轮，具名 pass：distill）。
        /// 此前这一族有四种写法（配置页借 <c>AddRow(grid, "", Ui.Caption(…))</c>、三个配置子对话框各带一份
        /// 私有 <c>AddHint</c>、新建任务对话框的 <c>AddRow</c>）；收成一处工厂后，门的提取口径必须跟着认这一种
        /// 写法——认不到就等于那二十六行说明行不再被长度门看过，而界面照旧显示它们。
        /// </summary>
        HintRowCallSite,
    }

    /// <summary>一条说明行：文件、行号、文字与写法。</summary>
    internal sealed record Caption(string File, int Line, string Text, CaptionForm Form);

    /// <summary>
    /// 文字来自数据或字段标签的说明行（不是就地写死的说明行），逐条登记理由。
    /// <para>
    /// 封闭清单：清单外的 <c>Ui.Caption(变量)</c> 一律判红（说明行不许藏在局部变量里），
    /// 清单里登记了却找不到对应写法的也判红（清单不许留死条目）。
    /// </para>
    /// </summary>
    private static readonly (string File, string Argument, string Why)[] DynamicCaptions =
    [
        ("Ui.cs", "text", "提示行工厂 Ui.HintRow：本处只交出形状（字段列一句 Caption、标签列留空），文字由调用处传入；"
            + "调用点的字面量与共享常量由 ExtractCaptions 按 HintRowCallSite 逐条提取，不藏在工厂里"),
        ("SupplementsDialog.cs", "editor.Label", "编辑位标题：文字来自编辑位自身"),
        ("GenerationDialog.cs", "GenerationEntries.KeyHint + \"。\"", "密钥提示：取共享常量再补一个句号，句子本体在 GenerationEntries 一份，界面不另抄一遍"),
        ("ProjectsPage.cs", "ProfileFormLayout.ProjectListHint(_editing is not null)", "提示文字由共享口径按「新建/编辑」两种状态给出"),
        ("SyncPreviewDialog.cs", "caption", "底部区说明行：文字来自 BuildBottomHost 调用点，调用点已逐条提取"),
        ("NoticeDialog.cs", "note", "提示附注：文字由调用方给（NoticeText 的两个共享常量），对话框不就地拼写"),
    ];

    /// <summary>共享口径里的字符串常量（<c>public const string X = "…";</c>），供共享常量写法解析。</summary>
    internal static Dictionary<string, string> SharedConstants()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in Repo.NativeSources())
        {
            var name = Path.GetFileNameWithoutExtension(source.Name);
            foreach (Match match in Regex.Matches(source.Text, @"public const string (\w+)\s*=\s*""([^""]*)"";"))
            {
                map[name + "." + match.Groups[1].Value] = match.Groups[2].Value;
            }
        }
        return map;
    }

    /// <summary>
    /// 提取说明行（四种写法）。纯函数：输入是「文件名 + 全文」与常量表，不读磁盘，可单测。
    /// </summary>
    internal static List<Caption> ExtractCaptions(
        IEnumerable<(string Name, string Text)> sources,
        IReadOnlyDictionary<string, string> constants)
    {
        var found = new List<Caption>();
        foreach (var (name, text) in sources)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                foreach (Match match in Regex.Matches(line, @"Ui\.Caption\(""([^""]*)""\)"))
                {
                    found.Add(new Caption(name, i + 1, match.Groups[1].Value, CaptionForm.Literal));
                }
                // 提示行工厂：文字仍是调用处给的那一句（就地字面量，或一个能解析的共享常量）。
                foreach (Match match in Regex.Matches(line, @"Ui\.HintRow\([^,]+,\s*""([^""]*)"""))
                {
                    found.Add(new Caption(name, i + 1, match.Groups[1].Value, CaptionForm.HintRowCallSite));
                }
                foreach (Match match in Regex.Matches(line, @"Ui\.HintRow\([^,]+,\s*(?<arg>[\w.]+)\s*\)"))
                {
                    var argument = match.Groups["arg"].Value;
                    if (constants.TryGetValue(argument, out var shared))
                    {
                        found.Add(new Caption(name, i + 1, shared, CaptionForm.HintRowCallSite));
                    }
                }
                foreach (Match match in Regex.Matches(line, @"BuildBottomHost\([^,]+,\s*""([^""]*)"""))
                {
                    found.Add(new Caption(name, i + 1, match.Groups[1].Value, CaptionForm.BottomHostCallSite));
                }
                foreach (var (_, argument) in CaptionArguments(line))
                {
                    if (constants.TryGetValue(argument, out var value))
                    {
                        found.Add(new Caption(name, i + 1, value, CaptionForm.SharedConstant));
                    }
                }
            }
        }
        return found;
    }

    /// <summary>
    /// 门看不见的说明行：<c>Ui.Caption(变量)</c> 里既不是共享常量、也不在封闭清单里的那些。
    /// 空标签（<c>Ui.Caption()</c>，文字稍后由数据填）不算说明行。
    /// </summary>
    internal static List<string> UnresolvedCaptionArguments(
        IEnumerable<(string Name, string Text)> sources,
        IReadOnlyDictionary<string, string> constants)
    {
        var unresolved = new List<string>();
        foreach (var (name, text) in sources)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (_, argument) in CaptionArguments(lines[i]).Concat(HintRowArguments(lines[i])))
                {
                    if (argument.Length == 0) continue;
                    if (argument.StartsWith('"')) continue;   // 就地字面量：已由 ExtractCaptions 提取
                    if (constants.ContainsKey(argument)) continue;
                    if (DynamicCaptions.Any(entry => entry.File == name && entry.Argument == argument)) continue;
                    unresolved.Add($"{name}:{i + 1}（{argument}）");
                }
            }
        }
        return unresolved;
    }

    /// <summary>
    /// 一行里所有 <c>Ui.HintRow(…)</c> 的<b>第二个</b>实参原文（第二百零六轮起的提示行工厂：
    /// 第一个实参是网格，第二个才是那句话）。取法与 <see cref="CaptionArguments"/> 同一套：
    /// 顶层逗号切一次，再按括号配对取到收尾的 <c>)</c>。
    /// </summary>
    private static IEnumerable<(int Column, string Argument)> HintRowArguments(string line)
    {
        var index = 0;
        while (index < line.Length)
        {
            var open = line.IndexOf("Ui.HintRow(", index, StringComparison.Ordinal);
            if (open < 0) yield break;
            var cursor = open + "Ui.HintRow(".Length;
            var depth = 1;
            var textStart = -1;
            while (cursor < line.Length)
            {
                var ch = line[cursor];
                if (ch == '(') depth++;
                else if (ch == ')') depth--;
                else if (ch == ',' && depth == 1)
                {
                    if (textStart < 0) { textStart = cursor + 1; }   // 第一个逗号之后才是文字
                }
                if (depth == 0) break;
                cursor++;
            }
            if (textStart >= 0)
            {
                // cursor 停在收尾的 )（不含），所以切出来就是完整的文字实参原文。
                yield return (open, line[textStart..Math.Min(cursor, line.Length)].Trim());
            }
            index = open + "Ui.HintRow(".Length;
        }
    }

    /// <summary>
    /// 一行里所有 <c>Ui.Caption(…)</c> 的第一个实参原文（按括号配对取，能处理嵌套括号；
    /// 顶层逗号处收手——<c>Ui.Caption("…", muted: false)</c> 的第二个实参不是说明行文字）。
    /// </summary>
    private static IEnumerable<(int Column, string Argument)> CaptionArguments(string line)
    {
        var index = 0;
        while (index < line.Length)
        {
            var open = line.IndexOf("Ui.Caption(", index, StringComparison.Ordinal);
            if (open < 0) yield break;
            var start = open + "Ui.Caption(".Length;
            var depth = 1;
            var cursor = start;
            while (cursor < line.Length)
            {
                var ch = line[cursor];
                if (ch == '(') depth++;
                else if (ch == ')') depth--;
                else if (ch == ',' && depth == 1) break;
                if (depth == 0) break;
                cursor++;
            }
            yield return (open, cursor > start ? line[start..cursor].Trim() : "");
            index = start;
        }
    }

    /// <summary>界面里所有说明行（三种写法，第一百六十轮）。</summary>
    private static List<Caption> Captions() =>
        ExtractCaptions(Repo.NativeSources().Select(s => (s.Name, s.Text)), SharedConstants());

    /// <summary>
    /// 说明行不得是段落：长度与句数各一条上限。
    /// <para>
    /// 变异验证（第一百五十五轮实测）：把任务详情区任意一段改回原来的长文，这条即红并指名文件与行号。
    /// 第一百六十轮起口径覆盖三种写法——把 <c>PlanEntries</c> 里的共享常量改长、或把底部区调用点的
    /// 字面量改长，同样即红（此前这两种写法根本不在门的视野里）。
    /// </para>
    /// </summary>
    [Fact]
    public void CaptionLiterals_AreOneLineNotParagraphs()
    {
        var captions = Captions();
        // 地板：提取口径失效时断言会静默空转（比没有断言更糟）。
        Assert.True(captions.Count >= 36, $"只提取到 {captions.Count} 处说明行，提取口径可能已失效。");

        var tooLong = captions
            .Where(c => c.Text.Length > MaxCaptionLength)
            .Select(c => $"{c.File}:{c.Line}（{c.Text.Length} 字，{FormName(c.Form)}）")
            .ToList();
        Assert.True(
            tooLong.Count == 0,
            $"以下说明行超过 {MaxCaptionLength} 字，是段落不是一行提示（蒸馏成「什么时候可用」那一句）："
            + string.Join("、", tooLong));

        var tooManySentences = captions
            .Where(c => c.Text.Count(ch => ch == '。') > MaxCaptionSentences)
            .Select(c => $"{c.File}:{c.Line}（{c.Text.Count(ch => ch == '。')} 句，{FormName(c.Form)}）")
            .ToList();
        Assert.True(
            tooManySentences.Count == 0,
            $"以下说明行超过 {MaxCaptionSentences} 句：说明行讲「这块是什么 + 什么时候可用」，教程与替代路径不在这里："
            + string.Join("、", tooManySentences));
    }

    /// <summary>
    /// 没有说明行能绕过门（第一百六十轮）：<c>Ui.Caption(变量)</c> 要么解析得到共享常量，
    /// 要么在封闭清单里登记了「文字来自数据」的理由。
    /// <para>
    /// 变异验证（本轮实测）：把 <c>Ui.Caption(EvidenceEntries.SnippetCaption)</c> 改成
    /// <c>Ui.Caption(text)</c>（局部变量），这条即红并指名文件与行号——说明行藏进局部变量后，
    /// 长度门与句数门都看不见它，而界面照旧显示。
    /// </para>
    /// </summary>
    [Fact]
    public void NoCaptionEscapesTheGate()
    {
        var sources = Repo.NativeSources().Select(s => (s.Name, s.Text)).ToList();
        var unresolved = UnresolvedCaptionArguments(sources, SharedConstants());
        Assert.True(
            unresolved.Count == 0,
            "以下说明行绕过了机械门（文字来自局部变量，长度与句数都判不到）："
            + string.Join("、", unresolved)
            + "。要么就地写字面量、要么收进共享常量；文字确实来自数据时在 DynamicCaptions 里登记理由。");

        // 反向：封闭清单里登记了却找不到对应写法，说明那条理由已经过期（清单不许留死条目）。
        var stale = DynamicCaptions
            .Where(entry => !sources.Any(s => s.Name == entry.File
                && CaptionArguments(s.Text).Concat(HintRowArguments(s.Text)).Any(a => a.Argument == entry.Argument)))
            .Select(entry => $"{entry.File}（{entry.Argument}）")
            .ToList();
        Assert.True(stale.Count == 0, "DynamicCaptions 里这些登记在源码里找不到对应写法（理由已过期）：" + string.Join("、", stale));
    }

    /// <summary>
    /// 提取器本身可单测（第一百六十轮，第二百零六轮补第四种）：四种写法各提取一条，局部变量形式提取不到
    /// ——后者正是 <see cref="NoCaptionEscapesTheGate"/> 存在的理由。
    /// </summary>
    [Fact]
    public void Extractor_SeesAllFourWritingForms()
    {
        const string source = """
            var a = Ui.Caption("就地字面量说明行");
            var b = BuildBottomHost(inner, "底部区说明行", 190);
            var c = Ui.Caption(Shared.Caption);
            var d = Ui.Caption(text);
            var e = Ui.Caption();
            var f = Ui.HintRow(grid, "提示行说明行");
            var g = Ui.HintRow(grid, Shared.Hint);
            """;
        var constants = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Shared.Caption"] = "共享常量说明行",
            ["Shared.Hint"] = "提示行共享常量",
        };
        var sources = new[] { ("Fake.cs", source) };

        var captions = ExtractCaptions(sources, constants);
        Assert.Equal(5, captions.Count);
        Assert.Contains(captions, c => c is { Text: "就地字面量说明行", Form: CaptionForm.Literal });
        Assert.Contains(captions, c => c is { Text: "底部区说明行", Form: CaptionForm.BottomHostCallSite });
        Assert.Contains(captions, c => c is { Text: "共享常量说明行", Form: CaptionForm.SharedConstant });
        Assert.Contains(captions, c => c is { Text: "提示行说明行", Form: CaptionForm.HintRowCallSite });
        Assert.Contains(captions, c => c is { Text: "提示行共享常量", Form: CaptionForm.HintRowCallSite });

        // 局部变量形式提取不到（门的盲区），空标签不算说明行（文字来自数据）。
        var unresolved = UnresolvedCaptionArguments(sources, constants);
        Assert.Equal(["Fake.cs:4（text）"], unresolved);
    }

    /// <summary>
    /// 登记为「文字来自数据」的说明行，其真实来源也要过同一道门（第一百六十轮）。
    /// <para>
    /// 封闭清单是门的逃生口：登记一条理由，那一行就不再被长度门看见。逃生口不能变成藏身处——
    /// 提示对话框的附注文字来自 <c>NoticeText</c> 的两个共享常量（「关于」与「打开目录失败」），
    /// 这里把这两个常量本身按同一道门判一遍：常量写长成段落，这条即红。
    /// </para>
    /// </summary>
    [Fact]
    public void DynamicCaptionSources_PassTheSameGate()
    {
        var constants = SharedConstants();
        foreach (var key in new[] { "NoticeText.AboutNote", "NoticeText.FolderErrorNote" })
        {
            Assert.True(constants.TryGetValue(key, out var text), $"共享常量 {key} 找不到（DynamicCaptions 里那条理由已过期）。");
            Assert.True(
                text!.Length <= MaxCaptionLength,
                $"{key} 的说明行 {text.Length} 字，超过 {MaxCaptionLength} 字：{text}");
            Assert.True(
                text.Count(ch => ch == '。') <= MaxCaptionSentences,
                $"{key} 的说明行超过 {MaxCaptionSentences} 句：{text}");
        }
    }

    /// <summary>
    /// 三屏说明行逐条过同一道门（第一百六十轮，简报 P2-3）：同步预览、报告与交接、代码证据。
    /// <para>
    /// 这三屏是「说明行写法最杂」的三处——同步预览的两处说明行经 <c>BuildBottomHost</c> 传入、
    /// 代码证据的片段区说明行取共享常量、报告与交接是就地字面量。地板按屏给：提取口径失效时
    /// 断言会静默空转，比没有断言更糟。
    /// </para>
    /// </summary>
    [Fact]
    public void SyncPreviewReportAndEvidenceCaptions_PassTheSameGate()
    {
        var captions = Captions();
        var screens = new (string File, int Floor)[]
        {
            ("SyncPreviewDialog.cs", 7),   // 勾选页 2（说明行 + 键盘手势提示，第二百九十轮）+ 预览页 3 + 底部区调用点 2
            ("ReportsDialog.cs", 1),
            ("EvidenceDialog.cs", 1),      // 片段区说明行（共享常量）
        };

        foreach (var (file, floor) in screens)
        {
            var mine = captions.Where(c => c.File == file).ToList();
            Assert.True(mine.Count >= floor, $"{file} 只提取到 {mine.Count} 条说明行（地板 {floor}），提取口径可能已失效。");
            foreach (var caption in mine)
            {
                Assert.True(
                    caption.Text.Length <= MaxCaptionLength,
                    $"{file}:{caption.Line} 的说明行 {caption.Text.Length} 字，超过 {MaxCaptionLength} 字：{caption.Text}");
                Assert.True(
                    caption.Text.Count(ch => ch == '。') <= MaxCaptionSentences,
                    $"{file}:{caption.Line} 的说明行超过 {MaxCaptionSentences} 句：{caption.Text}");
            }
        }
    }

    /// <summary>
    /// 任务详情区的说明行必须说清「什么时候可用」（门控），而不是把动作清单与替代路径讲一遍。
    /// <para>
    /// 只判这一屏：它是主工作区，也是本轮蒸馏的落点。判据是「含门控词」——「可用」「需」二者之一，
    /// 说明这一行讲的是下一步的条件；把说明行改成一句纯描述（例如「这里是源码注释区」）即红。
    /// </para>
    /// </summary>
    [Fact]
    public void TaskDetailCaptions_StateTheGateNotTheTutorial()
    {
        var source = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var captions = Regex.Matches(source, @"AddDetailRow\(grid, Ui\.Caption\(""([^""]*)""\)\)")
            .Select(m => m.Groups[1].Value)
            .ToList();
        // 地板：五个分区各一段（源码注释 / 生成文档 / 响应契约 / Apifox 差异与同步 / 报告与交接）。
        Assert.True(captions.Count >= 5, $"任务详情区只找到 {captions.Count} 段说明行，提取口径可能已失效。");

        foreach (var caption in captions)
        {
            Assert.True(caption.Length <= MaxCaptionLength, $"任务详情区说明行 {caption.Length} 字，超过 {MaxCaptionLength} 字：{caption}");
            Assert.True(
                caption.Contains("可用", StringComparison.Ordinal) || caption.Contains("需", StringComparison.Ordinal),
                $"任务详情区说明行没有说清「什么时候可用」：{caption}");
        }
    }

    /// <summary>
    /// 任务详情区五段说明行不得是同一句式（第一百五十六轮，具名 pass：clarify）。
    /// <para>
    /// <b>为什么还要一条</b>：第一百五十五轮的 <c>distill</c> 把长度收敛了（179/153/91/370/79 字 →
    /// 44/41/26/59/27 字），但五段仍是「一句描述 + 一句门控」的同一句式，且都以「…；…。」收尾——
    /// 模板脸的风险从「长度」挪到了「句式」：五段并排读，读者看到的是同一个模板填了五次，
    /// 而不是五个分区各自在说自己的事。长度门挡不住这件事（五段都合规、都短、都同形）。
    /// </para>
    /// <para>
    /// 判据取「末句的收尾两字」：同一收尾在五段里出现 ≥3 次即红。这是弱断言——它不规定每段该用
    /// 什么句式（问句/陈述/条件/结果导向各有各的用处），只钉住「不许五段一个模子」。
    /// 变异验证（第一百五十六轮实测）：把五段都改成以「时可用」收尾，这条即红并列出重复的收尾。
    /// </para>
    /// </summary>
    [Fact]
    public void TaskDetailCaptions_DoNotShareOneSentenceShape()
    {
        var source = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var captions = Regex.Matches(source, @"AddDetailRow\(grid, Ui\.Caption\(""([^""]*)""\)\)")
            .Select(m => m.Groups[1].Value)
            .ToList();
        // 地板：五个分区各一段，提取口径失效时断言会静默空转。
        Assert.True(captions.Count >= 5, $"任务详情区只找到 {captions.Count} 段说明行，提取口径可能已失效。");

        // 末句 = 最后一个分句（按全角标点切开后的最后一段），收尾取它的最后两个字。
        static string Ending(string caption)
        {
            var last = caption.Split('。', '；', '，', '：', '？', '！')
                .Last(clause => clause.Trim().Length > 0)
                .Trim();
            return last.Length <= 2 ? last : last[^2..];
        }

        var endings = captions.Select(Ending).ToList();
        var repeated = endings
            .GroupBy(e => e, StringComparer.Ordinal)
            .Where(g => g.Count() >= 3)
            .Select(g => $"「{g.Key}」×{g.Count()}")
            .ToList();
        Assert.True(
            repeated.Count == 0,
            $"任务详情区五段说明行是同一句式（收尾重复：{string.Join("、", repeated)}）："
            + "五段并排读像同一个模板填了五次，改成各自合适的句式（问句 / 陈述 / 条件 / 结果导向）。");
    }

    /// <summary>设计基线要记下这次蒸馏（口径与理由），否则下一轮又会把教程写回说明行。</summary>
    [Fact]
    public void DesignDoc_RecordsTheCaptionBrevityRule()
    {
        var design = Repo.DesignDocText;
        Assert.Contains("说明行长度（§6）", design);
        Assert.Contains("第一百五十五轮", design);
        Assert.Contains("distill", design);
        // 第一百六十轮：三种写法与「长度门不等于放得下」也要在设计基线里留痕。
        Assert.Contains("第一百六十轮", design);
        Assert.Contains("RunCaptionFit", design);
    }

    private static string FormName(CaptionForm form) => form switch
    {
        CaptionForm.Literal => "字面量",
        CaptionForm.BottomHostCallSite => "底部区调用点",
        CaptionForm.SharedConstant => "共享常量",
        CaptionForm.HintRowCallSite => "提示行工厂调用点",
        _ => form.ToString(),
    };
}
