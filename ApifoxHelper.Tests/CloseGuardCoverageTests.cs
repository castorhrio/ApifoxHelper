using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「带『停止』入口的对话框，关窗守卫有没有接上」的对账门（第四百零四轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：第四百零二轮给探测那一屏、第四百零三轮给同步预览那一屏接上了
/// <see cref="CloseGuardPlan.RefuseWhileBusy"/>——两屏都是「作业跑着 + 那颗『停止』是唯一停止入口 +
/// Esc / × 关窗」这同一个形状。第四百零四轮按控制仓简报 P0-2 只读核查了其余各屏，结论是
/// <b>没有第三处</b>（理由逐条写在 <see cref="CloseGuardPlan"/> 的类注释里）。但那个结论此前只活在
/// 一句散文里：下一轮谁再给某一屏加一颗「停止」，没有任何东西会提醒他「这一屏的关窗也得接上」——
/// 而这一族的后果是「用户以为取消了，实际是把唯一的停止入口关掉了」。
/// </para>
/// <para>
/// <b>本文件判什么</b>（对账门：判的是<b>被承诺的实物</b>，不是判据判自己）：
/// ① <b>表 ↔ 源码两侧对账</b>：<see cref="ScreensWithStopEntry"/> 里登记的每一屏，源码里必须真的
///    带「停止」入口、真的跑共享 <c>JobRunner</c>；反过来，源码里凡是「对话框 + 停止入口 + 跑作业」
///    三件套齐全的，必须登记在表里——<b>多一屏没登记即红</b>，这就是「已核查，无第三处」这句话的机械形态；
/// ② <b>登记了的每一屏都真的接上了守卫</b>：<c>OnFormClosing</c> 里恰好调用一处
///    <see cref="CloseGuardPlan.RefuseWhileBusy"/>、拦下时 <c>e.Cancel = true</c>、拦下之后说得出那一句
///    （<c>CloseGuardPlan.*BusyMessage</c>）；
/// ③ <b>判据自己被抓得住</b>（<see cref="Gate_ReportsTheBreaksItMustCatch"/>）：七发注入各报一处、合规写法不报。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「那一句写得对不对」（各屏自己的 <c>*CloseGuardTests</c> 判）、不判
/// 「忙态轮询会不会把那一句抹掉」（同上）、不判真机上按 Esc 的手感（G-D5，真机 0 台）。
/// 也<b>不判</b>「主窗口关窗」那一条路：主窗口关窗 = 退出应用，作业随进程结束，它走的是
/// 「确认 + 宽限取消」那一支（<c>NativeShellForm.OnFormClosing</c>），不是「关掉唯一停止入口」这个形状。
/// </para>
/// <para>
/// <b>将来真有一屏不该拦怎么办</b>：改这一条门时把理由写进 <see cref="CloseGuardPlan"/> 的类注释，
/// 再决定是「接上」还是「从表里去掉并说明」——门与实物一起长，这是本仓对账门的口径；
/// 悄悄把这一屏从表里删掉会让「无第三处」这句话重新变成一句没人核的散文。
/// </para>
/// </summary>
public class CloseGuardCoverageTests
{
    /// <summary>
    /// 带「停止」入口的对话框（<b>表</b>）。新增一屏时把文件名加进来，并在
    /// <see cref="CloseGuardPlan"/> 的类注释里补一处「第 N 处」——两件事一起做，缺一件这一条门就红。
    /// </summary>
    private static readonly string[] ScreensWithStopEntry =
    [
        "CapabilitiesDialog.cs",
        "SyncPreviewDialog.cs",
    ];

    /// <summary>「停止」入口的构造写法（全仓只有 <c>Ui.DangerGhost</c> 一处出厂这种钮）。</summary>
    private const string StopEntry = "Ui.DangerGhost(\"停止\")";

    /// <summary>守卫的调用点（判定只有一份，两屏共用同一支）。</summary>
    private const string GuardCall = "CloseGuardPlan.RefuseWhileBusy(";

    private const string OnFormClosingHeader = "protected override void OnFormClosing(FormClosingEventArgs e)";

    /// <summary>
    /// ① 表 ↔ 源码两侧对账 + ② 登记了的每一屏都真的接上了守卫。
    /// </summary>
    [Fact]
    public void EveryDialogWithAStopEntry_WiresTheCloseGuard()
    {
        var problems = Problems(Sources(), ScreensWithStopEntry);
        Assert.True(problems.Count == 0, string.Join("；", problems));
    }

