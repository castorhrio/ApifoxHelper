using System.Net;

namespace ApifoxHelper.Reports;

/// <summary>
/// 报告正文渲染的单一口径：Markdown 源文、前端交接说明、HTML 正文。
/// <para>
/// 存在理由（第一百四十一轮）：这三段渲染此前是 <see cref="ReportService"/> 的实例方法，而
/// <see cref="ReportService"/> 依赖 <c>TaskStore</c> 与 <c>CommentPlan</c> / <c>PropagationResult</c>
/// 等未共享编译的类型——于是「报告里的中文排版」在 Linux 侧一条断言都没有：报告是用户会原样
/// 发给前端与测试的那份东西，它的全角标点、假斜体、Markdown 记号泄漏只能靠人眼看。
/// 渲染本身只用到 <c>JsonObject</c> 与 <c>StringBuilder</c>（纯 BCL），故原样提取到这里，
/// 由 <see cref="ReportService"/> 一行委托——字节口径不变，测试工程可共享编译后真执行断言。
/// </para>
/// <para>
/// 分档口径：报告是 Markdown 文档，<c>#</c> / <c>-</c> / <c>|</c> / <c>&gt;</c> 是它的结构记号（合法）；
/// 界面文案不是（<c>**</c> 强调记号在两边都禁）。HTML 那一侧则相反：结构记号必须已经变成标签，
/// 正文里再出现 <c>|</c>、<c>&gt; </c>、<c>- </c> 就是泄漏。
/// </para>
/// </summary>
public static class ReportRender
{
    /// <summary>
    /// 「检查覆盖范围与限制」的四句固定说明：报告里最长的产品自述文字，也是唯一一处
    /// 不随任务数据变化的正文段落。
    /// <para>
    /// 单一落点（第一百四十二轮）：这四句此前写在 <see cref="ReportService.Build"/> 里，
    /// 排版检查只能从源码里用正则抠字符串字面量——抠不到就静默通过（「提取器失效」是隐性风险，
    /// 而它失效时检查看起来仍然是绿的）。现在 <see cref="ReportService.Build"/> 直接引用这里，
    /// 排版检查也直接量这里，两边量的是同一份文字。
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> Limits =
    [
        "分析不运行 MSBuild：条件属性、Import、源生成器未评估，相关缺口见扫描警告。",
        "动态派发、多实现 DI、反射与外部程序集中的逻辑未展开，相关结论需人工确认。",
        "用例脚本等任意文本中的模型依赖未分析，需人工复核。",
        "本地锁不能阻止其他用户同时修改 Apifox；执行前指纹核对后仍存在竞态窗口。",
    ];

