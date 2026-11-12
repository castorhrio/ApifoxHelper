namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 关键词框的三种手势各自做什么（DESIGN.md §6「列表筛选」与「菜单/快捷键」）：
/// <see cref="FocusKeyword"/> 是 Ctrl+F（焦点交给框并选中已有关键词）、
/// <see cref="FocusFirstResult"/> 是框里的裸 Enter（焦点与选中交给筛选结果第一行）、
/// <see cref="ClearKeyword"/> 是框里的 Esc（清空关键词回到全部行）。
/// </summary>
public enum FilterGestureAction
{
    /// <summary>不是本屏接的手势（交回基类：对话框的 Enter=确认、Esc=取消逐字不变）。</summary>
    None,

    /// <summary>Ctrl+F：焦点交给关键词框并选中已有关键词。只移焦点，不改筛选结果、不重建清单、不写数据。</summary>
    FocusKeyword,

    /// <summary>框里的裸 Enter：焦点与选中交给筛选结果第一行（显示序第一行，零行不动作）。</summary>
    FocusFirstResult,

    /// <summary>框里的 Esc：清空关键词回到全部行（重建仍由各视图既有的 TextChanged 触发）。</summary>
    ClearKeyword,
}

/// <summary>
/// 一个关键词框在这一刻的三个事实（不依赖 WinForms，与测试工程单一编译）：
/// 是否可用（停用的框不接 Ctrl+F——焦点交不过去，吞掉按键等于把「什么都没发生」说成做了什么）、
/// 是否持有焦点（Enter 与 Esc 看焦点，一次按键只有一个含义）、框里有没有关键词（空框里按 Esc 不接这一次按键）。
/// </summary>
public readonly record struct FilterBoxState(bool Enabled, bool Focused, bool HasKeyword);

/// <summary>判定结果：落在第几个框（按登记顺序，0 是本屏主框）、做什么。</summary>
public readonly record struct FilterGestureStep(int BoxIndex, FilterGestureAction Action);

/// <summary>
/// 一屏多个关键词框时「一次按键只有一个落点」的唯一判定（CRITIQUE 第 9 站，第二百二十六轮）：
/// 此前这条判定在三个双框对话框里各写一次（<c>SyncPreviewDialog</c> 两页各一个框、
/// <c>ApplyPlanDialog</c> 文件清单与跳过清单、<c>CheckResultsDialog</c> 接口清单与模型清单），
/// 三处都各自用「当前可见页 / 焦点在哪个框」挑一个框，规则相同、落点在代码里却是三份——
/// 第四个双框视图出现时会写出第四种。现在规则只在这里一份，界面只登记「本屏有哪几个框、哪个是主框」。
/// <para>
/// <b>三条规则（与收口前的三份实现逐条等价，不是重新设计）</b>：
/// ① Ctrl+F 只交给<b>主框</b>（登记顺序第 0 个），主框停用则这一次按键不接（不落到第二个框上——
/// 查找手势落在这一屏的主清单上，第二个框经 Tab 或点击进入，不为它发明第二个查找手势）；
/// ② Enter 交给<b>持有焦点</b>的那个框（焦点只有一个，故与登记顺序无关）；
/// ③ Esc 交给<b>持有焦点且框里有字</b>的那个框（空框里按 Esc 仍是取消对话框）。
/// </para>
/// <para>
/// <b>本文件只判定，不执行</b>：焦点、选中、清空由 <c>FilterGestures</c> 按判定结果落到真实控件上，
/// 目标行与选中仍走 <c>Ui.FocusFirstResult</c> 一处。也不判「这一屏该不该有第二个框」——
/// 那是各视图自己的事，本文件只回答「这一次按键落在哪一个框」。
/// </para>
/// </summary>
public static class FilterGesturePlan
{
    /// <summary>
    /// 判定这一次按键落在哪一个框、做什么；不是本屏接的手势返回 <c>null</c>（调用方交回基类）。
    /// 三个入参由调用方按既有单一口径算出（<c>ShellShortcuts.IsFindGesture</c>、<c>ListKeys.IsEnterGesture</c>、
    /// <c>ListFilter.EscClearsInDialog</c> 的同一批事实），本文件不认键、不拆修饰键。
    /// 三个手势同时成立时按 Ctrl+F → Enter → Esc 的顺序取第一个（与收口前的调用顺序一致）。
    /// </summary>
    public static FilterGestureStep? Resolve(bool find, bool enter, bool escape, IReadOnlyList<FilterBoxState> boxes)
    {
        if (boxes.Count == 0) return null;

        if (find)
        {
            // 主框停用：这一次按键不接（不落到第二个框上，见类注释第 ① 条）。
            return boxes[0].Enabled ? new FilterGestureStep(0, FilterGestureAction.FocusKeyword) : null;
        }

        if (enter)
        {
            for (var index = 0; index < boxes.Count; index++)
            {
                if (boxes[index].Focused) return new FilterGestureStep(index, FilterGestureAction.FocusFirstResult);
            }
            return null;
        }

        if (escape)
        {
            for (var index = 0; index < boxes.Count; index++)
            {
                if (boxes[index].Focused && boxes[index].HasKeyword)
                {
                    return new FilterGestureStep(index, FilterGestureAction.ClearKeyword);
                }
            }
            return null;
        }

        return null;
    }
}
