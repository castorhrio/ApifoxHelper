using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 对话框主动作的键盘入口（Ctrl+Enter，第三百五十九轮）的断言。
/// <para>
/// <b>补的是哪个缺口</b>：三个审阅对话框（注释候选 / 响应契约 / 差异确认）的主动作是
/// 「接受并下一条」「确认」「确认纳入」——逐条清队列时这一个动作要按几十次。它此前只有两条路：
/// 鼠标点，或 Tab 到那颗按钮再按空格。而这三个对话框的 <c>AcceptButton</c> 是「关闭」
/// （<c>Enter</c> 在对话框里是「确认/关闭」的既有语义，§6「菜单/快捷键」那一行），
/// 于是「键盘上按一下就走下一条」在整只壳里没有入口：审阅五十条候选要按五十次 Tab。
/// </para>
/// <para>
/// 门分两段，各自只证明自己那一段：① 真执行段跑的是主工程那一份判定（口径文件与测试工程共享编译），
/// 在 Linux 上真的执行；② 接线段读的是源码形状（装在哪、装在谁身上、点按钮是不是只有一处）——
/// WinForms 的按键投递在 Linux 上跑不起来，故这一段只能读形状，<b>不声称它运行过</b>；
/// 真机上的按键手感随 G-D5 复核。冒烟器另有一段在真实控件上真的按一次 Ctrl+Enter
/// （<c>RunPrimaryActionGesture</c>，wine 下跑）。
/// </para>
/// </summary>
public class PrimaryActionKeysTests
{
    /// <summary>登记了主动作的三个审阅对话框（本屏的主动作按钮 → 那一屏）。</summary>
    private static readonly (string File, string Button)[] ReviewDialogs =
    [
        ("CandidatesDialog.cs", "_acceptButton"),
        ("ContractsDialog.cs", "_confirmButton"),
        ("DecisionsDialog.cs", "_confirmButton"),
    ];

    // ---------- ① 真执行段：跑的是主工程那一份判定 ----------

    /// <summary>
    /// 八种修饰键组合逐一过一遍：只认裸 Ctrl。带 Shift/Alt 的组合不属本手势、留给控件自身
    /// （不吞键，也不给既有组合键发明新语义）。
    /// </summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void OnlyBareControl_IsThePrimaryActionGesture(bool shift, bool control, bool alt) =>
        Assert.Equal(control && !shift && !alt, PrimaryActionKeys.IsPrimaryActionGesture(shift, control, alt));

    /// <summary>
    /// 本仓四只 Ctrl 手势（找、复制、全选、主动作）拆修饰键必须是同一个形状：只认 Ctrl，
    /// 带 Shift/Alt 的组合不吞键。这条门让「同一纪律」成为可核对的，而不是各文件里的一句自述。
    /// </summary>
    [Fact]
    public void TheFourCtrlGestures_SplitTheModifiersTheSameWay()
    {
        for (var mask = 0; mask < 8; mask++)
        {
            var shift = (mask & 4) != 0;
            var control = (mask & 2) != 0;
            var alt = (mask & 1) != 0;
            var expected = control && !shift && !alt;
            Assert.Equal(expected, PrimaryActionKeys.IsPrimaryActionGesture(shift, control, alt));
            Assert.Equal(expected, ListChecks.IsSelectAllGesture(shift, control, alt));
            Assert.Equal(expected, ListCopy.IsCopyGesture(shift, control, alt));
            Assert.Equal(expected, ShellShortcuts.IsFindGesture(shift, control, alt));
        }
    }