    /// <summary>报告 Markdown 源文（与 Web 下载、桌面「另存为」同一份字节）。</summary>
    public static string Markdown(JsonObject report)
    {
        var sb = new StringBuilder();
        var task = report["task"]!;
        var target = report["target"]!;
        var stages = report["stages"]!;
        sb.AppendLine($"# {task["name"]} — Apifox 助手报告").AppendLine();
        sb.AppendLine($"- 目标：{target["projectName"] ?? "（未命名项目）"} / 项目 ID {target["projectId"]} / 分支 {target["branch"]}");
        sb.AppendLine($"- 来源：{report["source"]!["apiProject"]}，分析等级 {report["source"]!["analysisLevel"]}，Swagger {report["source"]!["swagger"]}");
        sb.AppendLine($"- 版本备注：{task["sourceVersionNote"]}；工具 {task["toolVersion"]}；CLI {task["cliVersion"] ?? "未使用"}").AppendLine();
        sb.AppendLine("## 分阶段结果").AppendLine();
        sb.AppendLine(TableRow("阶段", "状态", "说明")).AppendLine(TableRow("---", "---", "---"));
        var coverage = stages["sourceComments"]!["coverage"];
        sb.AppendLine(TableRow("源码注释完整性", stages["sourceComments"]!["status"], $"有效 {coverage?["valid"]}+继承 {coverage?["inheritDoc"]} / {coverage?["total"]}，未解析符号 {stages["sourceComments"]!["unresolvedSymbols"]}"));
        sb.AppendLine(TableRow("XML/Swagger 传播", stages["generatedDocs"]!["status"], $"源码 {stages["generatedDocs"]!["inSource"]}、XML {stages["generatedDocs"]!["inXml"]}、Swagger {stages["generatedDocs"]!["inSwagger"]} / {stages["generatedDocs"]!["entries"]}"));
        sb.AppendLine(TableRow("Apifox 一致性", stages["apifox"]!["status"], $"结构来源 {stages["apifox"]!["structureSource"] ?? "未检查"}"));
        sb.AppendLine(TableRow("业务运行验证", "未执行", stages["businessRuntime"]!["message"])).AppendLine();

        if (stages["apifox"]!["plan"] is JsonObject plan)
        {
            sb.AppendLine("## 实际写入").AppendLine();
            sb.AppendLine($"计划状态：{plan["status"]}").AppendLine();
            foreach (var action in plan["actions"] as JsonArray ?? new JsonArray())
            {
                sb.AppendLine($"- {action!["kind"]} {action["resourceKey"]}（ID {action["remoteId"] ?? action["createdId"]}）：{action["state"]} {action["message"]}");
            }
            if (plan["outOfScopeImpact"] is JsonArray { Count: > 0 } impact)
            {
                sb.AppendLine().AppendLine("### 共享模型对范围外接口的影响").AppendLine();
                foreach (var entry in impact) sb.AppendLine($"- {entry!["key"]}（{entry["name"]}，ID {entry["id"]}）");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## 接口变化").AppendLine();
        foreach (var change in report["changes"] as JsonArray ?? new JsonArray())
        {
            sb.AppendLine($"### {change!["key"]}（{change["status"]}）").AppendLine();
            foreach (var item in change["incompatible"] as JsonArray ?? new JsonArray()) sb.AppendLine($"- ⚠ {item}");
            foreach (var item in change["structure"] as JsonArray ?? new JsonArray()) sb.AppendLine($"- {item}");
            foreach (var item in change["docs"] as JsonArray ?? new JsonArray()) sb.AppendLine($"- 说明：{item}");
            sb.AppendLine();
        }
        sb.AppendLine("## 未完成事项").AppendLine();
        var unfinished = report["unfinished"] as JsonArray ?? new JsonArray();
        if (unfinished.Count == 0) sb.AppendLine("无。");
        foreach (var item in unfinished) sb.AppendLine($"- {item}");
        sb.AppendLine().AppendLine("## 检查覆盖范围与限制").AppendLine();
        foreach (var item in report["limits"] as JsonArray ?? new JsonArray()) sb.AppendLine($"- {item}");
        sb.AppendLine().AppendLine("> 验证边界：只核对文档，未执行业务接口。");
        return sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
    }

    /// <summary>前端交接说明：只列出前端关心的接口与字段变化。</summary>
    public static string Handoff(JsonObject report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 接口变更交接：{report["task"]!["name"]}").AppendLine();
        sb.AppendLine($"目标文档：{report["target"]!["projectName"]} / 分支 {report["target"]!["branch"]}；同步状态：{report["stages"]!["apifox"]!["status"]}").AppendLine();
        foreach (var change in report["changes"] as JsonArray ?? new JsonArray())
        {
            sb.AppendLine($"## {change!["key"]}").AppendLine();
            var incompatible = change["incompatible"] as JsonArray ?? new JsonArray();
            if (incompatible.Count > 0)
            {
                sb.AppendLine("需要前端注意：");
                foreach (var item in incompatible) sb.AppendLine($"- {item}");
            }
            foreach (var item in (change["structure"] as JsonArray ?? new JsonArray()).Take(20)) sb.AppendLine($"- {item}");
            foreach (var item in change["docs"] as JsonArray ?? new JsonArray()) sb.AppendLine($"- 说明：{item}");
            sb.AppendLine();
        }
        sb.AppendLine("> 说明：本交接只代表文档变化，接口行为未经实际调用验证。");
        return sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
    }

    /// <summary>
    /// Markdown → HTML 正文。结构记号在这里变成标签：标题、清单项（成组包 <c>&lt;ul&gt;</c>）、
    /// 表格（成组包 <c>&lt;table&gt;</c>，首行是 <c>&lt;thead&gt;&lt;th&gt;</c>、其余行是
    /// <c>&lt;tbody&gt;&lt;td&gt;</c>，分隔行 <c>| --- |</c> 不输出）、引用（<c>&gt; </c> → <c>&lt;blockquote&gt;</c>）。
    /// <para>
    /// 第一百四十一轮修掉的真实缺陷：此前只认标题与 <c>- </c> 两种记号，报告里的表格与引用原样落进
    /// <c>&lt;p&gt;</c>——HTML 报告正文里出现 <c>| 阶段 | 状态 | 说明 |</c> 与 <c>&amp;gt; 验证边界…</c>，
    /// 用户看到的是 Markdown 记号而不是表格与引用；<c>&lt;li&gt;</c> 也没有 <c>&lt;ul&gt;</c> 包裹。
    /// 第一百四十二轮补齐表格语义：整张表此前全是 <c>&lt;td&gt;</c>，表头与数据没有区别，
    /// 读屏软件与浏览器都无法把它当表头处理。
    /// </para>
    /// <para>
    /// 远端内容仍逐格 <c>HtmlEncode</c>（不执行脚本、不解释远端带来的记号）；单元格内的 <c>|</c>
    /// 在 Markdown 侧转义成 <c>\|</c>、在这里还原成 <c>|</c>，列数不因数据内容而变。
    /// </para>
    /// </summary>
    public static string Html(string markdown)
    {
        var body = new StringBuilder();
        var inList = false;
        var inTable = false;
        var tableHeadDone = false;

        void CloseList()
        {
            if (!inList) return;
            body.Append("</ul>\n");
            inList = false;
        }

        void CloseTable()
        {
            if (!inTable) return;
            body.Append(tableHeadDone ? "</tbody>\n</table>\n" : "</thead>\n</table>\n");
            inTable = false;
            tableHeadDone = false;
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var text = WebUtility.HtmlEncode(line);
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                CloseList();
                CloseTable();
                body.Append("<h1>").Append(WebUtility.HtmlEncode(line[2..])).Append("</h1>");
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                CloseList();
                CloseTable();
                body.Append("<h2>").Append(WebUtility.HtmlEncode(line[3..])).Append("</h2>");
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                CloseList();
                CloseTable();
                body.Append("<h3>").Append(WebUtility.HtmlEncode(line[4..])).Append("</h3>");
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                CloseTable();
                if (!inList)
                {
                    body.Append("<ul>\n");
                    inList = true;
                }
                body.Append("<li>").Append(WebUtility.HtmlEncode(line[2..])).Append("</li>\n");
                continue;
            }
            else if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                CloseList();
                CloseTable();
                body.Append("<blockquote>").Append(WebUtility.HtmlEncode(line[2..])).Append("</blockquote>");
            }
            else if (IsTableRow(line))
            {
                CloseList();
                if (IsTableSeparator(line))
                {
                    // 分隔行只表达「上一行是表头」，不输出内容。
                    continue;
                }
                if (!inTable)
                {
                    body.Append("<table>\n<thead>\n");
                    inTable = true;
                }
                var cells = TableCells(line);
                if (!tableHeadDone)
                {
                    // 表格首行是表头：<th> 进 <thead>，其余行进 <tbody> 的 <td>。
                    body.Append("<tr>");
                    foreach (var cell in cells) body.Append("<th>").Append(WebUtility.HtmlEncode(cell)).Append("</th>");
                    body.Append("</tr>\n</thead>\n<tbody>\n");
                    tableHeadDone = true;
                }
                else
                {
                    body.Append("<tr>");
                    foreach (var cell in cells) body.Append("<td>").Append(WebUtility.HtmlEncode(cell)).Append("</td>");
                    body.Append("</tr>\n");
                }
                continue;
            }
            else if (line.Trim().Length == 0)
            {
                CloseList();
                CloseTable();
                body.Append("<br>");
            }
            else
            {
                CloseList();
                CloseTable();
                body.Append("<p>").Append(text).Append("</p>");
            }
            body.Append('\n');
        }
        CloseList();
        CloseTable();
        return "<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">"
            + "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">"
            + "<title>Apifox 助手报告</title><style>body{font-family:system-ui,'Microsoft YaHei',sans-serif;max-width:960px;margin:24px auto;line-height:1.6;color:#1f2937}h1{font-size:22px}h2{font-size:18px;border-bottom:1px solid #e5e7eb}li{margin-left:18px}</style>"
            + "</head><body>" + body + "</body></html>";
    }

