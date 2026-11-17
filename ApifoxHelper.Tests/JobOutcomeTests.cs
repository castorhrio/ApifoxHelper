using ApifoxHelper.Desktop.Native;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 作业结束之后状态行该说的那一句（第三百七十八轮，具名 pass：clarify）。
/// <para>
/// <b>补的是哪个缺口</b>：任务页的作业（源码扫描、规则提取、模型生成、传播核对、定向构建、远端检查、
/// 同步执行）跑在后台，进度与结果此前只写在扫描区那一行固定占位的进度标签上。而作业一结束，
/// <c>TasksPage.OnTick</c> 立刻 <c>LoadTasks()</c>，那一次刷新把进度标签<b>藏起来</b>——失败原因因此在屏上
/// 只活了不到一个轮询周期（500ms），随后连同一整行一起消失；成功与取消更是从来没有一句话。
/// 用户按了「开始扫描」、回来只看到进度行没了，屏上没有任何一句话说这一次是成功、失败还是被取消。
/// </para>
/// <para>
/// <b>本文件判什么</b>：
/// ① 文案本身（真执行）：<see cref="JobOutcome.FinishedText"/> 的三种终态各一句、还在跑与认不出的状态
///    一律答 <c>null</c>（不猜、不编一句「已完成」）；
/// ② 色调（真执行）：只有失败是 <c>bad</c>，成功与取消都是 <c>muted</c>——强调色只出现在主行动、焦点与
///    品牌资产，成功不需要强调色，取消更不是故障；
/// ③ 顺序（源码门）：<c>OnTick</c> 里「重读任务」在前、「说结果」在后——反过来的话 <c>LoadTasks</c>
///    会把刚写上去的那一句再盖掉；
/// ④ 唯一落点（源码门）：<c>JobOutcome.FinishedText</c> 在产品源码里恰好调用一处，界面不就地再写一遍真值表；
/// ⑤ 一次作业只报一次（源码门）：欠账由 <c>StartTaskJob</c>（这一页起作业时，第三百八十六轮加）与
///    <c>ShowJobState</c>（界面看到它在跑时）两处记下、由 <c>ShowJobOutcome</c> 用掉即清，报之前先核它属于
///    当前选中的那一个任务。这一条本轮<b>改的是判据本体</b>：此前只认后者，于是一次几十毫秒内就失败的作业
///    两处都没落下、屏上对它一句话都没有（wine 在 120 / 192 两档量到的正是这一处）——是补上没记的那一处，
///    不是把「只报一次」放宽；
/// ⑥ 不覆盖读取失败（源码门）：<c>_loadFailed</c> 时那一句比这一次作业的结果更该留在屏上；
/// ⑦ 状态字面量两侧对账（第三百七十九轮）：<c>JobRunner</c> 写进 <c>Job.State</c> 的终态字面量集合，
///    与 <c>JobOutcome.FinishedText</c> 接受的字面量集合必须一致——两侧各有一个定义不在被它判的对象里，
///    所以这是对账门；另有一条真执行门把作业真的跑一遍（成功 / 失败 / 取消），验「写进去的就是认得的」；
/// ⑧ 色调两侧对账（第三百七十九轮）：<c>JobOutcome.Tone</c> 会答的那两个字，必须正是状态行那一处
///    「色调字 → 设计令牌」认的那两个字，且失败那一支落到 <c>BadText</c>、其余落到 <c>Muted</c>。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「真机上那一行字读起来顺不顺眼」——那要 Windows 真机（G-D5，真机 0 台）；
/// 也不判作业本身跑得对不对（那是各服务自己的门）。
/// </para>
/// </summary>
public class JobOutcomeTests
{
    private static string TasksPage => Repo.Source("Desktop", "Native", "TasksPage.cs");

    private const string ShowJobStateHeader = "private void ShowJobState(JobInfo? job)";
    private const string StartTaskJobHeader = "private void StartTaskJob(";
    private const string OnTickHeader = "private void OnTick()";
    private const string ShowJobOutcomeHeader = "private void ShowJobOutcome()";
    private const string OnSelectedChangedHeader = "private void OnSelectedChanged()";
    private const string FinishedTextHeader = "public static string? FinishedText(";
    private const string ToneHeader = "public static string Tone(string state)";

    /// <summary>作业真的跑在后台的那一份（<c>Infrastructure/JobRunner.cs</c>，与测试工程共享编译）。</summary>
    private static string JobRunner => Repo.Source("Infrastructure", "JobRunner.cs");

    /// <summary>判定那一份（<c>Desktop/Native/JobOutcome.cs</c>，与测试工程共享编译）。</summary>
    private static string JobOutcomeSource => Repo.Source("Desktop", "Native", "JobOutcome.cs");

    /// <summary>
    /// <c>JobRunner</c> 写进 <c>State</c> 的那一处：<c>job.State = "…";</c>。
    /// <para>
    /// 只认带点号的赋值：<c>JobInfo.State</c> 的出厂值写成 <c>public string State { get; set; } = "running";</c>
    /// （没有 <c>.State =</c>），由 <see cref="InitialState"/> 单独取；比较式 <c>j.State == "running"</c> 也不会命中
    /// （<c>==</c> 的第二个字符不是引号）。
    /// </para>
    /// </summary>
    private static readonly Regex WrittenState = new(@"\.State\s*=\s*""(?<state>[^""]*)""", RegexOptions.Compiled);

