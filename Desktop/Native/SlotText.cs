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

    /// <summary>
    /// 详情区里那一行的前缀（全仓唯一落点：三屏不各拼一遍「标注」这两个字与它的冒号）。
    /// </summary>
    public const string TagDetailCaption = "标注：";

    /// <summary>
    /// 把「标注」那一格的<b>全文</b>交给详情区（第二百四十二轮）。那一格本身没有内容时返回空串，
    /// 由 <see cref="TagDetailLines"/> 决定「一行都不给」。
    /// <para>
    /// <b>存在理由</b>：那一格的宽度按内容族给到 <see cref="ListColumnWidths.SlotTag"/>（110px），
    /// 而带长参数名的标注在 wine 96 档量到 162px（「参数 cancellationToken」）——三屏哪一个数都装不下，
    /// 加宽到装得下又会把同一屏的列尾挤进横向滚动条。参数名正是「这条注释缺在哪个参数上」的答案，
    /// 而详情区此前只说符号、现状、覆盖接口与证据，不说它：清单那一格被裁、详情那一行不讲，
    /// 这一屏就没有第三条读全的路。§6「状态行」那条「放不下就给一个读得全的出口」在这里的落点是详情区。
    /// </para>
    /// <para>
    /// <b>文字由调用方给</b>：注释缺口取 <see cref="Tag"/>、注释候选取
    /// <c>QueueText.CandidateTag</c>、生成文档核对取 <c>PropagationEntries.TagText</c>——
    /// 每一屏那一格本来用的就是自己那一份取法，本方法只交出前缀，不另造第二份字段读法
    /// （否则清单与详情会对同一件事说两种话，与第七十四轮「下拉与状态列不能对同一件事说两种话」同一纪律）。
    /// </para>
    /// </summary>
    public static string TagDetailLine(string tagText) =>
        string.IsNullOrWhiteSpace(tagText) ? "" : TagDetailCaption + tagText;

    /// <summary>
    /// 详情区那几行里的「标注」一行：0 行或 1 行，调用方在自己的列表里原位展开。
    /// 那一格没有内容时<b>一行都不给</b>——写一句「标注：」后面跟着空，是把「这一条没有标注」
    /// 说成「读到了但读不出」，与第二百三十五轮「只有名称没有前缀的模块只给名称、不给空括号」同一纪律。
    /// </summary>
    public static IEnumerable<string> TagDetailLines(string tagText)
    {
        var line = TagDetailLine(tagText);
        if (line.Length > 0) yield return line;
    }

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
