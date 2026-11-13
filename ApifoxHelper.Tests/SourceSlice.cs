namespace ApifoxHelper.Tests;

/// <summary>
/// 「从一份源码里取出一段方法体」的唯一口径（第二百六十四轮，简报 P1-2）。
/// <para>
/// <b>为什么要有这一份</b>：同一件事此前在测试工程里有<b>两份</b>实现——
/// <c>CliCrlfOutputTests.BodyOf</c>（第二百六十二轮硬化过：先抹掉字面量再数花括号，认普通 / 插值 /
/// 逐字 / 原始字符串与字符字面量）与 <c>ProfileJumpTests.BodyOf</c>（第二百六十三轮新写的简化版：
/// 直接数原文的花括号，注释里自承「不认字符串里的花括号」）。两份都在数花括号，一份认得字面量、
/// 一份不认——**这不产生 bug，但它让「方法体界定」这句话有两个意思**：同一段源码喂给两份，
/// 一份能界定、另一份会错位，而错位的方式是「找不到闭合」或「闭合到下一个方法」，
/// 两种都会让判据去判一段不是它的代码。本仓的纪律是「同一件事只有一个落点」，
/// 故收敛到这里一份，两处调用同源。
/// </para>
/// <para>
/// <b>它怎么界定</b>：① 先把字面量抹成空格（<see cref="MaskLiterals"/>，长度与换行位置不变，
/// 下标仍对得上原文）；② 在抹过的那一份上从签名往后找第一对配对的花括号——数括号必须在抹过的那一份上数，
/// 否则方法体里一句 <c>StartsWith('{')</c> 就会让方法体永远「没有闭合」；③ 取<b>原文</b>的同一段，
/// 交给 <see cref="CodeScan.Text"/> 去掉注释（去注释全仓只有 <see cref="CodeScan"/> 一份，
/// 这里不写第二份）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判取出来的那段代码对不对——那是调用方的事。它只保证「取到的是那一个方法」，
/// 取不到就<b>抛</b>（不静默返回半段或空串：空方法体上任何断言都会红在别的地方，读的人只会看到一句
/// 莫名其妙的失败）。
/// </para>
/// </summary>
internal static class SourceSlice
{
    /// <summary>
    /// 从一份源码里取出 <paramref name="header"/> 那个方法的方法体（含首尾花括号），已去掉注释。
    /// <para>
    /// 抽成「源码进、方法体出」的纯函数，是为了让调用方能拿<b>假源码</b>喂它——
    /// 第二百六十二轮之前它直接读仓里的文件，于是「换一种字面量写法会不会界定错」这件事
    /// 只能靠人读代码，判据自己没被跑过（第二百零九轮那两发假变异同一课）。
    /// </para>
    /// </summary>
    public static string BodyOf(string source, string header)
    {
        var masked = MaskLiterals(source);
        var start = masked.IndexOf(header, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException(
                $"源码里找不到「{header}」——判据要判的那一段不在了（签名改了、或它被挪到了别的文件）。");
        }

        var open = masked.IndexOf('{', start);
        if (open <= start)
        {
            throw new InvalidOperationException($"「{header}」后面没有方法体。");
        }

        var depth = 0;
        for (var i = open; i < masked.Length; i++)
        {
            if (masked[i] == '{') depth++;
            else if (masked[i] == '}' && --depth == 0) return CodeScan.Text(source[open..(i + 1)]);
        }

        throw new InvalidOperationException(
            $"「{header}」的方法体没有闭合——最可能的原因是方法体里出现了 MaskLiterals 认不出的字面量写法"
            + "（逐字 / 原始 / 带嵌套引号的插值字符串），它把后面的花括号一起抹掉了。先扩 MaskLiterals，再改这个方法体。");
    }

