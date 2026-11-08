using System.Text.RegularExpressions;
using ApifoxHelper.Comments;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 注释候选「改文字」与「撤销接受」口径断言（第一百一十轮，DESIGN.md §6「候选文字编辑」条款）：
/// 冻结页面的候选卡片有可编辑的说明文字框、「保存修改」与「撤销接受」，原生审阅对话框此前只能
/// 接受 / 搁置 / 驳回，候选文字只读——而候选文字正是要写进源码的 /// 注释，生成或导入的文字有错字、
/// 或需要按团队口径改写时，原生界面只能驳回重来（驳回后重新生成未必得到同一段文字）；
/// 更要紧的是「接受」在原生界面是单向的：已接受的候选只能改成搁置或驳回，退不回待审阅。
/// 口径收进共享的 <see cref="CandidateEdit"/>（不依赖 WinForms、与测试工程共享编译）：
/// 判定（能不能改、能不能保存、能不能撤销）与请求构造（接受 / 保存修改 / 撤销接受）各一处，
/// 写入仍经 DraftService.UpdateAsync 单一口径（占位文字、超长、注释标记、待确认问题需先回答、
/// 覆盖已有注释需勾选等校验全在服务层，本层不复制）。真值表在 Linux 真执行，界面接线以源码切片
/// 与行首正则钉住（被注释掉也算删除）；真机观感随 G-D5 复核。
/// </summary>
public class CandidateEditTests
{
    private static CommentCandidate Candidate(string action = "set", string reviewState = "pending") => new()
    {
        Id = "c1",
        SlotId = "s1",
        Text = "/// 订单编号。",
        Action = action,
        ReviewState = reviewState,
    };

    // ---------- 判定：能不能改、能不能保存、能不能撤销 ----------

    [Fact]
    public void CanEditText_OnlyForSetCandidates()
    {
        // "set" 的候选文字就是要写进源码的注释；"removeStale" 的文字是那条删除动作的说明，
        // 与冻结页面同一口径（那一格禁用）。
        Assert.True(CandidateEdit.CanEditText(Candidate("set")));
        Assert.False(CandidateEdit.CanEditText(Candidate("removeStale")));
    }

    [Theory]
    [InlineData("set", true, true)]
    [InlineData("set", false, false)]
    [InlineData("removeStale", true, false)]
    [InlineData("removeStale", false, false)]
    public void CanSaveText_RequiresEditableTextAndAnExistingSlot(string action, bool slotExists, bool expected)
    {
        // 位置已不在本次扫描结果里时保存必然被拒（服务按位置校验），按钮如实禁用而不是点了没反应。
        Assert.Equal(expected, CandidateEdit.CanSaveText(Candidate(action), slotExists));
    }

    [Theory]
    [InlineData("accepted", true, true)]
    [InlineData("accepted", false, false)]
    [InlineData("pending", true, false)]
    [InlineData("deferred", true, false)]
    [InlineData("rejected", true, false)]
    [InlineData("stale", true, false)]
    public void CanUndoAccept_OnlyForAcceptedCandidatesWithAnExistingSlot(string reviewState, bool slotExists, bool expected)
    {
        // 其余状态没有可撤销的接受；位置已不存在时服务会拒，按钮同样如实禁用。
        Assert.Equal(expected, CandidateEdit.CanUndoAccept(Candidate(reviewState: reviewState), slotExists));
    }

    // ---------- 请求构造：三个动作各一份，界面不就地拼 ----------

    [Fact]
    public void AcceptRequest_CarriesTheEditedTextForSetCandidates()
    {
        // 接受时连文字一起提交：用户可能刚在那一格里改过（与冻结页面 acceptBody 同一份）。
        var request = CandidateEdit.AcceptRequest(Candidate("set"), "/// 订单编号（改过的）。", "已确认", true);
        Assert.Equal("accepted", request.ReviewState);
        Assert.Equal("/// 订单编号（改过的）。", request.Text);
        Assert.Equal("已确认", request.Answers);
        Assert.True(request.OverwriteExisting);
    }

    [Fact]
    public void AcceptRequest_DoesNotSubmitTextForRemoveStaleCandidates()
    {
        // 服务只对 "set" 应用文字；删除失效标签的候选提交文字是无声的无效字段。
        var request = CandidateEdit.AcceptRequest(Candidate("removeStale"), "随便什么", null, null);
        Assert.Equal("accepted", request.ReviewState);
        Assert.Null(request.Text);
    }

    [Fact]
    public void AcceptRequest_LeavesUntouchedFieldsAlone()
    {
        // null 表示「本次没有改动」，服务按「不传即不改」处理——不把没动过的回答写成空串。
        var request = CandidateEdit.AcceptRequest(Candidate("set"), "/// 订单编号。", null, null);
        Assert.Null(request.Answers);
        Assert.Null(request.OverwriteExisting);
    }

