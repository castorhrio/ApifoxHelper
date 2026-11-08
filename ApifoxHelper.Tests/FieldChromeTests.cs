using System.Drawing;
using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 输入框形态断言（FieldChrome，共享源码单一口径）：输入框常态 1px Line 发丝边、聚焦描边 Accent、
/// 校验失败描边 BadText（DESIGN.md §6「输入框」），不用系统 3D 边框；多行可编辑输入框 Enter 换行
/// 而不触发对话框主按钮（AcceptsReturn，工厂单一落点）。镀铬工厂的接线由源码机械检查钉住：
/// 全部输入框经 Ui.Input/Ui.Preview 出厂（字段与属性两种声明形式都在检查内），
/// 可编辑输入框一律经 Ui.Field 包描边，绕过工厂的输入框会退回系统边框；
/// 共享解析口径的配置对话框在解析失败时按 UserException.Field 描红对应输入框。
/// </summary>
public class FieldChromeTests
{
    private static readonly Color Line = Color.FromArgb(0xE3, 0xE8, 0xF0);
    private static readonly Color Accent = Color.FromArgb(0x0B, 0x53, 0x94);
    private static readonly Color Bad = Color.FromArgb(0xB9, 0x1C, 0x1C);

    [Fact]
    public void FocusedField_UsesAccentBorder() =>
        Assert.Equal(Accent, FieldChrome.Border(focused: true, invalid: false, Line, Accent, Bad));

    [Fact]
    public void UnfocusedField_UsesHairlineBorder() =>
        Assert.Equal(Line, FieldChrome.Border(focused: false, invalid: false, Line, Accent, Bad));

    [Fact]
    public void InvalidField_UsesBadBorder_FocusedOrNot()
    {
        // 校验失败描边优先于聚焦：聚焦失败字段正是要修它的时刻，错误信号不能被聚焦色盖掉。
        Assert.Equal(Bad, FieldChrome.Border(focused: false, invalid: true, Line, Accent, Bad));
        Assert.Equal(Bad, FieldChrome.Border(focused: true, invalid: true, Line, Accent, Bad));
    }

    [Fact]
    public void BorderColors_MatchDesignDoc()
    {
        Assert.Contains("`Panel` 底 + 1px `Line`；聚焦描边 `Accent`", Repo.DesignSection(6));
        Assert.Contains("校验失败内联 `BadText` 文字 + 描边 `BadText`，不弹窗", Repo.DesignSection(6));
    }

    // ── 工厂接线机械检查 ──────────────────────────────────────────────

    [Fact]
    public void TextBoxes_AreOnlyCreatedByFactory()
    {
        foreach (var source in Repo.NativeSources().Where(s => !s.IsUi))
        {
            Assert.False(Regex.IsMatch(source.Text, @"new\s+TextBox\b"),
                $"{source.Name}:直接构造 TextBox。输入框一律经 Ui.Input()/Ui.Preview() 出厂（DESIGN.md §6 输入框：不用系统 3D 边框）。");
            // 目标类型 new() 的属性形式（TextBox X { get; } = new()）同样绕过工厂——此前只查字段形式，是检查盲区。
            Assert.False(Regex.IsMatch(source.Text, @"TextBox\s+\w+[^=;\n]*=\s*new\s*[\(\w]"),
                $"{source.Name}:直接构造 TextBox（属性/目标类型 new 形式）。输入框一律经 Ui.Input()/Ui.Preview() 出厂（DESIGN.md §6 输入框：不用系统 3D 边框）。");
            // 字段与属性两种声明都要从工厂出厂（属性形式此前也不在检查内）。
            foreach (var match in Regex.Matches(source.Text, @"TextBox\s+\w+[^=;\n]*=\s*([^;\n]+);").Cast<Match>())
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
            // 包描边的落点：就地 Ui.Field(...)，或交给 Ui.FilterRow(...)（工厂内部经 Field 包，第六十八轮）。
            Assert.True(source.Contains("Ui.Field(") || source.Contains("Ui.FilterRow("),
                $"{source.Name}:创建了输入框但没有 Ui.Field() 包 1px 描边——无边框输入框在 Bg 底上不可见（DESIGN.md §6 输入框）。");
        }
    }

