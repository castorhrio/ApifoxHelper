using ApifoxHelper.Desktop.Native;
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
/// ⑤ 一次作业只报一次（源码门）：作业 Id 由 <c>ShowJobState</c> 记下、由 <c>ShowJobOutcome</c> 用掉即清；
/// ⑥ 不覆盖读取失败（源码门）：<c>_loadFailed</c> 时那一句比这一次作业的结果更该留在屏上。
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
    private const string OnTickHeader = "private void OnTick()";
    private const string ShowJobOutcomeHeader = "private void ShowJobOutcome()";

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
    /// 上面那段判据<b>自己</b>被抓得住：四发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// ① 把「说结果」那一句从 <c>OnTick</c> 里删掉（进度行照旧收起，结果没人说——本门要补的正是这个缺口）；
    /// ② 把「说结果」挪到「重读任务」<b>之前</b>（顺序反了，<c>LoadTasks</c> 会把刚写上去的那一句盖掉）；
    /// ③ 把「用掉即清」那一行删掉（同一句会在每一次轮询里重复念）；
    /// ④ 把「不覆盖读取失败」那一行删掉（磁盘读不出来时，状态行会被这一次作业的结果顶掉）。
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
            "            LoadTasks();\n            ShowJobOutcome();\n",
            "            LoadTasks();\n");
        Assert.Contains(Problems(silent), p => p.Contains("ShowJobOutcome", StringComparison.Ordinal));

        // ② 顺序反了：先说结果、再重读任务——LoadTasks 会把刚写上去的那一句盖掉。
        var reordered = Inject(code,
            "            LoadTasks();\n            ShowJobOutcome();\n",
            "            ShowJobOutcome();\n            LoadTasks();\n");
        Assert.Contains(Problems(reordered), p => p.Contains("重读任务", StringComparison.Ordinal));

        // ③ 用掉即清那一行没了：同一句会在每一次轮询里重复念。
        var repeated = Inject(code, "        _lastJobId = null;\n", "");
        Assert.Contains(Problems(repeated), p => p.Contains("_lastJobId", StringComparison.Ordinal));

        // ④ 不覆盖读取失败那一行没了：磁盘读不出来时，状态行会被这一次作业的结果顶掉。
        var overwritten = Inject(code, "        if (_loadFailed) return;\n", "");
        Assert.Contains(Problems(overwritten), p => p.Contains("_loadFailed", StringComparison.Ordinal));
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

        // ⑤ 作业 Id 由 ShowJobState 记下：作业一结束 Running 就答 null，终态与失败原因只能按 Id 读回。
        var showJobState = Body(code, ShowJobStateHeader);
        if (showJobState is null)
        {
            problems.Add("TasksPage 里找不到 ShowJobState 的方法体：本门按它判「作业 Id 记在哪一处」，"
                + "方法改名或搬走之后这一条要跟着改取法。");
        }
        else if (!showJobState.Contains("_lastJobId = job.Id;", StringComparison.Ordinal))
        {
            problems.Add("ShowJobState 里没有记下作业 Id（应为 _lastJobId = job.Id;）：不记就只能在作业结束后"
                + "去问 Running，而它那时已经答 null——终态与失败原因都读不回来。");
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

        return problems;
    }

    /// <summary>
    /// 从去注释后的源码里取一个方法体（从签名那一行到与之配对的右花括号）。
    /// <para>
    /// 按花括号配对数取，不按「下一个 <c>private</c>」取：方法体里有 <c>if</c> / <c>foreach</c> 的花括号，
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
