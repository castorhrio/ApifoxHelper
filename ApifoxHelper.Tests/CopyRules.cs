namespace ApifoxHelper.Tests;

/// <summary>
/// 中文文案口径的唯一落点（DESIGN.md §3「中文排版附加条款」、设计验收 D10/D11）。
/// 界面层（<c>Desktop/Native</c>）与服务层用户可见文案（<c>UserException</c> 消息、<c>Describe()</c> 返回、
/// 被界面直接引用的文案常量）共用同一套判定——两处各写一份，口径分叉时就会出现
/// 「界面合规、错误路径不合规」的漏网，而错误路径上的字恰恰是用户最需要读懂的那一句。
/// </summary>
internal static class CopyRules
{
    /// <summary>中文文案里不得出现的半角标点（全角标点条款）。</summary>
    public static readonly string[] HalfWidthMarks = [",", ";", "?", "!", "."];

    /// <summary>
    /// 半角冒号只允许出现在技术记号里（「代码/路径/数字上下文」例外）：
    /// 「文件:行」是路径与行号的写法、「时:分」是时间格式，改成全角反而读错。
    /// 白名单是封闭清单——新增一条要先说明它为什么不是中文句子里的标点。
    /// </summary>
    public static readonly string[] TechnicalColonNotations = ["文件:行", "时:分"];

    /// <summary>空话禁词（方案 §6.2 #12：说结果不说机制）。</summary>
    public static readonly string[] BannedWords = ["赋能", "闭环", "一站式", "无缝", "智能", "极致"];

    /// <summary>工程进度自述（设计验收 D11：界面不向用户自述工程进度）。</summary>
    public static readonly string[] ProgressNarration = ["开发中", "迭代中", "本切片", "后续切片", "尚未接入", "待接入"];

    public static bool IsChinese(char c) => c is >= '\u4e00' and <= '\u9fff';

    public static bool HasChinese(string text) => text.Any(IsChinese);

    /// <summary>
    /// 半角标点紧挨中文的位置（-1 表示没有）。判定口径是「紧挨」而不是「出现」——
    /// 半角标点夹在代码、路径、数字之间（<c>app.js</c>、<c>1.5</c>、<c>GET /api/x</c>）不算违规，
    /// 那些不是中文句子里的标点。
    /// </summary>
    public static int IndexOfHalfWidthMark(string text, string mark)
    {
        for (var i = 0; i + mark.Length <= text.Length; i++)
        {
            if (string.CompareOrdinal(text, i, mark, 0, mark.Length) != 0) continue;
            var before = i > 0 && IsChinese(text[i - 1]);
            var after = i + mark.Length < text.Length && IsChinese(text[i + mark.Length]);
            if (before || after) return i;
        }
        return -1;
    }

    /// <summary>去掉技术记号之后的半角冒号位置（-1 表示没有）。</summary>
    public static int IndexOfStrayColon(string text)
    {
        var rest = text;
        foreach (var notation in TechnicalColonNotations)
        {
            rest = rest.Replace(notation, string.Empty, StringComparison.Ordinal);
        }
        return IndexOfHalfWidthMark(rest, ":");
    }
}
