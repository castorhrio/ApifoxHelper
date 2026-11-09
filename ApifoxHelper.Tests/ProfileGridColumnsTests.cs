using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 两列字段网格的列宽断言（DESIGN.md §6「两列字段网格」，第一百二十三轮）。
/// <para>
/// 缺陷本体：配置编辑对话框的右列（输入框）此前是 <c>SizeType.Percent, 100</c>，而网格是
/// <c>Dock = Top + AutoSize</c> 放进 <c>AutoScroll</c> 宿主——百分比列宽在网格还是默认 200 宽时
/// 算过一次就再没重算，网格被停靠拉到 663 宽后右列仍是 <c>200 − 48 − 120 = 32px</c>，
/// 名称 / 允许回写范围 / 版本备注 / Swagger 位置四个输入框窄到填不了字（第一百二十二轮在 wine 下
/// 量到真实控件边界）。本文件把「右列不得小于可读下限」翻译成可执行断言：
/// 列宽口径 <see cref="ApifoxHelper.Desktop.Native.ProfileGridColumns"/> 经共享源码编入测试程序集，
/// 这里跑的是主工程同一份代码；界面接线（工厂与五处调用点）以源码切片与行首正则钉住。
/// </para>
/// </summary>
public class ProfileGridColumnsTests
{
    private const int Pad = 48; // UiTokens.Xl × 2（网格左右留白），由 Ui.FieldGrid 传入

    private static int[] Widths(int totalWidth) =>
        ApifoxHelper.Desktop.Native.ProfileGridColumns.ColumnWidths(
            totalWidth,
            [ApifoxHelper.Desktop.Native.ProfileGridColumns.LabelWidth, ApifoxHelper.Desktop.Native.ProfileGridColumns.MinFieldWidth],
            Pad);

    // ── 逐值断言：够宽时右列吃掉剩余宽度 ──────────────────────────────────

    [Fact]
    public void WideEnough_FieldColumnTakesTheRest()
    {
        // 第一百二十二轮实测的网格宽度 663（对话框 680 减纵向滚动条 17）：右列应为 663 − 48 − 120 = 495。
        Assert.Equal([120, 495], Widths(663));
        // 对话框未出滚动条时的 680：右列 512（与第一百二十一轮「第二列应有 512px」同一算术）。
        Assert.Equal([120, 512], Widths(680));
        // 更宽的窗口：余量继续全给右列，标签列不跟着长。
        Assert.Equal([120, 852], Widths(1020));
    }

    [Fact]
    public void NarrowWindow_KeepsTheFieldColumnAtTheFloor()
    {
        // 恰好够放下两列最小宽：右列正好是下限 96，不被压到 96 以下。
        Assert.Equal([120, 96], Widths(120 + 96 + Pad));
        // 再宽一点，多出来的全给右列。
        Assert.Equal([120, 136], Widths(120 + 96 + Pad + 40));
    }

    [Fact]
    public void FieldColumnNeverBelowTheFloorWhenThereIsRoom()
    {
        // 把「塌陷」翻译成可执行断言：只要可用宽度容得下两列最小宽，右列就不得小于 96。
        // 这里写死 96 而不是引用常量——引用常量的话「把下限改成 0」这种变异不会变红，
        // 常量与文档值的一致性由 Constants_AreTheDocumentedValues 单独钉住。
        for (var total = 120 + 96 + Pad; total <= 1400; total++)
        {
            var widths = Widths(total);
            Assert.True(widths[1] >= 96, $"网格宽 {total} 时右列只有 {widths[1]}px，低于可读下限 96px。");
        }
    }

    // ── 逐值断言：不够宽时按比例收缩，不归零 ──────────────────────────────

    [Fact]
    public void ExtremelyNarrow_ShrinksProportionally()
    {
        // 可用宽度 108（网格 156）：120:96 按比例收缩成 60:48，两列都还在。
        Assert.Equal([60, 48], Widths(156));
        // 可用宽度 100：38 + 31 + 31 的同一算法在两列上是 56:44（余数按小数部分补）。
        Assert.Equal([56, 44], Widths(148));
    }

