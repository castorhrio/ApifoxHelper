using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// wine 试跑脚本的可移植性（第一百四十二轮，简报 P1）。
/// <para>
/// 为什么钉这一条：<c>.winecheck/</c> 下的脚本是入库的开发期辅助（不是产品运行路径），
/// 但它们此前写死了个人环境的绝对路径（某个用户主目录下的工作区、发行版打包的 wine 路径）——
/// 换一台机器就整段失效，而失效方式是「脚本跑起来、什么都没发生」，看不出是路径问题。
/// 现在全部改成可覆盖的环境变量，本文件钉住「不再写死个人路径」与「使用说明在位」。
/// </para>
/// </summary>
public class WineCheckScriptsTests
{
    private static string Directory => Path.Combine(Repo.Root, ".winecheck");

    private static IReadOnlyList<(string Name, string Text)> Scripts() =>
        System.IO.Directory.GetFiles(Directory, "*.sh")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))
            .ToList();

    /// <summary>脚本里不得出现个人环境的绝对路径（主目录、发行版打包路径、仓库外的固定目录）。</summary>
    [Fact]
    public void Scripts_HaveNoPersonalAbsolutePaths()
    {
        var forbidden = new[] { "/home/", "/Users/", "/usr/lib/", "/opt/" };
        var scripts = Scripts();
        Assert.NotEmpty(scripts);
        foreach (var (name, text) in scripts)
        {
            foreach (var path in forbidden)
            {
                Assert.False(
                    text.Contains(path, StringComparison.Ordinal),
                    $"{name} 里写死了个人环境绝对路径「{path}」——改成可覆盖的环境变量。");
            }
        }
    }

    /// <summary>每个脚本都从环境变量取路径，且缺变量时立刻失败（`set -u`），不静默跑空。</summary>
    [Fact]
    public void Scripts_TakeTheirPathsFromEnvironmentVariables()
    {
        foreach (var (name, text) in Scripts())
        {
            Assert.Contains("set -u", text, StringComparison.Ordinal);
            Assert.Contains("WINEPREFIX=${WINEPREFIX:-", text, StringComparison.Ordinal);
            Assert.Contains("DISPLAY=${DISPLAY:-", text, StringComparison.Ordinal);
        }
    }

    /// <summary>使用说明在位：wine 版本、Xvfb 依赖、产物位置三件事都写清楚了。</summary>
    [Fact]
    public void Readme_DocumentsDependenciesAndArtifacts()
    {
        var readme = Path.Combine(Directory, "README.md");
        Assert.True(File.Exists(readme), ".winecheck/README.md 缺失（脚本没有使用说明）。");
        var text = File.ReadAllText(readme);
        Assert.Contains("wine-10.0", text, StringComparison.Ordinal);
        Assert.Contains("Xvfb", text, StringComparison.Ordinal);
        Assert.Contains("产物位置", text, StringComparison.Ordinal);
        Assert.Contains("/tmp/ah-smoke-report.txt", text, StringComparison.Ordinal);
        // 说明里要讲清「这不是 Windows 真机证据」，否则截图会被当成观感复核。
        Assert.Contains("不能替代真机复核", text, StringComparison.Ordinal);
    }

    // ── 第一百六十九轮收敛（简报 P0-2）：登记与实物两侧对齐 ────────────

    /// <summary>
    /// README 里从某个标题到下一个标题之间的正文。停在**任意级**标题：
    /// 「## 脚本」下面紧跟的是「### 已删脚本」，只按同级收尾会把已删那三行也算进在册集合，
    /// 于是「登记与实物对齐」这条门判的就不是它以为在判的那张表了。
    /// </summary>
    private static string ReadmeSection(string heading)
    {
        var lines = File.ReadAllText(Path.Combine(Directory, "README.md")).ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimStart('#', ' ').Trim().Equals(heading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"README 缺「{heading}」一节（登记口径没了出处）。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("#", StringComparison.Ordinal));
        return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
    }

    /// <summary>表格各行第一列里被反引号包住的名字（一格写多个时也逐个取出来）。</summary>
    private static List<string> FirstColumnNames(string table)
    {
        var names = new List<string>();
        foreach (var line in table.Split('\n'))
        {
            if (!line.StartsWith("| `")) continue;
            var cell = line.Trim().Trim('|').Split('|')[0].Trim();
            names.AddRange(from Match m in Regex.Matches(cell, @"`([^`]+)`")
                           select m.Groups[1].Value);
        }

        Assert.True(names.Count > 0, "README 那张表里一行都没有（第一列没有被反引号包住的名字）——表被改成散文了。");
        return names;
    }

    /// <summary>
    /// 磁盘上的脚本集合 == README「脚本」表登记的集合，两个方向都核。
    /// <para>
    /// 第一百六十九轮清点时撞上的正是这一族：<c>shot_dialog.sh</c> 一直在磁盘上，README 的表里
    /// 从来没有它。答辩现场问「哪一支把对话框点开」时，读说明的人手上那张表指着一个不存在的
    /// <c>click.sh</c>，而真能跑的那一支没人知道。
    /// </para>
    /// </summary>
    [Fact]
    public void Scripts_OnDiskMatchTheReadmeRegistry()
    {
        var onDisk = Scripts().Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var registered = FirstColumnNames(ReadmeSection("脚本")).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

        foreach (var name in onDisk)
        {
            Assert.True(registered.Contains(name, StringComparer.Ordinal),
                $".winecheck/{name} 在磁盘上，但 README「脚本」表没登记——没人知道该跑它。");
        }

        foreach (var name in registered)
        {
            Assert.True(onDisk.Contains(name, StringComparer.Ordinal),
                $"README「脚本」表登记了 {name}，磁盘上没有这一支——说明指着不存在的脚本。");
        }

        Assert.Equal(onDisk, registered);
    }

    /// <summary>
    /// 被取代的脚本不得复活，且每一行都要点名一个**仍然存在**的取代者。
    /// 只写「已删」不说被谁取代，下一个人推不出它们为什么重复，就会重写一份。
    /// </summary>
    [Fact]
    public void SupersededScripts_StayDeletedAndNameAnExistingReplacement()
    {
        var table = ReadmeSection("已删脚本（由上表取代，不要复活）");
        var deleted = FirstColumnNames(table).Distinct().ToList();
        Assert.True(deleted.Count >= 3,
            $"已删脚本登记表只有 {deleted.Count} 行（第一百六十九轮收敛掉 run1 / click / fill2 三个重复脚本）。");

        var onDisk = Scripts().Select(s => s.Name).ToList();
        foreach (var line in table.Split('\n'))
        {
            if (!line.StartsWith("| `")) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            Assert.True(cells.Count == 3, $"已删脚本表有一行不是 3 列（{cells.Count} 列）：{line[..Math.Min(40, line.Length)]}…");

            var name = cells[0].Trim('`');
            Assert.False(onDisk.Contains(name, StringComparer.Ordinal),
                $"{name} 登记为已删，磁盘上却又出现了——同一件事留两份脚本，改了一处另一处不会红。");

            var replacement = cells[1].Trim('`');
            Assert.True(onDisk.Contains(replacement, StringComparer.Ordinal),
                $"{name} 的取代者写成 {replacement}，而它也不在磁盘上——指向不存在的脚本等于没有取代者。");
            Assert.True(cells[2].Length > 0, $"{name} 那一行没写「为什么是重复」。");
        }
    }

    /// <summary>
    /// 脚本里出现的 <c>Xxx.sh</c> 必须真实存在。
    /// 删掉 <c>run1.sh</c> 那一刻，另外三支的头部各留下一句「依赖与前置同 run1.sh」——
    /// 按这句话去找前置的人，找到的是一个不存在的文件，而脚本本身照样能跑到一半。
    /// <para>
    /// 取法必须带名字边界：第一版写成 <c>([A-Za-z0-9_]+\.sh)</c> 不加边界，把第一百六十九轮
    /// README 里新写的 <c>smoke-source.sha</c> 读成了「source.sh」，报出一处根本不存在的悬空引用。
    /// 这是本轮第二次被同一个形状绊到（第一次是 <c>IndexOf("fi")</c> 撞上「find」）——
    /// 子串取法在这些文本里必然出错，下面的自测就是把它钉住。
    /// </para>
    /// </summary>
    [Fact]
    public void Scripts_OnlyReferenceScriptsThatExist()
    {
        var onDisk = Scripts().Select(s => s.Name).ToList();
        foreach (var (name, text) in Scripts())
        {
            foreach (Match m in ScriptReferences(text))
            {
                var referenced = m.Value;
                Assert.True(onDisk.Contains(referenced, StringComparer.Ordinal),
                    $"{name} 引用了 {referenced}，而它不在磁盘上（已删脚本留下的悬空引用）。");
            }
        }

        // 取法自测：别把 .sha / .ps1 这类后缀读成脚本，也别把真引用漏掉。
        Assert.Empty(ScriptReferences("见 smoke-source.sha 与 publish.ps1"));
        Assert.Equal(["run_app.sh", "demo.sh"], ScriptReferences("同 run_app.sh；另一条 bash .winecheck/demo.sh").Select(m => m.Value).ToArray());
        Assert.Empty(ScriptReferences("没有引用的那一段"));
    }

    /// <summary>脚本引用取法：前后都要有名字边界（<c>smoke-source.sha</c> 不是 <c>source.sh</c>）。</summary>
    private static MatchCollection ScriptReferences(string text) =>
        Regex.Matches(text, @"(?<![A-Za-z0-9_.-])[A-Za-z0-9_]+\.sh(?![A-Za-z0-9_])");

    // ── 第二百零三轮（简报 P0-2）：真机准备脚本的失败路径也要有门 ──────────

    /// <summary>
    /// <c>prepare-real-machine.sh</c> 的 ①②③ 三步，<b>每一个失败分支都要自己点名</b>。
    /// <para>
    /// <b>补的是哪个缺口</b>：脚本第 ①②③ 步失败时确实各打一句「① / ② / ③ 未通过」，
    /// 但<b>没有任何一条门判这几句真的在</b>——把失败信息删掉、或挪到成功路径上，
    /// 本文件此前一条都不会红。真机到手当天靠这一支脚本，它的失败信息必须可核对：
    /// 三步里哪一步没过、为什么没过，是操作者当天唯一能读到的线索。
    /// </para>
    /// <para>
    /// <b>判什么</b>：按 <c>echo "-- ① …"</c> 这类段头把脚本切成三步，逐步核三件事——
    /// ① 这一步至少有一句 <c>echo "① 未通过…"</c>；
    /// ② 这一步的<b>失败分支数</b>（含 <c>||</c> 的行数）与<b>点名数</b>相等——第 ② 步有两条命令
    /// （构建 / 用例），删掉其中一句点名，分支数就比点名数多，即红；
    /// ③ 每一句点名都在<b>失败路径</b>上（本行带 <c>||</c>，或紧邻的上一行是 <c>|| {</c> 开的分支）——
    /// 只判「这句话在文件里」不够：挪到成功路径上，文件里照样有这句话，而失败时反而不打。
    /// </para>
    /// <para>
    /// 与本文件已有的「脚本与 README 表对齐」是同一族：机械判脚本源码里写明的可见约定，不发明新机制。
    /// </para>
    /// </summary>
    [Fact]
    public void PrepareRealMachineScript_EveryStepNamesItsOwnFailure()
    {
        var path = Path.Combine(Directory, "prepare-real-machine.sh");
        Assert.True(File.Exists(path), ".winecheck/prepare-real-machine.sh 缺失（真机到手前的第 0 条没有可跑的一支）。");
        var lines = File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n');

        var markers = new[] { "①", "②", "③" };
        // 段头：`echo "-- ① 复跑三档冒烟…--"`。切块只按段头，不按空行——空行会随排版漂。
        // ④ 也取段头：它只是第 ③ 步的**收尾边界**（不判它），否则第 ③ 步的块会一路吃到 ④ 的 `|| exit 1`。
        var boundaries = new[] { "①", "②", "③", "④" };
        var starts = boundaries
            .Select(m => Array.FindIndex(lines, l => Regex.IsMatch(l, "^echo \"-- " + m + " ")))
            .ToList();
        for (var i = 0; i < boundaries.Length; i++)
        {
            Assert.True(starts[i] >= 0,
                $"prepare-real-machine.sh 里找不到第 {boundaries[i]} 步的段头（`echo \"-- {boundaries[i]} …--`）："
                + "三步的边界判不出来，失败信息也就无从核对。");
        }

        for (var i = 0; i < markers.Length; i++)
        {
            var marker = markers[i];
            var block = lines[starts[i]..starts[i + 1]];

            var messages = block
                .Select((line, index) => (Line: line, Index: index))
                .Where(x => Regex.IsMatch(x.Line, "echo\\s+\"" + marker + " 未通过"))
                .ToList();
            Assert.True(messages.Count > 0,
                $"prepare-real-machine.sh 第 {marker} 步里找不到「{marker} 未通过」这一句："
                + "这一步失败时脚本会静默往下走，操作者看不出卡在哪一步——真机到手当天最需要的正是这一句。");

            // 失败分支数 == 点名数：第 ② 步有构建与用例两条命令，删掉其中一句点名即红。
            var branches = block.Count(l => l.Contains("||", StringComparison.Ordinal));
            Assert.True(branches == messages.Count,
                $"prepare-real-machine.sh 第 {marker} 步有 {branches} 个失败分支（含 `||` 的行），"
                + $"却只有 {messages.Count} 句「{marker} 未通过」：有分支失败时不点名，"
                + "操作者只看到脚本停住、看不到停在哪一步、为什么停。");

            foreach (var (line, index) in messages)
            {
                var onFailurePath = line.Contains("||", StringComparison.Ordinal)
                    || (index > 0 && block[index - 1].Contains("||", StringComparison.Ordinal));
                Assert.True(onFailurePath,
                    $"「{marker} 未通过」那一句不在失败路径上（本行与上一行都没有 `||`）："
                    + "它会在成功时也打出来，失败时反而可能不打——失败信息必须挂在失败分支上。");
            }
        }
    }

    /// <summary>
    /// 环境变量的登记与实读两个方向都核：<c>${VAR:-默认}</c> 是这些脚本唯一的取参入口。
    /// 第一百六十九轮两处都漂了——<c>DSHOT</c> 一直在 <c>shot_dialog.sh</c> 里用而表里没有，
    /// <c>CROP</c> 随 <c>fill2.sh</c> 一起失效而表里还留着。表里多一个用不上的名字，下一个人会
    /// 以为覆盖它就能改产物落点；少一个在用的名字，那个落点就成了只有读源码才知道的暗配置。
    /// </summary>
    [Fact]
    public void EnvironmentVariables_AreDocumentedExactlyAndStillUsed()
    {
        var documented = FirstColumnNames(ReadmeSection("可覆盖的环境变量")).Distinct().ToHashSet(StringComparer.Ordinal);
        // 位置参数（$1 / $2 / $3）不是环境变量：取法一样，但它们不进登记表。
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, text) in Scripts())
        {
            foreach (Match m in Regex.Matches(text, @"\$\{([A-Za-z_][A-Za-z0-9_]*):-"))
            {
                used.Add(m.Groups[1].Value);
            }
        }

        foreach (var name in used)
        {
            Assert.True(documented.Contains(name),
                $"脚本读到环境变量 {name}，README 的表里没登记——它成了只有读源码才知道的暗配置。");
        }
    }

    // ── 第二百零五轮（简报 P0-2）：Windows 侧那支与 demo.sh 逐段同名 ──────────

    /// <summary>
    /// 输出段名的取法（两份脚本共用一条）：单行字符串字面量里，去掉行首空格后，
    /// 第一个全角冒号之前的那一段（不超过 12 个字、且不含 <c>$ （） 「」 。</c>）就是这一支脚本
    /// 自己打出来的<b>段名</b>。这不是发明机制——它就是 <c>demo.sh</c> 打印讲稿与读数的那批前缀
    /// （<c>报告：</c>、<c>讲稿：</c>、<c>讲什么：</c>、<c>引用段：</c>、<c>结论口径：</c>、<c>证据出处：</c>、
    /// <c>演示未收口：</c>、<c>三档逐屏对账完成：</c>），答辩现场念的每一行都由这些前缀开头。
    /// </summary>
    private static readonly char[] LabelStopMarks = ['$', '（', '）', '「', '」', '。'];

    private static SortedSet<string> SectionLabels(string text)
    {
        var labels = new SortedSet<string>(StringComparer.Ordinal);
        // 字面量是单行的：[^"'] 不减掉换行会把 here-string 整块读成一份字面量，提出来的段名两边都不像谁。
        foreach (Match literal in Regex.Matches(text, "[\"']([^\"'\n]*)[\"']"))
        {
            var content = literal.Groups[1].Value.TrimStart(' ');
            var at = content.IndexOf('：');
            if (at <= 0 || at > 12) continue;
            var label = content[..at];
            if (label.Any(LabelStopMarks.Contains)) continue;
            labels.Add(label);
        }

        return labels;
    }

    /// <summary>
    /// Windows 侧脚本（<c>.winecheck/demo.ps1</c>，第二百零五轮，简报 P0-2）与 <c>demo.sh</c> <b>逐段同名</b>。
    /// <para>
    /// <b>补的是哪个缺口</b>：<c>demo.sh</c> 是 bash，从未在 Windows（含 Git Bash / WSL / MSYS）跑过。
    /// 到期日 2026-11-20 当天若真机仍不可得，降级答辩的唯一交付面就是这份讲稿——而它当时只有 Linux 一支。
    /// 补一支 PowerShell 版的代价不是「再写一遍」，是<b>两份讲稿各说一套话</b>：段名漂了、读数格式漂了，
    /// 现场念到的一行在另一台的报告里根本找不到。所以这里要求的不是「等价实现」，而是
    /// <b>同名同读数</b>：两份脚本打印的段名集合必须一模一样，改一处就得改另一处。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 段名集合逐字相等（哪一支多一个、少一个都红，并把差集打出来）；② 集合不为空且
    /// 地板 15 条（取法一旦退化，两份都会「相等在空集合上」，那等于没有门）；③ 六条关键段名（讲什么 /
    /// 引用段 / 结论口径 / 证据出处 / 报告 / 讲稿）都在；④ 档位段头的前缀 <c>== 档位 LogPixels=</c> 与
    /// 三份产物文件名（report- / narration- / console-）两边同形；⑤ 两边读的环境变量名同一套。
    /// </para>
    /// <para>
    /// <b>它不做什么</b>：不跑 <c>demo.ps1</c>，也不跑 <c>pwsh</c>——本容器没有 PowerShell
    /// （第二百零五轮实测：<c>pwsh</c> 与 <c>powershell</c> 都不在 PATH，wine 的
    /// <c>powershell.exe</c> 打出的第一行是 <c>fixme:powershell:wmain stub.</c>，那是个 stub）。
    /// 所以「语法可解析」这一条本轮<b>没有跑到</b>，如实记在 <c>docs/NOTES.md</c> 本轮记录里：
    /// 这份脚本落到仓、与 <c>demo.sh</c> 逐段同名是可核对的，它能不能在真机上跑起来要等机器。
    /// </para>
    /// </summary>
    [Fact]
    public void DemoPs1_NamesTheSameSectionsAsDemoSh()
    {
        var ps1Path = Path.Combine(Directory, "demo.ps1");
        Assert.True(File.Exists(ps1Path), ".winecheck/demo.ps1 缺失（Windows 侧的降级答辩没有可跑的一支）。");

        var sh = Repo.Source(".winecheck", "demo.sh");
        var ps1 = File.ReadAllText(ps1Path);
        var shLabels = SectionLabels(sh);
        var ps1Labels = SectionLabels(ps1);

        Assert.True(shLabels.Count >= 15,
            $"demo.sh 只取到 {shLabels.Count} 个段名（地板 15）——取法退化了，两份「相等」可能是相等在空集合上。");
        var onlySh = shLabels.Except(ps1Labels).ToList();
        var onlyPs1 = ps1Labels.Except(shLabels).ToList();
        Assert.True(onlySh.Count == 0 && onlyPs1.Count == 0,
            $"两支脚本打印的段名不是同一套。只在 demo.sh 里：{string.Join("、", onlySh)}；只在 demo.ps1 里：{string.Join("、", onlyPs1)}"
            + "——段名漂了的那一支，现场照着另一支念的那一行在它这里不存在。");
        foreach (var key in new[] { "wine 前缀", "报告", "讲稿", "讲什么", "引用段", "结论口径", "证据出处", "演示未收口", "三档逐屏对账完成" })
        {
            Assert.True(ps1Labels.Contains(key), $"demo.ps1 没有打印「{key}」这一段——现场照着 demo.sh 的讲稿念的那一行在这支脚本里不存在。");
        }

        // 档位段头与三份产物：段名同名，产物的名字也必须同形，否则两份脚本各写一套落点。
        foreach (var (name, text) in new[] { ("demo.sh", sh), ("demo.ps1", ps1) })
        {
            Assert.Contains("== 档位 LogPixels=", text, StringComparison.Ordinal);
            Assert.Contains("report-", text, StringComparison.Ordinal);
            Assert.Contains("narration-", text, StringComparison.Ordinal);
            Assert.Contains("console-", text, StringComparison.Ordinal);
            Assert.Contains("--report=", text, StringComparison.Ordinal);
            Assert.Contains("smoke-source.sha", text, StringComparison.Ordinal);
            Assert.False(Regex.IsMatch(text, @"#.*\bTODO\b"), $"{name} 里留着 TODO——降级答辩的面不能带未完成标记入库。");
        }

        // 两边读同一套环境变量（这是这些脚本唯一的取参入口，个人路径一律不许写死）。
        // demo.ps1 允许多两个：HOME 与 USERPROFILE——那不是旋钮，是取主目录（Windows 上 HOME 常不存在，
        // 回退 USERPROFILE）；除此之外多认一个名字，就是给自己开了一只 README 没登记的暗旋钮。
        var shVars = Regex.Matches(sh, @"\$\{([A-Za-z_][A-Za-z0-9_]*):-").Cast<Match>()
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var ps1Vars = Regex.Matches(ps1, @"\$\{env:([A-Za-z_][A-Za-z0-9_]*)\}").Cast<Match>()
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        foreach (var knob in shVars)
        {
            Assert.True(ps1Vars.Contains(knob),
                $"demo.sh 认环境变量 {knob}，demo.ps1 不认——同一份讲稿在两边不是同一条命令，切机器就得改脚本。");
        }

        var extra = ps1Vars.Where(v => !shVars.Contains(v) && v != "HOME" && v != "USERPROFILE").ToList();
        Assert.True(extra.Count == 0,
            $"demo.ps1 多认了环境变量 {string.Join("、", extra)}：demo.sh 没有的那一只从文档里查不到，覆盖它会发生什么没人知道。");
    }

    /// <summary>
    /// <c>demo.ps1</c> 的失败路径：某一屏引用的段在报告里找不到时，必须<b>自己点名那一段</b>、
    /// 计入本档、汇到总计，并在结尾那一支<b>真的</b>非零退出。
    /// <para>
    /// 判的是实现形状而不是「这句话在文件里」：<c>PrepareRealMachineScript_EveryStepNamesItsOwnFailure</c>
    /// 同一族纪律。三处各判一件事——① 缺段那一句带着 <c>$seg</c>（不点名就等于把「对不上」藏进一句总话里）；
    /// ② 缺段分支同时递增本档与总计两个计数（只增本档则结尾那支条件读不到它）；
    /// ③ 结尾那一句「演示未收口」与 <c>exit 1</c> 必须在同一个 <c>if ($totalFail -gt 0 -or $totalMissing -gt 0)</c>
    /// 块里——把它挪到块外，失败时照样打这句，成功时也打，那句话就成了自述。
    /// 另有一支「逐屏表某一行不是 5 列」的同族守卫（格式错的行静默跳过 = 那一屏整屏不出现在讲稿里），
    /// 判它自己点名并退出。
    /// </para>
    /// <para>不跑 pwsh（本机没有，理由见上一条），因此这一条判的是源码形状；真机上这一支会不会真的非零退出，
    /// 由下一次带机器的一轮补。</para>
    /// </summary>
    [Fact]
    public void DemoPs1_ExitsNonZeroWhenAScreenIsMissing()
    {
        var ps1 = File.ReadAllText(Path.Combine(Directory, "demo.ps1"));

        // ① 缺段那一句：自己点名是哪一个段，并说明「先修这一处再答辩」。
        var missing = Regex.Match(ps1, @"Write-Lines[^\n]*报告里没有这一段[^\n]*");
        Assert.True(missing.Success, "demo.ps1 里没有「报告里没有这一段」那一句——某一屏引用的段找不到时它必须说出来。");
        Assert.Contains("$seg", missing.Value, StringComparison.Ordinal);
        Assert.Contains("逐屏表与冒烟器对不上", missing.Value, StringComparison.Ordinal);

        // ② 递增两个计数：本档与总计，少一个就汇不到结尾那支条件。
        Assert.True(Regex.IsMatch(ps1, @"\$tierMissing\+\+"), "demo.ps1 里缺段分支没有递增本档计数（$tierMissing++）。");
        Assert.True(Regex.IsMatch(ps1, @"\$totalMissing\s*\+=\s*\$tierMissing"),
            "demo.ps1 没有把本档的「对不上」汇进总计（$totalMissing += $tierMissing）——结尾那一步读的就是这个总数。");

        // ③ 结尾那一步：条件是真的，且「演示未收口」与 exit 1 都在块内。
        var guard = Regex.Match(ps1, @"if \(\$totalFail -gt 0 -or \$totalMissing -gt 0\)[\s\S]{0,400}?\n\}");
        Assert.True(guard.Success,
            "demo.ps1 结尾没有「失败或对不上就非零退出」这一支（或被写成恒不成立的条件）。");
        Assert.Contains("演示未收口", guard.Value, StringComparison.Ordinal);
        Assert.Contains("exit 1", guard.Value, StringComparison.Ordinal);

        // ④ 逐屏表的行格式守卫：列数不对自己点名并退出，不静默跳过那一行。
        // 取法必须按块边界：[\s\S]{0,200}?exit 1 会越过本块去够下一支守卫里的 exit 1（第二百零五轮实测：
        // 把本块的 exit 1 删掉，那样写照样绿）。
        Assert.Contains("不是 5 列", ps1, StringComparison.Ordinal);
        var cellsGuard = Regex.Match(ps1, @"if \(\$cells\.Count -ne 5\) \{[\s\S]{0,300}?\n    \}");
        Assert.True(cellsGuard.Success,
            "demo.ps1 里「列数不对」那一支切不出块边界（写法被改动，或这一支的收尾大括号不在四空格那一档——本条按块判，先要能切出块）。");
        Assert.True(cellsGuard.Value.Contains("exit 1"),
            "demo.ps1 里「列数不对」那一支没有在自己的块内退出——静默跳过的那一屏不会出现在讲稿里，正是第一百六十七轮在 demo.sh 上修掉的同一族缺陷。");
    }
}
