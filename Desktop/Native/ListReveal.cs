namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 选中即定位口径（不依赖 WinForms、与测试工程单一编译）：程序化选中一行之后，那一行必须在可见位置，
/// 否则「选中」只剩数据——清单重建（刷新、切页、筛选、保存对话框返回、处理完一条续接下一条）会把滚动
/// 位置甩回顶部，几十上百行的列表里选中停在第 45 行时屏幕上什么也看不出来，方向键与审阅按钮都从
/// 一行看不见的行继续（第三十三轮恢复的焦点标记、第五十六轮续接的落点都要靠这一步才成立）。
/// 本文件只判定这一行在不在清单之内，不触碰控件：滚动由 <see cref="Ui.SelectItem"/> 一处承担，
/// 页面与对话框不各写一套。
/// 守卫不是形式：控件的 EnsureVisible 对负序号与不小于行数的序号先抛 <c>ArgumentOutOfRangeException</c>
/// 再判窗口句柄（.NET WinForms 源码 ListView.EnsureVisible：ThrowIfNegative / ThrowIfGreaterThanOrEqual）。
/// 本环境没有原生运行时可跑，这一步只能按框架源码事实预判：序号认不出这张清单时宁可不动，
/// 也不让一次选中在用户的机器上变成未处理的异常。
/// </summary>
public static class ListReveal
{
    /// <summary>
    /// 是否该为这一行请求滚动：序号在本清单行数之内（非负、不越过末行）才动；
    /// 空清单、行已离开控件（序号为负）、清单在请求之前又缩了水，一律不动作。
    /// </summary>
    public static bool ShouldReveal(int index, int displayedCount) => index >= 0 && index < displayedCount;
}
