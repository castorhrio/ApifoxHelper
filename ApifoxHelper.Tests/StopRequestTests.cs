using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「点下停止之后，那一行要说一句话」这一片的门（第三百八十八轮，具名 pass：harden）。
/// <para>
/// <b>它管的是哪一段</b>：<see cref="JobOutcomeTests"/> 管作业<b>结束之后</b>状态行说什么；这一族管的是
/// 它前面那一段——从用户点下那颗「停止 / 取消」，到作业真的在当前步骤之后停住。取消是协作式的
/// （<c>JobRunner.Cancel</c> 只按下标志，服务在当前步骤/动作之间才响应），这一段按项目大小是几秒到几十秒。
/// 改之前那一段屏上什么都没有：那一行照旧念「正在执行同步… 40%」，一个字不变，
/// 用户看不出自己那一击有没有被收到，只能再点一次，或者以为这颗按钮坏了。
/// </para>
/// <para>
/// <b>三屏一起改，不挑一屏</b>：带「停止」的屏全原生界面恰三处——任务页、同步预览、连接与写入探测。
/// 三处此前都是同一行内联 <c>_jobs.Cancel(…)</c>、都零回应。只补一处
/// 就是同壳不同行为（第五十五、五十七、五十九轮那条判据），补三处而把判定写三份则是下一轮漂移的来源，
/// 所以：<b>那句话只在 <c>JobOutcome</c> 里写一次，三屏各自只接线一次</b>。
/// 第三百八十八轮接线时那颗钮在任务页写「取消」、另两屏写「停止」——同一手势两种叫法，而屏上回应那一句
/// 说的是「正在停止…」；第三百八十九轮把三处的字面、字段名与入口名收成同一个词。
/// </para>
/// <para>
/// <b>它判什么</b>：① 那句文案真执行（三种入参组合）；② 那句话在全仓界面源码里只有一处落点；
/// ③ 三屏各自的接线（点击走具名方法、不走内联取消；判定只经共享口径；请求按<b>作业</b> Id 记；
/// 记一笔与清一笔各恰一处；先收下请求再重画）；④ 那一击没收到作业时不许凭空说「正在停止」；
/// ⑤ 那三颗钮在全原生界面用的是同一个词，而那个词正是回应那句里用的那一个。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判作业是不是真的停了（那是各服务自己的取消语义），也不判那句话在真机上
/// 停留得够不够久、读起来顺不顺眼——那要 Windows 真机（G-D5，真机 0 台）。
/// </para>
/// </summary>
public class StopRequestTests
{
    private static string OutcomeSource => Repo.Source("Desktop", "Native", "JobOutcome.cs");

    /// <summary>
    /// 三块屏：文件、那颗钮的点击接线、收下请求的那个方法、该方法要走的那一处重画。
    /// 四件凑齐才是「点下去 → 收下请求 → 当场说一句话」这条线成立。
    /// </summary>
    private static readonly (string File, string Click, string Request, string Render)[] Screens =
    [
        ("TasksPage.cs", "_stopButton.Click += (_, _) => RequestStop();", "private void RequestStop()", "ShowJobState(job);"),
        ("SyncPreviewDialog.cs", "_stopButton.Click += (_, _) => RequestStop();", "private void RequestStop()", "ShowRunningStatus(job);"),
        ("CapabilitiesDialog.cs", "_stopButton.Click += (_, _) => RequestStop();", "private void RequestStop()", "ShowProbeRunning(job);"),
    ];

    /// <summary>
    /// 按签名切出方法体（到下一个四空格缩进的收尾大括号为止）。
    /// 重画与记请求这两件要在<b>那一个方法里</b>数：整文件数会把轮询里那处同名的调用一起算进去。
    /// </summary>
    private static string Body(string code, string signature)
    {
        var at = code.IndexOf(signature, StringComparison.Ordinal);
        if (at < 0) return "";
        var end = code.IndexOf("\n    }", at, StringComparison.Ordinal);
        return end < 0 ? code[at..] : code[at..(end + 5)];
    }

    private static int Count(string text, string needle)
    {
        var at = 0;
        var seen = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            seen++;
            at += needle.Length;
        }