    [Fact]
    public void ShrinkKeepsTheRatio()
    {
        // 收缩比例一致：两列宽之比始终等于最小宽之比（整数取整的误差不超过一个最小宽单位）。
        for (var usable = 2; usable < 120 + 96; usable++)
        {
            var widths = Widths(usable + Pad);
            var drift = Math.Abs(widths[0] * 96 - widths[1] * 120);
            Assert.True(drift <= 120, $"可用宽度 {usable} 时列宽 {widths[0]}:{widths[1]} 偏离 120:96（偏差 {drift}）。");
        }
    }

    [Fact]
    public void ShrinkNeverProducesAZeroColumn()
    {
        // 可用宽度至少能每列 1px 时，任何一列都不得是 0——「按比例归零」正是要防的写法。
        for (var usable = 2; usable <= 400; usable++)
        {
            var widths = Widths(usable + Pad);
            Assert.True(widths[0] >= 1 && widths[1] >= 1, $"可用宽度 {usable} 时出现 0 列：{widths[0]}:{widths[1]}。");
        }
    }

    [Fact]
    public void SkewedMinimums_StillGetEveryColumnAPixel()
    {
        // 最小宽悬殊时（1000 : 1）按比例取整会把窄列算成 0——兜底从最宽的一列挪一像素过来，
        // 「不出现 0 列」不是靠比例恰好凑巧成立。
        var widths = ApifoxHelper.Desktop.Native.ProfileGridColumns.ColumnWidths(2 + Pad, [1000, 1], Pad);
        Assert.Equal([1, 1], widths);
    }

    [Fact]
    public void ColumnsAlwaysFillTheUsableWidth()
    {
        // 各列之和恰等于可用宽度：少一像素网格右侧就多一条缝，多一像素就出横向滚动条。
        for (var usable = 1; usable <= 1400; usable++)
        {
            var widths = Widths(usable + Pad);
            Assert.Equal(usable, widths[0] + widths[1]);
        }
    }

    // ── 收缩分支的有界性（第一百二十五轮，防缺陷 2）──────────────────────

    [Fact]
    public void RemainderLoop_IsBoundedByTheColumnCount()
    {
        // 补余数的循环此前写成 `for (var k = 0; assigned < usable; k = (k + 1) % count)`：
        // 终止只靠 assigned 递增，读代码看不出上界，输入偏斜时也无法从形状上排除多转。
        // 现在循环条件里带 `k < count`——整数除法每列丢的余数都小于 1px，合计必然小于列数，
        // 走一遍就够；形状本身钉住「迭代次数 ≤ 列数」。
        var rule = Repo.Source("Desktop", "Native", "ProfileGridColumns.cs");
        Assert.Contains("for (var k = 0; k < count && assigned < usable; k++)", rule);
        // 环形写法不得回归（`% count` 的取模步进正是无上界的来源）。
        Assert.DoesNotMatch(@"k = \(k \+ 1\) % count", rule);
        // 兜底：万一上界不成立，差额全给最后一列，各列之和仍等于可用宽度。
        Assert.Contains("if (assigned < usable) widths[count - 1] += usable - assigned;", rule);
    }

