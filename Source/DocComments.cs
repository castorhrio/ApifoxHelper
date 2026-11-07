using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ApifoxHelper.Source;

public sealed class DocElement
{
    public string Name { get; set; } = "";
    public string? NameAttribute { get; set; }
    public string? Cref { get; set; }
    public string Text { get; set; } = "";
    public bool HasChildElements { get; set; }

    /// <summary>文件中的行号（0 起）。</summary>
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    /// <summary>元素与其他内容同行或格式无法按行安全替换。</summary>
    public bool Complex { get; set; }

    public bool IsEmpty => DocComments.IsPlaceholder(Text) && !HasChildElements;
}

public sealed class DocInfo
{
    public bool HasComment { get; set; }
    public bool MultiLineStyle { get; set; }
    public bool MultipleBlocks { get; set; }
    public bool XmlValid { get; set; } = true;
    public string? XmlError { get; set; }

    /// <summary>注释块所在的首末行（0 起）；没有注释时为 -1。</summary>
    public int StartLine { get; set; } = -1;
    public int EndLine { get; set; } = -1;
    public string Indent { get; set; } = "";
    public List<DocElement> Elements { get; set; } = new();
    public bool HasInheritDoc => Elements.Any(e => e.Name == "inheritdoc");
    public bool HasInclude => Elements.Any(e => e.Name == "include");

    public DocElement? Find(string tag, string? name = null) =>
        Elements.FirstOrDefault(e => e.Name == tag && (name is null || e.NameAttribute == name));

    /// <summary>非空（非占位）文本；没有或为空时返回 null。</summary>
    public string? TextOf(string tag, string? name = null)
    {
        var element = Find(tag, name);
        return element is null || element.IsEmpty ? null : element.Text;
    }
}

/// <summary>
/// 从声明的前导 trivia 读取 /// 文档注释，记录每个顶层标签的行范围，供缺口检查和逐行安全编辑使用。
/// </summary>
public static class DocComments
{
    private static readonly Regex Placeholder = new(@"^(todo|tbd|待补充|待完善|暂无说明|\.\.\.|…|-)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsPlaceholder(string? text)
    {
        var normalized = TextUtil.NormalizeSpace(text);
        return normalized.Length == 0 || Placeholder.IsMatch(normalized);
    }

    public static DocInfo Read(SyntaxNode declaration)
    {
        var info = new DocInfo();
        var tree = declaration.SyntaxTree;
        var docTrivia = declaration.GetLeadingTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            .ToList();
        if (docTrivia.Count == 0) return info;

        info.HasComment = true;
        info.MultipleBlocks = docTrivia.Count > 1;
        if (docTrivia.Any(t => t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)))
        {
            info.MultiLineStyle = true;
        }

        var first = docTrivia[0];
        var last = docTrivia[^1];
        var startPos = tree.GetLineSpan(first.FullSpan).StartLinePosition;
        var endPos = tree.GetLineSpan(last.FullSpan).EndLinePosition;
        info.StartLine = startPos.Line;
        info.EndLine = endPos.Character == 0 && endPos.Line > startPos.Line ? endPos.Line - 1 : endPos.Line;

        var text = tree.GetText();
        var contentLines = new List<string>();
        for (var line = info.StartLine; line <= info.EndLine; line++)
        {
            var lineText = text.Lines[line].ToString();
            var trimmed = lineText.TrimStart();
            if (line == info.StartLine) info.Indent = lineText[..(lineText.Length - trimmed.Length)];
            if (!info.MultiLineStyle && trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                contentLines.Add(trimmed[3..]);
            }
            else
            {
                contentLines.Add(trimmed);
                if (!info.MultiLineStyle) info.MultiLineStyle = true;
            }
        }

        if (info.MultiLineStyle)
        {
            // /** */ 注释只做存在性判断，不按行编辑。
            var joined = string.Join("\n", contentLines);
            joined = joined.Replace("/**", "").Replace("*/", "");
            joined = Regex.Replace(joined, @"(?m)^\s*\*", "");
            ParseElements(joined, 0, info);
            foreach (var e in info.Elements) e.Complex = true;
            return info;
        }

        ParseElements(string.Join("\n", contentLines), info.StartLine, info);
        MarkComplexElements(info, contentLines);
        return info;
    }

    private static void ParseElements(string content, int baseLine, DocInfo info)
    {
        var xml = "<doc>\n" + content + "\n</doc>";
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreProcessingInstructions = true };
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            var lineInfo = (IXmlLineInfo)reader;
            DocElement? current = null;
            var textBuilder = new StringBuilder();
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1)
                {
                    current = new DocElement
                    {
                        Name = reader.LocalName,
                        NameAttribute = reader.GetAttribute("name"),
                        Cref = reader.GetAttribute("cref"),
                        StartLine = baseLine + lineInfo.LineNumber - 2,
                    };
                    textBuilder.Clear();
                    if (reader.IsEmptyElement)
                    {
                        current.EndLine = current.StartLine;
                        info.Elements.Add(current);
                        current = null;
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == 1 && current is not null)
                {
                    current.EndLine = baseLine + lineInfo.LineNumber - 2;
                    current.Text = TextUtil.NormalizeSpace(textBuilder.ToString());
                    info.Elements.Add(current);
                    current = null;
                }
                else if (current is not null)
                {
                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Text:
                        case XmlNodeType.CDATA:
                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            textBuilder.Append(reader.Value);
                            break;
                        case XmlNodeType.Element:
                            current.HasChildElements = true;
                            var reference = reader.GetAttribute("cref") ?? reader.GetAttribute("name") ?? reader.GetAttribute("langword") ?? reader.GetAttribute("href");
                            if (reference is not null)
                            {
                                var colon = reference.IndexOf(':');
                                textBuilder.Append(' ').Append(colon is 1 ? reference[2..] : reference).Append(' ');
                            }
                            break;
                    }
                }
            }
        }
        catch (XmlException ex)
        {
            info.XmlValid = false;
            info.XmlError = ex.Message;
        }
    }

    /// <summary>
    /// 只有“独占整行起止”的元素才能按行替换；与其他元素同行、行首或行尾有额外内容时标记为复杂。
    /// </summary>
    private static void MarkComplexElements(DocInfo info, List<string> contentLines)
    {
        for (var i = 0; i < info.Elements.Count; i++)
        {
            var e = info.Elements[i];
            var startText = contentLines[e.StartLine - info.StartLine].Trim();
            var endText = contentLines[e.EndLine - info.StartLine].Trim();
            if (!startText.StartsWith("<" + e.Name, StringComparison.Ordinal)) e.Complex = true;
            if (!(endText.EndsWith("</" + e.Name + ">", StringComparison.Ordinal) || endText.EndsWith("/>", StringComparison.Ordinal))) e.Complex = true;
            if (i > 0 && info.Elements[i - 1].EndLine >= e.StartLine)
            {
                e.Complex = true;
                info.Elements[i - 1].Complex = true;
            }
        }
    }
}
