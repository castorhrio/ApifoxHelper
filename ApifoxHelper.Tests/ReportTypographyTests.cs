using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ApifoxHelper.Reports;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 报告正文的中文排版机械自检（DESIGN.md §3「中文排版附加条款」、设计验收 D4/D10）。
/// <para>
/// 为什么扫报告：报告是用户会原样发给前端与测试的那份东西（F14），而它此前一条排版断言都没有——
/// 渲染在 <c>ReportService</c> 的实例方法里，依赖 <c>TaskStore</c> 与未共享编译的模型类型，
/// Linux 侧构造不出来。第一百四十一轮把渲染原样提取到 <see cref="ReportRender"/>（纯 BCL），
/// 本文件共享编译后**真执行**渲染，断言的是主工程同一份代码产出的字节。
/// </para>
/// <para>
/// 分档口径：报告是 Markdown 文档，<c>#</c> / <c>-</c> / <c>|</c> / <c>&gt;</c> 是它的结构记号（合法）；
/// 界面文案不是。HTML 那一侧相反：结构记号必须已经变成标签，正文里再出现就是泄漏。
/// 数据行（接口路径、远端说明、未完成事项）由数据决定，不在行宽检查范围——检查的是
/// **渲染器自己写的那句话**（用数据位一律取最短值的合成报告量）。
/// </para>
/// </summary>
public class ReportTypographyTests
{
    // ── 合成报告 ─────────────────────────────────────────────────────

    /// <summary>
    /// 覆盖 Markdown 每一条分支的合成报告：分阶段表格、实际写入（含范围外影响）、接口变化
    /// （不兼容/结构/说明三类条目）、未完成事项、限制清单、验证边界。
    /// 数据全部是解释产品行为的假设值，不指向真实项目。
    /// </summary>
    private static JsonObject Report(string? changeKey = null, string? sourceStatus = null) => new()
    {
        ["task"] = new JsonObject
        {
            ["id"] = "t1",
            ["name"] = "订单模块文档更新",
            ["createdAt"] = "2026-11-10T09:00:00+08:00",
            ["toolVersion"] = "1.0.0",
            ["cliVersion"] = "2.2.6",
            ["sourceVersionNote"] = "v1.2.3",
            ["scope"] = new JsonArray("Orders"),
        },
        ["target"] = new JsonObject
        {
            ["projectName"] = "订单服务",
            ["projectId"] = "123456",
            ["branch"] = "main（主分支）",
        },
        ["source"] = new JsonObject
        {
            ["workspace"] = "/repo",
            ["apiProject"] = "Orders.Api/Orders.Api.csproj",
            ["analysisLevel"] = "Partial",
            ["manifestHash"] = "abc",
            ["swagger"] = "file: swagger.json",
            ["swaggerHash"] = "def",
        },
        ["stages"] = new JsonObject
        {
            ["sourceComments"] = new JsonObject
            {
                ["status"] = sourceStatus ?? "completed",
                ["coverage"] = new JsonObject { ["valid"] = 3, ["inheritDoc"] = 1, ["total"] = 8 },
                ["unresolvedSymbols"] = 2,
                ["unresolvedCalls"] = 1,
                ["patchExported"] = false,
                ["commentPlan"] = new JsonObject { ["status"] = "applied", ["files"] = new JsonArray(), ["skipped"] = 0 },
            },
            ["generatedDocs"] = new JsonObject
            {
                ["status"] = "completed",
                ["message"] = "已核对",
                ["entries"] = 8,
                ["inSource"] = 8,
                ["inXml"] = 6,
                ["inSwagger"] = 5,
            },
            ["apifox"] = new JsonObject
            {
                ["status"] = "completed",
                ["structureSource"] = "swagger",
                ["snapshotComplete"] = true,
                ["plan"] = new JsonObject
                {
                    ["status"] = "verified",
                    ["actions"] = new JsonArray(new JsonObject
                    {
                        ["kind"] = "endpointUpdate",
                        ["resourceKey"] = "GET /api/orders",
                        ["remoteId"] = 42,
                        ["state"] = "verified",
                        ["message"] = "已写入并核对通过",
                    }),
                    ["outOfScopeImpact"] = new JsonArray(new JsonObject
                    {
                        ["key"] = "OrderDto",
                        ["name"] = "订单模型",
                        ["id"] = 7,
                    }),
                },
            },
            ["businessRuntime"] = new JsonObject { ["status"] = "notRun", ["message"] = "第一版不执行" },
        },
        ["changes"] = new JsonArray(new JsonObject
        {
            ["key"] = changeKey ?? "GET /api/orders",
            ["status"] = "结构变化",
            ["incompatible"] = new JsonArray("新增必填 query 参数 page"),
            ["structure"] = new JsonArray("query.page 新增"),
            ["docs"] = new JsonArray("接口说明已合并"),
        }),
        ["unfinished"] = new JsonArray("注释未补全：OrdersController.List summary（missing）"),
        // 四句固定说明取主工程同一份（ReportRender.Limits），测试工程不留第二份副本。
        ["limits"] = new JsonArray(ReportRender.Limits.Select(text => (JsonNode?)JsonValue.Create(text)).ToArray()),
    };