    /// <summary><c>JobInfo.State</c> 的出厂值（中间态）：<c>public string State { get; set; } = "running";</c>。</summary>
    private static readonly Regex InitialState = new(@"State\s*\{\s*get;\s*set;\s*\}\s*=\s*""(?<state>[^""]*)""", RegexOptions.Compiled);

    /// <summary><c>FinishedText</c> 的 switch 分支：<c>"succeeded" =&gt; …</c>。</summary>
    private static readonly Regex AcceptedState = new(@"""(?<state>[^""]*)""\s*=>", RegexOptions.Compiled);

    /// <summary><c>Tone</c> 的三元式：<c>state == "failed" ? "bad" : "muted"</c>。</summary>
    private static readonly Regex ToneTernary = new(@"==\s*""(?<state>[^""]+)""\s*\?\s*""(?<whenTrue>[^""]+)""\s*:\s*""(?<whenFalse>[^""]+)""", RegexOptions.Compiled);

    /// <summary>状态行那一处「色调字 → 设计令牌」：<c>JobOutcome.Tone(…) == "bad" ? UiTokens.BadText : UiTokens.Muted</c>。</summary>
    private static readonly Regex StatusLineTone = new(@"JobOutcome\.Tone\([^)]*\)\s*==\s*""(?<word>[^""]+)""\s*\?\s*UiTokens\.(?<whenTrue>\w+)\s*:\s*UiTokens\.(?<whenFalse>\w+)", RegexOptions.Compiled);

    /// <summary>
    /// ① 文案本身：三种终态各一句，还在跑与认不出的状态不猜。
    /// <para>
    /// 期望值按<b>理由</b>给：成功说「完成」、失败说「失败：原因」、取消说「已取消」——取消是用户自己的
    /// 动作，结果符合预期，不写成失败（与 <c>AttentionCue</c> 对 canceled 不提醒同一口径）。
    /// </para>
    /// </summary>
    [Fact]
    public void FinishedText_SaysWhatHappened()
    {
        Assert.Equal("源码扫描完成。", JobOutcome.FinishedText("源码扫描", "succeeded", null));
        Assert.Equal("源码扫描失败：磁盘只读。", JobOutcome.FinishedText("源码扫描", "failed", "磁盘只读。"));
        Assert.Equal("源码扫描已取消。", JobOutcome.FinishedText("源码扫描", "canceled", null));

        // 失败但原因没记下来：兜一句「原因未记录。」，不把「失败：」后面留空。
        Assert.Equal("源码扫描失败：原因未记录。", JobOutcome.FinishedText("源码扫描", "failed", null));
        Assert.Equal("源码扫描失败：原因未记录。", JobOutcome.FinishedText("源码扫描", "failed", "   "));

        // 还在跑、或状态认不出：什么都不说（不猜、不编一句「已完成」）。
        Assert.Null(JobOutcome.FinishedText("源码扫描", "running", null));
        Assert.Null(JobOutcome.FinishedText("源码扫描", "", null));
        Assert.Null(JobOutcome.FinishedText("源码扫描", "Succeeded", null));

        // 失败原因原样给出、不补句号：服务层的拒绝文案自带全角句号，脱敏后的异常消息不一定有。
        Assert.Equal("同步执行失败：连接超时", JobOutcome.FinishedText("同步执行", "failed", "连接超时"));
    }

    /// <summary>
    /// ② 色调：只有失败是 bad；成功与取消都是 muted（颜色只作辅助信号，含义由文字本身承载）。
    /// </summary>
    [Fact]
    public void Tone_OnlyFailureIsBad()
    {
        Assert.Equal("bad", JobOutcome.Tone("failed"));
        Assert.Equal("muted", JobOutcome.Tone("succeeded"));
        Assert.Equal("muted", JobOutcome.Tone("canceled"));
        Assert.Equal("muted", JobOutcome.Tone("running"));
    }

    /// <summary>
    /// ③④⑤⑥ 仓里当前那一份：逐条对账，把每一处「形状被改回去了」说出来（空 = 对得上）。
    /// </summary>
    [Fact]
    public void TasksPage_ReportsTheOutcomeOnTheStatusLine()
    {
        Assert.Empty(Problems(TasksPage));
    }

    /// <summary>
    /// ⑦ 状态字面量两侧对账（第三百七十九轮，简报 P0-1）：<c>JobRunner</c> 写进 <c>State</c> 的终态字面量集合，
    /// 与 <c>JobOutcome.FinishedText</c> 接受的字面量集合必须一致。
    /// <para>
    /// <b>为什么要有这一条</b>：<c>FinishedText</c> 只认 <c>"succeeded"</c> / <c>"failed"</c> / <c>"canceled"</c>
    /// 三个小写字面量，<c>"Succeeded"</c> 一律答 <c>null</c>——这是刻意的严格口径，但此前<b>没有任何一条门</b>
    /// 保证 <c>JobRunner</c> 写进去的就是这三个。若它某处写了大写、或将来新增一个中间态字面量，
    /// 用户会看到「按了按钮什么都没说」，正是第三百七十八轮要修的那个缺口的反面。
    /// </para>
    /// <para>
    /// <b>五步法（第二百八十二轮）第 ② 步答「不是」</b>：断言里那两个口径名——<c>JobRunner</c> 写什么、
    /// <c>FinishedText</c> 认什么——各有一个定义在对方之外，故这是<b>对账门</b>，不属「假门防护」那一族。
    /// </para>
    /// </summary>
    [Fact]
    public void JobRunnerStates_AreExactlyTheOnesFinishedTextAccepts()
    {
        Assert.Empty(StateProblems(JobRunner, JobOutcomeSource));
    }

