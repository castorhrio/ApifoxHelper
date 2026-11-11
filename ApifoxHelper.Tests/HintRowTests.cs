using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 两列字段网格里的提示行只经 <c>Ui.HintRow</c> 一处出厂（docs/DESIGN.md §6「提示行」，
/// 第二百零六轮，具名 pass：distill；出处：<c>docs/CRITIQUE.md</c> 第 1 站的「最该改的一处」）。
/// <para>
/// <b>补的是哪个缺口</b>：同一件事此前有四种写法——配置编辑对话框借 <c>AddRow(grid, "", Ui.Caption(…))</c>、
/// 「模块与模板 / 序列化与分析 / 模型服务」三个配置子对话框各带一份逐字相同的私有 <c>AddHint</c>、
/// 新建任务对话框的 <c>AddRow</c> 在空标签格里放一个占位控件。后两种放的是<b>默认尺寸的
/// <c>Panel</c>（200×100）</b>，于是每一行提示都被那个面板撑到 100px 高（三个子对话框 17 行提示、
/// 新建任务 5 行提示与离线模式那一行）。这个根因第一百四十八轮在配置页修过（<c>ProfileFormLayout.SpacerSize</c>
/// 记着理由），当时只改了那一个文件，同一族的三处没跟上——属同壳不同行为，也是「没有一处是选过的」那种脚手架感。
/// </para>
/// <para>
/// <b>本文件判什么</b>：① 工厂只交出形状（字段列一句 Caption、标签列不放控件、上下留白取令牌），不碰文字；
/// ② 二十六处提示行全部经工厂出厂，三份私有 <c>AddHint</c> 不再存在；③ 「空标签 + Caption」这条路不得复活；
/// ④ 行容器不得再往标签列塞占位控件；⑤ 二十六句提示文字逐字与本轮改动之前相同（distill 改的是形状不是话）；
/// ⑥ 设计基线记下这条口径；⑦ wine 侧那一段真实行高量测删不掉（长度与句数在 Linux 判得了，
/// 「这一行实际多高」只有真实控件答得出）。
/// </para>
/// </summary>
public class HintRowTests
{
    private static string UiSource => Repo.Source("Desktop", "Native", "Ui.cs");

    private static string NativeFile(string name) => Repo.Source("Desktop", "Native", name);

    private static string SmokeSource => Repo.Source("ApifoxHelper.Smoke", "Program.cs");

    /// <summary>本轮从四种写法收成一处工厂的五个文件，值 = 该文件里的提示行条数。</summary>
    private static readonly (string File, int Calls)[] CallSites =
    [
        ("ProjectsPage.cs", 4),
        ("TasksPage.cs", 5),
        ("ProfileAdvancedDialog.cs", 6),
        ("ProfileAnalysisDialog.cs", 8),
        ("GenerationDialog.cs", 3),
    ];

    /// <summary>就地字面量的提示文字（二十七句里的二十二句），逐句与本轮改动之前相同。</summary>
    private static readonly string[] LiteralHints =
    [
        // 配置编辑对话框
        "相对路径前缀，用「;」分隔；留空表示只允许导出补丁。",
        "提交号、构建号或版本描述；为空时报告显示「未绑定代码版本」。",
        "分支留空表示主分支。",
        // 新建任务对话框
        "留空则用「配置名 月-日 时:分」。",
        "勾选要检查的模块；都不勾选则检查该 API 项目的全部接口。",
        "补充接口路径前缀，用「,」「;」分隔；可与模块并用。",
        // 模块与模板
        "每行：模块名=路径前缀 1、路径前缀 2，例如 财务=/api/finance/；新建任务时可按模块选择范围。",
        "新建接口创建时的目录与公共设置；只随创建写入这些字段，不克隆脚本、用例或 Mock。",
        "留空表示未指定目录；未指定目录时新接口无法创建。",
        "用「,」分隔。",
        "JSON 对象或数组；留空表示不设置。",
        "填写接口 ID 后提取其目录、状态、服务、鉴权、安全方案、公共 Header 与标签填入上方字段；请检查后再保存。",
        // 序列化与分析
        "与 Swashbuckle SupportNonNullableReferenceTypes 一致。",
        "每行：类型名=数据属性名，例如 PNOSActionResult=Data；用于追踪 data 的实际类型（响应契约）。",
        "留空按目标框架与配置推导（DEBUG;TRACE;NET8_0…）。",
        "数字留空用默认值。",
        "可选：项目名=路径，每行一个；默认按 bin/配置/目标框架 推断。",
        "与 MvcOptions.SuppressAsyncSuffixInActionNames 一致（默认开启）。",
        "只读详情的并发数（1–8）；写入始终串行。",
        "可选；为空时自动查找 PATH 与全局 npm 目录。",
        // 模型服务
        "例如 https://api.openai.com/v1；只发送用户确认的材料。",
        "每次请求最多包含的注释位置数；数字留空用默认值。",
    ];

