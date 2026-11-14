using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 清单打开时的键盘归属（DESIGN.md §6「列表行」、§8 第八十八与第八十九轮）：打开即选中一行、详情区已经显示那一行，
/// 而对话框打开时键盘落在框架 tab 序的第一个可聚焦控件上——方向键动的是别的控件、Ctrl+C 也复制不到那一行，
/// 「打开就有一行被标出来」只成立了一半（第五十八轮补的是「处理完一条之后」那一半）。
/// 第八十八轮接上五张只读长清单（检查结果、接口追踪明细、生成文档核对、代码证据、任务日志），
/// 第八十九轮接上差异确认——三个审阅视图里只有它打开时自动选中一行（注释候选与响应契约当时打开时不选中任何一行，
/// 没有落点可交，故不接）。第九十轮把这两处的落点补上（<c>Ui.SelectFirstOnOpen</c>），它们随之接上同一处交键盘；
/// 第九十一轮把注释缺口清单的落点补上（它此前是最后一张「有选中动作、打开却不落点」的清单），一并接上；
/// 第九十五轮把补充说明的接口清单接上（它打开即选中第一个接口，是最后一张「打开即选中一行、键盘却不在清单上」的清单），
/// 至此十张清单打开时都把键盘交给清单。第二百八十六轮把同步预览的勾选页接上（它此前是最后一张
/// 「打开即选中一行、键盘却不在清单上」的清单：勾选页打开时一行都没选中，方向键没有落点、空格勾不上任何一行，
/// 第二百八十四轮接上的 Ctrl+A 全选手势也没有一个被标出来的落点），至此十一张。
/// 每一轮都经 <c>Ui.KeepKeyboardOnQueue</c> 同一处把键盘交给清单，判定仍是
/// <see cref="ListFocus.ShouldHandToQueue"/>（选中行与焦点行是同一行才交），接在 OnShown 而不是 OnLoad。
/// 真机上打开对话框后方向键是否直接走清单随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class OpenFocusTests
{
    /// <summary>
    /// 打开即选中一行、因而打开时该把键盘交给清单的十一张清单
    /// （五张只读长清单 + 三个审阅视图 + 注释缺口清单 + 补充说明的接口清单 + 同步预览的勾选页）。
    /// </summary>
    private static readonly string[] OpenPaths =
    [
        "CheckResultsDialog.cs",
        "EndpointTraceDialog.cs",
        "PropagationDialog.cs",
        "EvidenceDialog.cs",
        "JournalDialog.cs",
        "DecisionsDialog.cs",
        "CandidatesDialog.cs",
        "ContractsDialog.cs",
        "ScanResultsDialog.cs",
        "SupplementsDialog.cs",
        "SyncPreviewDialog.cs",
    ];

    /// <summary>
    /// 这一族里每个对话框的清单字段名：绝大多数对话框只有一张清单（<c>_list</c>），
    /// 同步预览有两页各一张（勾选页 <c>_selectList</c>、预览页 <c>_actionList</c>）——
    /// 交键盘的落点按清单认，不按「文件里恰好一处」认（第二百八十六轮）。
    /// </summary>
    private static string ListField(string file) =>
        file == "SyncPreviewDialog.cs" ? "_selectList" : "_list";

    // ---------- 真执行：打开时那一行是不是键盘该去的那一行 ----------

    [Fact]
    public void OpenPath_HandsTheKeyboardToTheRowTheDialogSelected()
    {
        // 打开路径先经 Ui.SelectItem 选中一行（Selected + Focused 一起落），随后问同一份判定：
        // 选中行与焦点行是同一行才交——打开时那一行正是这样，故放行。
        Assert.True(ListFocus.ShouldHandToQueue(0, 0));
        Assert.True(ListFocus.ShouldHandToQueue(41, 41));
    }

    [Fact]
    public void OpenPath_LeavesTheKeyboardAloneWhenTheScreenOpensEmpty()
    {
        // 打开时那一屏是提示或空态（读取失败、尚未检查、没有条目）：清单里没有选中行，
        // 交键盘等于把方向键交给一个没有落点的清单——那一步的下一步在提示与按钮上。
        Assert.False(ListFocus.ShouldHandToQueue(null, null));
        Assert.False(ListFocus.ShouldHandToQueue(null, 0));
        Assert.False(ListFocus.ShouldHandToQueue(0, null));
    }

    // ---------- 接线：六张清单在 OnShown 里经同一处入口交键盘 ----------

    [Theory]
    [InlineData("CheckResultsDialog.cs")]
    [InlineData("EndpointTraceDialog.cs")]
    [InlineData("PropagationDialog.cs")]
    [InlineData("EvidenceDialog.cs")]
    [InlineData("JournalDialog.cs")]
    [InlineData("DecisionsDialog.cs")]
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    [InlineData("ScanResultsDialog.cs")]
    [InlineData("SupplementsDialog.cs")]
    [InlineData("SyncPreviewDialog.cs")]
    public void OpenPaths_HandTheKeyboardOnShown(string file)
    {
        var body = OnShownBody(file);
        var field = ListField(file);
        // 先 base.OnShown：窗体显示之后再交键盘（OnLoad 时窗体还没显示，Focus 的落点取决于框架激活时的选择）。
        Assert.Matches(@"(?m)^\s*base\.OnShown\(e\);$", body);
        Assert.Single(Regex.Matches(body, @"(?m)^\s*Ui\.KeepKeyboardOnQueue\(" + field + @"\);$"));
        var baseCall = body.IndexOf("base.OnShown(e);", StringComparison.Ordinal);
        var hand = body.IndexOf($"Ui.KeepKeyboardOnQueue({field});", StringComparison.Ordinal);
        Assert.True(baseCall >= 0 && hand > baseCall, $"{file}:应在 base.OnShown 之后交键盘。");
    }

    [Theory]
    [InlineData("CheckResultsDialog.cs")]
    [InlineData("EndpointTraceDialog.cs")]
    [InlineData("PropagationDialog.cs")]
    [InlineData("EvidenceDialog.cs")]
    [InlineData("JournalDialog.cs")]
    [InlineData("DecisionsDialog.cs")]
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    [InlineData("ScanResultsDialog.cs")]
    [InlineData("SupplementsDialog.cs")]
    [InlineData("SyncPreviewDialog.cs")]
    public void OpenPaths_TouchNothingButTheKeyboard(string file)
    {
        // 选中、滚动、重建各有自己的唯一入口：打开时这一处只交键盘，不顺手写第二套（必然与那一套漂移）。
        var body = OnShownBody(file);
        Assert.DoesNotContain("SelectItem", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Items[0]", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Items[^1]", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateList", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyFilter", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Items.Clear", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenPaths_AreExactlyTheElevenListsThatSelectARowOnOpen()
    {
        // 打开时交键盘的清单恰十一张：五张只读长清单（第八十八轮）、差异确认（第八十九轮）、
        // 注释候选与响应契约（第九十轮补上落点后一并接上）、注释缺口清单（第九十一轮补上落点后一并接上）、
        // 补充说明的接口清单（第九十五轮）、同步预览的勾选页（第二百八十六轮）——
        // 它们的共同点是「打开即选中一行、详情区已经显示那一行」。
        // 这条机械检查是「打开时交键盘的清单」与「打开即选中一行的清单」两件事不许漂移的钉子：
        // 谁在 OnShown 里交键盘，谁就必须在打开时真的有一行被选中（否则判定会答「不交」，接上去是一句空话）。
        // 壳的 OnShown（第九十六轮）是启动时把键盘交给当前页列表：它交的是页的方法，
        // 不是自己写 Ui.KeepKeyboardOnQueue(_list)，故不在这一族里（见 PageOpenFocusTests）。
        var withOnShown = Repo.NativeSources()
            .Where(s => s.Contains("protected override void OnShown"))
            .Select(s => s.Name)
            .Where(name => name != "NativeShellForm.cs")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(OpenPaths.OrderBy(name => name, StringComparer.Ordinal), withOnShown);
        foreach (var file in OpenPaths)
        {
            // 每张清单在 OnShown 里恰一处（打开时交键盘）；三个审阅视图另有一处「处理完一条之后」的交回（第五十八轮），
            // 五张只读清单没有「刚处理的一条」，故整份源码恰一处。同步预览有两页各一张清单：
            // 勾选页两处（打开时、回到勾选页时），预览页一处（那一页由隐藏变可见时，第九十二轮）——
            // 交键盘按清单认，故这里按清单分别数。
            var field = ListField(file);
            var source = Repo.Source("Desktop", "Native", file);
            var expected = file switch
            {
                "DecisionsDialog.cs" or "CandidatesDialog.cs" or "ContractsDialog.cs" => 2,
                "SyncPreviewDialog.cs" => 2,
                _ => 1,
            };
            Assert.Equal(expected, Regex.Matches(source, $@"Ui\.KeepKeyboardOnQueue\({field}\);").Count);
        }
    }

    [Theory]
    [InlineData("ApplyPlanDialog.cs")]
    public void ViewsWithoutARowOnOpen_DoNotTakeTheKeyboardOnOpen(string file)
    {
        // 注释回写的数据在 OnLoad 之后异步到达（打开时清单还是空的，没有落点可交），故它不接「打开时」这一口径——
        // 它的文件清单在计划首次到达那一次交键盘（第九十三轮，见 ApplyPlanOpenFocusTests）。
        // 同步预览的勾选页第二百八十六轮补上落点后已接此口径（见 OpenPaths），
        // 它的预览页（写入清单）仍是「这一页由隐藏变可见那一次交」（第九十二轮，见 PlanOpenFocusTests）。
        // 注释候选与响应契约第九十轮补上落点后已接此口径，注释缺口清单第九十一轮补上落点后一并接上，
        // 补充说明的接口清单第九十五轮接上（它打开即选中第一个接口，见 SupplementOpenFocusTests），
        // 故都不在此列（见 OpenPaths）。
        var source = Repo.Source("Desktop", "Native", file);
        Assert.DoesNotContain("OnShown", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Judgment_StaysInTheSharedRule()
    {
        // 判定只有一份：界面侧调用它的地方只有工厂入口，六个对话框不自己补一套序号比较。
        var callers = Repo.NativeSources()
            .Where(s => s.Contains("ListFocus.ShouldHandToQueue"))
            .Select(s => s.Name)
            .ToList();
        Assert.Equal(new[] { "Ui.cs" }, callers);
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsOpenFocus()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**只读清单打开时的键盘归属**（`ListFocus` 同一口径，2026-11-08 第八十八轮）", section);
        Assert.Contains("打开时经 `Ui.KeepKeyboardOnQueue` 同一处把键盘交给清单", section);
        Assert.Contains("接在 `OnShown`（窗体首次显示之后）而不是 `OnLoad`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 只读清单打开时的键盘归属（§6） \|", ledger);
        Assert.Contains("第八十八轮", ledger);
        Assert.Contains("OnShown", ledger);
    }

    [Fact]
    public void DesignDoc_RecordsTheReviewOpenFocus()
    {
        // 第八十九轮：三个审阅视图里只有差异确认打开时自动选中一行，故只有它接此口径；
        // 第九十轮补上另两处的落点后一并接上，第八十九轮那条如实记成「当时不接」并指向下一轮。
        var section = Repo.DesignSection(6);
        Assert.Contains("**审阅清单打开时的键盘归属**（`ListFocus` 同一口径，2026-11-08 第八十九轮）", section);
        Assert.Contains("只有差异确认打开时自动选中一行", section);
        Assert.Contains("**注释候选与响应契约当时不接此口径**", section);
        Assert.Contains("第九十轮补上落点后一并接上", section);
        Assert.Contains("**注释回写仍不接**", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 审阅清单打开时的键盘归属（§6） \|", ledger);
        Assert.Contains("第八十九轮", ledger);
        Assert.Contains("OnShown", ledger);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimMoreThanWasDone()
    {
        // 第八十八轮没做的事不能被写成做了：注释回写、同步预览的预览页、补充说明、三个审阅对话框与注释缺口都不接此口径，
        // 台账里如实挂账，且不得出现「全部对话框打开时初始焦点已接线」这类话。
        // 第二百八十六轮起同步预览的**勾选页**接上了（它打开即选中第一条接口），但**预览页**（写入清单）仍不接——
        // 它在「这一页由隐藏变可见」那一次交键盘（第九十二轮），两页各一条口径。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 只读清单打开时的键盘归属（§6） |", "\n| 审阅清单打开时的键盘归属（§6） |");
        Assert.Contains("注释回写（数据在 OnLoad 之后异步到达", row);
        Assert.Contains("同步预览的**预览页**", row);
        Assert.Contains("其**勾选页**第二百八十六轮已接此口径", row);
        Assert.Contains("补充说明（编辑位是这一屏的输入）", row);
        Assert.DoesNotContain("全部对话框打开时初始焦点已接线", row, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimTheOtherReviewDialogsWereWired()
    {
        // 第八十九轮只接了差异确认：注释候选与响应契约当时打开时不选中任何一行（没有落点可交），
        // 注释回写的数据异步到达（到达时用户可能正在关键词框里打字）——两处都如实挂账，不写成已接线。
        // 第九十轮补上落点后，台账里第八十九轮那一行仍须说清「当时不接」并指向下一轮，不得改写成「当时就接了」。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 审阅清单打开时的键盘归属（§6） |", "\n| 审阅队列打开即选中第一条（§6） |");
        Assert.Contains("注释候选与响应契约当时不接此口径", row);
        Assert.Contains("第九十轮补上落点后一并接上", row);
        Assert.Contains("注释回写仍不接", row);
        Assert.DoesNotContain("全部审阅对话框打开时初始焦点已接线", row, StringComparison.Ordinal);
        Assert.DoesNotContain("三个审阅对话框都已接线", row, StringComparison.Ordinal);
    }

    /// <summary>OnShown 的方法体（无花括号的线性序列，切片取到方法末尾的右括号为止）。</summary>
    private static string OnShownBody(string file)
    {
        var source = Repo.Source("Desktop", "Native", file);
        var match = Regex.Match(source, @"protected override void OnShown\(EventArgs e\)\s*\{(?<body>[^}]*)\}");
        Assert.True(match.Success, $"{file}:缺少 OnShown（打开时把键盘交给清单的唯一时机）。");
        return match.Groups["body"].Value;
    }

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
