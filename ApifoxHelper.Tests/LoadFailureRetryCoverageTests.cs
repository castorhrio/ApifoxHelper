using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「凡有读取失败态的屏都有重试入口」的机械断言（第一百五十三轮，具名 pass：harden）。
/// <para>
/// <b>为什么要有这个文件</b>：第一百五十至一百五十二轮把「读取失败 → 唯一行动按钮「重试」」
/// 逐屏补到了配置页、任务页、报告对话框与差异确认对话框，但每一屏的接线都是**各写一条用例**
/// （<see cref="LoadFailureStateTests"/> 里那三条 <c>*_WiresRetryToTheSameLoadEntry</c>）。
/// 那种写法只能钉住「已经补过的那四屏」，钉不住「以后新加的屏」：新增一屏带失败态、
/// 却忘了接「重试」，全部用例照样绿——这正是「凡有读取失败态的页面都有重试入口」这句话
/// 在机械层面不成立的地方。
/// </para>
/// <para>
/// 本文件换一种问法：**先发现、再断言**。发现口径是「<c>Desktop/Native</c> 下引用了失败态口径
/// （<see cref="LoadFailureState"/> / <see cref="ProfileLoadState"/>）的界面源码」——引用即意味着
/// 这一屏有失败态；发现到的每一屏都必须满足同一组形状：按钮文字取自口径文件、动作回到
/// **与首次读取同一个 <c>Load*</c> 入口**、可见性由同一份判定门控、屏上不出现第二份「重试」字面。
/// 新增一屏只要引用了口径文件就自动进入断言范围，漏接即红。
/// </para>
/// <para>
/// <b>「回到同一个入口」允许一层包装</b>（第二百六十五轮）：配置页那颗「重试」读成功之后还要做一件事——
/// 落回用户从任务详情跳过来想找的那一行（<c>ProjectsPage.RetryLoad</c>）。于是点击目标不再是
/// <c>LoadProfiles</c> 本身，而是「先捕获挂起的那一跳、再调同一个 <c>LoadProfiles</c>、读成功后落回去」的包装。
/// 这一条因此改成<b>先解析、再判</b>：包装只许有一层，且包装体里<b>只许调一个</b> <c>Load*</c> 入口——
/// 在包装里再写一份读取逻辑，就是「两条路各读一份」，那正是这一条要拦的东西。
/// 解析本身由 <see cref="RetryWrapper_IsResolvedOnlyWhenItDelegatesToOneReadEntry"/> 拿三发注入当场验。
/// </para>
/// <para>
/// 发现本身也要防退化：<see cref="Discovery_CoversExactlyTheTwelveScreens"/> 把「发现到哪几屏」
/// 钉成封闭清单——发现正则写坏（比如口径文件改名）会让清单变空，那条用例先红，
/// 不会出现「一条都没发现、于是全部通过」的假绿。
/// <b>但这一族只看得见已经接入口径文件的屏</b>（第三百零三轮）：一屏从不引用 <c>LoadFailureState</c>，
/// 就从不被它看见——本轮接入的三屏正是这么漏着的。那层盲区由对面那一道兜住，
/// <see cref="NoScreen_SendsTheUserAwayToReopenTheWindow"/> 判的是那几句<b>话本身</b>，不认引用。
/// </para>
/// </summary>
public class LoadFailureRetryCoverageTests
{
    /// <summary>
    /// 失败态口径文件自身：它们定义机制与文案，不是「有失败态的屏」，不参与接线断言。
    /// <para>
    /// <c>ProfileJump.cs</c> 第二百六十四轮进这份名单：它给「跳过去时清单没读出来」那句话
    /// （<c>LoadFailedStatus</c>）指名了那颗「重试」，字面取自 <see cref="ProfileLoadState.RetryLabel"/>——
    /// 于是它引用了口径文件、被「引用即算有失败态」的发现口径扫进来。但它<b>不是一屏</b>：
    /// 没有按钮、没有清单、没有读取入口，它只是一句文案的唯一落点。把它算成屏，等于要求一句文案
    /// 去接一颗按钮——那才是把口径文件与屏混为一谈。**新增的文案口径文件同样进这份名单**，
    /// 判据是「它有没有自己的读取入口」，不是「它有没有引用口径文件」。
    /// </para>
    /// </summary>
    private static readonly string[] StateFiles = ["LoadFailureState.cs", "ProfileLoadState.cs", "ProfileJump.cs"];

    /// <summary>「这一屏有读取失败态」的发现标记：引用口径文件即算。</summary>
    private static readonly string[] StateMarkers = ["LoadFailureState.", "ProfileLoadState."];

    /// <summary>
    /// 首次读取的入口：对话框是「打开」（<c>OnLoad</c>），页面是「这一页出现」（<c>OnPageShown</c>）。
    /// 重试必须回到同一个方法——两条路各读一份，失败态与正常态就会各自漂移。
    /// </summary>
    private static readonly string[] InitialReadAnchors =
        ["protected override void OnLoad(EventArgs e)", "public void OnPageShown()"];

    /// <summary>发现到的失败态屏（按文件名排序，断言消息里带文件名）。</summary>
    private static IReadOnlyList<NativeSource> FailureScreens() =>
        Repo.NativeSources()
            .Where(s => !StateFiles.Contains(s.Name))
            .Where(s => StateMarkers.Any(s.Contains))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 不新增第二颗「重试」、而是<b>让失败提示指名屏上那颗已有入口</b>的屏（第三百零三轮：ApplyPlanDialog）。
    /// <para>
    /// 为什么它不进那几个按按钮形状判的循环：这一屏的第一次读取就是「生成计划」，而这一屏本来就有一颗
    /// 「重新生成计划」——点下去调的正是 <c>OnLoad</c> 调的那一个入口。再摆一颗「重试」等于同屏两颗做同一件
    /// 事的按钮，违反 §1.3「唯一行动按钮」；口径文件为此留了 <c>LoadFailureState.RetryHint</c> 这一支
    /// （「读取项目列表」「读取分支」那两颗按钮早就这么判）。它改由
    /// <see cref="NamedEntryScreen_RetryIsTheButtonTheHintNames"/> 单独判三件事：提示句指名的那颗按钮确实存在、
    /// 点的是同一个读取入口、失败态确实把它交回可用。<b>它不进封闭清单以外的第三条路</b>——
    /// <see cref="Discovery_CoversExactlyTheTwelveScreens"/> 判两组之和必须等于发现到的全部屏。
    /// </para>
    /// </summary>
    private static readonly string[] NamedEntryScreens = ["ApplyPlanDialog.cs"];

    /// <summary>按「屏上有一颗取自口径的「重试」按钮」这一形状判的那几屏。</summary>
    private static IReadOnlyList<NativeSource> ButtonScreens() =>
        FailureScreens().Where(s => !NamedEntryScreens.Contains(s.Name)).ToList();

    /// <summary>这一屏的「重试」按钮字段名（按钮文字必须取自口径文件，不就地写「重试」）。</summary>
    private static string RetryField(NativeSource screen)
    {
        var match = Regex.Match(
            screen.Text,
            @"private readonly Button (\w+) = Ui\.Ghost\((?:LoadFailureState|ProfileLoadState)\.RetryLabel\);");
        Assert.True(match.Success, $"{screen.Name}：没有找到「重试」按钮字段（文字须取自 LoadFailureState/ProfileLoadState.RetryLabel）。");
        return match.Groups[1].Value;
    }

    /// <summary>这一屏「重试」按钮点下去调用的方法名（必须是与首次读取同一个 Load* 入口）。</summary>
    private static string RetryTarget(NativeSource screen, string field)
    {
        var match = Regex.Match(screen.Text, @"(?m)^\s*" + Regex.Escape(field) + @"\.Click \+= \(_, _\) => (\w+)\(\);");
        Assert.True(match.Success, $"{screen.Name}：{field} 没有接点击动作（失败态给了按钮却点不动，等于没有入口）。");
        return match.Groups[1].Value;
    }

