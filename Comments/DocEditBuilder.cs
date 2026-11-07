using ApifoxHelper.Source;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ApifoxHelper.Comments;

public sealed class PlannedEdit
{
    public string CandidateId { get; set; } = "";
    public string SymbolId { get; set; } = "";
    public string Display { get; set; } = "";
    public string Tag { get; set; } = "";
    public string? ParameterName { get; set; }

    /// <summary>insertBlock / insertElement / replaceElement / removeElement</summary>
    public string Kind { get; set; } = "";

    /// <summary>编辑起始行（0 起，基于原文件）。</summary>
    public int StartLine { get; set; }
    public int DeleteCount { get; set; }
    public List<string> NewLines { get; set; } = new();
    public string? OldText { get; set; }
    public string? NewText { get; set; }
    public bool Overwrite { get; set; }

    /// <summary>同一位置多个插入时的顺序。</summary>
    public double Rank { get; set; }
}

public sealed record DocEditOperation(string CandidateId, string Tag, string? ParameterName, string Text, bool Overwrite, string Action);

/// <summary>
/// 只对选中声明的 /// 文档注释做逐行插入、替换或删除；不触碰声明本身、特性和任何执行代码。
/// 现有注释格式复杂（同行多个标签、/** */、多段注释、非法 XML）时拒绝自动修改。
/// </summary>
public static class DocEditBuilder
{
    public static (List<PlannedEdit> Edits, List<(string CandidateId, string Reason)> Skipped) Build(
        LineModel lines,
        SyntaxNode declaration,
        IReadOnlyList<string> parameterOrder,
        IReadOnlyList<DocEditOperation> operations,
        string symbolId,
        string display)
    {
        var edits = new List<PlannedEdit>();
        var skipped = new List<(string, string)>();
        var doc = DocComments.Read(declaration);
        var firstToken = declaration.GetFirstToken();
        var tokenPosition = firstToken.GetLocation().GetLineSpan().StartLinePosition;
        var declarationLine = tokenPosition.Line;
        var lineText = lines.Lines[declarationLine];
        if (lineText[..Math.Min(tokenPosition.Character, lineText.Length)].Trim().Length > 0)
        {
            skipped.AddRange(operations.Select(o => (o.CandidateId, "声明前同一行还有其他内容，无法安全插入注释")));
            return (edits, skipped);
        }

        if (!doc.HasComment)
        {
            var indent = LineModel.LeadingWhitespace(lineText);
            var block = new List<(double Rank, DocEditOperation Op)>();
            foreach (var op in operations)
            {
                if (op.Action != "set")
                {
                    skipped.Add((op.CandidateId, "没有可删除的标签"));
                    continue;
                }
                block.Add((Rank(op.Tag, op.ParameterName, parameterOrder), op));
            }
            if (block.Count == 0) return (edits, skipped);
            var newLines = new List<string>();
            foreach (var (_, op) in block.OrderBy(b => b.Rank))
            {
                newLines.AddRange(ElementLines(indent, op.Tag, op.ParameterName, op.Text));
            }
            edits.Add(new PlannedEdit
            {
                CandidateId = string.Join(",", block.Select(b => b.Op.CandidateId)),
                SymbolId = symbolId,
                Display = display,
                Tag = string.Join(",", block.OrderBy(b => b.Rank).Select(b => b.Op.ParameterName is null ? b.Op.Tag : $"{b.Op.Tag}:{b.Op.ParameterName}")),
                Kind = "insertBlock",
                StartLine = declarationLine,
                DeleteCount = 0,
                NewLines = newLines,
                NewText = string.Join(" / ", block.OrderBy(b => b.Rank).Select(b => b.Op.Text)),
            });
            return (edits, skipped);
        }

        if (doc.MultiLineStyle || doc.MultipleBlocks || !doc.XmlValid)
        {
            var reason = !doc.XmlValid ? "现有注释不是合法 XML" : doc.MultiLineStyle ? "/** */ 风格或含非 /// 行的注释" : "存在多段文档注释";
            skipped.AddRange(operations.Select(o => (o.CandidateId, reason + "，需人工处理")));
            return (edits, skipped);
        }

        var docIndent = doc.Indent;
        foreach (var op in operations)
        {
            var element = doc.Find(op.Tag, op.ParameterName);
            if (op.Action == "removeStale")
            {
                if (element is null)
                {
                    skipped.Add((op.CandidateId, "要删除的标签已不存在"));
                    continue;
                }
                if (element.Complex)
                {
                    skipped.Add((op.CandidateId, "标签与其他内容同行，需人工删除"));
                    continue;
                }
                edits.Add(new PlannedEdit
                {
                    CandidateId = op.CandidateId, SymbolId = symbolId, Display = display, Tag = op.Tag, ParameterName = op.ParameterName,
                    Kind = "removeElement", StartLine = element.StartLine, DeleteCount = element.EndLine - element.StartLine + 1, OldText = element.Text,
                });
                continue;
            }

            if (element is not null)
            {
                if (!element.IsEmpty && !op.Overwrite)
                {
                    skipped.Add((op.CandidateId, "该标签已有有效注释，未勾选覆盖"));
                    continue;
                }
                if (element.Complex)
                {
                    skipped.Add((op.CandidateId, "现有标签与其他内容同行，需人工处理"));
                    continue;
                }
                edits.Add(new PlannedEdit
                {
                    CandidateId = op.CandidateId, SymbolId = symbolId, Display = display, Tag = op.Tag, ParameterName = op.ParameterName,
                    Kind = "replaceElement", StartLine = element.StartLine, DeleteCount = element.EndLine - element.StartLine + 1,
                    NewLines = ElementLines(docIndent, op.Tag, op.ParameterName, op.Text), OldText = element.Text, NewText = op.Text, Overwrite = !element.IsEmpty,
                });
                continue;
            }

            // 新增标签：插在所有“排序应在它之前”的现有标签之后；没有则放在注释块顶部。
            var rank = Rank(op.Tag, op.ParameterName, parameterOrder);
            var predecessors = doc.Elements.Where(e => Rank(e.Name, e.NameAttribute, parameterOrder) < rank).ToList();
            int insertAt;
            if (predecessors.Count > 0)
            {
                var anchor = predecessors.OrderBy(e => e.EndLine).Last();
                if (anchor.Complex)
                {
                    skipped.Add((op.CandidateId, "插入位置附近的标签格式复杂，需人工处理"));
                    continue;
                }
                insertAt = anchor.EndLine + 1;
            }
            else
            {
                var firstElement = doc.Elements.OrderBy(e => e.StartLine).FirstOrDefault();
                if (firstElement is { Complex: true } && firstElement.StartLine == doc.StartLine)
                {
                    skipped.Add((op.CandidateId, "注释块首行格式复杂，需人工处理"));
                    continue;
                }
                insertAt = doc.StartLine;
            }
            edits.Add(new PlannedEdit
            {
                CandidateId = op.CandidateId, SymbolId = symbolId, Display = display, Tag = op.Tag, ParameterName = op.ParameterName,
                Kind = "insertElement", StartLine = insertAt, DeleteCount = 0, NewLines = ElementLines(docIndent, op.Tag, op.ParameterName, op.Text), NewText = op.Text, Rank = rank,
            });
        }

        // 替换/删除范围不能重叠，插入点不能落在被替换范围内部。
        var ranges = edits.Where(e => e.DeleteCount > 0).OrderBy(e => e.StartLine).ToList();
        for (var i = 1; i < ranges.Count; i++)
        {
            if (ranges[i].StartLine < ranges[i - 1].StartLine + ranges[i - 1].DeleteCount)
            {
                skipped.Add((ranges[i].CandidateId, "与同一注释块中的其他修改重叠"));
                edits.Remove(ranges[i]);
            }
        }
        foreach (var insert in edits.Where(e => e.DeleteCount == 0).ToList())
        {
            if (ranges.Any(r => insert.StartLine > r.StartLine && insert.StartLine < r.StartLine + r.DeleteCount))
            {
                skipped.Add((insert.CandidateId, "插入位置落在其他修改范围内"));
                edits.Remove(insert);
            }
        }
        return (edits, skipped);
    }