    /// <summary>
    /// 一行表格：<c>| 甲 | 乙 | 丙 |</c>。单元格文字先经 <see cref="EscapeCell"/> 处理——
    /// 数据里带 <c>|</c> 或换行时列数不变、行不被截断（远端说明与状态文字都可能带这些字符）。
    /// </summary>
    private static string TableRow(params object?[] cells) =>
        "| " + string.Join(" | ", cells.Select(cell => EscapeCell(cell?.ToString() ?? ""))) + " |";

    /// <summary>单元格转义：<c>|</c> → <c>\|</c>（Markdown 的转义写法），换行折成空格。</summary>
    private static string EscapeCell(string text) =>
        text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace("|", "\\|");

    /// <summary>表格行：以 <c>|</c> 开头（报告里的表格都由渲染器自己写，远端内容只进单元格）。</summary>
    private static bool IsTableRow(string line) => line.StartsWith('|');

    /// <summary>表格分隔行：<c>| --- | --- |</c>，每格只由 <c>-</c> 与空白组成。</summary>
    private static bool IsTableSeparator(string line)
    {
        var cells = TableCells(line);
        return cells.Count > 0 && cells.All(cell => cell.Length > 0 && cell.All(c => c is '-' or ' '));
    }

    /// <summary>
    /// 拆表格单元格：按未转义的 <c>|</c> 切分（<c>\|</c> 是单元格内的竖线，不切），
    /// 去掉首尾两个空段（行首行尾的 <c>|</c>），逐格去空白。
    /// </summary>
    private static List<string> TableCells(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\' && i + 1 < line.Length && line[i + 1] == '|')
            {
                cell.Append('|');
                i++;
                continue;
            }
            if (c == '|')
            {
                cells.Add(cell.ToString());
                cell.Clear();
                continue;
            }
            cell.Append(c);
        }
        cells.Add(cell.ToString());
        if (cells.Count > 0 && cells[0].Trim().Length == 0) cells.RemoveAt(0);
        if (cells.Count > 0 && cells[^1].Trim().Length == 0) cells.RemoveAt(cells.Count - 1);
        return cells.Select(part => part.Trim()).ToList();
    }
}
