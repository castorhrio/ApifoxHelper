using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「对话框筛选框里的 Esc 退出筛选」这一个手势的口径断言（DESIGN.md §6「列表筛选」与「菜单/快捷键」）：
/// 两个主页面的全键盘路径自第六十四轮起是完整的——Ctrl+F 到框 → 输入 → Enter 到结果第一行 →
/// 方向键逐行看 → Esc 回到全部；五张长清单对话框（检查结果、差异确认、注释缺口、注释候选、响应契约）
/// 在第六十九、七十轮补上了前两步（Ctrl+F 与 Enter），退出这一步却仍是「取消对话框」：
/// 刚敲的关键词与审阅位置一起丢掉，要看全部只能重开一次。同一个手势在主页有、在对话框没有，
/// 属同壳不同行为，按第五十五、五十七、五十九轮那条判据判定为缺口，不是偏好。
/// 对话框的 Esc 另有「取消」这一既有含义（§6「对话框 Enter=确认、Esc=取消」），所以判定必须看焦点：
/// 焦点在筛选框上时 Esc 退出筛选（筛选框不是表单字段，与第七十轮 Enter 的判定同一理由），
/// 焦点在别处时 Esc 仍是取消——一次按键只有一个含义。判定在 <see cref="ListFilter.EscClearsInDialog"/>
/// 单一口径，接线只经 <see cref="Ui.TryEscapeFilter"/> 一处，对话框不就地认 Esc、不自己清空关键词。
/// 口径是真执行（ListFilter 与测试工程共享编译），接线以源码切片与行首正则钉住。
/// Esc 的实际按键路由与清空后列表回到全部的真机观感随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class FilterEscapeTests
{
    private static readonly string[] Dialogs =
    [
        "CheckResultsDialog.cs",
        "DecisionsDialog.cs",
        "ScanResultsDialog.cs",
        "CandidatesDialog.cs",
        "ContractsDialog.cs",
    ];

    // ---------- 口径真值表：这一次 Esc 归谁 ----------

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void DialogEscapeGate_FourCombinations(bool keywordFocused, bool hasKeyword, bool expected) =>
        Assert.Equal(expected, ListFilter.EscClearsInDialog(keywordFocused, hasKeyword));

    [Fact]
    public void FocusElsewhere_LeavesTheEscapeToCancel()
    {
        // 焦点不在框上时 Esc 仍是取消对话框：框里留着字不构成把「取消」从用户手里拿走的理由。
        Assert.False(ListFilter.EscClearsInDialog(keywordFocused: false, hasKeyword: true));
    }

    [Fact]
    public void EmptyKeyword_LeavesTheKeyAlone()
    {
        // 空框里按 Esc 什么都没做，就不把这一次按键说成做了什么（不吞键，交回基类去取消对话框）。
        Assert.False(ListFilter.EscClearsInDialog(keywordFocused: true, hasKeyword: false));
    }

    [Fact]
    public void DialogGate_ComposesWithTheKeywordTestInsteadOfReinventingIt()
    {
        // 「框里有没有关键词」只有一份问法：接在 IsActive 上，全空白也算没有筛选（与两页同一口径）。
        Assert.False(ListFilter.EscClearsInDialog(true, ListFilter.IsActive("   ")));
        Assert.False(ListFilter.EscClearsInDialog(true, ListFilter.IsActive(null)));
        Assert.True(ListFilter.EscClearsInDialog(true, ListFilter.IsActive("订单")));
    }

    [Fact]
    public void DialogGate_DiffersFromThePageGateInExactlyOneThing()
    {
        // 两页的 Esc 不看焦点（筛选是这一屏的状态），对话框的 Esc 看焦点（它另有「取消」这一含义）：
        // 差别只有这一处，其余（关键词为空不接）逐字相同。
        Assert.True(ListFilter.EscClears(pageVisible: true, hasKeyword: true));
        Assert.False(ListFilter.EscClearsInDialog(keywordFocused: false, hasKeyword: true));
        Assert.False(ListFilter.EscClears(pageVisible: true, hasKeyword: false));
        Assert.False(ListFilter.EscClearsInDialog(keywordFocused: true, hasKeyword: false));
    }

    [Fact]
    public void DialogGate_IsTheExactConjunctionOverTheWholeWindow()
    {
        // 两个布尔的全量核对：放行恰等于「焦点在框上 ∧ 框里有字」，多一分是抢走取消、少一分是退出不了筛选。
        foreach (var focused in new[] { false, true })
        {
            foreach (var hasKeyword in new[] { false, true })
            {
                Assert.Equal(focused && hasKeyword, ListFilter.EscClearsInDialog(focused, hasKeyword));
            }
        }
    }

    [Fact]
    public void Gauge_DoesNotReachForWinForms()
    {
        // 引用只算代码行：注释里写框架文档名不算引用，而 using 一行真写上就编不进测试工程（共享编译）。
        Assert.Empty(CodeLines(Repo.Source("Desktop", "Native", "ListFilter.cs"), "System.Windows.Forms"));
    }

    // ---------- 接线机械检查：口径存在但对话框没接线等于没有 ----------

    [Fact]
    public void Dialogs_WireEscapeThroughTheSharedEntry()
    {
        foreach (var file in Dialogs)
        {
            var source = Repo.Source("Desktop", "Native", file);
            var body = Slice(source, "protected override bool ProcessDialogKey(Keys keyData)", "return base.ProcessDialogKey(keyData);");
            // 只经 Ui.TryEscapeFilter 一处接线：对话框不就地认 Esc、不就地拆修饰键、不自己清空关键词。
            Assert.Matches(@"(?m)^\s*if \(Ui\.TryEscapeFilter\(keyData, _keyword\)\) return true;$", body);
            Assert.Empty(CodeLines(source, "Keys.Escape"));
            Assert.Empty(CodeLines(source, "ListFilter.EscClearsInDialog"));
            Assert.Empty(CodeLines(source, "_keyword.Clear()"));
            Assert.Empty(CodeLines(source, "_keyword.Text = \"\""));
            // 三条判定并存、顺序固定：Ctrl+F 在前（先到框）、Enter 居中（再到行）、Esc 在后（退出筛选）。
            var find = body.IndexOf("Ui.TryFocusFilter", StringComparison.Ordinal);
            var enter = body.IndexOf("Ui.TryEnterFilter", StringComparison.Ordinal);
            var escape = body.IndexOf("Ui.TryEscapeFilter", StringComparison.Ordinal);
            Assert.True(find >= 0 && enter > find, $"{file}:Ctrl+F 判定应排在 Enter 判定之前。");
            Assert.True(escape > enter, $"{file}:Esc 判定应排在 Enter 判定之后（先到行、再退出筛选）。");
        }
    }

    [Fact]
    public void Ui_TryEscapeFilter_OnlyClearsTheKeyword()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var body = Slice(ui, "public static bool TryEscapeFilter(Keys keyData, TextBox keyword)", "/// <summary>");
        // 先认键、再经共享口径、最后清空：三步顺序固定，判定不在入口里就地写。
        Assert.Matches(@"(?m)^\s*if \(\(keyData & Keys\.KeyCode\) != Keys\.Escape\) return false;$", body);
        Assert.Matches(@"(?m)^\s*if \(!ListFilter\.EscClearsInDialog\(keyword\.Focused, ListFilter\.IsActive\(keyword\.Text\)\)\) return false;$", body);
        Assert.Matches(@"(?m)^\s*keyword\.Clear\(\);$", body);
        var keyCheck = body.IndexOf("Keys.Escape", StringComparison.Ordinal);
        var gate = body.IndexOf("ListFilter.EscClearsInDialog", StringComparison.Ordinal);
        var clear = body.IndexOf("keyword.Clear();", StringComparison.Ordinal);
        Assert.True(keyCheck >= 0 && gate > keyCheck && clear > gate, "Ui.TryEscapeFilter:三步顺序应为「认键 → 判定 → 清空」。");
        // 只清关键词：不重建清单（重建仍由各对话框既有的 TextChanged 触发）、不碰选中与滚动、不写数据。
        Assert.DoesNotContain("PopulateList", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyFilter", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ListView", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectItem", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Focus()", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_list", body, StringComparison.Ordinal);
    }

    [Fact]
    public void KeywordIsClearedInExactlyOnePlace()
    {
        // 清空关键词只在一处（Ui.TryEscapeFilter）：对话框各写一句就是五份会各自漂移的实现。
        var owners = Repo.NativeSources()
            .Select(source => (source.Name, lines: CodeLines(source.Text, "keyword.Clear()")))
            .Where(o => o.lines.Count > 0)
            .ToList();
        Assert.Equal(["Ui.cs"], owners.Select(o => o.Name).ToArray());
        Assert.Single(owners[0].lines);
    }

    [Fact]
    public void EscapeEntry_IsReachedFromExactlyTheFiveLongLists()
    {
        // 触达点清单：恰为五张长清单对话框的接线（定义在 Ui.cs，调用形式只在这五处）；其余界面（含两个主页面）不接这一条。
        var callers = Repo.NativeSources()
            .Where(source => CodeLines(source.Text, "Ui.TryEscapeFilter(").Count > 0)
            .Select(source => source.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(Dialogs.OrderBy(name => name, StringComparer.Ordinal).ToArray(), callers);
    }

    [Fact]
    public void SharedCompileList_CarriesTheGauge()
    {
        // 口径文件必须与测试工程共享编译，否则上面那些真值表跑的不是主工程那一份代码。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListFilter.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheDialogEscapeClause()
    {
        var row = DesignRow(6, "列表筛选");
        Assert.Contains("对话框筛选框里的 Esc", row);
        Assert.Contains("EscClearsInDialog", row);
        Assert.Contains("焦点", row);
        Assert.Contains("第七十一轮", row);
    }

    [Fact]
    public void DesignDoc_RecordsTheRoundInTheLedger()
    {
        var row = DesignRow(8, "对话框筛选框的 Esc（§6）");
        Assert.Contains("第七十一轮", row);
        Assert.Contains("TryEscapeFilter", row);
        Assert.Contains("G-D5", row);
        // 本轮没做的事不能被写成做了：焦点不在框上时 Esc 仍是取消，且没有「一律清空筛选」这种说法。
        Assert.Contains("焦点不在框上时 Esc 仍是取消", row);
        Assert.DoesNotContain("Esc 一律清空筛选", row);
    }

    // ---------- 助手 ----------

    private static string DesignRow(int section, string clause)
    {
        var lines = Repo.DesignSection(section).Split('\n');
        var row = lines.SingleOrDefault(l => l.StartsWith($"| {clause} |", StringComparison.Ordinal));
        Assert.True(row is not null, $"DESIGN.md §{section} 缺少「{clause}」这一行。");
        return row;
    }

    private static List<string> CodeLines(string text, string fragment) =>
        text.Split('\n')
            .Select(l => l.TrimStart())
            .Where(l => l.Contains(fragment, StringComparison.Ordinal)
                && !l.StartsWith("//", StringComparison.Ordinal)
                && !l.StartsWith("*", StringComparison.Ordinal)
                && !l.StartsWith("///", StringComparison.Ordinal))
            .ToList();

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
