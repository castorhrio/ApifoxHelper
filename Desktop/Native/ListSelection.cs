namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「当前选中的那一行」读取口径（DESIGN.md §6「列表行」）：界面上这一问只有一个问法——
/// 先量选中数，量到才有那一行，量不到就没有那一行；不得先索引再问有没有。
/// 理由不是风格。<c>SelectedItems</c> 与 <c>SelectedIndices</c> 的索引器在越界时做什么
/// （返回 null 还是抛异常），本环境既没有原生运行时、也没有网络可核对官方源码；
/// 把「按钮按下去发生了什么」建在这个不可验证的语义上，落地只有两种：那一次点击是未处理异常，
/// 或者是第四十轮修过的同一族缺陷「按钮残留启用、点击无响应」。
/// 此前界面上 20 处显式量过选中数、4 处依赖索引器的可空返回值——同一件事两种做法，
/// 现收成一处：判定在本口径，索引只在 <c>Ui.SelectedItem</c> / <c>Ui.SelectedIndex</c> 唯一入口发生，
/// 页面与对话框不自己写索引（与第五十二轮「剪贴板只触达一次」、第五十七轮「滚动只触达一次」、
/// 第五十八轮「交回键盘只触达一次」同一做法）。
/// 列表一律经 <c>Ui.List</c> 工厂出厂且 MultiSelect = false，选中数最多为 1，
/// 所以「选中集合的第一格」就是「那一行」；不为多选清单猜其余行。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class ListSelection
{
    /// <summary>
    /// 选中集合里可取的那一格的位次：选中数大于零才是 0，否则为 null——拿到 null 的调用方不得碰索引器。
    /// 负数按「没有」处理：不可发生的形状不猜它指向哪一格（与第五十三轮「声明的列落在格数之外时宁可复制不到」、
    /// 第五十六轮「一行不剩则没有落点」同一取舍）。
    /// </summary>
    public static int? FirstPosition(int selectedCount) => selectedCount > 0 ? 0 : null;
}
