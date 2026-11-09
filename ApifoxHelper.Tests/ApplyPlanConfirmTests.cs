using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 注释回写的确认框（第一百三十三轮）：钉住「默认委托不漂移」。
/// <para>
/// 存在理由：第一百三十二轮为冒烟器把「确认」收成可替换委托 <c>ApplyPlanDialog.ConfirmApply</c>，
/// 产品路径与测试路径从此共用同一个委托——默认实现就是用户按下「确认回写」之前看到的那个原生确认框。
/// 而那个委托在 Linux 上构造不出来（WinForms 窗体），于是「文案与默认按钮不漂移」这件事此前
/// 没有任何机械检查：有人把默认按钮从「取消」改成「确定」（回车误触即写入源码）、
/// 或把「只修改 /// 注释行，不改执行代码」这句承诺删掉，测试全绿。
/// </para>
/// <para>
/// 本文件分两层钉：① 正文收进 <see cref="ApplyPlanEntries.ConfirmApplyText"/> 后**真执行**断言
/// （共享源码编入测试工程，跑的是主工程同一份代码）；② 默认委托的接线与默认按钮用源码切片钉住
/// （WinForms 控件触达在本环境不可执行，不冒充跑过）。真实运行时证据在
/// <c>ApifoxHelper.Smoke</c>（wine 下构造对话框并核对默认委托的声明类型）。
/// </para>
/// </summary>
public class ApplyPlanConfirmTests
{
    private const string File = "ApplyPlanDialog.cs";

    /// <summary>写入安全边界在界面上的唯一表述：只改注释行、不动执行代码。</summary>
    private const string SafetyPromise = "只修改 /// 注释行，不改执行代码";

    // ---------- 正文：真执行 ----------