    [Fact]
    public void ExtremeSkew_StillFillsTheUsableWidth()
    {
        // 极端偏斜的最小宽（1000:1、1:1000、int.MaxValue 对 1）在收缩分支里最容易把
        // 「各列之和恰等于可用宽度」算丢：这里逐值跑一遍，和必须精确等于可用宽度，
        // 且可用宽度容得下每列 1px 时不得出现 0 列。
        int[][] skews =
        [
            [1, 1_000_000],
            [1_000_000, 1],
            [int.MaxValue, 1],
            [1, int.MaxValue],
            [int.MaxValue, int.MaxValue],
            [int.MaxValue, int.MaxValue, 3],
            [1, 1, 1_000_000],
        ];
        foreach (var minimums in skews)
        {
            for (var usable = 1; usable <= 600; usable++)
            {
                var widths = ApifoxHelper.Desktop.Native.ProfileGridColumns.ColumnWidths(usable + Pad, minimums, Pad);
                Assert.Equal(usable, widths.Sum());
                Assert.All(widths, w => Assert.True(w >= 0, $"最小宽 [{string.Join(",", minimums)}]、可用宽度 {usable} 时出现负列宽。"));
                if (usable >= minimums.Length)
                {
                    Assert.All(widths, w => Assert.True(w >= 1, $"最小宽 [{string.Join(",", minimums)}]、可用宽度 {usable} 时出现 0 列。"));
                }
            }
        }
    }

    [Fact]
    public void HugeMinimums_DoNotOverflowTheSum()
    {
        // 最小宽合计在 int 上会溢出成负数（int.MaxValue + int.MaxValue = −2），
        // 于是「没有最小宽约束」那条分支被误判命中、各列之和不再等于可用宽度。
        // 合计改用 long 之后，极端输入仍走收缩分支、和仍精确。
        var widths = ApifoxHelper.Desktop.Native.ProfileGridColumns.ColumnWidths(663, [int.MaxValue, int.MaxValue], Pad);
        Assert.Equal(615, widths.Sum());
        Assert.All(widths, w => Assert.True(w >= 1));
        // 三列同理（int.MaxValue + int.MaxValue + 3 在 int 上是 1，会误判成「合计只有 1」）。
        var three = ApifoxHelper.Desktop.Native.ProfileGridColumns.ColumnWidths(663, [int.MaxValue, int.MaxValue, 3], Pad);
        Assert.Equal(615, three.Sum());
        Assert.All(three, w => Assert.True(w >= 1));
    }

    [Fact]
    public void NoUsableWidth_ReturnsZeros()
    {
        // 宿主还没布局（网格仍是默认 200 宽之前、或宽度小于留白）：全 0，调用方据此跳过这一次重算。
        Assert.Equal([0, 0], Widths(Pad));
        Assert.Equal([0, 0], Widths(Pad - 8));
        Assert.Equal([0, 0], Widths(0));
        Assert.Equal([0, 0], Widths(-100));
    }

    // ── 通用口径：三列与退化输入 ──────────────────────────────────────────

    [Fact]
    public void ThreeColumns_LastTakesTheSlack()
    {
        var widths = ApifoxHelper.Desktop.Native.ProfileGridColumns.ColumnWidths(663, [120, 96, 96], Pad);
        Assert.Equal([120, 96, 399], widths);
    }

    [Fact]
    public void ThreeColumns_ShrinkKeepsEveryColumn()
    {
        var widths = ApifoxHelper.Desktop.Native.ProfileGridColumns.ColumnWidths(148, [120, 96, 96], Pad);
        Assert.Equal(100, widths.Sum());
        Assert.All(widths, w => Assert.True(w >= 1));
    }

    [Fact]
    public void DegenerateInputs_DoNotThrow()
    {
        Assert.Empty(ApifoxHelper.Desktop.Native.ProfileGridColumns.ColumnWidths(663, [], Pad));
        // 没有最小宽约束：全部给最后一列（与「字段列吃剩余宽度」同一口径）。
        Assert.Equal([0, 615], ApifoxHelper.Desktop.Native.ProfileGridColumns.ColumnWidths(663, [0, 0], Pad));
        // 负的留白按 0 处理（不把可用宽度算小），可用宽度即网格宽度。
        Assert.Equal([120, 543], ApifoxHelper.Desktop.Native.ProfileGridColumns.ColumnWidths(663, [120, 96], -48));
    }

