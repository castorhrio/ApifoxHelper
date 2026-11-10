using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// <see cref="CodeScan"/> 的真值表（第一百六十八轮，简报 P0-2）。
/// <para>
/// <b>存在的理由</b>：本仓「接线断言被注释行骗过」这一族已经犯过三次，第一百六十七轮收口时只挡住了
/// 行首 <c>//</c>。这一族要真正收口，判据必须自己可测：<b>把块注释判断删掉，这里就红</b>；
/// 反过来，把字符串字面量里的 <c>/*</c> 当成注释起点（本仓 <c>Source/DocComments.cs</c> 真有这种字面量），
/// 这里也红——后者比原来的盲区更糟，因为它会让门漏掉真代码还看起来是绿的。
/// </para>
/// </summary>
public class CodeScanTests
{
    [Fact]
    public void LineComment_IsNotCode()
    {
        const string source = """
            Console.WriteLine(line);
            // Console.WriteLine(other);
                /// 文档注释里写 Console.WriteLine(third);
            """;
        Assert.True(CodeScan.HasLine(source, "Console.WriteLine(line);"));
        Assert.False(CodeScan.HasLine(source, "Console.WriteLine(other);"));
        Assert.False(CodeScan.HasLine(source, "Console.WriteLine(third);"));
    }

    /// <summary>简报点名的那一条：整行块注释里的代码不算代码行。</summary>
    [Fact]
    public void WholeLineBlockComment_IsNotCode()
    {
        const string source = """
            /* Console.WriteLine(line); */
            /** Console.WriteLine(other); */
            """;
        Assert.False(CodeScan.HasLine(source, "Console.WriteLine(line);"));
        Assert.False(CodeScan.HasLine(source, "Console.WriteLine(other);"));
        Assert.Empty(CodeScan.Lines(source));
    }

    /// <summary>跨行的块注释：区间内的每一行都不是代码，注释结束之后恢复。</summary>
    [Fact]
    public void MultiLineBlockComment_HidesEverythingInsideIt()
    {
        const string source = """
            Alpha();
            /* 说明：
            Beta();
              Gamma();
            */
            Delta();
            """;
        var kept = string.Join('\n', CodeScan.Lines(source));
        Assert.Contains("Alpha();", kept, StringComparison.Ordinal);
        Assert.Contains("Delta();", kept, StringComparison.Ordinal);
        Assert.DoesNotContain("Beta();", kept, StringComparison.Ordinal);
        Assert.DoesNotContain("Gamma();", kept, StringComparison.Ordinal);
    }

    /// <summary>同一行里注释前后都是代码：只掉中间那一段。</summary>
    [Fact]
    public void InlineBlockComment_DropsOnlyItsOwnText()
    {
        const string source = "catch { /* Hidden(); */ } Publicly();";
        Assert.True(CodeScan.HasLine(source, "catch {"));
        Assert.True(CodeScan.HasLine(source, "Publicly();"));
        Assert.False(CodeScan.HasLine(source, "Hidden();"));
        // 这一行仍然是代码行：注释是它的一部分，不是整行的属性。
        Assert.Single(CodeScan.Lines(source));
    }

    [Fact]
    public void TrailingLineComment_DropsItsOwnTextOnly()
    {
        const string source = "Publicly(); // Hidden();";
        Assert.True(CodeScan.HasLine(source, "Publicly();"));
        Assert.False(CodeScan.HasLine(source, "Hidden();"));
    }