    /// <summary>
    /// ③ 判据自己被抓得住：把每一种该报的破坏各注入一次，判据必须报出来；合规写法不报。
    /// <para>
    /// 与 <c>SyncCloseGuardTests</c> / <c>CloseGuardPlanTests</c> 同一形状：判据写成「纯文本进、问题出」，
    /// 自测拿<b>注入过的</b>源码喂它。判据判不到自己该抓的东西，就是一条假门。
    /// </para>
    /// </summary>
    [Fact]
    public void Gate_ReportsTheBreaksItMustCatch()
    {
        var real = Sources();
        var probe = real.First(s => s.Name == "CapabilitiesDialog.cs").Text;

        // 合规写法：一处都不报。
        Assert.Empty(Problems(real, ScreensWithStopEntry));

        // ① 第三屏出现（带停止入口、跑作业），但关窗守卫没接。
        var third = probe.Replace(GuardCall, "CloseGuardPlan.RefuseWhileBusyRemoved(", StringComparison.Ordinal);
        var withThird = real.Append(("ThirdDialog.cs", third)).ToList();
        var registeredThird = ScreensWithStopEntry.Append("ThirdDialog.cs").ToList();
        Assert.Contains("ThirdDialog.cs", string.Join("；", Problems(withThird, registeredThird)), StringComparison.Ordinal);

        // ② 接了守卫但忘了 e.Cancel = true：窗口照样关掉，守卫等于没接。
        var noCancel = probe.Replace("            e.Cancel = true;\n", "", StringComparison.Ordinal);
        Assert.Contains("e.Cancel", string.Join("；", Problems(real.Append(("ThirdDialog.cs", noCancel)).ToList(), registeredThird)), StringComparison.Ordinal);

        // ③ 拦下之后不说那一句：用户按 Esc 只会觉得窗口坏了。
        var noMessage = probe.Replace(
            "_statusHost.SetText(CloseGuardPlan.ProbeBusyMessage, UiTokens.WarnText);",
            "_statusHost.SetText(\"\", UiTokens.WarnText);",
            StringComparison.Ordinal);
        Assert.Contains("BusyMessage", string.Join("；", Problems(real.Append(("ThirdDialog.cs", noMessage)).ToList(), registeredThird)), StringComparison.Ordinal);

        // ④ 同一屏里调了两处：这一屏有第二个地方也在决定关不关窗。
        var twice = probe.Replace(
            "CloseGuardPlan.RefuseWhileBusy(_jobs.Running(_scope) is not null, e.CloseReason == CloseReason.UserClosing)",
            "CloseGuardPlan.RefuseWhileBusy(_jobs.Running(_scope) is not null, e.CloseReason == CloseReason.UserClosing)"
            + " && CloseGuardPlan.RefuseWhileBusy(true, true)",
            StringComparison.Ordinal);
        Assert.Contains("2 处", string.Join("；", Problems(real.Append(("ThirdDialog.cs", twice)).ToList(), registeredThird)), StringComparison.Ordinal);

        // ⑤ 源码里多出一屏没登记：表没跟着长，「无第三处」这句话就没人核了。
        Assert.Contains("没登记", string.Join("；", Problems(withThird, ScreensWithStopEntry)), StringComparison.Ordinal);

        // ⑥ 表里养死条目：登记了一屏源码里已经没有停止入口的。
        Assert.Contains("GhostDialog.cs", string.Join("；", Problems(real, ScreensWithStopEntry.Append("GhostDialog.cs").ToList())), StringComparison.Ordinal);

        // ⑦ 那一屏的停止入口被删了（表里还登记着）：两侧对账同样要报。
        var noStop = probe.Replace(StopEntry, "Ui.Ghost(\"停止\")", StringComparison.Ordinal);
        var replaced = real.Where(s => s.Name != "CapabilitiesDialog.cs").Append(("CapabilitiesDialog.cs", noStop)).ToList();
        Assert.Contains("CapabilitiesDialog.cs", string.Join("；", Problems(replaced, ScreensWithStopEntry)), StringComparison.Ordinal);
    }

    /// <summary>Desktop/Native 下的界面源码（文件名 + 全文）。</summary>
    private static List<(string Name, string Text)> Sources()
        => Repo.NativeSources().Select(s => (s.Name, s.Text)).ToList();