        return seen;
    }

    /// <summary>
    /// ① 文案真执行：请求收下时说出那一句（带作业名、带「当前步骤结束后停下」这一步），
    /// 没收下时答 null——各屏沿用自己那句进行中说明。
    /// </summary>
    [Fact]
    public void StoppingText_OnlySpeaksWhenTheRequestWasReceived()
    {
        Assert.Equal("正在停止源码扫描…当前步骤结束后停下。", JobOutcome.StoppingText("源码扫描", true));
        Assert.Equal("正在停止执行同步…当前步骤结束后停下。", JobOutcome.StoppingText("执行同步", true));
        Assert.Equal("正在停止写入兼容探测…当前步骤结束后停下。", JobOutcome.StoppingText("写入兼容探测", true));

        // 没点过：答 null 而不是答一句「正在停止…」——那等于替用户说他没做过的事。
        Assert.Null(JobOutcome.StoppingText("源码扫描", false));
    }

    /// <summary>
    /// ② 那句话在整个界面源码里只写一次（在 <c>JobOutcome.cs</c>），三屏都不就地拼。
    /// <para>
    /// 按剥掉注释后的代码算：注释里点名这句话是读者找实物的路，不是第二份字面量（<see cref="CodeScan"/> 口径）。
    /// </para>
    /// </summary>
    [Fact]
    public void StoppingSentence_HasOneHomeInTheWholeNativeLayer()
    {
        var written = Repo.NativeSources()
            .Where(s => s.Code.Contains("正在停止", StringComparison.Ordinal))
            .Select(s => s.Name)
            .ToArray();

        Assert.Equal(["JobOutcome.cs"], written);
        Assert.Equal(1, Count(CodeScan.Text(OutcomeSource), "正在停止"));
    }

    /// <summary>
    /// ③ 三屏接线逐条对账（空 = 对得上）。这一判本体可喂夹具，注入与实物走同一条路。
    /// </summary>
    [Fact]
    public void EveryStopButton_WiredToTheSharedJudgment()
    {
        Assert.Empty(Problems(Repo.NativeSources()));
    }

    /// <summary>
    /// ④ 那一击没收到作业时什么都不说：<c>JobRunner.Cancel</c> 答 false（没有作业在跑）时不得记下请求。
    /// </summary>
    [Fact]
    public void StopRequest_IsRecordedOnlyWhenACancelWasAccepted()
    {
        var problems = new List<string>();
        foreach (var (file, _, request, _) in Screens)
        {
            var body = Body(CodeScan.Text(Repo.Source("Desktop", "Native", file)), request);
            var accepted = body.IndexOf("!_jobs.Cancel(", StringComparison.Ordinal);
            var recorded = body.IndexOf("_stopRequestedJobId = job.Id;", StringComparison.Ordinal);
            if (accepted < 0) problems.Add($"{file}：{request} 没有判 _jobs.Cancel 的返回值——没有作业在跑时也会说「正在停止」。");
            else if (recorded < 0) problems.Add($"{file}：{request} 没有记下是哪一个作业被请求停止。");
            else if (recorded < accepted) problems.Add($"{file}：{request} 里记请求排在问 Cancel 结果之前，没收到也会说那句话。");
        }

        Assert.Empty(problems);
    }

    /// <summary>
    /// ⑤ 那三颗钮全界面同一个词，且那个词就是回应那一句里用的那一个（第三百八十九轮）。
    /// <para>
    /// <b>补的是哪个缺口</b>：第三百八十八轮把三屏的接线收成一处口径，那颗钮自己却仍三处两个叫法——
    /// 任务页写「取消」、另两屏写「停止」。同一手势在一个壳里两种叫法，按之前就得先认出这是两件事；
    /// 而屏上回应那一句说的是「正在停止…」，叫「取消」的那一颗按下去听到的是「停止」，用词自己先分家。
    /// </para>
    /// <para>
    /// <b>这条门判的是被承诺的实物</b>：三处字面在各屏的出厂点上，不在这条断言自己的对象里，
    /// 按第二百五十二轮起那五步的第 ② 步答「不是」——是对账门，不属「判据判自己」那一族，照常随实物一起长：
    /// 第四颗那样的钮出厂时没登记进来，这里报的是颗数，不是文案。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryStopButton_UsesTheOneWordTheReplyAlsoUses()
    {
        Assert.Empty(LabelProblems(Repo.NativeSources()));
    }

    /// <summary>
    /// ⑤ 的判据本体：把 <c>Ui.DangerGhost("…")</c> 出厂的字面逐个收上来对两件事——
    /// 颗数恰为 <see cref="Screens"/> 那三块屏，且它们用的是回应那句里那一个字。
    /// </summary>
    private static List<string> LabelProblems(IReadOnlyList<NativeSource> sources)
    {
        var problems = new List<string>();
        var labels = new List<(string File, string Label)>();
        foreach (var source in sources)
        {
            foreach (Match match in DangerGhostLabel.Matches(source.Code))
            {
                var taken = match.Groups[1].Value;
                labels.Add((source.Name, taken));
                if (taken.Length == 0) problems.Add($"{source.Name}：一颗 DangerGhost 出厂时没有字面。");
            }
        }

        if (labels.Count != Screens.Length)
        {
            problems.Add($"全原生界面 DangerGhost 出厂的钮是 {labels.Count} 颗，本族登记的是 {Screens.Length} 块屏："
                + "同一手势的屏集合变了，先改这一族的清单再改界面（两侧不许各长一半）。");
        }

        // 回应那一句用的那两个字从 JobOutcome 源码现读，不在这里抄第二份整句。
        var reply = Regex.Match(CodeScan.Text(OutcomeSource), "正在([\u4e00-\u9fff]{2})");
        if (!reply.Success)
        {
            problems.Add("JobOutcome 里那句「正在……」不写了：那颗钮按下去没有任何一句话可对照用词。");
            return problems;
        }

        var word = reply.Groups[1].Value;
        foreach (var (file, label) in labels.Where(l => !l.Label.Contains(word, StringComparison.Ordinal)))
        {
            problems.Add($"{file}：那颗钮写「{label}」，而屏上回应那一句说的是「{word}」："
                + "同一手势一个词，按下去听到的那一句得用同一个字（另写一份说法，下一轮改一处不动另一处）。");
        }

        return problems;
    }

    /// <summary>一颗 DangerGhost 出厂时给的字面。</summary>
    private static readonly Regex DangerGhostLabel = new(@"Ui\.DangerGhost\(""([^""]*)""\)", RegexOptions.Compiled);

    /// <summary>
    /// 自测：把这一族的接线一样一样改回去，每一条都要报出自己那一处（改回实物后全空）。
    /// </summary>
    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        var real = Repo.NativeSources();
        Assert.Empty(Problems(real));

        // ① 点击退回内联取消：那一击又变成只有服务层知道的事。
        var inline = Replace(real, "SyncPreviewDialog.cs",
            "_stopButton.Click += (_, _) => RequestStop();",
            "_stopButton.Click += (_, _) => _jobs.Cancel(_taskId);");
        Assert.Contains(Problems(inline), p => p.Contains("Click", StringComparison.Ordinal));

        // ② 界面自己拼那句话：一处改了另两处不会跟着走。
        var rewritten = Replace(real, "TasksPage.cs",
            "JobOutcome.StoppingText(job.Kind, _stopRequestedJobId == job.Id)",
            "$\"正在停止{job.Kind}…\"");
        Assert.Contains(Problems(rewritten), p => p.Contains("StoppingText", StringComparison.Ordinal));

        // ③ 记下的是任务 Id 而不是作业 Id：下一次作业会读到上一次的请求。
        var byTask = Replace(real, "CapabilitiesDialog.cs",
            "_stopRequestedJobId = job.Id;",
            "_stopRequestedJobId = job.Scope;");
        Assert.Contains(Problems(byTask), p => p.Contains("CapabilitiesDialog.cs", StringComparison.Ordinal)
                                               && p.Contains("恰一处", StringComparison.Ordinal));

        // ④ 清那一笔的地方多出一处：两个落点清同一个字段，下一轮不知道该信哪个。
        var doubleCleared = Replace(real, "SyncPreviewDialog.cs",
            "_stopRequestedJobId = null;",
            "_stopRequestedJobId = null;\n        _stopRequestedJobId = null;");
        Assert.Contains(Problems(doubleCleared), p => p.Contains("清", StringComparison.Ordinal));

        // ⑤ 收下请求之后不重画：那句话要等下一个轮询周期才出来，那一击仍然没有回应。
        var noRepaint = Replace(real, "TasksPage.cs",
            "ShowJobState(job);\n    }",
            "}\n    }");
        Assert.Contains(Problems(noRepaint), p => p.Contains("重画", StringComparison.Ordinal));

        // ⑥ 那颗钮的叫法退回「取消」：同一手势又分成两个词，而屏上回应那一句照旧说「停止」。
        Assert.Empty(LabelProblems(real));
        var renamed = Replace(real, "TasksPage.cs",
            "Ui.DangerGhost(\"停止\")",
            "Ui.DangerGhost(\"取消\")");
        Assert.Contains(LabelProblems(renamed), p => p.Contains("TasksPage.cs", StringComparison.Ordinal)
                                                 && p.Contains("停止", StringComparison.Ordinal));
    }

    /// <summary>
    /// 三屏接线的判定本体（与 <c>BannerFocusPlanTests.Problems</c>、<c>JobOutcomeTests.Problems</c> 同一形状）。
    /// </summary>
    private static List<string> Problems(IReadOnlyList<NativeSource> sources)
    {
        var problems = new List<string>();
        foreach (var (file, click, request, render) in Screens)
        {
            var source = sources.FirstOrDefault(s => s.Name == file)
                ?? throw new InvalidOperationException($"{file} 不在 Desktop/Native 下：这一族的接线对象变了，先改清单再改界面。");
            var code = source.Code;
            var body = Body(code, request);
            if (body.Length == 0)
            {
                problems.Add($"{file}：找不到收下停止请求的那个方法「{request}」。");
                continue;
            }

            if (Count(code, click) != 1)
                problems.Add($"{file}：那颗钮的点击没有且只有一处「{click}」（现在 {Count(code, click)} 处）——" +
                             "点击要么还内联着取消调用，要么被拆成了两套说法。");

            if (Regex.IsMatch(code, @"\.Click\s*\+=\s*_?\s*\(_?,\s*_?\)\s*=>\s*_jobs\.Cancel", RegexOptions.Compiled))
                problems.Add($"{file}：点击里直接调 _jobs.Cancel，屏上那一句改由谁说是分散的。");

            if (Count(code, "JobOutcome.StoppingText(") != 1)
                problems.Add($"{file}：共享口径 StoppingText 的调用点必须恰一处（现在 {Count(code, "JobOutcome.StoppingText(")} 处）——" +
                             "零处就是界面自己拼那句话；两处就是同一屏两种说法各活一半。");

            if (Count(body, "_stopRequestedJobId = job.Id;") != 1)
                problems.Add($"{file}：{request} 里必须恰一处记下「为哪一个作业请求过停止」、且记的是作业 Id（现在 {Count(body, "_stopRequestedJobId = job.Id;")} 处）——" +
                             "记任务 Id 或作业序号，下一次作业会读到上一次的请求。");

            if (Count(code, "_stopRequestedJobId = null;") != 1)
                problems.Add($"{file}：清那一笔只许一个落点（现在 {Count(code, "_stopRequestedJobId = null;")} 处）——" +
                             "两个落点清同一个字段，下一轮不知道该信哪个。");

            if (Count(body, render) != 1)
                problems.Add($"{file}：{request} 里要走那唯一一处重画「{render}」恰一次（现在 {Count(body, render)} 处）——" +
                             "不重画，那句话要等下一个轮询周期才出来，那一击仍然没有回应。");

            if (code.Contains("正在停止", StringComparison.Ordinal))
                problems.Add($"{file}：界面源码里出现了那句话的字面，它应当在 JobOutcome 只写一次。");
        }

        return problems;
    }

    /// <summary>把一个文件的一段替换成另一段，其余文件原样带上——注入走与实物同一个判体。</summary>
    private static IReadOnlyList<NativeSource> Replace(
        IReadOnlyList<NativeSource> sources, string file, string from, string to)
    {
        var target = sources.First(s => s.Name == file);
        if (!target.Text.Contains(from, StringComparison.Ordinal))
            throw new InvalidOperationException($"{file} 里没有这一段，注入本身失效：{from}");

        var rewritten = target.Text.Replace(from, to, StringComparison.Ordinal);
        return sources
            .Select(s => s.Name == file ? new NativeSource(s.Name, rewritten) : s)
            .ToList();
    }
}