    /// <summary>
    /// 字符串字面量里的 <c>/*</c> 与 <c>//</c> 不开注释。本仓真有这么写的源码
    /// （<c>Source/DocComments.cs</c> 的 <c>Replace("/**", "")</c>、<c>Api/HelperHost.cs</c> 的 <c>http://</c>）：
    /// 把它们当注释起点会让后面的真代码整片消失，门会因此<b>漏判而不是误判</b>。
    /// </summary>
    [Fact]
    public void StringLiterals_AreNotCommentStarts()
    {
        const string source = """
            joined = joined.Replace("/**", "").Replace("*/", "");
            AfterTheLiteral();
            var url = $"http://127.0.0.1:{Port}/";
            AlsoHere();
            """;
        var kept = string.Join('\n', CodeScan.Lines(source));
        Assert.Contains("Replace(\"/**\", \"\")", kept, StringComparison.Ordinal);
        Assert.Contains("AfterTheLiteral();", kept, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1", kept, StringComparison.Ordinal);
        Assert.Contains("AlsoHere();", kept, StringComparison.Ordinal);
    }

    /// <summary>逐字字符串、字符字面量与原始字符串里的注释符号都不开注释状态。</summary>
    [Fact]
    public void OtherLiteralForms_DontSwallowTheCodeAfterThem()
    {
        Assert.True(CodeScan.HasLine("var a = @\"path/*not a comment\"; Kept();", "Kept();"), "逐字字符串里的 /* 开了注释");
        Assert.True(CodeScan.HasLine("var b = '\''; Kept();", "Kept();"), "字符字面量让后面丢了");
        Assert.True(CodeScan.HasLine("var c = 0; /* 说明 */ Kept();", "Kept();"));

        var raw = "var s = \"\"\"text\n/* 原始字符串里的这一行不是注释\nKept();\n\"\"\";\nAfterTheRaw();";
        Assert.True(CodeScan.HasLine(raw, "Kept();"), "原始字符串里的 /* 开了注释");
        Assert.True(CodeScan.HasLine(raw, "AfterTheRaw();"), "原始字符串结束后代码没有恢复");
    }

    /// <summary><see cref="CodeScan.IndexOfLine"/> 比的是位置：注释里那份必须被跳过。</summary>
    [Fact]
    public void IndexOfLine_SkipsTheCopyInAComment()
    {
        const string commented = "// form.Scale(new SizeF(factor, factor));\n";
        Assert.Equal(-1, CodeScan.IndexOfLine(commented, "form.Scale(new SizeF(factor, factor));"));

        var source = "// form.Alpha();\nform.Alpha();\nform.Beta();";
        Assert.Equal("// form.Alpha();\n".Length, CodeScan.IndexOfLine(source, "form.Alpha();"));
        Assert.True(CodeScan.IndexOfLine(source, "form.Alpha();") < CodeScan.IndexOfLine(source, "form.Beta();"),
            "两处都在代码里时，先后顺序要照原文给出。");
    }

    /// <summary>跨过去掉的注释片段拼出来的那一次命中不算（<c>A();/*x*/B();</c> 里不存在 <c>A();B();</c>）。</summary>
    [Fact]
    public void IndexOfLine_NeverMatchesAcrossADroppedComment()
    {
        const string source = "A(); /* x */ B();";
        Assert.Equal(-1, CodeScan.IndexOfLine(source, "A(); B();"));
        Assert.True(CodeScan.IndexOfLine(source, "A();") >= 0);
    }

    [Fact]
    public void CountAndLinesContaining_UseCodeLinesOnly()
    {
        const string source = """
            Ui.StatusLine();
            // Ui.StatusLine();
            /* Ui.StatusLine(); */
            /*
             * Ui.StatusLine();
             */
            """;
        Assert.Equal(1, CodeScan.CountContaining(source, "Ui.StatusLine();"));
        Assert.Single(CodeScan.LinesContaining(source, "Ui.StatusLine();"));
    }

    [Fact]
    public void EmptyAndBlankSources_HaveNoCodeLines()
    {
        Assert.Empty(CodeScan.Lines(string.Empty));
        Assert.Empty(CodeScan.Lines("\n\n   \n"));
        Assert.Empty(CodeScan.Text("// 只有注释"));
    }

    /// <summary>
    /// 「排除注释」这一条口径在本仓只有一份实现。
    /// <para>
    /// <b>存在的理由</b>：这一族已经犯过三次——第二十七轮（接线断言被子串匹配 + 注释行骗过）、
    /// 第一百六十七轮（被注释掉的 <c>_lines.Add(line);</c> 骗过）、本轮（<c>/* … */</c> 区间内的同一行代码骗过）。
    /// 每一次的形态都不同，共同点是<b>每个文件自己写了一份「去注释」</b>：修一处只修那一处。
    /// 这条把「再写一份过滤」直接判红——第四种形态没有落脚处：要么用 <see cref="CodeScan"/>，要么看着它红。
    /// </para>
    /// </summary>
    [Fact]
    public void CommentFilter_IsWrittenInOnePlaceOnly()
    {
        // 字面量拼出来，避免这个文件自己成为它要抓的那一处。
        var markers = new[] { "StartsWith(" + "\"//\"", "StartsWith(" + "\"///\"", "StartsWith(" + "\"*\"" };
        var offenders = Directory.GetFiles(Path.Combine(Repo.Root, "ApifoxHelper.Tests"), "*.cs")
            .Where(f => Path.GetFileName(f) != "CodeScan.cs")
            .SelectMany(f => CodeScan.Lines(File.ReadAllText(f))
                .Where(line => markers.Any(m => line.Contains(m, StringComparison.Ordinal)))
                .Select(line => $"{Path.GetFileName(f)}：{line.Trim()}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "这些行自己写了一份「去注释」，而它挡不住 /* … */ 区间里的行：" + string.Join(" ／ ", offenders)
            + "（注释口径只在 CodeScan.cs 一份）。");
    }

    /// <summary>行尾 <c>\r</c> 不参与判定（读进来的源码可能是 CRLF）。</summary>
    [Fact]
    public void CarriageReturns_AreNotPartOfTheJudgement() =>
        Assert.True(CodeScan.HasLine("Alpha();\r\n// Beta();\r\n", "Alpha();"));

    /// <summary>
    /// 真实源码自查：主工程与服务层里每一行<b>本身不含注释符号</b>、且以 <c>;</c>、<c>{</c>、<c>}</c> 收尾的行，
    /// 扫描之后必须还在。
    /// <para>
    /// 这条挡的不是「本行的注释被去掉」（那本来就该去掉），而是<b>状态串了</b>：某处字面量被当成注释起点，
    /// 之后一整片真代码消失，门会因此静默失去牙齿——上面那些真值表全都测不到它。
    /// 本仓 <c>Source/DocComments.cs</c> 与 <c>Api/HelperHost.cs</c> 正是这种误判的引信。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryRealSource_KeepsEveryOneOfItsCodeLines()
    {
        var scanned = Repo.NativeSources().Select(s => (s.Name, s.Text))
            .Concat(Repo.ServiceSources().Select(s => (s.Relative, s.Text)))
            .Concat([("Desktop/DesktopApp.cs", Repo.Source("Desktop", "DesktopApp.cs")),
                     ("Desktop/MainForm.cs", Repo.Source("Desktop", "MainForm.cs")),
                     ("Program.cs", Repo.Source("Program.cs"))]);

        var missing = new List<string>();
        foreach (var (name, text) in scanned)
        {
            var kept = string.Join('\n', CodeScan.Lines(text));
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                // 本身带注释符号的行不参与：它的代码部分与原文不同是正确行为，比不出东西。
                if (line.Contains("//", StringComparison.Ordinal)
                    || line.Contains("/*", StringComparison.Ordinal)
                    || line.Contains("*/", StringComparison.Ordinal)) continue;
                var last = line[^1];
                if (last != ';' && last != '{' && last != '}') continue;
                if (!kept.Contains(line, StringComparison.Ordinal)) missing.Add($"{name}：{line}");
            }
        }

        Assert.True(missing.Count == 0,
            $"扫描器在这些行之前误进了注释状态，真代码被吃掉：{string.Join(" ／ ", missing.Take(5))}");
    }
}