    [Fact]
    public void Constants_AreTheDocumentedValues()
    {
        // 三个常量是界面与文档共用的口径：改值必须同一次改动更新 DESIGN.md §6 与本断言。
        Assert.Equal(120, ApifoxHelper.Desktop.Native.ProfileGridColumns.LabelWidth);
        Assert.Equal(110, ApifoxHelper.Desktop.Native.ProfileGridColumns.NarrowLabelWidth);
        Assert.Equal(96, ApifoxHelper.Desktop.Native.ProfileGridColumns.MinFieldWidth);
        // 字段控件自身的最小宽（第一百二十四轮从三处字面 480 收进来）。
        Assert.Equal(480, ApifoxHelper.Desktop.Native.ProfileGridColumns.MinFieldBoxWidth);
    }

    // ── 留白与列宽是同一笔账（第一百二十四轮）────────────────────────────

    [Fact]
    public void PadChange_MovesTheColumnWidths()
    {
        // 可用宽度 = 网格宽 − 左右留白：留白多 16px，右列就少 16px（左列不动）。
        // 这条断言钉的是「留白与列宽同源」这件事本身——界面那边把留白从 Padding.Horizontal 取，
        // 改 Padding 就必然改列宽，不会出现「Padding 改了、列宽还按旧留白算」的缝。
        var narrow = ApifoxHelper.Desktop.Native.ProfileGridColumns.ColumnWidths(663, [120, 96], 48);
        var wide = ApifoxHelper.Desktop.Native.ProfileGridColumns.ColumnWidths(663, [120, 96], 64);
        Assert.Equal([120, 495], narrow);
        Assert.Equal([120, 479], wide);
        Assert.Equal(narrow[1] - 16, wide[1]);
        // 留白大到吃掉全部可用宽度时全 0（调用方据此跳过这一次重算），不出现负数。
        Assert.Equal([0, 0], ApifoxHelper.Desktop.Native.ProfileGridColumns.ColumnWidths(663, [120, 96], 663));
    }

    [Fact]
    public void FieldGridPanel_TakesThePadFromItsOwnPadding()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var start = ui.IndexOf("private sealed class FieldGridPanel", StringComparison.Ordinal);
        Assert.True(start >= 0, "Ui.cs 缺少 FieldGridPanel（两列字段网格的唯一形状落点）。");
        var rest = ui[start..];
        var end = rest.IndexOf("\n    }\n", StringComparison.Ordinal);
        var panel = end < 0 ? rest : rest[..end];

