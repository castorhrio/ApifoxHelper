using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「在列表中查找」这一个手势的口径断言（DESIGN.md §6「列表筛选」与「菜单/快捷键」）：
/// Ctrl+F 在壳里由菜单项绑定并分发到当前可见页的筛选框，而模态对话框打开期间主窗体菜单不路由——
/// 五张长清单（检查结果、差异确认、注释缺口、注释候选、响应契约）此前在对话框里按 Ctrl+F 没有任何反应，
/// 同一个手势在主页有、在对话框没有，属同壳不同行为。本轮把字母与修饰键判定收进
/// <see cref="ShellShortcuts"/> 单一口径（<c>FindKey</c> 与 <c>IsFindGesture</c>），
/// 菜单显示、壳绑定与对话框判定同源；对话框只经 <c>Ui.TryFocusFilter</c> 一处接线。
/// 第七十三轮把第六张长清单（同步预览的接口勾选页）接上同一处入口，触达点清单随之由五张变六张；
/// 第七十五轮把第七张（接口追踪明细的接口清单）接上，清单再变七张；
/// 第七十六轮把第八张（补充说明的接口清单）接上、第七十七轮把第九张（生成文档核对条目）接上，清单再变九张。
/// 第七十九轮把第十张（注释回写的源码文件清单）接上，清单再变十张。
/// 口径是真执行（ShellShortcuts 与测试工程共享编译），接线以源码切片与行首正则钉住。
/// Ctrl+F 的实际按键路由与选中的真机观感随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class FindGestureTests
{
    private static readonly string[] Dialogs =
    [
        "CheckResultsDialog.cs",
        "DecisionsDialog.cs",
        "ScanResultsDialog.cs",
        "CandidatesDialog.cs",
        "ContractsDialog.cs",
        "SyncPreviewDialog.cs",
        "EndpointTraceDialog.cs",
        "SupplementsDialog.cs",
        "PropagationDialog.cs",
        "ApplyPlanDialog.cs",
    ];

    // ---------- 口径真值表 ----------

    [Theory]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, true, false)]
    public void Find_Gesture_IsPlainControlOnly(bool shift, bool control, bool alt, bool expected)
    {
        // 只认裸 Ctrl：带 Shift/Alt 的组合不属本手势、不吞键（与列表的 Enter、复制两个手势同一纪律）。
        Assert.Equal(expected, ShellShortcuts.IsFindGesture(shift, control, alt));
    }

    [Fact]
    public void FindKey_IsTheSingleSourceOfTheGestureText()
    {
        // 手势文字由字母拼出：菜单显示、壳绑定与对话框判定认的是同一个字母，另写一份必然漂移。
        Assert.Equal("Ctrl+" + ShellShortcuts.FindKey, ShellShortcuts.Find.Gesture);
        Assert.Equal("Ctrl+F", ShellShortcuts.Find.Gesture);
        Assert.Equal("Ctrl+F", ShellShortcuts.GestureOf("find"));
        // 手势仍不占编辑键（Ctrl+F 不在保留清单里，壳可以接）。
        Assert.DoesNotContain("Ctrl+F", ShellShortcuts.ReservedEditingKeys);
    }

    // ---------- 接线机械检查：口径存在但对话框没接线等于没有 ----------

    [Fact]
    public void Dialogs_WireFindThroughTheSharedEntry()
    {
        foreach (var file in Dialogs)
        {
            var source = Repo.Source("Desktop", "Native", file);
            var body = Slice(source, "protected override bool ProcessDialogKey(Keys keyData)", "return base.ProcessDialogKey(keyData);");
            // 手势判定只经 Ui.TryFocusFilter 一处（字母与修饰键都在共享口径里），对话框不就地写 Keys.F。
            Assert.Matches(@"(?m)^\s*if \(Ui\.TryFocusFilter\(keyData, _keyword\)\) return true;$", body);
            Assert.DoesNotContain("Keys.F", CodeLines(source), StringComparison.Ordinal);
            Assert.DoesNotContain("ShellShortcuts.IsFindGesture", CodeLines(source), StringComparison.Ordinal);
            // 裸 Enter 走「到结果第一行」的共享入口（第七十轮接线，见 FilterEnterTests），两条判定并存、顺序固定。
            Assert.Matches($@"(?m)^\s*if \(Ui\.TryEnterFilter\(keyData, _keyword, {ListField(file)}\)\) return true;$", body);
            var find = body.IndexOf("Ui.TryFocusFilter", StringComparison.Ordinal);
            var enter = body.IndexOf("Ui.TryEnterFilter", StringComparison.Ordinal);
            Assert.True(find >= 0 && enter > find, $"{file}:Ctrl+F 判定应排在 Enter 判定之前。");
        }
    }

    [Fact]
    public void Ui_TryFocusFilter_OnlyMovesFocus()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var body = Slice(ui, "public static bool TryFocusFilter(Keys keyData, TextBox keyword)", "/// <summary>");
        // 字母与修饰键都取共享口径，界面不就地拆修饰键、不就地写 Keys.F。
        Assert.Contains("ShellShortcuts.FindKey", body, StringComparison.Ordinal);
        Assert.Contains("ShellShortcuts.IsFindGesture", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.F", CodeLines(body), StringComparison.Ordinal);
        // 只移焦点：把焦点交给框并选中已有关键词（重输即替换），不改筛选结果、不重建列表、不写数据。
        Assert.Matches(@"(?m)^\s*keyword\.Focus\(\);$", body);
        Assert.Matches(@"(?m)^\s*keyword\.SelectAll\(\);$", body);
        Assert.DoesNotContain(".Text =", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Items", body, StringComparison.Ordinal);
        // 不是本手势的按键一律不接（返回 false，交回基类）。
        Assert.Contains("return false;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Pages_ShareTheSameFindEntry()
    {
        // 两个主页面的 Ctrl+F 仍由壳分发到 FocusListFilter（菜单不路由时才轮到对话框自己接），
        // 两页不因此多出一份手势判定。
        foreach (var file in new[] { "ProjectsPage.cs", "TasksPage.cs" })
        {
            var source = Repo.Source("Desktop", "Native", file);
            Assert.DoesNotContain("Keys.F", source, StringComparison.Ordinal);
            Assert.DoesNotContain("TryFocusFilter", source, StringComparison.Ordinal);
            Assert.Matches(@"(?m)^\s*public void FocusListFilter\(\)$", source);
        }
    }

    [Fact]
    public void SharedCompile_IncludesShellShortcuts()
    {
        // 口径只有一份：断言对准主工程真实实现（共享编译清单里必须有 ShellShortcuts.cs）。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ShellShortcuts.cs", csproj);
    }

    [Fact]
    public void DesignDoc_RecordsTheDialogFindGesture()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("Ctrl+F", section);
        Assert.Contains("ShellShortcuts", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("对话框里的 Ctrl+F", ledger);
    }

    /// <summary>
    /// 各对话框里「本屏清单」的字段名：共享入口收的是「关键词框 + 这一屏的清单」，
    /// 同步预览勾选页的清单叫 <c>_selectList</c>（同一对话框里还有写入清单与排除清单，不能都叫 _list）。
    /// </summary>
    private static string ListField(string file) => file == "SyncPreviewDialog.cs" ? "_selectList" : "_list";

    /// <summary>只算代码行：注释里引用「界面不就地写 Keys.F」是在说明理由，不算第二套判定。</summary>
    private static string CodeLines(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