    [Fact]
    public void SaveTextRequest_DoesNotTouchTheReviewState()
    {
        // 改文字不该顺手把一条待审阅的候选变成已接受（与冻结页面「保存修改」同一份）。
        var request = CandidateEdit.SaveTextRequest("/// 订单编号（改过的）。", "已确认", false);
        Assert.Null(request.ReviewState);
        Assert.Equal("/// 订单编号（改过的）。", request.Text);
        Assert.Equal("已确认", request.Answers);
        Assert.False(request.OverwriteExisting);
    }

    [Fact]
    public void UndoAcceptRequest_OnlyReturnsToPending()
    {
        // 退回待审阅：同一位置的其他已接受候选由服务一并退回（服务层单一口径），本层不复制。
        var request = CandidateEdit.UndoAcceptRequest();
        Assert.Equal("pending", request.ReviewState);
        Assert.Null(request.Text);
        Assert.Null(request.Answers);
        Assert.Null(request.OverwriteExisting);
    }

    // ---------- 文字：说明行与完成文案各一份 ----------

    [Fact]
    public void Captions_SayWhatTheBoxIsAndWhyItIsReadOnly()
    {
        // 三种状态三句话，界面不就地写两种说法。
        Assert.Equal(CandidateEdit.TextCaption, CandidateEdit.TextCaptionFor(Candidate("set")));
        Assert.Equal(CandidateEdit.TextCaptionReadOnly, CandidateEdit.TextCaptionFor(Candidate("removeStale")));
        Assert.NotEqual(CandidateEdit.TextCaption, CandidateEdit.TextCaptionReadOnly);
        Assert.NotEqual(CandidateEdit.TextCaptionNoSelection, CandidateEdit.TextCaption);
        // 可编辑那一句要讲清「改完怎么生效」——否则用户改完不知道还要点一下。
        Assert.Contains("保存修改", CandidateEdit.TextCaption, StringComparison.Ordinal);
        // 只读那一句要讲清为什么不能改（删除动作没有可改的说明文字）。
        Assert.Contains("删除失效标签", CandidateEdit.TextCaptionReadOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void DoneTexts_AreDistinctAndSayWhatHappened()
    {
        Assert.NotEqual(CandidateEdit.SaveDoneText, CandidateEdit.UndoDoneText);
        Assert.Contains("保存", CandidateEdit.SaveDoneText, StringComparison.Ordinal);
        // 撤销接受的结果是「回到待审阅」，状态行要说出来（用户下一步就是重新审阅它）。
        Assert.Contains("待审阅", CandidateEdit.UndoDoneText, StringComparison.Ordinal);
    }

    // ---------- 界面接线：两个动作、一格输入、一个异步落点 ----------

    [Fact]
    public void Dialog_CreatesTheTwoActionsThroughTheFactoryAndWiresThem()
    {
        var source = Repo.Source("Desktop", "Native", "CandidatesDialog.cs");
        // 两个动作都是 Ghost 形态（§6：每屏至多一个主按钮，接受才是主按钮）。
        Assert.Matches(@"(?m)^\s*_saveTextButton = Ui\.Ghost\(""保存修改""\);", source);
        Assert.Matches(@"(?m)^\s*_undoButton = Ui\.Ghost\(""撤销接受""\);", source);
        Assert.Matches(@"(?m)^\s*_saveTextButton\.Click \+= \(_, _\) => SaveText\(\);", source);
        Assert.Matches(@"(?m)^\s*_undoButton\.Click \+= \(_, _\) => UndoAccept\(\);", source);
        // 两个按钮都进了动作行（加了按钮却没摆上去，等于点了没反应）。
        Assert.Matches(@"(?m)^\s*actionRow\.Controls\.Add\(_saveTextButton, \d+, 0\);", source);
        Assert.Matches(@"(?m)^\s*actionRow\.Controls\.Add\(_undoButton, \d+, 0\);", source);
    }

    [Fact]
    public void Dialog_GatesTheTwoButtonsThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "CandidatesDialog.cs");
        // 可用性取共享口径，界面不就地写「action == "set"」或「reviewState == "accepted"」。
        Assert.Matches(@"(?m)^\s*_saveTextButton\.Enabled = candidate is not null && CandidateEdit\.CanSaveText\(candidate, slotExists\);", source);
        Assert.Matches(@"(?m)^\s*_undoButton\.Enabled = candidate is not null && CandidateEdit\.CanUndoAccept\(candidate, slotExists\);", source);
        Assert.DoesNotMatch(@"(?m)^\s*_saveTextButton\.Enabled = true;", source);
        Assert.DoesNotMatch(@"(?m)^\s*_undoButton\.Enabled = true;", source);
    }

