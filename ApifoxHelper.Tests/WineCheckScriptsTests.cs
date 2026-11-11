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
}
