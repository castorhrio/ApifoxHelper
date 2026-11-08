using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 控件形态的统一出口（docs/DESIGN.md §6）：主按钮/ghost/危险、标签档位、发丝边包裹、列表行形态。
/// 界面代码从这里取控件形态，不逐处设置颜色与边框。
/// </summary>
internal static class Ui
{
    // 列表行保留的语义色（颜色+文字双通道）：与 UiTokens 一一对应，交给 ListRows 判定。
    private static readonly HashSet<Color> SemanticColors =
    [
        UiTokens.Muted,
        UiTokens.OkText,
        UiTokens.WarnText,
        UiTokens.BadText,
    ];

    /// <summary>首屏大标题（Title 档，DESIGN.md §3 声明的使用点：空态引导）。</summary>
    public static Label Title(string? text = null) => Label(UiTokens.Title.Value, UiTokens.Text, text);
    /// <summary>实心主按钮：Accent 填充 + 白字，hover 只改底色；每屏至多一个。</summary>
    public static Button Primary(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiTokens.Accent,
            ForeColor = Color.White,
            Padding = new Padding(UiTokens.Md, UiTokens.Xs, UiTokens.Md, UiTokens.Xs),
            Margin = new Padding(0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = UiTokens.AccentHover;
        button.FlatAppearance.MouseDownBackColor = UiTokens.AccentHover;
        return button;
    }

    /// <summary>Ghost 按钮：面板底 + 1px 发丝边，hover 只改底色。</summary>
    public static Button Ghost(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiTokens.Panel,
            ForeColor = UiTokens.Text,
            Padding = new Padding(UiTokens.Md, UiTokens.Xs, UiTokens.Md, UiTokens.Xs),
            Margin = new Padding(0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = UiTokens.Line;
        button.FlatAppearance.MouseOverBackColor = UiTokens.Bg;
        button.FlatAppearance.MouseDownBackColor = UiTokens.Bg;
        return button;
    }

    /// <summary>停止/危险按钮：ghost 形态 + 危险字色（位置尺寸不变，防按钮跳位）。</summary>
    public static Button DangerGhost(string text)
    {
        var button = Ghost(text);
        button.ForeColor = UiTokens.BadText;
        return button;
    }

    public static Label Caption(string? text = null, bool muted = true) => Label(UiTokens.Caption.Value, muted ? UiTokens.Muted : UiTokens.Text, text);

    public static Label Body(string? text = null, bool muted = false) => Label(UiTokens.Body.Value, muted ? UiTokens.Muted : UiTokens.Text, text);

    public static Label Section(string? text = null) => Label(UiTokens.Section.Value, UiTokens.Text, text);

    public static Label Mono(string? text = null) => Label(UiTokens.Mono.Value, UiTokens.Text, text);

    public static Label StatusText(string? text = null) => Label(UiTokens.Body.Value, UiTokens.Text, text);

    /// <summary>错误/提示行：固定占位（文案变化不推动布局），错误用文字色不铺底。</summary>
    public static Label StatusLine() => new()
    {
        AutoSize = false,
        Height = UiTokens.Body.Value.Height + UiTokens.Xs * 2,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = UiTokens.BadText,
        Font = UiTokens.Body.Value,
        Text = "",
    };

    private static Label Label(Font font, Color color, string? text) => new()
    {
        Text = text ?? "",
        AutoSize = true,
        Font = font,
        ForeColor = color,
        Margin = new Padding(0),
    };

    /// <summary>
    /// 可编辑输入框（DESIGN.md §6「输入框」）：去系统 3D 边框、面板底色；
    /// 多行输入 Enter 换行（AcceptsReturn），不触发对话框主按钮——模块范围、指定接口、
    /// JSON 编辑位等「每行一条」的输入靠 Enter 分行，主按钮用点击或 Tab 进入；
    /// 放入界面时必须经 <see cref="Field"/> 包 1px 描边（常态 Line、聚焦 Accent）。
    /// </summary>
    public static TextBox Input(
        bool multiline = false,
        bool mono = false,
        bool caption = false,
        bool wordWrap = true,
        bool password = false,
        ScrollBars scrollBars = ScrollBars.None) =>
        Box(new TextBox { Multiline = multiline, WordWrap = wordWrap, UseSystemPasswordChar = password, ScrollBars = scrollBars, AcceptsReturn = multiline }, mono, caption);

    /// <summary>只读预览框：同一镀铬加 ReadOnly；放入界面时经 Hairline 包发丝边（只读无聚焦描边）。</summary>
    public static TextBox Preview(
        bool multiline = false,
        bool mono = false,
        bool caption = false,
        bool wordWrap = true,
        ScrollBars scrollBars = ScrollBars.None) =>
        Box(new TextBox { ReadOnly = true, Multiline = multiline, WordWrap = wordWrap, ScrollBars = scrollBars }, mono, caption);

    private static TextBox Box(TextBox box, bool mono, bool caption)
    {
        box.BorderStyle = BorderStyle.None;
        box.BackColor = UiTokens.Panel;
        box.Margin = new Padding(0);
        if (mono) box.Font = UiTokens.Mono.Value;
        if (caption) box.Font = UiTokens.Caption.Value;
        return box;
    }

    /// <summary>
    /// 输入框镀铬（DESIGN.md §6「输入框」）：1px 描边面板——常态 Line，聚焦描边 Accent，
    /// 校验失败描边 BadText（<see cref="FieldBox.SetInvalid"/>），色判定在 FieldChrome（与测试工程共享编译）。
    /// 内外尺寸双向同步：文本框改宽高时面板跟随，面板被布局拉伸（行助手设宽设高）时文本框跟随。
    /// </summary>
    public static FieldBox Field(TextBox input) => new(input);

    public sealed class FieldBox : Panel
    {
        private readonly TextBox _inner;
        private bool _syncing;
        private bool _invalid;

        public FieldBox(TextBox inner)
        {
            _inner = inner;
            BackColor = UiTokens.Line;
            Padding = new Padding(1);
            Margin = new Padding(0);
            inner.GotFocus += (_, _) => RefreshChrome();
            inner.LostFocus += (_, _) => RefreshChrome();
            // 用户改值后旧判定不再适用：解除校验失败描边（文字状态行由对话框自行管理）。
            inner.TextChanged += (_, _) => SetInvalid(false);
            inner.Resize += (_, _) => SyncFromInner();
            Resize += (_, _) => SyncFromOuter();
            Controls.Add(inner);
            Size = new Size(inner.Width + Padding.Horizontal, inner.Height + Padding.Vertical);
        }

        /// <summary>校验失败标记：描边 BadText（DESIGN.md §6「输入框」，双通道中的形状通道——文字仍在状态行）。</summary>
        public void SetInvalid(bool invalid)
        {
            if (_invalid == invalid) return;
            _invalid = invalid;
            RefreshChrome();
        }

        private void RefreshChrome() => BackColor = FieldChrome.Border(_inner.Focused, _invalid, UiTokens.Line, UiTokens.Accent, UiTokens.BadText);

        private void SyncFromInner()
        {
            if (_syncing) return;
            _syncing = true;
            Size = new Size(_inner.Width + Padding.Horizontal, _inner.Height + Padding.Vertical);
            _syncing = false;
        }

        private void SyncFromOuter()
        {
            if (_syncing) return;
            _syncing = true;
            _inner.Location = new Point(Padding.Left, Padding.Top);
            _inner.Size = new Size(Width - Padding.Horizontal, Height - Padding.Vertical);
            _syncing = false;
        }
    }

    /// <summary>用 1px 发丝边（Line 色）包裹无边框内容控件。</summary>
    public static Panel Hairline(ListView inner)
    {
        inner.Dock = DockStyle.Fill;
        inner.BorderStyle = BorderStyle.None;
        return HairlineCore(inner);
    }

    /// <summary>同上，用于只读文本框（去系统边框，包发丝边）。</summary>
    public static Panel Hairline(TextBox inner)
    {
        inner.Dock = DockStyle.Fill;
        inner.BorderStyle = BorderStyle.None;
        return HairlineCore(inner);
    }

    private static Panel HairlineCore(Control inner)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTokens.Line,
            Padding = new Padding(1),
        };
        panel.Controls.Add(inner);
        return panel;
    }

