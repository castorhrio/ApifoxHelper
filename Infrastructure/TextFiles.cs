namespace ApifoxHelper.Infrastructure;

public sealed class SourceFileText
{
    public required string FullPath { get; init; }
    public required byte[] Bytes { get; init; }
    public required string Text { get; init; }
    public bool HasBom { get; init; }
    public bool IsUtf8 { get; init; }
    public required string Hash { get; init; }
}

public static class TextFiles
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// 读取源码文件。只认可 UTF-8（可带 BOM）；其他编码按有损方式读取供分析，但标记为不可回写，
    /// 不做 GBK 兜底后静默继续。
    /// </summary>
    public static SourceFileText Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var hash = Canonical.Sha256Hex(bytes);
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            var encoding = bytes[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode;
            return new SourceFileText { FullPath = path, Bytes = bytes, Text = encoding.GetString(bytes, 2, bytes.Length - 2), HasBom = true, IsUtf8 = false, Hash = hash };
        }

        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var offset = hasBom ? 3 : 0;
        try
        {
            var text = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
            return new SourceFileText { FullPath = path, Bytes = bytes, Text = text, HasBom = hasBom, IsUtf8 = true, Hash = hash };
        }
        catch (DecoderFallbackException)
        {
            var text = Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
            return new SourceFileText { FullPath = path, Bytes = bytes, Text = text, HasBom = hasBom, IsUtf8 = false, Hash = hash };
        }
    }

    public static byte[] EncodeUtf8(string text, bool withBom)
    {
        var body = Utf8NoBom.GetBytes(text);
        if (!withBom) return body;
        var result = new byte[body.Length + 3];
        result[0] = 0xEF;
        result[1] = 0xBB;
        result[2] = 0xBF;
        Buffer.BlockCopy(body, 0, result, 3, body.Length);
        return result;
    }

    public static string HashFile(string path) => Canonical.Sha256Hex(File.ReadAllBytes(path));
}

/// <summary>
/// 逐行保存原文与各自的换行符，拼回时逐字节一致；用于只改注释行而不触碰其他内容。
/// </summary>
public sealed class LineModel
{
    private LineModel(List<string> lines, List<string> eols)
    {
        Lines = lines;
        Eols = eols;
    }

    public List<string> Lines { get; }

    public List<string> Eols { get; }

    public static LineModel Parse(string text)
    {
        var lines = new List<string>();
        var eols = new List<string>();
        var start = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\r' || c == '\n')
            {
                lines.Add(text[start..i]);
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    eols.Add("\r\n");
                    i += 2;
                }
                else
                {
                    eols.Add(c.ToString());
                    i += 1;
                }
                start = i;
                continue;
            }
            i++;
        }
        lines.Add(text[start..]);
        eols.Add("");
        return new LineModel(lines, eols);
    }

    public string DominantEol
    {
        get
        {
            var crlf = Eols.Count(e => e == "\r\n");
            var lf = Eols.Count(e => e == "\n");
            if (crlf == 0 && lf == 0) return "\r\n";
            return crlf >= lf ? "\r\n" : "\n";
        }
    }

    public string EolNear(int lineIndex)
    {
        for (var i = Math.Min(lineIndex, Eols.Count - 1); i >= 0; i--)
        {
            if (Eols[i].Length > 0) return Eols[i];
        }
        return DominantEol;
    }

    public string Join()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < Lines.Count; i++)
        {
            sb.Append(Lines[i]);
            sb.Append(Eols[i]);
        }
        return sb.ToString();
    }

    public static string LeadingWhitespace(string line)
    {
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return line[..i];
    }
}