    /// <summary>
    /// 数据位一律取最短值的合成报告：渲染出来的每一行都只剩「渲染器自己写的那句话」，
    /// 行宽检查量的就是它（数据行由数据决定，不该被这条检查判红）。
    /// </summary>
    private static JsonObject MinimalReport() => new()
    {
        ["task"] = new JsonObject
        {
            ["id"] = "x", ["name"] = "x", ["createdAt"] = "x", ["toolVersion"] = "x",
            ["cliVersion"] = "x", ["sourceVersionNote"] = "x", ["scope"] = new JsonArray(),
        },
        ["target"] = new JsonObject { ["projectName"] = "x", ["projectId"] = "x", ["branch"] = "x" },
        ["source"] = new JsonObject
        {
            ["workspace"] = "x", ["apiProject"] = "x", ["analysisLevel"] = "x",
            ["manifestHash"] = "x", ["swagger"] = "x", ["swaggerHash"] = "x",
        },
        ["stages"] = new JsonObject
        {
            ["sourceComments"] = new JsonObject
            {
                ["status"] = "x",
                ["coverage"] = new JsonObject { ["valid"] = 0, ["inheritDoc"] = 0, ["total"] = 0 },
                ["unresolvedSymbols"] = 0,
                ["unresolvedCalls"] = 0,
                ["patchExported"] = false,
                ["commentPlan"] = new JsonObject { ["status"] = "x", ["files"] = new JsonArray(), ["skipped"] = 0 },
            },
            ["generatedDocs"] = new JsonObject
            {
                ["status"] = "x", ["message"] = "x", ["entries"] = 0,
                ["inSource"] = 0, ["inXml"] = 0, ["inSwagger"] = 0,
            },
            ["apifox"] = new JsonObject
            {
                ["status"] = "x",
                ["structureSource"] = "x",
                ["snapshotComplete"] = true,
                ["plan"] = new JsonObject
                {
                    ["status"] = "x",
                    ["actions"] = new JsonArray(new JsonObject
                    {
                        ["kind"] = "x", ["resourceKey"] = "x", ["remoteId"] = 0,
                        ["state"] = "x", ["message"] = "x",
                    }),
                    ["outOfScopeImpact"] = new JsonArray(new JsonObject
                    {
                        ["key"] = "x", ["name"] = "x", ["id"] = 0,
                    }),
                },
            },
            ["businessRuntime"] = new JsonObject { ["status"] = "x", ["message"] = "x" },
        },
        ["changes"] = new JsonArray(new JsonObject
        {
            ["key"] = "x",
            ["status"] = "x",
            ["incompatible"] = new JsonArray("x"),
            ["structure"] = new JsonArray("x"),
            ["docs"] = new JsonArray("x"),
        }),
        ["unfinished"] = new JsonArray("x"),
        ["limits"] = new JsonArray("x"),
    };

    // ── 行宽（DESIGN §3：正文容器 ≤45em）────────────────────────────

    /// <summary>
    /// 中文按 1em、半角按 0.5em 折算的显示宽度。报告是纯文本，没有字号可量，
    /// 故按「一个汉字占一格」折算——45em 就是 45 个汉字宽，与 DESIGN §3 的容器上限同一口径。
    /// </summary>
    private static double WidthEm(string text)
    {
        double width = 0;
        foreach (var c in text) width += c < 0x2E80 ? 0.5 : 1.0;
        return width;
    }

