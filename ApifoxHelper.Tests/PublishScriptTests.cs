using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「发布这一条路」的机械门（第一百七十四轮，简报 P0-2 第 2 项）。
/// <para>
/// <b>补的是哪个洞</b>：G-D5 清单第 1 条的前半句是「<c>dotnet publish</c> 出自包含单文件（发布脚本
/// <c>publish.ps1</c>）」。这一句里<b>不依赖 Windows 的那半截</b>——参数拼得对不对、产物形状是不是
/// README 念的那个——现在就能在 Linux 上量；而此前整仓对 <c>publish.ps1</c> 一条断言都没有：
/// 脚本改了参数名、改了输出目录名，README 不会红，答辩现场念的还是旧的那一行。
/// 第一百六十九轮给 <c>.winecheck/demo.sh</c> 立过同一条纪律（「念的数出自当前源码」），本轮把它接到发布面。
/// </para>
/// <para>
/// <b>门判的是形状，不是读数</b>：这里不跑 <c>dotnet publish</c>（一次真跑 30 秒以上，且产物大小随 SDK 漂），
/// 它钉的是「README 里那两行用法与那两条产物路径，只能从 <c>publish.ps1</c> 自己的字面量里拼出来」。
/// 实测读数记在 <c>docs/NOTES.md</c>「G-D5 真机复核清单（可勾选）」第 1 条下方，由
/// <see cref="LocalPublishReading_IsRecordedAndKeepsTheRealMachineGap"/> 核它<b>在不在、有没有把没量到的那半截写清</b>——
/// 读数本身对不对，靠的是那一行下面写明的复跑命令，不靠这条门。
/// </para>
/// </summary>
public class PublishScriptTests
{
    private static string Script => Repo.Source("publish.ps1");

    private static string Readme => Repo.Source("README.md");

    /// <summary>脚本 <c>param()</c> 块里声明的参数名（PowerShell 参数一律 <c>$Name</c>，大小写不敏感）。</summary>
    private static List<string> DeclaredParameters()
    {
        var body = Regex.Match(Script, @"param\((?<body>[\s\S]*?)\n\)").Groups["body"].Value;
        Assert.True(body.Length > 0, "publish.ps1 里找不到 param() 块：参数面没有单一落点，用法文档无从对齐。");
        return Regex.Matches(body, @"\[(?:string|switch|int|bool|string\[\])\]\$(?<name>[A-Za-z0-9_]+)")
            .Select(m => m.Groups["name"].Value).ToList();
    }

    /// <summary>某一文件里每处调用 <c>publish.ps1</c> 时用到的开关名（README 的用法块 + 脚本自己的 .EXAMPLE）。</summary>
    private static List<string> DocumentedSwitches(string text) => Regex.Matches(text, @"publish\.ps1(?<tail>[^\r\n]*)")
        .SelectMany(m => Regex.Matches(m.Groups["tail"].Value, @"\s-(?<name>[A-Za-z][A-Za-z0-9_]*)").Select(n => n.Groups["name"].Value))
        .ToList();

    /// <summary>脚本里那两条产物路径的字面量来源：默认输出目录名、两种档位的 RID 子目录名、可执行文件名。</summary>
    private static (string Output, string SelfContainedDir, string FrameworkDependentDir, string Exe) ScriptLiterals()
    {
        var output = Regex.Match(Script, @"\$Output\s*=\s*\(Join-Path\s+\$PSScriptRoot\s*'(?<v>[^']+)'\)").Groups["v"].Value;
        var dirs = Regex.Match(Script, @"\$target\s*=\s*Join-Path\s+\$Output\s+\$\(if\s*\(\$selfContained\)\s*\{\s*'(?<a>[^']+)'\s*\}\s*else\s*\{\s*'(?<b>[^']+)'").Groups;
        var exe = Regex.Match(Script, @"\$exe\s*=\s*Join-Path\s+\$target\s*'(?<v>[^']+)'").Groups["v"].Value;
        Assert.True(output.Length > 0 && dirs["a"].Value.Length > 0 && dirs["b"].Value.Length > 0 && exe.Length > 0,
            "publish.ps1 的默认输出目录 / 两种档位子目录 / 可执行文件名不是能从字面量读出来的形状："
            + $"Output='{output}' 自包含='{dirs["a"].Value}' 框架依赖='{dirs["b"].Value}' exe='{exe}'。"
            + "这一句红，说明 README 里那两条路径已经无从与脚本对齐——脚本把这三处之一改成了拼接或变量。");
        return (output, dirs["a"].Value, dirs["b"].Value, exe);
    }