    /// <summary>
    /// ⑦ 的另一半：把作业<b>真的跑一遍</b>（成功 / 失败 / 取消），验「写进去的就是认得的」。
    /// <para>
    /// 上面那条是源码对账，它抓得住「字面量集合对不上」，抓不住「某个分支其实没走到」；这一条反过来，
    /// 走的是真的 <c>JobRunner</c>（与测试工程共享编译，Linux 上真执行）：起三个作业、等它们到终态，
    /// 把 <c>State</c> 与 <c>Error</c> 原样喂给 <see cref="JobOutcome.FinishedText"/>，三种终态都必须答得出那一句。
    /// 失败那一支还要验原因真的从作业里带到了那一句上（<c>JobRunner</c> 的失败分支一定写 <c>Error</c>）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判作业本身做对了什么（那是各服务自己的门），也不判真机上那一行字的样子（G-D5）。
    /// </para>
    /// </summary>
    [Fact]
    public async Task JobRunner_ReallyWritesTheStatesFinishedTextAccepts()
    {
        var runner = new JobRunner(NullLogger<JobRunner>.Instance);

        var succeeded = runner.Start("t-ok", "源码扫描", _ => Task.FromResult<object?>(null));
        await succeeded.Completion!;

        var failed = runner.Start("t-bad", "源码扫描", _ => throw new UserException("磁盘只读。", 500));
        await failed.Completion!;

        var canceled = runner.Start("t-cancel", "源码扫描", async context =>
        {
            await Task.Delay(Timeout.Infinite, context.Token);
            return null;
        });
        runner.Cancel("t-cancel");
        await canceled.Completion!;

        // 三个终态各是哪一个字面量：与 FinishedText 接受的那三个逐字相同。
        Assert.Equal("succeeded", succeeded.State);
        Assert.Equal("failed", failed.State);
        Assert.Equal("canceled", canceled.State);

        // 三种终态各说一句（不猜、不留空）。
        Assert.Equal("源码扫描完成。", JobOutcome.FinishedText(succeeded.Kind, succeeded.State, succeeded.Error));
        Assert.Equal("源码扫描已取消。", JobOutcome.FinishedText(canceled.Kind, canceled.State, canceled.Error));

        // 失败那一句把作业记下的原因原样带出来——这是「作业 → 状态行」这条链真的接通了的那一半。
        var failureText = JobOutcome.FinishedText(failed.Kind, failed.State, failed.Error);
        Assert.NotNull(failureText);
        Assert.Contains("磁盘只读。", failureText, StringComparison.Ordinal);

        // 还在跑的时候（出厂值）什么都不说：进度行才是「正在做什么」的通道。
        var running = runner.Start("t-running", "源码扫描", async context =>
        {
            await Task.Delay(Timeout.Infinite, context.Token);
            return null;
        });
        Assert.Null(JobOutcome.FinishedText(running.Kind, running.State, running.Error));
        runner.Cancel("t-running");
        await running.Completion!;
    }

