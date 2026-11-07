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

    /// <summary>
    /// 项目配置页的列表在重建（切页、刷新、新建/编辑保存）后恢复选中与键盘焦点——与任务页同一口径：
    /// 选中变化时记录配置 Id，重建前先取局部（Clear 会触发选中事件把字段抹掉），重建后经 Ui.SelectItem 恢复；
    /// 配置对话框保存成功后暴露 Saved，调用方定位刚保存的行（新建后编辑/探测入口随即可用）。
    /// </summary>
    [Fact]
    public void ProjectsPage_SelectionSurvivesReloads()
    {
        var page = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.True(Regex.IsMatch(page, @"^    private string\? _selectedId;", RegexOptions.Multiline),
            "ProjectsPage 缺少选中 Id 字段——列表重建后选中无处恢复。");
        Assert.True(Regex.IsMatch(page, @"^            _selectedId = Selected\(\)\?\.Id;", RegexOptions.Multiline),
            "选中变化未记录配置 Id——重建后无法恢复到当前行。");
        Assert.True(Regex.IsMatch(page, @"^        var selectedId = _selectedId;", RegexOptions.Multiline),
            "LoadProfiles 未在重建前取局部选中 Id——Items.Clear 触发的选中事件会把字段抹成 null。");
        Assert.True(Regex.IsMatch(page, @"^            RestoreSelection\(selectedId\);", RegexOptions.Multiline),
            "LoadProfiles 重建后未恢复选中——切页、刷新与保存后的选中都会丢失，「编辑/连接与写入探测」退回禁用。");
        Assert.True(Regex.IsMatch(page, @"^                Ui\.SelectItem\(item\);", RegexOptions.Multiline),
            "恢复选中绕过 Ui.SelectItem——焦点标记不会恢复，方向键从列表头部重新开始。");
        Assert.True(Regex.IsMatch(page, @"^    public ProjectProfile\? Saved \{ get; private set; \}", RegexOptions.Multiline),
            "ProfileDialog 缺少 Saved 属性——调用方无法得知刚保存的配置。");
        Assert.True(Regex.IsMatch(page, @"^            Saved = _profile;", RegexOptions.Multiline),
            "保存成功路径未记录 Saved（保存失败保持 null）。");
        Assert.True(Regex.IsMatch(page, @"^            if \(dialog\.Saved is \{ \} saved\) _selectedId = saved\.Id;", RegexOptions.Multiline),
            "配置对话框确认后未定位刚保存的配置——新建/编辑保存后还要手动找行。");
    }
}
