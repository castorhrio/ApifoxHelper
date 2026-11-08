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
}