    /// <summary>
    /// 手势成立还不够：按钮此刻可用、可见才动作。三条都成立才点下去。
    /// </summary>
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, false)]
    public void ShouldRun_NeedsGestureAndEnabledAndVisible(bool gesture, bool enabled, bool visible, bool expected) =>
        Assert.Equal(expected, PrimaryActionKeys.ShouldRun(gesture, enabled, visible));

    /// <summary>
    /// 真执行段跑的是主工程那一份：口径文件必须与测试工程共享编译，
    /// 否则上面那两张真值表量的是一份拷贝，改主工程它不响。
    /// </summary>
    [Fact]
    public void TheGestureRule_IsSharedWithTheMainProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\PrimaryActionKeys.cs", csproj, StringComparison.Ordinal);
    }

    /// <summary>
    /// 判定文件不碰 WinForms：它要能在这台 Linux 上真的跑，故不得引控件类型。
    /// <para>
    /// <b>判的是代码行，不是散文（第三百五十九轮实测）</b>：这一条起初拿整份源码判
    /// <c>Button</c> 这个子串，而口径文件的类注释里写着「这三个对话框的 <c>AcceptButton</c> 是「关闭」」——
    /// 那句注释正是这一条要讲清的理由，却被读成「引了控件类型」。注释里提到一个类型名不是接线，
    /// 与 <see cref="CodeScan"/> 那一族「注释里引用同一行字是在说明理由、不是在接线」同一纪律，
    /// 故这里按 <see cref="CodeScan.Text"/> 去掉注释再判。
    /// </para>
    /// </summary>
    [Fact]
    public void TheGestureRule_DoesNotDependOnWinForms()
    {
        var code = CodeScan.Text(Repo.Source("Desktop", "Native", "PrimaryActionKeys.cs"));
        Assert.DoesNotContain("System.Windows.Forms", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Button", code, StringComparison.Ordinal);
        // 判据自己被抓得住：喂它一行真的引了控件类型的代码，它必须报出来。
        Assert.Contains("Button", CodeScan.Text("private Button? _primary;"), StringComparison.Ordinal);
    }

    // ---------- ② 接线段：读源码形状（WinForms 投递在 Linux 上跑不起来，不声称它运行过） ----------

    /// <summary>
    /// 三个审阅对话框各登记一次主动作，登记的是本屏那颗主动作按钮本身
    /// （不是「点下去做什么」——可用性与可见性由按钮自己带着）。
    /// </summary>
    [Theory]
    [MemberData(nameof(ReviewDialogCases))]
    public void EachReviewDialog_RegistersItsPrimaryActionButton(string file, string button)
    {
        var code = CodeScan.Text(Repo.Source("Desktop", "Native", file));
        Assert.Matches($@"(?m)^\s*PrimaryActionButton = {Regex.Escape(button)};$", code);
    }

    public static TheoryData<string, string> ReviewDialogCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (file, button) in ReviewDialogs) data.Add(file, button);
        return data;
    }

    /// <summary>
    /// 登记点恰好这三处：多一处就是「第四屏也接了」，那一屏的主动作是什么得先说清楚；
    /// 少一处就是某一屏的主动作在键盘上没有入口（本切片要补的正是这个）。
    /// </summary>
    [Fact]
    public void TheGesture_IsRegisteredInExactlyThreePlaces()
    {
        var holders = Repo.NativeSources()
            .Where(s => CodeScan.HasLine(s.Text, "PrimaryActionButton = "))
            .Select(s => s.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            ReviewDialogs.Select(d => d.File).OrderBy(name => name, StringComparer.Ordinal).ToList(),
            holders);
    }

    /// <summary>
    /// 点按钮只在一处：<c>PerformClick</c> 在整个原生界面恰出现一次，就在 <c>DpiAwareForm</c> 的
    /// <c>ProcessCmdKey</c> 里。三屏各写一次就是三份「什么时候该点」的判定，迟早对不上。
    /// </summary>
    [Fact]
    public void TheClick_HappensInExactlyOnePlace()
    {
        var holders = Repo.NativeSources()
            .Where(s => CodeScan.HasLine(s.Text, "PerformClick()"))
            .Select(s => s.Name)
            .ToList();
        Assert.Equal(new[] { "DpiAwareForm.cs" }, holders);
    }

    /// <summary>
    /// 判定与点击都在基类一处：<c>ProcessCmdKey</c> 里先问手势、再问按钮此刻可用可见、最后才点；
    /// 不成立的那一支交回基类（不吞键）——「什么都没发生」不许说成做了什么。
    /// </summary>
    [Fact]
    public void TheBaseClass_JudgesThenClicksThenFallsBack()
    {
        var source = Repo.Source("Desktop", "Native", "DpiAwareForm.cs");
        var body = Slice(source, "protected override bool ProcessCmdKey", "return base.ProcessCmdKey");
        Assert.Contains("PrimaryActionButton is { } primary", body, StringComparison.Ordinal);
        Assert.Contains("PrimaryActionKeys.IsPrimaryActionGesture(", body, StringComparison.Ordinal);
        Assert.Contains("PrimaryActionKeys.ShouldRun(", body, StringComparison.Ordinal);
        Assert.Contains("primary.PerformClick();", body, StringComparison.Ordinal);
        Assert.Contains("return true;", body, StringComparison.Ordinal);
        // 判定在点击之前：先问「该不该动作」，再动作。
        Assert.True(
            body.IndexOf("ShouldRun(", StringComparison.Ordinal) < body.IndexOf("PerformClick()", StringComparison.Ordinal),
            "ProcessCmdKey 里先点了按钮再判「该不该动作」：顺序反了。");
    }

    /// <summary>
    /// 三个对话框不就地认键、不就地拆修饰键：认键在基类一处，界面只登记按钮。
    /// （同一族的问题此前在别处出现过：对话框里按 Ctrl+F 没反应，正是「就地写了一份」。）
    /// </summary>
    [Theory]
    [MemberData(nameof(ReviewDialogCases))]
    public void TheDialog_DoesNotRecognizeTheKeyItself(string file, string button)
    {
        var code = CodeScan.Text(Repo.Source("Desktop", "Native", file));
        Assert.DoesNotContain("Keys.Enter", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Control", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Alt", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessCmdKey", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PerformClick", code, StringComparison.Ordinal);
        // 本屏也不许自己点那颗按钮：点下去只经基类那一处（`PerformClick` 全仓恰一处）。
        Assert.DoesNotContain($"{button}.PerformClick", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// 表单类对话框不接这个手势：它们的主动作是「保存/确定」，那一步的入口是那颗按钮本身与它自己的
    /// 校验（保存会落盘、会拒绝非法值），不为它另开一个键盘入口。这一条是<b>豁免登记</b>，
    /// 不是「还没做」——登记在这里，日后谁要给某一屏接上，先改这一条并说清那一屏的主动作为什么是保存。
    /// </summary>
    [Fact]
    public void FormDialogs_AreExemptFromTheGesture()
    {
        // 豁免的是「主动作 = 保存/确定」的那几屏，逐屏写明它为什么是保存：
        // 那一步会落盘、会拒绝非法值，入口是那颗按钮本身与它自己的校验，不为它另开一个键盘入口。
        (string File, string Why)[] exempt =
        [
            ("GenerationDialog.cs", "「生成」会写文档到磁盘"),
            ("ProfileAdvancedDialog.cs", "「保存」会写配置到磁盘"),
            ("ProfileAnalysisDialog.cs", "「保存」会写配置到磁盘"),
            ("ScanResultsDialog.cs", "内含「手工填写说明」子对话框，它的主动作「添加为候选」会写候选"),
        ];
        foreach (var (file, why) in exempt)
        {
            var code = CodeScan.Text(Repo.Source("Desktop", "Native", file));
            Assert.True(
                !code.Contains("PrimaryActionButton = ", StringComparison.Ordinal),
                $"{file} 登记了主动作按钮，而它是豁免屏（{why}）：要给这一屏接上，先改这一条并说清它的主动作为什么是保存。");
        }
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsThePrimaryActionGesture()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("主动作的键盘入口", section, StringComparison.Ordinal);
        Assert.Contains("PrimaryActionKeys.IsPrimaryActionGesture", section, StringComparison.Ordinal);
        Assert.Contains("DpiAwareForm", section, StringComparison.Ordinal);
        // 这一条要写明「不吞键」与「表单类对话框豁免」两件事，否则条款只说了半句。
        Assert.Contains("不吞键", section, StringComparison.Ordinal);
        Assert.Contains("豁免", section, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var section = Repo.DesignSection(8);
        var row = section.Split('\n').FirstOrDefault(l => l.StartsWith("| 主动作的键盘入口（§6） |", StringComparison.Ordinal));
        Assert.NotNull(row);
        Assert.Contains("第三百五十九轮", row!, StringComparison.Ordinal);
        Assert.Contains("PrimaryActionKeys", row!, StringComparison.Ordinal);
        // 台账要如实：接线不证明真机上的按键手感，那一条仍挂在 G-D5 下面。
        Assert.Contains("G-D5", row!, StringComparison.Ordinal);
    }

    // ---------- 辅助 ----------

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
