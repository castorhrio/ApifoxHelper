using System.Windows.Forms;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 一屏的关键词框登记表 + 手势分发（DESIGN.md §6「列表筛选」与「菜单/快捷键」）。
/// 各视图在构造时把「本屏有哪几个关键词框、各自配哪张清单」登记一次（主框先登记），
/// <see cref="ProcessDialogKey"/> 里只留一行 <see cref="Dispatch"/>——认键在
/// <see cref="Ui.FilterGestureFacts"/> 一处、判定在 <see cref="FilterGesturePlan"/> 一处、
/// 动作在 <see cref="Ui.TryFocusFilter"/> / <see cref="Ui.TryEnterFilter"/> / <see cref="Ui.TryEscapeFilter"/>
/// 三处，界面不就地认键、不就地拆修饰键、不自己选中或滚动、不自己清空关键词。
/// <para>
/// <b>为什么要有这一层</b>（CRITIQUE 第 9 站，第二百二十六轮）：收口前三个双框对话框各写一遍
/// 「按当前可见页 / 焦点挑一个框」的序列（<c>SyncPreviewDialog</c> 两页各一个框、
/// <c>ApplyPlanDialog</c> 文件清单与跳过清单、<c>CheckResultsDialog</c> 接口清单与模型清单），
/// 规则相同、落点三份；单框的九张长清单又各写一遍三行。现在「哪几个框」是数据（登记），
/// 「落在哪一个」是判定（<see cref="FilterGesturePlan"/>），「怎么落」是动作（Ui 那三处入口），三件事各一处。
/// </para>
/// <para>
/// <b>只做三件事</b>：Ctrl+F 把焦点交给主框并选中已有关键词；框里的裸 Enter 把焦点与选中交给
/// 筛选结果第一行（目标行与选中走 <see cref="Ui.FocusFirstResult"/> 一处）；框里的 Esc 清空关键词
/// （重建仍由各视图既有的 TextChanged 触发）。不改筛选结果、不重建清单、不写数据、不碰选中。
/// 不是本屏接的按键返回 false，交回基类——对话框的 Enter=确认、Esc=取消因此逐字不变。
/// </para>
/// </summary>
internal sealed class FilterGestures
{
    /// <summary>登记的一项：一个关键词框与它筛的那张清单（字段名与 Ui 那三处入口的形参同名，读起来对得上）。</summary>
    private readonly record struct Box(TextBox Keyword, ListView List);

    private readonly List<Box> _boxes = [];

    /// <summary>登记本屏的主框（Ctrl+F 的落点）与它筛的那张清单。</summary>
    public FilterGestures(TextBox primary, ListView primaryList)
    {
        _boxes.Add(new Box(primary, primaryList));
    }

    /// <summary>再登记一个框（Enter 与 Esc 看焦点各接一次；Ctrl+F 仍只交给主框）。</summary>
    public FilterGestures Then(TextBox keyword, ListView list)
    {
        _boxes.Add(new Box(keyword, list));
        return this;
    }

    /// <summary>登记了几个框（用例按它核对「界面登记的框数 == 这一屏的关键词框数」）。</summary>
    public int Count => _boxes.Count;

    /// <summary>
    /// 接这一次按键：认键取 <see cref="Ui.FilterGestureFacts"/>（手势字母与修饰键都在共享口径里），
    /// 判定取 <see cref="FilterGesturePlan.Resolve"/>，动作按判定结果交回 Ui 那三处入口——
    /// 本文件不认键、不拆修饰键、不自己选中或滚动、不自己清空关键词。
    /// </summary>
    public bool Dispatch(Keys keyData)
    {
        var (find, enter, escape) = Ui.FilterGestureFacts(keyData);
        if (!find && !enter && !escape) return false;

        var states = new FilterBoxState[_boxes.Count];
        for (var index = 0; index < _boxes.Count; index++)
        {
            var box = _boxes[index];
            states[index] = new FilterBoxState(box.Keyword.Enabled, box.Keyword.Focused, ListFilter.IsActive(box.Keyword.Text));
        }

        if (FilterGesturePlan.Resolve(find, enter, escape, states) is not { } step) return false;

        var (target, list) = _boxes[step.BoxIndex];
        return step.Action switch
        {
            FilterGestureAction.FocusKeyword => Ui.TryFocusFilter(keyData, target),
            FilterGestureAction.FocusFirstResult => Ui.TryEnterFilter(keyData, target, list),
            FilterGestureAction.ClearKeyword => Ui.TryEscapeFilter(keyData, target),
            _ => false,
        };
    }
}
