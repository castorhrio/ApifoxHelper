using System.Security.Cryptography;
using System.Text;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「当前源码指纹」——<b>与 <c>.winecheck/demo.sh</c> 的 <c>source_fingerprint()</c> 同一口径</b>。
/// <para>
/// <b>为什么要有这一份</b>：<c>.winecheck/reading.txt</c> 的 <c>source=</c> 记的就是这个值，
/// 它回答的是「那份三档读数出自哪一版源码」。第二百五十九轮之前，这个问题的答案只能靠人
/// 每轮手工复算一遍（第二百一十四轮就是这么干的：手算 <c>53143628…</c> 与记录一字相同，
/// 于是判定「不需要重跑」）。人手复算的那一轮是对的，但它不是判据——下一轮没人复算，
/// 一份过期的读数照样全绿。这一份把那条口径搬进 <c>dotnet test</c>，让「读数对应哪份源码」
/// 从自述变成可判。
/// </para>
/// <para>
/// <b>口径逐字照抄 shell 那一支</b>（<c>demo.sh</c> 里那五行）：
/// <c>cd "$REPO" &amp;&amp; find . -name bin -prune -o -name obj -prune -o -name '*.cs'
/// -not -path './ApifoxHelper.Tests/*' -print0 | LC_ALL=C sort -z | xargs -0 sha1sum | sha1sum</c>。
/// 也就是：<b>全部 <c>*.cs</c>，剪掉任何一层叫 <c>bin</c> / <c>obj</c> 的目录，剪掉测试工程</b>
/// （测试工程不编进冒烟产物，改了它不该要求重发冒烟器），按路径字节序排好，逐文件 sha1，
/// 再把「<c>哈希 + 两个空格 + 仓库内相对路径</c>」这一串整体 sha1 一次。
/// </para>
/// <para>
/// <b>为什么是相对路径（第三百九十九轮）</b>：旧口径把<b>绝对路径</b>哈希进去，于是同一版源码
/// 在 <c>/opt/ApifoxHelper</c> 与 <c>/tmp/ah-cold</c> 各得一个指纹——答辩现场换一台机器 clone 一次，
/// <c>preflight.sh</c> 第 ② 条就判「读数属于上一版代码」，而源码一个字节没变。现在参与哈希的
/// 是 <c>./Api/X.cs</c> 这种<b>仓库内相对路径</b>，与仓库所在的那一段路径无关。
/// 这条性质由 <c>ReadingConsistencyTests.SourceFingerprint_IsTheSameForTheSameSourceAtTwoDifferentPaths</c>
/// 机械判住（两棵内容相同、路径不同的树各算一次，逐字比）。
/// </para>
/// <para>
/// <b>两份实现会不会漂</b>：会，而且漂了以后两边各自「自洽」。所以
/// <c>ReadingConsistencyTests.SourceFingerprint_AgreesWithTheShellOneWhenBashIsHere</c>
/// 在有 bash 的机器上把 shell 那一支真跑一遍，与本类算出来的值逐字比——两份实现只有一处能改。
/// 没有 bash 的机器上那一条判不到（同 <c>DemoPs1_ParsesWithARealPowerShellParserWhenOneIsHere</c>
/// 的做法：判不到就说判不到，不静默绿）。
/// </para>
/// </summary>
internal static class SourceFingerprint
{
    /// <summary>仓里当前源码的指纹（<c>reading.txt</c> 的 <c>source=</c> 该等于它）。</summary>
    public static string OfRepo() => Of(Repo.Root);

    /// <summary>指定根目录下按同一口径算出来的指纹（测试里也拿临时小树验「改一个字节就变」）。</summary>
    public static string Of(string root)
    {
        var text = new StringBuilder();
        foreach (var file in Files(root))
        {
            text.Append(Sha1Hex(File.ReadAllBytes(file)))
                .Append("  ")
                .Append(RelativeKey(root, file))
                .Append('\n');
        }

        return Sha1Hex(Encoding.UTF8.GetBytes(text.ToString()));
    }

    /// <summary>
    /// 参与哈希的那一串字符：<c>./</c> + 仓库内相对路径 + <c>/</c> 分隔
    /// （与 shell 那一支 <c>find .</c> 打出来的形状逐字相同）。
    /// <para>
    /// <b>为什么不是绝对路径</b>：绝对路径里带着仓库所在的那一段，同一版源码换一个 clone 路径
    /// 就算出另一个指纹（第三百九十九轮修掉的冷启动缺陷）。
    /// </para>
    /// </summary>
    public static string RelativeKey(string root, string file) =>
        "./" + Path.GetRelativePath(root, file).Replace('\\', '/');

    /// <summary>
    /// 参与指纹的文件（绝对路径，已按<b>相对路径</b>的字节序排好）——与 shell 那一支的 <c>find</c> 逐条对应。
    /// <para>
    /// 剪枝在<b>遍历时</b>做（不是先枚举再过滤）：<c>bin</c> / <c>obj</c> 底下有成千上万个文件，
    /// 先枚举再丢掉只是把同一件事做慢。
    /// </para>
    /// <para>
    /// 排序键是<b>参与哈希的那串字符</b>（相对路径），不是绝对路径：两者只差一个公共前缀，
    /// 排出来的次序通常一样，但「按什么排」与「按什么算」必须是同一件事（<c>demo.ps1</c>
    /// 第二百零七轮就是栽在这条上）。
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Files(string root)
    {
        var tests = Path.Combine(root, "ApifoxHelper.Tests") + Path.DirectorySeparatorChar;
        return Walk(root)
            .Where(f => !f.StartsWith(tests, StringComparison.Ordinal))
            .OrderBy(f => RelativeKey(root, f), StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> Walk(string dir)
    {
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            // 与 `-name bin -prune -o -name obj -prune` 同一件事：任何一层叫这两个名字的目录都不进。
            if (name is "bin" or "obj") continue;
            foreach (var file in Walk(sub)) yield return file;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.cs")) yield return file;
    }

    /// <summary>小写十六进制 sha1（<c>sha1sum</c> 的输出形态）。</summary>
    private static string Sha1Hex(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
}