    /// <summary>工厂只交出形状：加一行、AutoSize、Caption 进字段列、上下留白取令牌——不碰文字、不放占位控件。</summary>
    [Fact]
    public void HintRow_IsTheOnlyFactoryAndCarriesNoSpacer()
    {
        Assert.Single(Regex.Matches(UiSource, @"static Label HintRow\(TableLayoutPanel grid, string text\)"));
        var body = FactoryBody();
        Assert.Contains("grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));", body, StringComparison.Ordinal);
        Assert.Contains("var hint = Ui.Caption(text);", body, StringComparison.Ordinal);
        Assert.Contains("grid.Controls.Add(hint, 1, row);", body, StringComparison.Ordinal);
        Assert.Contains("hint.Margin = new Padding(0, UiTokens.Xs, 0, UiTokens.Xs);", body, StringComparison.Ordinal);
        // 占位控件不得回来：默认尺寸的 Panel（200×100）会把这一行撑到 100px 高。
        Assert.False(body.Contains("new Panel", StringComparison.Ordinal),
            "Ui.HintRow 又往标签列放了占位控件——提示行的死空白正是这么来的（ProfileFormLayout 记着同一根因）。");
        // 文字是调用方交进来的：工厂不加工文字，否则各屏那句提示会被悄悄改写。
        Assert.False(Regex.IsMatch(body, @"Trim\(|Replace\(|Substring|\+ """),
            "Ui.HintRow 改写了传入的提示文字——这一处只管形状，文字口径在各屏自己那一份。");
    }

    /// <summary>
    /// 三份私有 <c>AddHint</c> 不得复活——这一条只管「是否经工厂出厂」，<b>不看条数</b>。
    /// 第二百零七轮（简报 P1-1）把它与下面的登记比对分开：写在一起时，
    /// 下一轮<b>正经新增一屏提示行</b>会被报成「有的地方退回就地拼」，那是把人对着错的方向改代码。
    /// </summary>
    [Fact]
    public void EveryHintRow_GoesThroughTheFactory()
    {
        foreach (var source in Repo.NativeSources())
        {
            Assert.False(Regex.IsMatch(source.Text, @"(?m)^\s*private void AddHint\("),
                $"{source.Name} 又写回一份私有 AddHint——同一件事四种写法就是从这里长回来的。");
        }
    }

