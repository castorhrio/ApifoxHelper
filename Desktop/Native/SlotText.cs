using ApifoxHelper.Source;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 注释位置这一行在屏幕上被读作的那几格：清单的行、详情区的标题与关键词匹配都取这里。
/// 拼一次：同一段文字在两处各拼一次，必然有一处先漂（键与名字各改一次，两屏就说出两个名字）。
/// </summary>
internal static class SlotText
{
    /// <summary>行首那一格（符号名）：符号显示名为空时退回符号 Id。</summary>
    public static string Name(DocSlot slot) => string.IsNullOrWhiteSpace(slot.Display) ? slot.SymbolId : slot.Display;

    /// <summary>「标注」列：标注种类；参数另附参数名。</summary>
    public static string Tag(DocSlot slot) =>
        TaskDisplay.SlotTagText(slot.Tag) + (slot.Tag == "param" && slot.ParameterName is { } name ? " " + name : "");

    /// <summary>「文件:行」列。</summary>
    public static string Location(DocSlot slot) => slot.File + ":" + slot.Line;

    /// <summary>
    /// 关键词匹配这一行的哪几格：符号、标注、角色与文件位置——屏幕上被读作的那几格，不另造一套字段。
    /// 状态不在其中：那一列的取值由筛选下拉管（缺失/空/已失效/有效/其他问题），不必再敲一遍。
    /// </summary>
    public static string?[] KeywordFields(DocSlot slot) =>
        [Name(slot), Tag(slot), TaskDisplay.SlotRoleText(slot.Role), Location(slot)];
}