    /// <summary>
    /// 长内容区的可滚动宿主（DESIGN.md §6「长内容区」）：内容超出可视高度时出滚动条；
    /// 子项多为不接收焦点的标签，点击内容的任意处（含标签上）把焦点交给宿主，滚轮随后可滚动
    /// （任务详情、补充说明编辑区共用）。不是 Tab 停靠点，不改变 Tab 序。
    /// </summary>
    public static Panel ScrollHost() => new ScrollPanel();

    private sealed class ScrollPanel : Panel
    {
        public ScrollPanel()
        {
            AutoScroll = true;
            BackColor = UiTokens.Bg;
            // Panel 默认不可选中：不开 Selectable 就 Focus() 不到，滚轮事件到不了宿主。
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = false;
            // 内容盖住宿主可视面时，点击落在子控件上，宿主自己的 MouseDown 收不到——对子树代挂取焦。
            // e.Control 注解可空，事件源不保证非空。
            ControlAdded += (_, e) => { if (e.Control is not null) HookFocus(e.Control); };
        }

        private void HookFocus(Control control)
        {
            // 按钮与输入框点击自带焦点，滚轮事件也会沿父链传播到宿主，不代劳（复选框同属 ButtonBase）。
            if (control is ButtonBase or TextBoxBase) return;
            control.MouseDown += (_, _) => Focus();
            foreach (Control child in control.Controls) HookFocus(child);
            control.ControlAdded += (_, e) => { if (e.Control is not null) HookFocus(e.Control); };
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
        }
    }