    /// <summary>正文行：去掉表格行（结构行，列宽由表格决定）与标题行（标题不折行）。</summary>
    private static IEnumerable<string> ProseLines(string markdown) =>
        markdown.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0 && !line.StartsWith('|') && !line.StartsWith('#'));

    [Fact]
    public void Markdown_FixedProseLines_AreWithin45Em()
    {
        var markdown = ReportRender.Markdown(MinimalReport());
        var tooWide = ProseLines(markdown)
            .Select(line => (Line: line, Width: WidthEm(line)))
            .Where(row => row.Width > 45)
            .ToList();
        Assert.True(
            tooWide.Count == 0,
            "报告正文里这些行超过 45em（DESIGN §3 的正文容器上限）："
            + string.Join("；", tooWide.Select(row => $"{row.Width:0.#}em「{row.Line}」")));
    }

    /// <summary>
    /// 报告里最长的那几句固定说明是「检查覆盖范围与限制」清单（它们不随数据变化，是产品自己写的话）。
    /// 渲染器那一侧的行宽由上面那条用例钉住，这一条钉住那四句——它们进 Markdown 时是列表项，
    /// 同样受 45em 约束。
    /// <para>
    /// 第一百四十二轮：这四句此前写在 <c>ReportService.Build</c> 里，本用例只能从源码里用正则抠
    /// 字符串字面量——抠不到就静默通过（提取器失效时检查看起来仍然是绿的）。现在四句是
    /// <see cref="ReportRender.Limits"/> 一份常量，本用例直接量它，另有一条钉住 Build 引用同一份。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildLimits_AreWithin45Em()
    {
        Assert.True(ReportRender.Limits.Count >= 4, $"限制清单只剩 {ReportRender.Limits.Count} 句（清单被删短了）。");
        var tooWide = ReportRender.Limits.Where(text => WidthEm(text) > 45).ToList();
        Assert.True(tooWide.Count == 0, "限制清单里这些句子超过 45em：" + string.Join("；", tooWide));
        // 四句都真的进了报告正文（常量存在但没被渲染，等于用户看不到）。
        var markdown = ReportRender.Markdown(Report());
        foreach (var text in ReportRender.Limits)
        {
            Assert.Contains($"- {text}", markdown, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 四句固定说明只有一份：<c>ReportService.Build</c> 必须引用 <see cref="ReportRender.Limits"/>，
    /// 不得在服务层再抄一遍字面量（抄一份就多一处会漂移的副本，而排版检查只量得到其中一份）。
    /// </summary>
    [Fact]
    public void Build_TakesLimitsFromTheSingleSource()
    {
        var source = Repo.Source("Reports", "ReportService.cs");
        Assert.Contains("ReportRender.Limits", source, StringComparison.Ordinal);
        foreach (var text in ReportRender.Limits)
        {
            Assert.DoesNotContain(text, source, StringComparison.Ordinal);
        }
    }

    // ── 中文标点与记号（复用 CopyRules 口径）────────────────────────

    [Fact]
    public void Markdown_UsesFullWidthPunctuation()
    {
        var markdown = ReportRender.Markdown(Report());
        foreach (var line in markdown.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            foreach (var mark in CopyRules.HalfWidthMarks)
            {
                var index = CopyRules.IndexOfHalfWidthMark(line, mark);
                Assert.True(index < 0, $"报告里半角「{mark}」紧挨中文：「{line}」（位置 {index}）。");
            }
            var colon = CopyRules.IndexOfStrayColon(line);
            Assert.True(colon < 0, $"报告里半角冒号紧挨中文：「{line}」（位置 {colon}）。");
        }
    }

    [Fact]
    public void Markdown_HasNoFakeItalicsOrEmphasisMarkers()
    {
        var markdown = ReportRender.Markdown(Report());
        Assert.DoesNotContain("**", markdown, StringComparison.Ordinal);
        foreach (var line in markdown.Split('\n'))
        {
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] != '*') continue;
                var before = i > 0 && CopyRules.IsChinese(line[i - 1]);
                var after = i + 1 < line.Length && CopyRules.IsChinese(line[i + 1]);
                Assert.False(before || after, $"报告里出现假斜体（* 包裹中文）：「{line}」");
            }
        }
    }

    [Fact]
    public void Markdown_UsesCornerBracketsForChineseQuotes()
    {
        var markdown = ReportRender.Markdown(Report());
        foreach (var quote in new[] { "“", "”", "‘", "’" })
        {
            Assert.DoesNotContain(quote, markdown, StringComparison.Ordinal);
        }
        // 半角引号紧挨中文同样不合规（技术记号里的引号不挨中文，故按「紧挨」判定）。
        foreach (var line in markdown.Split('\n'))
        {
            Assert.True(CopyRules.IndexOfHalfWidthMark(line, "\"") < 0, $"报告里半角引号紧挨中文：「{line}」");
        }
    }

    [Fact]
    public void Markdown_HasNoBannedWordsOrProgressNarration()
    {
        var markdown = ReportRender.Markdown(Report());
        foreach (var word in CopyRules.BannedWords.Concat(CopyRules.ProgressNarration))
        {
            Assert.DoesNotContain(word, markdown, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Handoff_UsesTheSameTypographyCaliber()
    {
        var handoff = ReportRender.Handoff(Report());
        foreach (var line in handoff.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            foreach (var mark in CopyRules.HalfWidthMarks)
            {
                Assert.True(CopyRules.IndexOfHalfWidthMark(line, mark) < 0, $"交接说明里半角「{mark}」紧挨中文：「{line}」");
            }
            Assert.True(CopyRules.IndexOfStrayColon(line) < 0, $"交接说明里半角冒号紧挨中文：「{line}」");
        }
        Assert.DoesNotContain("**", handoff, StringComparison.Ordinal);
        foreach (var word in CopyRules.BannedWords.Concat(CopyRules.ProgressNarration))
        {
            Assert.DoesNotContain(word, handoff, StringComparison.Ordinal);
        }
        // 交接说明的固定句同样受 45em 约束（数据位取最短值量）。
        var minimal = ReportRender.Handoff(MinimalReport());
        var tooWide = ProseLines(minimal).Where(line => WidthEm(line) > 45).ToList();
        Assert.True(tooWide.Count == 0, "交接说明里这些行超过 45em：" + string.Join("；", tooWide));
    }

    // ── HTML 正文：结构记号必须已经变成标签 ─────────────────────────

    private static string Body(string html)
    {
        var start = html.IndexOf("<body>", StringComparison.Ordinal);
        var end = html.IndexOf("</body>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "HTML 报告里找不到 body。");
        return html[(start + "<body>".Length)..end];
    }

    [Fact]
    public void Html_DoesNotLeakMarkdownMarkers()
    {
        var body = Body(ReportRender.Html(ReportRender.Markdown(Report())));
        Assert.DoesNotContain("|", body, StringComparison.Ordinal);
        Assert.DoesNotContain("&gt;", body, StringComparison.Ordinal);
        Assert.DoesNotContain("**", body, StringComparison.Ordinal);
        foreach (var marker in new[] { "<p>- ", "<p># ", "<p>> ", "<p>|" })
        {
            Assert.DoesNotContain(marker, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Html_RendersTablesAsTables()
    {
        var markdown = ReportRender.Markdown(Report());
        var body = Body(ReportRender.Html(markdown));
        var tableRows = markdown.Split('\n').Count(line => line.StartsWith('|'));
        var separators = markdown.Split('\n').Count(line => line.StartsWith("| ---", StringComparison.Ordinal));
        Assert.Single(Regex.Matches(body, "<table>"));
        Assert.Single(Regex.Matches(body, "</table>"));
        // 表头 + 四行数据；分隔行只表达「上一行是表头」，不输出成一行。
        Assert.Equal(tableRows - separators, Regex.Matches(body, "<tr>").Count);
        Assert.DoesNotContain("<td>---</td>", body, StringComparison.Ordinal);
        // 表头是 <th>（第一百四十二轮），数据行才是 <td>。
        Assert.Contains("<th>阶段</th>", body, StringComparison.Ordinal);
        Assert.DoesNotContain("<td>阶段</td>", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 表格语义（第一百四十二轮）：首行是表头，必须落在 <c>&lt;thead&gt;&lt;th&gt;</c> 里，
    /// 其余行落在 <c>&lt;tbody&gt;&lt;td&gt;</c> 里。此前整张表全是 <c>&lt;td&gt;</c>，
    /// 表头与数据没有区别——读屏软件读不出「这一列是什么」，浏览器也不会把它当表头处理。
    /// </summary>
    [Fact]
    public void Html_RendersTableHeaderAsTheadTh()
    {
        var markdown = ReportRender.Markdown(Report());
        var body = Body(ReportRender.Html(markdown));

        // Markdown 侧的表头就是第一行表格行（分隔行只表达「上一行是表头」）。
        var headerLine = markdown.Split('\n').First(line => line.StartsWith('|'));
        var headerCells = headerLine.Split('|').Select(cell => cell.Trim()).Where(cell => cell.Length > 0).ToList();
        Assert.Equal(3, headerCells.Count);

        Assert.Contains("<thead>", body, StringComparison.Ordinal);
        Assert.Contains("</thead>", body, StringComparison.Ordinal);
        Assert.Contains("<tbody>", body, StringComparison.Ordinal);
        Assert.Contains("</tbody>", body, StringComparison.Ordinal);
        Assert.True(
            body.IndexOf("<thead>", StringComparison.Ordinal) < body.IndexOf("<tbody>", StringComparison.Ordinal),
            "HTML 报告里 <tbody> 出现在 <thead> 之前。");

        // <th> 数量 == 表头列数，且逐格文字与 Markdown 源逐字一致。
        Assert.Equal(headerCells.Count, Regex.Matches(body, "<th>").Count);
        foreach (var cell in headerCells)
        {
            Assert.Contains($"<th>{cell}</th>", body, StringComparison.Ordinal);
        }

        // 数据行只出 <td>：行数 = 表格行数 − 分隔行，每行格数 = 表头列数。
        var tableRows = markdown.Split('\n').Count(line => line.StartsWith('|'));
        var separators = markdown.Split('\n').Count(line => line.StartsWith("| ---", StringComparison.Ordinal));
        var dataRows = Regex.Matches(body, "<tr>(.*?)</tr>").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(tableRows - separators, dataRows.Count);
        foreach (var row in dataRows.Skip(1))
        {
            Assert.Equal(headerCells.Count, Regex.Matches(row, "<td>").Count);
            Assert.DoesNotContain("<th>", row, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("<td>", dataRows[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// 单元格内的竖线（第一百四十二轮）：远端状态、说明与结构来源都可能带 <c>|</c>，
    /// 而 <c>|</c> 正是表格的列分隔符——不保护就会多切出一格，整行列数错位、后面的内容被当成新列。
    /// 保护方式：Markdown 侧转义成 <c>\|</c>（列数不变），HTML 侧还原成 <c>|</c>（用户看到原文）。
    /// </summary>
    [Fact]
    public void Html_KeepsPipesInsideCells()
    {
        var markdown = ReportRender.Markdown(Report(sourceStatus: "completed|partial"));
        Assert.Contains("completed\\|partial", markdown, StringComparison.Ordinal);

        var body = Body(ReportRender.Html(markdown));
        Assert.Contains("<td>completed|partial</td>", body, StringComparison.Ordinal);
        // 那一行仍是三格（表头列数），竖线没有把行切成四格。
        var row = Regex.Matches(body, "<tr>(.*?)</tr>").Select(m => m.Groups[1].Value)
            .Single(cells => cells.Contains("completed|partial", StringComparison.Ordinal));
        Assert.Equal(3, Regex.Matches(row, "<td>").Count);
    }

    [Fact]
    public void Html_GroupsListItemsUnderList()
    {
        var markdown = ReportRender.Markdown(Report());
        var body = Body(ReportRender.Html(markdown));
        var items = markdown.Split('\n').Count(line => line.StartsWith("- "));
        Assert.Equal(items, Regex.Matches(body, "<li>").Count);
        Assert.Equal(Regex.Matches(body, "<ul>").Count, Regex.Matches(body, "</ul>").Count);
        // 每个 <li> 都必须落在某个 <ul> 里（裸 <li> 不是合法 HTML，浏览器会自己猜结构）。
        var inList = false;
        foreach (var token in Regex.Matches(body, "<ul>|</ul>|<li>").Select(m => m.Value))
        {
            if (token == "<ul>") inList = true;
            else if (token == "</ul>") inList = false;
            else Assert.True(inList, "HTML 报告里出现不在 <ul> 里的 <li>。");
        }
    }

    [Fact]
    public void Html_RendersBlockquoteAndHeadings()
    {
        var markdown = ReportRender.Markdown(Report());
        var body = Body(ReportRender.Html(markdown));
        Assert.Contains("<blockquote>验证边界：只核对文档，未执行业务接口。</blockquote>", body, StringComparison.Ordinal);
        foreach (var (prefix, tag) in new[] { ("# ", "h1"), ("## ", "h2"), ("### ", "h3") })
        {
            var expected = markdown.Split('\n').Count(line => line.StartsWith(prefix, StringComparison.Ordinal)
                && !line.StartsWith(prefix + "#", StringComparison.Ordinal));
            Assert.Equal(expected, Regex.Matches(body, $"<{tag}>").Count);
        }
    }

    [Fact]
    public void Html_EscapesRemoteContent()
    {
        var body = Body(ReportRender.Html(ReportRender.Markdown(Report(changeKey: "<script>alert(1)</script>"))));
        Assert.DoesNotContain("<script>", body, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", body, StringComparison.Ordinal);
    }

    // ── 单一落点（渲染只有一份，Render 的字节就是这里测的字节）────────

    [Fact]
    public void ReportService_DelegatesRenderingToReportRender()
    {
        var source = Repo.Source("Reports", "ReportService.cs");
        Assert.Contains("public string Markdown(JsonObject report) => ReportRender.Markdown(report);", source);
        Assert.Contains("public string Handoff(JsonObject report) => ReportRender.Handoff(report);", source);
        Assert.Contains("public string Html(string markdown) => ReportRender.Html(markdown);", source);
        // 渲染只有一份：ReportService 里不得再出现第二份拼正文的代码。
        Assert.DoesNotContain("sb.AppendLine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WebUtility.HtmlEncode", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportRender_IsSharedCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Reports\ReportRender.cs", csproj);
        // 渲染口径不依赖 WinForms / GDI+（否则 Linux 侧共享编译不成立）。
        var render = Repo.Source("Reports", "ReportRender.cs");
        Assert.DoesNotContain("System.Windows.Forms", render, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Drawing", render, StringComparison.Ordinal);
    }

    // ── HTML 观感：内联样式（第一百四十三轮，具名 pass：polish + typeset）──────

    /// <summary>
    /// 字体栈有序回退（DESIGN §3）：Latin → 中文 → <c>sans-serif</c>。
    /// <para>
    /// 此前只有 <c>system-ui,'Microsoft YaHei',sans-serif</c>——中文只有一个候选，缺这个字体的机器
    /// （macOS、精简 Windows、Linux 浏览器）上中文落到浏览器默认衬线，报告的中文正文变成宋体观感。
    /// 断言的是**顺序**而不是「有没有某个名字」：顺序错了，回退就落在错的字体上。
    /// </para>
    /// </summary>
    [Fact]
    public void HtmlStyle_UsesOrderedFontStack()
    {
        var stack = FontStackOf(ReportRender.HtmlStyle);
        Assert.True(stack.Count >= 4, $"字体栈只有 {stack.Count} 项：{string.Join(" → ", stack)}");

        // Latin 在前：西文与数字先用 Latin 字体（中文候选里没有拉丁字形时才会被用到）。
        Assert.Equal("-apple-system", stack[0]);
        // 中文候选按「macOS → Windows → 开源 CJK」排列，且都在 sans-serif 之前。
        var chinese = new[] { "'PingFang SC'", "'Microsoft YaHei'", "'Noto Sans CJK SC'" };
        var positions = chinese.Select(name => stack.IndexOf(name)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.OrderBy(p => p), positions);
        Assert.True(positions[0] > 0, "中文候选出现在 Latin 候选之前。");

        // 收尾必须是通用无衬线：全不匹配时也不能落到衬线。
        Assert.Equal("sans-serif", stack[^1]);
        Assert.DoesNotContain("serif", stack.Take(stack.Count - 1));
        // 刻意排除衬线中文字体（与界面层 FontStack 同一口径）。
        foreach (var serif in new[] { "SimSun", "Songti", "NSimSun", "STSong" })
        {
            Assert.DoesNotContain(serif, ReportRender.HtmlStyle, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 正文行长（DESIGN §3：正文容器 ≤45em）。此前 <c>max-width:960px</c>——16px 字号下约 60 个中文字，
    /// 一行读下来眼睛要来回扫；报告是给人读的，行长是排版条款里最直接的一条。
    /// </summary>
    [Fact]
    public void HtmlStyle_ConstrainsProseWidth()
    {
        var maxWidth = Regex.Match(ReportRender.HtmlStyle, @"body\{[^}]*max-width:(\d+(?:\.\d+)?)em");
        Assert.True(maxWidth.Success, "body 规则里没有以 em 计的 max-width（px 不随字号缩放，量不出字数）。");
        var em = double.Parse(maxWidth.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(em <= 45, $"正文容器 {em}em 超过 DESIGN §3 的 45em 上限。");
        Assert.True(em >= 30, $"正文容器 {em}em 过窄（约 {em:0} 个中文字），表格会被挤成竖排。");
        // 居中：窄容器贴左边读起来像半张纸。
        Assert.Contains("margin:24px auto", ReportRender.HtmlStyle, StringComparison.Ordinal);
    }

    /// <summary>
    /// 表格观感：<c>th</c>/<c>td</c> 必须有边框与内边距，表头有底色。
    /// 此前表格一条样式都没有——三列文字之间只有空白，列与列的分界靠猜，表头与数据同色同重。
    /// </summary>
    [Fact]
    public void HtmlStyle_GivesTablesBordersAndPadding()
    {
        var style = ReportRender.HtmlStyle;
        Assert.Contains("table{border-collapse:collapse", style, StringComparison.Ordinal);

        var cell = Regex.Match(style, @"th,td\{([^}]*)\}");
        Assert.True(cell.Success, "样式里没有 th,td 规则（表格单元格没有边框与内边距）。");
        var rule = cell.Groups[1].Value;
        Assert.Contains("border:1px solid", rule, StringComparison.Ordinal);
        Assert.Contains("padding:", rule, StringComparison.Ordinal);
        Assert.Contains("text-align:left", rule, StringComparison.Ordinal);
        Assert.Contains("vertical-align:top", rule, StringComparison.Ordinal);

        // 表头与数据行要能区分：底色或字重至少一项（灰阶铬色，不引入第二个强调色）。
        var head = Regex.Match(style, @"th\{([^}]*)\}");
        Assert.True(head.Success, "样式里没有 th 规则（表头与数据行同色同重）。");
        Assert.True(head.Groups[1].Value.Contains("background:", StringComparison.Ordinal)
                    || head.Groups[1].Value.Contains("font-weight:", StringComparison.Ordinal),
            "表头既没有底色也没有字重差异。");
    }

    /// <summary>
    /// 清单项行高（DESIGN §3：中文行高 1.55）。此前 <c>li</c> 只有 <c>margin-left:18px</c>，
    /// 行高跟着浏览器默认（约 1.2）走——报告里最长的「未完成事项」与「限制」两段都是清单，
    /// 行距过紧时中文读起来是连成一片的。
    /// </summary>
    [Fact]
    public void HtmlStyle_GivesListItemsLineHeight()
    {
        var style = ReportRender.HtmlStyle;
        var body = Regex.Match(style, @"body\{([^}]*)\}");
        Assert.True(body.Success);
        Assert.Contains("line-height:1.55", body.Groups[1].Value, StringComparison.Ordinal);

        var item = Regex.Match(style, @"li\{([^}]*)\}");
        Assert.True(item.Success, "样式里没有 li 规则（清单项行高跟着浏览器默认走）。");
        var lineHeight = Regex.Match(item.Groups[1].Value, @"line-height:(\d+(?:\.\d+)?)");
        Assert.True(lineHeight.Success, "li 规则里没有 line-height。");
        var value = double.Parse(lineHeight.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(value >= 1.5 && value <= 1.6, $"清单项行高 {value} 不在 1.5–1.6 之间（DESIGN §3 取 1.55）。");
    }

    /// <summary>
    /// 行首行尾禁则在这一份交付面上的落地（DESIGN.md §3「中文排版附加条款」、§6「中文排版」第 ② 条）。
    /// <para>
    /// 为什么门在这里：§3 一直写着「行首行尾禁则」，而第一百六十八轮之前全仓没有任何东西执行它——
    /// 唯一会折行的产物就是这份 HTML（用户会转发它、会把它截成长图发群里），浏览器默认
    /// <c>line-break:auto</c> 允许把「。，、；」拆到行首、把「（「」拆到行尾。
    /// 原生界面的标签不折行，所以那里没有可判的折行点（§6 同一项写清了这一取舍）。
    /// </para>
    /// <para>
    /// <c>word-break</c> 也要钉：它是 <c>line-break</c> 的邻位开关，任何一处写成
    /// <c>break-all</c>/<c>break-word</c> 都会允许在词中间硬切，等于把 strict 声明悄悄作废——
    /// 而 <c>line-break:strict</c> 这个子串照样还在，只查它的门仍然绿。
    /// </para>
    /// </summary>
    [Fact]
    public void HtmlStyle_EnforcesTheChineseLineBreakProhibition()
    {
        var body = Regex.Match(ReportRender.HtmlStyle, @"body\{([^}]*)\}");
        Assert.True(body.Success, "样式里没有 body 规则。");
        Assert.Contains("line-break:strict", body.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("word-break:normal", body.Groups[1].Value, StringComparison.Ordinal);

        // 邻位开关不得被改成允许词中硬切（整份样式都查，不只 body 规则）。
        foreach (var breaker in new[] { "word-break:break-all", "word-break:break-word" })
        {
            Assert.DoesNotContain(breaker, ReportRender.HtmlStyle, StringComparison.Ordinal);
        }

        // 声明要真的进到用户拿到的那份文件里，不是只在常量里躺着。
        var html = ReportRender.Html(ReportRender.Markdown(Report()));
        Assert.Contains("line-break:strict;word-break:normal", html, StringComparison.Ordinal);

        // 条款与代码不能各说一套：§6「中文排版」的第 ② 项必须指着这条声明。
        var clause = Repo.DesignSection(6).Split('\n')
            .Single(l => l.StartsWith("| **中文排版**", StringComparison.Ordinal));
        var item2 = clause[clause.IndexOf("**② ", StringComparison.Ordinal)..];
        item2 = item2[..item2.IndexOf("**③ ", StringComparison.Ordinal)];
        Assert.Contains("line-break:strict", item2, StringComparison.Ordinal);
        Assert.Contains("行首", item2, StringComparison.Ordinal);
    }

    /// <summary>
    /// 中英文之间留空隙——<b>量的是渲染出来的字节</b>（DESIGN.md §6「中文排版」第 ⑤ 条）。
    /// <para>
    /// 界面层与服务层那两条静态门只能判作者打下的字：插值空洞里可能是中文，静态当拉丁会假红，
    /// 所以那边把空洞剥掉了。代价是「共 {count} 处」这类拼装后才紧贴的字面判不到。
    /// 报告这一面没有这个限制：渲染器是真执行的（<see cref="ReportRender"/> 共享编译），
    /// 数字 substitution 之后的那一行就在下面这些字符串里，紧贴与否当场判得出来。
    /// </para>
    /// </summary>
    [Fact]
    public void RenderedCopy_LeavesASpaceBetweenChineseAndLatin()
    {
        var markdown = ReportRender.Markdown(Report());
        var minimal = ReportRender.Markdown(MinimalReport());
        var handoff = ReportRender.Handoff(Report());
        var body = Body(ReportRender.Html(markdown));

        foreach (var (label, text) in new[] { ("Markdown", markdown), ("Markdown（数据位取最短）", minimal), ("交接说明", handoff) })
        {
            foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')))
            {
                var index = CopyRules.IndexOfMissingLatinGap(line);
                Assert.True(index < 0, $"{label} 这一行里的半角字母或数字紧挨汉字（位置 {index}）：「{line}」。");
            }
        }

        // HTML 只查正文：样式块里的 1px/40em 那些是 CSS 数值，不是中文句子。
        foreach (var segment in Regex.Split(body, "<[^>]*>"))
        {
            var index = CopyRules.IndexOfMissingLatinGap(segment);
            Assert.True(index < 0, $"HTML 正文这一段里的半角字母或数字紧挨汉字（位置 {index}）：「{segment}」。");
        }
    }

    /// <summary>
    /// 样式只有一份：<c>Html</c> 必须引用 <see cref="ReportRender.HtmlStyle"/>，
    /// 文件里不得再出现第二处 <c>font-family</c> 或 <c>&lt;style&gt;</c> 字面量。
    /// 否则排版断言量的是常量、用户看到的是另一份。
    /// </summary>
    [Fact]
    public void HtmlStyle_IsTheOnlyStyleSource()
    {
        var html = ReportRender.Html(ReportRender.Markdown(Report()));
        Assert.Contains("<style>" + ReportRender.HtmlStyle + "</style>", html, StringComparison.Ordinal);

        var render = Repo.Source("Reports", "ReportRender.cs");
        Assert.Single(Regex.Matches(render, "font-family"));
        Assert.Single(Regex.Matches(render, "<style>"));
        Assert.Contains("+ HtmlStyle +", render, StringComparison.Ordinal);
    }

    /// <summary>字体栈解析：<c>font-family:</c> 到 <c>}</c> 之间按逗号切分、去空白。</summary>
    private static List<string> FontStackOf(string style)
    {
        var match = Regex.Match(style, @"font-family:([^;]+);");
        Assert.True(match.Success, "样式里没有 font-family。");
        return match.Groups[1].Value.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
    }
}
