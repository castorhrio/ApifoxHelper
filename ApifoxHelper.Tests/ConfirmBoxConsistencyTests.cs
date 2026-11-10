using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 原生确认框的一致性（第一百四十六轮，具名 pass：harden）。
/// <para>
/// 存在理由：全原生界面有六处原生确认框，此前只有注释回写那一处被
/// <c>ApplyPlanConfirmTests</c> 钉住「默认按钮在取消 + 正文取共享口径 + 一处落点」，
/// 其余五处（写入探测、模型生成、定向构建、同步执行、退出）没有任何机械检查：
/// 有人把某一处的默认按钮从「取消」改成「确定」（回车误触即写入远端或写源码）、
/// 或把正文就地拼回窗体里，测试全绿。同壳不同行为 = 缺口，不是偏好。
/// </para>
/// <para>
/// 本文件分两层钉：① 正文收进 <see cref="ConfirmEntries"/> 后**真执行**断言
/// （共享源码编入测试工程，跑的是主工程同一份代码）；② 六处调用点用源码切片钉住
/// （WinForms 控件触达在本环境不可执行，不冒充跑过）。真实运行时证据在
/// <c>ApifoxHelper.Smoke</c>（wine 下构造对话框并核对默认委托）。
/// </para>
/// </summary>
public class ConfirmBoxConsistencyTests
{
    /// <summary>
    /// 登记表：<c>Desktop/Native</c> 下每一处 <c>MessageBox.Show(</c> 的落点与处数。
    /// 清单是封闭的——新增一处确认框必须在这里登记，否则「默认按钮在取消」这条检查覆盖不到它。
    /// </summary>
    private static readonly (string File, int Count)[] Registered =
    [
        ("ApplyPlanDialog.cs", 1),
        ("CapabilitiesDialog.cs", 1),
        ("NativeShellForm.cs", 1),
        ("SyncPreviewDialog.cs", 1),
        ("TasksPage.cs", 2),
    ];

    /// <summary>
    /// 正文的共享口径：确认框的正文只能来自这几处，不得就地拼字。
    /// 清单同样是封闭的——新增一处口径要在这里登记，否则「正文取共享口径」这条检查会漏掉它。
    /// </summary>
    private static readonly string[] CaliberCalls =
    [
        "ApplyPlanEntries.ConfirmApplyText(",
        "CompatEntries.ProbeConfirmText(",
        "DraftEntries.ModelConfirmText(",
        "BuildStatus.ConfirmText(",
        "ConfirmEntries.ExitConfirmText(",
        "ConfirmEntries.SyncConfirmText(",
    ];

    // ---------- 调用点：登记表 ----------