    /// <summary>
    /// 列表（DESIGN.md §6「列表行」）：自绘选中态——选中行 Bg 底 + 左侧 3px Accent 标记，
    /// 不用系统高亮蓝；未选行 Panel 底；逐子项语义色（Muted/Ok/Warn/Bad）保留（ListRows 判定）。
    /// 表头沿用系统绘制（与既有外观一致）；复选框列由本工厂绘制（自绘模式下系统不再画）。
    /// Ctrl+C 复制焦点行的标识列原文也在这里装上——单元格自绘后文字不可选中，Ctrl+C 是列表里的
    /// 文字交出去的唯一通路（只读预览框原本就能选中复制），故剪贴板只在本工厂触达一次，页面与对话框不各装一套。
    /// 标识列默认首列（与 ListColumns 的「标识列（首列）」同一约定，绝大多数列表的名称格就在首列）；
    /// 首列不是名称的列表（写入清单首列是动作序号、差异确认首列是「接口/模型」分类）在出厂处声明真正的名称格，
    /// 取值与越界守卫都在 ListCopy，本工厂不另判一套。
    /// </summary>
    public static ListView List(bool checkBoxes = false, int identityColumn = ListCopy.DefaultIdentityColumn)
    {
        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            CheckBoxes = checkBoxes,
            OwnerDraw = true,
            BackColor = UiTokens.Panel,
            ForeColor = UiTokens.Text,
        };
        // 自绘逐行重绘需双缓冲，否则闪烁（Control.DoubleBuffered 为 protected）。
        typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(list, true);
        list.DrawColumnHeader += (_, e) => e.DrawDefault = true;
        list.DrawItem += DrawListRow;
        list.DrawSubItem += DrawListCell;
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.C || !ListCopy.IsCopyGesture(e.Shift, e.Control, e.Alt)) return;
            // 以焦点行为准（与 Enter 打开选中行同一纪律）：没指到行、或那一行没被标出来，都不动作。
            if (list.FocusedItem is not ListViewItem row) return;
            // 声明的标识列不在这行的格数之内时没有可交出去的一格：不动剪贴板，也不抄邻列的文字顶替。
            if (ListCopy.IdentityColumn(row.SubItems.Count, identityColumn) is not int column) return;
            if (ListCopy.CopyText(row.SubItems[column].Text, row.Selected) is not string text) return;
            e.SuppressKeyPress = true;
            try
            {
                Clipboard.SetText(text);
            }
            catch (ExternalException)
            {
                // 剪贴板被其他进程占用时本次没有复制：不重试、不弹窗，也不谎称已复制（粘贴即见实际内容）。
            }
        };
        return list;
    }

    /// <summary>
    /// 程序列表选中（唯一入口）：除 Selected 外同时恢复 Focused——列表重建后 FocusedItem 丢失，
    /// 只恢复选中时方向键从列表头部重新开始、详情跳到第一行；恢复焦点标记后键盘导航从选中项继续（Windows 列表惯例）。
    /// </summary>
    public static void SelectItem(ListViewItem item)
    {
        item.Selected = true;
        item.Focused = true;
    }

    /// <summary>
    /// 列表行激活（唯一入口）：双击只在命中行时回调该行。ListView.DoubleClick 不区分命中行，
    /// 双击空白区（首击已清空选中）同样触发——配置页此前会借这个空选中打开「新建配置」对话框；
    /// 命中哪行就激活哪行，双击空白区不动作。
    /// byEnter 为真时同一条回调也接列表的裸 Enter（Windows 列表「Enter 打开选中项」惯例）：
    /// 以焦点行为准、焦点行缺失即不动作，与双击的命中行守卫同一纪律——键盘入口不得借当前选中
    /// 做出与鼠标不同的事。手势判定取 ListKeys 单一口径，组合 Enter 不吞键。
    /// Enter 只按需在壳的页面接线、对话框不接：窗体设了 AcceptButton 时 Enter 在控件收到按键
    /// 之前就被窗体的对话键处理取走（§6「对话框 Enter=确认、Esc=取消」既有语义，本入口不改动它）。
    /// </summary>
    public static void ActivateItem(ListView list, Action<ListViewItem> activate, bool byEnter = false)
    {
        list.MouseDoubleClick += (_, e) =>
        {
            if (list.HitTest(e.Location).Item is not ListViewItem item) return;
            activate(item);
        };
        if (!byEnter) return;
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || !ListKeys.IsEnterGesture(e.Shift, e.Control, e.Alt)) return;
            if (list.FocusedItem is not ListViewItem item) return;
            e.SuppressKeyPress = true;
            activate(item);
        };
    }

    private static void DrawListRow(object? sender, DrawListViewItemEventArgs e)
    {
        // 整行底色与选中标记（标记每行画一次，画在行首）。
        e.Graphics.FillRectangle(e.Item.Selected ? UiBrushes.Bg : UiBrushes.Panel, e.Bounds);
        if (e.Item.Selected)
        {
            e.Graphics.FillRectangle(UiBrushes.Accent, e.Bounds.X, e.Bounds.Y, ListRows.MarkerWidth, e.Bounds.Height);
        }
    }

    private static void DrawListCell(object? sender, DrawListViewSubItemEventArgs e)
    {
        var list = (ListView)sender!;
        if (e.Item is null) return;
        var selected = e.Item.Selected;
        e.Graphics.FillRectangle(selected ? UiBrushes.Bg : UiBrushes.Panel, e.Bounds);

        var x = e.Bounds.X;
        if (e.ColumnIndex == 0)
        {
            if (selected) x += ListRows.MarkerWidth;
            if (list.CheckBoxes)
            {
                // 自绘模式下系统不再绘制复选框，这里按系统同位绘制（命中区仍在 ListView 布局的原状态图标位置）。
                var glyph = CheckBoxRenderer.GetGlyphSize(e.Graphics, System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal);
                var state = e.Item.Checked
                    ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal
                    : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal;
                CheckBoxRenderer.DrawCheckBox(
                    e.Graphics,
                    new Point(x + UiTokens.Xs, e.Bounds.Y + (e.Bounds.Height - glyph.Height) / 2),
                    state);
                x += UiTokens.Xs + glyph.Width + UiTokens.Xs;
            }
            else
            {
                x += UiTokens.Xs;
            }
        }
        else
        {
            x += UiTokens.Xs;
        }

        var text = e.ColumnIndex == 0 ? e.Item.Text : e.SubItem?.Text ?? "";
        var color = ListRows.TextColor(
            e.SubItem?.ForeColor ?? e.Item.ForeColor,
            e.Item.ForeColor,
            UiTokens.Text,
            SemanticColors);
        TextRenderer.DrawText(
            e.Graphics,
            text,
            e.SubItem?.Font ?? e.Item.Font,
            new Rectangle(x, e.Bounds.Y, Math.Max(0, e.Bounds.Right - x), e.Bounds.Height),
            color,
            TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    /// <summary>UiTokens 颜色的共享笔刷（进程内复用，句柄与进程同生命周期）。</summary>
    private static class UiBrushes
    {
        public static readonly SolidBrush Bg = new(UiTokens.Bg);
        public static readonly SolidBrush Panel = new(UiTokens.Panel);
        public static readonly SolidBrush Accent = new(UiTokens.Accent);
    }
}
