namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 键盘归属口径（不依赖 WinForms、与测试工程单一编译）：审阅动作把一条处理完之后，键盘该不该交给清单。
/// 交回是一件一直没被管过的事：<c>ListViewItem.Focused</c> 与 <c>Selected</c> 都是清单内部的项级标记，
/// 控件本身有没有键盘是另一件事——点「接受」「保存」时焦点停在那个按钮上，方向键此后动的是按钮所在
/// 的控件，不是队列。第三十三轮的焦点标记、第五十六轮的续接落点、第五十七轮的定位说的都是「方向键从该行继续」，
/// 这句话在鼠标完成的那条路上此前并不成立（两页在第五十一轮正是为此先 <c>_list.Focus()</c> 再经唯一入口选中）。
/// 交回的判定只认一件事：选中行与清单内的焦点行是同一行、且那一行确实在这份清单之内。
/// 不满足就不动键盘——不猜第一行、不把方向键交给一个没有落点的清单（与第五十三轮「声明的列落在格数之外时
/// 宁可复制不到」、第五十六轮「一行不剩则没有落点」同一取舍）；队列清完时的下一步仍由各视图的空态与
/// 提示出口承担（第四十二、五十轮）。
/// 这里不索引任何序号，只把键盘交给清单控件本身，因此不需要第五十七轮那套与框架异常窗口互补的上界守卫。
/// </summary>
public static class ListFocus
{
    /// <summary>
    /// 是否把键盘交给清单：选中行序号有效，且与焦点行是同一行才交。
    /// 选中行缺失（详情区是空的）、焦点行缺失（方向键没有起点）、两者不是同一行（交回去会让方向键
    /// 从另一行继续，而详情与审阅按钮跟着的是选中的那一行）一律不动作。
    /// </summary>
    public static bool ShouldHandToQueue(int? selectedIndex, int? focusedIndex) =>
        selectedIndex is int selected && selected >= 0 && focusedIndex is int focused && focused == selected;
}
