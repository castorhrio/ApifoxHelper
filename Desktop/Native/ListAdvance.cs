namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 审阅队列的选中续接口径（DESIGN.md §6「列表行」）：处理完一条、该行不再属于当前筛选而从清单上消失时，
/// 选中落到<b>接替他位置的那一行</b>——原来显示在第 N 行，重建后第 N 行就是它的下一行；
/// 他本来就在末行时落到上一行；清单空了就没有落点。
/// 取 Outlook/资源管理器「删除或归档当前项后选中相邻下一项」的既有惯例，不回跳队首：
/// 在「待处理/待审阅」筛选下逐条清队列时，每次保存都把选中甩回第一行等于每清一条都要重新找位置，
/// 而选中一丢详情区与审阅按钮随之清空，只能回鼠标点下一行。
/// 只算落点，不碰数据：不改审阅状态、不写任务数据、不重建清单（重建仍由各视图自己的入口做）。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class ListAdvance
{
    /// <summary>没有落点：不选中任何行。</summary>
    public const int None = -1;

    /// <summary>「上一条」按钮的文案（DESIGN.md §6「注释缺口清单的上一条/下一条」，第一百一十四轮）。</summary>
    public const string PrevLabel = "上一条";

    /// <summary>「下一条」按钮的文案。</summary>
    public const string NextLabel = "下一条";

    /// <summary>
    /// 当前这一条不在可见清单里时位置指示的那句话——与冻结页面 <c>showSlot</c> 的「不在当前筛选中」逐字一致
    /// （那一屏的「上一条 / 下一条」此时两侧都按不动，这里同一口径）。
    /// </summary>
    public const string NotInFilterText = "不在当前筛选中";

    /// <summary>
    /// 由「被处理那一行在旧清单中的显示序号」与「重建后的可见行数」给出新落点的显示序号。
    /// 行数少于落点时取末行（多条一并消失时不越过边界）；<paramref name="handledIndex"/> 未知
    /// （旧清单上本来就没有选中，例如刚切换筛选）时从队列开头继续，不猜一个中间位置；
    /// 重建后一行不剩时返回 <see cref="None"/>。
    /// </summary>
    public static int IndexAfterRemoval(int handledIndex, int displayedCount)
    {
        if (displayedCount <= 0) return None;
        if (handledIndex < 0) return 0;
        return Math.Min(handledIndex, displayedCount - 1);
    }

    /// <summary>
    /// 「接受并下一条」的落点（DESIGN.md §6「候选文字编辑」的「接受并下一条」条款）：
    /// 接受一条候选之后，选中交给<b>下一条</b>——即使这一条仍在当前筛选里（例如停在「全部候选」上），
    /// 也不留在原处。冻结页面的候选卡片写的就是「接受并下一条」：逐条清队列时每接受一条都要再点一次下一行，
    /// 等于把「下一条」这一步从用户手里拿走；而「搁置 / 驳回 / 保存修改 / 撤销接受」仍留在原处
    /// （那几步的下一步是看这一条的结果，不是下一条）。
    /// <para>
    /// <paramref name="newIndex"/> 是这一条在重建后清单里的显示序号（还在当前筛选里时 ≥ 0）：
    /// 还在时落到它的下一行，它本来就在末行时留在末行（不借第一行、不越界）；
    /// 已不在当前筛选里（<see cref="None"/>）时落到接替他位置的那一行——与 <see cref="IndexAfterRemoval"/>
    /// 同一落点，因为那一行就是「下一条」。重建后一行不剩时返回 <see cref="None"/>。
    /// </para>
    /// 只算落点，不碰数据：不改审阅状态、不写任务数据、不重建清单。
    /// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
    /// </summary>
    public static int NextAfterAccept(int handledIndex, int newIndex, int displayedCount)
    {
        if (displayedCount <= 0) return None;
        if (newIndex >= 0) return Math.Min(newIndex + 1, displayedCount - 1);
        return Math.Min(handledIndex < 0 ? 0 : handledIndex, displayedCount - 1);
    }

    /// <summary>
    /// 逐条前进/后退的落点（DESIGN.md §6「注释缺口清单的上一条/下一条」，第一百一十四轮）：
    /// 冻结页面的注释位置详情有「‹ 上一条 / N / M / 下一条 ›」，原生此前只有清单——逐条处理
    /// （看代码证据、手工填写说明、删除失效标签）时不知道自己在第几条、还剩多少，鼠标用户要逐条走
    /// 只能回清单点行。这里只算落点：<paramref name="index"/> 是当前这一条在可见清单里的显示序号，
    /// <paramref name="delta"/> 是 ±1，<paramref name="count"/> 是可见行数。
    /// <para>
    /// 到头不绕回（第一条再往前、末条再往后都没有落点，与冻结页面两侧按钮在边界上按不动同一口径）；
    /// 没有当前行（<see cref="None"/>，或序号已越界）时也不猜第一行——那一步的下一步是先在清单上选一行，
    /// 与第五十一轮「焦点行缺失即不动作」、第五十三轮「宁可复制不到」同一取舍；
    /// <paramref name="delta"/> 为 0 不是一次前进/后退，同样没有落点。
    /// </para>
    /// 只算落点，不碰数据：不改选中、不滚动、不重建清单。
    /// </summary>
    public static int Step(int index, int delta, int count)
    {
        if (count <= 0 || delta == 0) return None;
        if (index < 0 || index >= count) return None;
        var target = index + delta;
        return target < 0 || target >= count ? None : target;
    }

    /// <summary>
    /// 位置指示（与冻结页面 <c>showSlot</c> 的「N / M」同一语义）：当前这一条在可见清单里是第几条、共几条。
    /// 补上中文单位（纯数字斜杠在中文界面里没有单位，与计数行「位置 42」同一词汇）；
    /// 当前这一条不在可见清单里（没有选中、或序号越界）时如实说「不在当前筛选中」；
    /// 一行都没有时留空——那一屏由提示出口说话，位置指示再讲「第 0 条」是把不存在的行说成还能走。
    /// </summary>
    public static string PositionText(int index, int count)
    {
        if (count <= 0) return "";
        if (index < 0 || index >= count) return NotInFilterText;
        return $"第 {index + 1} 条 / 共 {count} 条";
    }
}
