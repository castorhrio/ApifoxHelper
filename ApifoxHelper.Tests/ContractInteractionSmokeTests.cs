using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 响应契约对话框的真实控件交互门（第一百八十二轮，简报 P0-2「让降级路径真正可交付」）。
/// <para>
/// <b>要解决的问题</b>：降级答辩逐屏表第 7 行（响应契约对话框）此前写的是「构造冒烟 +
/// <c>ContractReviewService</c> 用例；真实点击未跑」，结论口径「源码 + 编译证据」——也就是说
/// 这一屏的审阅动作（确认 / 拒绝 / 重置）与「写任务数据并递增任务版本使旧检查过期」这件事，
/// 从来没有在真实控件上走过一次，答辩现场只能说「代码与用例在，真实端到端未跑」。
/// 本轮把它补成冒烟段 <c>RunContractsInteraction</c> 的五段真实控件交互，并把逐屏表那一行改成
/// 「wine 量到」。
/// </para>
/// <para>
/// <b>这一节凭什么不是又一次自述</b>：本文件不跑 wine（跑法是 <c>.winecheck/demo.sh</c>），
/// 它钉的是「这一段真的接在冒烟器上、真的点按钮、真的读回磁盘、真的核版本号」这几层——
/// 把这一段掏空成「构造一下就算过」、把按钮接线改掉、把逐屏表那一行改回「源码 + 编译证据」，
/// 都会在这里红，不必等到答辩现场才发现讲稿与代码对不上。
/// </para>
/// </summary>
public class ContractInteractionSmokeTests
{
    private static string SmokeProgram => Repo.Source("ApifoxHelper.Smoke", "Program.cs");

    private static string Dialog => Repo.Source("Desktop", "Native", "ContractsDialog.cs");

    private static string Notes => Repo.Source("docs", "NOTES.md");

    /// <summary>
    /// 冒烟段本体：从方法签名切到下一个 <c>/// &lt;summary&gt;</c> 之前（紧随其后的是夹具方法）。
    /// 往后数固定长度会吃到夹具，判到的就不是这一段了。
    /// </summary>
    private static string Section()
    {
        var program = SmokeProgram;
        var start = program.IndexOf("private static void RunContractsInteraction(", StringComparison.Ordinal);
        Assert.True(start >= 0, "冒烟器里没有 RunContractsInteraction——响应契约的真实控件交互段被删了。");
        var end = program.IndexOf("\n    /// <summary>", start, StringComparison.Ordinal);
        Assert.True(end > start, "RunContractsInteraction 的方法体切不出边界。");
        return program[start..end];
    }

    /// <summary>
    /// 段注册与调用点：标题带 <c>nameof</c>（段改名即编译不过，演示脚本按段名对账），
    /// 且调用点是一行真代码——注释掉的调用在报告里不会出现这一段，演示脚本会判「对不上」。
    /// </summary>
    [Fact]
    public void Smoke_RunsTheContractsInteractionSection()
    {
        Assert.Contains("Section(\"响应契约对话框（真实控件交互）\", nameof(RunContractsInteraction))", SmokeProgram);
        Assert.True(CodeScan.HasLine(SmokeProgram, "RunContractsInteraction(services, dataRoot);"),
            "冒烟器没有真的调用 RunContractsInteraction（注释掉的调用不算接线）。");
    }

    /// <summary>
    /// 五段都在：夹具落盘 → 打开即选中第一条与门控 → 确认落盘且版本递增 → 阻断契约确认禁用 →
    /// 重置回待确认。少一段就说明这一段被掏空了，逐屏表那一行「wine 量到」也就不成立。
    /// </summary>
    [Fact]
    public void Section_CoversTheFiveReviewSteps()
    {
        var section = Section();
        string[] steps =
        [
            "响应契约：准备契约清单（真实落盘）",
            "响应契约：打开即选中第一条且计数行与动作门控对得上（真实控件）",
            "响应契约：确认后契约落盘且任务版本递增（真实控件）",
            "响应契约：阻断契约的「确认」是禁用的且详情区给出未解析点（真实控件）",
            "响应契约：重置回待确认（真实控件）",
        ];
        foreach (var step in steps)
        {
            Assert.Contains($"RunAction(\"{step}\"", section);
        }
    }

