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
}
