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

    /// <summary>
    /// <b>驱动 wine 的脚本</b>（封闭名单）。
    /// <para>
    /// 第二百零八轮之前，下面那条门要求 <c>.winecheck/</c> 下<b>每一支</b> <c>.sh</c> 都设
    /// <c>WINEPREFIX</c> 与 <c>DISPLAY</c>。新增的 <c>collect-real-machine.sh</c> 撞上了这条：
    /// 它只读一个目录树、一个进程都不启动，照原样要求它设这两只旋钮，等于逼它留两只
    /// 「README 里得登记、但覆盖了什么没人知道」的假旋钮——正是同文件另一条门
    /// （<see cref="EnvironmentVariables_AreDocumentedExactlyAndStillUsed"/>）要防的那种暗旋钮。
    /// </para>
    /// <para>
    /// 名单写成字面量而不是「从脚本里抓 wine 字样」，与本仓同一族做法一致（<c>Repo.ServiceDirectories</c>、
    /// <c>RealMachineEvidenceTests.ExpectedDirectories</c>）：<b>抓出来的名单会跟着源码漂</b>，
    /// 而这条门要防的恰恰是漂。名单的<b>单向</b>风险由 <see cref="WineInvocations"/> 兜住：
    /// 名单外那一支一旦真的调用起 wine / X，门就红并把它点名进名单。
    /// </para>
    /// </summary>
    private static readonly string[] WineDrivingScripts =
        ["run_app.sh", "shot_dialog.sh", "fill.sh", "demo.sh", "prepare-real-machine.sh"];

    /// <summary>「真的在驱动 wine」的形状：启动 wine 进程、问 wine 要路径、点窗口、改缩放档。</summary>
    private static readonly string[] WineInvocations =
        ["WINE_BIN", "winepath", "xdotool", "xvfb-run", "LogPixels"];

    /// <summary>
    /// 每个脚本都从环境变量取路径、缺变量时立刻失败（<c>set -u</c>），不静默跑空；
    /// 而 <c>WINEPREFIX</c> / <c>DISPLAY</c> 只对<b>真的驱动 wine</b> 的那几支是硬要求。
    /// </summary>
    [Fact]
    public void Scripts_TakeTheirPathsFromEnvironmentVariables()
    {
        var onDisk = Scripts().Select(s => s.Name).ToList();
        foreach (var name in WineDrivingScripts)
        {
            Assert.True(onDisk.Contains(name, StringComparer.Ordinal),
                $"wine 驱动名单里的 {name} 不在磁盘上了——名单要么跟上现实，要么这条门判的就不是它以为在判的那几支。");
        }

        foreach (var (name, text) in Scripts())
        {
            Assert.Contains("set -u", text, StringComparison.Ordinal);
            var driving = WineDrivingScripts.Contains(name, StringComparer.Ordinal);

            if (driving)
            {
                Assert.Contains("WINEPREFIX=${WINEPREFIX:-", text, StringComparison.Ordinal);
                Assert.Contains("DISPLAY=${DISPLAY:-", text, StringComparison.Ordinal);
                continue;
            }

            // 名单外：不碰 wine 是允许的，但路径仍不许写死——至少得有一只 ${VAR:-} 旋钮。
            var invoked = WineInvocations.Where(text.Contains).ToList();
            Assert.True(invoked.Count == 0,
                $"{name} 不在 wine 驱动名单里，却用到了 {string.Join("、", invoked)}："
                + "把它加进名单（它需要 WINEPREFIX 与 DISPLAY 的缺省），否则这一支换台机器就会静默跑空。");
            Assert.True(Regex.IsMatch(text, @"\$\{[A-Za-z_][A-Za-z0-9_]*:-"),
                $"{name} 既不驱动 wine，也没有任何 ${'{'}VAR:-默认{'}'} 旋钮："
                + "它的路径一定是写死的，换一台机器就撞「找不到文件」，而那句话看起来像脚本自己的错。");
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
    /// <b>它不做什么</b>：这一条只做两份文本的段名比对，不跑 <c>demo.ps1</c>、也不解析它。
    /// 「语法可解析」与「整支跑通」是第二百零七轮补上的另两件事：<see cref="DemoPs1_ParsesWithARealPowerShellParserWhenOneIsHere"/>
    /// 在有解析器的机器上真解析（本容器有 dotnet 全局工具 powershell 7.6.9，落在 <c>~/.dotnet/tools/pwsh</c>
    /// 而不在 PATH——第二百零五轮那句「容器没有 PowerShell」是照着 PATH 说的，当时那条判据成立、结论不成立），
    /// <see cref="DemoPs1_Structure_IsBalanced"/> 在不依赖外部工具的机器上守住结构，
    /// 跑到哪一层的逐条记录在 <c>.winecheck/README.md</c>「Windows 侧那支」一节。
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
    /// <para>本条判的是<b>源码形状</b>（三个计数与两处退出都得在文本里真实存在）。再往上的两层第二百零七轮
    /// 各自补上了：语法由 <see cref="DemoPs1_ParsesWithARealPowerShellParserWhenOneIsHere"/> 用真解析器判，
    /// 「整支跑通」由那一次 pwsh 7.6.9 + wine 的实跑判（读数与修掉的指纹分歧记在
    /// <c>.winecheck/README.md</c>「Windows 侧那支」一节）。<b>真机上这一支的非零退出仍未验</b>——
    /// 那一台机器还没到手，G-D5 仍 <c>[ ]</c>。</para>
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

    // ── 第二百三十二轮（简报 P0-1 降级路径）：讲稿必须从空文件写起 ──────────

    /// <summary>
    /// 两支演示脚本写讲稿之前都必须<b>先把那一份截成空文件</b>。
    /// <para>
    /// <b>补的是哪个缺口</b>：<c>demo.sh</c> 写讲稿前有一句 <c>: &gt;"$NARRATION"</c>，<c>demo.ps1</c> 没有——
    /// 而它的 <c>Write-Lines</c> 是<b>追加</b>写（<c>File.AppendAllText</c>），只清报告、不清讲稿。
    /// 于是「先跑 <c>demo.sh</c> 再跑 <c>demo.ps1</c>」（正是答辩当天动作单的顺序，两支的 <c>OUT</c> 缺省都是
    /// <c>/tmp/ah-demo</c>）时，<c>narration-&lt;档位&gt;.txt</c> 里留着上一份讲稿，这一档的讲稿变成两份叠在一起：
    /// 第二百三十二轮实测 <b>98 行 → 196 行</b>，现场会把每一屏念两遍，而
    /// <c>.winecheck/README.md</c>「Windows 侧那支」第 3 条那句「100% 档讲稿与 <c>demo.sh</c> 逐字节相同」
    /// 也当场不成立。修法就是补上同一件事（先截空再追加），本条钉住它不许再掉。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① <c>demo.sh</c> 里截断那一句出现在第一次追加之前；② <c>demo.ps1</c> 里截断那一句
    /// 出现在第一次 <c>Write-Lines $narration</c> 之前；③ 自测——把两支的截断各删掉一次，判据必须当场报出来
    /// （否则「它绿着」只说明取法退化，不说明脚本对）。
    /// </para>
    /// <para>
    /// <b>它不做什么</b>：不跑这两支脚本（跑起来要 wine + Xvfb + 已发布的冒烟器），也不判讲稿内容对不对。
    /// 「修好之后 96 档讲稿与 <c>demo.sh</c> 逐字节相同」是第二百三十二轮当场跑出来的读数，
    /// 记在 <c>.winecheck/README.md</c>「Windows 侧那支」第 5 条，不是本条判的。
    /// </para>
    /// </summary>
    [Fact]
    public void DemoPs1_TruncatesTheNarrationBeforeWriting()
    {
        var sh = Repo.Source(".winecheck", "demo.sh");
        var ps1 = File.ReadAllText(Path.Combine(Directory, "demo.ps1"));

        // ① demo.sh：`: >"$NARRATION"` 在第一次 `>>"$NARRATION"` 之前。
        Assert.True(TruncatesBeforeAppending(sh, @":\s*>""\$NARRATION""", @">>""$NARRATION"""),
            "demo.sh 里「先把讲稿截空」那一句不在第一次追加之前——讲稿会叠在上一份后面。");

        // ② demo.ps1：截断在第一次 Write-Lines $narration 之前。
        Assert.True(TruncatesBeforeAppending(ps1, @"WriteAllText\(\s*\$narration\s*,\s*''", "Write-Lines $narration"),
            "demo.ps1 写讲稿前没有先把 narration-<档位>.txt 截成空文件：Write-Lines 是追加写，"
            + "同一份 OUT 目录里先跑过 demo.sh 时这一档的讲稿会变成两份叠在一起（实测 98 行 → 196 行）。");

        // ③ 自测：两支各删掉截断那一句，判据必须报出来。
        var shBroken = sh.Replace(": >\"$NARRATION\"", "");
        Assert.NotEqual(sh, shBroken);
        Assert.False(TruncatesBeforeAppending(shBroken, @":\s*>""\$NARRATION""", @">>""$NARRATION"""),
            "自测失败：把 demo.sh 的截断删掉之后判据仍然绿——取法退化了，它判不到它该判的东西。");

        var ps1Broken = ps1.Replace("[System.IO.File]::WriteAllText($narration, '', $Utf8NoBom)", "");
        Assert.NotEqual(ps1, ps1Broken);
        Assert.False(TruncatesBeforeAppending(ps1Broken, @"WriteAllText\(\s*\$narration\s*,\s*''", "Write-Lines $narration"),
            "自测失败：把 demo.ps1 的截断删掉之后判据仍然绿——取法退化了，它判不到它该判的东西。");
    }

    /// <summary>
    /// 「先截空、再追加」的形状判据：截断那一句必须出现在第一次追加之前。
    /// 只判先后，不判写法——两支脚本各用各的写法（bash 的 <c>: &gt;</c> 与 .NET 的 <c>WriteAllText</c>），
    /// 判的是同一件事：这一份文件是从空的开始写的。
    /// </summary>
    private static bool TruncatesBeforeAppending(string text, string truncationPattern, string firstAppendMarker)
    {
        var appendAt = text.IndexOf(firstAppendMarker, StringComparison.Ordinal);
        if (appendAt < 0) return false;
        var truncation = Regex.Match(text, truncationPattern);
        return truncation.Success && truncation.Index < appendAt;
    }

    // ── 第二百零七轮（简报 P0-2）：这一支真的跑过、也能被真的解析 ──────────

    /// <summary>
    /// <c>demo.ps1</c> 的<b>纯文本级</b>结构门：括号配平、引号与 here-string 闭合、关键字块成块。
    /// <para>
    /// <b>补的是哪个缺口</b>：第二百零五轮到第二百零六轮之间，这一支只判过「与 demo.sh 逐段同名」——
    /// 那是两份文本在互相点头，<b>没有一条判过它自己是不是一份能解析的 PowerShell</b>。
    /// 第二百零七轮第一次真跑之前，谁也说不出它能不能跑起来。
    /// </para>
    /// <para>
    /// <b>取法</b>：先把字符串字面量、here-string 与注释整段抹成空格（长度与换行位置不变），
    /// 再在剩下的「代码」上数 <c>() {} []</c> 的配平、检每一处 <c>'</c> / <c>"</c> / <c>@'</c> / <c>@"</c>
    /// 是否闭合、检每一个 <c>if / elseif / else / foreach / for / while / switch / try / catch / finally / function</c>
    /// 同一行是否开了块。不抹字符串就数括号，会被 <c>'（报告里没有这一段）'</c> 这类中文标点与
    /// 正则字面量里的 <c>\[</c> 骗到——那时红的是判据，不是脚本。
    /// </para>
    /// <para>
    /// <b>它判不到什么</b>（写在脸上，别拿它当「这一支能跑」）：不判 cmdlet 名存不存在、不判参数是否合法、
    /// 不判变量拼错、不判运行时行为，也不判 Windows PowerShell 5.1 与 pwsh 7 的语法差异。
    /// 「能跑」那一条在下面 <see cref="DemoPs1_ParsesWithARealPowerShellParserWhenOneIsHere"/>，
    /// 以及第二百零七轮那一次真跑（读数见 <c>.winecheck/reading.txt</c> 与 <c>docs/NOTES.md</c> 本轮记录）。
    /// </para>
    /// </summary>
    [Fact]
    public void DemoPs1_Structure_IsBalanced()
    {
        var ps1 = File.ReadAllText(Path.Combine(Directory, "demo.ps1"));
        var problems = PowerShellStructureProblems(ps1);
        Assert.True(problems.Count == 0,
            "demo.ps1 的结构过不去：" + string.Join(" ／ ", problems)
            + "。这一支是 Windows 侧答辩现场要跑的脚本——少一个 } 在 Linux 侧不会让任何别的门红，"
            + "到了现场才第一次报错就太晚了。");
    }

    /// <summary>
    /// <b>判据自己得是活的</b>：把 <c>demo.ps1</c> 故意改坏三处（删一个 <c>}</c>、删一个 <c>"</c>、
    /// 把 <c>foreach …) {</c> 的 <c>{</c> 抹掉），结构门必须逐条报出来。
    /// 第二百零五轮实测过一次反面：写法不当时「切不出块边界」的门会在<b>正确</b>的源码上红，
    /// 而真正的破坏反倒没人报。这条自测就是为了让「它绿着」不等于「它什么都看不见」。
    /// </summary>
    [Fact]
    public void DemoPs1_StructureDetector_ReportsTheBreaksItMustCatch()
    {
        var ps1 = File.ReadAllText(Path.Combine(Directory, "demo.ps1"));
        Assert.Empty(PowerShellStructureProblems(ps1)); // 前提：本体是干净的，下面的「变红」才只可能来自注入的破坏。

        var withoutBrace = ps1.Remove(ps1.LastIndexOf('}'), 1);
        Assert.NotEmpty(PowerShellStructureProblems(withoutBrace));

        // 删掉一条已知成对的单引号语句里的开引号：剩下的那个引号会一路报到行尾（未闭合字符串）。
        const string knownPair = "$ErrorActionPreference = 'Stop'";
        var at = ps1.IndexOf(knownPair, StringComparison.Ordinal);
        Assert.True(at >= 0, "取法退化：demo.ps1 里找不到那条语句，注入测试成了空转。");
        var brokenQuote = ps1.Remove(at + knownPair.IndexOf('\''), 1);
        Assert.NotEmpty(PowerShellStructureProblems(brokenQuote));

        var openBlock = Regex.Match(ps1, @"(?m)^(\s*foreach \(\$tier in [^\n]*?) \{$");
        Assert.True(openBlock.Success, "取法退化：找不到逐档循环的那一行，关键字成块这一条没在被测的东西上验过。");
        var headless = ps1.Replace(openBlock.Value, openBlock.Groups[1].Value);
        Assert.NotEmpty(PowerShellStructureProblems(headless));
    }

    /// <summary>
    /// 有 PowerShell 解析器时，<b>用真解析器</b>解析 <c>demo.ps1</c>，语法错误必须为 0。
    /// <para>
    /// 第二百零七轮起这一条在本容器是<b>活的</b>：容器里装了 dotnet 全局工具 powershell 7.6.9
    /// （<c>~/.dotnet/tools/pwsh</c>），实测 0 处语法错误、1560 个 token。上一轮的简报写
    /// 「容器无 PowerShell、这一层做不到」——那件事在这台机器上已经不成立了，所以这条门把它钉住。
    /// </para>
    /// <para>
    /// <b>它判到哪一层要说清</b>：<c>Parser::ParseFile</c> 只判语法（能不能解析成 AST），
    /// 不判 cmdlet 存不存在、不判参数、更不判 Windows PowerShell 5.1 的差异。真跑一遍是另一件事，
    /// 记在 <c>docs/NOTES.md</c> 本轮记录里（那一跑抓到的是源码指纹的区域序 / 字节序分歧，不是语法）。
    /// </para>
    /// <para>
    /// <b>没有解析器的那台机器上判不到</b>——这种情况不许静默绿：<c>.winecheck/README.md</c> 必须把
    /// 「语法由哪一台的哪个版本实测过、以及它不判行为」登记在案（下面那条 <c>..._ParseVerification_IsRegistered</c>
    /// 不分路径都判这份登记）。同 <c>HtmlWrapTests.ThisBrowser_CannotTell…</c> 一个做法：判不到就说判不到。
    /// </para>
    /// </summary>
    [Fact]
    public void DemoPs1_ParsesWithARealPowerShellParserWhenOneIsHere()
    {
        var parser = LocatePowerShell();
        if (parser is null) return; // 判不到：由登记在案那条门守着（它在任何机器上都判）。

        var path = Path.Combine(Directory, "demo.ps1").Replace('\'', ' ');
        var script =
            "$t=$null;$e=$null;" +
            "$null=[System.Management.Automation.Language.Parser]::ParseFile('" +
            path.Replace("'", "''") + "',[ref]$t,[ref]$e);" +
            "'ERRORS=' + $e.Count;" +
            "foreach($x in $e){'L' + $x.Extent.StartLineNumber + ' ' + $x.Message};" +
            "'TOKENS=' + $t.Count;'HOST=' + $PSVersionTable.PSVersion";

        var psi = new ProcessStartInfo(parser)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);

        var reported = Regex.Match(output, @"ERRORS=(\d+)");
        Assert.True(reported.Success,
            $"PowerShell 解析器没有报出台数（退出码 {process.ExitCode}）。stdout={output} stderr={error}");
        Assert.True(int.Parse(reported.Groups[1].Value) == 0,
            "demo.ps1 有语法错误，真解析器点名的位置："
            + string.Join(" ／ ", output.Split('\n').Where(l => l.StartsWith("L")).Select(l => l.Trim()).Take(5))
            + "。这一支要在 Windows 答辩现场第一次跑，语法错到那时才看见就晚了。");
        Assert.Matches(@"TOKENS=[1-9]\d{2,}", output); // 真读进去了几百个 token，不是「解析了空文件」。
    }

    /// <summary>
    /// <c>demo.ps1</c> 被验到哪一层，<c>.winecheck/README.md</c> 必须自己写清：哪个解析器、哪一个版本、
    /// 跑到的是「语法 + 整支跑通」还是只有语法，以及「这不等于 Windows 真机」。
    /// 不分路径都判——没有解析器的那台机器上，这份登记就是唯一的凭据。
    /// </summary>
    [Fact]
    public void DemoPs1_ParseVerification_IsRegisteredInTheReadme()
    {
        var readme = Repo.Source(".winecheck", "README.md");
        Assert.Contains("demo.ps1", readme, StringComparison.Ordinal);
        Assert.Matches(@"(PowerShell|pwsh)\s*7\.\d+", readme);
        Assert.Contains("语法", readme, StringComparison.Ordinal);
        Assert.Contains("真机", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// 两支演示脚本写出的<b>读数记录格式必须同源</b>：同一批键、同一份表头口径。
    /// 记录是门与文档对账的凭据（<c>ReadingConsistencyTests</c>），两边各写一种格式，
    /// 换一支脚本跑就变成「读数没有出处」——那正是本轮要修的断链点。
    /// </summary>
    [Fact]
    public void BothDemoScripts_WriteTheSameReadingRecord()
    {
        var sh = Repo.Source(".winecheck", "demo.sh");
        var ps1 = Repo.Source(".winecheck", "demo.ps1");
        foreach (var key in new[] { "script=", "tool=", "date=", "source=", "tier=", "section=" })
        {
            Assert.True(sh.Contains(key, StringComparison.Ordinal), $"demo.sh 写读数记录却没有 {key} 这一行。");
            Assert.True(ps1.Contains(key, StringComparison.Ordinal), $"demo.ps1 写读数记录却没有 {key} 这一行。");
        }

        // 逐段计数的口径也要同一条：只认带 [RunXxx] 的段标题（否则两份记录的段集合会各自漂）。
        Assert.Contains(@"\[Run[A-Za-z0-9_]+\]", sh, StringComparison.Ordinal);
        Assert.Contains(@"\[Run[A-Za-z0-9_]+\]", ps1, StringComparison.Ordinal);

        // 记录本体在不在、对不对，由 ReadingConsistencyTests 判；这里只钉「两支写得同源」。
        Assert.Contains("READING", sh, StringComparison.Ordinal);
        Assert.Contains("READING", ps1, StringComparison.Ordinal);
    }

    /// <summary>
    /// 纯文本级 PowerShell 结构检查（<see cref="DemoPs1_Structure_IsBalanced"/> 的实现）。
    /// 返回问题清单，空清单 = 过得去。
    /// </summary>
    private static List<string> PowerShellStructureProblems(string text)
    {
        var problems = new List<string>();
        var code = BlankPowerShellLiterals(text, problems);

        var stack = new Stack<(char Ch, int Line)>();
        var lines = code.Split('\n');
        for (var n = 0; n < lines.Length; n++)
        {
            foreach (var c in lines[n])
            {
                switch (c)
                {
                    case '(' or '{' or '[':
                        stack.Push((c, n + 1));
                        break;
                    case ')' or '}' or ']':
                        var closer = c switch { ')' => '(', '}' => '{', _ => '[' };
                        if (stack.Count == 0) problems.Add($"第 {n + 1} 行的 {c} 没有配对的开括号");
                        else
                        {
                            var (open, openLine) = stack.Pop();
                            if (open != closer) problems.Add($"第 {n + 1} 行的 {c} 对不上第 {openLine} 行的 {open}");
                        }

                        break;
                }
            }
        }

        while (stack.Count > 0)
        {
            var (open, openLine) = stack.Pop();
            problems.Add($"第 {openLine} 行的 {open} 到文件末尾还没合上");
        }

        // 关键字必须成块：同一行没有 { 就是块被抹掉了（现场报错会是「表达式或语句中缺少 }」这类含糊话）。
        foreach (Match m in Regex.Matches(code,
                     @"(?<![A-Za-z0-9_$.])(function|if|elseif|else|foreach|for|while|switch|try|catch|finally)(?![A-Za-z0-9_$])"))
        {
            var end = code.IndexOf('\n', m.Index);
            var tail = code[(m.Index + m.Length)..(end < 0 ? code.Length : end)];
            if (tail.Contains('{') || tail.Contains('}')) continue;
            problems.Add($"关键字 {m.Groups[1].Value} 所在那一行既没有 {{ 也没有 }}（块被写坏了？）");
        }

        return problems;
    }

    /// <summary>
    /// 把 PowerShell 的字符串字面量、here-string 与注释整段抹成空格——<b>长度与换行位置一字不变</b>，
    /// 这样抹完之后按行数出来的行号仍然是原文件的行号。闭合不了的引号 / here-string 直接记成问题。
    /// </summary>
    private static string BlankPowerShellLiterals(string text, List<string> problems)
    {
        var blanked = text.ToCharArray();
        var line = 1;
        var i = 0;

        void BlankRange(int from, int to)
        {
            for (var k = from; k < to && k < blanked.Length; k++)
            {
                if (blanked[k] != '\n') blanked[k] = ' ';
            }
        }

        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\n') { line++; i++; continue; }

            // 块注释 <# … #>
            if (c == '<' && i + 1 < text.Length && text[i + 1] == '#')
            {
                var end = text.IndexOf("#>", i + 2, StringComparison.Ordinal);
                if (end < 0) { problems.Add($"第 {line} 行开的 <# 块注释到文件末尾还没合上"); break; }
                BlankRange(i, end + 2);
                line += CountInRange(text, i, end + 2, '\n');
                i = end + 2;
                continue;
            }

            // 行注释 #…
            if (c == '#')
            {
                var end = text.IndexOf('\n', i);
                if (end < 0) end = text.Length;
                BlankRange(i, end);
                i = end;
                continue;
            }

            // here-string @' … '@ / @" … "@（收尾必须顶在一行的行首，与 PowerShell 一致）
            if (c == '@' && i + 1 < text.Length && (text[i + 1] == '\'' || text[i + 1] == '"'))
            {
                var quote = text[i + 1];
                var closer = quote + "@";
                var fromContent = i + 2;
                var end = -1;
                for (var probe = text.IndexOf('\n', fromContent) + 1; probe > 0 && probe < text.Length;
                     probe = text.IndexOf('\n', probe) + 1)
                {
                    if (text.AsSpan(probe).StartsWith(closer.AsSpan(), StringComparison.Ordinal)) { end = probe; break; }
                }

                if (end < 0) { problems.Add($"第 {line} 行开的 here-string {quote}@ 没有顶格收尾"); break; }
                var stop = end + closer.Length;
                BlankRange(i, stop);
                line += CountInRange(text, i, stop, '\n');
                i = stop;
                continue;
            }

            // 单引号串（'' 是转义），双引号串（` 是转义）
            if (c is '\'' or '"')
            {
                var start = i;
                var k = i + 1;
                var closed = false;
                while (k < text.Length)
                {
                    var next = k + 1 < text.Length ? text[k + 1] : '\0';
                    // 转义：'' 与 `` （收尾的那个引号后面不跟同类引号，所以下一条才是收尾）
                    if (text[k] == c && next == c) { k += 2; continue; }
                    if (c == '"' && text[k] == '`') { k += 2; continue; }
                    if (text[k] == '\n') break; // 单行串不跨行
                    if (text[k] == c) { closed = true; k++; break; }
                    k++;
                }

                if (!closed)
                {
                    problems.Add($"第 {line} 行开的 {c} 字符串没在本行合上");
                    i = k;
                    continue;
                }

                BlankRange(start, k);
                i = k;
                continue;
            }

            i++;
        }

        return new string(blanked);
    }

    /// <summary>一段区间里某个字符有几个（抹字符串时要跟着挪行号）。</summary>
    private static int CountInRange(string text, int from, int to, char needle)
    {
        var n = 0;
        for (var i = from; i < to && i < text.Length; i++) if (text[i] == needle) n++;
        return n;
    }

    /// <summary>
    /// 找一台 PowerShell：<c>PATH</c> 上的 <c>pwsh</c> / <c>powershell</c>，加上 dotnet 全局工具目录
    /// 与 Windows 的两个常规装法。找不到返回 null——调用方据此走「这台机器判不到」那一支。
    /// </summary>
    private static string? LocatePowerShell()
    {
        var names = new[] { "pwsh", "powershell" };
        var dirs = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path)) dirs.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home)) dirs.Add(Path.Combine(home, ".dotnet", "tools"));
        dirs.AddRange(["/usr/bin", "/usr/local/bin",
            @"C:\Program Files\PowerShell\7", @"C:\Program Files (x86)\PowerShell\7"]);

        foreach (var dir in dirs.Distinct(StringComparer.Ordinal))
        {
            foreach (var name in names)
            {
                foreach (var leaf in new[] { name, name + ".exe" })
                {
                    try
                    {
                        var candidate = Path.Combine(dir, leaf);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (ArgumentException)
                    {
                        // 这一台上不合法的目录写法（比如 Windows 路径出现在 Linux 上）跳过就行。
                    }
                }
            }
        }

        return null;
    }

    // ── 第二百零八轮（简报 P0-1）：真机证据归档核对这支脚本 ─────────────

    /// <summary>
    /// <c>collect-real-machine.sh</c>（第二百零八轮，简报 P0-1）自己的形状：<b>每一条「未通过」都自己点名、
    /// 并且真的停住</b>，段尾那句「未收口」挂在块内、成功那句挂在块外，三个退出码都可达，
    /// 并且它在 <c>.winecheck/README.md</c> 里有登记、登记里写明了它<b>不判内容</b>与<b>自己的 Windows 面</b>。
    /// <para>
    /// <b>补的是哪个缺口</b>：这一支把「空目录 = 未达成」从文档里的自觉变成一条可跑的核对，
    /// 判据全在它那三段的输出上。它跑在真机到手那一天——那天没有第二个人在场，
    /// 「脚本停住了但没说为什么停」正是最贵的一种停法。同一族纪律已在
    /// <see cref="PrepareRealMachineScript_EveryStepNamesItsOwnFailure"/> 与
    /// <see cref="DemoPs1_ExitsNonZeroWhenAScreenIsMissing"/> 落过，这里按这一支的形状落第三次：
    /// </para>
    /// <para>
    /// ① 第 ①② 段各至少一句「① / ② 未通过」，每一句都在失败路径上（本行或上一行有 <c>||</c>，
    /// 或上一行是 <c>if …; then</c>），且每一句后面三行内真的 <c>exit 2</c>——只点名不停住，
    /// 脚本会带着没读到的清单继续把「九个目录都空」念成结论；
    /// ② 段尾「核对未收口」与 <c>exit 1</c> 必须在 <c>if [ "$PENDING_COUNT" -gt 0 ]; then … fi</c> 块内，
    /// 而「归档核对通过」必须在块外——两句互换，成功与失败就会打同一句话；
    /// ③ <c>exit 0 / 1 / 2</c> 三个都可达（缺一个就意味着某一类结果没法表达）；
    /// ④ 它打印的口径里要有一句「本脚本不判」——操作者把退出码 0 读成「证据合格」是这一族最自然的误读；
    /// ⑤ README 的登记在位：脚本名、这段核对的名字、不判内容、以及<b>它自己是 bash、真机需要 Git Bash / WSL</b>
    /// 这一条挂账（没有 PowerShell 版这件事必须写在脸上，不许留到下一个人去撞）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不跑这一支脚本（跑它是 <c>RealMachineEvidenceTests.CollectScript_TreatsThePlaceholderAsEmptyAndNamesEveryDirectory</c>
    /// 的活，那条在本容器每次都真执行）；也不判归档内容对不对——那要人看。
    /// </para>
    /// </summary>
    [Fact]
    public void CollectRealMachineScript_NamesItsOwnFailureAndKeepsOneSourceOfTheFileList()
    {
        var path = Path.Combine(Directory, "collect-real-machine.sh");
        Assert.True(File.Exists(path),
            ".winecheck/collect-real-machine.sh 缺失：「空目录 = 未达成」就仍只是文档里的自觉，"
            + "真机当天还得靠人挨个翻目录，而翻一遍的代价是半天。");
        var text = File.ReadAllText(path);
        var lines = text.ReplaceLineEndings("\n").Split('\n');

        // 段头切块（与 prepare-real-machine.sh 同一取法：按段头切，不按空行切——空行随排版漂）。
        var boundaries = new[] { "①", "②", "③" };
        var starts = boundaries
            .Select(m => Array.FindIndex(lines, l => Regex.IsMatch(l, "^echo \"-- " + m + " ")))
            .ToList();
        for (var i = 0; i < boundaries.Length; i++)
        {
            Assert.True(starts[i] >= 0,
                $"collect-real-machine.sh 里找不到第 {boundaries[i]} 段的段头（`echo \"-- {boundaries[i]} …--`）："
                + "分段边界判不出来，「哪一步没过」也就无从核对。");
        }

        for (var i = 0; i < 2; i++)
        {
            var marker = boundaries[i];
            var block = lines[starts[i]..(i + 1 < starts.Count ? starts[i + 1] : lines.Length)];
            // 「注释掉的那一行」不算点名：第二百零八轮变异实测，把这一句整行前面加 `#` 注释掉，
            // 只按文本取的判据照样绿——而脚本那时已经不会打出这句话了。取法必须落在真的会跑的那一行上。
            var messages = block
                .Select((line, index) => (Line: line, Index: index))
                .Where(x => !x.Line.TrimStart().StartsWith('#')
                             && Regex.IsMatch(x.Line, "echo \"" + marker + " 未通过"))
                .ToList();
            Assert.True(messages.Count > 0,
                $"collect-real-machine.sh 第 {marker} 段没有一句「{marker} 未通过」：这一段跑不动时它会静默往下走，"
                + "操作者只看到退出码，看不到是清单没读到、还是目录名两边对不上。");

            // 按分支配对：每一个 exit 2 停靠点都得有自己那一句点名。
            // 只判「这一段里至少有一句」是不够的——第二百零八轮实测：把 ② 段两条分支里的一条点名删掉，
            // 另一条还在，门照样绿，而被删掉那一条失败时就是「停住了但没说为什么停」。
            var stops = block
                .Select((line, index) => (Line: line, Index: index))
                .Where(x => x.Line.Trim() == "exit 2")
                .ToList();
            Assert.True(messages.Count == stops.Count,
                $"collect-real-machine.sh 第 {marker} 段有 {stops.Count} 个 `exit 2` 停靠点，却只有 {messages.Count} 句"
                + $"「{marker} 未通过」：有一条分支停住时不点名，操作者当天只能看到脚本退了场。");
            foreach (var (_, stopIndex) in stops)
            {
                var named = messages.Any(x => x.Index < stopIndex && stopIndex - x.Index <= 3);
                Assert.True(named,
                    $"第 {marker} 段第 {starts[i] + stopIndex + 1} 行那个 `exit 2` 前面三行内没有本段的「{marker} 未通过」："
                    + "只点名不停住、或只停住不点名，都是同一种停法——当天没人知道卡在哪。");
            }

            foreach (var (line, index) in messages)
            {
                var previous = index > 0 ? block[index - 1] : string.Empty;
                var onFailurePath = line.Contains("||", StringComparison.Ordinal)
                    || previous.Contains("||", StringComparison.Ordinal)
                    || Regex.IsMatch(previous.Trim(), @"^(if|elif)\b.*; then$");
                Assert.True(onFailurePath,
                    $"「{marker} 未通过」那一句不在失败路径上（本行与上一行都没有 `||`，上一行也不是 `if …; then`）："
                    + "它会在成功时也打出来，失败时反而可能不打。");

                var stoppedWithinThreeLines = block.Skip(index + 1).Take(3).Any(l => l.Trim().StartsWith("exit 2"));
                Assert.True(stoppedWithinThreeLines,
                    $"「{marker} 未通过」那一句后面三行内没有 `exit 2`：只点名不停住，脚本会带着没读到的清单"
                    + "把「九个目录都空」念成结论——那正是这一支要防的那种结论。");
            }
        }

        // 段尾：未收口与 exit 1 必须在块内，通过那一句必须在块外。
        var pendingOpen = Array.FindIndex(lines, l => l.Trim().Contains("if [ \"$PENDING_COUNT\" -gt 0 ]; then", StringComparison.Ordinal));
        Assert.True(pendingOpen >= 0,
            "collect-real-machine.sh 没有「if [ \"$PENDING_COUNT\" -gt 0 ]; then」那一步：还差东西的目录必须被汇成一句并非零退出。");
        var pendingClose = Array.FindIndex(lines, pendingOpen + 1, l => l.Trim() == "fi");
        Assert.True(pendingClose > pendingOpen, "那段 `if` 切不出收尾的 `fi`（块被写坏了？）——本条按块边界判。");
        var pendingBlock = string.Join('\n', lines[pendingOpen..(pendingClose + 1)]);
        Assert.Contains("未收口", pendingBlock, StringComparison.Ordinal);
        Assert.Contains("exit 1", pendingBlock, StringComparison.Ordinal);
        var tail = string.Join('\n', lines[(pendingClose + 1)..]);
        Assert.Contains("归档核对通过", tail, StringComparison.Ordinal);
        Assert.True(!pendingBlock.Contains("归档核对通过", StringComparison.Ordinal),
            "「归档核对通过」写在了未收口那个块里：成功与失败打同一句话，退出码就只剩数字在说话。");

        foreach (var code in new[] { "exit 0", "exit 1", "exit 2" })
        {
            Assert.True(text.Contains(code, StringComparison.Ordinal),
                $"collect-real-machine.sh 里 {code} 不可达：三个退出码分别说的是「活儿干完了」「活儿没干完」"
                + "与「这条核对本身跑不动」，少一个就有一类结果没法表达。");
        }

        // 它必须把自己不判的那一半写在输出里，而不只写在注释里——而且**两个方向都要有**：
        // 第二百零八轮变异实测，只判「输出里任意一处有这句」时，把开头那一行的「本脚本不判」删掉照样绿
        // （结尾成功那一句还顶着）。开头那一句是当天第一屏读到的口径，结尾那一句挡的是「退出码 0 = 证据合格」这一种误读。
        var opening = lines[0..starts[0]];
        Assert.True(opening.Any(l => l.Contains("echo", StringComparison.Ordinal) && l.Contains("不判", StringComparison.Ordinal)),
            "collect-real-machine.sh 开头那一句口径里没有「不判」：操作者进场第一屏读到的是「核完九个目录」，"
            + "不是「核完九个目录，且不判内容」——这两句话差的那一半正是当天要人看的那一半。");
        Assert.True(tail.Contains("不判", StringComparison.Ordinal),
            "成功那一句（退出码 0 那一支）没写「不判」：退出码 0 会被读成「证据合格」，而它只说「目录都不空」。");

        // ⑥ 文件名只有一份出处：脚本里不许出现任何产出物文件名的字面量。
        //    清单改了而脚本没跟上，正是这一族最贵的漂移（第一百六十九轮「run1.sh 的悬空引用」同一课）；
        //    .md / .sh 是脚本自己要读的仓内文档，不算产出物。
        foreach (var suffix in new[] { ".png", ".json", ".jsonl", ".txt" })
        {
            Assert.False(text.Contains(suffix, StringComparison.Ordinal),
                $"collect-real-machine.sh 里出现了带 {suffix} 的字面量：产出物文件名只有一份出处"
                + "（docs/evidence/real-machine/README.md 的清单），脚本抄一份就意味着改清单时这一支会静默落后，"
                + "念出来的「该放什么」是上一版的。");
        }

        // README 登记：这一段核对的落点必须**独有**，不能被别处的文字替身救活。
        // 第二百零八轮实测：原先只查「不判内容」「Git Bash」这两个词，把整节登记删掉门照样绿——
        // 脚本表那一行里也写着「不判内容」，demo.ps1 那一节里也写着 Git Bash。
        var readme = Repo.Source(".winecheck", "README.md");
        Assert.Contains("## 真机证据归档核对", readme, StringComparison.Ordinal);
        Assert.Contains("有东西 0 / 9", readme, StringComparison.Ordinal);
        Assert.True(new[] { "0 = 九个", "1 = 还有", "2 = " }.Count(readme.Contains) == 3,
            ".winecheck/README.md 没把这支脚本的三个退出码各写一行：退出码是当天唯一要读的东西，"
            + "少一个就意味着有一类结果只有读源码才知道它是什么。");
        Assert.Contains("真机上要有 Git Bash", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>collect-real-machine.sh</c> 的<b>目录集合地板</b>（第二百零九轮，简报 P0-2）。
    /// <para>
    /// <b>补的是哪个缺口</b>：上一轮那条护栏数的是「解析出几条动作」（7 条），它<b>不数目录</b>。
    /// 于是有一种改法能躲过去：把 README 某一行的「归档目录」格改成不匹配的形状，而表格行数与动作号
    /// 仍凑得够 7——脚本照跑、照退 1、照念「九个目录还空着」，而那天真正少的是一个落点。
    /// 这一族的老读法正是「目录里有两张图就以为这条齐了」的反面：核对自己被改坏了，读数仍然像好消息。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 那条护栏判的是<b>写死的地板数 9</b>，不是拿现算出的行数自证（那样改成几都是绿的）；
    /// ② 重名那一条在<b>代码行</b>里（<c>uniq -d</c>），注释里提一句不算接线——数量地板判不到
    /// 「同一个目录被点两次、九个名字仍齐」那一种，两条各挡一种；③ 两条都排在第 ② 段<b>之前</b>，
    /// 清单已经读歪就不许再把歪清单数出来的「九个目录都空」念成结论；④ 每一个地板分支后面三行内
    /// 真的 <c>exit 2</c>（只点名不停住 = 带坏清单往下跑）；⑤ 这件事在 <c>.winecheck/README.md</c>
    /// 的登记里有落点：退出码 2 的说明点到这一类，场景表里也有对应的一行——当天读那页的人要知道
    /// 退出码 2 也可能是「清单的目录集合歪了」，而不只是「表被删过一行」。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判那九个目录名（脚本里出现任何一个才是真抄了第二份清单，那一半由
    /// <c>RealMachineEvidenceTests</c> 拿 <c>ExpectedDirectories</c> 那份地板逐个对）；也不跑脚本
    /// （跑它是 <c>CollectScript_TreatsThePlaceholderAsEmptyAndNamesEveryDirectory</c> 的活，
    /// 本轮那两种 README 改法都在临时假树上真跑过）。
    /// </para>
    /// </summary>
    [Fact]
    public void CollectRealMachineScript_KeepsTheDirectorySetFloorAtNine()
    {
        var path = Path.Combine(Directory, "collect-real-machine.sh");
        var text = File.ReadAllText(path);
        var lines = text.ReplaceLineEndings("\n").Split('\n')
            .Select((line, index) => (Line: line, Index: index))
            .Where(x => !x.Line.TrimStart().StartsWith('#'))
            .ToList();

        var seg2 = lines.FindIndex(x => Regex.IsMatch(x.Line, "^echo \"-- ② "));
        Assert.True(seg2 >= 0, "collect-real-machine.sh 里找不到第 ② 段的段头：分段边界判不出来，「地板排在核对之前」也无从判。");

        // ①② 两条地板：比较的是写死的 9（数量）与 uniq -d（重名），且各自是一个 if 分支。
        var countFloor = lines.FindIndex(x => Regex.IsMatch(x.Line.Trim(), @"^if \[ ""\$[A-Z_]+"" != ""9"" \]; then$"));
        Assert.True(countFloor >= 0,
            "collect-real-machine.sh 没有「[ \"$…\" != \"9\" ]」那一条目录数地板：动作数（7 条）只数认出几条动作，"
            + "把某一行的归档目录格改成不匹配的形状、行数仍凑得够 7 时，核对会带着少一个落点的清单退 1——"
            + "而那天没人知道少的正是截图要放的那个目录。");
        var dupFloor = lines.FindIndex(x => x.Line.Contains("| sort | uniq -d", StringComparison.Ordinal));
        Assert.True(dupFloor >= 0,
            "collect-real-machine.sh 没有判「同一个归档目录被点到两次」那一条：九个名字仍齐时数量地板是绿的，"
            + "而产出物已经散进两个名字，当天得自己猜哪个才是落点。");

        // ③ 两条都排在逐目录核对之前。
        foreach (var (name, at) in new[] { ("目录数地板", countFloor), ("重名地板", dupFloor) })
        {
            Assert.True(at < seg2,
                $"collect-real-machine.sh 把{name}排到了第 ② 段之后：清单已经读歪，却先按歪清单把「九个目录还空着」"
                + "念成了结论——那一念当天会被当成 G-D5 的读数，而不是「核对失效」。");

            // ④ 点名 + 停住：本行或后两行内有「① 未通过」，且其后三行内有 exit 2。
            var block = lines.Skip(at).Take(5).Select(x => x.Line).ToList();
            Assert.True(block.Any(l => l.Contains("① 未通过", StringComparison.Ordinal)),
                $"collect-real-machine.sh 的{name}分支没有一句「① 未通过」点名：停住了但没说为什么停。");
            Assert.True(block.Any(l => l.Trim() == "exit 2"),
                $"collect-real-machine.sh 的{name}分支后面没有 exit 2：只点名不停住，脚本会带着歪清单往下跑。");
        }

        // ⑤ 登记在位：退出码 2 的说明点到这一类，场景表里也有对应行。
        var readme = Repo.Source(".winecheck", "README.md");
        Assert.True(readme.Contains("目录集合凑不满 9 个", StringComparison.Ordinal)
                    || readme.Contains("解析出 8 个归档目录", StringComparison.Ordinal),
            ".winecheck/README.md 那节没把「目录集合地板」这一类失败写进入出码 2 的说明或场景表：当天的人看到退出码 2 "
            + "只会去数表有几行，不会去数那一列有几个目录格。");
    }

    // ── 第二百三十七轮（简报 P0-1）：到期日当天那条把四步串起来的命令 ──────────────

    private static string DefenseDayPath => Path.Combine(Directory, "defense-day.sh");

    private static string DefenseDayText
    {
        get
        {
            Assert.True(File.Exists(DefenseDayPath),
                "缺 .winecheck/defense-day.sh：到期日当天要做的四件事仍散在四份说明里——"
                + "那天在场的人得先自己决定先跑哪一支，而这一支存在的理由就是把那个顺序定死。");
            return File.ReadAllText(DefenseDayPath);
        }
    }

    /// <summary>
    /// 取某一步的调用行号：<c>bash "$HERE/&lt;脚本&gt;"</c> 那一行，容得下 <c>变量=$(bash "$HERE/…" 2&gt;&amp;1)</c>
    /// 那种收输出的写法（第 ① 步就是——它要把 due-date.sh 的输出收进变量再判「当天」，不落临时文件）。
    /// </summary>
    private static int IndexOfDefenseDayCall(string[] lines, string script)
    {
        var needle = $"bash \"$HERE/{script}\"";
        return Array.FindIndex(lines, l => BareCall(l).StartsWith(needle, StringComparison.Ordinal));

        static string BareCall(string line)
        {
            var trimmed = line.TrimStart();
            var assignment = Regex.Match(trimmed, @"^[A-Za-z_][A-Za-z0-9_]*=\$\(bash\s+(?<rest>.*)$");
            return assignment.Success ? "bash " + assignment.Groups["rest"].Value : trimmed;
        }
    }

    /// <summary>
    /// 这一支串起来的<b>前三步</b>（第四步是本脚本读讲稿打印序，不调脚本）：被调的脚本名 + 步号。
    /// <para>
    /// <b>简报那句「四个既有脚本」在这里怎么落</b>：第四支 <c>collect-real-machine.sh</c> <b>不由这一支直接调</b>——
    /// <c>preflight.sh</c> 的第 ④ 条已经在调它，而且只把退出码 2（核对本身跑不动）算失败；这一支若再调它一次
    /// 并按「任一步非零即停」处理，就会把「九个归档目录还空着」（真机 0 台时当天的正常状态，退出码 1）
    /// 读成停链理由，正好把这一族门一直在防的那种误读引进当天。所以本条判的是<b>三支被调用的脚本 + 第四步的出处</b>，
    /// 而 <c>collect-real-machine.sh</c> 仍由 <c>PreflightScriptTests</c> 与
    /// <see cref="CollectRealMachineScript_NamesItsOwnFailureAndKeepsOneSourceOfTheFileList"/> 各自判住。
    /// </para>
    /// </summary>
    private static readonly (string Script, string Marker)[] DefenseDaySteps =
        [("due-date.sh", "①"), ("preflight.sh", "②"), ("demo.sh", "③")];

    /// <summary>
    /// <b>四步顺序固定、任一步非零即停</b>的形状判据（返回问题清单，空清单 = 过得去）。
    /// <para>
    /// 判五件事：① 前三步各有一行 <c>bash "$HERE/&lt;脚本&gt;"</c>，且三个行号严格递增（顺序倒了，当天会先烧三档再判日子）；
    /// ② 每一行调用<b>紧跟</b>一行 <c>RC=$?</c>——中间隔了任何一句，<c>$?</c> 就被那句取走，检查的就不是这一步的退出码；
    /// ③ 每一步的停链都在自己的 <c>if [ "$RC" != "0" ]; then … fi</c> 块里<b>同时</b>有「停在第 X 步」那句点名
    /// 与 <c>exit "$RC"</c>（点名与退出分家 = 一个是自述、一个是空转；退出码写成常量 = 当天读不出是停在哪一类）；
    /// ④ 第 ④ 步的两处「读不到就退场」各自点名 <c>停在第 ④ 步</c> 并在三行内 <c>exit 3</c>——
    /// 讲稿读不到却照样走完，屏幕末尾那句「四步走完」就成了假话；
    /// ⑤ 读讲稿那一段排在第 ③ 步之后（排在之前读的是上一场留下的讲稿）。
    /// </para>
    /// </summary>
    private static List<string> DefenseDayChainProblems(string text)
    {
        var problems = new List<string>();
        var lines = text.ReplaceLineEndings("\n").Split('\n');

        if (!lines[0].StartsWith("#!/bin/bash", StringComparison.Ordinal)) problems.Add("第一行不是 `#!/bin/bash`");
        if (!text.Contains("set -u", StringComparison.Ordinal)) problems.Add("没有 `set -u`：变量没赋上值时会静默跑空而不是退场");

        var lastCall = -1;
        foreach (var (script, marker) in DefenseDaySteps)
        {
            var call = IndexOfDefenseDayCall(lines, script);
            if (call < 0)
            {
                problems.Add($"第 {marker} 步找不到 `bash \"$HERE/{script}\"` 那一行（这一支不再调它，顺序就散了）");
                continue;
            }

            if (call <= lastCall) problems.Add($"第 {marker} 步（{script}）排在前一步之前：当天会先跑演示再判日子");
            lastCall = call;

            var next = lines[call + 1].Trim();
            if (next != "RC=$?")
                problems.Add($"第 {marker} 步的调用后面跟的不是 `RC=$?`（是「{next}」）：$? 会被中间那一句取走");

            // 收进变量的输出必须打回屏幕：这一支在抬头里承诺「停的那一步自己的输出原样在上面」，
            // 收而不打印，那句话就成了自述，当天读到的只有空屏加一句「停在第几步」。
            var captured = Regex.Match(lines[call].TrimStart(), @"^(?<var>[A-Za-z_][A-Za-z0-9_]*)=\$\(bash");
            if (captured.Success)
            {
                var variable = captured.Groups["var"].Value;
                if (!lines.Skip(call).Take(7).Any(l => l.Contains($"\"${variable}\"", StringComparison.Ordinal)
                                                      && (l.TrimStart().StartsWith("echo ", StringComparison.Ordinal)
                                                          || l.TrimStart().StartsWith("printf ", StringComparison.Ordinal)
                                                          || l.TrimStart().StartsWith("cat ", StringComparison.Ordinal))))
                {
                    problems.Add($"第 {marker} 步把输出收进了 ${variable} 却在其后六行内没打回屏幕：那一步失败时当天只看到「停在第 {marker} 步」，看不到它自己点名的原因");
                }
            }

            var guard = Array.FindIndex(lines, call, l => l.Trim() == "if [ \"$RC\" != \"0\" ]; then");
            if (guard < 0)
            {
                problems.Add($"第 {marker} 步的调用后面没有 `if [ \"$RC\" != \"0\" ]; then`：那一步非零退出时没人看");
                continue;
            }

            var close = Array.FindIndex(lines, guard + 1, l => l.Trim() == "fi");
            if (close < 0)
            {
                problems.Add($"第 {marker} 步那支 `if` 切不出收尾的 `fi`：本条按块边界判，先要能切出块");
                continue;
            }

            var block = lines[guard..(close + 1)];
            // 注释掉的那一行不算点名：第二百零八轮实测，只按「块里有这句话」判时，把整行前面加 `#`
            // 照样绿——而那时脚本已经不会打出这句话了。取法必须落在真的会跑的那一行的行首。
            if (!block.Any(l => l.TrimStart().StartsWith($"echo \"停在第 {marker} 步", StringComparison.Ordinal)))
                problems.Add($"第 ①②③ 步里第 {marker} 步的停链块里没有一句真会跑的 `echo \"停在第 {marker} 步…`"
                    + "（整行被注释掉也算没有）：脚本停住了但没说停在第几步");
            if (!block.Any(l => Regex.IsMatch(l.Trim(), @"^exit ""\$RC""$")))
                problems.Add($"第 {marker} 步的停链块里没有 `exit \"$RC\"`：只点名不停住，下一步会带着没过的那一步继续跑");
        }

        var fourth = lines
            .Select((line, index) => (Line: line, Index: index))
            .Where(x => x.Line.TrimStart().StartsWith("echo \"停在第 ④ 步", StringComparison.Ordinal))
            .ToList();
        if (fourth.Count < 2)
            problems.Add($"第 ④ 步只有 {fourth.Count} 处点名（要 2 处：读不到结尾那句口径、读不到讲稿）："
                + "缺任何一处，那一类「读不到」就会带着空屏走到末尾那句「四步走完」");
        foreach (var (_, index) in fourth)
        {
            if (!lines.Skip(index + 1).Take(3).Any(l => l.Trim() == "exit 3"))
                problems.Add($"第 ④ 步第 {index + 1} 行那句点名后面三行内没有 `exit 3`：只说不退，念序那一屏照样算走完");
        }

        var narration = Array.FindIndex(lines,
            l => l.TrimStart().StartsWith("NARRATION=\"$OUT/narration-", StringComparison.Ordinal));
        if (narration < 0) problems.Add("第 ④ 步没有从 $OUT 取 narration-<档位>.txt 的那一行：念序没有出处");
        else if (narration <= lastCall) problems.Add("第 ④ 步读讲稿排在第 ③ 步跑演示之前：那读的是上一场留下的讲稿");

        return problems;
    }

    /// <summary>
    /// 这一支的形状（顺序 / 每步的 <c>RC=$?</c> / 停链点名与退出同块 / 第 ④ 步两处退场）。
    /// <para>
    /// <b>补的是哪个缺口</b>：到期日当天要做的四件事此前散在四支脚本、四份 README 小节里，
    /// <b>没有任何一条命令说先跑哪一支</b>。新增这一支把顺序定死，形状就得有人钉：本文件对
    /// <c>prepare-real-machine.sh</c>（<see cref="PrepareRealMachineScript_EveryStepNamesItsOwnFailure"/>）
    /// 与 <c>collect-real-machine.sh</c>（<see cref="CollectRealMachineScript_NamesItsOwnFailureAndKeepsOneSourceOfTheFileList"/>）
    /// 用的是同一族判法——点名必须在失败路径上、必须真的停住。
    /// </para>
    /// <para>
    /// <b>判据自己得是活的</b>：下面注入四处破坏（删掉第 ② 步的点名、把那一句整行注释掉、
    /// 把第 ③ 步的 <c>exit</c> 摘掉、调换第 ①② 步的调用顺序），逐条必须被报出来。第二百零五轮那一课（「切不出块边界」的门会在
    /// 正确源码上红、真破坏反倒没人报）就是靠这种自测兜住的。
    /// </para>
    /// </summary>
    [Fact]
    public void DefenseDayScript_ChainsTheFourStepsInOrderAndStopsOnAnyNonZero()
    {
        var problems = DefenseDayChainProblems(DefenseDayText);
        Assert.True(problems.Count == 0,
            "defense-day.sh 的链形状过不去：" + string.Join(" ／ ", problems)
            + "。这一支是到期日当天唯一一条命令——它「停住了但没说停在第几步」的那天，没有第二个人在场。");

        var lines = DefenseDayText.ReplaceLineEndings("\n").Split('\n');

        // 破坏一：删掉第 ② 步的点名（脚本仍会停住，但当天读不出停在第几步）。
        var original = DefenseDayText.ReplaceLineEndings("\n");
        var unnamed = string.Join('\n', lines.Where(l => !l.TrimStart().StartsWith("echo \"停在第 ② 步", StringComparison.Ordinal)));
        Assert.NotEqual(original, unnamed);
        Assert.Contains(DefenseDayChainProblems(unnamed), p => p.Contains("②", StringComparison.Ordinal));

        // 破坏二：不删那一行，只把它整行注释掉——脚本照样停得住，但那天读不到「停在第 ② 步」。
        // 第二百零八轮在 collect-real-machine.sh 上实测过这一族：只按「文本里有这句话」取的判据，注释掉之后照样绿。
        var commented = string.Join('\n', lines.Select(l =>
            l.TrimStart().StartsWith("echo \"停在第 ② 步", StringComparison.Ordinal) ? "#" + l : l));
        Assert.NotEqual(original, commented);
        Assert.Contains(DefenseDayChainProblems(commented), p => p.Contains("②", StringComparison.Ordinal));

        // 破坏三：把第 ③ 步的 `exit "$RC"` 摘掉（只点名不停住，下一步带着没过的三档继续）。
        var running = new List<string>(lines);
        var demoCall = IndexOfDefenseDayCall(running.ToArray(), "demo.sh");
        var exitAt = running.FindIndex(demoCall, l => l.Trim() == "exit \"$RC\"");
        Assert.True(exitAt > demoCall, "取法退化：第 ③ 步的停链块里找不到 `exit \"$RC\"`，注入测试成了空转。");
        running[exitAt] = "  : 忘了退场";
        Assert.NotEmpty(DefenseDayChainProblems(string.Join('\n', running)));

        // 破坏四：调换第 ①② 步的调用顺序（先跑预检再判日子——那天会先烧掉一场预检才被告知已过）。
        var swapped = new List<string>(lines);
        var dueCall = IndexOfDefenseDayCall(swapped.ToArray(), "due-date.sh");
        var preflightCall = IndexOfDefenseDayCall(swapped.ToArray(), "preflight.sh");
        Assert.True(0 <= dueCall && dueCall < preflightCall, "取法退化：两支调用的先后没按 ①② 排，注入测试成了空转。");
        (swapped[dueCall], swapped[preflightCall]) = (swapped[preflightCall], swapped[dueCall]);
        Assert.NotEmpty(DefenseDayChainProblems(string.Join('\n', swapped)));
    }

    /// <summary>
    /// 三样东西都不许抄第二份：到期日（一个 <c>YYYY-MM-DD</c> 都不许出现）、讲稿落点与档位
    /// （从 <c>demo.sh</c> 的 <c>OUT</c> / <c>TIERS</c> 缺省现读）、结尾那一句口径
    /// （从 <c>demo.sh</c> 的录制口径第 4 条现读）；「当天」那句标志则从 <c>due-date.sh</c> 现读。
    /// <para>
    /// <b>为什么这四样值得钉</b>：抄一份日期，改期那天脚本仍按旧日期判、而且<b>照样绿</b>（它判的是自己那个字面量）——
    /// 这是 <c>DueDateScriptTests.Script_ReadsTheDueDateFromNotesInsteadOfCarryingASecondCopy</c>
    /// 那一族在 <c>due-date.sh</c> 上踩过的坑，本条把它挪到这一支上。抄一份落点或档位，改了 <c>demo.sh</c> 的人不会知道这里还留着一份旧的，
    /// 第 ④ 步就去读一个 <c>demo.sh</c> 永远不会写出的目录，然后退场。抄一份结尾那句，
    /// 现场念出的那一句与 <c>demo.sh</c> 自己打出的那一句就可能不是同一句。
    /// </para>
    /// </summary>
    [Fact]
    public void DefenseDayScript_KeepsOneSourceOfTheDateThePathsAndTheClosingSentence()
    {
        var text = DefenseDayText;

        // ① 到期日：本脚本里一个日期字面量都不许出现。
        var dates = Regex.Matches(text, @"\d{4}-\d{2}-\d{2}").Cast<Match>().Select(m => m.Value).ToList();
        Assert.True(dates.Count == 0,
            $"defense-day.sh 里出现了日期字面量 {string.Join("、", dates)}：到期日只有 NOTES 的 G-D5 那一格一份出处，"
            + "抄进来就意味着改期那天这一支仍按旧日期判，而它照样退 0——那一格才是当天要读的东西。");

        var demo = Repo.Source(".winecheck", "demo.sh");

        // ② 结尾那句：出处那一行必须还在 demo.sh 里，且本脚本只读不抄。
        Assert.True(Regex.IsMatch(demo, @"(?m)^ *[0-9]+\. 结尾固定一句："),
            "demo.sh 里读不到「N. 结尾固定一句：」那一行了：第 ④ 步的口径没有出处，本条与那一支同时失去落点。");
        Assert.Contains("/结尾固定一句：/", text, StringComparison.Ordinal);
        var closing = Regex.Match(demo, @"(?m)^ *[0-9]+\. 结尾固定一句：(?<s>[^\n]+)").Groups["s"].Value;
        Assert.True(closing.Length > 20, $"demo.sh 的结尾那句短到不像一句口径：「{closing}」");
        Assert.False(text.Contains(closing[..Math.Min(24, closing.Length)], StringComparison.Ordinal),
            "defense-day.sh 把结尾那一句抄了第二份：那一句只有一份出处（demo.sh 的录制口径第 4 条），"
            + "改那一支的人不会知道这里还留着一份，现场念的两句话就分家了。");

        // ③ 落点与档位：本脚本的 sed 取法必须逐字咬住 demo.sh 那两行赋值。
        foreach (var knob in new[] { "OUT", "TIERS" })
        {
            Assert.True(Regex.IsMatch(demo, @"(?m)^" + knob + @"=\$\{" + knob + @":-(.+)\}$"),
                $"demo.sh 里 `^{knob}=${{{knob}:-…}}$` 那一行不在了：这一支的现读取法会读到空，"
                + "而它只能退 3 说「读不到缺省值」——写它的那天得同时改这一支。");
            Assert.Contains($@"s/^{knob}=${{{knob}:-\(.*\)}}$/\1/p", text, StringComparison.Ordinal);
        }

        // ④ 「当天」那句标志：从 due-date.sh 的「当天」那一支现读，不在这条门里写死。
        var dueDate = Repo.Source(".winecheck", "due-date.sh").ReplaceLineEndings("\n").Split('\n');
        var branch = Array.FindIndex(dueDate, l => l.Trim() == "if [[ \"$DUE\" == \"$TODAY\" ]]; then");
        Assert.True(branch >= 0,
            "due-date.sh 的「当天」那一支切不出条件行（写法被改动？）：标志读不到，本条判不到它以为在判的那句。");
        var firstEcho = dueDate.Skip(branch + 1).FirstOrDefault(l => l.TrimStart().StartsWith("echo \"", StringComparison.Ordinal));
        Assert.NotNull(firstEcho);
        var marker = Regex.Match(firstEcho!, "^\\s*echo\\s+\"(?<m>[^$]*)").Groups["m"].Value.Trim();
        Assert.True(marker.Length > 0, $"从那句 echo 里取不出标志文字：{firstEcho}");
        Assert.Contains($"TODAY_MARKER=\"{marker}\"", text, StringComparison.Ordinal);
    }

    /// <summary>找 bash（PATH，再退三个常规位置）；找不到返回 null，调用方走「这台机器判不到」那一支。</summary>
    /// <remarks>取法与 <c>PreflightScriptTests</c> / <c>DueDateScriptTests</c> / <c>RealMachineEvidenceTests</c>
    /// 里那三份一致——本轮把它们收敛成一份属于测试基建的重构，不在这一轮的范围内（简报：只做 P0-1）。</remarks>
    private static string? LocateBash()
    {
        var dirs = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path)) dirs.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        dirs.AddRange(["/bin", "/usr/bin", "/usr/local/bin"]);

        foreach (var dir in dirs.Distinct(StringComparer.Ordinal))
        {
            var candidate = Path.Combine(dir, "bash");
            try
            {
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // 这一台上不合法的目录写法跳过就行。
            }
        }

        return null;
    }

    /// <summary>跑一次这一支；<paramref name="env"/> 覆盖到子进程环境上（子脚本原样继承）。</summary>
    private static (int Exit, string Output) RunDefenseDay(params (string Key, string Value)[] env)
    {
        var psi = new ProcessStartInfo(LocateBash()!, DefenseDayPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = Repo.Root,
        };
        psi.Environment["LANG"] = "C.UTF-8";
        foreach (var (key, value) in env) psi.Environment[key] = value;

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), "defense-day.sh 跑了 120 秒还没退出——这两条都只该走到第 ①② 步。");
        return (process.ExitCode, output + error);
    }

    /// <summary>
    /// <b>真的跑</b>第 ① 步那一支的失败方向：到期日已过 → 这一支退非零、点名停在第 ① 步、
    /// 并且<b>不进</b>第 ② 步。假 NOTES 写到期日、<c>TODAY</c> 指到它第二天，于是这一条不依赖今天几号。
    /// <para>
    /// 判「不进第 ② 步」而不是只判「说了停在第 ① 步」：那天真正的代价是<b>再烧一遍三档</b>——
    /// 已过的那一天该做的是改判与回写（<c>docs/NOTES.md</c>「到期日判定（细则）」），
    /// 把三档跑了既不产出新东西，又会被读成「今天又量了一次」。
    /// </para>
    /// </summary>
    [Fact]
    public void DefenseDayScript_StopsAtStepOneWhenTheDueDateHasPassed()
    {
        if (LocateBash() is null) return; // 这台机器上没有 bash：判不到。

        var root = Path.Combine(Path.GetTempPath(), "ah-defense-" + Guid.NewGuid().ToString("N")[..12]);
        try
        {
            System.IO.Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "docs", "NOTES.md"), "| [ ] | G-D5 真机观感 | 到期日 2026-11-20 |\n");

            var (exit, output) = RunDefenseDay(
                ("NOTES", Path.Combine(root, "docs", "NOTES.md")),
                ("TODAY", "2026-11-21"));

            Assert.True(exit != 0, "到期日已过而这一支退出 0——那天会被当成「四步走完」，而它该走的是改判。");
            Assert.Contains("停在第 ① 步", output, StringComparison.Ordinal);
            Assert.Contains("已过期", output, StringComparison.Ordinal); // 由 due-date.sh 打出，词表不在这里发明。
            Assert.DoesNotContain("-- ② 临场预检四条", output, StringComparison.Ordinal);
            Assert.DoesNotContain("-- ③ 跑冒烟与讲稿", output, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    /// <summary>
    /// <b>真的跑</b>第 ② 步那一支的失败方向：临场预检不过 → 这一支退非零、点名停在第 ② 步、
    /// 并且<b>不进</b>第 ③ 步。<c>REPO</c> 指到一棵没有读数记录、没有归档清单的假树，
    /// 于是 <c>preflight.sh</c> 的 ②③④ 三条当场红——这一条同样不依赖这台机器上有没有 pwsh 与已发布的冒烟器。
    /// <para>判的是「带着预检没过的环境不许去跑三档」：跑出来的读数属于上一版源码或另一台机器，
    /// 而 <c>demo.sh</c> 还会把那份读数写回 <c>reading.txt</c>（本条把 <c>READING</c> 指到仓外，
    /// 即便有人把这一条改成「让它跑到第 ③ 步」也不会把仓里的对账材料写脏）。</para>
    /// </summary>
    [Fact]
    public void DefenseDayScript_StopsAtStepTwoWhenThePreflightFails()
    {
        if (LocateBash() is null) return;

        var root = Path.Combine(Path.GetTempPath(), "ah-defense-" + Guid.NewGuid().ToString("N")[..12]);
        try
        {
            System.IO.Directory.CreateDirectory(Path.Combine(root, "docs"));
            // 到期日在未来（这一支该继续走到第 ② 步），但假树里没有读数记录、没有归档清单：preflight 必红。
            File.WriteAllText(Path.Combine(root, "docs", "NOTES.md"), "| [ ] | G-D5 真机观感 | 到期日 2999-12-31 |\n");
            File.WriteAllText(Path.Combine(root, "reading.txt"), "source=deadbeef\n");

            var (exit, output) = RunDefenseDay(("REPO", root), ("READING", Path.Combine(root, "reading.txt")));

            Assert.True(exit != 0, "临场预检没过而这一支退出 0——带着上一版源码的读数也能开讲。");
            Assert.Contains("停在第 ② 步", output, StringComparison.Ordinal);
            Assert.Contains("预检未通过", output, StringComparison.Ordinal);
            Assert.Contains("是 preflight 自己那四条预检的编号", output, StringComparison.Ordinal);
            Assert.DoesNotContain("-- ③ 跑冒烟与讲稿", output, StringComparison.Ordinal);
            Assert.DoesNotContain("wine 前缀：", output, StringComparison.Ordinal); // demo.sh 一旦被调起来第一句就是它。
        }
        finally
        {
            TryDelete(root);
        }
    }

    /// <summary>
    /// Windows 侧当天跑哪一支：<b>这一句登记在两处都在位，且与磁盘上的 <c>.ps1</c> 实物对得上</b>
    /// （第二百四十轮，简报 P0-1 的 B 案：不补 <c>defense-day.ps1</c>，把「不补」写成一句可判的登记）。
    /// <para>
    /// <b>补的是哪个洞</b>：第 ③ 步那一支 <c>demo.sh</c> 有 PowerShell 对照物（<c>demo.ps1</c>，
    /// 段名逐字同名，见 <see cref="DemoPs1_NamesTheSameSectionsAsDemoSh"/>），而<b>第 ①②④ 步没有</b>。
    /// 这个不对称此前只活在第二百三十七轮的当轮记录里（文末逐轮台账，不在当天会翻开的那两页上），
    /// 于是 Windows 侧当天真正会读的两份文件（本 README 那一节、<c>docs/NOTES.md</c>「当天先跑哪一支」那一段）
    /// <b>都没说这一支在 Windows 上怎么跑</b>——接手的人只能回 README 手抄四步，而手抄丢掉的是
    /// 「任一步非零即停并点名停在第几步」这一条当天最需要的行为。
    /// </para>
    /// <para>
    /// <b>为什么选登记而不是补一支 ps1</b>：这一支四步全在只读文件、比日期、跑 wine，现场就在容器里；
    /// 补一份长期无人真跑的 PowerShell 副本只会多出第二处要维护的顺序，与本仓
    /// 「脚本里不许抄第二份」那条纪律是同一取舍。登记写清楚，比再造一个没人跑的脚本便宜且诚实。
    /// </para>
    /// <para>
    /// <b>判五件事</b>：① 本 README「到期日当天一条命令」那一节里有这一句；② <c>docs/NOTES.md</c>
    /// 「当天先跑哪一支」那一段里有同一句；③ 两处都点到 <c>Git Bash</c> / <c>WSL</c>、那条可跑的命令
    /// <c>bash .winecheck/defense-day.sh</c>、以及「不提供 <c>defense-day.ps1</c>」这件事本身；
    /// ④ <b>磁盘上真的没有</b> <c>defense-day.ps1</c>——补了对照物而登记没跟着改，这一句就成了假的；
    /// ⑤ 磁盘上的 <c>.ps1</c> 只有 <c>demo.ps1</c> 一份（多出一支而登记仍说「只对照第 ③ 步」，同样是假的）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判这几支 bash 在真 Windows 的 Git Bash 里到底跑不跑得动（那是真机那天要量的，
    /// 随 G-D5 一起挂账），也不判登记的措辞好不好——它判的是「下一轮不必再问一遍」。
    /// </para>
    /// </summary>
    [Fact]
    public void DefenseDayScript_WindowsSideRunbook_IsRegisteredInBothPlaces()
    {
        var readme = Repo.Source(".winecheck", "README.md");
        var notes = Repo.Source("docs", "NOTES.md");

        // 出处一：本节自己（从那一节的标题起，到下一个二级标题之前——与本仓切节的同一取法）。
        var sectionStart = readme.IndexOf("## 到期日当天一条命令（`defense-day.sh`）", StringComparison.Ordinal);
        Assert.True(sectionStart >= 0,
            "`.winecheck/README.md` 里找不到「到期日当天一条命令（defense-day.sh）」那一节：登记没有落点。");
        var tail = readme[sectionStart..].Split('\n').Skip(1).ToArray();
        var cut = Array.FindIndex(tail, l => l.StartsWith("## ", StringComparison.Ordinal));
        var section = cut < 0 ? string.Join('\n', tail) : string.Join('\n', tail[..cut]);

        // 出处二：NOTES 里那一段（一行到底，与第二百三十七轮写它时同一个形状）。
        var paragraphStart = notes.IndexOf("> **当天先跑哪一支", StringComparison.Ordinal);
        Assert.True(paragraphStart >= 0,
            "`docs/NOTES.md` 里找不到「当天先跑哪一支」那一段：Windows 侧当天读到的那一页又没有出处了。");
        var paragraph = notes[paragraphStart..].Split('\n')[0];

        foreach (var (place, text) in new[] { (".winecheck/README.md", section), ("docs/NOTES.md", paragraph) })
        {
            var missing = new List<string>();
            foreach (var need in new[] { "defense-day.ps1", "不提供", "Git Bash", "WSL", "bash .winecheck/defense-day.sh" })
            {
                if (!text.Contains(need, StringComparison.Ordinal)) missing.Add(need);
            }

            Assert.True(missing.Count == 0,
                $"Windows 侧当天那一处登记不完整：{place} 里那一段少了 {string.Join("、", missing)}"
                + "——当天在场的人读不到「这一支在 Windows 上还是跑 bash，本仓不提供 ps1 对照物」，"
                + "下一步就是回 README 手抄四步，而手抄丢掉的是「停在第几步」的点名。");
        }

        // 与磁盘对账：登记说「不提供」，磁盘上就不许有；登记说的那一份唯一 ps1 也不许长出第二份。
        var ps1 = System.IO.Directory.GetFiles(Directory, "*.ps1")
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.DoesNotContain("defense-day.ps1", ps1, StringComparer.Ordinal);
        Assert.Equal(["demo.ps1"], ps1);
    }

    /// <summary>临时树清不掉不影响判据（与 <c>PreflightScriptTests.FakeTree.Dispose</c> 同一取舍）。</summary>
    private static void TryDelete(string root)
    {
        try
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
