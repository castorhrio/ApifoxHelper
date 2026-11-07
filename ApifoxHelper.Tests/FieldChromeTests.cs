using System.Drawing;
using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 输入框形态断言（FieldChrome，共享源码单一口径）：输入框常态 1px Line 发丝边、聚焦描边 Accent，
/// 不用系统 3D 边框（DESIGN.md §6「输入框」）。镀铬工厂的接线由源码机械检查钉住：
/// 全部输入框经 Ui.Input/Ui.Preview 出厂，可编辑输入框一律经 Ui.Field 包描边，
/// 绕过工厂的输入框会退回系统边框。校验失败描边 BadText 待字段定位接入（DESIGN.md §8 挂账）。
/// </summary>
public class FieldChromeTests
{
    private static readonly Color Line = Color.FromArgb(0xE3, 0xE8, 0xF0);
    private static readonly Color Accent = Color.FromArgb(0x0B, 0x53, 0x94);

    [Fact]
    public void FocusedField_UsesAccentBorder() =>
        Assert.Equal(Accent, FieldChrome.Border(focused: true, Line, Accent));

    [Fact]
    public void UnfocusedField_UsesHairlineBorder() =>
        Assert.Equal(Line, FieldChrome.Border(focused: false, Line, Accent));

    [Fact]
    public void BorderColors_MatchDesignDoc()
    {
        Assert.Contains("`Panel` 底 + 1px `Line`；聚焦描边 `Accent`", Repo.DesignSection(6));
    }

    // ── 工厂接线机械检查 ──────────────────────────────────────────────

    [Fact]
    public void TextBoxes_AreOnlyCreatedByFactory()
    {
        foreach (var source in Repo.NativeSources().Where(s => !s.IsUi))
        {
            Assert.False(Regex.IsMatch(source.Text, @"new\s+TextBox\b"),
                $"{source.Name}:直接构造 TextBox。输入框一律经 Ui.Input()/Ui.Preview() 出厂（DESIGN.md §6 输入框：不用系统 3D 边框）。");
            foreach (var match in Regex.Matches(source.Text, @"TextBox\s+\w+\s*=\s*([^;\n]+);").Cast<Match>())
            {
                var factory = match.Groups[1].Value;
                Assert.True(factory.StartsWith("Ui.Input(") || factory.StartsWith("Ui.Preview("),
                    $"{source.Name}:TextBox 字段不是经 Ui.Input()/Ui.Preview() 出厂（实际：{factory.Trim()}）。");
            }
        }
    }

    [Fact]
    public void Inputs_AreWrappedInFieldChrome()
    {
        foreach (var source in Repo.NativeSources().Where(s => !s.IsUi))
        {
            if (!source.Contains("Ui.Input(")) continue;
            Assert.True(source.Contains("Ui.Field("),
                $"{source.Name}:创建了输入框但没有 Ui.Field() 包 1px 描边——无边框输入框在 Bg 底上不可见（DESIGN.md §6 输入框）。");
        }
    }

    [Fact]
    public void Factory_WiresFocusChromeAndSizeSync()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Contains("FieldChrome.Border(_inner.Focused, UiTokens.Line, UiTokens.Accent)", ui);
        Assert.Contains("inner.GotFocus += (_, _) => RefreshChrome();", ui);
        Assert.Contains("inner.LostFocus += (_, _) => RefreshChrome();", ui);
        // 描边面板：1px 发丝边由 Padding(1) 与 Line 底色构成（同 HairlineCore 的做法）。
        Assert.Contains("BackColor = UiTokens.Line;", ui);
        Assert.Contains("Padding = new Padding(1);", ui);
        // 内外尺寸双向同步：文本框改宽高时面板跟随，面板被布局拉伸时文本框跟随。
        Assert.Contains("inner.Resize += (_, _) => SyncFromInner();", ui);
        Assert.Contains("Resize += (_, _) => SyncFromOuter();", ui);
        Assert.Contains("_inner.Size = new Size(Width - Padding.Horizontal, Height - Padding.Vertical);", ui);
    }

    [Fact]
    public void Factories_SetBorderlessChromeAndPreviewReadonly()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Matches(@"TextBox\s*\{\s*Multiline = multiline,\s*WordWrap = wordWrap,\s*UseSystemPasswordChar = password,\s*ScrollBars = scrollBars\s*\}", ui);
        Assert.Matches(@"TextBox\s*\{\s*ReadOnly = true,\s*Multiline = multiline,\s*WordWrap = wordWrap,\s*ScrollBars = scrollBars\s*\}", ui);
        Assert.Contains("box.BorderStyle = BorderStyle.None;", ui);
        Assert.Contains("box.BackColor = UiTokens.Panel;", ui);
        Assert.Contains("if (mono) box.Font = UiTokens.Mono.Value;", ui);
        Assert.Contains("if (caption) box.Font = UiTokens.Caption.Value;", ui);
    }

    [Fact]
    public void KeyDialogs_WrapInputsAtPlacement()
    {
        Assert.Contains("Row(Ui.Field(_key), _saveKeyButton)", Repo.Source("Desktop", "Native", "GenerationDialog.cs"));
        Assert.Contains("Ui.Field(_projectId)", Repo.Source("Desktop", "Native", "CapabilitiesDialog.cs"));
        Assert.Contains("AddBoxRow(detailGrid, Ui.Field(_answers), 44)", Repo.Source("Desktop", "Native", "CandidatesDialog.cs"));
        Assert.Contains("Ui.Field(_name)", Repo.Source("Desktop", "Native", "TasksPage.cs"));
        Assert.Contains("Ui.Field(_name)", Repo.Source("Desktop", "Native", "ProjectsPage.cs"));
    }

    [Fact]
    public void ReadonlyPreviews_KeepHairlineWrap()
    {
        Assert.Contains("Ui.Hairline(_diff)", Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs"));
        Assert.Contains("Ui.Preview(multiline: true, mono: true, wordWrap: false, scrollBars: ScrollBars.Both)", Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs"));
    }
}