    /// <summary>
    /// 工厂调用点的<b>登记</b>要与源码对得上，且对不上时分方向点名（第二百零七轮，简报 P1-1）。
    /// <para>
    /// 原来这里写死 <c>Assert.Equal(26, total)</c> 加逐文件条数：新增一屏提示行时它一定红，
    /// 而红话说的是「退回就地拼形状」——一句把下一位引向错方向的诊断。现在拆成三种情况各自说：
    /// 多了（该登记一处）、少了（该查是不是退回了就地拼）、以及**整个文件没登记 / 登记了一个没有工厂调用的文件**。
    /// 登记本身就是那份名单的权威：<b>总数不再单独写死一份</b>（26 这个数只从登记表算出来）。
    /// </para>
    /// </summary>
    [Fact]
    public void CallSites_RegistryMatchesTheSource_AndSaysWhichDirection()
    {
        var discovered = Repo.NativeSources()
            .Select(s => (Name: s.Name, Calls: Regex.Matches(s.Text, @"(?m)^\s*Ui\.HintRow\(grid, ").Count))
            .Where(s => s.Calls > 0)
            .ToDictionary(s => s.Name, s => s.Calls, StringComparer.Ordinal);
        var registered = CallSites.ToDictionary(s => s.File, s => s.Calls, StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var (file, calls) in registered)
        {
            var found = discovered.TryGetValue(file, out var n) ? n : 0;
            if (found == calls) continue;
            problems.Add(found > calls
                ? $"{file} 有 {found} 处经 Ui.HintRow 出厂，登记表 {nameof(CallSites)} 只记了 {calls} 处——新增的这一处确实经工厂出厂的话，把登记改到 {found}（这条门不许把工厂调用删掉来变绿）"
                : $"{file} 只剩 {found} 处经 Ui.HintRow 出厂，登记的是 {calls} 处——少的那几处要么退回了就地拼形状，要么这一屏的提示行被删了而登记没跟上");
        }

        foreach (var file in discovered.Keys.Except(registered.Keys, StringComparer.Ordinal))
        {
            problems.Add($"{file} 有 {discovered[file]} 处 Ui.HintRow 却没在 {nameof(CallSites)} 里登记——提示行的口径没进这本账，下一轮就没人知道它是第几族的写法");
        }

        foreach (var file in registered.Keys.Except(discovered.Keys, StringComparer.Ordinal))
        {
            problems.Add($"{nameof(CallSites)} 登记了 {file}，可它现在已经没有一处 Ui.HintRow——登记指向一屏不再存在的写法，这本账就空转了");
        }

        Assert.True(problems.Count == 0,
            "提示行的工厂调用点登记与源码对不上：" + string.Join("；", problems) + "。");
    }

    /// <summary>
    /// 「空标签 + Caption」这种借字段行放提示的写法不得复活（配置页此前的写法）。
    /// 离线模式那一行是勾选框、不是提示行，允许走 <c>AddRow(grid, "", _offline)</c>。
    /// </summary>
    [Fact]
    public void HintRows_AreNotSneakedInAsEmptyLabelFieldRows()
    {
        foreach (var source in Repo.NativeSources())
        {
            foreach (Match match in Regex.Matches(source.Text, @"(?m)^\s*AddRow\([^,]+, ""\s*"", Ui\.Caption\("))
            {
                Assert.Fail($"{source.Name}:提示行又借字段行出厂（{match.Value.Trim()}）——一律走 Ui.HintRow（DESIGN.md §6「提示行」）。");
            }
        }
    }