    /// <summary>
    /// 把源码与表喂给判据，返回「哪几处对不上」的说明（空 = 全对得上）。
    /// </summary>
    private static List<string> Problems(
        IReadOnlyList<(string Name, string Text)> sources,
        IReadOnlyList<string> registered)
    {
        var problems = new List<string>();
        var detected = new List<string>();

        foreach (var (name, text) in sources)
        {
            var code = CodeScan.Text(text);
            // 三件套按<b>同一个类</b>判，不按整个文件判：一个文件里可以既有页又有对话框
            // （`TasksPage.cs` 里就还有一个 `NewTaskDialog`），按文件判会把「页上的那颗停止」
            // 读成「对话框上的那颗停止」——那正是这一条门要防的假绿/假红。
            var screen = ClassBlocks(code).FirstOrDefault(block =>
                block.Contains(StopEntry, StringComparison.Ordinal)
                && block.Contains("JobRunner", StringComparison.Ordinal)
                && System.Text.RegularExpressions.Regex.IsMatch(block, @"class\s+\w+\s*:\s*(DpiAwareForm|Form)\b"));
            if (screen is null) continue;
            detected.Add(name);

            if (!registered.Contains(name, StringComparer.Ordinal))
            {
                problems.Add($"{name} 带「停止」入口、又跑共享 JobRunner 作业，却没登记在 ScreensWithStopEntry 表里："
                    + "这一屏的关窗守卫有没有接上就没人核了——把它加进表里，并在 CloseGuardPlan 的类注释里补一处「第 N 处」；"
                    + "若这一屏确实不该拦（例如关窗 = 退出应用、作业随进程结束），把理由写进那份类注释再决定。");
                continue;
            }

            var closing = Body(screen, OnFormClosingHeader);
            if (closing is null)
            {
                problems.Add($"{name} 里找不到 OnFormClosing 的方法体：作业跑着的时候关窗没人拦，"
                    + "那颗「停止」会跟着窗口一起消失，而作业在服务层继续跑（JobRunner 不因窗口关闭而取消）。");
                continue;
            }

            var calls = Count(screen, GuardCall);
            if (calls != 1)
            {
                problems.Add($"{name} 里调用 CloseGuardPlan.RefuseWhileBusy 出现 {calls} 处（应为 1 处，在 OnFormClosing 里）："
                    + "0 处 = 判定没人用；多于 1 处 = 这一屏有第二处也在决定关不关窗。");
            }
            else if (!closing.Contains(GuardCall, StringComparison.Ordinal))
            {
                problems.Add($"{name} 的 OnFormClosing 里没有调用 CloseGuardPlan.RefuseWhileBusy："
                    + "判定接在了别处，关窗那一条路（Esc / × / CancelButton 都汇到 FormClosing）接不到它。");
            }

            if (!closing.Contains("e.Cancel = true;", StringComparison.Ordinal))
            {
                problems.Add($"{name} 的 OnFormClosing 拦下分支里没有 e.Cancel = true;：不取消这一次关窗，守卫等于没接。");
            }

            if (!closing.Contains("BusyMessage", StringComparison.Ordinal))
            {
                problems.Add($"{name} 的 OnFormClosing 拦下之后没有写 CloseGuardPlan 里那一句（*BusyMessage）："
                    + "拦下不说一句为什么，用户按 Esc 只会觉得窗口坏了。");
            }
        }

        foreach (var name in registered)
        {
            if (!detected.Contains(name, StringComparer.Ordinal))
            {
                problems.Add($"表里登记的 {name} 在 Desktop/Native 里找不到「停止」入口（或它已经不再跑作业）："
                    + "表不许养死条目——这一屏若真的不再有这个形状，把它从表里去掉，并在 CloseGuardPlan 的类注释里改准那一处。");
            }
        }

        return problems;
    }

    /// <summary>
    /// 从去注释后的源码里取一个方法体（从签名那一行到与之配对的右花括号）。
    /// <para>
    /// 按花括号配对数取，不按「下一个 <c>private</c>」取：方法体里有 <c>if</c> 的花括号，
    /// 按行首关键字切会把方法体截短，而截短之后「里面没有某一行」会假绿。
    /// </para>
    /// </summary>
    private static string? Body(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return null;
        var open = code.IndexOf('{', start);
        if (open < 0) return null;
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}')
            {
                depth--;
                if (depth == 0) return code[open..(i + 1)];
            }
        }
        return null;
    }

    /// <summary>
    /// 把一份源码按<b>顶层类声明</b>切成若干块（每块从 `class X : Base` 那一行到与之配对的右花括号）。
    /// <para>
    /// 为什么要切：一个文件里可以有多个类（`TasksPage.cs` 里既有页 `TasksPage` 又有对话框 `NewTaskDialog`），
    /// 按整个文件判「有没有停止入口 / 跑不跑作业」会把两个类的事混成一件。
    /// </para>
    /// </summary>
    private static List<string> ClassBlocks(string code)
    {
        var blocks = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(code, @"^\s*(?:internal|public)?\s*(?:sealed\s+|abstract\s+)?class\s+\w+", System.Text.RegularExpressions.RegexOptions.Multiline))
        {
            var open = code.IndexOf('{', match.Index);
            if (open < 0) continue;
            var depth = 0;
            for (var i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        blocks.Add(code[match.Index..(i + 1)]);
                        break;
                    }
                }
            }
        }
        return blocks;
    }

    /// <summary>数一段文本里某个片段出现的次数。</summary>
    private static int Count(string text, string fragment)
    {
        var total = 0;
        var index = text.IndexOf(fragment, StringComparison.Ordinal);
        while (index >= 0)
        {
            total++;
            index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal);
        }
        return total;
    }
}
