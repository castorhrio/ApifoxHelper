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
        // 三处调用：构造一次（先按最小宽摆好）、尺寸变化一次、留白变化一次（第一百二十四轮）。
        Assert.Equal(3, Regex.Matches(panel, @"(?m)^\s*ApplyColumnWidths\(\);").Count);
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
        // 同一个数三处字面重复。现在只允许引用 ProfileGridColumns.MinFieldBoxWidth。
        var dialogs = new[] { "GenerationDialog.cs", "ProfileAdvancedDialog.cs", "ProfileAnalysisDialog.cs" };
        foreach (var file in dialogs)
        {
            var source = Repo.Source("Desktop", "Native", file);
            Assert.Contains("ProfileGridColumns.MinFieldBoxWidth", source);
            // 界面代码不出现字面 480（这个数只属于 ProfileGridColumns）。
            Assert.DoesNotMatch(@"\b480\b", source);
            // 字段加宽只经共享常量：不得再出现「Math.Max(field.Width, 数字)」。
            Assert.DoesNotMatch(@"Math\.Max\(field\.Width, \d", source);
        }
        // 常量本身只在一处定义。
        var rule = Repo.Source("Desktop", "Native", "ProfileGridColumns.cs");
        Assert.Contains("public const int MinFieldBoxWidth = 480;", rule);
        var others = Directory.GetFiles(Path.Combine(Repo.Root, "Desktop", "Native"), "*.cs")
            .Where(f => Path.GetFileName(f) != "ProfileGridColumns.cs")
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"const int MinFieldBoxWidth"));
        Assert.Empty(others);
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