    /// <summary>
    /// 行容器不得再往标签列塞占位控件。配置页那份由 <c>ProfileFormLayoutTests.AddRow_DoesNotAddDefaultSizedSpacer</c>
    /// 钉着，这里补新建任务对话框那一份（它此前放的就是默认尺寸 200×100 的 Panel，离线模式那一行被撑高）。
    /// </summary>
    [Fact]
    public void RowContainers_NeverAddASpacerToTheLabelColumn()
    {
        var body = MethodBody(NativeFile("TasksPage.cs"), "private void AddRow(TableLayoutPanel grid, string label, Control field)");
        Assert.False(body.Contains("new Panel", StringComparison.Ordinal),
            "新建任务对话框的 AddRow 又往空标签格放占位 Panel——那一行会被撑到 100px 高（同一根因见 ProfileFormLayout）。");
        Assert.Contains("if (label.Length > 0)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 二十六句提示文字逐字与本轮改动之前相同：distill 收的是形状，不是话。
    /// 字面量按集合比（多一句、少一句、改一个字都红）；取共享常量的那五处按调用点比。
    /// </summary>
    [Fact]
    public void TheHintTexts_AreWordForWordUnchanged()
    {
        var all = string.Join("\n", CallSites.Select(s => NativeFile(s.File)));
        var literals = Regex.Matches(all, @"Ui\.HintRow\(grid, ""([^""]*)""\)").Cast<Match>()
            .Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(LiteralHints.Length, literals.Count);
        Assert.Equal(LiteralHints.OrderBy(t => t, StringComparer.Ordinal), literals.OrderBy(t => t, StringComparer.Ordinal));

        // 取共享口径的那五处：文字来自一份常量，界面不另抄一遍。
        foreach (var (file, call) in new[]
        {
            ("TasksPage.cs", "Ui.HintRow(grid, TaskCreation.KeysHint);"),
            ("TasksPage.cs", "Ui.HintRow(grid, TaskCreation.NoteFallbackHint);"),
            ("ProjectsPage.cs", "Ui.HintRow(grid, ProfileFormLayout.ProjectListHint(_editing is not null));"),
            ("GenerationDialog.cs", "Ui.HintRow(grid, GenerationEntries.KeyHint + \"。\");"),
        })
        {
            Assert.Contains(call, NativeFile(file), StringComparison.Ordinal);
        }
    }

    /// <summary>设计基线记下这条口径与它的唯一落点，界面引用它而不是引用一段散文。</summary>
    [Fact]
    public void DesignDoc_DeclaresTheHintRowClause()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("Ui.HintRow", section, StringComparison.Ordinal);
        Assert.Contains("提示行", section, StringComparison.Ordinal);
        // §8 记下这一族修到了哪几屏、哪一族还没量（按钮行的占位控件本轮未动）。
        var gap = Repo.DesignSection(8);
        Assert.Contains("Ui.HintRow", gap, StringComparison.Ordinal);
        Assert.Contains("按钮行", gap, StringComparison.Ordinal);
    }

    /// <summary>
    /// 「这一行到底多高」在 Linux 判不了，由冒烟器在 wine 下逐条量（第一百六十轮那条同一判据：
    /// 长度门管「别写成段落」，行高归真实控件）。这一段被删掉、或不再覆盖那四个对话框，本条即红。
    /// </summary>
    [Fact]
    public void Smoke_MeasuresTheHintRowHeightsOfThoseFourDialogs()
    {
        Assert.Contains("四个子对话框的提示行没有死空白", SmokeSource, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^\s*private static bool IsHintRow\(TableLayoutPanel grid, int row\)$", SmokeSource);
        foreach (var screen in new[] { "新建任务", "模块与模板", "序列化与分析", "模型服务" })
        {
            Assert.Contains($"(\"{screen}\"", SmokeSource, StringComparison.Ordinal);
        }

        // 阈值取同一份口径，不在冒烟器里另写一个数。
        Assert.Contains("ProfileFormLayout.MaxRowHeight(UiTokens.Body.Value.Height)", SmokeSource, StringComparison.Ordinal);
    }

    private static string FactoryBody()
    {
        var start = UiSource.IndexOf("static Label HintRow(TableLayoutPanel grid, string text)", StringComparison.Ordinal);
        Assert.True(start >= 0, "Ui.cs 里没有 HintRow。");
        var end = UiSource.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "Ui.HintRow 切不出方法体边界。");
        return UiSource[start..end];
    }

    /// <summary>方法体切到下一个顶格 <c>private</c> 声明之前（段体里有 lambda，花括号配对会被字符串骗）。</summary>
    private static string MethodBody(string text, string signature)
    {
        var start = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"找不到方法 {signature}。");
        var end = text.IndexOf("\n    private", start + signature.Length, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
