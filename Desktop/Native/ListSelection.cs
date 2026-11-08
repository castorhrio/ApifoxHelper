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
/// 同一个主题的另一侧也在这里：没有选中那一行时，这一屏说什么（<see cref="MissingSelectionHint"/>），
/// 以及打开这一屏时该落在哪一行（<see cref="FirstRowOnOpen"/>）。
/// 「怎么取那一行」「取不到时怎么说」「打开时落在哪一行」讲的是同一格状态，分成几份文件必然各写各的条件。
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

    /// <summary>
    /// 「缺什么」这句话由详情区来说吗：清单里有行可选、却一行都没选中才说。
    /// 一行都没有时缺的不是「选中」而是筛选或数据本身，那一屏的说话位是提示出口
    /// （第五十九、六十轮立的形状），详情区这时再讲「选中一行」就是把不存在的行说成还能选——
    /// 与第四十二轮「禁用不得移除提示所指引的下一步」同侧：指的路得真的走得通。
    /// 负数按「没有行」处理，与 <see cref="FirstPosition"/> 同一纪律。
    /// </summary>
    public static bool ShouldExplainMissingSelection(int displayedRowCount) => displayedRowCount > 0;

    /// <summary>
    /// 打开这一屏时该落在哪一行（DESIGN.md §6「列表行」的「审阅队列打开即选中第一条」，第九十轮）：
    /// 清单里有行就落在第一条（位次 0），一行都没有就没有落点（null）——不索引不存在的第一行，
    /// 那一屏的下一步在提示与按钮上（与第五十六轮「一行不剩则没有落点」、第五十三轮「宁可复制不到」同一取舍）。
    /// 与 <see cref="ShouldExplainMissingSelection"/> 是同一件事的两面：有行才落，落不到时那一屏由提示出口说话；
    /// 用户自己取消选中之后，缺什么仍由详情区那句话交代。
    /// 只回答「落在哪一行」，不回答「要不要把键盘也交给它」——那是 <see cref="ListFocus"/> 的事，
    /// 打开时交键盘接在窗体首次显示之后（OnShown），落点接在数据读进来之后（OnLoad），两件事各有各的时机。
    /// </summary>
    public static int? FirstRowOnOpen(int displayedRowCount) => displayedRowCount > 0 ? 0 : null;

    /// <summary>未选中时指名的下一步；说这句话，清单就得真的有行可选（见 <see cref="ShouldExplainMissingSelection"/>）。</summary>
    public const string MissingSelectionPointer = "在清单里选中一行，它的详情显示在这里，选中动作随之可用。";

    /// <summary>
    /// 没有选中那一行时详情区给的一句话（DESIGN.md §7「禁用 = 控件禁用 + 一句话说明缺什么」）：
    /// 前半句说缺什么，后半句是共享的下一步。名词沿用 <see cref="ListFilter"/> 的那五个常量——
    /// 「这一行的条目叫什么」只有一个词汇表，两处各写一份必然漂移。
    /// </summary>
    public static string MissingSelectionHint(string noun) => $"没有选中{noun}。{MissingSelectionPointer}";
}
