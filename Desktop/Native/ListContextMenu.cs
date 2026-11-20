namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 列表右键菜单的口径（不依赖 WinForms、与测试工程单一编译）。
/// <para>
/// <b>补的是哪个缺口</b>：列表单元格自第二十三轮起由 <c>Ui.List</c> 工厂自绘，文字不再可选中；
/// 第五十二轮补上了 Ctrl+C 复制标识列，那是<b>唯一</b>把一格文字交给用户的通路——而它<b>在屏幕上不可见</b>：
/// 不知道这个手势的人，看着「GET /路径」那一格仍然只能肉眼抄。本仓从第二十二轮起把「不做右键菜单」
/// 挂在账上（第五十二、五十三、五十七、六十一、六十二、六十三轮各记一次），挂账的理由是
/// 「右键不是点选手势，界面上也没有上下文菜单可给它翻」——现在有了：右键菜单是 Windows 列表里
/// 「这一行能做什么」的既有入口，而这一行能做的事恰好只有一件，就是复制它的标识列。
/// </para>
/// <para>
/// <b>只放一项，动作与 Ctrl+C 同一条路</b>：菜单里只有「复制」一项，它调用的守卫链与 Ctrl+C 逐字相同
/// （<c>Ui.List</c> 工厂里那一个 <c>CopyTarget</c>：焦点行 → 声明的标识列 → 那一格的原文），
/// 于是「哪一格算标识列」「哪一行算被标出来」仍然只有一处判定，两条入口不会各说一套
/// （与第二百八十四轮「Ctrl+A 与『全选可同步』那颗按钮同一件事的两条入口」同一做法）。
/// 剪贴板写入点仍只有 <c>Ui.CopyToClipboard</c> 一处。
/// </para>
/// <para>
/// <b>键盘上的同一件事（第四百二十二轮）</b>：第四百一十八轮把菜单装上了，可它<b>只有鼠标到得了</b>——
/// 键盘用户（以及手上没有鼠标的那一下）看着焦点行仍然拿不到那一格文字，而 Windows 列表里打开上下文菜单
/// 的既有手势有两个：<c>Shift+F10</c> 与菜单键（<c>VK_APPS</c>）。本轮把这两个手势接到<b>同一份菜单、
/// 同一条守卫链、同一个弹出点</b>上（工厂里那一个 <c>ShowMenu</c>：先按 <c>CopyTarget</c> 定可不可点，
/// 再弹），于是「这一次该不该弹」仍只有一处判定（<see cref="ShouldOffer"/> 判鼠标、
/// <see cref="ShouldOfferByKeyboard"/> 判键盘），两条入口不会各说一套。
/// </para>
/// <para>
/// <b>不新造视觉语言</b>：菜单是系统 <c>ContextMenuStrip</c>，不设颜色、不设图标、不加第二项——
/// 本仓的灰阶加单强调色说的是界面自己画的东西，系统菜单沿用系统观感（与表头「沿用系统绘制」同一取舍）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判该复制哪一格（那是 <see cref="ListCopy"/>）、不判右键要不要翻勾选框
/// （右键不是点选手势，<see cref="ListChecks"/> 一行未动）、不判菜单项此刻可不可点
/// （可点与否由同一条 <c>CopyTarget</c> 守卫链给出：没有可交出去的文字就灰着，不弹一个点了没反应的项）。
/// </para>
/// </summary>
public static class ListContextMenu
{
    /// <summary>
    /// 菜单项文字（唯一落点）：与 Ctrl+C 是同一件事，所以只说「复制」，不写「复制标识列」——
    /// 用户看到的是「复制这一行的那一格」，而「哪一格」是 <see cref="ListCopy"/> 的口径，不是菜单要解释的事。
    /// 助记符 <c>(&amp;C)</c> 与壳菜单的写法一致（<c>文件(&amp;F)</c> 那一族）。
    /// </summary>
    public const string CopyItemText = "复制(&C)";

    /// <summary>
    /// 这一次右键该不该弹菜单：<b>右键</b>且<b>命中在一行上</b>，两条都成立才弹。
    /// <para>
    /// 左键不弹：左键的按下与抬起是选中、双击与勾选的地盘，本入口不接（<c>MouseUp</c> 里判按钮，
    /// 不靠「右键才走到这里」这种读代码看不出来的约定）。
    /// </para>
    /// <para>
    /// 命中不在行上（列头、清单外空白）不弹：与第三十九轮「双击空白区不动作」、第六十三轮
    /// 「命中不在行上不动作」同一纪律——空白处没有「这一行」，菜单里的动作没有作用对象。
    /// 也不为空白处发明一个「背景菜单」：这一屏在空白处能做的事，工具栏与菜单里都已经有了。
    /// </para>
    /// </summary>
    public static bool ShouldOffer(bool rightButton, bool hitOnRow) => rightButton && hitOnRow;

    /// <summary>
    /// 键盘上打开这份菜单的手势（第四百二十二轮）：<c>Shift+F10</c> 或菜单键（<c>VK_APPS</c>）——
    /// Windows 列表里打开上下文菜单的两个既有手势，不为这一屏发明第三个。
    /// <para>
    /// 只认这两个：裸 <c>F10</c> 是「把焦点交给菜单栏」的系统手势（壳有菜单栏，抢它等于让菜单栏按不动），
    /// 裸 <c>Enter</c>／<c>Space</c>／方向键各有自己的事（打开选中行、翻勾选、移动焦点），
    /// 带 <c>Ctrl</c>／<c>Alt</c> 的组合不吞键（与 <see cref="ListCopy.IsCopyGesture"/>、
    /// <see cref="ListChecks.IsToggleRowGesture"/> 同一分寸：不给既有组合键发明新语义）。
    /// </para>
    /// </summary>
    public static bool IsMenuGesture(bool menuKey, bool shift, bool f10) => menuKey || (shift && f10);

    /// <summary>
    /// 这一次按键该不该弹菜单：手势成立，且清单里<b>有焦点行</b>。
    /// <para>
    /// 焦点行缺失（清单是空的、或焦点框不在任何一行上）不弹：菜单里的动作作用于「这一行」，
    /// 没有这一行就没有作用对象——与 <see cref="ShouldOffer"/> 里「命中不在行上不弹」同一纪律，
    /// 也不借当前选中去猜一行（与第五十一轮「焦点行缺失即不动作」同一取舍）。
    /// </para>
    /// <para>
    /// 与鼠标那条入口分开判、不合并成一个「有没有作用对象」的参数：鼠标问的是「光标下有没有一行」，
    /// 键盘问的是「焦点框在哪一行」，两个问题的答案可以不同（右键落在别的行上时焦点行还是原来那一行），
    /// 合并会把「右键先选中光标下那一行」这条既有语义读丢。
    /// </para>
    /// </summary>
    public static bool ShouldOfferByKeyboard(bool menuKey, bool shift, bool f10, bool hasFocusedRow) =>
        IsMenuGesture(menuKey, shift, f10) && hasFocusedRow;
}