    /// <summary>
    /// ⑧ 色调两侧对账（第三百七十九轮，简报 P0-2）：<c>JobOutcome.Tone</c> 会答的那两个字，
    /// 必须正是状态行那一处「色调字 → 设计令牌」认的那两个字。
    /// <para>
    /// <b>为什么要有这一条</b>：<c>Tone</c> 答的是语义字（<c>bad</c> / <c>muted</c>），而状态行那一处
    /// 拿它跟一个字面量比、再选一个设计令牌。此前<b>没有一条门</b>保证这两个字对得上——<c>Tone</c> 若改成答
    /// <c>warn</c>，状态行会静默落到 <c>muted</c> 那一支，失败那一句看起来像「没事」，而所有门照旧全绿。
    /// 注释里那句「颜色只作辅助信号、含义由文字承载」不是判据，这一条才是。
    /// </para>
    /// <para>
    /// <b>五步法第 ② 步答「不是」</b>：<c>Tone</c> 的定义在 <c>JobOutcome.cs</c>、状态行那一处的定义在
    /// <c>TasksPage.cs</c>，两侧各在对方之外，故这是<b>对账门</b>。
    /// </para>
    /// </summary>
    [Fact]
    public void Tone_IsTheWordTheStatusLineRecognizes()
    {
        Assert.Empty(ToneProblems(JobOutcomeSource, TasksPage));
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：八发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// ① 把「说结果」那一句从 <c>OnTick</c> 里删掉（进度行照旧收起，结果没人说——本门要补的正是这个缺口）；
    /// ② 把「说结果」挪到「重读任务」<b>之前</b>（顺序反了，<c>LoadTasks</c> 会把刚写上去的那一句盖掉）；
    /// ③ 把「用掉即清」那一行删掉（同一句会在每一次轮询里重复念）；
    /// ④ 把「不覆盖读取失败」那一行删掉（磁盘读不出来时，状态行会被这一次作业的结果顶掉）；
    /// ⑤ 起作业时不登记（第三百八十六轮补的那一格没了：几十毫秒内就失败的那一次，屏上又一句话都没有）；
    /// ⑥ 触发退回「界面看到过它在跑」那个 flag（同一发的另一种写法：登记在、触发不看它）；
    /// ⑦ 欠账所属任务的核对删掉（换过任务之后，上一个任务的旧结果被念到当前这个任务头上）；
    /// ⑧ 把抹欠账搬回换任务那一处（<c>LoadTasks</c> 重建列表时也走那里，刚要报的那一句被一起抹掉——
    ///    这一发是第三百八十六轮写这一片时<b>真的踩过</b>的形状，留成注入，不让它靠「看起来更严」回来）。
    /// </para>
    /// <para>
    /// 注入的输入是<b>去注释后的代码</b>（<see cref="CodeScan.Text"/>）：判据读的是那一层，注入必须落在同一层，
    /// 否则命中的若是注释里那一句，原文变了、代码一个字没动，那一发就白喂了。
    /// </para>
    /// </summary>
    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        // 合规写法：仓里这一份一处都不报。
        Assert.Empty(Problems(TasksPage));

        var code = CodeScan.Text(TasksPage);

        // ① 删掉「说结果」：进度行照旧收起，这一次是成功、失败还是被取消，屏上一句话都没有。
        var silent = Inject(code,
            "        LoadTasks();\n        ShowJobOutcome();\n",
            "        LoadTasks();\n");
        Assert.Contains(Problems(silent), p => p.Contains("ShowJobOutcome", StringComparison.Ordinal));

        // ② 顺序反了：先说结果、再重读任务——LoadTasks 会把刚写上去的那一句盖掉。
        var reordered = Inject(code,
            "        LoadTasks();\n        ShowJobOutcome();\n",
            "        ShowJobOutcome();\n        LoadTasks();\n");
        Assert.Contains(Problems(reordered), p => p.Contains("重读任务", StringComparison.Ordinal));

        // ③ 用掉即清那一行没了：同一句会在每一次轮询里重复念。
        var repeated = Inject(code, "        _lastJobId = null;\n", "");
        Assert.Contains(Problems(repeated), p => p.Contains("_lastJobId", StringComparison.Ordinal));

        // ④ 不覆盖读取失败那一行没了：磁盘读不出来时，状态行会被这一次作业的结果顶掉。
        var overwritten = Inject(code, "        if (_loadFailed) return;\n", "");
        Assert.Contains(Problems(overwritten), p => p.Contains("_loadFailed", StringComparison.Ordinal));

        // ⑤ 起作业时不登记：一次几十毫秒内就失败的作业，界面第一次看到它之前已经到终态，
        //    两处都没落下 → 那一句永远说不出口（第三百八十五轮在 120 / 192 两档量到的正是这一发）。
        var notRemembered = Inject(code,
            "        RememberOutcome(_jobs.Start(taskId, kind, work));\n",
            "        _jobs.Start(taskId, kind, work);\n");
        Assert.Contains(Problems(notRemembered), p => p.Contains("StartTaskJob", StringComparison.Ordinal));

        // ⑥ 触发退回「界面看到过它在跑」那个 flag：那个 flag 只在看到它跑时立起，快失败的那一次又沉默。
        var backToFlag = Inject(code,
            "        if (_lastJobId is null) return;\n",
            "        if (!_runningShown) return;\n");
        Assert.Contains(Problems(backToFlag), p => p.Contains("flag", StringComparison.Ordinal));

        // ⑦ 欠账所属任务的核对没了：换过任务之后，上一个任务的旧结果被念到当前这个任务头上。
        var unbound = Inject(code, "        if (owedByTask != _selectedTask?.Id) return;\n", "");
        Assert.Contains(Problems(unbound), p => p.Contains("_selectedTask?.Id", StringComparison.Ordinal));

        // ⑧ 把抹欠账搬回换任务那一处：LoadTasks 重建列表时也走那里，刚要报的那一句被一起抹掉。
        var wipedOnSelect = Inject(code,
            "        _selectedId = _selectedTask?.Id;\n",
            "        _selectedId = _selectedTask?.Id;\n        _lastJobId = null;\n");
        Assert.Contains(Problems(wipedOnSelect), p => p.Contains("OnSelectedChanged", StringComparison.Ordinal));
    }