    /// <summary>
    /// 把字符串字面量、字符字面量与行注释抹成空格（长度与换行位置不变，下标仍对得上原文）。
    /// <para>
    /// <b>它认哪几种字面量</b>：普通字符串 <c>"…"</c>、插值字符串 <c>$"…"</c>、逐字字符串
    /// <c>@"…"</c>（含 <c>$@"…"</c> / <c>@$"…"</c>）、原始字符串 <c>"""…"""</c>（引号数 ≥ 3，可带
    /// <c>$</c> 前缀）、字符字面量 <c>'…'</c>、行注释 <c>//…</c>。
    /// </para>
    /// <para>
    /// <b>为什么第二百六十二轮要扩它</b>：此前只认普通字符串与字符字面量，于是逐字字符串
    /// <c>@"C:\"</c> 会被当成「<c>\"</c> 是转义引号」——它一路吃到行尾，把同一行后面的
    /// <c>if (x) { y(); }</c> 连花括号一起抹掉，方法体界定当场错位（<b>错位方向是「找不到闭合」或
    /// 「闭合到下一个方法」</b>，两种都会让形状门判错东西）。原始字符串 <c>"""…"""</c> 同理：
    /// 三个引号会被读成「空字符串 + 一个开引号」。这不是「Parse 现在有这些写法」，而是
    /// <b>判据只在特定输入下正确</b>——与仓库「判据自己被抓得住」的纪律不符。
    /// </para>
    /// <para>
    /// <b>它仍然不是通用 C# 词法器</b>：插值字符串的 <c>{…}</c> 里若再嵌一个带引号的表达式
    /// （<c>$"{a ?? "b"}"</c>），这里会把内层引号当成外层字符串的结尾。这一条<b>没有</b>被覆盖，
    /// 由 <c>CliCrlfOutputTests.ParseBody_BoundsTheMethodOnEveryLiteralForm</c> 的最后一发注入如实登记：
    /// 真要在被界定的方法体里写这种形态，先扩这个方法，别指望它自己扛住。
    /// </para>
    /// </summary>
    public static string MaskLiterals(string text)
    {
        var chars = text.ToCharArray();
        var i = 0;
        while (i < chars.Length)
        {
            if (chars[i] == '/' && i + 1 < chars.Length && chars[i + 1] == '/')
            {
                while (i < chars.Length && chars[i] != '\n')
                {
                    chars[i] = ' ';
                    i++;
                }

                continue;
            }

            // 原始字符串 """…"""（引号数 ≥ 3，可带 $ 前缀）：抹到与开头等量的连续引号为止。
            if (chars[i] == '"' && i + 2 < chars.Length && chars[i + 1] == '"' && chars[i + 2] == '"')
            {
                var quotes = 0;
                while (i + quotes < chars.Length && chars[i + quotes] == '"') quotes++;
                for (var k = 0; k < quotes; k++) chars[i + k] = ' ';
                i += quotes;
                while (i < chars.Length)
                {
                    if (chars[i] != '"')
                    {
                        chars[i] = ' ';
                        i++;
                        continue;
                    }

                    var run = 0;
                    while (i + run < chars.Length && chars[i + run] == '"') run++;
                    for (var k = 0; k < run; k++) chars[i + k] = ' ';
                    i += run;
                    if (run >= quotes) break;
                }

                continue;
            }

            // 逐字字符串 @"…" / $@"…" / @$"…"："" 是转义引号，反斜杠不是转义符，可以跨行。
            var verbatimAt = -1;
            if (chars[i] == '@' && i + 1 < chars.Length && chars[i + 1] == '"') verbatimAt = i + 1;
            else if (chars[i] == '$' && i + 2 < chars.Length && chars[i + 1] == '@' && chars[i + 2] == '"') verbatimAt = i + 2;
            else if (chars[i] == '@' && i + 2 < chars.Length && chars[i + 1] == '$' && chars[i + 2] == '"') verbatimAt = i + 2;
            if (verbatimAt >= 0)
            {
                for (var k = i; k <= verbatimAt; k++) chars[k] = ' ';
                i = verbatimAt + 1;
                while (i < chars.Length)
                {
                    if (chars[i] == '"' && i + 1 < chars.Length && chars[i + 1] == '"')
                    {
                        chars[i] = ' ';
                        chars[i + 1] = ' ';
                        i += 2;
                        continue;
                    }

                    if (chars[i] == '"')
                    {
                        chars[i] = ' ';
                        i++;
                        break;
                    }

                    chars[i] = ' ';
                    i++;
                }

                continue;
            }

            if (chars[i] is '"' or '\'')
            {
                var quote = chars[i];
                chars[i] = ' ';
                i++;
                while (i < chars.Length)
                {
                    if (chars[i] == '\\')
                    {
                        chars[i] = ' ';
                        if (i + 1 < chars.Length) chars[i + 1] = ' ';
                        i += 2;
                        continue;
                    }

                    if (chars[i] == quote)
                    {
                        chars[i] = ' ';
                        i++;
                        break;
                    }

                    if (chars[i] == '\n') break; // 未闭合：不把整份文件吃掉
                    chars[i] = ' ';
                    i++;
                }

                continue;
            }

            i++;
        }

        return new string(chars);
    }
}