    /// <summary>取一个方法体：从入口行到下一个 4 空格缩进的收尾大括号（单行表达式体只取那一行）。</summary>
    private static string Body(string text, string anchor)
    {
        var start = text.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(start >= 0, $"未找到入口「{anchor}」。");
        var lineEnd = text.IndexOf('\n', start);
        if (lineEnd < 0) return text[start..];
        if (text[start..lineEnd].Contains("=>", StringComparison.Ordinal)) return text[start..lineEnd];
        var end = text.IndexOf("\n    }", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }

    /// <summary>去掉注释行后的源码：断言「屏上不出现某字面」时不能把注释里的引述一起算。</summary>
    private static string CodeOnly(string source) => CodeScan.Text(source);

    // ---------- 发现：范围本身要钉住，否则「一条都没发现」也是绿的 ----------

    [Fact]
    public void Discovery_CoversExactlyTheFourteenScreens()
    {
        // 封闭清单：新增一屏带失败态时，这里会红——提醒把新屏的接线一并补上（而不是悄悄漏掉）。
        // 第一百五十七轮新增第五屏 JournalDialog.cs（任务日志对话框的读取失败态）。
        // 第三百零三轮补到八屏：CandidatesDialog（注释候选）、SupplementsDialog（补充说明）此前只在失败时
        // 给一句「请关闭后重试」，屏上没有可点的入口；ApplyPlanDialog 走「指名已有按钮」那一支（见 NamedEntryScreens）。
        // 第三百零五轮补到九屏：ScanResultsDialog（注释缺口）读的是同一份 symbols.json，接法与第三百零三轮那两屏一致。
        // 第三百零八轮补到十屏：CheckResultsDialog（检查结果）读的是与差异确认、补充说明同一份 diff.json，
        // 那两家第三百零三轮起就有那颗按钮，而这一屏写的是第三种说法——同一件事在两屏不许有两种说法，
        // 而其中一种屏上没有入口正是第三百零三轮点名的盲区。
        // 第三百零九轮补到十一屏：EvidenceDialog（代码证据）读的是 evidence.json，与接口追踪明细那一屏
        // （EndpointTraceDialog，当时仍欠着）是同一份产物两种说法，两边那句下一步写的还是同一句话。
        // 第三百一十轮补到十二屏：EndpointTraceDialog（接口追踪明细）读的就是同一份 evidence.json，
        // 第三百零九轮把这一条写在本文件「为什么只收这一屏」那一段里，本轮收的是它自己那一笔——
        // 两句收成一句，口径文件那一对因此从一屏变两屏（见 SharedEvidenceCopy 那条）。
        // 第三百一十二轮补到十三屏：SyncPreviewDialog（同步预览勾选页）读的是 diff.json，与差异确认、
        // 补充说明、检查结果同一份产物，而它写的是第三种说法、屏上没有入口——那一笔欠账
        // SharedCheckCopy 的反向那一判早就预先算成「欠着的第四屏」，本轮收掉它。
        // 第三百一十三轮补到十四屏：PropagationDialog（生成文档核对）读的是 propagation.json，
        // 失败态此前只剩「关闭」可点（「仍欠的两屏」里最硬的一屏，没有任何等价入口），本轮接上那颗取自口径的「重试」。
        var names = FailureScreens().Select(s => s.Name).ToArray();
        Assert.Equal(
            [
                "ApplyPlanDialog.cs", "CandidatesDialog.cs", "CheckResultsDialog.cs", "DecisionsDialog.cs",
                "EndpointTraceDialog.cs", "EvidenceDialog.cs", "JournalDialog.cs", "ProjectsPage.cs",
                "PropagationDialog.cs", "ReportsDialog.cs", "ScanResultsDialog.cs", "SupplementsDialog.cs",
                "SyncPreviewDialog.cs", "TasksPage.cs",
            ],
            names);

        // 两个形状必须把这十四屏分完：一屏既不进两个循环，也不从两个循环之间漏出去。
        // 少了这一句，往 NamedEntryScreens 里写一个不存在的文件名会静默少判一屏。
        var partition = ButtonScreens().Select(s => s.Name)
            .Concat(NamedEntryScreens)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(names, partition);
    }

    // ---------- 接线：每一屏都必须有「重试」，且回到同一个读取入口 ----------

    [Fact]
    public void EveryFailureScreen_HasOneRetryButtonFromTheSharedLabel()
    {
        foreach (var screen in ButtonScreens())
        {
            var field = RetryField(screen);
            // 按钮要真的挂到屏上：造出来不 Add 等于没有（页面挂进空态动作行，对话框挂进动作行网格）。
            Assert.Matches(@"\.Controls\.Add\(" + Regex.Escape(field) + @"[,)]", screen.Text);
        }
    }

    [Fact]
    public void EveryFailureScreen_RetryCallsTheSameLoadEntryAsTheInitialRead()
    {
        foreach (var screen in ButtonScreens())
        {
            var field = RetryField(screen);
            var target = RetryTarget(screen, field);

            // 首次读取走的是哪个入口：只认「打开」这一条路（OnLoad / OnPageShown）——
            // RefreshData 是刷新入口，它调什么不能替「打开时读的是哪个方法」作证。
            var anchor = InitialReadAnchors.FirstOrDefault(a => screen.Text.Contains(a, StringComparison.Ordinal));
            Assert.True(anchor is not null, $"{screen.Name}：找不到首次读取入口（OnLoad / OnPageShown）。");
            var initial = Body(screen.Text, anchor!);

            // 重试点下去最终读的是哪个入口：直接就是它，或经一层只调一个读取入口的包装。
            var (entry, problem) = ResolveReadEntry(screen.Text, target, initial);
            Assert.True(problem is null, $"{screen.Name}：{problem}");

            // ① 重试最终回到的是一个读取入口（Load*），不是另写一份读取逻辑、也不是「重开窗口」。
            Assert.Matches(@"^Load\w+$", entry);
            // ② 那个入口在本屏有定义（不是调一个不存在的方法）。
            Assert.Matches(@"private\s+(?:async\s+)?(?:void|Task)\s+" + Regex.Escape(entry) + @"\(", screen.Text);
            // ③ 首次读取走的是同一个入口：打开这一屏（或这一页出现）时调的就是它。
            Assert.True(
                initial.Contains(entry + "();", StringComparison.Ordinal),
                $"{screen.Name}：打开这一屏时没有走 {entry}()——「重试」与打开时读的不是同一个入口，两条路会各自漂移。");
        }
    }

    /// <summary>
    /// 上面那条「重试回到同一个读取入口」的解析<b>自己</b>被抓得住（第二百六十五轮）：三发注入各报一处，
    /// 合规形状不报。本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异）。
    /// </summary>
    [Fact]
    public void RetryWrapper_IsResolvedOnlyWhenItDelegatesToOneReadEntry()
    {
        const string initial = "public void OnPageShown()\n    {\n        LoadProfiles();\n    }";

        // ① 直接就是读取入口：原样返回，不报。
        Assert.Equal(("LoadProfiles", (string?)null), ResolveReadEntry(initial, "LoadProfiles", initial));

        // ② 一层包装、体里只调一个读取入口：解析到那个入口，不报。
        var wrapper = initial + "\n    private void RetryLoad()\n    {\n        var pending = _pendingReveal;\n"
            + "        LoadProfiles();\n        if (pending is not { } target) return;\n"
            + "        RevealProfile(target.Id, target.Name);\n    }";
        Assert.Equal(("LoadProfiles", (string?)null), ResolveReadEntry(wrapper, "RetryLoad", initial));

        // ③ 包装里调了两个读取入口：报出来（两条路各读一份）。
        var two = initial + "\n    private void RetryLoad()\n    {\n        LoadProfiles();\n        LoadProfilesAgain();\n    }";
        var (_, twoProblem) = ResolveReadEntry(two, "RetryLoad", initial);
        Assert.NotNull(twoProblem);
        Assert.Contains("2 个读取入口", twoProblem!, StringComparison.Ordinal);

        // ④ 包装里一个读取入口都没调（自己写了一份读取逻辑）：同样报出来。
        var none = initial + "\n    private void RetryLoad()\n    {\n        _list.Items.Clear();\n    }";
        var (_, noneProblem) = ResolveReadEntry(none, "RetryLoad", initial);
        Assert.NotNull(noneProblem);
        Assert.Contains("0 个读取入口", noneProblem!, StringComparison.Ordinal);
    }

    /// <summary>
    /// 「重试」点下去最终读的是哪个入口：文本进、结论出（问题为空 = 过得去）。
    /// <para>
    /// 两种合法形状：① 直接就是首次读取调的那个入口；② 一层包装——读完之后还要做一件事
    /// （配置页那颗「重试」读成功之后要落回用户从任务详情跳过来想找的那一行）。
    /// 包装<b>只许有一层</b>、体里<b>只许调一个</b> <c>Load*</c> 入口：在包装里再写一份读取逻辑，
    /// 就是「两条路各读一份」，那正是这一条要拦的东西。
    /// </para>
    /// </summary>
    private static (string Entry, string? Problem) ResolveReadEntry(string text, string target, string initial)
    {
        if (initial.Contains(target + "();", StringComparison.Ordinal)) return (target, null);

        var start = text.IndexOf("private void " + target + "()", StringComparison.Ordinal);
        if (start < 0)
            return (target, $"「重试」点下去调的 {target} 既不是首次读取的入口，本屏也没有它的定义。");
        var end = text.IndexOf("\n    }", start, StringComparison.Ordinal);
        var wrapper = end < 0 ? text[start..] : text[start..end];
        var calls = Regex.Matches(wrapper, @"(?<![\w.])Load\w+\(")
            .Select(m => m.Value[..^1])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (calls.Count != 1)
            return (target, $"{target} 是「重试」的包装，但体里调了 {calls.Count} 个读取入口"
                + $"（{string.Join("、", calls)}）——包装里再写一份读取逻辑，两条路会各自漂移。");
        return (calls[0], null);
    }

    [Fact]
    public void EveryFailureScreen_GatesRetryVisibility()
    {
        // 三态同一时刻只显示一个行动按钮：失败态才出现「重试」。两种合法形状——
        // ① 页面：可见性由口径文件的同一份判定给出（RetryVisible），不就地写 if；
        // ② 对话框：读成功时收起（false）、失败时按条件置位（true / canRetry），不留在常显。
        // 两种都不成立的屏（按钮造出来就常显、或只置 true 从不收起）在这里红。
        foreach (var screen in ButtonScreens())
        {
            var field = RetryField(screen);
            var assignments = Regex.Matches(screen.Text, Regex.Escape(field) + @"\.Visible = ([^;]+);")
                .Select(m => m.Groups[1].Value.Trim())
                .ToList();
            Assert.True(assignments.Count > 0, $"{screen.Name}：{field} 的可见性从未被设置（造出来就常显）。");

            var bySharedPredicate = assignments.Any(a =>
                a.StartsWith("LoadFailureState.RetryVisible(", StringComparison.Ordinal) ||
                a.StartsWith("ProfileLoadState.RetryVisible(", StringComparison.Ordinal));
            // 对话框那一支：读成功时收起（false），失败时按条件置位（true 或 canRetry 这类判定值）。
            var toggledBothWays = assignments.Contains("false") && assignments.Any(a => a != "false");
            Assert.True(
                bySharedPredicate || toggledBothWays,
                $"{screen.Name}：{field} 的可见性没有被门控（既不走 RetryVisible，也没有在失败与成功两条路上各设一次）。");
        }
    }

    [Fact]
    public void EveryFailureScreen_KeepsTheRetryLiteralInTheStateFiles()
    {
        // 「重试」这个字面全仓只在口径文件里出现一次：屏上再写一份必然漂移（一颗叫「重试」、一颗叫「重新读取」）。
        foreach (var screen in FailureScreens())
        {
            Assert.Empty(Regex.Matches(CodeOnly(screen.Text), "\"[^\"\\r\\n]*重试[^\"\\r\\n]*\""));
        }
    }

    [Fact]
    public void StateFiles_AreTheOnlyPlaceThatDefinesTheRetryLiteral()
    {
        // 口径文件本身：字面只在这里定义一次，配置页的 ProfileLoadState 委托给它（不各写一份）。
        var shared = Repo.Source("Desktop", "Native", "LoadFailureState.cs");
        Assert.Single(Regex.Matches(shared, "\"重试\""));
        var profile = Repo.Source("Desktop", "Native", "ProfileLoadState.cs");
        Assert.Empty(Regex.Matches(profile, "\"重试\""));
        Assert.Contains("LoadFailureState.RetryLabel", profile);
    }

    // ---------- 「不新增按钮、而是让提示指名已有入口」那一支（第三百零三轮） ----------

    /// <summary>
    /// <see cref="NamedEntryScreens"/> 那一支单独判，形状与上面那几条一一对得上，只是那颗按钮是屏上本来就有的。
    /// <para>
    /// 判四件事：① 按钮文字有一处常量落点、按钮从它出厂；② 失败提示经 <c>RetryHint</c> 把同一个名字交进去；
    /// ③ 那颗按钮点下去调的正是首次读取（<c>OnLoad</c>）调的那一个入口；④ 读取失败那一支确实把它交回可用，
    /// 而「前置未满足」那一支不点亮它（重试读不出新东西时，亮着一颗做用没有的按钮比不亮更坏）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判那句话在屏幕上放不放得下（随 G-D5），也不判「这一屏该不该再多一颗按钮」——
    /// 那是 §1.3 那句「唯一行动按钮」的判断，写在口径文件的注释里。
    /// </para>
    /// </summary>
    [Fact]
    public void NamedEntryScreen_RetryIsTheButtonTheHintNames()
    {
        const string file = "ApplyPlanDialog.cs";
        var screen = Repo.NativeSources().Single(s => s.Name == file);
        var text = screen.Text;

        // ① 按钮文字有一处常量落点。
        Assert.Matches(@"(?m)^\s*private const string RegenerateLabel = ""[^""]+"";$", text);
        Assert.Matches(@"(?m)^\s*_regenButton = Ui\.Ghost\(RegenerateLabel\);", text);

        // ② 失败提示把同一个名字交进去，而不是自己拼一句「请关闭后…」。
        Assert.Contains("LoadFailureState.ApplyPlan.Hint(RegenerateLabel)", text, StringComparison.Ordinal);

        // ③ 那颗按钮调的就是首次读取的入口，两条路不各读一份。
        Assert.Matches(@"(?m)^\s*_regenButton\.Click \+= \(_, _\) => PendingLoad = LoadPlanAsync\(\);$", text);
        Assert.Matches(@"(?m)^\s*(?:private|internal)\s+(?:async\s+)?(?:void|Task)\s+LoadPlanAsync\(", text);
        Assert.Contains("PendingLoad = LoadPlanAsync();", Body(text, InitialReadAnchors[0]), StringComparison.Ordinal);

        // ④ 失败态把它交回可用，而这一条由 ShowHint 的那一个参数决定——两处必须连着看。
        Assert.Contains(
            "_regenButton.Enabled = canRetry;",
            Body(text, "private void ShowHint(string message, Color color, bool canRetry = false)"),
            StringComparison.Ordinal);
        var load = Body(text, "internal async Task LoadPlanAsync()");
        Assert.Contains("canRetry: true", load, StringComparison.Ordinal);
        // 前置未满足（没扫描 / 没有已接受候选）那一支不传 canRetry：那句话自己写了下一步在别处。
        Assert.Contains("ShowHint(ex.Message, UiTokens.WarnText);", load, StringComparison.Ordinal);
    }

    /// <summary>
    /// 「扫描产物读不出来」那一句在两屏只有一份字面（第三百零五轮，接上注释缺口时补的两侧对账）。
    /// <para>
    /// <b>判的是哪件事</b>：注释缺口（<c>ScanResultsDialog</c>）与注释候选的「扫描结果读不出来」那一支
    /// 读的是同一份 symbols.json，<b>同一件事在两屏不许有两种说法，更不许其中一种屏上没有入口</b>
    /// （第三百零三轮就是靠「那句话本身」才发现这一族的盲区的）。本轮把那句文案收成口径文件里的一对共用常量，
    /// 于是这里两侧对账：口径侧那两句字面各只出现一次 ↔ 界面侧引用这对常量的屏恰好是那两屏。
    /// </para>
    /// <para>
    /// <b>两个方向都判</b>：有人把自己那处改回就地拼一句 → 界面侧少一屏，红；第三屏也开始念这句却没人登记 →
    /// 界面侧多一屏，红；谁把口径侧那句话抄成第二份 → 字面出现两次，红。
    /// 按第二百八十二轮那五步判：断言里的名字是 <c>LoadFailureState.Slots*</c>（定义在口径文件）与
    /// <c>Desktop/Native</c> 各屏的源码文本（在口径文件之外），第 ② 步答「不是」，第 ③ 步按<b>对账门</b>处理。
    /// </para>
    /// </summary>
    [Fact]
    public void SharedScanCopy_HasOneLiteralAndTheTwoScreensThatReadSymbols()
    {
        const string title = "读取扫描结果失败";
        const string hintTail = "扫描结果仍读不到时回「";

        var shared = Repo.Source("Desktop", "Native", "LoadFailureState.cs");
        Assert.Equal(1, Count(shared, title));
        Assert.Equal(1, Count(shared, hintTail));
        // 两份成员是委托，不是第三份字面（委托写反成自引用会编译不过，这里判的是它指向上面那一对）。
        Assert.Contains("SlotsTitle = LoadFailureState.SlotsTitle;", shared, StringComparison.Ordinal);
        Assert.Contains("SlotsHint = LoadFailureState.SlotsHint;", shared, StringComparison.Ordinal);

        var callers = Repo.NativeSources()
            .Where(s => s.Name != "LoadFailureState.cs")
            .Where(s => s.Code.Contains("SlotsTitle", StringComparison.Ordinal)
                        || s.Code.Contains("SlotsHint", StringComparison.Ordinal))
            .Select(s => s.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(["CandidatesDialog.cs", "ScanResultsDialog.cs"], callers);

        // 两屏都不把那句话抄第二份（抄了就有两份会各自漂，而其中一份可能根本没有入口）。
        foreach (var screen in Repo.NativeSources().Where(s => callers.Contains(s.Name)))
        {
            Assert.DoesNotContain(title, screen.Code, StringComparison.Ordinal);
            Assert.DoesNotContain(hintTail, screen.Code, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 界面侧引用「检查结果读不出来」那一对口径的<b>全部写法</b>：口径文件顶层那一对（检查结果那一屏直接引用），
    /// 加上差异确认与补充说明两家<b>委托过去</b>的成员名（第三百零八轮）。取的都是去注释后的代码——
    /// 注释里提一句那个名字是在说明理由，不是在这一屏接这句话。
    /// </summary>
    private static readonly string[] CheckCopyMarkers =
        ["CheckTitle", "CheckHint", "Decisions.Title", "Decisions.Hint", "Supplements.Title", "Supplements.Hint"];

    /// <summary>
    /// 判定本体（纯函数：口径文件源码 + 全部界面源码 + 已共用的那几屏 + 还欠着的那一屏进，问题出）——
    /// 注入自测直接喂夹具，磁盘一个字不动。判四件事：
    /// ① 口径侧那一句字面在口径文件里只出现一次；② <c>Decisions</c> / <c>Supplements</c> 两家确实是委托
    /// （改回第三份字面即红）；③ 引用这一族的屏恰好是登记的那几屏，且每一屏都不把那句话抄第二份
    /// （少一屏 = 有人把自己那处改回就地拼一句，多一屏 = 有人接上了却没登记）；
    /// ④ 还欠着的那一屏<b>现在仍然</b>自己写着那一句——收掉它的那一轮要把这一条改成四屏、
    /// 并删掉 <see cref="SendAwayExemptions"/> 里那一行，登记与实物不许各指一处（第三百零六轮那句 stale 的同一读法）。
    /// </summary>
    internal static List<string> CheckCopyProblems(
        string stateSource,
        IEnumerable<(string Name, string Code)> nativeScreens,
        string[] shipped)
    {
        const string title = "读取检查结果失败";
        var problems = new List<string>();

        var literals = Count(stateSource, title);
        if (literals != 1)
        {
            problems.Add($"口径文件里「{title}」出现 {literals} 次，登记的是 1 次——两份会各自漂，"
                + "而下一轮只能看见其中一份（第三百零三轮之前那两家就是这么各写一份的）。");
        }

        foreach (var member in new[] { "Title = LoadFailureState.CheckTitle;", "Hint = LoadFailureState.CheckHint;" })
        {
            var delegates = Count(stateSource, member);
            if (delegates != 2)
            {
                problems.Add($"口径文件里「{member}」有 {delegates} 处，登记的是 2 处（差异确认与补充说明两家的委托）——"
                    + "把它改回写死一份字面，同一件事就又有了第二说法。");
            }
        }

        var screens = nativeScreens.ToList();
        var callers = screens
            .Where(s => s.Name != "LoadFailureState.cs")
            .Where(s => CheckCopyMarkers.Any(s.Code.Contains))
            .Select(s => s.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var expected = shipped.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        if (!callers.SequenceEqual(expected, StringComparer.Ordinal))
        {
            problems.Add($"引用这一族口径的屏是 [{string.Join("、", callers)}]，登记的是 [{string.Join("、", expected)}]"
                + "——少一屏说明那一屏改回就地拼一句，多一屏说明接上了却没在这里登记。");
        }

        foreach (var screen in screens.Where(s => expected.Contains(s.Name, StringComparer.Ordinal)))
        {
            if (screen.Code.Contains(title, StringComparison.Ordinal))
            {
                problems.Add($"{screen.Name}：那一屏把「{title}」抄了第二份——屏幕上那句话从此有两处可改。");
            }
        }

        return problems;
    }

    /// <summary>
    /// 「检查结果读不出来」那一句在三屏只有一份字面（第三百零八轮，接上检查结果时补的两侧对账）。
    /// <para>
    /// <b>判的是哪件事</b>：差异确认、补充说明与检查结果读的是同一份 diff.json、欠的是同一句下一步。
    /// 前两家自第三百零三轮起各写一份字面（两份会各自漂），第三家写的是<b>第三种说法而屏上没有入口</b>——
    /// 那正是第三百零三轮点名的盲区。本轮收成口径文件顶层一对共用常量（<c>CheckTitle</c> / <c>CheckHint</c>）、
    /// 那两家的成员改为委托（屏幕上念的还是同一句老话，一个字未变），于是这里两侧对账：
    /// 口径侧那一句字面只出现一次 + 那两组确实是委托 ↔ 界面侧引用这一族的屏恰是那三屏，三屏都不抄第二份。
    /// </para>
    /// <para>
    /// <b>第四屏不在这里</b>：同步预览勾选页读的也是 diff.json，今天仍自己写着那一句、屏上仍没有入口，
    /// 那一行欠账登在 <see cref="SendAwayExemptions"/> 里（简报那句「一次只收一屏」）。这一条因此<b>反向也判</b>：
    /// 它现在仍然要写着那一句——接上它的那一轮如果只删名单那一行、忘了把这里改成四屏，这里当场红。
    /// <b>它不判什么</b>：不判那颗按钮在屏幕上放不放得下（随 G-D5），也不判那句话该不该再多个动词——
    /// 那是 <see cref="NoScreen_SendsTheUserAwayToReopenTheWindow"/> 与文案本身的事，这里不叠第二套。
    /// 按第二百八十二轮那五步判：① 断言里的名字是 <c>LoadFailureState.Check*</c> 与那两家委托（口径文件）
    /// 加 <c>Desktop/Native</c> 各屏的源码文本（在口径文件之外）；② 后者不在被它判的那个对象里，答「不是」；
    /// ③ 按第 ③ 步当<b>对账门</b>处理，不属第二百五十六轮收口那一族，照常随实物一起长。
    /// </para>
    /// </summary>
    [Fact]
    public void SharedCheckCopy_HasOneLiteralAndTheFourScreensThatReadDiff()
    {
        var problems = CheckCopyProblems(
            Repo.Source("Desktop", "Native", "LoadFailureState.cs"),
            Repo.NativeSources().Select(s => (s.Name, s.Code)).ToList(),
            ["CheckResultsDialog.cs", "DecisionsDialog.cs", "SupplementsDialog.cs", "SyncPreviewDialog.cs"]);
        Assert.True(problems.Count == 0,
            "「检查结果读不出来」那一句在三屏只该有一份字面，而欠着的第四屏要能被看见（DESIGN.md §7 的对面那一半："
            + "同一件事在两屏不许有两种说法，更不许其中一种屏上没有入口）：" + string.Join("；", problems));
    }

    /// <summary>
    /// 上面那条判据<b>自己</b>被抓得住（简报 P0 要求的那一份注入破坏自测）：合规形状不报，七发各报出该报的那一处。
    /// 夹具是一份假口径源码 + 五屏假代码，磁盘一个字不动——本仓那句老话：「判据自己没被跑过」比「没有判据」更贵。
    /// </summary>
    [Fact]
    public void CheckCopyDetector_ReportsTheShapesItMustCatch()
    {
        const string title = "读取检查结果失败";
        var shared = "public const string CheckTitle = \"" + title + "\";\n"
            + "public const string CheckHint = DirectoryHint;\n"
            + "public const string Title = LoadFailureState.CheckTitle;\n"
            + "public const string Hint = LoadFailureState.CheckHint;\n"
            + "public const string Title = LoadFailureState.CheckTitle;\n"
            + "public const string Hint = LoadFailureState.CheckHint;\n";
        var shipped = new[] { "CheckResultsDialog.cs", "DecisionsDialog.cs", "SupplementsDialog.cs", "SyncPreviewDialog.cs" };

        var screens = new (string Name, string Code)[]
        {
            ("LoadFailureState.cs", shared),
            ("CheckResultsDialog.cs",
                "ShowHint(LoadFailureState.FailureMessage(LoadFailureState.CheckTitle, error, LoadFailureState.CheckHint), UiTokens.BadText, canRetry: true);"),
            ("DecisionsDialog.cs",
                "LoadFailureState.FailureMessage(LoadFailureState.Decisions.Title, Redactor.Redact(ex.Message), LoadFailureState.Decisions.Hint)"),
            ("SupplementsDialog.cs",
                "LoadFailureState.FailureMessage(LoadFailureState.Supplements.Title, error, LoadFailureState.Supplements.Hint)"),
            ("SyncPreviewDialog.cs",
                "DisableSelect(LoadFailureState.FailureMessage(LoadFailureState.CheckTitle, error, LoadFailureState.CheckHint), UiTokens.BadText, canRetry: true);"),
        };
        Assert.Empty(CheckCopyProblems(shared, screens, shipped));

        // ① 口径侧那句话被抄成第二份。
        var twoLiterals = shared + "\npublic const string Third = \"" + title + "\";";
        Assert.Contains(CheckCopyProblems(twoLiterals, screens, shipped),
            p => p.Contains("出现 2 次", StringComparison.Ordinal));

        // ② 把一家的委托改回写死（写的这句不含那五个字，好让这一发只报委托这一处）。
        var notDelegated = shared.Replace("Title = LoadFailureState.CheckTitle;", "Title = \"读不回来\";");
        Assert.Contains(CheckCopyProblems(notDelegated, screens, shipped),
            p => p.Contains("两家的委托", StringComparison.Ordinal));

        // ③ 已接的那一屏改回就地拼一句：它从此不再引用这一族（登记侧与实物侧各指一处）。
        var inlined = screens
            .Select(s => s.Name == "CheckResultsDialog.cs"
                ? (Name: s.Name, Code: "ShowHint(\"读取失败：磁盘满。请关闭后重试。\", UiTokens.BadText);")
                : s)
            .ToList();
        Assert.Contains(CheckCopyProblems(shared, inlined, shipped),
            p => p.Contains("少一屏", StringComparison.Ordinal));

        // ④ 又一屏开始引用这一族却没人登记（下一轮接上第四屏时漏登记的形态）。
        var extraCaller = screens
            .Concat([(Name: "PropagationDialog.cs", Code: "LoadFailureState.FailureMessage(LoadFailureState.CheckTitle, error, LoadFailureState.CheckHint)")])
            .ToList();
        Assert.Contains(CheckCopyProblems(shared, extraCaller, shipped),
            p => p.Contains("多一屏", StringComparison.Ordinal));

        // ⑤ 已共用的一屏另起一处把那五个字抄进自己屏里（委托还在，第二份字面也回来了）。
        var secondCopy = screens
            .Select(s => s.Name == "DecisionsDialog.cs"
                ? (Name: s.Name, Code: s.Code + "\n_status.Text = \"" + title + "（已停用）\";")
                : s)
            .ToList();
        Assert.Contains(CheckCopyProblems(shared, secondCopy, shipped),
            p => p.Contains("抄了第二份", StringComparison.Ordinal));

        // ⑥ 第四屏（同步预览勾选页）接上之后，登记里少写它一屏同样红——「少一屏」那一句与 ③ 同一形状，
        //     这里单独喂一次，钉的是「第四屏真的进了登记」这件事本身。
        Assert.Contains(CheckCopyProblems(shared, screens, ["CheckResultsDialog.cs", "DecisionsDialog.cs", "SupplementsDialog.cs"]),
            p => p.Contains("少一屏", StringComparison.Ordinal));
    }

    // ---------- 「那份证据产物读不出来」那一句在两屏只有一份字面（第三百一十轮） ----------

    /// <summary>
    /// 界面侧引用「那份证据产物读不出来」这一对的写法：口径文件顶层那一对的两个成员名。
    /// 取的是去注释后的代码——注释里提一句那个名字是在说明理由，不是在这一屏接这句话。
    /// </summary>
    private static readonly string[] EvidenceCopyMarkers = ["Evidence.Title", "Evidence.Hint"];

    /// <summary>那两份界面共读的那一份产物（实物侧的读法：谁读它，谁就该念这一句）。</summary>
    private const string EvidenceArtifact = "TaskFiles.Evidence";

    /// <summary>
    /// 判定本体（纯函数：口径源码 + 全部界面源码 + 登记的那两屏进，问题出）——注入自测直接喂夹具，磁盘一个字不动。
    /// <para>
    /// 判四件事：① 口径侧那一句字面在口径文件里只出现一次（抄成第二份就有两处可改，而下一轮只看得见其中一处）；
    /// ② 每一屏<b>真读那份产物，就必须把那两个成员都引用上</b>——少一个即报（一屏只报一处）；
    /// ③ 引用了却不读那份产物的屏另报（那句话说的正是它读不出来，念到别的屏上是答非所问）；
    /// ④ 登记的集合必须等于真读那份产物的集合（少一屏 = 那一屏改回就地拼一句；多一屏 = 接上了却没登记），
    ///    且已登记的每一屏都不把那句字面抄进自己屏里。
    /// </para>
    /// <para>
    /// <b>②为什么是「两个都要引用」而不是「引用其一」</b>：本轮磁盘注入 I2（把那一句的标题换回屏里自己拼的
    /// 「读取追踪明细失败」、说明仍取 <c>Evidence.Hint</c>）在「引用其一」那一版上是<b>绿的</b>——那一屏确实还引用着
    /// 这一族，门就看不见它已经把屏幕上那句话的<b>主语</b>换掉了，而「同一份产物两种说法」恰恰活在那个词上。
    /// 取法因此收紧成两个成员都要在：少哪个报哪个，一屏只报一处，于是一发注入只红一条用例、说的是那一处。
    /// </para>
    /// </summary>
    internal static List<string> EvidenceCopyProblems(
        string stateSource,
        IEnumerable<(string Name, string Code)> nativeScreens,
        string[] shipped)
    {
        const string title = "读取代码证据失败";
        var problems = new List<string>();

        var literals = Count(stateSource, title);
        if (literals != 1)
        {
            problems.Add($"口径文件里「{title}」出现 {literals} 次，登记的是 1 次——两份会各自漂，"
                + "而两屏念出口的那一句从此只有一处被人看到。");
        }

        var screens = nativeScreens
            .Where(s => s.Name != "LoadFailureState.cs")
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
        var readers = screens.Where(s => s.Code.Contains(EvidenceArtifact, StringComparison.Ordinal)).ToList();
        var quoters = screens.Where(s => EvidenceCopyMarkers.Any(s.Code.Contains)).ToList();

        foreach (var screen in readers)
        {
            var missing = EvidenceCopyMarkers.Where(marker => !screen.Code.Contains(marker, StringComparison.Ordinal)).ToList();
            if (missing.Count > 0)
            {
                problems.Add($"{screen.Name}：它读 {EvidenceArtifact}，却没有引用这一对的 {string.Join("、", missing)}"
                    + "——屏幕上那句失败提示的主语（读不出来的那份产物叫什么）从此由这一屏自己拼，"
                    + "「同一份产物两种说法」就是活在这种一字之差上（磁盘注入 I2 实测到的正是这一处）。");
            }
        }

        foreach (var screen in quoters.Where(s => !readers.Contains(s)).ToList())
        {
            problems.Add($"{screen.Name}：引用了这一对却不读 {EvidenceArtifact}"
                + "——那句话说的正是那份产物读不出来，不读它的屏念它就是答非所问。");
        }

        string[] Sorted(IEnumerable<string> names) => names.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var readerNames = Sorted(readers.Select(s => s.Name));
        var expected = Sorted(shipped);
        if (!readerNames.SequenceEqual(expected, StringComparer.Ordinal))
        {
            problems.Add($"读 {EvidenceArtifact} 的屏是 [{string.Join("、", readerNames)}]，登记的是 [{string.Join("、", expected)}]"
                + "——少一屏说明那一屏不再读那份产物（登记要跟着收），多一屏说明有人也开始读它却没在这里登记。");
        }

        foreach (var screen in screens.Where(s => expected.Contains(s.Name, StringComparer.Ordinal)))
        {
            if (screen.Code.Contains(title, StringComparison.Ordinal))
            {
                problems.Add($"{screen.Name}：那一屏把「{title}」抄了第二份——屏幕上那句话从此有两处可改。");
            }
        }

        return problems;
    }

    /// <summary>
    /// 「那份证据产物读不出来」那一句在两屏只有一份字面（第三百一十轮，接上接口追踪明细时补的两侧对账）。
    /// <para>
    /// <b>判的是哪件事</b>：代码证据（<c>EvidenceDialog</c>）与接口追踪明细（<c>EndpointTraceDialog</c>）
    /// 读的是同一份 <c>evidence.json</c>，第三百零九轮接第一屏时把第二屏登成欠账并写了那句
    /// 「接上它的那一轮把两句收成一句，本文件的这一对因此从一屏变两屏」。本轮接上它：那一屏不再自己拼
    /// 「读取追踪明细失败」那一句，两屏念出的因此是同一句话——同一件事在两屏不许有两种说法，
    /// 更不许一种有入口、另一种没有（DESIGN.md §7 的对面那一半）。
    /// </para>
    /// <para>
    /// <b>两侧对账、三个方向都判</b>：有人把自己那处改回就地拼一句 → 红；第三屏也开始读那份产物却没人登记 → 红；
    /// 谁把口径侧那句话抄成第二份、或已接的那一屏另抄一份 → 红。与
    /// <see cref="SharedScanCopy_HasOneLiteralAndTheTwoScreensThatReadSymbols"/>（symbols.json 两屏）、
    /// <see cref="SharedCheckCopy_HasOneLiteralAndTheThreeScreensThatReadDiff"/>（diff.json 三屏）同一形状，
    /// 区别只在这一族登记的是<b>读法</b>而不是委托成员名——<c>Evidence</c> 那一对没有第三家委托它。
    /// 按第二百八十二轮那五步判：① 断言里出现的名字是 <c>LoadFailureState.Evidence.*</c>（口径文件）与
    /// <c>Desktop/Native</c> 各屏的源码文本（在口径文件之外）；② 后者不在被它判的那个对象里，答「不是」；
    /// ③ 按第 ③ 步当<b>对账门</b>处理，不属第二百五十六轮收口那一族，照常随实物一起长。
    /// </para>
    /// </summary>
    [Fact]
    public void SharedEvidenceCopy_HasOneLiteralAndTheTwoScreensThatReadEvidence()
    {
        var problems = EvidenceCopyProblems(
            Repo.Source("Desktop", "Native", "LoadFailureState.cs"),
            Repo.NativeSources().Select(s => (s.Name, s.Code)).ToList(),
            ["EndpointTraceDialog.cs", "EvidenceDialog.cs"]);
        Assert.True(problems.Count == 0,
            "同一份 evidence.json 读不出来，在两屏只该有一句说法，而读它的那两屏要能被看见："
            + string.Join("；", problems));
    }

    /// <summary>
    /// 上面那条判据<b>自己</b>被抓得住：合规形状不报，六发各报该报的那一处。
    /// 夹具是一份假口径源码 + 四屏假代码，磁盘一个字不动（本仓那句老话——「判据自己没被跑过」比「没有判据」更贵）。
    /// <para>
    /// <b>第 ② 发是磁盘教出来的</b>：本轮同一发的磁盘版本跑在「引用其一」那一版判定上是<b>绿的</b>，
    /// 于是判定本体收成「两个成员都要引用」，这一发与那条磁盘注入现在都红得下来。
    /// </para>
    /// </summary>
    [Fact]
    public void EvidenceCopyDetector_ReportsTheShapesItMustCatch()
    {
        const string title = "读取代码证据失败";
        var shared = "public const string Title = \"" + title + "\";\npublic const string Hint = DirectoryHint;\n";
        var shipped = new[] { "EndpointTraceDialog.cs", "EvidenceDialog.cs" };
        const string readLine = "store = _tasks.Read<EvidenceStore>(_task.Id, TaskFiles.Evidence);";
        const string quoteLine =
            "ShowHint(LoadFailureState.FailureMessage(LoadFailureState.Evidence.Title, error, LoadFailureState.Evidence.Hint), UiTokens.BadText, canRetry: true);";
        var screens = new (string Name, string Code)[]
        {
            ("LoadFailureState.cs", shared),
            ("EvidenceDialog.cs", quoteLine + "\n" + readLine),
            ("EndpointTraceDialog.cs", quoteLine + "\n" + readLine),
            // 一张不读那份产物的屏：注入 ⑤ 用它当「引用了却不读它」的那一发。
            ("JournalDialog.cs", "rows = JournalEntries.Read(node);"),
        };
        Assert.Empty(EvidenceCopyProblems(shared, screens, shipped));

        // ① 口径侧那句话被抄成第二份。
        var twoLiterals = shared + "\npublic const string Third = \"" + title + "\";";
        Assert.Contains(EvidenceCopyProblems(twoLiterals, screens, shipped),
            p => p.Contains("出现 2 次", StringComparison.Ordinal));

        // ② 那一屏把标题换回自己拼的一句、说明仍取 Evidence.Hint——「引用其一」那一版对这一发是绿的（本轮磁盘注入实测），
        //     收紧之后它报的是「少了哪一个成员」，一屏只报这一处。
        var ownTitle = screens
            .Select(s => s.Name == "EndpointTraceDialog.cs"
                ? (Name: s.Name, Code: s.Code.Replace("LoadFailureState.Evidence.Title", "\"读取追踪明细失败\"", StringComparison.Ordinal))
                : s)
            .ToList();
        var ownTitleProblems = EvidenceCopyProblems(shared, ownTitle, shipped);
        Assert.Contains(ownTitleProblems, p => p.Contains("却没有引用这一对的 Evidence.Title", StringComparison.Ordinal));
        Assert.Single(ownTitleProblems);

        // ③ 第三屏也开始读那份产物却不接这一对（下一轮新增一屏读它的形态）。
        //     同一发里「登记的集合与真读的集合不一致」也报出来——那两句说的都是这一发真破坏了的东西。
        var newReader = screens
            .Concat([(Name: "ScanWarningsDialog.cs", Code: readLine)])
            .ToList();
        var newReaderProblems = EvidenceCopyProblems(shared, newReader, shipped);
        Assert.Contains(newReaderProblems, p => p.Contains("却没有引用这一对的", StringComparison.Ordinal));
        Assert.Contains(newReaderProblems, p => p.Contains("登记的是", StringComparison.Ordinal));

        // ④ 已接的一屏另把那五个字抄进自己屏里（两个成员都还在、也仍读那份产物，多出来的只是第二处字面）。
        var secondCopy = screens
            .Select(s => s.Name == "EvidenceDialog.cs"
                ? (Name: s.Name, Code: s.Code + "\n_status.Text = \"" + title + "（已停用）\";")
                : s)
            .ToList();
        Assert.Contains(EvidenceCopyProblems(shared, secondCopy, shipped),
            p => p.Contains("抄了第二份", StringComparison.Ordinal));

        // ⑤ 引用了那一对却不读那份产物：那句话说的就是它读不出来，念到别的屏上就是答非所问。
        var wrongRegister = screens
            .Select(s => s.Name == "JournalDialog.cs"
                ? (Name: s.Name, Code: s.Code + "\n" + quoteLine)
                : s)
            .ToList();
        Assert.Contains(
            EvidenceCopyProblems(shared, wrongRegister, ["EndpointTraceDialog.cs", "EvidenceDialog.cs", "JournalDialog.cs"]),
            p => p.Contains("却不读", StringComparison.Ordinal));

        // ⑥ 名单里漏掉一屏真读那份产物的（登记与实物各指一处——收屏忘了登记、或加屏忘了删行都红在这一句）。
        Assert.Contains(EvidenceCopyProblems(shared, screens, ["EvidenceDialog.cs"]),
            p => p.Contains("登记的是", StringComparison.Ordinal));
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    // ---------- 盲区对面的一道：不把用户支去「重开窗口」 ----------

    /// <summary>
    /// 把用户支去重开窗口的几种说法。发现口径是<b>那几种说法本身</b>，不是「有没有引用口径文件」。
    /// <para>
    /// 为什么要单独一份名单：<see cref="StateMarkers"/> 那一族只看得见<b>已经接入</b>重试口径的屏——
    /// 一屏从不引用 <c>LoadFailureState</c>，就从不被那一族看见。第三百零三轮逐字读过才确认全原生界面
    /// 还有三处失败提示写的是「请关闭后…」。这一条判的就是那一句句子里的东西。
    /// </para>
    /// </summary>
    private static readonly string[] SendAwayPhrases = ["请关闭后", "关闭本窗口重新打开", "关闭后重试"];

    /// <summary>
    /// 还欠着的屏（封闭豁免名单，第三百零三轮立起这一条时逐字现读）。
    /// <para>
    /// <b>为什么允许豁免存在</b>：本轮接入的是三屏（注释候选、补充说明、注释回写）。同一族另有七屏——
    /// 一次改十屏等于把十次「读法接线 + 按钮行加一列 + 落点与键盘归属」叠成一轮，改坏了没有一发红会指得清
    /// 是哪一屏。<b>但欠账不能只活在散文中</b>（本仓第一百九十九轮那句「散文不会红」）：这一张名单是封闭的，
    /// 名单里每一个文件<b>现在仍然</b>必须命中那几种说法之一——收掉一屏却不删这一行，
    /// <see cref="NoScreen_SendsTheUserAwayToReopenTheWindow"/> 当场红；新写第八处同样红（它不在名单里）。
    /// </para>
    /// <para>
    /// <b>第三百零五轮收掉第一条</b>：注释缺口（<c>ScanResultsDialog</c>）读的是同一份 symbols.json，
    /// 接法与上面那句完全一致（读取收进 <c>LoadAll</c> + 那颗取自口径的「重试」），于是那一行从名单里删掉、
    /// 名单由七条缩到六条。这一行不是删了就完事——删的同一轮它必须真的不再命中禁词（否则 <c>offenders</c>
    /// 那一句红），而名单里剩下六条若被谁偷偷改掉却没人删行，<c>stale</c> 那一句同样红。两侧都判，才留不下死条目。
    /// </para>
    /// <para>
    /// <b>第三百零八轮收掉第二条</b>：检查结果（<c>CheckResultsDialog</c>）读的是与差异确认、补充说明<b>同一份
    /// diff.json</b>，接法照上一条（读取收进 <c>LoadAll</c> + 那颗取自口径的「重试」），名单由六条缩到五条。
    /// 这一条比上一条更该删：那两家第三百零三轮起就有那颗按钮、写的就是「点「重试」再读一次」，
    /// 而这一屏写的是第三种说法、屏上没有入口——同一份产物、同一句下一步，一种说法有按钮、另一种没有，
    /// 那正是第三百零三轮立这一条时点名的盲区（本轮顺手把那一句收成口径文件顶层一对共用常量，见
    /// <see cref="SharedCheckCopy_HasOneLiteralAndTheThreeScreensThatReadDiff"/>）。删的同一轮它必须真的不再
    /// 命中禁词，剩下五条里被谁偷偷改掉却没人删行同样红——两侧都判的读法一字未变。
    /// </para>
    /// <para>
    /// <b>第三百零九轮收掉第三条</b>：代码证据（<c>EvidenceDialog</c>）与接口追踪明细（<c>EndpointTraceDialog</c>，
    /// 仍欠着）读的是<b>同一份 evidence.json</b>，两边那句下一步写的还是同一句话、而两屏都没有做那次读取的入口。
    /// 名单由五条缩到四条。<b>简报那条选屏判据本轮现场读过，仍不成立</b>（第三百零八轮同一处教训）：
    /// 剩下四屏里，接口追踪明细那颗「查看代码证据」做的是看某一条证据、不是重读这一屏的清单，
    /// 生成文档核对只剩「关闭」，响应契约那三颗审阅按钮在失败态被提示出口一并禁用、
    /// 点下去什么都不做，同步预览那颗「重新核对（只读）」在预览页而失败发生在勾选页、做的也不是那一次读取——
    /// 没有一屏有一颗等价入口，故本轮仍走「新增一颗取自 <c>LoadFailureState.RetryLabel</c> 的按钮 + 读取收进
    /// <c>LoadAll</c>」那一支（与第三百零三、三百零五、三百零八轮同一形状）。
    /// </para>
    /// <para>
    /// <b>第三百一十轮收掉第四条</b>：接口追踪明细（<c>EndpointTraceDialog</c>）读的正是上一轮那一屏读的
    /// <b>同一份 evidence.json</b>（全原生界面只有这两处读它），两边那句下一步是同一句「请关闭后重新扫描再试」，
    /// 而两屏此前都没有做那次读取的入口。名单由四条缩到三条，口径文件那一对（<c>LoadFailureState.Evidence</c>）
    /// 因此从一屏变两屏——上一轮那段「为什么只收这一屏」欠的就是这一笔，本轮收的是它自己那一笔。
    /// 选屏判据本轮现场又读了一遍：剩下三屏里 <c>ContractsDialog</c> 那三颗审阅按钮在失败态被提示出口一并禁用、
    /// <c>PropagationDialog</c> 只剩「关闭」、<c>SyncPreviewDialog</c> 那颗「重新核对（只读）」在预览页而
    /// 失败发生在勾选页且做的不是那一次读取——仍没有一屏有等价入口，故仍走新增那颗取自口径的按钮那一支。
    /// <b>「两句收成一句」在门上是看得见的</b>：
    /// <see cref="SharedEvidenceCopy_HasOneLiteralAndTheTwoScreensThatReadEvidence"/> 两侧对账
    /// （口径侧那一句字面只出现一次 ↔ 界面侧读那份产物的屏恰是这两屏、且两屏都不抄第二份），
    /// 与第三百零五、三百零八轮那两条同形状。
    /// </para>
    /// <para>
    /// <b>理由里点名的那个落点，本轮起要能在源码里找到</b>（第三百零六轮，简报 P1-2）：上面那一句
    /// 「这一屏的下一步在屏外」此前只是一句被读到的散文。第三百零五轮删掉 <c>ScanResultsDialog</c> 那一行之后，
    /// 剩下两条写的还是「同上」——「上」那一行已经不在了，理由指向一个不存在的东西，而名单照旧六条、
    /// <c>stale</c> 那一句照旧绿。所以现在每条理由必须写成<b>「某页」页的「某入口」</b>这个相邻形状，
    /// 由 <see cref="SendAwayExemptions_EveryReasonNamesAnEntryThatShips"/> 逐条到 <c>Desktop/Native</c> 里找：
    /// 点名的文字必须是界面出厂的按钮/标签文字（不是某句提示里顺手写到的动词），点名的那一页必须有界面文件，
    /// 被承诺在那一页上的就是紧接「某页」页的<b>那一颗</b>（其余点名如屏上那颗「关闭」只要求出厂过，
    /// 不假定在哪一页）；那一屏的失败句里若已经指到某一页，理由也必须写同一页。
    /// <b>它不判什么</b>：不判「这一屏该不该豁免」——那是产品判断，只有人读得出来；也不判理由的句子通不通
    /// （句子的长度与空话由 <c>GateSourceConsistencyTests</c> 对另一张表判的那一条同族，这里不叠第二套）。
    /// 按第二百八十二轮那五步判：① 断言里出现的名字是 <c>SendAwayExemptions.Reason</c>（登记侧）与
    /// <c>Desktop/Native</c> 出厂的界面文字、两页的界面文件（实物侧）；② 后者不在被它判的那个对象里，答「不是」；
    /// ③ 按第 ③ 步当<b>对账门</b>处理，不属第二百五十六轮收口那一族，照常随实物一起长。
    /// </para>
    /// </summary>
    private static readonly (string File, string Reason)[] SendAwayExemptions =
    [
        ("ContractsDialog.cs", "响应契约：读不出来时这一屏只剩「关闭」可点（提示出口把三颗审阅按钮一并禁用），"
            + "那一步在「检查任务」页的「开始扫描」上——那三颗在审阅保存成功之后确实会重读一次清单，可失败态下点不动"),
    ];

    /// <summary>判定本体（纯函数：源码进、命中的说法出）——注入自测直接喂夹具，不必改磁盘。</summary>
    internal static List<string> SendAwayHits(string code) => SendAwayPhrases.Where(code.Contains).ToList();

    [Fact]
    public void NoScreen_SendsTheUserAwayToReopenTheWindow()
    {
        var exempt = SendAwayExemptions.Select(x => x.File).ToArray();
        var offenders = Repo.NativeSources()
            .Where(s => !exempt.Contains(s.Name))
            .Select(s => (s.Name, Hits: SendAwayHits(CodeOnly(s.Text))))
            .Where(x => x.Hits.Count > 0)
            .Select(x => $"{x.Name}：{string.Join("、", x.Hits)}")
            .ToList();
        Assert.True(offenders.Count == 0,
            "这些界面文件把「再读一次」交给用户手动重开窗口，而屏上没有可点的入口（DESIGN.md §7「禁用不得移除下一步」）："
            + string.Join("；", offenders)
            + "。接法两条：加那颗取自口径的「重试」，或让提示句指名屏上那颗已有入口（LoadFailureState.RetryHint）。");

        // 豁免不许过期：名单里每一个文件现在仍然要命中。收掉一屏忘了删这一行，这里红。
        var stale = SendAwayExemptions
            .Where(x => SendAwayHits(CodeOnly(Repo.NativeSources().Single(s => s.Name == x.File).Text)).Count == 0)
            .Select(x => x.File)
            .ToList();
        Assert.True(stale.Count == 0,
            "这些文件已经不再写「请关闭后…」了，却还留在豁免名单里：" + string.Join("、", stale)
            + "。收掉一屏就删掉那一行——留在名单里，下一轮就看不见还剩几屏没接。");
    }

    [Fact]
    public void SendAwayDetector_ReportsThePhrasesItMustCatch()
    {
        // 三种说法各报一次。
        Assert.Equal(["请关闭后"], SendAwayHits("_hint.Text = \"读取失败：磁盘满。请关闭后在任务页重试。\";"));
        Assert.Equal(["关闭后重试"], SendAwayHits("_hint.Text = \"读取失败：磁盘满。关闭后重试。\";"));
        Assert.Equal(["关闭本窗口重新打开"], SendAwayHits("_hint.Text = \"生成失败：磁盘满。可关闭本窗口重新打开。\";"));

        // 合规写法不报：① 指名屏上那颗「重试」；② 指名另一颗已有入口；
        // ③ 「可重新运行或关闭本窗口」——它先给了屏上真有的那颗「重新运行」，没有把用户支走。
        Assert.Empty(SendAwayHits("_hint.Text = \"读取失败：磁盘满。任务目录可能被占用或权限不足；点「重试」再读一次。\";"));
        Assert.Empty(SendAwayHits("_hint.Text = \"生成失败：磁盘满。可点「重新生成计划」重试。\";"));
        Assert.Empty(SendAwayHits("_hint.Text = \"已停止写入兼容探测。可重新运行或关闭本窗口。\";"));
    }

    // ---------- 豁免理由点名的那个落点，逐条到源码里找（第三百零六轮，简报 P1-2） ----------

    /// <summary>
    /// 两个主页面：<b>源码里的引用名</b> → <b>出厂的页面名</b> → <b>那一页的界面文件</b>。
    /// <para>
    /// 三样一起登记是因为理由指的是<b>一整个落点</b>：「在「检查任务」页的「开始扫描」上」这句话要同时成立，
    /// 得有那么一页、那一页有那个界面文件、而那颗按钮就在那个文件里。分开判会出现一种中间状态——
    /// 页面名对、按钮也在仓里，可它在别那一页上，用户照理由走过去找不到。
    /// 新增第三个页面时这里必须跟着登记，否则理由点了那一页会红着要求登记，而不是静默放过。
    /// </para>
    /// </summary>
    private static readonly (string Reference, string Page, string File)[] Pages =
    [
        ("PageNames.Projects", PageNames.Projects, "ProjectsPage.cs"),
        ("PageNames.Tasks", PageNames.Tasks, "TasksPage.cs"),
    ];

    /// <summary>
    /// 界面<b>出厂</b>的按钮 / 标签文字，两种形状（全 <c>Desktop/Native</c> 逐字读过之后定下的唯一取法）：
    /// ① <c>Ui.Ghost("…")</c> / <c>Ui.Primary("…")</c> 的字面参数——工厂直接给出的那颗按钮；
    /// ② <c>const string X = "…";</c> 的字面值——口径文件与页面里那份文字常量（两个页面名就在这形状里）。
    /// <para>
    /// <b>为什么不算第三种形状</b>：一句提示里出现的动词（「请关闭后重新扫描再试」里的「重新扫描」）不是屏幕上
    /// 能被点着的东西。把它当成「落点」正是这一条要挡的写法——理由念起来像指了地方，而那一处根本不在界面上。
    /// 取的都是<b>去注释后的代码</b>（<see cref="CodeScan"/> 那一份口径）：注释里点名一颗按钮是在说明理由，
    /// 不是出厂一个文字。声明写成跨行时这里取不到，于是点名它的那条理由<b>红</b>——这一族宁可误红，
    /// 不能把「界面里没有」读成「界面里有」。
    /// </para>
    /// </summary>
    private static IReadOnlySet<string> ShippedLabels()
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in Repo.NativeSources())
        {
            foreach (Match m in Regex.Matches(source.Code, @"Ui\.(?:Ghost|Primary)\(\s*""([^""]+)"""))
            {
                labels.Add(m.Groups[1].Value);
            }
            foreach (Match m in Regex.Matches(source.Code, @"const string \w+ = ""([^""]+)"";"))
            {
                labels.Add(m.Groups[1].Value);
            }
        }
        return labels;
    }

    /// <summary>文件名 → 去注释后的代码（不在仓里的名字交出 null，由判定本体报成「名单指着一屏不存在的界面」）。</summary>
    private static Func<string, string?> NativeCodeByName()
    {
        var byName = Repo.NativeSources().ToDictionary(s => s.Name, s => s.Code, StringComparer.Ordinal);
        return name => byName.GetValueOrDefault(name);
    }

    /// <summary>
    /// 判定本体（纯函数：一份登记 + 一份源码取法 + 一份出厂文字进，问题出）——注入自测直接喂夹具，不改磁盘。
    /// <para>判五件事：① 名单里那一屏得真的在 <c>Desktop/Native</c> 里；② 理由不许写「同上」（它指的是上一行，
    /// 而上一行会被收掉）；③ 理由点名的每一处文字都必须是<b>界面出厂的</b>按钮 / 标签文字；④ 每一条理由都必须写成
    /// 「某页」页的「某入口」那个相邻形状，紧接的那一颗必须真在那一页的界面文件里（点了 <see cref="Pages"/> 里没有的
    /// 那一页，另报一处）；⑤ 反向：那一屏的失败句里已经点到某一页，理由必须写同一页——句子与登记不许各指一处。</para>
    /// </summary>
    internal static List<string> ReasonProblems(
        IEnumerable<(string File, string Reason)> exemptions,
        Func<string, string?> codeOf,
        IReadOnlySet<string> labels)
    {
        var problems = new List<string>();
        foreach (var (file, reason) in exemptions)
        {
            var code = codeOf(file);
            if (code is null)
            {
                problems.Add($"豁免名单里的 {file} 在 Desktop/Native 找不到：名单指着一屏不存在的界面。");
                continue;
            }

            if (reason.Contains("同上", StringComparison.Ordinal))
            {
                problems.Add($"{file}：理由写了「同上」——那指的是名单里上一行，而上一行随时会被收掉："
                    + "第三百零五轮收掉 ScanResultsDialog 之后，剩下两条「同上」指的就是一个不存在的东西。");
            }

            var names = Regex.Matches(reason, @"「([^」]+)」").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            foreach (var name in names)
            {
                if (!labels.Contains(name))
                {
                    problems.Add($"{file}：理由点名的「{name}」不是界面出厂的按钮 / 标签文字"
                        + "（它既不是 Ui.Ghost / Ui.Primary 的字面参数，也不是任何 const string 的值）："
                        + "一句提示里出现的那个动词，不是屏幕上能点着的东西。");
                }
            }

            // 「那一步在哪」这句承诺要落成可判的形状：必须写成「某页」页的「某入口」，紧接的那一颗就是被承诺
            // 在那一页上的入口。只说「这一屏没有入口」「只剩「关闭」可点」都挡不住这一条——那两句说的是这一屏，
            // 不是那一步。
            var claim = Regex.Match(reason, @"「([^」]+)」页的「([^」]+)」");
            if (!claim.Success)
            {
                problems.Add($"{file}：理由没写成「某页」页的「某入口」那个形状——屏外那一步到底点哪一颗，下一轮只能靠猜。");
                continue;
            }

            var claimedRow = Pages.FirstOrDefault(p => p.Page == claim.Groups[1].Value);
            if (claimedRow.Page is null)
            {
                problems.Add($"{file}：理由点了「{claim.Groups[1].Value}」页，而 Pages 里没有这一页——新增页面要在这里连文件一起登记。");
                continue;
            }

            var entry = claim.Groups[2].Value;
            var pageCode = codeOf(claimedRow.File);
            if (pageCode is null)
            {
                problems.Add($"{file}：理由说那一步在「{claimedRow.Page}」页上，可 {claimedRow.File} 不在仓里。");
            }
            else if (!pageCode.Contains("\"" + entry + "\"", StringComparison.Ordinal))
            {
                problems.Add($"{file}：理由说「{entry}」在「{claimedRow.Page}」页上，可那一页的 {claimedRow.File} 里没有这个文字"
                    + "——用户照理由走过去找不到那颗按钮。");
            }

            // 反向：那一屏的失败句已经指到某一页，理由必须写同一页——句子与登记不许各指一处。
            var hits = code.Split('\n').Where(line => SendAwayHits(line).Count > 0).ToList();
            foreach (var pageRow in Pages)
            {
                if (hits.Any(h => h.Contains(pageRow.Reference, StringComparison.Ordinal)) && !names.Contains(pageRow.Page))
                {
                    problems.Add($"{file}：那一屏的失败句点的是「{pageRow.Page}」页（句子里写的 {pageRow.Reference}），"
                        + "理由却没写这一页——界面指一处、登记指另一处，下一轮照着理由去找就找不到。");
                }
            }
        }
        return problems;
    }

    [Fact]
    public void SendAwayExemptions_EveryReasonNamesAnEntryThatShips()
    {
        var problems = ReasonProblems(SendAwayExemptions, NativeCodeByName(), ShippedLabels());
        Assert.True(problems.Count == 0,
            "豁免理由里点名的落点在源码里找不到（DESIGN.md §7「禁用不得移除下一步」的对面那一半："
            + "欠着的屏至少要说清那一步真的在哪）：" + string.Join("；", problems));
    }

    /// <summary>
    /// 上面那条判据<b>自己</b>被抓得住：六发注入各报一处，合规形状不报（本仓那句老话——
    /// 「判据自己没被跑过」比「没有判据」更贵）。夹具是两份假源码 + 一份出厂文字，磁盘一个字不动。
    /// </summary>
    [Fact]
    public void SendAwayReasonDetector_ReportsTheNamesItMustCatch()
    {
        var labels = new HashSet<string>(StringComparer.Ordinal)
        {
            PageNames.Projects, PageNames.Tasks, "开始扫描", "核对传播", "开始检查", "新建配置", "关闭",
        };
        var codeOf = FixtureCode(
            ("TasksPage.cs", """
                        _scanButton = Ui.Ghost("开始扫描");
                        _checkButton = Ui.Ghost("核对传播");
                        _remoteCheckButton = Ui.Ghost("开始检查");
             """),
            ("ProjectsPage.cs", "        _newProfile = Ui.Ghost(\"新建配置\");"),
            // 那一屏的失败句已经指到「检查任务」页（页 ↔ 反向那一判都从这里取）。
            ("EvidenceDialog.cs", "        ShowHint(\"读取代码证据失败。请关闭后在「\" + PageNames.Tasks + \"」页重新扫描再试。\");"),
            // 那一屏的失败句没有指任何一页：注入 ①—④ 用它，好让每一发只报该报的那一处。
            ("ContractsDialog.cs", "        ShowHint(\"读取响应契约失败。请关闭后重新扫描再试。\");"));

        // 合规三条：① 句子指「检查任务」页、理由写同一页且那颗入口真在 TasksPage.cs 里；② 句子没指页、理由指了页；
        // ③ 理由先点名屏上那颗「关闭」、再写「某页」页的「某入口」——页名那一组必须是「不带」的」，
        //    写成 .+? 会跨过下一个「」一路吃到最近的「某页」页，把那颗「关闭」当成页名（本轮真门当场抓出来的）。
        var ok = new[]
        {
            ("EvidenceDialog.cs", "代码证据：这一屏没有做那次读取的入口，那一步在「检查任务」页的「开始扫描」上"),
            ("ContractsDialog.cs", "响应契约：这一屏同样没有入口，那一步在「检查任务」页的「开始扫描」上"),
            ("ContractsDialog.cs", "响应契约：这一屏只剩「关闭」可点，那一步在「检查任务」页的「开始扫描」上"),
        };
        Assert.Empty(ReasonProblems(ok, codeOf, labels));

        // ① 「同上」：指的是上一行，而上一行会被收掉。
        var dangling = new[] { ("ContractsDialog.cs", "代码证据：那一步同上在「检查任务」页的「开始扫描」上") };
        Assert.Contains(ReasonProblems(dangling, codeOf, labels), p => p.Contains("同上", StringComparison.Ordinal));

        // ② 没写成那个形状：只说「屏上没有入口」，没说那一步点哪一颗。
        var unnamed = new[] { ("ContractsDialog.cs", "代码证据：这一屏没有入口，回任务页重扫一次就行") };
        Assert.Contains(ReasonProblems(unnamed, codeOf, labels), p => p.Contains("没写成", StringComparison.Ordinal));

        // ③ 点名的是提示句里的一个动词，界面上没有那一处（同一发里「那一页的文件里没有这个文字」也报出来：
        //     那颗既没出厂、也不可能在任务页里——两条都是这一发要挡的）。
        var verb = new[] { ("ContractsDialog.cs", "代码证据：那一步在「检查任务」页的「重新扫描全部接口」上") };
        Assert.Contains(ReasonProblems(verb, codeOf, labels), p => p.Contains("不是界面出厂的按钮", StringComparison.Ordinal));

        // ④ 页 ↔ 入口错位：那颗按钮在任务页，理由却说它在配置页。
        var wrongPage = new[] { ("ContractsDialog.cs", "代码证据：那一步在「项目设置」页的「开始扫描」上") };
        Assert.Contains(ReasonProblems(wrongPage, codeOf, labels), p => p.Contains("ProjectsPage.cs 里没有这个文字", StringComparison.Ordinal));

        // ⑤ 反向：句子指「检查任务」页，理由改指「项目设置」页——两处各指一处。
        var mismatch = new[] { ("EvidenceDialog.cs", "代码证据：那一步在「项目设置」页的「新建配置」上") };
        Assert.Contains(ReasonProblems(mismatch, codeOf, labels), p => p.Contains("理由却没写这一页", StringComparison.Ordinal));

        // ⑥ 名单里那一屏根本不存在。
        var ghost = new[] { ("NoSuchDialog.cs", "不存在：那一步在「检查任务」页的「开始扫描」上") };
        Assert.Contains(ReasonProblems(ghost, codeOf, labels), p => p.Contains("在 Desktop/Native 找不到", StringComparison.Ordinal));

        // ⑦ 点了 Pages 里没有登记的那一页：新增第三个页面而这里没跟着登记，理由指的那一页就没有界面文件可对，
        //     这一发要红着要求人把 Pages 一起改，而不是静默放过（同发里「界面上没出厂」那条也一并报出来）。
        var newPage = new[] { ("ContractsDialog.cs", "代码证据：那一步在「分析报表」页的「开始扫描」上") };
        Assert.Contains(ReasonProblems(newPage, codeOf, labels), p => p.Contains("Pages 里没有这一页", StringComparison.Ordinal));
    }

    /// <summary>注入夹具：文件名 → 那段假源码（磁盘一个字不动，取法与真门同一个委托形状）。</summary>
    private static Func<string, string?> FixtureCode(params (string Name, string Code)[] files)
    {
        var byName = files.ToDictionary(f => f.Name, f => f.Code, StringComparer.Ordinal);
        return name => byName.GetValueOrDefault(name);
    }
}
