using System.Text.RegularExpressions;

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

    /// <summary>
    /// 中英文空隙的例外（封闭清单）：整串按「先剥掉再判」处理，与 <see cref="TechnicalColonNotations"/>
    /// 同一手法。新增一条要先说明它为什么不是一个中文句子——标识符、代码片段与单位写法才例外，
    /// 「写不下」不是。
    /// <para>
    /// 第一百六十九轮建这条门时全仓命中 1 处（<c>ProfileAdvancedDialog</c> 的「路径前缀1」），
    /// 已改掉，所以清单是空的。空清单是有意的：留一条用不上的例外，等于把它变成免检通道。
    /// </para>
    /// </summary>
    public static readonly string[] TechnicalSpacingExceptions = [];

    /// <summary>参与「中英文之间留空隙」判定的字符：半角字母与数字（标点与符号不算，它们本来就自带空隙语义）。</summary>
    public static bool IsLatinSpacingMember(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    /// <summary>
    /// 半角字母或数字紧挨汉字、中间没有空隙的位置（-1 表示没有）。
    /// <para>
    /// <b>传进来的文本必须是 <see cref="LiteralText"/> 的产物</b>：插值空洞里可能是中文
    /// （「这个筛选下没有{word}。」），把空洞当成拉丁会把合规的句子判红，
    /// 而假红三次之后一条门就被整条注释掉了。代价是「共{count}处」这种拼装后才会露出的紧贴
    /// 判不到——那一份由渲染后的字节判（<c>ReportTypographyTests.RenderedCopy_LeavesASpaceBetweenChineseAndLatin</c>）。
    /// </para>
    /// </summary>
    public static int IndexOfMissingLatinGap(string text)
    {
        var rest = text;
        foreach (var exception in TechnicalSpacingExceptions)
        {
            rest = rest.Replace(exception, string.Empty, StringComparison.Ordinal);
        }

        for (var i = 0; i + 1 < rest.Length; i++)
        {
            var (a, b) = (rest[i], rest[i + 1]);
            if ((IsLatinSpacingMember(a) && IsChinese(b)) || (IsChinese(a) && IsLatinSpacingMember(b))) return i;
        }

        return -1;
    }

    /// <summary>
    /// 把源码里的一个字符串字面量还原成<b>用户会读到的那几个字</b>：剥掉插值空洞，再解码转义。
    /// <para>
    /// 两步都省不得。只剥空洞不解转义时，<c>"。\n\n退出时会请求停止"</c> 里源码那两个字母
    /// <c>n</c> 被当成文案里的拉丁字符，判出一处假红（第一百六十九轮实测抓到）；
    /// 只解转义不剥空洞时，<c>"…没有{word}。"</c> 里的表达式被当成拉丁，同样假红。
    /// 空洞里的表达式不参与判定：那里面写的是代码，不是交付给用户的字。
    /// </para>
    /// <para>解的是 <c>\n</c> <c>\r</c> <c>\t</c> <c>\&quot;</c> <c>\\</c> 五种（<c>\\</c> 先保护再解，
    /// 否则 <c>"\\\\n"</c> 会被读成换行）；产品文案里没有 <c>\uXXXX</c> 写法，不预备它。</para>
    /// </summary>
    public static string LiteralText(string literal)
    {
        // 转义大括号先占位，免得它们被当成空洞的边界。
        var rest = Regex.Replace(literal.Replace("{{", "\u0002").Replace("}}", "\u0003"), @"\{[^{}]*\}", string.Empty);
        rest = rest.Replace("\\\\", "\u0001")
            .Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\"", "\"")
            .Replace('\u0001', '\\')
            .Replace('\u0002', '{').Replace('\u0003', '}');
        return rest;
    }
}