    [Fact]
    public void EveryConfirmBox_IsRegistered()
    {
        var actual = CallSites()
            .GroupBy(c => c.File, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var expected = Registered.ToDictionary(r => r.File, r => r.Count, StringComparer.Ordinal);

        // 少一处：有人删了确认框（写入类动作不再问一句）——必须显式改登记表。
        // 多一处：新增的确认框没人钉默认按钮——同样必须显式登记。
        Assert.Equal(
            expected.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "×" + p.Value),
            actual.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "×" + p.Value));
        Assert.Equal(6, CallSites().Count);
    }

    // ---------- 调用点：默认按钮 ----------

    [Fact]
    public void EveryConfirmBox_DefaultsToCancel()
    {
        foreach (var site in CallSites())
        {
            // 默认按钮必须是「取消」：回车误触不能把注释写进源码、不能把接口写进 Apifox、不能退出。
            Assert.Contains("MessageBoxDefaultButton.Button2", site.Block);
            Assert.DoesNotContain("MessageBoxDefaultButton.Button1", site.Block);
            Assert.DoesNotContain("MessageBoxDefaultButton.Button3", site.Block);
        }
    }

    [Fact]
    public void EveryConfirmBox_IsAQuestionWithTheWarningIcon()
    {
        foreach (var site in CallSites())
        {
            // 二选一（确定/取消 或 是/否）：不是只有一个「确定」的通知框。
            Assert.True(
                site.Block.Contains("MessageBoxButtons.OKCancel", StringComparison.Ordinal)
                || site.Block.Contains("MessageBoxButtons.YesNo", StringComparison.Ordinal),
                $"{site.File} 的确认框不是二选一。");
            // 不可逆动作一律带警示图标（§1.4 例外条款的那一类）。
            Assert.Contains("MessageBoxIcon.Warning", site.Block);
        }
    }

    // ---------- 调用点：正文取共享口径 ----------

    [Fact]
    public void EveryConfirmBox_TakesItsTextFromTheSharedCaliber()
    {
        foreach (var site in CallSites())
        {
            Assert.True(
                CaliberCalls.Any(call => site.Block.Contains(call, StringComparison.Ordinal)),
                $"{site.File} 的确认框正文没有取自共享口径（就地拼字在 Linux 上无法断言）。");
            // 正文是第一个参数：`MessageBox.Show(this,` 之后紧跟的不能是字符串字面量。
            var afterOwner = site.Block.IndexOf("MessageBox.Show(this,", StringComparison.Ordinal);
            Assert.True(afterOwner >= 0, $"{site.File} 的确认框没有以 this 为所有者。");
            var rest = site.Block[(afterOwner + "MessageBox.Show(this,".Length)..].TrimStart();
            Assert.False(rest.StartsWith('"'), $"{site.File} 的确认框正文是就地拼的字符串字面量。");
        }
    }

    [Fact]
    public void EveryConfirmBox_ContinuesOnlyOnTheAffirmative()
    {
        foreach (var site in CallSites())
        {
            // 只有「确定」/「是」才继续：取消与直接关闭一律不动作。
            Assert.True(
                site.Following.Contains("DialogResult.OK", StringComparison.Ordinal)
                || site.Following.Contains("DialogResult.Yes", StringComparison.Ordinal),
                $"{site.File} 的确认框没有按「确定/是」分支继续。");
        }
    }

    // ---------- 正文：真执行 ----------

    [Fact]
    public void ExitConfirmText_SaysWhatIsRunningHowLongAndWhatHappensNext()
    {
        var text = ConfirmEntries.ExitConfirmText("源码扫描、执行同步", 15);
        // 谁还在跑：作业类型来自运行中的作业本身，不是另算一份。
        Assert.Contains("以下操作仍在执行：源码扫描、执行同步。", text, StringComparison.Ordinal);
        // 等多久：不是「立刻退出」。
        Assert.Contains("最多等待 15 秒让当前步骤结束", text, StringComparison.Ordinal);
        // 没做完的下次怎么办：未完成的操作不会自动重放（方案 §8.6）。
        Assert.Contains("未完成的操作下次打开后请重新核对", text, StringComparison.Ordinal);
        // 问句收尾：这是一个要用户回答的问题，不是通知。
        Assert.EndsWith("确定退出吗？", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExitConfirmText_FormatsTheGraceInvariantly()
    {
        // 换一台机器（区域设置不同）不会把「15 秒」写成「15,0 秒」。
        Assert.Contains("最多等待 15 秒", ConfirmEntries.ExitConfirmText("扫描", 15), StringComparison.Ordinal);
        Assert.Contains("最多等待 5 秒", ConfirmEntries.ExitConfirmText("扫描", 5), StringComparison.Ordinal);
    }

    [Fact]
    public void SyncConfirmText_SaysWhereWhatAndTheLockBoundary()
    {
        var text = ConfirmEntries.SyncConfirmText("12345", "main", 3, 2, 0);
        // 写哪里、写多少：数字来自计划本身。
        Assert.Contains("将写入 Apifox 项目 12345 / 分支 main：3 个接口、2 个模型。", text, StringComparison.Ordinal);
        // 本地锁的边界（方案 §8.4）在界面上的唯一表述，不得删。
        Assert.Contains("本地锁不能阻止其他用户的修改", text, StringComparison.Ordinal);
        Assert.EndsWith("确认同步？", text, StringComparison.Ordinal);
        // 范围外影响为 0 时整句不出现：写「会影响范围外 0 个接口」是一句空话。
        Assert.DoesNotContain("范围外", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SyncConfirmText_ShowsTheOutOfScopeImpactOnlyWhenThereIsOne()
    {
        var text = ConfirmEntries.SyncConfirmText("12345", "release", 12, 0, 7);
        Assert.Contains("共享模型更新会影响范围外 7 个接口的文档表现。", text, StringComparison.Ordinal);
        // 分支名原样带出（主分支由调用方给 "main"，这里不猜）。
        Assert.Contains("/ 分支 release：12 个接口、0 个模型。", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmTexts_FollowTheTypographyCaliber()
    {
        var texts = new[]
        {
            ConfirmEntries.ExitConfirmText("源码扫描", 15),
            ConfirmEntries.SyncConfirmText("12345", "main", 3, 2, 1),
            ConfirmEntries.SyncConfirmTitle,
        };
        foreach (var text in texts)
        {
            foreach (var mark in CopyRules.HalfWidthMarks)
            {
                Assert.True(CopyRules.IndexOfHalfWidthMark(text, mark) < 0, $"「{text}」里的半角「{mark}」紧挨中文。");
            }
            Assert.True(CopyRules.IndexOfStrayColon(text) < 0, $"「{text}」里有半角冒号。");
            Assert.False(text.Contains('“') || text.Contains('”'), $"「{text}」里出现弯引号。");
            foreach (var word in CopyRules.BannedWords.Concat(CopyRules.ProgressNarration))
            {
                Assert.DoesNotContain(word, text, StringComparison.Ordinal);
            }
        }
    }

    // ---------- 正文：一份落点 ----------

    [Fact]
    public void ConfirmTexts_AreNotInlinedInTheDialogs()
    {
        // 字面只在口径文件里出现一次：窗体取函数，不就地拼一份（另写一份必然漂移）。
        foreach (var fragment in new[] { "以下操作仍在执行", "将写入 Apifox 项目", "同步期间请避免", "本地锁不能阻止" })
        {
            Assert.Equal(1, Repo.NativeSources().Count(s => s.Contains(fragment)));
            Assert.Contains(fragment, Repo.Source("Desktop", "Native", "ConfirmEntries.cs"), StringComparison.Ordinal);
        }
        // 标题同理：窗体取常量，不就地拼字。
        // （「确认同步」这四个字在界面上还有第二个正当用处——预览页那个实心主按钮的文案，
        //  故这里不数「字面出现几次」，只钉「确认框的标题取自常量」。）
        Assert.Contains("ConfirmEntries.SyncConfirmTitle", Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs"), StringComparison.Ordinal);
        Assert.Contains("public const string SyncConfirmTitle = \"确认同步\";", Repo.Source("Desktop", "Native", "ConfirmEntries.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ExitConfirm_TakesTheGraceFromTheJobGraceItself()
    {
        // 秒数来自作业宽限本身（JobGrace），不是另写一个魔数——改了宽限，确认框跟着改。
        var source = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        Assert.Contains("ConfirmEntries.ExitConfirmText(kinds, (int)JobGrace.TotalSeconds)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("最多等待", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SyncConfirm_TakesTheCountsFromThePlan()
    {
        var source = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        // 接口/模型数与范围外影响都取自计划本身，不另算一份。
        Assert.Contains(
            "ConfirmEntries.SyncConfirmText(plan.ProjectId, plan.Branch ?? \"main\", endpoints, models, plan.OutOfScopeImpact.Count)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("共享模型更新会影响范围外", source, StringComparison.Ordinal);
    }

    // ---------- 共享编译与设计基线 ----------

    [Fact]
    public void ConfirmEntries_IsSharedCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ConfirmEntries.cs", csproj);
        // 口径文件不依赖 WinForms（否则 Linux 侧共享编译不成立）。
        var caliber = Repo.Source("Desktop", "Native", "ConfirmEntries.cs");
        Assert.DoesNotContain("System.Windows.Forms", caliber, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox", caliber, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_RecordsTheConfirmRule()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**原生确认框的一致性**（2026-11-10 第一百四十六轮）", section);
        Assert.Contains("`Desktop/Native/ConfirmEntries`", section);
        Assert.Contains("`MessageBoxDefaultButton.Button2`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 原生确认框的一致性（§6） \|", ledger);
        Assert.Contains("第一百四十六轮", ledger);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimMoreThanWasDone()
    {
        // 本轮只把两处正文收成一份落点、补机械检查：不得写成改了确认流程或改了写入安全边界。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 原生确认框的一致性（§6） |", "\n以上每项在达成时回写本文并注明证据。");
        Assert.Contains("纯呈现层", row);
        Assert.DoesNotContain("改了确认流程", row, StringComparison.Ordinal);
        Assert.DoesNotContain("改了写入安全边界", row, StringComparison.Ordinal);
    }

    // ---------- 切片工具 ----------

    private sealed record CallSite(string File, string Block, string Following);

    /// <summary>
    /// 全部 <c>MessageBox.Show(</c> 调用点：<see cref="CallSite.Block"/> 是这一次调用本身
    /// （按括号深度界定，不靠缩进猜），<see cref="CallSite.Following"/> 是紧随其后的两条语句
    /// （「只有确定才继续」那一句在这里）。
    /// </summary>
    private static List<CallSite> CallSites()
    {
        var sites = new List<CallSite>();
        foreach (var source in Repo.NativeSources())
        {
            var text = source.Text;
            var index = 0;
            while ((index = text.IndexOf("MessageBox.Show(", index, StringComparison.Ordinal)) >= 0)
            {
                var open = index + "MessageBox.Show".Length;
                var close = MatchingParen(text, open);
                Assert.True(close > open, $"{source.Name} 的 MessageBox.Show 调用没有闭合。");
                var block = text[index..(close + 1)];
                sites.Add(new CallSite(source.Name, block, Following(text, close)));
                index = close + 1;
            }
        }
        return sites;
    }

    private static int MatchingParen(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>调用之后的两条语句（到第二个分号为止）。</summary>
    private static string Following(string text, int close)
    {
        var first = text.IndexOf(';', close);
        if (first < 0) return "";
        var second = text.IndexOf(';', first + 1);
        return second < 0 ? text[(close + 1)..] : text[(close + 1)..(second + 1)];
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
