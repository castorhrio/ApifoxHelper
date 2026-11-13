namespace ApifoxHelper.Tests;

/// <summary>
/// 「只算代码行」的唯一口径。测试里大量判定是「这一行代码在不在」——注释里引用同一行字是在
/// 说明理由、不是在接线，所以判定前必须先把注释去掉。
/// <para>
/// <b>为什么要有这一份</b>：这一族在本仓已经犯过三次（第二十七轮：接线断言用子串 <c>Contains</c>
/// 被 <c>//</c> 注释行骗过；第一百六十七轮：被注释掉的 <c>_lines.Add(line);</c> 骗过；
/// 第一百六十七轮收口时只修了「行首 <c>//</c>」这一种）。把 <c>//</c> 判掉之后，
/// <c>/* … */</c> 区间里的同一行代码照样能骗过门——那是第四种形态。本文件把「注释」按 C# 的
/// 两种写法一起判掉，测试工程里其余的「去注释」一律委托到这里，不再各写一份。
/// </para>
/// <para>
/// <b>字符串字面量不是注释</b>：本仓源码里真实存在 <c>joined.Replace("/**", "")</c>
/// （<c>Source/DocComments.cs</c>）与 <c>"http://127.0.0.1"</c>（<c>Api/HelperHost.cs</c>）这类字面量，
/// 只看字符序列的扫描器会把它们当成注释起点，从而<b>把后面的真代码整片当成注释放过</b>——
/// 那比原来的盲区更糟。故扫描时带字符串状态：<c>"…"</c>、<c>@"…"</c>、<c>"""…"""</c>、<c>'…'</c> 之内的
/// <c>//</c> 与 <c>/*</c> 都不算注释。
/// </para>
/// <para>
/// 已知不覆盖：内插字符串的 <c>{</c> 表达式里再套字符串时，状态机按普通字符串处理；
/// 本仓被扫描的源码没有这种写法（有则此处即红：<c>CodeScanTests.EveryRealSource_KeepsEveryOneOfItsCodeLines</c>）。
/// </para>
/// </summary>
internal static class CodeScan
{
    private enum State
    {
        Text,
        LineComment,
        BlockComment,
        String,
        VerbatimString,
        RawString,
        Char,
    }

    /// <summary>一行的代码部分：注释文字已去掉，缩进保留；<see cref="Offsets"/> 把它逐字映射回原文位置。</summary>
    private sealed record LineInfo(string Code, int[] Offsets);

    /// <summary>逐行扫描出的代码部分（含只有注释的行——它的 <see cref="LineInfo.Code"/> 是空的）。</summary>
    private static List<LineInfo> Parse(string source)
    {
        var result = new List<LineInfo>();
        var state = State.Text;
        var offset = 0;
        foreach (var raw in source.Split('\n'))
        {
            // 行尾 \r 不参与判定：仓库内源码是 LF，这里只容下 CRLF 的读法。
            var text = raw.EndsWith('\r') ? raw[..^1] : raw;
            result.Add(ScanLine(text, offset, ref state));
            offset += raw.Length + 1;
            // 行注释、普通字符串与字符字面量都不能跨行；跨行的一律当写到行尾为止。
            if (state is State.LineComment or State.String or State.Char) state = State.Text;
        }

        return result;
    }

    private static LineInfo ScanLine(string text, int lineStart, ref State state)
    {
        var code = new StringBuilder();
        var offsets = new List<int>();

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            var rest = i + 2 < text.Length ? text[i + 2] : '\0';

            switch (state)
            {
                case State.BlockComment:
                    if (ch == '*' && next == '/')
                    {
                        i++;
                        state = State.Text;
                    }

                    continue;

                case State.String:
                    code.Append(ch);
                    offsets.Add(lineStart + i);
                    if (ch == '\\' && next != '\0')
                    {
                        code.Append(next);
                        offsets.Add(lineStart + i + 1);
                        i++;
                    }
                    else if (ch == '"')
                    {
                        state = State.Text;
                    }

                    continue;

                case State.VerbatimString:
                    code.Append(ch);
                    offsets.Add(lineStart + i);
                    if (ch == '"' && next == '"')
                    {
                        code.Append(next);
                        offsets.Add(lineStart + i + 1);
                        i++;
                    }
                    else if (ch == '"')
                    {
                        state = State.Text;
                    }

                    continue;

                case State.RawString:
                    code.Append(ch);
                    offsets.Add(lineStart + i);
                    if (ch == '"' && next == '"' && rest == '"')
                    {
                        code.Append(next).Append(rest);
                        offsets.Add(lineStart + i + 1);
                        offsets.Add(lineStart + i + 2);
                        i += 2;
                        state = State.Text;
                    }

                    continue;

                case State.Char:
                    code.Append(ch);
                    offsets.Add(lineStart + i);
                    if (ch == '\\' && next != '\0')
                    {
                        code.Append(next);
                        offsets.Add(lineStart + i + 1);
                        i++;
                    }
                    else if (ch == '\'')
                    {
                        state = State.Text;
                    }

                    continue;

                default:
                    break;
            }

            // Text（含行注释起点判定）
            if (ch == '/' && next == '/')
            {
                state = State.LineComment;
                break;
            }

            if (ch == '/' && next == '*')
            {
                i++;
                state = State.BlockComment;
                continue;
            }

            if (ch == '"' && next == '"' && rest == '"')
            {
                code.Append(ch).Append(next).Append(rest);
                offsets.Add(lineStart + i);
                offsets.Add(lineStart + i + 1);
                offsets.Add(lineStart + i + 2);
                i += 2;
                state = State.RawString;
                continue;
            }

            // $"""…""" 与 @"""…"""：同样进原始字符串，否则下一行的引号会把状态彻底带偏。
            if ((ch == '$' || ch == '@') && next == '"' && rest == '"')
            {
                code.Append(ch).Append(next).Append(rest);
                offsets.Add(lineStart + i);
                offsets.Add(lineStart + i + 1);
                offsets.Add(lineStart + i + 2);
                i += 2;
                state = State.RawString;
                continue;
            }

            if ((ch == '@' || ch == '$') && next == '"' && rest != '"')
            {
                code.Append(ch).Append(next);
                offsets.Add(lineStart + i);
                offsets.Add(lineStart + i + 1);
                i++;
                state = ch == '@' ? State.VerbatimString : State.String;
                continue;
            }

            if ((ch == '@' || ch == '$') && ((next == '@' && rest == '"') || (next == '$' && rest == '"')))
            {
                code.Append(ch).Append(next).Append(text[i + 2]);
                offsets.Add(lineStart + i);
                offsets.Add(lineStart + i + 1);
                offsets.Add(lineStart + i + 2);
                i += 2;
                state = State.VerbatimString;
                continue;
            }

            // 普通字符串与字符字面量：里面的 /* 与 // 不是注释（本仓真有这么写的源码）。
            if (ch == '"')
            {
                state = State.String;
            }
            else if (ch == '\'')
            {
                state = State.Char;
            }

            code.Append(ch);
            offsets.Add(lineStart + i);
        }

