using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Sync;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步预览接口勾选页的三个筛选手势（第七十三轮，DESIGN.md §6「列表筛选」与「菜单/快捷键」）：
/// 第七十二轮给这一页补上关键词框时如实挂账「Ctrl+F / Enter / Esc 三个手势本轮未接（另六张长清单在
/// 第六十九至七十一轮各接一次）」——框有了、键盘路径却断在框上：Ctrl+F 到不了框、输入完按 Enter 焦点留在框里
/// （方向键动的是光标）、想回到全部接口只能重开对话框（Esc 是取消）。同一个手势在另六张长清单有、在这一处没有，
/// 属同壳不同行为，按第五十五、五十七、五十九轮那条判据判定为缺口，不是偏好。
/// <para>
/// 这一页与那六张有一处不同：**关键词框只在勾选页上**（预览页没有这个框），故三个手势在 <c>_page1.Visible</c>
/// 一处门控之内——预览页上 Ctrl+F 不吞键、Esc 仍是取消对话框（一次按键只有一个含义）。
/// 另一处不同是勾选挂在行上：Esc 清空关键词会重建清单，勾选按接口键记在 <see cref="SyncSelect"/>，
/// 重建从它恢复，**勾选不因退出筛选而丢**——这条不变量由本文件在 Linux 上真执行钉住。
/// </para>
/// <para>
/// 三处判定与接线仍只在 <see cref="Ui.TryFocusFilter"/>、<see cref="Ui.TryEnterFilter"/>、
/// <see cref="Ui.TryEscapeFilter"/> 与 <see cref="ShellShortcuts"/>、<see cref="ListKeys"/>、
/// <see cref="ListFilter.EscClearsInDialog"/> 单一口径，本对话框不就地认键、不就地拆修饰键、不自己选中或滚动、
/// 不自己清空关键词。界面接线属 WinForms 控件触达、本环境不可执行，全部以源码切片与行首正则钉住，不冒充跑过；
/// 按键的实际路由与退出筛选后列表回到全部接口的真机观感随 G-D5 复核。
/// </para>
/// </summary>
public class SyncSelectGestureTests
{
    private const string File = "SyncPreviewDialog.cs";

    // ---------- 真执行：三个手势在这一页上落到哪 ----------

    [Fact]
    public void Enter_TargetsTheFirstVisibleRow_NotTheFirstEndpointInTheData()
    {
        // 关键词把前两条筛掉之后，Enter 的去向是筛选结果的第一行（显示序第一行），不是数据源第一行——
        // 与两个主页面、另六张长清单同一份目标行口径（ListKeys.FirstResultIndex 恒为 0）。
        var diff = Diff(
            Endpoint("GET", "/orders", "new", "订单列表"),
            Endpoint("POST", "/orders", "new", "创建订单"),
            Endpoint("GET", "/users", "new", "用户列表"));
        var select = new SyncSelect { Query = "users" };

        var visible = select.Visible(diff);
        Assert.Equal(["GET /users"], visible.Select(e => e.Key));
        Assert.Equal(0, ListKeys.FirstResultIndex(visible.Count));
        Assert.Equal("GET /users", visible[ListKeys.FirstResultIndex(visible.Count)!.Value].Key);
    }

    [Fact]
    public void Enter_HasNoTargetWhenTheKeywordMatchesNothing()
    {
        // 筛到零行不动作：这一屏的下一步是空态那句话指名的关键词框，不把焦点丢进空清单。
        var diff = Diff(Endpoint("GET", "/orders", "new", "订单列表"));
        var select = new SyncSelect { Query = "没有这个接口" };

        Assert.Empty(select.Visible(diff));
        Assert.Null(ListKeys.FirstResultIndex(select.Visible(diff).Count));
    }

    [Fact]
    public void Escape_ClearsTheKeywordAndTheChecksSurviveTheRebuild()
    {
        // Esc 的整条链：判定放行（焦点在框上 ∧ 框里有字）→ 清空关键词 → 重建清单 → 勾选仍在。
        // 这一页的勾选挂在行上，退出筛选若丢勾选，点「生成同步计划」写出去的就是少的那几个。
        var diff = Diff(
            Endpoint("GET", "/orders", "new", "订单列表"),
            Endpoint("POST", "/orders", "new", "创建订单"),
            Endpoint("GET", "/users", "new", "用户列表"));
        var select = new SyncSelect();
        select.Set("GET /orders", true);
        select.Set("GET /users", true);
        select.Query = "创建";

        // 框里有字、焦点在框上：这一次 Esc 归筛选（不是取消对话框）。
        Assert.True(ListFilter.EscClearsInDialog(keywordFocused: true, hasKeyword: ListFilter.IsActive(select.Query)));
        Assert.Single(select.Visible(diff));

        // Ui.TryEscapeFilter 只做 keyword.Clear()，重建由既有的 TextChanged 触发——这里等价地清空关键词。
        select.Query = "";

        Assert.Equal(3, select.Visible(diff).Count);
        Assert.Equal(["GET /orders", "GET /users"], select.CheckedKeys(diff));
        Assert.Equal(2, select.CheckedCount);
    }

