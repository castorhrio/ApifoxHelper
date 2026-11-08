namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 列表 Ctrl+C 复制的口径（不依赖 WinForms、与测试工程单一编译）。界面里的列表单元格是自绘的，
/// 文字不可选中；只读预览框能选，列表与标签不能——此前整个原生界面没有任何把文字交给用户的通路
/// （没有剪贴板入口，也没有右键菜单）。「GET /路径」这类身份出现在检查结果、补充说明、同步预览与
/// 契约/追踪清单的名称列，而新建任务的「指定接口」要求逐行手敲同样的文字。
/// 补的是资源管理器本就有的手势：Ctrl+C 复制选中项的名称列——即本行的标识列，不是整行拼接，
/// 也不给列表加新控件、新颜色、新提示。标识列默认是首列（与 <see cref="ListColumns"/> 的
/// 「标识列（首列）」同一约定，绝大多数列表的名称格就在首列）；首列不是名称的列表（写入清单首列是
/// 动作序号、差异确认首列是「接口/模型」这种分类）在出厂处声明真正的名称格——抄出去的是这一行在
/// 屏幕之外还能被指代的东西，而不是一格认不出是谁的数字。
/// 本文件只判定按键、标识列与要交出去的文字，不触碰控件、不调用剪贴板：
/// 写入由 <see cref="Ui"/> 的列表工厂在一处承担，页面与对话框不各写一套。
/// </summary>
public static class ListCopy
{
    /// <summary>标识列的默认位置：首列。列表不声明即按此取值。</summary>
    public const int DefaultIdentityColumn = 0;

    /// <summary>
    /// 裸 Ctrl+C 判定：调用方已确认按下 C，这里只看修饰键。只认 Ctrl——带 Shift/Alt 的组合不属
    /// 本口径、留给控件自身，不吞键也不给既有组合键发明新语义（与 ListKeys.IsEnterGesture 同一纪律）。
    /// </summary>
    public static bool IsCopyGesture(bool shift, bool control, bool alt) => control && !shift && !alt;

    /// <summary>
    /// 标识列取值：出厂处声明的列必须落在这一行实际的格数之内。列被增删而声明没跟上时，
    /// 这一行没有可交出去的一格（返回 null，不触剪贴板）——宁可什么都不做，也不把邻列的文字
    /// 当成名称抄出去：用户粘贴到「指定接口」里的东西一旦认错，比复制不到更难发现。
    /// </summary>
    public static int? IdentityColumn(int cellCount, int declared) =>
        declared >= 0 && declared < cellCount ? declared : null;

    /// <summary>
    /// 要交出去的文字：焦点行标识列的原文，原样给出（不裁剪、不补空格、不拼其余列）。
    /// 焦点行未选中即返回 null——只复制屏幕上真正标出来的那一行，与鼠标点选看到的是同一行；
    /// 标识列没有文字同样返回 null——不动作，更不把用户先前复制的内容抹掉。
    /// </summary>
    public static string? CopyText(string? focusedRowText, bool focusedRowSelected)
    {
        if (!focusedRowSelected) return null;
        return string.IsNullOrWhiteSpace(focusedRowText) ? null : focusedRowText;
    }
}
