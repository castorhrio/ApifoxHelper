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
/// <c>find "$REPO" -name bin -prune -o -name obj -prune -o -name '*.cs'
/// -not -path "$REPO/ApifoxHelper.Tests/*" -print0 | LC_ALL=C sort -z | xargs -0 sha1sum | sha1sum</c>。
/// 也就是：<b>全部 <c>*.cs</c>，剪掉任何一层叫 <c>bin</c> / <c>obj</c> 的目录，剪掉测试工程</b>
/// （测试工程不编进冒烟产物，改了它不该要求重发冒烟器），按路径字节序排好，逐文件 sha1，
/// 再把「<c>哈希 + 两个空格 + 绝对路径</c>」这一串整体 sha1 一次。
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
                .Append(file)
                .Append('\n');
        }

        return Sha1Hex(Encoding.UTF8.GetBytes(text.ToString()));
    }

    /// <summary>
    /// 参与指纹的文件（绝对路径，已按字节序排好）——与 shell 那一支的 <c>find</c> 逐条对应。
    /// <para>
    /// 剪枝在<b>遍历时</b>做（不是先枚举再过滤）：<c>bin</c> / <c>obj</c> 底下有成千上万个文件，
    /// 先枚举再丢掉只是把同一件事做慢。
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Files(string root)
    {
        var tests = Path.Combine(root, "ApifoxHelper.Tests") + Path.DirectorySeparatorChar;
        return Walk(root)
            .Where(f => !f.StartsWith(tests, StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
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
