using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 读取失败态重试入口推广到任务页 / 报告对话框 / 差异确认对话框（具名 pass：harden；
/// DESIGN.md §7「禁用不得移除下一步」「空态=说明现在有什么可做 + 唯一行动按钮」）。
/// <para>
/// 第一百五十轮只给配置页补了「读取失败 → 唯一行动按钮「重试」」。同一族缺陷在另外三屏仍然成立：
/// 任务页 <c>LoadTasks</c> 的 catch 只往状态行写一句红字（清单区保持上一次的样子，首次进入是整块空白）；
/// 报告对话框与差异确认对话框的读取失败只给一句「请关闭本窗口重新打开」「请关闭后重试」——
/// 把「再读一次」交给用户手动重开窗口，屏上没有任何可点的入口。
/// </para>
/// <para>
/// 通用机制与各屏文案的单一落点是 <see cref="LoadFailureState"/>（共享源码编入本程序集，
/// 断言比的是主工程同一份代码）；配置页的 <see cref="ProfileLoadState"/> 把通用机制委托给它，
/// 「重试」这个字面全仓只有一处。三处接线由源码切片机械检查钉住；真实控件上的
/// 「失败 → 重试 → 恢复」由冒烟器在 Windows/wine 下走。
/// </para>
/// </summary>
public class LoadFailureStateTests
{
    // ---------- 口径：按钮文字与判定 ----------

    [Fact]
    public void RetryLabel_IsTheSameLiteralEverywhere()
    {
        // 四屏同一颗按钮、同一字面：各写一份字面必然漂移（一颗叫「重试」、一颗叫「重新读取」）。
        Assert.Equal("重试", LoadFailureState.RetryLabel);
        Assert.Equal(LoadFailureState.RetryLabel, ProfileLoadState.RetryLabel);
        // 各屏说明行里点名的就是这颗按钮（第四十二轮纪律：提示语指名的入口必须真实存在）。
        Assert.Contains("「" + LoadFailureState.RetryLabel + "」", LoadFailureState.Tasks.Hint);
        Assert.Contains("「" + LoadFailureState.RetryLabel + "」", LoadFailureState.Reports.Hint);
        Assert.Contains("「" + LoadFailureState.RetryLabel + "」", LoadFailureState.Decisions.Hint);
        Assert.Contains("「" + LoadFailureState.RetryLabel + "」", LoadFailureState.Decisions.SavedHint);
    }

    [Fact]
    public void RetryVisible_OnlyInFailureState()
    {
        // 三种状态同一时刻只显示一个行动按钮：失败时给「新建任务」等于把用户往另一条路上引。
        Assert.True(LoadFailureState.RetryVisible(failed: true));
        Assert.False(LoadFailureState.RetryVisible(failed: false));
        Assert.True(ProfileLoadState.RetryVisible(failed: true));
        Assert.False(ProfileLoadState.RetryVisible(failed: false));
    }

    [Fact]
    public void FailureStatus_KeepsTheReasonAfterTheTitle()
    {
        // 状态行讲原因、清单区讲下一步：两条通道各说一件事，原因仍如实跟在标题后面。
        Assert.Equal("读取任务失败：目录不可读", LoadFailureState.FailureStatus(LoadFailureState.Tasks.Title, "目录不可读"));
        Assert.Equal("读取任务失败：目录不可读", LoadFailureState.FailureStatus("读取任务失败", "目录不可读"));
        // 配置页的旧签名仍成立（委托到同一份拼装）。
        Assert.Equal("读取配置失败：目录不可读", ProfileLoadState.FailureStatus("目录不可读"));
    }

