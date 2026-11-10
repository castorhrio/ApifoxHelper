using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 任务日志对话框的读取失败态（第一百五十七轮，具名 pass：harden）。
/// <para>
/// 存在理由：第一百五十至一百五十三轮把「读取失败 → 唯一行动按钮「重试」」推广到了配置页、任务页、
/// 报告对话框与差异确认对话框，<b>任务日志对话框漏在外面</b>：它的 catch 只写一句
/// 「读取任务日志失败：…。请关闭后重新执行一次再试。」——把「再读一次」交给用户手动重开窗口，
/// 屏上没有任何可点的入口。这与 DESIGN.md §7「禁用不得移除下一步」是同一条纪律：
/// 失败态也是空态的一种，它同样欠用户一句「现在能做什么」和一个能点的入口。
/// </para>
/// <para>
/// 断言分两层：口径（<see cref="LoadFailureState"/> 经共享源码编入本程序集，真执行）与接线
/// （<c>JournalDialog.cs</c> 的源码切片——它依赖 WinForms，Linux 侧不能共享编译）。
/// 真实控件上的「失败 → 重试 → 恢复」由冒烟器 <c>RunJournalLoadFailure</c> 在 wine/真机下走。
/// </para>
/// </summary>
public class JournalLoadFailureTests
{
    private static string Dialog => Repo.Source("Desktop", "Native", "JournalDialog.cs");

    // ---------- 口径 ----------

    /// <summary>失败态文案只有一份：标题、说明、按钮文字都取 <see cref="LoadFailureState"/>。</summary>
    [Fact]
    public void JournalCopy_ComesFromTheSingleSource()
    {
        Assert.Equal("读取任务日志失败", LoadFailureState.Journal.Title);
        // 说明行点名那颗按钮（第四十二轮纪律：提示语指名的入口必须真实存在）。
        Assert.Contains("「" + LoadFailureState.RetryLabel + "」", LoadFailureState.Journal.Hint);
        // 说清「怎么办」：再读一次。
        Assert.Contains("再读一次", LoadFailureState.Journal.Hint);
    }

    /// <summary>失败文案过中文排版口径（错误路径上的那一句也是用户要读的）。</summary>
    [Fact]
    public void JournalFailureMessage_FollowsTheTypographyCaliber()
    {
        var message = LoadFailureState.FailureMessage(
            LoadFailureState.Journal.Title, "文件被占用", LoadFailureState.Journal.Hint);
        Assert.StartsWith("读取任务日志失败：文件被占用。", message, StringComparison.Ordinal);
        foreach (var mark in CopyRules.HalfWidthMarks)
        {
            Assert.True(CopyRules.IndexOfHalfWidthMark(message, mark) < 0,
                $"「{message}」里的半角「{mark}」紧挨中文。");
        }
        Assert.True(CopyRules.IndexOfStrayColon(message) < 0, $"「{message}」里有半角冒号。");
        Assert.False(message.Contains('“') || message.Contains('”'), $"「{message}」里出现弯引号。");
        foreach (var word in CopyRules.BannedWords.Concat(CopyRules.ProgressNarration))
        {
            Assert.DoesNotContain(word, message, StringComparison.Ordinal);
        }
        // 不出现「。；」「。。」这种双标点。
        Assert.DoesNotContain("。；", message, StringComparison.Ordinal);
        Assert.DoesNotContain("。。", message, StringComparison.Ordinal);
    }

    // ---------- 接线（源码切片） ----------

    /// <summary>
    /// 打开与「重试」走同一个读取入口：不复制读取逻辑，也不出现第二条读日志的路。
    /// </summary>
    [Fact]
    public void JournalDialog_ReadsThroughOneEntry()
    {
        var source = Dialog;
        // OnLoad 只调入口，不自己读。
        Assert.Contains("LoadJournal();", source, StringComparison.Ordinal);
        // 「重试」的动作回到同一个入口。
        Assert.Contains("_retry.Click += (_, _) => LoadJournal();", source, StringComparison.Ordinal);
        // 读日志只有一处（FileStore.ReadJsonLines 在整份文件里恰出现一次）。
        Assert.Equal(1, Count(source, "FileStore.ReadJsonLines("));
    }

