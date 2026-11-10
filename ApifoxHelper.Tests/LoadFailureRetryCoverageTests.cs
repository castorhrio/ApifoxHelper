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
/// 发现本身也要防退化：<see cref="Discovery_CoversExactlyTheFiveScreens"/> 把「发现到哪几屏」
/// 钉成封闭清单——发现正则写坏（比如口径文件改名）会让清单变空，那条用例先红，
/// 不会出现「一条都没发现、于是全部通过」的假绿。
/// </para>
/// </summary>
public class LoadFailureRetryCoverageTests
{
    /// <summary>失败态口径文件自身：它们定义机制与文案，不是「有失败态的屏」，不参与接线断言。</summary>
    private static readonly string[] StateFiles = ["LoadFailureState.cs", "ProfileLoadState.cs"];

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
    private static string CodeOnly(string source) =>
        string.Join('\n', source.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // ---------- 发现：范围本身要钉住，否则「一条都没发现」也是绿的 ----------

    [Fact]
    public void Discovery_CoversExactlyTheFiveScreens()
    {
        // 封闭清单：新增一屏带失败态时，这里会红——提醒把新屏的接线一并补上（而不是悄悄漏掉）。
        // 第一百五十七轮新增第五屏 JournalDialog.cs（任务日志对话框的读取失败态）。
        var names = FailureScreens().Select(s => s.Name).ToArray();
        Assert.Equal(["DecisionsDialog.cs", "JournalDialog.cs", "ProjectsPage.cs", "ReportsDialog.cs", "TasksPage.cs"], names);
    }

    // ---------- 接线：每一屏都必须有「重试」，且回到同一个读取入口 ----------

    [Fact]
    public void EveryFailureScreen_HasOneRetryButtonFromTheSharedLabel()
    {
        foreach (var screen in FailureScreens())
        {
            var field = RetryField(screen);
            // 按钮要真的挂到屏上：造出来不 Add 等于没有（页面挂进空态动作行，对话框挂进动作行网格）。
            Assert.Matches(@"\.Controls\.Add\(" + Regex.Escape(field) + @"[,)]", screen.Text);
        }
    }

    [Fact]
    public void EveryFailureScreen_RetryCallsTheSameLoadEntryAsTheInitialRead()
    {
        foreach (var screen in FailureScreens())
        {
            var field = RetryField(screen);
            var target = RetryTarget(screen, field);

            // ① 重试回到的是一个读取入口（Load*），不是另写一份读取逻辑、也不是「重开窗口」。
            Assert.Matches(@"^Load\w+$", target);
            // ② 那个入口在本屏有定义（不是调一个不存在的方法）。
            Assert.Matches(@"private\s+(?:async\s+)?(?:void|Task)\s+" + Regex.Escape(target) + @"\(", screen.Text);

            // ③ 首次读取走的是同一个入口：打开这一屏（或这一页出现）时调的就是它。
            //    只认「打开」这一条路（OnLoad / OnPageShown）——RefreshData 是刷新入口，
            //    它调什么不能替「打开时读的是哪个方法」作证。
            var anchor = InitialReadAnchors.FirstOrDefault(a => screen.Text.Contains(a, StringComparison.Ordinal));
            Assert.True(anchor is not null, $"{screen.Name}：找不到首次读取入口（OnLoad / OnPageShown）。");
            var initial = Body(screen.Text, anchor!);
            Assert.True(
                initial.Contains(target + "();", StringComparison.Ordinal),
                $"{screen.Name}：打开这一屏时没有走 {target}()——「重试」与打开时读的不是同一个入口，两条路会各自漂移。");
        }
    }

    [Fact]
    public void EveryFailureScreen_GatesRetryVisibility()
    {
        // 三态同一时刻只显示一个行动按钮：失败态才出现「重试」。两种合法形状——
        // ① 页面：可见性由口径文件的同一份判定给出（RetryVisible），不就地写 if；
        // ② 对话框：读成功时收起（false）、失败时按条件置位（true / canRetry），不留在常显。
        // 两种都不成立的屏（按钮造出来就常显、或只置 true 从不收起）在这里红。
        foreach (var screen in FailureScreens())
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
}