        // 留白唯一来源：列宽计算取 Padding.Horizontal，不再另写 UiTokens.Xl × 2 的字面。
        Assert.Contains("ProfileGridColumns.ColumnWidths(Width, [_labelWidth, ProfileGridColumns.MinFieldWidth], Padding.Horizontal)", panel);
        Assert.DoesNotContain("UiTokens.Xl * 2", panel);
        // Padding 变化也走同一次重算：改 Padding 不会算错列宽。
        Assert.Matches(@"(?m)^\s*protected override void OnPaddingChanged\(EventArgs e\)", panel);
        var paddingHandler = panel[panel.IndexOf("OnPaddingChanged", StringComparison.Ordinal)..];
        Assert.Contains("ApplyColumnWidths();", paddingHandler[..paddingHandler.IndexOf("}", StringComparison.Ordinal)]);
    }

    [Fact]
    public void FieldGridPanel_BuildsColumnsBeforePadding()
    {
        // 第一百二十八轮（wine 实测的真实崩溃）：ColumnCount = 2 只声明列数、不建 ColumnStyle，
        // 而 set_Padding 会同步触发 OnPaddingChanged → ApplyColumnWidths，此时 ColumnStyles 还是空的，
        // 索引 [0] 抛 ArgumentOutOfRangeException——新建配置对话框一打开就崩（界面线程未处理异常，
        // 对话框根本不出现，前几轮 wine 里「对话框没出现」的真因）。
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var start = ui.IndexOf("private sealed class FieldGridPanel", StringComparison.Ordinal);
        Assert.True(start >= 0, "Ui.cs 缺少 FieldGridPanel（两列字段网格的唯一形状落点）。");
        var rest = ui[start..];
        var end = rest.IndexOf("\n    }\n", StringComparison.Ordinal);
        var panel = end < 0 ? rest : rest[..end];

        // 构造顺序：两列样式必须先于 Padding 落地（Padding 的 setter 会立刻重算列宽）。
        var firstColumnStyle = panel.IndexOf("ColumnStyles.Add(", StringComparison.Ordinal);
        var padding = panel.IndexOf("Padding = new Padding(", StringComparison.Ordinal);
        Assert.True(firstColumnStyle >= 0, "构造里没有建列样式。");
        Assert.True(padding >= 0, "构造里没有设 Padding。");
        Assert.True(firstColumnStyle < padding, "列样式必须先于 Padding 落地，否则构造期重算会索引空集合。");

        // 事件顺序不承担正确性：列样式没建好时重算直接跳过（构造末尾与首次尺寸变化都会再走一次）。
        Assert.Contains("if (ColumnStyles.Count < 2) return;", panel);
        var guard = panel.IndexOf("if (ColumnStyles.Count < 2) return;", StringComparison.Ordinal);
        var firstIndex = panel.IndexOf("ColumnStyles[0]", StringComparison.Ordinal);
        Assert.True(guard < firstIndex, "空集合守卫必须在任何 ColumnStyles[下标] 之前。");
    }

    // ── 界面接线：形状唯一落点 ────────────────────────────────────────────

    [Fact]
    public void Ui_FieldGrid_RecomputesOnSizeChange()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Contains("public static TableLayoutPanel FieldGrid(int labelWidth) => new FieldGridPanel(labelWidth);", ui);

        var start = ui.IndexOf("private sealed class FieldGridPanel", StringComparison.Ordinal);
        Assert.True(start >= 0, "Ui.cs 缺少 FieldGridPanel（两列字段网格的唯一形状落点）。");
        // 切到本类结束（类体收尾是 4 空格缩进的右花括号，方法体收尾是 8 空格）。
        var rest = ui[start..];
        var end = rest.IndexOf("\n    }\n", StringComparison.Ordinal);
        var panel = end < 0 ? rest : rest[..end];

        // 形状：停靠顶部 + 自动高度（滚动范围来自内容自身高度，Dock=Fill 会退回不可滚动）。
        Assert.Matches(@"(?m)^\s*Dock = DockStyle\.Top;", panel);
        Assert.Matches(@"(?m)^\s*AutoSize = true;", panel);
        Assert.Matches(@"(?m)^\s*ColumnCount = 2;", panel);
        // 列宽按当前宽度重算：尺寸变化时调一次，构造时也调一次（先按最小宽摆好）。
        Assert.Matches(@"(?m)^\s*protected override void OnSizeChanged\(EventArgs e\)", panel);
        // 四处调用：构造一次（先按最小宽摆好）、尺寸变化一次、留白变化一次（第一百二十四轮）、
        // 加控件一次（第一百三十轮：新加进字段列的控件立刻按当前列宽摆好，不靠事件顺序）。
        Assert.Equal(4, Regex.Matches(panel, @"(?m)^\s*ApplyColumnWidths\(\);").Count);
        Assert.Contains("ProfileGridColumns.ColumnWidths(Width,", panel);
        // 百分比列正是缺陷本体：工厂里不得再出现。
        Assert.DoesNotMatch(@"SizeType\.Percent", panel);
    }

    [Fact]
    public void Dialogs_UseTheFactoryOnly()
    {
        // 五处两列字段网格（配置编辑、新建任务、三个配置子对话框）一律经工厂出厂，
        // 界面代码不得自己拼网格、不得自己设列宽（形状与列宽口径各只有一份）。
        var expected = new (string File, string LabelWidth)[]
        {
            ("ProjectsPage.cs", "ProfileGridColumns.LabelWidth"),
            ("TasksPage.cs", "ProfileGridColumns.NarrowLabelWidth"),
            ("GenerationDialog.cs", "ProfileGridColumns.LabelWidth"),
            ("ProfileAdvancedDialog.cs", "ProfileGridColumns.LabelWidth"),
            ("ProfileAnalysisDialog.cs", "ProfileGridColumns.LabelWidth"),
        };
        foreach (var (file, labelWidth) in expected)
        {
            var source = Repo.Source("Desktop", "Native", file);
            Assert.Contains($"var grid = Ui.FieldGrid({labelWidth});", source);
            // 旧写法（自己给两列设宽）不得回归：标签列 120/110 的绝对宽只允许出现在工厂里。
            Assert.DoesNotMatch(@"(?m)^\s*grid\.ColumnStyles\.Add\(new ColumnStyle\(SizeType\.Absolute, (120|110)\)\);", source);
            Assert.DoesNotMatch(@"new TableLayoutPanel \{ Dock = DockStyle\.Top, AutoSize = true, ColumnCount = 2, BackColor = UiTokens\.Panel", source);
        }
    }

    [Fact]
    public void FieldBoxMinimum_LivesOnlyInTheSharedEntry()
    {
        // 第一百二十四轮：三个配置子对话框的 AddRow 里各写一遍 Math.Max(field.Width, 480)，
        // 同一个数三处字面重复。第一百三十轮把「字段控件多宽」整件事收进网格（Ui.FieldGridPanel
        // 调 ProfileGridColumns.FieldWidth），界面代码不再自己设字段宽度——所以这里钉的是
        // 「五处字段网格的宿主文件里一个字段宽度都不许写」。
        var dialogs = new[]
        {
            "ProjectsPage.cs",
            "TasksPage.cs",
            "GenerationDialog.cs",
            "ProfileAdvancedDialog.cs",
            "ProfileAnalysisDialog.cs",
        };
        foreach (var file in dialogs)
        {
            var source = Repo.Source("Desktop", "Native", file);
            // 界面代码不出现字面 480（这个数只属于 ProfileGridColumns）。
            Assert.DoesNotMatch(@"\b480\b", source);
            // 字段加宽只经共享口径：不得再出现「Math.Max(field.Width, 数字)」。
            Assert.DoesNotMatch(@"Math\.Max\(field\.Width, \d", source);
            // 也不得再出现「某个字段控件就地设宽」（第一百三十轮收口的 420 / 320 / 380 三处）。
            Assert.DoesNotMatch(@"(?m)^\s*_(name|allowed|swaggerType|swaggerLocation|apiProject)\.Width = \d+;", source);
        }
        // 常量本身只在一处定义。
        var rule = Repo.Source("Desktop", "Native", "ProfileGridColumns.cs");
        Assert.Contains("public const int MinFieldBoxWidth = 480;", rule);
        var others = Directory.GetFiles(Path.Combine(Repo.Root, "Desktop", "Native"), "*.cs")
            .Where(f => Path.GetFileName(f) != "ProfileGridColumns.cs")
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"const int MinFieldBoxWidth"));
        Assert.Empty(others);
    }

    // ── 字段控件宽度：列宽与下限取大者（第一百三十轮）────────────────────

    [Fact]
    public void FieldWidth_TakesTheColumnWidthWhenThereIsRoom()
    {
        // 字段列 495（网格 663 的实测值）：控件就摆 495，右边缘与列对齐。
        Assert.Equal(495, ApifoxHelper.Desktop.Native.ProfileGridColumns.FieldWidth(495));
        // 新建任务对话框的字段列 505（标签列 110）：同样摆满。
        Assert.Equal(505, ApifoxHelper.Desktop.Native.ProfileGridColumns.FieldWidth(505));
        // 恰好等于下限时仍是下限。
        Assert.Equal(480, ApifoxHelper.Desktop.Native.ProfileGridColumns.FieldWidth(480));
    }

    [Fact]
    public void FieldWidth_KeepsTheFloorWhenTheColumnIsNarrower()
    {
        // 窗口很窄时列宽按比例收缩到 480 以下：控件保持 480，由网格出横向滚动条，
        // 不把控件压到读不出内容（MinFieldBoxWidth 的原始口径）。
        Assert.Equal(480, ApifoxHelper.Desktop.Native.ProfileGridColumns.FieldWidth(96));
        Assert.Equal(480, ApifoxHelper.Desktop.Native.ProfileGridColumns.FieldWidth(0));
        Assert.Equal(480, ApifoxHelper.Desktop.Native.ProfileGridColumns.FieldWidth(-100));
        // 单调不减：列宽变大，控件宽度不会变小。
        for (var column = 0; column <= 1200; column++)
        {
            Assert.True(ApifoxHelper.Desktop.Native.ProfileGridColumns.FieldWidth(column) >= 480);
            Assert.True(ApifoxHelper.Desktop.Native.ProfileGridColumns.FieldWidth(column + 1)
                >= ApifoxHelper.Desktop.Native.ProfileGridColumns.FieldWidth(column));
        }
    }

    [Fact]
    public void FieldGridPanel_StretchesFieldControlsToTheColumn()
    {
        // 第一百三十轮的真机缺陷：字段列 495，而控件宽度是各对话框自己写的 420 / 320，
        // 「版本备注」那一行一个宽度都没设（保持 TextBox 自然宽）——同一列里三种宽度，
        // 右边缘参差，最窄的那个连一个路径都填不下。宽度口径收进网格之后：
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var start = ui.IndexOf("private sealed class FieldGridPanel", StringComparison.Ordinal);
        Assert.True(start >= 0, "Ui.cs 缺少 FieldGridPanel（两列字段网格的唯一形状落点）。");
        var rest = ui[start..];
        var end = rest.IndexOf("\n    }\n", StringComparison.Ordinal);
        var panel = end < 0 ? rest : rest[..end];

        // 宽度只经共享口径算，不就地写数。
        Assert.Contains("var width = ProfileGridColumns.FieldWidth(columnWidth);", panel);
        // 只认字段控件两类：标签（AutoSize）、按钮、复选框、行容器都不在这里改宽。
        Assert.Contains("if (control is not (FieldBox or ComboBox)) continue;", panel);
        // 只改字段列（第 1 列）里的控件。
        Assert.Contains("if (GetColumn(control) != 1) continue;", panel);
        // 每次重算列宽之后都跟着摆一次字段宽度（尺寸变化、留白变化、加控件三条路径同一处）。
        var apply = panel.IndexOf("private void ApplyColumnWidths()", StringComparison.Ordinal);
        Assert.True(apply >= 0);
        var applyBody = panel[apply..];
        Assert.Contains("ApplyFieldWidths(widths[1]);", applyBody[..applyBody.IndexOf("\n        }\n", StringComparison.Ordinal)]);
        // 加控件也走同一次重算：不靠「尺寸变化事件一定在行加完之后才到」这个顺序假设。
        Assert.Matches(@"(?m)^\s*protected override void OnControlAdded\(ControlEventArgs e\)", panel);
    }

    [Fact]
    public void SharedCompileList_IncludesTheColumnRule()
    {
        // 列宽口径必须与测试工程共享同一份源码，否则断言跑的不是主工程的代码。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"<Compile Include=""..\Desktop\Native\ProfileGridColumns.cs"" Link=""Shared\ProfileGridColumns.cs"" />", csproj);
    }

    [Fact]
    public void DesignBaseline_RecordsTheGridRule()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("两列字段网格", section);
        Assert.Contains("`Ui.FieldGrid`", section);
        Assert.Contains("`ProfileGridColumns`", section);
    }
}
