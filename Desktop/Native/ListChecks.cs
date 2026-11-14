namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 复选清单的勾选手势口径（不依赖 WinForms、与测试工程单一编译）：点行的任意处就把那一行勾上或取消。
/// 全原生界面只有两种清单：走 <c>Ui.List</c> 工厂的自绘清单，与新建任务的模块清单——后者此前是
/// <c>CheckedListBox</c> 且开了 <c>CheckOnClick</c>，点行名即勾选，代价是选中行退回系统高亮蓝、
/// 边框是系统线而不是 §5 的发丝边。并进同一个工厂的前提，就是把这一手势一并搬过来：
/// 工厂自绘后系统只认状态图标那一小格命中区，而勾选框是画出来的（第二十三轮），
/// 手势不接上的话「勾选」的目标从整行缩成十几像素，比它替掉的东西更难点。
/// 一次点击只翻一次：判定只让开系统自己会翻的那一格，其余落在行上的落点归本手势。
/// 两者用的是同一套命中坐标（<c>HitTest</c> 就是控件自己那套判定），因此不会一次点击翻两下；
/// 而自绘框与系统命中区之间哪怕有位差，用户看着框点下去也仍然翻得动——目标只会更大，不会更碎。
/// 行以外（列头、清单外空白）不动作，与第三十九轮「命中哪行激活哪行、双击空白区不动作」同一纪律；
/// 右键不勾选——右键不是点选手势，界面上也没有上下文菜单可给它翻（第五十二、五十三轮挂账）。
/// <para>
/// 本文件同时是<b>批量「全选」手势</b>（Ctrl+A）的判定落点（第二百八十四轮）：两只手势判的是同一件事的两头——
/// 一只翻单行、一只翻可见的一批，且都在<b>复选清单</b>上，故同住一处，不另起一份口径文件：
/// 同一个 Ctrl 修饰键在两个文件里各拆一遍，两份迟早漂（本仓另外两只 Ctrl 手势——筛选框找一找、
/// 清单复制——判定各在自己的手势文件里，本文件也不重复它们）。
/// 装法分两处但各司其职：整行勾选装在 <c>Ui.List</c> 工厂的 <c>checkBoxes</c> 分支（每一张复选清单都自带），
/// 全选装在 <c>Ui.WireSelectAll</c> 这个共用入口上——它只认键与修饰键，勾哪些行由调用方交进来的动作决定。
/// </para>
/// <para>
/// <b>「翻这一行」的键盘入口（第二百八十九轮）</b>：同一件事此前只有鼠标一条入口——键盘上按空格翻行
/// 靠的是 <c>ListView</c> 自带的系统实现，本仓既没有它的判定、也没有任何断言，而
/// <c>docs/DESIGN.md</c> §6 与 <c>SyncSelect</c> 的注释里「逐行按空格」这句话一直指着一处没人接的默认行为。
/// 现把空格收进本文件（<see cref="IsToggleRowGesture"/>），与鼠标那条入口共用同一个翻转点
/// （<c>Ui.List</c> 工厂里的 <c>ToggleRow</c>）：两条入口、一件事、一处翻转。
/// </para>
/// </summary>
public static class ListChecks
{
    /// <summary>
    /// 这一次点击是否由本手势翻那一行：左键、命中在一行上、且落点不是系统自己会翻的那一格。
    /// </summary>
    public static bool ShouldToggle(bool leftClick, bool hitOnRow, bool hitOnCheckbox) =>
        leftClick && hitOnRow && !hitOnCheckbox;

    /// <summary>
    /// 裸空格判定（复选清单的「翻这一行」手势，第二百八十九轮）：调用方已确认按下空格，这里只看修饰键。
    /// 只认裸键——带 Ctrl/Shift/Alt 的组合不属本手势、留给控件自身，不吞键，也不给既有组合键发明新语义
    /// （与 <see cref="IsSelectAllGesture"/>、<see cref="ListCopy.IsCopyGesture"/>、
    /// <see cref="ShellShortcuts.IsFindGesture"/> 同一纪律：那三处各在自己的口径文件里，本文件不重复它们）。
    /// <para>
    /// 为什么补这一只手势：勾选页的批量动作（Ctrl+A 与「全选可同步」）第二百八十四轮已接上，
    /// 而「只勾这一行」在键盘上一直靠 <c>ListView</c> 的系统默认实现——本仓没有它的判定、没有它的接线、
    /// 也没有任何断言，于是同一件事的两条入口（鼠标点行 / 键盘空格）落在两处实现上：
    /// 鼠标那条走 <c>Ui.List</c> 工厂的翻转点，键盘那条走系统；系统那一支一旦被自绘、被
    /// <c>ItemCheck</c> 的拒绝路径或某个修饰键改掉，界面上就是「点得动、按不动」，而全仓没有一条门会响。
    /// </para>
    /// <para>
    /// 它不接管文本框里的空格：本判定只装在复选清单自己的按键通路上（<c>Ui.List</c> 的
    /// <c>checkBoxes</c> 分支），焦点在关键词框里时空格仍是「打一个空格」。
    /// </para>
    /// </summary>
    public static bool IsToggleRowGesture(bool shift, bool control, bool alt) => !shift && !control && !alt;

    /// <summary>
    /// 裸 Ctrl+A 判定（复选清单的「全选」手势）：调用方已确认按下 A，这里只看修饰键。
    /// 只认 Ctrl——带 Shift/Alt 的组合不属本手势、留给控件自身，不吞键，也不给既有组合键发明新语义
    /// （与列表的复制手势、Enter 手势同一纪律：那两处判定各在自己的口径文件里，本文件不重复它们）。
    /// <para>
    /// 为什么补这一只手势：同步预览的接口勾选页是「生成计划前选哪些接口」的唯一入口，清单是整份检查结果的
    /// 接口（方案 §4.2 记录千级接口分支），而这一页的批量动作此前只有一颗「全选可同步」按钮——
    /// 键盘用户要勾一批只能逐行按空格。资源管理器里同一个动作就是 Ctrl+A，而 <see cref="SyncSelect"/>
    /// 的类注释早就写着那颗按钮的作用域「与资源管理器 Ctrl+A 同序」：<b>那句话指着一个界面上按不出来的键</b>。
    /// 本手势补的就是它，作用域与那颗按钮逐字相同（只作用于可见行），动作也交给同一处
    /// （<see cref="SyncSelect.SelectAllSyncable"/>），不另写一份「勾哪些行」的判定。
    /// </para>
    /// <para>
    /// 它不接管文本框里的 Ctrl+A：本判定只装在复选清单自己的按键通路上（<c>Ui.WireSelectAll</c>），
    /// 焦点在关键词框里时 Ctrl+A 仍是「选中框里的全部文字」——<see cref="ShellShortcuts.ReservedEditingKeys"/>
    /// 把 Ctrl+A 列为壳不得占用的编辑键，正是这一条。
    /// </para>
    /// </summary>
    public static bool IsSelectAllGesture(bool shift, bool control, bool alt) => control && !shift && !alt;
}