    [Fact]
    public void Factory_WiresFocusChromeAndSizeSync()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Contains("FieldChrome.Border(_inner.Focused, _invalid, UiTokens.Line, UiTokens.Accent, UiTokens.BadText)", ui);
        Assert.Contains("inner.GotFocus += (_, _) => RefreshChrome();", ui);
        Assert.Contains("inner.LostFocus += (_, _) => RefreshChrome();", ui);
        // 校验失败描边由对话框按 UserException.Field 置位；用户改值后旧判定不再适用，工厂自动解除。
        // 行首正则断言（非子串）：接线被注释掉也算删除，同样变异可红。
        Assert.Contains("public void SetInvalid(bool invalid)", ui);
        Assert.Matches(@"(?m)^\s*inner\.TextChanged \+= \(_, _\) => SetInvalid\(false\);", ui);
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
        // 多行可编辑输入框 Enter 换行（AcceptsReturn）：窗体设了 AcceptButton 时，未设该属性的
        // 多行 TextBox 会把 Enter 交给主按钮（WinForms 文档行为）——模块范围/指定接口/JSON 编辑位
        // 等「每行一条」的输入将无法换行，「指定接口」里按 Enter 甚至直接开始扫描。只读 Preview
        // 不设（Enter 触发查看器对话框的关闭按钮与 Esc 同语义）。
        Assert.Matches(@"TextBox\s*\{\s*Multiline = multiline,\s*WordWrap = wordWrap,\s*UseSystemPasswordChar = password,\s*ScrollBars = scrollBars,\s*AcceptsReturn = multiline\s*\}", ui);
        Assert.Matches(@"TextBox\s*\{\s*ReadOnly = true,\s*Multiline = multiline,\s*WordWrap = wordWrap,\s*ScrollBars = scrollBars\s*\}", ui);
        Assert.Contains("box.BorderStyle = BorderStyle.None;", ui);
        Assert.Contains("box.BackColor = UiTokens.Panel;", ui);
        Assert.Contains("if (mono) box.Font = UiTokens.Mono.Value;", ui);
        Assert.Contains("if (caption) box.Font = UiTokens.Caption.Value;", ui);
    }

    [Fact]
    public void SupplementEditors_UseInputFactoryWithFieldChrome()
    {
        // 补充说明编辑位此前是裸 new() TextBox 绕过工厂（无聚焦描边、Enter 触发对话框「关闭」），
        // 与其余 35 处输入框同一形态收口（DESIGN.md §6 输入框）；接线由行首正则钉住防回退。
        var supplements = Repo.Source("Desktop", "Native", "SupplementsDialog.cs");
        Assert.Matches(@"(?m)^\s*public TextBox Text \{ get; \} = Ui\.Input\(multiline: true, scrollBars: ScrollBars\.Vertical\);", supplements);
        Assert.Matches(@"(?m)^\s*var field = Ui\.Field\(box\.Text\);", supplements);
        Assert.Matches(@"(?m)^\s*field\.Dock = DockStyle\.Fill;", supplements);
        Assert.False(supplements.Contains("Ui.Hairline(box.Text)"),
            "SupplementsDialog:编辑位不得回到只读预览的 Hairline 镀铬——可编辑输入框经 Ui.Field 包描边（聚焦 Accent）。");
    }

    [Fact]
    public void KeyDialogs_WrapInputsAtPlacement()
    {
        Assert.Contains("Row(Ui.Field(_key), _saveKeyButton)", Repo.Source("Desktop", "Native", "GenerationDialog.cs"));
        Assert.Contains("Ui.Field(_projectId)", Repo.Source("Desktop", "Native", "CapabilitiesDialog.cs"));
        Assert.Contains("AddBoxRow(detailGrid, Ui.Field(_answers), 44)", Repo.Source("Desktop", "Native", "CandidatesDialog.cs"));
        Assert.Contains("Ui.Field(_name)", Repo.Source("Desktop", "Native", "TasksPage.cs"));
        // 配置对话框的四个可解析失败输入框经登记式 Field() 镀铬（标识 → 描边面板）；版本备注等
        // 不会在保存校验中被拒绝的输入框仍是直接 Ui.Field。
        var projects = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.Contains("Field(_name, ProfileStore.FieldName)", projects);
        Assert.Contains("Ui.Field(_sourceVersion)", projects);
    }

    [Fact]
    public void ParseDialogs_MarkFailedFields()
    {
        // 共享解析口径的三个配置对话框：解析失败按 UserException.Field 描红对应输入框
        // （文字仍在状态行，双通道）；接线由机械检查钉住，防字段标识与输入框登记被删。
        // 描红调用用行首正则断言（非子串）：接线被注释掉也算删除，同样变异可红。
        var generation = Repo.Source("Desktop", "Native", "GenerationDialog.cs");
        Assert.Matches(@"(?m)^\s*MarkInvalidField\(ex\);", generation);
        Assert.Contains("Field(_timeout, GenerationEntries.FieldTimeoutSeconds)", generation);
        Assert.Contains("Field(_batch, GenerationEntries.FieldMaxSlotsPerRequest)", generation);

        var advanced = Repo.Source("Desktop", "Native", "ProfileAdvancedDialog.cs");
        Assert.Matches(@"(?m)^\s*MarkInvalidField\(ex\);", advanced);
        Assert.Contains("Field(_folderId, ProfileAdvanced.FieldFolderId)", advanced);
        Assert.Contains("Field(_auth, ProfileAdvanced.FieldAuth)", advanced);
        Assert.Contains("Field(_security, ProfileAdvanced.FieldSecurityScheme)", advanced);
        Assert.Contains("Field(_headers, ProfileAdvanced.FieldHeaderParameters)", advanced);

        var analysis = Repo.Source("Desktop", "Native", "ProfileAnalysisDialog.cs");
        Assert.Matches(@"(?m)^\s*MarkInvalidField\(ex\);", analysis);
        Assert.Contains("Field(_depth, ProfileAnalysis.FieldMaxTraceDepth)", analysis);
        Assert.Contains("Field(_budget, ProfileAnalysis.FieldMaxMethodsPerEndpoint)", analysis);
        Assert.Contains("Field(_xmlDocPaths, ProfileAnalysis.FieldXmlDocPaths)", analysis);
        Assert.Contains("Field(_concurrency, ProfileAnalysis.FieldReadConcurrency)", analysis);
    }

    [Fact]
    public void SaveDialog_MarksFailedCoreFields()
    {
        // 外层「保存」的校验失败（ProfileStore.Validate）同样按 UserException.Field 描红：
        // 可解析失败的文本输入框登记 ProfileStore 字段标识常量；API 项目与 Apifox 目标是
        // ComboBox，不在 §6 输入框范围（设计基线 §8 挂账），失败只显示状态行文字。
        // 描红调用用行首正则断言（非子串）并锚定在 Save 方法内：接线被注释掉也算删除，变异可红。
        var projects = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.Contains("Field(_name, ProfileStore.FieldName)", projects);
        Assert.Contains("Field(_workspace, ProfileStore.FieldWorkspaceRoot)", projects);
        Assert.Contains("Field(_allowed, ProfileStore.FieldAllowedSourcePaths)", projects);
        Assert.Contains("Field(_swaggerLocation, ProfileStore.FieldSwaggerLocation)", projects);
        Assert.Contains("private void MarkInvalidField(UserException ex)", projects);
        var save = projects[(projects.IndexOf("private void Save()", StringComparison.Ordinal))..];
        Assert.Matches(@"(?m)^\s*MarkInvalidField\(ex\);", save);
    }

    [Fact]
    public void ReadonlyPreviews_KeepHairlineWrap()
    {
        Assert.Contains("Ui.Hairline(_diff)", Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs"));
        Assert.Contains("Ui.Preview(multiline: true, mono: true, wordWrap: false, scrollBars: ScrollBars.Both)", Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs"));
    }
}