    /// <summary>
    /// 真的点按钮、真的读回磁盘、真的核版本号——这三件事是「真实控件交互」与「构造冒烟」的分界。
    /// <para>
    /// 另钉两处容易写成假门的地方：① 必须 <c>dialog.Show()</c>——不显示窗体时 <c>Control.Visible</c>
    /// 恒为 <c>false</c>，判可见性会得到一条永远绿的假门（第一百七十六轮同一族）；
    /// ② 异步续体必须泵消息（<c>PumpUntilCondition</c>），否则 <c>async void</c> 的审阅动作永远等不到。
    /// </para>
    /// </summary>
    [Fact]
    public void Section_ClicksRealButtonsAndReadsTheDiskBack()
    {
        var section = Section();
        Assert.True(CodeScan.HasLine(section, "dialog.Show();"), "这一段没有真的显示对话框（不显示就判可见性＝假门）。");
        Assert.True(CodeScan.HasLine(section, "Click(dialog, \"确认\");"), "这一段没有真的点「确认」。");
        Assert.True(CodeScan.HasLine(section, "Click(dialog, \"重置\");"), "这一段没有真的点「重置」。");
        Assert.True(CodeScan.HasLine(section, "PumpUntilCondition("), "异步审阅动作没有泵消息，等不到状态行变化。");
        // 磁盘回读：确认后、阻断契约核对、重置后各一次，少一次就说明有一段只看界面不看盘。
        Assert.True(CodeScan.CountContaining(section, "tasks.Read<List<ResponseContract>>(task.Id, TaskFiles.ResponseContract)") >= 3,
            "这一段读回 responseContract.json 的次数少于 3 次——有步骤只看了界面、没核磁盘。");
        // 版本号：确认与重置各核一次「+1」（旧检查与旧计划要过期）。
        Assert.True(CodeScan.CountContaining(section, "tasks.Get(task.Id).Revision") >= 2,
            "这一段没有核任务版本递增——「确认后旧检查过期」这件事就没被量到。");
        // 门控：阻断契约的「确认」必须是禁用的（不是点了才报错）。
        Assert.True(CodeScan.HasLine(section, "if (ButtonOf(dialog, \"确认\").Enabled)"),
            "这一段没有判「阻断契约的确认按钮是禁用的」。");
    }

    /// <summary>
    /// 夹具里必须真有一条带未解析点的阻断契约：否则「阻断契约的确认是禁用的」那一段是空断言
    /// （没有阻断契约时它判的是「找不到这一行」而不是「按钮被挡住」）。
    /// </summary>
    [Fact]
    public void Fixture_HasABlockedContractWithUnresolvedPoints()
    {
        var program = SmokeProgram;
        var start = program.IndexOf("private static List<ResponseContract> ContractsFixture(", StringComparison.Ordinal);
        Assert.True(start >= 0, "冒烟器里没有 ContractsFixture——响应契约夹具被删了。");
        var end = program.IndexOf("\n    /// <summary>", start, StringComparison.Ordinal);
        var fixture = end > start ? program[start..end] : program[start..];
        Assert.Contains("ReviewState = \"blocked\"", fixture);
        Assert.Contains("UnresolvedPoints = [", fixture);
        Assert.Contains("ReviewState = \"pending\"", fixture);
        Assert.Contains("ReviewState = \"rejected\"", fixture);
    }

    /// <summary>
    /// 三个审阅动作真的接在共享 <c>ContractReviewService</c> 上：冒烟里点的是按钮，
    /// 按钮背后必须是同一个服务入口（写任务数据 + 递增版本），不是界面自己改内存。
    /// </summary>
    [Fact]
    public void Dialog_WiresTheThreeReviewActionsToTheSharedService()
    {
        var dialog = Dialog;
        Assert.True(CodeScan.HasLine(dialog, "_confirmButton.Click += (_, _) => RunReview(\"confirmed\");"),
            "「确认」按钮没有接到 RunReview(\"confirmed\")。");
        Assert.True(CodeScan.HasLine(dialog, "_rejectButton.Click += (_, _) => RunReview(\"rejected\");"),
            "「拒绝」按钮没有接到 RunReview(\"rejected\")。");
        Assert.True(CodeScan.HasLine(dialog, "_resetButton.Click += (_, _) => RunReview(\"pending\");"),
            "「重置」按钮没有接到 RunReview(\"pending\")。");
        Assert.True(CodeScan.HasLine(dialog, "await _reviews.ReviewAsync(_task.Id, endpointKey, reviewState, CancellationToken.None);"),
            "三个动作没有走共享 ContractReviewService.ReviewAsync（界面自己改内存不算审阅）。");
    }

    /// <summary>
    /// 逐屏表第 7 行跟着改：引用的段名必须真存在（<c>DemoScriptTests.EveryCitedSegment_HasADefinitionAndASectionId</c>
    /// 判这一半），结论口径必须是「wine 量到」而不是「源码 + 编译证据」——这一行是本轮唯一被推进的屏，
    /// 表不改就等于讲稿还在说「真实点击未跑」。
    /// </summary>
    [Fact]
    public void ScreenTable_UpgradesTheContractsRowToWineEvidence()
    {
        var notes = Notes;
        var start = notes.IndexOf("## 降级答辩脚本", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/NOTES.md 缺「## 降级答辩脚本」一节。");
        var end = notes.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = end < 0 ? notes[start..] : notes[start..end];
        var row = section.Split('\n').FirstOrDefault(line => line.StartsWith("| 7 |", StringComparison.Ordinal));
        Assert.True(row is not null, "逐屏表里没有第 7 行（响应契约对话框）。");
        Assert.Contains("RunContractsInteraction", row!);
        Assert.Contains("wine 量到", row!);
        Assert.DoesNotContain("源码 + 编译证据", row!);
        Assert.DoesNotContain("真实点击未跑", row!);
        // 「只有源码与编译证据的屏」那份名单里不许再有第 7 行：名单与表必须同一口径。
        Assert.Contains("第 6/8/11 行", section);
        Assert.DoesNotContain("第 6/7/8/11 行", section);
    }
}
