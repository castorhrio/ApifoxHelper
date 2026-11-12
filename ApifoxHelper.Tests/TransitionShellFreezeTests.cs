using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「过渡壳冻结」的机械门（第一百九十九轮，简报 P0-2）。
/// <para>
/// <b>补的是哪个缺口</b>：<c>docs/方案.md</c> §2.4 与 <c>README.md</c> 都承诺「页面层（<c>Web/</c> 的
/// index.html / app.js / app.css）已冻结、只修阻断性缺陷」，但仓内此前<b>没有任何一条断言</b>判这三个文件
/// 没被功能性改动——承诺只活在两句散文里。散文不会红：谁往 <c>Web/app.js</c> 里加一段交互打磨，
/// 构建与全部用例照旧全绿，冻结承诺静默失效，直到有人回头读那两句话才发现。
/// </para>
/// <para>
/// <b>判什么</b>：<c>docs/NOTES.md</c>「过渡壳冻结登记（可机械核对）」一节里，三个文件各有一行基线
/// （<c>- `Web/app.js` sha256 `&lt;64 位十六进制&gt;` · 第 N 轮</c>），基线哈希必须等于磁盘上文件的
/// SHA-256；<c>Web/</c> 目录里也不许出现基线之外的新文件（新增页面层文件同样是「不再冻结」）。
/// </para>
/// <para>
/// <b>白名单怎么放行</b>：冻结不等于永远不许动——「只修阻断性缺陷」这一支要能走。放行条件是<b>显式登记</b>：
/// 改文件的同时 ① 更新该文件的基线哈希与轮次，② 在「变更登记」里加一行
/// <c>- `Web/app.js` · 第 N 轮 · 理由：…</c>，且轮次与基线那一行一致、理由非空。
/// 只改文件不改基线即红（<see cref="ShellFiles_MatchTheRegisteredHashes"/>）；
/// 改了基线却没有同轮次的理由登记也红（<see cref="EveryRegisteredHash_HasAChangeEntryWithAReason"/>）——
/// 哈希能证明「文件没变」，只有理由能证明「变了是为什么」。
/// </para>
/// <para>
/// <b>不判什么</b>：不判改动是否真的「阻断性」（那是人的判断，机械门判不了，也不该假装判得了）；
/// 不判 <c>Web/</c> 之外的任何文件；不判 G-D6 退场（退场是删文件，届时本节与三个文件一起删，
/// 由 <c>AcceptanceChecklistTests.ExitChecklist_EveryItemPointsToRealSymbols</c> 那一族管）。
/// </para>
/// </summary>
public class TransitionShellFreezeTests
{
    private const string Heading = "## 过渡壳冻结登记（可机械核对）";

    /// <summary>冻结承诺覆盖的三个文件（<c>docs/方案.md</c> §2.4 的「页面层」）。</summary>
    private static readonly string[] ShellFiles = ["Web/app.js", "Web/app.css", "Web/index.html"];