        return new LineInfo(code.ToString(), offsets.ToArray());
    }

    /// <summary>代码行（注释行与块注释区间内的行不在内），保留原缩进。</summary>
    public static IEnumerable<string> Lines(string source) =>
        Parse(source).Select(l => l.Code).Where(code => code.Trim().Length > 0);

    /// <summary>
    /// 注释行的原文（<c>///</c> 文档注释、<c>//</c> 行注释、<c>/* … */</c> 区间内的行），保留缩进；
    /// 代码行与空行都不在内。
    /// <para>
    /// <b>为什么这一份也放在这里</b>：第二百五十三轮要判「类注释里念的读数与算式取值是否一致」，
    /// 那需要<b>注释文字</b>而不是代码行。若在测试文件里自己写一句「取 <c>///</c> 开头的行」，
    /// 就是本仓明令禁止的「第二份去注释」——<c>CodeScanTests.CommentFilter_IsWrittenInOnePlaceOnly</c>
    /// 会当场红。故取注释也走同一台状态机：<see cref="Parse"/> 判出「这一行没有代码」，
    /// 原文那一行就是注释。
    /// </para>
    /// </summary>
    public static IEnumerable<string> CommentLines(string source)
    {
        var raw = source.Split('\n');
        var parsed = Parse(source);
        for (var i = 0; i < parsed.Count && i < raw.Length; i++)
        {
            if (parsed[i].Code.Trim().Length == 0 && raw[i].Trim().Length > 0) yield return raw[i];
        }
    }

    /// <summary>代码行，去掉行首缩进。</summary>
    public static IEnumerable<string> TrimmedLines(string source) => Lines(source).Select(code => code.TrimStart());

    /// <summary>把代码行重新拼成一份文本（注释整体消失，其余逐字不变）。</summary>
    public static string Text(string source) => string.Join('\n', Lines(source));

    /// <summary><paramref name="source"/> 的代码行里是否出现 <paramref name="fragment"/>。</summary>
    public static bool HasLine(string source, string fragment) =>
        Lines(source).Any(code => code.Contains(fragment, StringComparison.Ordinal));

    /// <summary>出现 <paramref name="fragment"/> 的代码行数。</summary>
    public static int CountContaining(string source, string fragment) =>
        Lines(source).Count(code => code.Contains(fragment, StringComparison.Ordinal));

    /// <summary>出现 <paramref name="fragment"/> 的代码行（去缩进）。</summary>
    public static List<string> LinesContaining(string source, string fragment) =>
        TrimmedLines(source).Where(code => code.Contains(fragment, StringComparison.Ordinal)).ToList();

    /// <summary>
    /// <paramref name="fragment"/> 第一次出现在<b>代码</b>里的位置（原文中的字符下标），注释里的引用一律跳过。
    /// 找不到回 -1。给「几步接线的先后顺序」这类判定用——它们比的是位置，不是有无。
    /// </summary>
    public static int IndexOfLine(string source, string fragment)
    {
        foreach (var line in Parse(source))
        {
            for (var at = line.Code.IndexOf(fragment, StringComparison.Ordinal); at >= 0;
                 at = line.Code.IndexOf(fragment, at + 1, StringComparison.Ordinal))
            {
                // 命中段的字符在原文里必须连着——跨过去掉的注释片段的那一次出现，是注释在说话。
                if (at + fragment.Length > line.Offsets.Length) continue;
                var contiguous = true;
                for (var j = 1; j < fragment.Length; j++)
                {
                    if (line.Offsets[at + j] != line.Offsets[at] + j)
                    {
                        contiguous = false;
                        break;
                    }
                }

                if (contiguous) return line.Offsets[at];
            }
        }

        return -1;
    }
}