    [Fact]
    public void ConfirmText_SaysWhatWillBeWrittenAndWhatWillNot()
    {
        var text = ApplyPlanEntries.ConfirmApplyText(3, 7);
        // 写什么、写多少：数字来自计划本身，不是另算一份。
        Assert.Contains("将把注释改动写入 3 个源码文件，共 7 处注释修改。", text, StringComparison.Ordinal);
        // 不写什么：这条承诺是写入安全边界在界面上的唯一表述。
        Assert.Contains(SafetyPromise, text, StringComparison.Ordinal);
        // 确认后文件被改动即拒绝写入——用户要知道「确认」不等于「无条件覆盖」。
        Assert.Contains("文件在确认后被改动会拒绝写入", text, StringComparison.Ordinal);
        // 问句收尾：这是一个要用户回答的问题，不是通知。
        Assert.EndsWith("确认回写？", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmText_TakesThePlansOwnNumbers()
    {
        // 零与多位数都按计划给的数走：不四舍五入、不省略、不写死。
        Assert.Contains("写入 0 个源码文件，共 0 处注释修改。", ApplyPlanEntries.ConfirmApplyText(0, 0), StringComparison.Ordinal);
        Assert.Contains("写入 12 个源码文件，共 345 处注释修改。", ApplyPlanEntries.ConfirmApplyText(12, 345), StringComparison.Ordinal);
        // 数字按不变文化格式化：换一台机器（区域设置不同）不会变成「1,234」。
        Assert.Contains("共 1234 处注释修改。", ApplyPlanEntries.ConfirmApplyText(1, 1234), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmTitle_IsTheDocumentedOne()
    {
        Assert.Equal("确认注释回写", ApplyPlanEntries.ConfirmApplyTitle);
    }

    // ---------- 正文：一份落点 ----------

    [Fact]
    public void ConfirmText_IsTheSingleSource()
    {
        // 承诺句与「将把注释改动写入」都只在口径文件里出现一次：对话框取函数，不就地拼一份
        // （另写一份必然漂移——改一处、另一处还是旧话）。
        Assert.Equal(1, Repo.NativeSources().Count(s => s.Contains(SafetyPromise)));
        Assert.Equal(1, Repo.NativeSources().Count(s => s.Contains("将把注释改动写入")));
        Assert.Contains(SafetyPromise, Repo.Source("Desktop", "Native", "ApplyPlanEntries.cs"), StringComparison.Ordinal);
        var source = Repo.Source("Desktop", "Native", File);
        Assert.DoesNotContain(SafetyPromise, source, StringComparison.Ordinal);
        Assert.DoesNotContain("将把注释改动写入", source, StringComparison.Ordinal);
        // 标题同理：字面只在口径文件里，对话框取常量。
        Assert.Equal(1, Repo.NativeSources().Count(s => s.Contains("\"确认注释回写\"")));
        Assert.Contains("ApplyPlanEntries.ConfirmApplyTitle", source, StringComparison.Ordinal);
    }

    // ---------- 默认委托：接线 ----------

    [Fact]
    public void DefaultDelegate_IsWiredInTheConstructor()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 委托是可替换的（冒烟器要用），但默认值必须在构造里就装上——
        // 留空的话产品路径点「确认回写」会 NRE，而 Linux 侧测试全绿。
        Assert.Matches(@"(?m)^\s*internal Func<CommentPlan, bool> ConfirmApply \{ get; set; \}$", source);
        var constructor = ConstructorBody(source);
        Assert.Contains("ConfirmApply = plan =>", constructor, StringComparison.Ordinal);
        // 默认实现取共享正文与标题，不就地拼字。
        Assert.Contains("ApplyPlanEntries.ConfirmApplyText(writable.Count, edits)", constructor, StringComparison.Ordinal);
        Assert.Contains("ApplyPlanEntries.ConfirmApplyTitle", constructor, StringComparison.Ordinal);
        // 计数从计划里算：只数允许回写的文件，处数取这些文件的注释处数之和。
        Assert.Contains("plan.Files.Where(f => f.WriteAllowed)", constructor, StringComparison.Ordinal);
        Assert.Contains("writable.Sum(f => f.Edits.Count)", constructor, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultDelegate_DefaultsToCancel()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 默认按钮必须是「取消」：回车误触不能把注释写进源码（与另五处确认框同一口径）。
        Assert.Contains("MessageBoxDefaultButton.Button2", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBoxDefaultButton.Button1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBoxDefaultButton.Button3", source, StringComparison.Ordinal);
        // 确认框是「确定/取消」二选一 + 警告图标：不是 YesNo（YesNo 的默认按钮语义与「取消」不同）。
        Assert.Contains("MessageBoxButtons.OKCancel", source, StringComparison.Ordinal);
        Assert.Contains("MessageBoxIcon.Warning", source, StringComparison.Ordinal);
        // 这一屏只有一个确认框：多一个就是多一处没人钉的默认按钮。
        Assert.Single(Regex.Matches(source, @"MessageBox\.Show\("));
    }

    [Fact]
    public void RunApply_AsksBeforeWriting()
    {
        var source = Repo.Source("Desktop", "Native", File);
        var body = MethodBody(source, "private async Task RunApplyAsync()");
        // 确认在写入之前：拒绝即返回，不触达服务层。
        var ask = body.IndexOf("if (!ConfirmApply(plan)) return;", StringComparison.Ordinal);
        var write = body.IndexOf("_plans.ApplyAsync(", StringComparison.Ordinal);
        Assert.True(ask >= 0, "RunApplyAsync 里没有调用 ConfirmApply。");
        Assert.True(write > ask, "确认必须排在写入之前（先问再写）。");
        // 计划为空或按钮禁用时连问都不问（没有可写的东西，问一句是空话）。
        Assert.Contains("if (plan is null || _busy || !_applyButton.Enabled) return;", body, StringComparison.Ordinal);
    }

    // ---------- 真实运行时：冒烟器核对默认委托 ----------

    [Fact]
    public void SmokeProgram_ChecksTheDefaultDelegateAtRuntime()
    {
        // 源码切片只能钉住「构造里装了委托」这一处形状；「装上的确实是对话框自己的默认实现」
        // 只有真构造一次才知道。冒烟器在 wine 下构造 ApplyPlanDialog 并核对委托的声明类型，
        // 这里钉住那段核对还在（删掉即红）。
        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        Assert.Contains("ConfirmApply", smoke, StringComparison.Ordinal);
        Assert.Contains("Method.DeclaringType", smoke, StringComparison.Ordinal);
        Assert.Contains("typeof(ApplyPlanDialog)", smoke, StringComparison.Ordinal);
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsTheConfirmRule()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**注释回写的确认框**（2026-11-10 第一百三十三轮）", section);
        Assert.Contains("`ApplyPlanEntries.ConfirmApplyText`", section);
        Assert.Contains("`MessageBoxDefaultButton.Button2`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 注释回写确认框的默认委托（§6） \|", ledger);
        Assert.Contains("第一百三十三轮", ledger);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimMoreThanWasDone()
    {
        // 本轮只把正文收成一份落点、补机械检查：不得写成改了回写口径、改了确认流程或改了写入安全边界。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 注释回写确认框的默认委托（§6） |", "\n以上每项在达成时回写本文并注明证据。");
        Assert.Contains("纯呈现层", row);
        Assert.DoesNotContain("改了回写口径", row, StringComparison.Ordinal);
        Assert.DoesNotContain("改了写入安全边界", row, StringComparison.Ordinal);
    }

    // ---------- 切片工具 ----------

    /// <summary>构造函数体（<c>public ApplyPlanDialog(</c> 起的花括号块）。</summary>
    private static string ConstructorBody(string source) =>
        MethodBody(source, "public ApplyPlanDialog(CommentPlanService plans, TaskStore tasks, CheckTask task)");

    private static string MethodBody(string source, string signature)
    {
        var from = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {signature}。");
        var open = source.IndexOf('{', from);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        Assert.Fail($"{signature} 的方法体没有闭合。");
        return "";
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