    private static double Rank(string tag, string? parameterName, IReadOnlyList<string> parameterOrder) => tag switch
    {
        "summary" => 0,
        "typeparam" => 1,
        "param" => 2 + (parameterName is not null && parameterOrder.Contains(parameterName) ? (parameterOrder.ToList().IndexOf(parameterName) + 1) / 1000.0 : 0.999),
        "returns" => 3,
        "value" => 3.5,
        "remarks" => 4,
        _ => 5,
    };

    public static List<string> ElementLines(string indent, string tag, string? name, string text)
    {
        var escaped = EscapeXml(text.Trim()).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd()).ToList();
        var open = name is null ? $"<{tag}>" : $"<{tag} name=\"{name}\">";
        var close = $"</{tag}>";
        if (tag is not ("summary" or "remarks") && escaped.Count == 1 && escaped[0].Length <= 100)
        {
            return [$"{indent}/// {open}{escaped[0]}{close}"];
        }
        var result = new List<string> { $"{indent}/// {open}" };
        result.AddRange(escaped.Select(l => l.Length == 0 ? $"{indent}///" : $"{indent}/// {l}"));
        result.Add($"{indent}/// {close}");
        return result;
    }

    public static string EscapeXml(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>按行套用编辑，保留每一行原有的换行符；新行使用相邻行的换行符。</summary>
    public static string Apply(LineModel original, IEnumerable<PlannedEdit> edits)
    {
        var lines = new List<string>(original.Lines);
        var eols = new List<string>(original.Eols);
        foreach (var edit in edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.DeleteCount).ThenByDescending(e => e.Rank))
        {
            var eol = original.EolNear(Math.Max(0, edit.StartLine - 1));
            if (eol.Length == 0) eol = original.DominantEol;
            lines.RemoveRange(edit.StartLine, edit.DeleteCount);
            eols.RemoveRange(edit.StartLine, edit.DeleteCount);
            lines.InsertRange(edit.StartLine, edit.NewLines);
            eols.InsertRange(edit.StartLine, edit.NewLines.Select(_ => eol));
        }
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            sb.Append(lines[i]).Append(eols[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 修改前后的“代码签名”：所有 token 与除文档注释、空白、换行外的全部 trivia（含普通注释、预处理指令、被条件编译排除的代码）。
    /// 两者完全一致才证明本次只改了文档注释。
    /// </summary>
    public static string? VerifyOnlyDocCommentsChanged(string before, string after, CSharpParseOptions options)
    {
        var oldRoot = CSharpSyntaxTree.ParseText(before, options).GetRoot();
        var newRoot = CSharpSyntaxTree.ParseText(after, options).GetRoot();
        using var oldSignature = Signature(oldRoot).GetEnumerator();
        using var newSignature = Signature(newRoot).GetEnumerator();
        var index = 0;
        while (true)
        {
            var hasOld = oldSignature.MoveNext();
            var hasNew = newSignature.MoveNext();
            if (!hasOld && !hasNew) break;
            if (hasOld != hasNew || oldSignature.Current != newSignature.Current)
            {
                return $"校验失败：除文档注释外的内容发生了变化（第 {index} 个语法单元）";
            }
            index++;
        }
        var oldErrors = oldRoot.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
        var newErrors = newRoot.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
        if (newErrors > oldErrors) return "校验失败：修改后出现新的语法错误";
        return null;
    }

    private static IEnumerable<string> Signature(SyntaxNode root)
    {
        foreach (var token in root.DescendantTokens(descendIntoTrivia: false))
        {
            foreach (var trivia in token.LeadingTrivia)
            {
                if (Counts(trivia)) yield return "T:" + trivia.RawKind + ":" + trivia.ToFullString();
            }
            yield return "K:" + token.RawKind + ":" + token.Text;
            foreach (var trivia in token.TrailingTrivia)
            {
                if (Counts(trivia)) yield return "T:" + trivia.RawKind + ":" + trivia.ToFullString();
            }
        }
    }

    private static bool Counts(SyntaxTrivia trivia) =>
        !(trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
          || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
          || trivia.IsKind(SyntaxKind.WhitespaceTrivia)
          || trivia.IsKind(SyntaxKind.EndOfLineTrivia));

    /// <summary>
    /// 修改后再读一次目标声明的注释：XML 合法、param 名称都存在于签名、没有重复标签、写入的文字确实出现。
    /// </summary>
    public static string? VerifyDeclaration(string after, CSharpParseOptions options, int tokenIndex, IReadOnlyList<string> parameterOrder, IEnumerable<DocEditOperation> operations)
    {
        var root = CSharpSyntaxTree.ParseText(after, options).GetRoot();
        var token = root.DescendantTokens().Skip(tokenIndex).FirstOrDefault();
        var declaration = token.Parent?.AncestorsAndSelf().FirstOrDefault(n => n.GetFirstToken() == token && n is MemberDeclarationSyntax or EnumMemberDeclarationSyntax);
        if (declaration is null) return "校验失败：修改后找不到原声明";
        var doc = DocComments.Read(declaration);
        if (!doc.HasComment || !doc.XmlValid) return "校验失败：修改后的文档注释不是合法 XML";
        var paramNames = doc.Elements.Where(e => e.Name == "param").Select(e => e.NameAttribute ?? "").ToList();
        if (paramNames.Count != paramNames.Distinct(StringComparer.Ordinal).Count()) return "校验失败：存在重复的 param 标签";
        foreach (var op in operations)
        {
            if (op.Action == "removeStale")
            {
                if (doc.Find("param", op.ParameterName) is not null) return "校验失败：失效标签仍然存在";
                continue;
            }
            if (op.Tag == "param" && (op.ParameterName is null || !parameterOrder.Contains(op.ParameterName))) return $"校验失败：param 名称 {op.ParameterName} 不在签名中";
            var written = doc.Find(op.Tag, op.ParameterName);
            if (written is null || TextUtil.NormalizeSpace(written.Text) != TextUtil.NormalizeSpace(op.Text)) return $"校验失败：{op.Tag} 写入内容与确认内容不一致";
        }
        return null;
    }

    public static int TokenIndex(SyntaxNode declaration)
    {
        var first = declaration.GetFirstToken();
        var index = 0;
        foreach (var token in declaration.SyntaxTree.GetRoot().DescendantTokens())
        {
            if (token == first) return index;
            index++;
        }
        return -1;
    }
}