    /// <summary>
    /// 失败态取单一落点：标题、说明、按钮文字都不在对话框里就地拼。
    /// 此前那句「请关闭后重新执行一次再试」把下一步交给用户手动重开窗口，必须消失。
    /// </summary>
    [Fact]
    public void JournalDialog_TakesFailureCopyFromTheSingleSource()
    {
        // 只看代码行：注释里引用旧文案是文档（说明改了什么），不是就地拼字面量。
        var source = CodeOnly(Dialog);
        Assert.Contains("LoadFailureState.FailureMessage(", source, StringComparison.Ordinal);
        Assert.Contains("LoadFailureState.Journal.Title", source, StringComparison.Ordinal);
        Assert.Contains("LoadFailureState.Journal.Hint", source, StringComparison.Ordinal);
        Assert.Contains("Ui.Ghost(LoadFailureState.RetryLabel)", source, StringComparison.Ordinal);
        // 就地拼的字面量一个都不许留。
        Assert.DoesNotContain("读取任务日志失败", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"重试\"", source, StringComparison.Ordinal);
        // 旧文案（把「再读一次」交给用户重开窗口）不得回来。
        Assert.DoesNotContain("请关闭后重新执行", source, StringComparison.Ordinal);
        Assert.DoesNotContain("请关闭后重试", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 失败态不留旧行：这一屏刚证明「磁盘上的日志读不出来」，继续显示旧行等于把不可信的内容当成现状
    /// （与第一百五十轮配置页、第一百五十二轮任务页同一纪律）。
    /// </summary>
    [Fact]
    public void JournalDialog_FailureDropsTheStaleRows()
    {
        var body = MethodBody(Dialog, "private void ShowLoadFailure(");
        Assert.Contains("_lines = [];", body, StringComparison.Ordinal);
        Assert.Contains("_list.Items.Clear();", body, StringComparison.Ordinal);
        // 未结束动作那句话讲的是旧数据，一起收掉。
        Assert.Contains("_unfinishedIds = new HashSet<string>(StringComparer.Ordinal);", body, StringComparison.Ordinal);
        // 失败文案与「重试」都从这一处出去。
        Assert.Contains("canRetry: true", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 「重试」只在读取失败时出现：空态与筛到零行不是失败，重试解决不了它们
    /// （同一时刻只给一个下一步，与差异确认「尚未检查不显示重试」同一取舍）。
    /// </summary>
    [Fact]
    public void JournalDialog_ShowsRetryOnlyInFailureState()
    {
        var body = MethodBody(Dialog, "private void ShowHint(");
        // 门控取共享口径，不就地写 `_retry.Visible = true`。
        Assert.Contains("_retry.Visible = LoadFailureState.RetryVisible(canRetry);", body, StringComparison.Ordinal);
        // 空态那一支不带 canRetry（默认 false）。
        Assert.Contains("ShowHint(JournalEntries.EmptyText, UiTokens.Muted);", Dialog, StringComparison.Ordinal);
        // 读成功即退出失败态：否则重试成功后仍停在失败态。
        var load = MethodBody(Dialog, "private void LoadJournal(");
        Assert.Contains("_retry.Visible = false;", load, StringComparison.Ordinal);
    }

    /// <summary>
    /// 按钮真的在屏上：失败态文案指名的那颗按钮必须被加进按钮行（第四十二轮纪律）。
    /// </summary>
    [Fact]
    public void JournalDialog_PutsTheRetryButtonOnScreen()
    {
        var source = Dialog;
        Assert.Contains("buttonRow.Controls.Add(_retry);", source, StringComparison.Ordinal);
        // 默认不显示（打开时不是失败态）。
        Assert.Contains("_retry.Visible = false;", source, StringComparison.Ordinal);
        // 关闭仍是默认按钮与取消按钮：新增一颗按钮不改对话框的 Enter/Esc 语义。
        Assert.Contains("AcceptButton = closeButton;", source, StringComparison.Ordinal);
        Assert.Contains("CancelButton = closeButton;", source, StringComparison.Ordinal);
    }

    /// <summary>去掉注释（<c>//</c>、<c>///</c> 与 <c>/* … */</c> 区间，口径在 CodeScan 一份）：断言「代码里不就地拼字面量」时不该被文档引用绊住。</summary>
    private static string CodeOnly(string source) => CodeScan.Text(source);

    private static int Count(string text, string fragment)
    {
        var count = 0;
        for (var i = text.IndexOf(fragment, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(fragment, i + fragment.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    /// <summary>取一个方法的方法体（按花括号深度界定，不靠缩进猜）；找不到即红。</summary>
    private static string MethodBody(string source, string header)
    {
        var start = source.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, $"JournalDialog.cs 里找不到「{header}」。");
        var open = source.IndexOf('{', start);
        Assert.True(open > start, $"「{header}」后面没有方法体。");
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source[open..(i + 1)];
            }
        }
        Assert.Fail($"「{header}」的方法体没有闭合。");
        return string.Empty;
    }
}
