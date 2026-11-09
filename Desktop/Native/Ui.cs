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

    /// <summary>
    /// 两列字段网格（DESIGN.md §6「两列字段网格」，第一百二十三轮）：左列字段名固定宽、右列输入框吃剩余宽度，
    /// 列宽随网格<b>当前</b>宽度重算——配置编辑、新建任务与三个配置子对话框共用这一份形状。
    /// <para>
    /// 此前五处各拼一遍 <c>new TableLayoutPanel { Dock = Top, AutoSize = true, … }</c> 并把右列写成
    /// <c>SizeType.Percent, 100</c>：百分比列宽在网格还是默认 200 宽时算过一次就再没重算，
    /// 网格被停靠拉到 663 宽后右列仍是 <c>200 − 48 − 120 = 32px</c>，四个输入框窄到填不了字
    /// （第一百二十二轮在 wine 下量到真实控件边界，见 docs/NOTES.md）。现两列都按像素给，
    /// 宽度在尺寸变化时经 <see cref="ProfileGridColumns.ColumnWidths"/> 重算——列宽口径只有一份，
    /// 且能在 Linux 上逐值断言（<c>ProfileGridColumnsTests</c>）。
    /// </para>
    /// </summary>
    public static TableLayoutPanel FieldGrid(int labelWidth) => new FieldGridPanel(labelWidth);

    private sealed class FieldGridPanel : TableLayoutPanel
    {
        private readonly int _labelWidth;

        public FieldGridPanel(int labelWidth)
        {
            _labelWidth = labelWidth;
            Dock = DockStyle.Top;
            AutoSize = true;
            ColumnCount = 2;
            BackColor = UiTokens.Panel;
            Padding = new Padding(UiTokens.Xl, UiTokens.Lg, UiTokens.Xl, 0);
            // 构造时先按最小宽摆好（此时 Width 还是 TableLayoutPanel 的默认 200），
            // 真正的列宽由第一次尺寸变化重算——不在打开时一次性定死。
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ProfileGridColumns.MinFieldWidth));
            ApplyColumnWidths();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyColumnWidths();
        }

        /// <summary>
        /// 留白改了就重算列宽（第一百二十四轮）：可用宽度是「网格宽 − 左右留白」，
        /// 留白与列宽是同一笔账的两项——此前 <see cref="ApplyColumnWidths"/> 把留白写成
        /// <c>UiTokens.Xl × 2</c> 的字面，与构造里设的 <c>Padding</c> 靠「两处都写 Xl」这种
        /// 读代码看不出来的约定耦合：改 Padding 而忘了改那一处，列宽就会多算或少算一段，
        /// 网格右侧多一条缝或出横向滚动条。现在留白只从 <c>Padding.Horizontal</c> 取，
        /// 且 Padding 变化时也走同一次重算。
        /// </summary>
        protected override void OnPaddingChanged(EventArgs e)
        {
            base.OnPaddingChanged(e);
            ApplyColumnWidths();
        }

        /// <summary>
        /// 按网格当前宽度重算两列宽；宿主还没给出可用宽度时保持构造时的最小宽，等下一次尺寸变化。
        /// 左右留白取 <c>Padding.Horizontal</c>（唯一来源），不再另写一个数。
        /// </summary>
        private void ApplyColumnWidths()
        {
            var widths = ProfileGridColumns.ColumnWidths(Width, [_labelWidth, ProfileGridColumns.MinFieldWidth], Padding.Horizontal);
            if (widths[0] <= 0) return;
            if (Math.Abs(ColumnStyles[0].Width - widths[0]) < 0.5f && Math.Abs(ColumnStyles[1].Width - widths[1]) < 0.5f) return;
            ColumnStyles[0].Width = widths[0];
            ColumnStyles[1].Width = widths[1];
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
    /// 清单宿主（DESIGN.md §6「列表行」的「空态叠在清单之上」，第一百一十九轮）：
    /// 发丝边包住清单，空态面板叠在它前面——两个主页面的清单区共用这一份形状。
    /// <para>
    /// 顺序不是随手写的：发丝边面板与空态面板都是 <c>Dock = Fill</c>、占同一块矩形，
    /// 谁在上面只由 z 序决定，而 <c>Control.Controls</c> 的索引 0 才是 z 序最前
    /// （<c>BringToFront()</c> 即 <c>SetChildIndex(this, 0)</c>），后加入的控件默认排在后面。
    /// 项目设置页此前把发丝边面板加在空态面板之后，于是没有配置时内容区被发丝边面板
    /// （<c>BackColor = Line</c>）整块盖住：空态标题、说明与「新建配置」按钮都看不见，
    /// 只剩一片 <c>#E3E8F0</c>（第一百一十九轮在 wine 下实测复现，见 docs/NOTES.md）。
    /// 这里显式把空态提到最前，不依赖「谁先加入」这种读代码看不出来的约定。
    /// </para>
    /// </summary>
    public static Panel ListHost(ListView list, Panel empty)
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg };
        host.Controls.Add(Hairline(list));
        host.Controls.Add(empty);
        // 空态压在发丝边面板之上：两者同矩形，不显式提到最前就被发丝边整块盖住。
        empty.BringToFront();
        return host;
    }

    /// <summary>筛选行里关键词框的宽度（唯一落点）：五张长清单的框一样宽，不各写一个数。</summary>
    public const int FilterKeywordWidth = 220;

    /// <summary>
    /// 长清单的筛选行（DESIGN.md §6「列表筛选」）：筛选 | 下拉 | 关键词 | 关键词框 | 计数。
    /// 五张带筛选下拉的清单（检查结果、差异确认、注释缺口、注释候选、响应契约）共用这一份形状——
    /// 此前四处各拼一遍，第五张（检查结果）拼成了 FlowLayoutPanel + 240 宽 + Sm 间距：同一个筛选行
    /// 在五张清单上有两种样子（窄窗口下 Flow 把框裁掉，Table 让末列吸收剩余宽度），属同壳不同行为。
    /// 关键词框经 <see cref="Field"/> 包 1px 发丝边（§6「输入框」：放入界面时必须经 Field 包装），
    /// 宽度取 <see cref="FilterKeywordWidth"/>；<paramref name="counts"/> 为 null 时（检查结果的计数在表头行）
    /// 末列留空、由它吸收剩余宽度，行形状仍是同一份。
    /// </summary>
    public static TableLayoutPanel FilterRow(ComboBox? filter, TextBox keyword, Label? counts)
    {
        keyword.Width = FilterKeywordWidth;
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = UiTokens.Panel,
            ColumnCount = 5,
            Padding = new Padding(0, 0, 0, UiTokens.Sm),
            Margin = new Padding(0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // 没有筛选下拉的清单（同步预览的接口勾选页）传 null：前两列留空，关键词框仍在第 4 列、
        // 末列仍吸收剩余宽度——行形状仍是同一份，不为它另起一种排布。
        if (filter is not null)
        {
            var filterCaption = Body("筛选");
            filterCaption.Margin = new Padding(0, 0, UiTokens.Xs, 0);
            filterCaption.TextAlign = ContentAlignment.MiddleLeft;
            row.Controls.Add(filterCaption, 0, 0);
            row.Controls.Add(filter, 1, 0);
        }
        var keywordCaption = Body("关键词");
        keywordCaption.Margin = new Padding(UiTokens.Lg, 0, UiTokens.Xs, 0);
        keywordCaption.TextAlign = ContentAlignment.MiddleLeft;
        row.Controls.Add(keywordCaption, 2, 0);
        row.Controls.Add(Field(keyword), 3, 0);
        if (counts is not null)
        {
            counts.AutoSize = false;
            counts.Dock = DockStyle.Fill;
            counts.TextAlign = ContentAlignment.MiddleRight;
            row.Controls.Add(counts, 4, 0);
        }
        return row;
    }

    /// <summary>
    /// 只有关键词框、没有筛选下拉的清单（同步预览的接口勾选页）：同一个五列形状，前两列留空，
    /// 关键词框仍在第 4 列、末列仍吸收剩余宽度——不为它另起一种排布（第六十八轮那条纪律：
    /// 形状只在工厂里组装，界面交的是三件东西，不各拼一遍）。
    /// </summary>
    public static TableLayoutPanel FilterRow(TextBox keyword, Label? counts) => FilterRow(null, keyword, counts);

    /// <summary>
    /// 对话框里的「在列表中查找」（DESIGN.md §6「菜单/快捷键」）：Ctrl+F 把焦点交给本屏关键词框并选中已有关键词，
    /// 与两个主页面同一手势——手势只有一份登记（<see cref="ShellShortcuts.FindKey"/> 与
    /// <see cref="ShellShortcuts.IsFindGesture"/>），菜单显示、壳绑定与对话框判定同源，这里不再写一个 Keys.F。
    /// 只移焦点：不改筛选结果、不重建列表、不写数据；不是本手势的按键一律不接（返回 false，交回基类）。
    /// </summary>
    public static bool TryFocusFilter(Keys keyData, TextBox keyword)
    {
        var keyName = (keyData & Keys.KeyCode).ToString();
        if (!string.Equals(keyName, ShellShortcuts.FindKey, StringComparison.Ordinal)) return false;
        if (!ShellShortcuts.IsFindGesture((keyData & Keys.Shift) != 0, (keyData & Keys.Control) != 0, (keyData & Keys.Alt) != 0)) return false;
        // 停用的框不接这一次按键：焦点交不过去（Focus 对停用控件不生效），吞掉按键等于把「什么都没发生」
        // 说成做了什么（与 ListFilter.EscClears「空框里按 Esc 不接这一次按键」同一纪律）。
        // 停用的时机见 ListFilter.KeywordEnabled：整份数据不可得、作业运行期间。
        if (!keyword.Enabled) return false;
        keyword.Focus();
        keyword.SelectAll();
        return true;
    }

    /// <summary>
    /// 对话框筛选框里的裸 Enter（DESIGN.md §6「列表筛选」）：与两个主页面同一手势——输入完按 Enter 把焦点与选中
    /// 交给筛选结果第一行，键盘随后在清单上逐行看（此前对话框里这一步什么都不做：焦点留在框里，方向键动的是光标，
    /// 要看某一行只能回鼠标点清单；同一个手势在主页有、在对话框没有，属同壳不同行为）。
    /// 手势判定取 <see cref="ListKeys.IsEnterGesture"/> 单一口径（带 Ctrl/Alt/Shift 的组合不吞键、交回基类），
    /// 目标行与选中仍走 <see cref="FocusFirstResult"/> 一处——对话框不各写一套，也不自己滚动。
    /// 只移焦点与选中：不改筛选关键词、不重建清单、不写数据；不是本手势的按键返回 false。
    /// 对话框的 Enter=确认（表单字段）不受影响：本入口只在关键词框取得焦点时接住这一次按键，而筛选框不是表单字段。
    /// </summary>
    public static bool TryEnterFilter(Keys keyData, TextBox keyword, ListView list)
    {
        if (!keyword.Focused) return false;
        if ((keyData & Keys.KeyCode) != Keys.Enter) return false;
        if (!ListKeys.IsEnterGesture((keyData & Keys.Shift) != 0, (keyData & Keys.Control) != 0, (keyData & Keys.Alt) != 0)) return false;
        FocusFirstResult(list);
        return true;
    }

    /// <summary>
    /// 对话框筛选框里的 Esc（DESIGN.md §6「列表筛选」那条全键盘路径的退出一步）：与两个主页面同一手势——
    /// Ctrl+F 到框、输入、Enter 到结果第一行、方向键逐行看，想回到全部时按 Esc 清空关键词。
    /// 此前对话框里这一步是「取消对话框」：刚敲的关键词与审阅位置一起丢掉，要看全部只能重开一次。
    /// 判定取 <see cref="ListFilter.EscClearsInDialog"/> 单一口径（焦点在框上、框里有字才接），
    /// 清空只经 keyword.Clear() 一处——重建仍由各对话框既有的 TextChanged 触发，本入口不重建清单、
    /// 不写数据、不碰选中（选中按各视图既有口径恢复）；不是本手势的按键返回 false，交回基类，
    /// 对话框的 Esc=取消因此逐字不变（焦点不在框上时它仍是取消）。
    /// </summary>
    public static bool TryEscapeFilter(Keys keyData, TextBox keyword)
    {
        if ((keyData & Keys.KeyCode) != Keys.Escape) return false;
        if (!ListFilter.EscClearsInDialog(keyword.Focused, ListFilter.IsActive(keyword.Text))) return false;
        keyword.Clear();
        return true;
    }

    /// <summary>
    /// 筛选框 Enter 的去向（DESIGN.md §6「列表筛选」）：焦点与选中交给筛选结果第一行——显示序第一行，
    /// 即筛选与排序之后的可见顺序；零行不动作（那一步的下一步是空态的「清空筛选」按钮，不把焦点丢进空列表）。
    /// 目标行判定在 <see cref="ListKeys.FirstResultIndex"/>，选中仍经 <see cref="SelectItem"/> 唯一入口
    /// （与鼠标点行、重建恢复同一条路，滚动也随该入口一并触达）。两个主页面与五张长清单对话框共用这一份，
    /// 不各写一套——此前两页各有一份同样的四行，对话框再各写一份就是七份。
    /// </summary>
    public static void FocusFirstResult(ListView list)
    {
        if (ListKeys.FirstResultIndex(list.Items.Count) is not int index) return;
        list.Focus();
        SelectItem(list.Items[index]);
    }

    /// <summary>
    /// 长内容区的可滚动宿主（DESIGN.md §6「长内容区」）：内容超出可视高度时出滚动条；
    /// 子项多为不接收焦点的标签，点击内容的任意处（含标签上）把焦点交给宿主，滚轮随后可滚动
    /// （任务详情、补充说明编辑区共用）。不是 Tab 停靠点，不改变 Tab 序。
    /// <para>
    /// <b>宿主自己就是「填满剩余空间」的那一块</b>（<c>Dock = Fill</c>，第一百二十轮）：
    /// 此前工厂只设 <c>AutoScroll</c>、不设 <c>Dock</c>，而 <c>Panel</c> 的默认尺寸是 200×100——
    /// 于是每一个「长内容区」都变成左上角一块 200×100 的小框：配置编辑对话框里
    /// 第二列（<c>SizeType.Percent</c>）只剩 200 − 120（标签列）− 48（左右留白）− 17（滚动条）≈ 15px，
    /// 输入框窄到无法填写，其余客户区一片空白（真机故障报告：输入框约 15px、滚动区约 100px 高）；
    /// 任务详情同样被压成 200×100，九个分区与全部同步/报告动作挤在一条缝里。
    /// 停靠方向由工厂一处决定，界面代码不得再各自设 <c>Dock</c>（机械检查钉住）。
    /// </para>
    /// </summary>
    public static Panel ScrollHost() => new ScrollPanel();

    private sealed class ScrollPanel : Panel
    {
        public ScrollPanel()
        {
            // 填满父容器的剩余空间：停靠按集合倒序生效，先加入的宿主最后停靠，
            // 因此底部按钮行与状态行先占位、宿主拿剩下的全部（与各对话框的加入顺序配套）。
            Dock = DockStyle.Fill;
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
    /// 表头沿用系统绘制（与既有外观一致）；复选框列由本工厂绘制（自绘模式下系统不再画），
    /// 勾选目标也由本工厂放大到整行（点行的任意处即勾选，落点判定在 ListChecks——系统自己会翻的那一格让开）。
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
        if (checkBoxes)
        {
            // 整行点击即勾选：自绘之后系统只认状态图标那一小格命中区，而勾选框是这个工厂画出来的——
            // 不接这一步，「勾选」的目标就从整行缩成十几像素。判定在 ListChecks，本工厂不自己认落点；
            // 系统会翻的那一格让开，一次点击因此只翻一次。
            list.MouseDown += (_, e) =>
            {
                var hit = list.HitTest(e.Location);
                if (hit.Item is not ListViewItem row) return;
                if (!ListChecks.ShouldToggle(e.Button == MouseButtons.Left, true, hit.Location == ListViewHitTestLocations.StateImage)) return;
                row.Checked = !row.Checked;
            };
        }
        return list;
    }

    /// <summary>
    /// 读当前选中的那一行（唯一入口）：位次先经 <see cref="ListSelection"/> 量过选中数，量不到就没有这一行——
    /// 索引器因此在无选中时根本不被触发。把「按钮按下去落在哪一行」交给索引器的越界行为是不可交付的：
    /// 它要么是那一次点击变成未处理异常，要么是第四十轮修过的「按钮残留启用、点击无响应」同一族缺陷，
    /// 而本环境没有原生运行时、也没有网络可核对官方源码，两者都无法验证（界面上此前有 20 处显式量数、4 处赌返回值）。
    /// 与 <see cref="SelectItem"/> 是一对：写选中走那一个入口，读选中走这一个。
    /// </summary>
    public static ListViewItem? SelectedItem(ListView list) =>
        ListSelection.FirstPosition(list.SelectedItems.Count) is { } position ? list.SelectedItems[position] : null;

    /// <summary>
    /// 读当前选中行的显示序号（唯一入口）：与 <see cref="SelectedItem"/> 同一份 <see cref="ListSelection"/> 判定、
    /// 取的是同一行——序号是选中集合第一格指向的显示序号，不是选中数。没有选中返回 null，
    /// 调用方按各自口径决定缺省（续接用 <see cref="ListAdvance.None"/>，即「没有刚处理的一条」）。
    /// </summary>
    public static int? SelectedIndex(ListView list) =>
        ListSelection.FirstPosition(list.SelectedIndices.Count) is { } position ? list.SelectedIndices[position] : null;

    /// <summary>
    /// 程序列表选中（唯一入口）：除 Selected 外同时恢复 Focused——列表重建后 FocusedItem 丢失，
    /// 只恢复选中时方向键从列表头部重新开始、详情跳到第一行；恢复焦点标记后键盘导航从选中项继续（Windows 列表惯例）。
    /// 选中即定位：再把该行滚入可见区——清单重建（刷新、切页、筛选、保存对话框返回、处理完一条续接下一条）
    /// 会把滚动位置甩回顶部，落在屏外的选中不是定位：详情与按钮跟着那一行走，屏幕上却看不出自己在哪。
    /// 滚动只在这一处触达（第五十一轮在两页各写一次、第五十六轮挂账的「不滚动到落点所在」由此收口），
    /// 是否请求滚动由 <see cref="ListReveal"/> 判定：行不在这份清单之内就不动，越界序号在控件那边是先抛异常。
    /// </summary>
    public static void SelectItem(ListViewItem item)
    {
        item.Selected = true;
        item.Focused = true;
        var list = item.ListView;
        if (list is null) return;
        var index = item.Index;
        if (!ListReveal.ShouldReveal(index, list.Items.Count)) return;
        list.EnsureVisible(index);
    }

    /// <summary>
    /// 队列续接（唯一落点）：刚处理完的那一行已不属于当前筛选、从清单上消失时，把选中落到接替他位置的
    /// 下一行（末行落到上一行，落点由 <see cref="ListAdvance"/> 单一口径给出），仍经 <see cref="SelectItem"/>
    /// 唯一入口——详情与按钮随选中事件跟上，方向键从该行继续，那一行也随本入口一并滚入可见区（本处不另请求滚动）。
    /// 一行不剩时不动作，交由各视图既有的空态出口呈现。
    /// </summary>
    public static void AdvanceAfterRemoval(ListView list, int handledIndex)
    {
        var index = ListAdvance.IndexAfterRemoval(handledIndex, list.Items.Count);
        if (index < 0) return;
        SelectItem(list.Items[index]);
    }

    /// <summary>
    /// 「接受并下一条」的落点（唯一触达点，第一百一十一轮）：接受一条候选之后选中交给<b>下一条</b>——
    /// 即使这一条仍在当前筛选里（停在「全部候选」上时它不会消失），也不留在原处。
    /// 落点由 <see cref="ListAdvance.NextAfterAccept"/> 单一口径给出（还在清单上就落到它的下一行、
    /// 本来在末行则留在末行；已不在当前筛选上则落到接替他位置的那一行），
    /// 选中仍经 <see cref="SelectItem"/> 唯一入口——详情与按钮随选中事件跟上，方向键从该行继续，
    /// 那一行也随本入口一并滚入可见区（本处不另请求滚动）。
    /// <paramref name="newIndex"/> 是这一条在重建后清单里的显示序号，已不在清单上时传 <see cref="ListAdvance.None"/>。
    /// 一行不剩时不动作，交由各视图既有的空态出口呈现。
    /// </summary>
    public static void AdvanceAfterAccept(ListView list, int handledIndex, int newIndex)
    {
        var index = ListAdvance.NextAfterAccept(handledIndex, newIndex, list.Items.Count);
        if (index < 0) return;
        SelectItem(list.Items[index]);
    }

    /// <summary>
    /// 打开这一屏时的落点（唯一入口，第九十轮）：清单里有行就选中第一条，一行都没有就不动作——
    /// 落点由 <see cref="ListSelection.FirstRowOnOpen"/> 判定（有行才落，不索引不存在的第一行），
    /// 选中仍经 <see cref="SelectItem"/> 唯一入口（Selected + Focused + 滚入可见区，与鼠标点同一行同一条路，
    /// 详情与审阅按钮随选中事件跟上）。
    /// 只落选中、不取键盘：打开时把键盘交给清单是另一件事，接在窗体首次显示之后（<see cref="KeepKeyboardOnQueue"/>），
    /// 那时窗体还没显示、Focus 的落点取决于框架在激活时怎么选初始焦点（第八十八、八十九轮同一划界）。
    /// 也不重建清单、不写数据——调用方在数据读进来、清单重建之后调一次。
    /// </summary>
    public static void SelectFirstOnOpen(ListView list)
    {
        if (ListSelection.FirstRowOnOpen(list.Items.Count) is not int index) return;
        SelectItem(list.Items[index]);
    }

    /// <summary>
    /// 把键盘交给清单（唯一触达点），两个时刻共用这一处：
    /// <list type="bullet">
    /// <item>审阅完成后交回（第五十八轮）：点「接受」「保存」时焦点落在那个按钮上，
    /// 而 <see cref="SelectItem"/> 落的 Selected/Focused 与滚动都是清单内部的事——控件没拿到键盘时
    /// 方向键动的是按钮所在的控件，第三十三轮「方向键从选中项继续」、第五十六轮续接落点、第五十七轮选中即定位
    /// 在鼠标完成的那条路上都不成立（两页在第五十一轮正是为此先让清单取焦点再选中）。</item>
    /// <item>只读清单打开时交给它（第八十八轮）：对话框打开时键盘落在框架 tab 序的第一个可聚焦控件上，
    /// 而这一族屏打开即选中一行（详情区已经显示那一行），键盘却不在清单上——方向键动的是别的控件，
    /// Ctrl+C 也复制不到那一行，「打开就有一行被标出来」只成立了一半。名字里的 keep 说的是同一件事：
    /// 键盘留在清单上，不交给按钮、也不交给框架的默认落点。</item>
    /// </list>
    /// 是否交由 <see cref="ListFocus"/> 判定：选中行与焦点行是同一行才交，否则不动键盘——
    /// 队列清完（没有落点）、打开时那一屏是提示或空态（没有选中行）时的下一步由各视图既有的空态与提示出口承担。
    /// 保存被拒的路径不调用这里：用户要改的就是手里那一格（待确认问题的回答、说明冲突的选择），
    /// 把键盘交走等于把下一步从他手里拿走。
    /// </summary>
    public static void KeepKeyboardOnQueue(ListView list)
    {
        var selected = SelectedIndex(list);
        if (!ListFocus.ShouldHandToQueue(selected, list.FocusedItem?.Index)) return;
        list.Focus();
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