    /// <summary>本节正文的行（从标题行到下一个 <c>## </c> 之前）。</summary>
    private static string[] SectionLines()
    {
        var lines = Repo.Source("docs", "NOTES.md").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == Heading);
        Assert.True(start >= 0,
            $"docs/NOTES.md 缺「{Heading}」一节：页面层冻结（docs/方案.md §2.4）没有登记就没有门，"
            + "承诺会静默失效。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    /// <summary>
    /// 基线行：<c>- `Web/app.js` sha256 `&lt;64 hex&gt;` · 第一百九十九轮</c>。
    /// 行首允许引用块标记（<c>&gt;</c>）——本节整段是 blockquote，登记行写在引用里。
    /// </summary>
    private static readonly Regex BaselineLine = new(
        @"^[>\s]*-\s*`(?<file>Web/[A-Za-z0-9._-]+)`\s+sha256\s+`(?<hash>[0-9a-f]{64})`\s*·\s*(?<round>第[^·]+轮)\s*$");

    /// <summary>变更登记行：<c>- `Web/app.js` · 第一百九十九轮 · 理由：…</c>（同样允许行首引用块标记）。</summary>
    private static readonly Regex ChangeLine = new(
        @"^[>\s]*-\s*`(?<file>Web/[A-Za-z0-9._-]+)`\s*·\s*(?<round>第[^·]+轮)\s*·\s*理由：(?<reason>.+)$");

    private static List<(string File, string Hash, string Round)> Baselines() => SectionLines()
        .Select(l => BaselineLine.Match(l.Trim()))
        .Where(m => m.Success)
        .Select(m => (m.Groups["file"].Value, m.Groups["hash"].Value, m.Groups["round"].Value.Trim()))
        .ToList();

    private static List<(string File, string Round, string Reason)> Changes() => SectionLines()
        .Select(l => ChangeLine.Match(l.Trim()))
        .Where(m => m.Success)
        .Select(m => (m.Groups["file"].Value, m.Groups["round"].Value.Trim(), m.Groups["reason"].Value.Trim()))
        .ToList();

    /// <summary>磁盘上某个文件的 SHA-256（小写十六进制）。取法走 <see cref="Repo.Sha256Of"/> 那一处——
    /// 第 241 轮给 <c>.winecheck/</c> 建第二族冻结基线时，这把哈希在本仓就有了第二个读者；
    /// 各写一把的后果是「与基线一致」这句话有两个意思。</summary>
    private static string Hash(string relative)
    {
        Assert.True(File.Exists(Path.Combine(Repo.Root, relative.Replace('/', Path.DirectorySeparatorChar))),
            $"冻结基线登记了 {relative}，但仓库里没有这个文件。");
        return Repo.Sha256Of(relative);
    }

    /// <summary>
    /// 三个过渡壳文件的内容必须与冻结基线逐字节一致。
    /// <para>
    /// 变异验证：往 <c>Web/app.js</c> 末尾加一行注释即红（哈希变），还原后全绿。
    /// </para>
    /// </summary>
    [Fact]
    public void ShellFiles_MatchTheRegisteredHashes()
    {
        var baselines = Baselines();
        Assert.True(baselines.Count > 0,
            $"「{Heading}」一节里没有一行冻结基线（形状：- `Web/app.js` sha256 `<64 位十六进制>` · 第 N 轮）。");

        foreach (var file in ShellFiles)
        {
            var registered = baselines.Where(b => b.File == file).ToList();
            Assert.True(registered.Count == 1,
                $"冻结基线里 {file} 登记了 {registered.Count} 行（应恰好 1 行）：多行会让「以哪一行为准」变成人的判断。");

            var actual = Hash(file);
            Assert.True(actual == registered[0].Hash,
                $"{file} 的内容与冻结基线不一致（登记 {registered[0].Hash}，实际 {actual}）："
                + "页面层已冻结、只修阻断性缺陷（docs/方案.md §2.4）。确实要改，就同时更新本节该文件的基线哈希与轮次，"
                + "并在「变更登记」里加一行 `- " + file + "` · 第 N 轮 · 理由：…（轮次与基线一致）。");
        }
    }

    /// <summary>
    /// <c>Web/</c> 目录里只许有基线登记的那三个文件——新增页面层文件同样是「不再冻结」。
    /// </summary>
    [Fact]
    public void WebDirectory_HoldsOnlyTheFrozenFiles()
    {
        var onDisk = Directory.GetFiles(Path.Combine(Repo.Root, "Web"))
            .Select(f => "Web/" + Path.GetFileName(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        var registered = Baselines().Select(b => b.File).OrderBy(f => f, StringComparer.Ordinal).ToList();

        Assert.Equal(registered, onDisk);
    }

    /// <summary>
    /// 基线哈希的每一次更新都要有同轮次的理由登记——哈希能证明「文件没变」，理由才能证明「变了是为什么」。
    /// <para>
    /// 变异验证：把基线哈希改成别的值（模拟「改了文件顺手改哈希」）而不同步加理由行，本条即红。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryRegisteredHash_HasAChangeEntryWithAReason()
    {
        var changes = Changes();
        foreach (var baseline in Baselines())
        {
            var match = changes.Where(c => c.File == baseline.File && c.Round == baseline.Round).ToList();
            Assert.True(match.Count == 1,
                $"冻结基线里 {baseline.File} 的当前哈希登记在「{baseline.Round}」，"
                + $"但「变更登记」里同文件同轮次的行有 {match.Count} 条（应恰好 1 条）："
                + "改哈希必须同时写清这一轮为什么动它，否则「只修阻断性缺陷」这句话无法核对。");
            Assert.True(match[0].Reason.Length >= 8,
                $"「变更登记」里 {baseline.File}（{baseline.Round}）的理由只有「{match[0].Reason}」："
                + "理由要能让人判断这次改动是不是阻断性缺陷，八个字以下等于没写。");
        }
    }

    /// <summary>
    /// 门钉在承诺上：<c>README.md</c> 与 <c>docs/方案.md</c> §2.4 里那句「页面层已冻结」必须还在。
    /// 承诺被删掉而门还在，就成了「守着一个没人再承诺的约束」——两边一起改才是有意的决定。
    /// </summary>
    [Fact]
    public void FreezePromise_IsStillWrittenWhereUsersReadIt()
    {
        Assert.Contains("页面层已冻结", Repo.Source("README.md"), StringComparison.Ordinal);
        Assert.Contains("在此之前冻结（只修阻断性缺陷", Repo.Source("docs", "方案.md"), StringComparison.Ordinal);
    }
}