    /// <summary>
    /// ⑦⑧ 那两条对账门<b>自己</b>被抓得住：五发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// ① <c>JobRunner</c> 把成功写成大写（<c>FinishedText</c> 只认小写三个字面量，作业结束之后屏上一句话都没有）；
    /// ② <c>JobOutcome</c> 多认一个字面量（<c>JobRunner</c> 从不写它：那一支永远走不到，而真写进去的那一个没人认）；
    /// ③ <c>Tone</c> 把失败答成 <c>muted</c>（状态行会把失败那一句染成次要文字色，看起来像「没事」）；
    /// ④ 状态行比的是另一个字（<c>Tone</c> 答 <c>bad</c>、状态行比 <c>warn</c>，失败落到 <c>muted</c> 那一支）；
    /// ⑤ 状态行把 <c>bad</c> 映射到次要文字色（失败那一句不再有警示色）。
    /// </para>
    /// </summary>
    [Fact]
    public void StateAndToneGates_ReportTheBreaksTheyMustCatch()
    {
        // 合规写法：仓里这两份一处都不报。
        Assert.Empty(StateProblems(JobRunner, JobOutcomeSource));
        Assert.Empty(ToneProblems(JobOutcomeSource, TasksPage));

        var runner = CodeScan.Text(JobRunner);
        var outcome = CodeScan.Text(JobOutcomeSource);
        var page = CodeScan.Text(TasksPage);

        // ① 大写：JobRunner 写 "Succeeded"，FinishedText 只认 "succeeded"。
        var capitalized = Inject(runner, "job.State = \"succeeded\";", "job.State = \"Succeeded\";");
        Assert.Contains(StateProblems(capitalized, outcome), p => p.Contains("Succeeded", StringComparison.Ordinal));

        // ② 多认一个：JobOutcome 认 "cancelled"，而 JobRunner 写的是 "canceled"。
        var extra = Inject(outcome, "\"canceled\" => $\"{kind}已取消。\",", "\"cancelled\" => $\"{kind}已取消。\",");
        Assert.Contains(StateProblems(runner, extra), p => p.Contains("cancelled", StringComparison.Ordinal));

        // ③ 失败答成 muted：状态行会把失败那一句染成次要文字色。
        var mutedFailure = Inject(outcome, "? \"bad\" : \"muted\"", "? \"muted\" : \"muted\"");
        Assert.Contains(ToneProblems(mutedFailure, page), p => p.Contains("muted", StringComparison.Ordinal));

        // ④ 状态行比的是另一个字：Tone 答 bad、状态行比 warn，失败落到 muted 那一支。
        var otherWord = Inject(page, "JobOutcome.Tone(job.State) == \"bad\"", "JobOutcome.Tone(job.State) == \"warn\"");
        Assert.Contains(ToneProblems(outcome, otherWord), p => p.Contains("warn", StringComparison.Ordinal));

        // ⑤ 状态行把 bad 映射到次要文字色：失败那一句不再有警示色。
        var wrongToken = Inject(page, "? UiTokens.BadText : UiTokens.Muted", "? UiTokens.Muted : UiTokens.Muted");
        Assert.Contains(ToneProblems(outcome, wrongToken), p => p.Contains("BadText", StringComparison.Ordinal));
    }

    /// <summary>
    /// 把 <paramref name="from"/> 换成 <paramref name="to"/>，并当场核「差异落在<b>代码区</b>」。
    /// <para>
    /// 与 <c>BannerFocusPlanTests.Inject</c> 同一形状：注入的输入本身就是去注释后的代码，这一道再当场核一次
    /// 「去注释之后仍然不同」——命中的若是注释里那一句，这一发就是白喂的。
    /// </para>
    /// </summary>
    private static string Inject(string code, string from, string to)
    {
        var injected = code.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(code, injected);
        Assert.NotEqual(CodeScan.Text(code), CodeScan.Text(injected));
        return injected;
    }

    /// <summary>
    /// ⑦ 的对账：<c>JobRunner</c> 写进 <c>State</c> 的终态字面量集合 ↔ <c>JobOutcome.FinishedText</c>
    /// 接受的字面量集合（空 = 对得上）。
    /// <para>
    /// 写成纯文本进、问题出的函数，是为了让上面那条自测能拿<b>注入过的</b>源码喂它——与
    /// <c>BannerFocusPlanTests.Problems</c>、<c>DetailFocusCoordinationTests.Problems</c> 同一形状。
    /// </para>
    /// <para>
    /// <b>取法上的两条假定（登记在此，不为它加门）</b>：① <c>JobRunner</c> 写 <c>State</c> 的写法是
    /// <c>job.State = "…";</c> 这一种，改成经常量或方法写就要跟着改 <see cref="WrittenState"/>；
    /// ② <c>FinishedText</c> 的接受集合写成 switch 分支 <c>"…" =&gt; …</c>，改成字典或别的形状同理。
    /// 两条假定一旦不成立，本门会报「一处都没读到」而不是静默绿。
    /// </para>
    /// </summary>
    private static List<string> StateProblems(string jobRunner, string jobOutcome)
    {
        var problems = new List<string>();
        var runner = CodeScan.Text(jobRunner);
        var outcome = CodeScan.Text(jobOutcome);

        var written = WrittenState.Matches(runner)
            .Select(m => m.Groups["state"].Value)
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        if (written.Count == 0)
        {
            problems.Add("JobRunner 里一处 `State = \"…\"` 都没读到：本门按这一形状取「作业真的写进哪几个终态字面量」，"
                + "改成经常量或方法写之后这一条要跟着改取法。");
            return problems;
        }

        var finishedText = Body(outcome, FinishedTextHeader);
        if (finishedText is null)
        {
            problems.Add("JobOutcome 里找不到 FinishedText 的方法体：本门按它取「接受哪几个终态字面量」，"
                + "方法改名或搬走之后这一条要跟着改取法。");
            return problems;
        }

        var accepted = AcceptedState.Matches(finishedText)
            .Select(m => m.Groups["state"].Value)
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        if (accepted.Count == 0)
        {
            problems.Add("JobOutcome.FinishedText 里一个 switch 分支字面量都没读到：本门按 `\"…\" => …` 这一形状取"
                + "「接受哪几个终态字面量」，改成字典或别的形状之后这一条要跟着改取法。");
            return problems;
        }

        if (!written.SequenceEqual(accepted, StringComparer.Ordinal))
        {
            problems.Add($"JobRunner 写进 State 的终态字面量是 [{string.Join(", ", written)}]，"
                + $"而 JobOutcome.FinishedText 接受的是 [{string.Join(", ", accepted)}]：两边对不上时，"
                + "作业结束之后状态行会对某一种终态答 null——用户按了按钮，屏上一句话都没有。");
        }

        // 真执行那一半：写进去的每一个字面量，FinishedText 都必须答得出那一句。
        foreach (var state in written.Where(s => JobOutcome.FinishedText("源码扫描", s, null) is null))
        {
            problems.Add($"JobRunner 会写 State = \"{state}\"，而 JobOutcome.FinishedText 对它答 null："
                + "这一种终态结束之后，状态行不会说任何一句话。");
        }

        // 中间态：JobInfo.State 的出厂值必须答 null（还在跑的时候不该说结果）。
        var initial = InitialState.Match(runner);
        if (!initial.Success)
        {
            problems.Add("JobRunner 里读不到 JobInfo.State 的出厂值（应为 `public string State { get; set; } = \"running\";`）："
                + "本门按它判「中间态答不答 null」，那一行改了形状之后这一条要跟着改取法。");
        }
        else if (JobOutcome.FinishedText("源码扫描", initial.Groups["state"].Value, null) is not null)
        {
            problems.Add($"JobInfo.State 的出厂值是 \"{initial.Groups["state"].Value}\"，而 JobOutcome.FinishedText 对它答了一句结果："
                + "还在跑的作业不该在状态行上说「完成」。");
        }

        return problems;
    }

