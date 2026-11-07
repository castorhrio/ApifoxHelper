using System.Drawing;
using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 列表行形态断言（ListRows，共享源码单一口径）：选中行底 Bg + 左侧 3px Accent 标记，
/// 不用系统高亮蓝（DESIGN.md §6「列表行」）；逐子项语义色保留——「颜色+文字」双通道不能因自绘丢失。
/// 自绘工厂的接线由源码机械检查钉住：全部列表经 Ui.List 出厂，绕过工厂的列表会退回系统高亮蓝。
/// </summary>
public class ListRowsTests
{
    private static readonly Color Muted = Color.FromArgb(0x6B, 0x72, 0x80);
    private static readonly Color Warn = Color.FromArgb(0xB4, 0x53, 0x09);
    private static readonly Color Body = Color.FromArgb(0x1F, 0x29, 0x37);
    private static readonly HashSet<Color> Semantic = [Muted, Warn];

    [Fact]
    public void SubItemSemanticColor_IsKept()
    {
        var color = ListRows.TextColor(subItemColor: Warn, itemColor: Body, bodyColor: Body, semanticColors: Semantic);
        Assert.Equal(Warn, color);
    }

    [Fact]
    public void ItemLevelSemanticColor_IsUsedWhenSubItemUnset() =>
        Assert.Equal(Muted, ListRows.TextColor(Body, Muted, Body, Semantic));

    [Fact]
    public void UnsetColors_FallBackToBody() =>
        Assert.Equal(Body, ListRows.TextColor(Body, Body, Body, Semantic));

    [Fact]
    public void SubItemColor_WinsOverItemColor() =>
        Assert.Equal(Warn, ListRows.TextColor(Warn, Muted, Body, Semantic));

    [Fact]
    public void MarkerWidth_MatchesDesignDoc()
    {
        Assert.Equal(3, ListRows.MarkerWidth);
        Assert.Contains("左侧 3px `Accent` 标记", Repo.DesignSection(6));
    }

    // ── 工厂接线机械检查 ──────────────────────────────────────────────

    [Fact]
    public void Lists_AreOnlyCreatedByFactory()
    {
        foreach (var source in Repo.NativeSources().Where(s => !s.IsUi))
        {
            foreach (var match in Regex.Matches(source.Text, @"ListView\s+\w+\s*=\s*([^;\n]+);").Cast<Match>())
            {
                Assert.True(match.Groups[1].Value.StartsWith("Ui.List("),
                    $"{source.Name}:列表字段不是经 Ui.List() 出厂（DESIGN.md §6 列表行：选中态不用系统高亮蓝）。");
            }
            Assert.False(Regex.IsMatch(source.Text, @"new ListView\b(?!Item)"),
                $"{source.Name}:直接构造 ListView。列表一律经 Ui.List() 出厂。");
        }
    }

    [Fact]
    public void Factory_WiresOwnerDrawAndRowForm()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Contains("OwnerDraw = true", ui);
        Assert.Contains("HideSelection = false", ui);
        Assert.Contains("DrawColumnHeader", ui);
        Assert.Contains("e.DrawDefault = true", ui);
        Assert.Contains("DrawItem += DrawListRow", ui);
        Assert.Contains("DrawSubItem += DrawListCell", ui);
        Assert.Matches(@"e\.Item\.Selected\s*\?\s*UiBrushes\.Bg\s*:\s*UiBrushes\.Panel", ui);
        Assert.Matches(
            @"FillRectangle\(UiBrushes\.Accent, e\.Bounds\.X, e\.Bounds\.Y, ListRows\.MarkerWidth, e\.Bounds\.Height\)",
            ui);
        Assert.Contains("ListRows.TextColor", ui);
    }

    [Fact]
    public void Factory_KeepsSemanticDualChannel()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Contains("UiTokens.Muted,", ui);
        Assert.Contains("UiTokens.OkText,", ui);
        Assert.Contains("UiTokens.WarnText,", ui);
        Assert.Contains("UiTokens.BadText,", ui);
    }

    [Fact]
    public void CheckboxList_UsesFactoryCheckboxPath()
    {
        var preview = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        Assert.Contains("Ui.List(true)", preview);
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Contains("CheckBoxRenderer.DrawCheckBox", ui);
        Assert.Matches(@"CheckBoxes = checkBoxes", ui);
    }

    /// <summary>
    /// 程序化选中只走 Ui.SelectItem：列表重建后 FocusedItem 丢失，只恢复 Selected 时
    /// 方向键从列表头部重新开始、详情跳到第一行；选中与焦点标记必须同时恢复（Windows 列表惯例）。
    /// </summary>
    [Fact]
    public void ProgrammaticSelection_RestoresKeyboardFocus()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var helper = Regex.Match(ui, @"public static void SelectItem\(ListViewItem item\)\s*\{[^}]*\}");
        Assert.True(helper.Success, "Ui 缺少 SelectItem 程序选中口径（列表选中唯一入口）。");
        Assert.Contains("item.Selected = true;", helper.Value);
        Assert.Contains("item.Focused = true;", helper.Value);

        foreach (var source in Repo.NativeSources().Where(s => !s.IsUi))
        {
            Assert.False(Regex.IsMatch(source.Text, @"^(?!\s*//).*\.Selected\s*=\s*true", RegexOptions.Multiline),
                $"{source.Name}:程序化选中绕过 Ui.SelectItem——重建列表后焦点标记丢失，方向键会从列表头部重新开始。");
        }
    }

    [Fact]
    public void CapabilitiesDialog_EscClosesLikeTitleBarX()
    {
        var dialog = Repo.Source("Desktop", "Native", "CapabilitiesDialog.cs");
        Assert.Contains("ProcessDialogKey", dialog);
        Assert.Contains("Keys.Escape", dialog);
    }
}
