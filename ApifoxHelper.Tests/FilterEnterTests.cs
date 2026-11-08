using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「筛选框输入完按 Enter 到结果第一行」这一个手势在对话框里的口径断言（DESIGN.md §6「列表筛选」与「菜单/快捷键」）：
/// 两个主页面早就有这条路径（Enter 聚焦框 → 输入 → Enter 到结果 → 方向键逐行看），而五张长清单对话框
/// （检查结果、差异确认、注释缺口、注释候选、响应契约）的关键词框在 Enter 上什么都不做——焦点留在框里，
/// 方向键动的是光标，要看某一行只能回鼠标点清单。同一个手势在主页有、在对话框没有，属同壳不同行为。
/// 第六十九轮已把「到结果第一行」整段收进 <see cref="Ui.FocusFirstResult"/>（目标行取
/// <see cref="ListKeys.FirstResultIndex"/>、选中经 <see cref="Ui.SelectItem"/> 唯一入口、滚动随该入口一并触达），
/// 本轮把对话框接上：手势判定取 <see cref="ListKeys.IsEnterGesture"/> 单一口径，接线只经
/// <see cref="Ui.TryEnterFilter"/> 一处，对话框不就地拆修饰键、不自己选中或滚动。
/// 第七十三轮把第六张长清单（同步预览的接口勾选页）接上同一处入口，触达点清单随之由五张变六张；
/// 第七十五轮把第七张（接口追踪明细的接口清单）接上，清单再变七张；
/// 第七十六轮把第八张（补充说明的接口清单）接上，清单再变八张。
/// 口径是真执行（ListKeys 与测试工程共享编译），接线以源码切片与行首正则钉住。
/// Enter 的实际按键路由与选中的真机观感随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class FilterEnterTests
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
    ];

    // ---------- 口径真值表（手势判定只有一份，对话框认的就是它） ----------

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void Enter_Gesture_IsTheSameOneThePagesUse(bool shift, bool control, bool alt, bool expected)
    {
        // 对话框与两个主页面认同一个裸 Enter：带修饰键的组合不吞键、交回基类（不给既有组合键发明新语义）。
        Assert.Equal(expected, ListKeys.IsEnterGesture(shift, control, alt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Enter_HasNoTargetWhenTheFilterMatchesNothing(int displayedCount)
    {
        // 筛到零行不动作：那一步的下一步是空态的「清空筛选」按钮，不把焦点丢进空列表。
        Assert.Null(ListKeys.FirstResultIndex(displayedCount));
    }

    // ---------- 接线机械检查：口径存在但对话框没接线等于没有 ----------

    [Fact]
    public void Dialogs_WireEnterThroughTheSharedEntry()
    {
        foreach (var file in Dialogs)
        {
            var source = Repo.Source("Desktop", "Native", file);
            var body = Slice(source, "protected override bool ProcessDialogKey(Keys keyData)", "return base.ProcessDialogKey(keyData);");
            // 只经 Ui.TryEnterFilter 一处接线：对话框不就地写 Keys.Enter、不就地拆修饰键、不自己选中或滚动。
            Assert.Matches($@"(?m)^\s*if \(Ui\.TryEnterFilter\(keyData, _keyword, {ListField(file)}\)\) return true;$", body);
            Assert.DoesNotContain("Keys.Enter", CodeLines(source), StringComparison.Ordinal);
            Assert.DoesNotContain("ListKeys.IsEnterGesture", CodeLines(source), StringComparison.Ordinal);
            Assert.DoesNotContain("FocusFirstResult", CodeLines(source), StringComparison.Ordinal);
            Assert.DoesNotContain("EnsureVisible", CodeLines(source), StringComparison.Ordinal);
            // 两条判定并存、顺序固定：Ctrl+F 在前（先到框），Enter 在后（再到行）。
            var find = body.IndexOf("Ui.TryFocusFilter", StringComparison.Ordinal);
            var enter = body.IndexOf("Ui.TryEnterFilter", StringComparison.Ordinal);
            Assert.True(find >= 0 && enter > find, $"{file}:Ctrl+F 判定应排在 Enter 判定之前。");
        }
    }

    [Fact]
    public void Ui_TryEnterFilter_OnlyMovesFocusAndSelection()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var body = Slice(ui, "public static bool TryEnterFilter(Keys keyData, TextBox keyword, ListView list)", "/// <summary>");
        // 只在关键词框取得焦点时接住这一次按键：对话框的 Enter=确认（表单字段）不受影响。
        Assert.Matches(@"(?m)^\s*if \(!keyword\.Focused\) return false;$", body);
        // 修饰键判定取共享口径（与 Ctrl+F 同一写法：只把三个修饰位交给口径，不在这里另立一套规则）。
        Assert.Contains("ListKeys.IsEnterGesture", body, StringComparison.Ordinal);
        // 目标行与选中走 Ui.FocusFirstResult 一处：对话框不自己选中、不自己滚动、不自己索引行。
        Assert.Matches(@"(?m)^\s*FocusFirstResult\(list\);$", body);
        Assert.DoesNotContain("SelectItem", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Items[", body, StringComparison.Ordinal);
        // 只移焦点与选中：不改筛选关键词、不重建清单、不写数据。
        Assert.DoesNotContain(".Text =", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Items.Clear", body, StringComparison.Ordinal);
        // 不是本手势的按键一律不接（返回 false，交回基类）。
        Assert.Contains("return false;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Pages_KeepTheirOwnEnterPath()
    {
        // 两个主页面走的是控件级 KeyDown（筛选框不是对话框，没有对话键要挡），不因此多出一份对话框入口；
        // 它们仍直接调 Ui.FocusFirstResult，与对话框共用同一个目标行与选中入口。
        foreach (var file in new[] { "ProjectsPage.cs", "TasksPage.cs" })
        {
            var source = Repo.Source("Desktop", "Native", file);
            Assert.DoesNotContain("TryEnterFilter", source, StringComparison.Ordinal);
            Assert.Matches(@"(?m)^\s*Ui\.FocusFirstResult\(_list\);$", source);
        }
    }

    [Fact]
    public void SharedCompile_IncludesListKeys()
    {
        // 口径只有一份：断言对准主工程真实实现（共享编译清单里必须有 ListKeys.cs）。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListKeys.cs", csproj);
    }

    [Fact]
    public void DesignDoc_RecordsTheDialogEnterGesture()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("TryEnterFilter", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("对话框筛选框的 Enter", ledger);
    }

    /// <summary>
    /// 各对话框里「本屏清单」的字段名：共享入口收的是「关键词框 + 这一屏的清单」，
    /// 同步预览勾选页的清单叫 <c>_selectList</c>（同一对话框里还有写入清单与排除清单，不能都叫 _list）。
    /// </summary>
    private static string ListField(string file) => file == "SyncPreviewDialog.cs" ? "_selectList" : "_list";

    /// <summary>只算代码行：注释里引用「对话框不就地写 Keys.Enter」是在说明理由，不算第二套判定。</summary>
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