    /// <summary>
    /// README 与脚本 .EXAMPLE 里用到的每一个开关，都必须是 <c>param()</c> 真声明过的；反过来，
    /// 声明过的每一个参数都得在这两处之一被念到。
    /// <para>
    /// 两个方向都要：只查「用掉的 ⊆ 声明的」，新增一个参数而文档不写，门照样绿，而使用者手里那份
    /// README 就少了一条能用的开关；只查「声明的 ⊆ 用到的」，文档里多打一个不存在的开关（旧参数名残留）
    /// 照样绿，而那条命令敲下去就是报错。PowerShell 参数大小写不敏感，所以这里按忽略大小写比。
    /// </para>
    /// </summary>
    [Fact]
    public void PublishUsage_DocumentedSwitchesAreTheDeclaredOnes()
    {
        var declared = DeclaredParameters();
        Assert.True(declared.Count >= 2, $"publish.ps1 只声明了 {declared.Count} 个参数，取法不对（应有输出目录与框架依赖两档）。");

        var used = DocumentedSwitches(Readme).Concat(DocumentedSwitches(Script)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(used.Count >= 2, $"README 与脚本示例里一共只找到 {used.Count} 个开关用法，取法不对。");

        foreach (var used1 in used)
        {
            Assert.True(declared.Any(d => string.Equals(d, used1, StringComparison.OrdinalIgnoreCase)),
                $"用法里出现了 publish.ps1 没有声明的开关 -{used1}（声明的只有 {string.Join("、", declared)}）："
                + "这条命令敲下去就是「找不到与接受参数 -xxx 匹配的参数」。");
        }

        foreach (var param in declared)
        {
            Assert.True(used.Any(u => string.Equals(u, param, StringComparison.OrdinalIgnoreCase)),
                $"publish.ps1 声明了参数 -{param}，README 的打包块与脚本自己的 .EXAMPLE 里一次都没念到："
                + "文档比实现少一条，读的人不知道它存在。");
        }

        // 调用写法本身：README 必须按脚本的真文件名调它（文件名改了而文档没跟，那条命令找不到文件）。
        Assert.Contains(@".\publish.ps1", Readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// README 里那两条产物路径<b>只能由脚本自己的字面量拼出来</b>，不是文档里独立抄的一份。
    /// <para>
    /// 抄一份的后果就是漂：第一百六十八轮给 <c>demo.sh</c> 立的屏单只有一条，同一族毛病在发布面还没人管。
    /// 这一条把两个方向都钉住——正向：拼出来的两条路径必须逐字出现在 README；
    /// 负向：README 里凡是「<c>&lt;默认目录&gt;\…\ApifoxHelper.exe</c>」形状的路径，都必须等于这两条之一
    /// （多出来一条旧的、或改了 RID 子目录名而 README 没跟，红在这里）。
    /// </para>
    /// </summary>
    [Fact]
    public void PublishedPaths_InReadme_AreTheOnesTheScriptComputes()
    {
        var (output, selfContained, frameworkDependent, exe) = ScriptLiterals();
        var expected = new[]
        {
            $@"{output}\{selfContained}\{exe}",
            $@"{output}\{frameworkDependent}\{exe}",
        };

        foreach (var path in expected)
        {
            Assert.True(Readme.Contains(path, StringComparison.Ordinal),
                $"README 的打包一节没有原样写出脚本真正会产出的路径「{path}」："
                + "要么脚本改了目录名，要么文档抄的是另一份——答辩时念的必须是当前脚本算出来的那一条。");
        }

        // 负向那一半：README 里同形状的路径，一条都不许在这两条之外。
        var stray = Regex.Matches(Readme, Regex.Escape(output) + @"\\[^`\s|]*" + Regex.Escape(exe))
            .Select(m => m.Value)
            .Where(v => !expected.Contains(v, StringComparer.Ordinal))
            .ToList();
        Assert.True(stray.Count == 0,
            $"README 出现了脚本拼不出来的发布产物路径：{string.Join(" ／ ", stray)}（脚本只会产出 {string.Join(" 与 ", expected)}）。");
    }

    /// <summary>
    /// G-D5 清单第 1 条下方那一行「本地可验的那半截」的读数必须在，且必须把<b>没量到的那半截写明</b>。
    /// <para>
    /// 存在的理由与第一百七十一轮给浏览器探针立的那条相同：一次实测如果只落在某轮的提交说明里，
    /// 下一轮就只剩「发布路径已验」这句没有出处的自述。所以这里核四件事：
    /// ① 那一行在，并带轮次与日期；② 两种档位的<b>字节数</b>都在（不是只写「约 78 MB」——没有字节读数就无法复跑对账）；
    /// ③ 退出码为 0 这件事写在行里；④ 「双击 / 首程四站没量到」这一句在，且整行不许出现真机达成的措辞。
    /// </para>
    /// <para>
    /// 顺带把 README 那句「体积约 N MB」与这行记录的字节数对一次（容差 1 MB）：文档里的约数不能脱离实测读数自己漂。
    /// </para>
    /// </summary>
    [Fact]
    public void LocalPublishReading_IsRecordedAndKeepsTheRealMachineGap()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var line = notes.Split('\n').FirstOrDefault(l => l.Contains("本地发布读数", StringComparison.Ordinal));
        Assert.True(line is not null,
            "docs/NOTES.md 的 G-D5 一节里没有「本地发布读数」那一行：第 1 条本地可验的那半截没有落账，"
            + "「发布路径已验」就又变成一句无从核对的自述。");
        Assert.True(Regex.IsMatch(line, @"第[零一二三四五六七八九十百]+轮\s*·\s*\d{4}-\d{2}-\d{2}"),
            $"「本地发布读数」那一行没写成带轮次与日期的形状（第X轮 · YYYY-MM-DD）：{line[..Math.Min(120, line.Length)]}…");

        // 按标签取，不取「任意一个数字 + 字节」：同一行还记着未压缩那份的主文件字节数，
        // 拿它去对 README 的「体积约 78 MB」会红在错的那一个数上。
        var selfContainedBytes = LabeledBytes(line, "自包含单文件");
        var frameworkDependentBytes = LabeledBytes(line, "框架依赖单文件");
        Assert.Contains("退出码 0", line, StringComparison.Ordinal);
        Assert.Contains("没量到", line, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "真机实测", "真机已验证", "真机证据已归档" })
        {
            Assert.DoesNotContain(forbidden, line, StringComparison.Ordinal);
        }

        // README 的约数与实测读数对一次：脱离字节读数的约数会自己漂走。
        var claimed = Regex.Match(Readme, @"体积约\s*(?<mb>\d+)\s*MB").Groups["mb"].Value;
        Assert.True(claimed.Length > 0, "README 里找不到「体积约 N MB」那一句（性能设计一节）：取法变了，这一条要跟着改。");
        var measuredMb = selfContainedBytes / 1024d / 1024d;
        Assert.True(Math.Abs(measuredMb - long.Parse(claimed)) <= 1,
            $"README 念的「体积约 {claimed} MB」与本地发布读数里自包含单文件 {selfContainedBytes} 字节（≈{measuredMb:F1} MB）差超过 1 MB："
            + "文档里的约数必须跟着最近一次实测读数走。");
        Assert.True(frameworkDependentBytes < selfContainedBytes,
            $"本地发布读数说框架依赖档 {frameworkDependentBytes} 字节不小于自包含档 {selfContainedBytes} 字节："
            + "README 念的「体积小的版本」这一句就没了出处。");
    }

    /// <summary>取「标签 + 空格 + 数字 + 字节」里的字节数；取不到就是那一行没按可复跑对账的形状记。</summary>
    private static long LabeledBytes(string line, string label)
    {
        var value = Regex.Match(line, Regex.Escape(label) + @"\s*(?<count>\d{6,})\s*字节").Groups["count"].Value;
        Assert.True(value.Length > 0, $"「本地发布读数」那一行没有按「{label} NNNNNN 字节」的形状记数（只写约数无法复跑对账）。");
        return long.Parse(value);
    }
}