    [Fact]
    public void Escape_LeavesTheKeyToCancelWhenTheBoxIsNotFocused()
    {
        // 焦点不在框上时 Esc 仍是取消对话框：框里留着字不构成把「取消」从用户手里拿走的理由。
        // 本页不另写一套判定，认的就是另六张长清单那一份。
        Assert.False(ListFilter.EscClearsInDialog(keywordFocused: false, hasKeyword: true));
        Assert.False(ListFilter.EscClearsInDialog(keywordFocused: true, hasKeyword: false));
    }

    // ---------- 接线机械检查：口径存在但对话框没接线等于没有 ----------

    [Fact]
    public void Dialog_WiresTheThreeGesturesThroughTheSharedEntries()
    {
        var source = Repo.Source("Desktop", "Native", File);
        var body = Slice(source, "protected override bool ProcessDialogKey(Keys keyData)", "return base.ProcessDialogKey(keyData);");

        // 只经 Ui 的三处共享入口接线：本对话框不就地认键、不就地拆修饰键、不自己选中或滚动、不自己清空关键词。
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryFocusFilter\(keyData, _keyword\)\) return true;$", body);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEnterFilter\(keyData, _keyword, _selectList\)\) return true;$", body);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEscapeFilter\(keyData, _keyword\)\) return true;$", body);
        Assert.DoesNotContain("SelectItem", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusFirstResult", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Clear()", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ListKeys.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ListFilter.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellShortcuts.", body, StringComparison.Ordinal);

        // 全文件的代码行里没有 Keys.*（注释里写手势名是说明理由，不算第二套判定）。
        Assert.Empty(CodeLines(source, "Keys."));
        // 也不另开一条按键通路：对话框只覆盖对话键，不覆盖命令键与控件级 KeyDown。
        Assert.DoesNotContain("ProcessCmdKey", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OnKeyDown", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_KeepsTheGesturesInsideTheSelectPageGate()
    {
        // 形状锁：关键词框只在勾选页上，三个手势必须在 _page1.Visible 之内——预览页没有这个框，
        // 门控之外接住按键等于在预览页上吞掉 Ctrl+F、把 Esc 从「取消」手里拿走。
        var source = Repo.Source("Desktop", "Native", File);
        var expected = string.Join('\n',
        [
            "    protected override bool ProcessDialogKey(Keys keyData)",
            "    {",
            "        if (_page1.Visible)",
            "        {",
            "            if (Ui.TryFocusFilter(keyData, _keyword)) return true;",
            "            if (Ui.TryEnterFilter(keyData, _keyword, _selectList)) return true;",
            "            if (Ui.TryEscapeFilter(keyData, _keyword)) return true;",
            "        }",
            "        return base.ProcessDialogKey(keyData);",
            "    }",
        ]);
        Assert.Contains(expected, source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_GatesOnTheSelectPageInExactlyOnePlace()
    {
        var source = Repo.Source("Desktop", "Native", File);
        var body = Slice(source, "protected override bool ProcessDialogKey(Keys keyData)", "return base.ProcessDialogKey(keyData);");
        // 门控只有一处，且排在三个手势之前（先判这一页有没有那个框，再认键）。
        Assert.Single(Regex.Matches(body, @"_page1\.Visible"));
        var gate = body.IndexOf("if (_page1.Visible)", StringComparison.Ordinal);
        var find = body.IndexOf("Ui.TryFocusFilter", StringComparison.Ordinal);
        var enter = body.IndexOf("Ui.TryEnterFilter", StringComparison.Ordinal);
        var escape = body.IndexOf("Ui.TryEscapeFilter", StringComparison.Ordinal);
        Assert.True(gate >= 0 && find > gate && enter > find && escape > enter,
            "顺序应为「勾选页可见 → Ctrl+F → Enter → Esc」。");
        // 不另写反向判定（「预览页可见时……」）：一次按键只有一个含义，门控只有这一处。
        Assert.DoesNotContain("!_page1.Visible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_page2.Visible", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_KeepsEnterAsConfirmAndEscapeAsCancel()
    {
        // 对话框的 Enter=确认、Esc=取消逐字不变：三个手势只在关键词框取得焦点时接住按键
        // （Ui.TryEnterFilter/TryEscapeFilter 的焦点门控），焦点在别处时仍走窗体自己的对话键。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Matches(@"(?m)^\s*AcceptButton = _closeButton;$", source);
        Assert.Matches(@"(?m)^\s*CancelButton = _closeButton;$", source);
        // 本对话框不自己接管这两个键：代码行里没有 Keys.Enter / Keys.Escape。
        Assert.Empty(CodeLines(source, "Keys.Enter"));
        Assert.Empty(CodeLines(source, "Keys.Escape"));
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheSelectPageGestures()
    {
        var row = DesignRow(6, "列表筛选");
        Assert.Contains("勾选页的三个手势", row, StringComparison.Ordinal);
        Assert.Contains("第七十三轮", row, StringComparison.Ordinal);
        // 这一页与那六张的差别要写清：关键词框只在勾选页上，预览页不接这三个手势。
        Assert.Contains("关键词框只在勾选页上", row, StringComparison.Ordinal);
        Assert.Contains("预览页", row, StringComparison.Ordinal);
        // 勾选挂在行上，退出筛选不能丢勾选。
        Assert.Contains("勾选不因退出筛选而丢", row, StringComparison.Ordinal);
        // 旧措辞「三个手势本轮未接」不得留着冒充现状。
        Assert.DoesNotContain("三个手势本轮未接", row, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var row = DesignRow(8, "同步预览勾选页的三个手势（§6）");
        Assert.Contains("第七十三轮", row, StringComparison.Ordinal);
        Assert.Contains("TryFocusFilter", row, StringComparison.Ordinal);
        Assert.Contains("TryEnterFilter", row, StringComparison.Ordinal);
        Assert.Contains("TryEscapeFilter", row, StringComparison.Ordinal);
        Assert.Contains("_page1.Visible", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        // 上一轮那一行仍留着当时的挂账理由，并把挂账指向本轮收口。
        var previous = DesignRow(8, "同步预览勾选页的关键词框（§6）");
        Assert.Contains("第七十三轮", previous, StringComparison.Ordinal);
        Assert.DoesNotContain("三个手势本轮未接（另六张长清单在第六十九至七十一轮各接一次），真机上的输入手感、筛选行排布与勾选在重建后的可见状态随 G-D5 复核 |", previous, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimWhatThisRoundDidNotDo()
    {
        var section = Repo.DesignSection(6);
        // 本轮没有给预览页加关键词框、没有改勾选口径、没有新增视觉语言。
        Assert.DoesNotContain("预览页也有关键词框", section, StringComparison.Ordinal);
        Assert.DoesNotContain("勾选页新增视觉", section, StringComparison.Ordinal);
        // 可同步口径与两个批量动作的作用域逐字不动——条款里要写明这一点。
        Assert.Contains("可同步口径", section, StringComparison.Ordinal);
    }

    // ---------- 辅助 ----------

    private static string DesignRow(int section, string clause)
    {
        var lines = Repo.DesignSection(section).Split('\n');
        var row = lines.SingleOrDefault(l => l.StartsWith($"| {clause} |", StringComparison.Ordinal));
        Assert.True(row is not null, $"DESIGN.md §{section} 缺少「{clause}」这一行。");
        return row!;
    }

    private static CheckResult Diff(params EndpointCheck[] endpoints) => new() { Endpoints = [.. endpoints] };

    private static EndpointCheck Endpoint(string method, string path, string status, string? name = null) => new()
    {
        Key = method + " " + path,
        Method = method,
        Path = path,
        Status = status,
        RemoteName = name,
        InRemote = true,
    };

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }

    /// <summary>出现该字面量的代码行（注释行跳过：注释里引用这句话是说明理由，不是第二套拼写）。</summary>
    private static List<string> CodeLines(string text, string fragment) =>
        text.Split('\n').Where(l =>
        {
            var trimmed = l.TrimStart();
            return trimmed.Contains(fragment, StringComparison.Ordinal)
                && !trimmed.StartsWith("//", StringComparison.Ordinal)
                && !trimmed.StartsWith("*", StringComparison.Ordinal);
        }).ToList();
}
