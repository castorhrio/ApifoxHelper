namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 列表 Ctrl+C 复制的口径（不依赖 WinForms、与测试工程单一编译）。界面里的列表单元格是自绘的，
/// 文字不可选中；只读预览框能选，列表与标签不能——此前整个原生界面没有任何把文字交给用户的通路
/// （没有剪贴板入口，也没有右键菜单）。「GET /路径」这类身份只在检查结果、补充说明、同步预览与
/// 契约/追踪清单的首列出现，而新建任务的「指定接口」要求逐行手敲同样的文字。
/// 补的是资源管理器本就有的手势：Ctrl+C 复制选中项的名称列——在这里即首列（标识列，与
/// <see cref="ListColumns"/> 的「标识列（首列）」同一约定），不是整行拼接，也不给列表加新控件、
/// 新颜色、新提示。本文件只判定按键与要交出去的文字，不触碰控件、不调用剪贴板：
/// 写入由 <see cref="Ui"/> 的列表工厂在一处承担，页面与对话框不各写一套。
/// </summary>
public static class ListCopy
{
    /// <summary>
    /// 裸 Ctrl+C 判定：调用方已确认按下 C，这里只看修饰键。只认 Ctrl——带 Shift/Alt 的组合不属
    /// 本口径、留给控件自身，不吞键也不给既有组合键发明新语义（与 ListKeys.IsEnterGesture 同一纪律）。
    /// </summary>
    public static bool IsCopyGesture(bool shift, bool control, bool alt) => control && !shift && !alt;

    /// <summary>
    /// 要交出去的文字：焦点行自身的首列原文，原样给出（不裁剪、不补空格、不拼其余列）。
    /// 焦点行未选中即返回 null——只复制屏幕上真正标出来的那一行，与鼠标点选看到的是同一行；
    /// 首列没有文字同样返回 null——不动作，更不把用户先前复制的内容抹掉。
    /// </summary>
    public static string? CopyText(string? focusedRowText, bool focusedRowSelected)
    {
        if (!focusedRowSelected) return null;
        return string.IsNullOrWhiteSpace(focusedRowText) ? null : focusedRowText;
    }
}