    /// <summary>
    /// ⑧ 的对账：<c>JobOutcome.Tone</c> 会答的那两个字 ↔ 状态行那一处「色调字 → 设计令牌」认的那两个字
    /// （空 = 对得上）。
    /// <para>
    /// <b>取法上的两条假定（登记在此，不为它加门）</b>：① <c>Tone</c> 写成三元式
    /// <c>state == "…" ? "…" : "…"</c>；② 状态行那一处写成
    /// <c>JobOutcome.Tone(…) == "…" ? UiTokens.X : UiTokens.Y</c>。两条一旦不成立，本门会报「读不到」而不是静默绿。
    /// </para>
    /// </summary>
    private static List<string> ToneProblems(string jobOutcome, string tasksPage)
    {
        var problems = new List<string>();
        var outcome = CodeScan.Text(jobOutcome);
        var page = CodeScan.Text(tasksPage);

        var tone = Expression(outcome, ToneHeader);
        if (tone is null)
        {
            problems.Add("JobOutcome 里找不到 Tone 的表达式体：本门按它取「哪一个状态答哪一个色调」，"
                + "方法改名或搬走之后这一条要跟着改取法。");
            return problems;
        }

        var ternary = ToneTernary.Match(tone);
        if (!ternary.Success)
        {
            problems.Add("JobOutcome.Tone 不再是「状态 == \"…\" ? \"…\" : \"…\"」这一形状：本门按这一形状取"
                + "「哪一个状态答哪一个色调」，改成别的写法之后这一条要跟着改取法。");
            return problems;
        }

        var failedState = ternary.Groups["state"].Value;
        var failureTone = ternary.Groups["whenTrue"].Value;
        var otherTone = ternary.Groups["whenFalse"].Value;

        var showJobOutcome = Body(page, ShowJobOutcomeHeader);
        if (showJobOutcome is null)
        {
            problems.Add("TasksPage 里找不到 ShowJobOutcome 的方法体：状态行那一处「色调字 → 设计令牌」没有落点。");
            return problems;
        }

        var statusLine = StatusLineTone.Match(showJobOutcome);
        if (!statusLine.Success)
        {
            problems.Add("ShowJobOutcome 里读不到「色调字 → 设计令牌」那一处（应为 "
                + "`JobOutcome.Tone(…) == \"bad\" ? UiTokens.BadText : UiTokens.Muted`）：本门按这一处判"
                + "「状态行认不认 JobOutcome 答的那两个字」，改成别的写法之后这一条要跟着改取法。");
            return problems;
        }

        var comparedWord = statusLine.Groups["word"].Value;
        var failureToken = statusLine.Groups["whenTrue"].Value;
        var otherToken = statusLine.Groups["whenFalse"].Value;

        if (failedState != "failed")
        {
            problems.Add($"JobOutcome.Tone 拿「{failedState}」当失败那一支：作业的失败终态字面量是 \"failed\""
                + "（JobRunner 写的就是它），对不上时失败会被念成 muted。");
        }

        if (failureTone != comparedWord)
        {
            problems.Add($"JobOutcome.Tone 对失败答「{failureTone}」，而状态行比的是「{comparedWord}」："
                + "两个字对不上时失败那一句会落到 muted 那一支，屏上看起来像「没事」。");
        }

        if (otherTone == comparedWord)
        {
            problems.Add($"JobOutcome.Tone 对成功与取消也答「{otherTone}」——与失败同一个字："
                + "状态行会把成功与取消也染成警示色。");
        }

        if (otherTone != "muted")
        {
            problems.Add($"JobOutcome.Tone 对成功与取消答「{otherTone}」而不是「muted」："
                + "成功不需要强调色、取消不是故障（设计验收 D3 双通道：颜色只作辅助信号）。");
        }

        if (failureToken != "BadText")
        {
            problems.Add($"状态行把「{comparedWord}」映射到 UiTokens.{failureToken} 而不是 UiTokens.BadText："
                + "失败那一句该用警示色。");
        }

        if (otherToken != "Muted")
        {
            problems.Add($"状态行的另一支映射到 UiTokens.{otherToken} 而不是 UiTokens.Muted："
                + "成功与取消该用次要文字色。");
        }

        return problems;
    }