    [Fact]
    public void FailureMessage_JoinsReasonAndHintWithoutDoublePunctuation()
    {
        // 对话框失败文案 = 标题 + 原因 + 一句可操作说明；原因末尾不带句号，故补一个「。」再接说明。
        Assert.Equal(
            "报告生成失败：目录不可读。" + LoadFailureState.Reports.Hint,
            LoadFailureState.FailureMessage(LoadFailureState.Reports.Title, "目录不可读", LoadFailureState.Reports.Hint));
        // 不出现「。；」「。。」这种双标点。
        var message = LoadFailureState.FailureMessage(LoadFailureState.Decisions.SavedTitle, "文件损坏", LoadFailureState.Decisions.SavedHint);
        Assert.DoesNotContain("。；", message);
        Assert.DoesNotContain("。。", message);
        Assert.DoesNotContain("；；", message);
    }

    /// <summary>
    /// 原因自带句读时不出现双标点（第一百五十八轮，wine 实测抓到的真实缺陷）。
    /// <para>
    /// 存在理由：<see cref="LoadFailureState.FailureMessage"/> 此前无条件补一个「。」，而原因来自
    /// <c>Exception.Message</c>——.NET 的 <c>IOException</c> 是英文句子、以半角句点收尾。第一百五十八轮
    /// wine 实测的任务日志失败提示原文是「读取任务日志失败：The process cannot access the file '…'
    /// because it is being used by another process.。任务目录可能被占用或权限不足；点「重试」再读一次。」
    /// ——两个句号挨在一起。这一句是错误路径上用户唯一能读到的解释，排版缺陷就在最显眼处。
    /// </para>
    /// <para>
    /// 断言用<b>真实异常消息</b>（<see cref="IOException"/> 在 Linux 上真跑出来的那句），不是手拼的样本：
    /// 手拼的样本只能证明「我想到的那种形状被处理了」，真异常消息才能证明「产品路径上那种形状被处理了」。
    /// </para>
    /// </summary>
    [Fact]
    public void FailureMessage_TrimsTheReasonSentenceEnd_BeforeAppendingTheHint()
    {
        // ① 真实英文异常消息（半角句点收尾）：不出现「.。」。
        string english;
        try
        {
            using var hold = new FileStream(
                Path.Combine(Path.GetTempPath(), "ah-load-failure-" + Guid.NewGuid().ToString("N") + ".jsonl"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var second = new FileStream(hold.Name, FileMode.Open, FileAccess.Read, FileShare.None);
            throw new InvalidOperationException("独占打开没有失败，本用例的前提不成立。");
        }
        catch (IOException ex)
        {
            english = ex.Message;
        }
        Assert.EndsWith(".", english, StringComparison.Ordinal);
        var message = LoadFailureState.FailureMessage(LoadFailureState.Journal.Title, english, LoadFailureState.Journal.Hint);
        Assert.DoesNotContain(".。", message, StringComparison.Ordinal);
        Assert.DoesNotContain("。。", message, StringComparison.Ordinal);
        Assert.DoesNotContain("。；", message, StringComparison.Ordinal);
        // 原因本身仍如实保留（去掉的只是末尾那一个句读，不是把原因吞掉）。
        Assert.Contains(english.TrimEnd('.'), message, StringComparison.Ordinal);
        // 说明行照旧接在后面，且只补了一个「。」。
        Assert.EndsWith(LoadFailureState.Journal.Hint, message, StringComparison.Ordinal);
        Assert.Equal(1, Count(message, "。" + LoadFailureState.Journal.Hint));

        // ② 中文原因自带「。」：不出现「。。」。
        var chinese = LoadFailureState.FailureMessage(LoadFailureState.Tasks.Title, "目录不可读。", LoadFailureState.Tasks.Hint);
        Assert.Equal("读取任务失败：目录不可读。" + LoadFailureState.Tasks.Hint, chinese);
        Assert.DoesNotContain("。。", chinese, StringComparison.Ordinal);

        // ③ 原因整段为空：不留一个孤零零的「：。」。
        var empty = LoadFailureState.FailureMessage(LoadFailureState.Reports.Title, "  ", LoadFailureState.Reports.Hint);
        Assert.Equal("报告生成失败。" + LoadFailureState.Reports.Hint, empty);
        Assert.DoesNotContain("：。", empty, StringComparison.Ordinal);

        // ④ 同一口径也管「读取项目列表」那条状态行（半角句点收尾时不出「.；」）。
        var picker = LoadFailureState.PickerFailureStatus("读取分支失败：", "network unreachable.", "读取分支");
        Assert.Equal("读取分支失败：network unreachable；可点「读取分支」重试。", picker);
        Assert.DoesNotContain(".；", picker, StringComparison.Ordinal);
    }

    /// <summary>
    /// 原因以「，」「；」「！」「？」收尾时的反向边界（第一百五十九轮，控制仓简报 P1-3）。
    /// <para>
    /// 存在理由：第一百五十八轮只钉住了「半角句点」与「中文句号」两种收尾——那是 wine 实测抓到的
    /// 那一条真实异常消息的形状。但 <see cref="LoadFailureState"/> 的句读清单是<b>一组</b>字符
    /// （<c>。.；;，,！!？? </c>），清单里其余六个字符此前没有任何用例：把清单改窄成只剩句号，
    /// 全部用例照样绿，而产品路径上「原因以逗号收尾」会拼出「…不可读，。」——半句接句号。
    /// 原因来自 <c>UserException</c> 的文案（服务层逐字写给用户看的那一句），它可以是任何标点收尾。
    /// </para>
    /// <para>
    /// 口径（本轮钉住，不改行为）：原因末尾的句读一律去掉，再接<b>一个</b>「。」与说明行。
    /// 「，」「；」去掉是必须的（否则「，。」「；。」是半句接句号）；「！」「？」也一并归一为「。」——
    /// 说明行是独立的一句操作指引，不是感叹/疑问的延续，保留原标点会让「…磁盘已满！任务目录可能被占用…」
    /// 读成一句话。代价是丢掉原因里的感叹语气，如实记在这里：这是有意的取舍，不是漏改。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("，")]
    [InlineData("；")]
    [InlineData("！")]
    [InlineData("？")]
    public void FailureMessage_TrimsEverySentenceEndInTheList_NotJustThePeriod(string end)
    {
        var reason = "目录不可读" + end;
        var message = LoadFailureState.FailureMessage(LoadFailureState.Tasks.Title, reason, LoadFailureState.Tasks.Hint);

        // ① 末尾句读被去掉、只补一个「。」：整句与「原因本来就没有标点」时逐字相同。
        Assert.Equal("读取任务失败：目录不可读。" + LoadFailureState.Tasks.Hint, message);
        // ② 不出现「，。」「；。」「！。」「？。」这种半句接句号，也不出现双句号。
        Assert.DoesNotContain(end + "。", message, StringComparison.Ordinal);
        Assert.DoesNotContain("。。", message, StringComparison.Ordinal);
        // ③ 原因本身仍如实保留（去掉的只是末尾那一个句读，不是把原因吞掉）。
        Assert.Contains("目录不可读", message, StringComparison.Ordinal);
        // ④ 说明行照旧接在后面，且只出现一次（没有把说明行拼两遍）。
        Assert.EndsWith(LoadFailureState.Tasks.Hint, message, StringComparison.Ordinal);
        Assert.Equal(1, Count(message, LoadFailureState.Tasks.Hint));

        // ⑤ 同一口径也管「读取项目列表」那条状态行：接的是「；」而不是「。」，同样不得出现「，；」「！；」。
        var picker = LoadFailureState.PickerFailureStatus("读取分支失败：", "网络不可达" + end, "读取分支");
        Assert.Equal("读取分支失败：网络不可达；可点「读取分支」重试。", picker);
        Assert.DoesNotContain(end + "；", picker, StringComparison.Ordinal);
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

    [Fact]
    public void PickerFailureStatus_NamesTheEntryAndAvoidsDoublePunctuation()
    {
        // 配置对话框不新增第二颗「重试」：失败文案指名那颗已经回到可用的读取按钮。
        Assert.Equal(
            "读取项目列表失败：命令超时；可点「读取项目列表」重试。",
            LoadFailureState.PickerFailureStatus("读取项目列表失败：", "命令超时", "读取项目列表"));
        // 描述自带句号时不出现「。；」这种双标点。
        Assert.Equal(
            "读取分支失败：网络不可达；可点「读取分支」重试。",
            LoadFailureState.PickerFailureStatus("读取分支失败：", "网络不可达。", "读取分支"));
        // 配置页的旧签名仍成立（委托到同一份拼装）。
        Assert.Equal(
            "读取分支失败：网络不可达；可点「读取分支」重试。",
            ProfileLoadState.PickerFailureStatus("读取分支失败：", "网络不可达。", "读取分支"));
    }

    [Fact]
    public void FailureCopy_PassesChineseTypographyRules()
    {
        // 失败态文案与其余界面文案同一套中文排版口径（DESIGN.md §3 排版附加条款、设计验收 D10/D11）。
        var samples = new[]
        {
            LoadFailureState.RetryLabel,
            LoadFailureState.Tasks.Title,
            LoadFailureState.Tasks.Hint,
            LoadFailureState.Reports.Title,
            LoadFailureState.Reports.Hint,
            LoadFailureState.Decisions.Title,
            LoadFailureState.Decisions.Hint,
            LoadFailureState.Decisions.SavedTitle,
            LoadFailureState.Decisions.SavedHint,
            LoadFailureState.FailureStatus(LoadFailureState.Tasks.Title, "目录不可读"),
            LoadFailureState.FailureMessage(LoadFailureState.Reports.Title, "目录不可读", LoadFailureState.Reports.Hint),
            LoadFailureState.FailureMessage(LoadFailureState.Decisions.SavedTitle, "文件损坏", LoadFailureState.Decisions.SavedHint),
        };
        foreach (var text in samples)
        {
            foreach (var mark in CopyRules.HalfWidthMarks)
            {
                Assert.True(CopyRules.IndexOfHalfWidthMark(text, mark) < 0, $"「{text}」里的半角「{mark}」紧挨中文。");
            }
            Assert.True(CopyRules.IndexOfStrayColon(text) < 0, $"「{text}」里的半角冒号紧挨中文。");
            Assert.DoesNotContain("“", text);
            Assert.DoesNotContain("”", text);
            foreach (var word in CopyRules.BannedWords) Assert.DoesNotContain(word, text);
            foreach (var phrase in CopyRules.ProgressNarration) Assert.DoesNotContain(phrase, text);
        }
    }

    // ---------- 接线：口径存在但界面没接线等于没有 ----------

    [Fact]
    public void TasksPage_WiresRetryToTheSameLoadEntry()
    {
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        // 按钮文字取自单一落点，动作回到同一个读取入口（不复制一份读取逻辑）。
        Assert.Matches(@"(?m)^\s*private readonly Button _emptyRetry = Ui\.Ghost\(LoadFailureState\.RetryLabel\);", page);
        Assert.Matches(@"(?m)^\s*_emptyRetry\.Click \+= \(_, _\) => LoadTasks\(\);", page);
        Assert.Matches(@"(?m)^\s*emptyActions\.Controls\.Add\(_emptyRetry\);", page);
        // 三态门控：失败时只显示「重试」，另两颗按钮一起收掉。
        var refresh = Slice(page, "private void RefreshEmptyState()", "private static string?[] FilterFields(");
        Assert.Matches(@"(?m)^\s*_emptyCreate\.Visible = !_loadFailed && !filtered;", refresh);
        Assert.Matches(@"(?m)^\s*_emptyClear\.Visible = !_loadFailed && filtered;", refresh);
        Assert.Matches(@"(?m)^\s*_emptyRetry\.Visible = LoadFailureState\.RetryVisible\(_loadFailed\);", refresh);
        Assert.Matches(@"(?m)^\s*_emptyTitle\.Text = _loadFailed \? LoadFailureState\.Tasks\.Title", refresh);
        Assert.Matches(@"(?m)^\s*_emptyHint\.Text = _loadFailed \? LoadFailureState\.Tasks\.Hint", refresh);
    }

    [Fact]
    public void TasksPage_FailureStateReplacesTheListAndKeepsTheReason()
    {
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        // 两条 catch 都进失败态：UserException 的文案本身就是给用户看的那一句，其余异常先脱敏。
        var load = Slice(page, "private void LoadTasks()", "private void ShowLoadFailure(");
        Assert.Matches(@"(?m)^\s*ShowLoadFailure\(ex\.Message\);", load);
        Assert.Matches(@"(?m)^\s*ShowLoadFailure\(LoadFailureState\.FailureStatus\(LoadFailureState\.Tasks\.Title, Redactor\.Redact\(ex\.Message\)\)\);", load);
        // 读成功即退出失败态（否则重试成功后仍停在失败态）。
        Assert.Matches(@"(?m)^\s*_loadFailed = false;", load);
        var failure = Slice(page, "private void ShowLoadFailure(", "private void RefreshEmptyState()");
        Assert.Matches(@"(?m)^\s*_loadFailed = true;", failure);
        // 清单不留旧行：这一屏刚证明「磁盘上的任务读不出来」，继续显示旧行等于把不可信的内容当成现状。
        Assert.Matches(@"(?m)^\s*_list\.Items\.Clear\(\);", failure);
        Assert.Matches(@"(?m)^\s*_empty\.Visible = true;", failure);
        Assert.Matches(@"(?m)^\s*_list\.Visible = false;", failure);
        // 清单空了，详情区没有可作用的对象（与「没有选中任务」同一呈现）。
        Assert.Matches(@"(?m)^\s*_selectedTask = null;", failure);
        Assert.Matches(@"(?m)^\s*RefreshDetail\(\);", failure);
        // 状态行仍如实给出原因，且刷新空态（三态在同一轮推导）。
        Assert.Matches(@"(?m)^\s*RefreshEmptyState\(\);", failure);
        Assert.Matches(@"(?m)^\s*SetStatus\(status\);", failure);
    }

    [Fact]
    public void TasksPage_DoesNotInlineFailureCopy()
    {
        // 「读取任务失败」字面只在 LoadFailureState 里：页面出现带它的字符串字面即口径漂移（注释里的引述不算）。
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        Assert.Empty(Regex.Matches(page, "\"[^\"\\r\\n]*读取任务失败[^\"\\r\\n]*\""));
        Assert.Contains("LoadFailureState.FailureStatus", page);
    }

    [Fact]
    public void ReportsDialog_RetryRerunsTheSamePreviewEntry()
    {
        var dialog = Repo.Source("Desktop", "Native", "ReportsDialog.cs");
        // 按钮文字取自单一落点，动作回到同一个生成入口（不复制渲染逻辑）。
        Assert.Matches(@"(?m)^\s*private readonly Button _retry = Ui\.Ghost\(LoadFailureState\.RetryLabel\);", dialog);
        Assert.Matches(@"(?m)^\s*_retry\.Click \+= \(_, _\) => LoadPreview\(\);", dialog);
        // 打开时与点「重试」走同一个入口。
        var onLoad = Slice(dialog, "protected override void OnLoad(EventArgs e)", "private void LoadPreview()");
        Assert.Matches(@"(?m)^\s*LoadPreview\(\);", onLoad);
        // 失败态：提示可见、预览收起、四个导出入口一起停用、「重试」出现。
        var hint = Slice(dialog, "private void ShowHint(string message, Color color)", "private void SetStatus(");
        Assert.Matches(@"(?m)^\s*_retry\.Visible = true;", hint);
        Assert.Matches(@"(?m)^\s*_mdButton\.Enabled = false;", hint);
        Assert.Matches(@"(?m)^\s*_handoffButton\.Enabled = false;", hint);
        // 读成功即退出失败态（否则重试成功后仍停在失败态）。
        var load = Slice(dialog, "private void LoadPreview()", "/// <summary>导出：");
        Assert.Matches(@"(?m)^\s*_retry\.Visible = false;", load);
        Assert.Matches(@"(?m)^\s*_mdButton\.Enabled = true;", load);
        // 失败文案取单一落点，且不再把「重开窗口」当成下一步（注释里对旧文案的引述不算）。
        Assert.Contains("LoadFailureState.FailureMessage(LoadFailureState.Reports.Title", dialog);
        Assert.DoesNotContain("关闭本窗口重新打开", CodeOnly(dialog));
    }

    [Fact]
    public void DecisionsDialog_RetryRerunsTheSameLoadEntry()
    {
        var dialog = Repo.Source("Desktop", "Native", "DecisionsDialog.cs");
        Assert.Matches(@"(?m)^\s*private readonly Button _retry = Ui\.Ghost\(LoadFailureState\.RetryLabel\);", dialog);
        Assert.Matches(@"(?m)^\s*_retry\.Click \+= \(_, _\) => LoadAll\(\);", dialog);
        var onLoad = Slice(dialog, "protected override void OnLoad(EventArgs e)", "private void LoadAll()");
        Assert.Matches(@"(?m)^\s*LoadAll\(\);", onLoad);
        // 两种失败态（检查结果读不出来、已保存决策读不出来）都进「可重试」的提示出口。
        var load = Slice(dialog, "private void LoadAll()", "/// <summary>");
        Assert.Equal(2, Regex.Matches(load, @"canRetry: true").Count);
        Assert.Contains("LoadFailureState.FailureMessage(LoadFailureState.Decisions.Title", load);
        Assert.Contains("LoadFailureState.FailureMessage(LoadFailureState.Decisions.SavedTitle", load);
        // 「尚未检查」不是失败态：重试解决不了它，下一步在任务页。
        Assert.Contains("尚未检查。先在任务页点「开始检查」", load);
        // 提示出口按门控决定「重试」是否出现（筛选空态与「尚未检查」都不显示）。
        var hint = Slice(dialog, "private void ShowHint(string message, Color color", "private void EnableActions(");
        Assert.Matches(@"(?m)^\s*_retry\.Visible = canRetry;", hint);
        // 不再把「关闭后重试」当成下一步（注释里对旧文案的引述不算）。
        var code = CodeOnly(dialog);
        Assert.DoesNotContain("请关闭后重试", code);
        Assert.DoesNotContain("请关闭后在任务页重新检查再试", code);
    }

    [Fact]
    public void LoadFailureState_IsInTheSharedCompileList()
    {
        // 口径文件必须经共享源码编入测试程序集：断言比的是主工程同一份代码，不是抄一份。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\LoadFailureState.cs", csproj);
        // 口径文件不依赖 WinForms（否则 Linux 上编不进测试程序集）。
        var source = Repo.Source("Desktop", "Native", "LoadFailureState.cs");
        Assert.DoesNotContain("using System.Windows.Forms", source);
        Assert.DoesNotContain("using System.Drawing", source);
    }

    /// <summary>
    /// 去掉注释行后的源码：断言「不再出现某句旧文案」时不能连注释里对旧文案的引述一起算
    /// （第五十二、六十三轮同一教训：切法不算注释，检查就会把说明文字当成违规）。
    /// </summary>
    private static string CodeOnly(string source) => CodeScan.Text(source);

    /// <summary>取 [start, end) 之间的源码段：把断言限定在方法体内。</summary>
    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
