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
    /// 计数行（第一百三十六轮收进共享口径）：按<b>全部</b>注释位置统计，不随筛选与排序变化——
    /// 它讲的是整份扫描，筛到零行时这几个数字仍然可信（与 <see cref="ListFilter.IsFilteredEmpty"/> 同一取舍）。
    /// 未知状态计入「其他」而不是丢掉：扫描新增状态值时，计数行不能悄悄少算一条。
    /// <para>
    /// 收进这里而不是留在对话框里：冒烟器（<c>ApifoxHelper.Smoke</c>）要在真实控件上断言
    /// 「计数行与扫描产物对得上」，若两边各拼一份，断言比的是第二份文字、不是屏幕上那一份。
    /// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
    /// </para>
    /// </summary>
    public static string CountsLine(IReadOnlyList<DocSlot> slots)
    {
        if (slots.Count == 0) return "没有注释位置";
        int valid = 0, missing = 0, empty = 0, stale = 0, other = 0;
        foreach (var slot in slots)
        {
            switch (slot.Status)
            {
                case "valid": valid++; break;
                case "missing": missing++; break;
                case "empty": empty++; break;
                case "stale": stale++; break;
                default: other++; break;
            }
        }
        return $"位置 {slots.Count} · 有效 {valid} · 缺失 {missing} · 空 {empty} · 失效 {stale} · 其他 {other}";
    }

    /// <summary>
    /// 关键词匹配这一行的哪几格：符号、标注、角色与文件位置——屏幕上被读作的那几格，不另造一套字段。
    /// 状态不在其中：那一列的取值由筛选下拉管（缺失/空/已失效/有效/其他问题），不必再敲一遍。
    /// </summary>
    public static string?[] KeywordFields(DocSlot slot) =>
        [Name(slot), Tag(slot), TaskDisplay.SlotRoleText(slot.Role), Location(slot)];
}