    /// <summary>
    /// 把一份源码喂给判据，返回「哪几处对不上」的说明（空 = 全对得上）。
    /// <para>
    /// 写成纯文本进、问题出的函数，是为了让上面那条自测能拿<b>注入过的</b>源码喂它——与
    /// <c>BannerFocusPlanTests.Problems</c>、<c>DetailFocusCoordinationTests.Problems</c> 同一形状。
    /// </para>
    /// </summary>
    private static List<string> Problems(string tasksPage)
    {
        var problems = new List<string>();
        var code = CodeScan.Text(tasksPage);

        // ⑤ 欠账记在两处（第三百八十六轮改准）：这一页起作业那一处当场记下、界面看到它在跑那一处也记下。
        //    此前只记后者——一次几十毫秒内就失败的作业，RefreshDetail 那一次 Running 已经答 null，
        //    两处都没落下，屏上对它一句话都没有（第三百八十五轮在 120 / 192 两档量到的正是这一处）。
        var showJobState = Body(code, ShowJobStateHeader);
        if (showJobState is null)
        {
            problems.Add("TasksPage 里找不到 ShowJobState 的方法体：本门按它判「作业 Id 记在哪一处」，"
                + "方法改名或搬走之后这一条要跟着改取法。");
        }
        else if (!showJobState.Contains("RememberOutcome(job);", StringComparison.Ordinal))
        {
            problems.Add("ShowJobState 里没有记下这一次作业（应为 RememberOutcome(job);）：不记就只能在作业结束后"
                + "去问 Running，而它那时已经答 null——终态与失败原因都读不回来。");
        }

        // ⑤b 起作业时当场记下（第三百八十六轮补的那一格），且作业启动点只许有一处。
        var startTaskJob = Body(code, StartTaskJobHeader);
        if (startTaskJob is null)
        {
            problems.Add("TasksPage 里找不到 StartTaskJob 的方法体：这一页起作业没收在一处，"
                + "「起作业时当场记下欠账」这一判就没有对象，快失败的那一次会在没收进来的那一个启动点上又是沉默。");
        }
        else if (!startTaskJob.Contains("RememberOutcome(_jobs.Start(", StringComparison.Ordinal))
        {
            problems.Add("StartTaskJob 没有把 _jobs.Start 的返回值当场记下（应为 RememberOutcome(_jobs.Start(...))）："
                + "作业几十毫秒内就失败时，界面第一次看到它之前它已经到终态，起作业时不记，"
                + "OnTick 后半支永远走不到——用户按了「开始扫描」，屏上对这一次失败一句话都没有。");
        }

        var directStarts = Count(code, "_jobs.Start(");
        if (directStarts != 1)
        {
            problems.Add($"TasksPage 里直接调用 _jobs.Start( 出现 {directStarts} 处（应为 1 处，只在 StartTaskJob 里）："
                + "多出来的每一处都不记欠账，那一次作业结束之后屏上就是沉默（第三百八十五轮 wine 红话量的正是这个形状）。");
        }

        // ③ 顺序：OnTick 里「重读任务」在前、「说结果」在后。
        var onTick = Body(code, OnTickHeader);
        if (onTick is null)
        {
            problems.Add("TasksPage 里找不到 OnTick 的方法体：本门按它判「重读任务在前、说结果在后」，"
                + "方法改名或搬走之后这一条要跟着改取法。");
        }
        else
        {
            var reload = onTick.IndexOf("LoadTasks();", StringComparison.Ordinal);
            var report = onTick.IndexOf("ShowJobOutcome();", StringComparison.Ordinal);
            if (report < 0)
            {
                problems.Add("OnTick 里没有调用 ShowJobOutcome()：作业结束之后进度行照旧收起，"
                    + "这一次是成功、失败还是被取消，屏上一句话都没有——本门要补的正是这个缺口。");
            }
            else if (reload < 0)
            {
                problems.Add("OnTick 里找不到 LoadTasks()：本门按「重读任务在前、说结果在后」判顺序，"
                    + "那一行没了这一判就没有对象。");
            }
            else if (report < reload)
            {
                problems.Add("OnTick 里「说结果」排在「重读任务」之前：LoadTasks 会重建列表与状态行，"
                    + "刚写上去的那一句当场被盖掉。重读任务必须在说结果之前。");
            }
            else if (onTick.Contains("_runningShown", StringComparison.Ordinal))
            {
                problems.Add("OnTick 又按「界面看到过它在跑」那样的 flag 触发：那种 flag 只在看到它跑的那一次立起，"
                    + "到终态比第一次看到它还早的作业因此永远说不出口。触发只看「还欠着一次结果没报」。");
            }
            else if (!onTick.Contains("_lastJobId", StringComparison.Ordinal))
            {
                problems.Add("OnTick 里没有按欠账（_lastJobId）决定要不要说结果：本门判「还欠着一次结果就说」，"
                    + "那一判没了这一条就没有对象。");
            }
        }

        // ④⑤⑥ 说结果那一处：读回终态、用掉即清、不覆盖读取失败、色调只由判定给。
        var showJobOutcome = Body(code, ShowJobOutcomeHeader);
        if (showJobOutcome is null)
        {
            problems.Add("TasksPage 里找不到 ShowJobOutcome 的方法体：注册了却没有落点，这一句等于没说。");
        }
        else
        {
            if (!showJobOutcome.Contains("_jobs.Get(id)", StringComparison.Ordinal))
            {
                problems.Add("ShowJobOutcome 里没有按 Id 读回作业（应为 _jobs.Get(id)）：作业结束后 Running 答 null，"
                    + "不按 Id 取就读不到终态与失败原因。");
            }

            var cleared = showJobOutcome.IndexOf("_lastJobId = null;", StringComparison.Ordinal);
            var written = showJobOutcome.IndexOf("SetStatus(", StringComparison.Ordinal);
            if (cleared < 0)
            {
                problems.Add("ShowJobOutcome 里没有把 _lastJobId 用掉即清（应为 _lastJobId = null;）："
                    + "不清的话同一句会在每一次轮询里重复念，状态行永远停在上一件事上。");
            }
            else if (written >= 0 && cleared > written)
            {
                problems.Add("ShowJobOutcome 里「用掉即清」排在写状态行之后：中途 return 的那几支会把 Id 留着，"
                    + "下一次轮询再念一遍同一件事。清必须在写之前。");
            }

            if (!showJobOutcome.Contains("_loadFailed", StringComparison.Ordinal))
            {
                problems.Add("ShowJobOutcome 里没有判 _loadFailed：磁盘上的任务读不出来时，状态行已经在说那件事的原因，"
                    + "那一句比这一次作业的结果更该留在屏上（屏上此刻连任务清单都没有）。");
            }

            if (!showJobOutcome.Contains("_lastJobTaskId", StringComparison.Ordinal)
                || !showJobOutcome.Contains("_selectedTask?.Id", StringComparison.Ordinal))
            {
                problems.Add("ShowJobOutcome 里没有把这一笔欠账与当前选中的任务比在一起（要同时见到 _lastJobTaskId "
                    + "与 _selectedTask?.Id）：换过任务之后，上一个任务的旧结果会被念到当前这个任务头上"
                    + "——状态行说的是「当前这个任务刚才那件事怎么样了」。");
            }

            if (!showJobOutcome.Contains("JobOutcome.FinishedText(", StringComparison.Ordinal))
            {
                problems.Add("ShowJobOutcome 里没有用 JobOutcome.FinishedText 取那一句："
                    + "就地再写一遍真值表，两处口径迟早对不上。");
            }

            if (!showJobOutcome.Contains("JobOutcome.Tone(", StringComparison.Ordinal))
            {
                problems.Add("ShowJobOutcome 里没有用 JobOutcome.Tone 取色调："
                    + "就地判「是不是失败」会让「取消不是故障」这条口径散成两处。");
            }
        }

        // ④ 唯一落点：文案与色调的判定在产品源码里各恰好调用一处。
        foreach (var (needle, why) in new[]
                 {
                     ("JobOutcome.FinishedText(", "「该说哪一句」的判定"),
                     ("JobOutcome.Tone(", "「用哪一种色调」的判定"),
                 })
        {
            var total = Count(code, needle);
            if (total != 1)
            {
                problems.Add($"产品源码里调用 {needle} 出现 {total} 处（应为 1 处，在 ShowJobOutcome 里）："
                    + $"{why}只该有一处，第二处就是第二个说了算的人。");
            }
        }

        // 换任务那一处不许抹欠账（第三百八十六轮踩过的那一发）：LoadTasks 重建列表时 Items.Clear 也会走
        // OnSelectedChanged，在那里抹掉就把 OnTick 刚要报的那一句一起抹了——「换任务不念旧结果」
        // 由上面那一判（ShowJobOutcome 按 _lastJobTaskId 核）承担。
        var onSelectedChanged = Body(code, OnSelectedChangedHeader);
        if (onSelectedChanged is not null
            && onSelectedChanged.Contains("_lastJobId = null;", StringComparison.Ordinal))
        {
            problems.Add("OnSelectedChanged 里抹了欠账（_lastJobId = null;）：作业结束那一次刷新走的是 "
                + "OnTick → LoadTasks → ShowJobOutcome，而 LoadTasks 清空列表时也会触发选中事件走到这里，"
                + "抹掉之后那一句永远说不出口。要挡住「对着另一个任务念旧结果」，请在 ShowJobOutcome 里按所属任务核。");
        }

        return problems;
    }