    [Fact]
    public void Dialog_BuildsEveryRequestThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "CandidatesDialog.cs");
        // 三个请求构造各一处，界面不就地拼请求体。
        Assert.Single(Regex.Matches(source, @"CandidateEdit\.AcceptRequest\("));
        Assert.Single(Regex.Matches(source, @"CandidateEdit\.SaveTextRequest\("));
        Assert.Single(Regex.Matches(source, @"CandidateEdit\.UndoAcceptRequest\("));
        // 「退回待审阅」这个状态字面只在口径文件里出现一次（界面另写一份必然漂移）。
        Assert.DoesNotMatch(@"(?m)ReviewState = ""pending""", source);
        // 候选文字只经口径提交，界面不自己往请求体里塞文字。
        Assert.DoesNotMatch(@"(?m)Text = _text\.Text", source);
    }

    [Fact]
    public void Dialog_EditsTheTextThroughTheFactoryField()
    {
        var source = Repo.Source("Desktop", "Native", "CandidatesDialog.cs");
        // 输入框经工厂出厂（多行、可滚动）并包 1px 描边（§6「输入框」），不裸 new。
        Assert.Matches(@"(?m)^\s*private readonly TextBox _text = Ui\.Input\(multiline: true, scrollBars: ScrollBars\.Vertical\);", source);
        Assert.Matches(@"(?m)^\s*AddBoxRow\(detailGrid, Ui\.Field\(_text\), \d+\);", source);
        // 可编辑与否取共享口径；说明行按候选取那一句。
        Assert.Matches(@"(?m)^\s*_text\.Enabled = CandidateEdit\.CanEditText\(candidate\);", source);
        Assert.Matches(@"(?m)^\s*_textCaption\.Text = CandidateEdit\.TextCaptionFor\(candidate\);", source);
        // 没有选中/提示那一屏：这一格随提示一起收掉，说明行换成「选中一条候选后可编辑」。
        Assert.Equal(2, Regex.Matches(source, @"(?m)^\s*_textCaption\.Text = CandidateEdit\.TextCaptionNoSelection;").Count);
    }

    [Fact]
    public void Dialog_KeepsOneAsyncLandingForAllFiveActions()
    {
        var source = Repo.Source("Desktop", "Native", "CandidatesDialog.cs");
        // 五个动作（三个审阅 + 保存修改 + 撤销接受）共用同一个异步落点：写入、重读、重建、
        // 续接选中与交回键盘只有那一处（两套落点必然漂移，第五十六/五十八轮的口径也只在那一处成立）。
        Assert.Single(Regex.Matches(source, @"private async void RunCandidateUpdate\("));
        Assert.Equal(3, Regex.Matches(source, @"(?m)^\s*RunCandidateUpdate\(").Count);
        // 三个审阅动作经 RunReview 交给落点，自己不再写一套 await。
        var review = Slice(source, "private void RunReview(string reviewState)", "/// <summary>重建后回到刚处理的那一条");
        Assert.Contains("RunCandidateUpdate(update,", review, StringComparison.Ordinal);
        Assert.DoesNotContain("await ", review, StringComparison.Ordinal);
        // 保存修改与撤销接受先过共享判定再落盘（不可用时点了没反应，不如如实禁用）。
        var save = Slice(source, "private void SaveText()", "/// 撤销接受（DESIGN.md §6「候选文字编辑」）");
        Assert.Contains("CandidateEdit.CanSaveText(", save, StringComparison.Ordinal);
        var undo = Slice(source, "private void UndoAccept()", "/// 保存修改与撤销接受共用的落点");
        Assert.Contains("CandidateEdit.CanUndoAccept(", undo, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedCompile_IncludesCandidateEdit()
    {
        // 真值表跑的是主工程同一份代码：共享编译清单里必须有 CandidateEdit.cs。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\CandidateEdit.cs", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_RecordsCandidateTextEditing()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("候选文字编辑", section);
        Assert.Contains("保存修改", section);
        Assert.Contains("撤销接受", section);
        Assert.Contains("CandidateEdit", section);
        var gaps = Repo.DesignSection(8);
        Assert.Contains("候选文字编辑", gaps);
    }

    /// <summary>方法体切片：从签名到下一个成员锚点（锚点缺失即红，防止断言漂移到空切片）。</summary>
    private static string Slice(string source, string signature, string endAnchor)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        var end = source.IndexOf(endAnchor, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"缺少 {signature} 或其后续锚点 {endAnchor}。");
        return source[start..end];
    }
}