    /// <summary>
    /// 从去注释后的源码里取一个方法体（从签名那一行到与之配对的右花括号）。
    /// <para>
    /// 按花括号配对数取，不按「下一个 <c>private</c>」取：方法体里有 <c>if</c> / <c>foreach</c> 的花括号，
    /// 按行首关键字切会把方法体截短，而截短之后「里面没有某一行」会假绿。
    /// </para>
    /// <para>
    /// <b>假定（第三百七十九轮按简报 P1-1 登记，取「只登记」那一支）</b>：本方法按花括号配对数取，
    /// <b>不跳过字符串字面量里的 <c>{</c> / <c>}</c></b>。当前被取的三处方法体（<c>ShowJobState</c> /
    /// <c>OnTick</c> / <c>ShowJobOutcome</c>）与 <c>FinishedText</c> 的 switch 块里，字面量中的花括号都是成对的
    /// （<c>$"{kind}完成。"</c> 这种），配对结果不受影响。将来若在方法体里写进<b>不成对</b>的花括号字面量
    /// （例如 <c>"{"</c>），本方法会取错边界——那时要改成跳过字符串字面量，或改用 Roslyn 解析。
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
    /// 从去注释后的源码里取一个<b>表达式体</b>成员（从签名那一行到它自己的分号）。
    /// <para>
    /// <c>Tone</c> 是 <c>=&gt; …;</c> 这一种，没有花括号，<see cref="Body"/> 取不到它（它会一路找到下一个
    /// 方法的花括号）。故单列一处：签名之后第一个分号就是它的结尾。
    /// </para>
    /// </summary>
    private static string? Expression(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return null;
        var end = code.IndexOf(';', start);
        return end < 0 ? null : code[start..(end + 1)];
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}
