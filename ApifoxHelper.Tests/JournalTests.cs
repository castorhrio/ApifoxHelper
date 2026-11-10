using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Tasks;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 任务日志的写入形状（<see cref="JournalLines"/>）与读法口径（<see cref="JournalEntries"/>）断言。
/// 基线缺陷可核对：<c>SyncExecutor</c> 写意图那一行是 <c>new { at, kind = "intent", planId, action.ActionId, action.Kind, … }</c>，
/// 匿名类型的 <c>kind</c> 与 <c>action.Kind</c> 在 camelCase 序列化下撞成同一个 JSON 名，
/// <c>JsonSerializer</c> 在写第一行时就抛 <c>InvalidOperationException</c>——它抛在写入动作里、只被
/// <c>UserException</c> 接住，同步执行的第一个写入动作就会以未预期异常结束、计划停在 executing。
/// 行形状散在六个匿名对象里，没有任何一处能把它断言住；现每个事件一个工厂方法，形状由本文件逐行序列化断言。
/// 读法（事件名中文、三格文字、未结束动作）在 <see cref="JournalEntries"/> 一处，界面不就地拼。
/// </summary>
public class JournalTests
{
    // ---------- 写入形状：六个事件各一行，字段名互不冲突 ----------

    [Fact]
    public void IntentLine_SerializesWithoutAPropertyNameCollision()
    {
        // 核心回归：这一行此前序列化即抛 InvalidOperationException（kind 与 action.Kind 撞名）。
        var line = JournalLines.IntentLine(At, "plan-1", "a1", "createModel", "POST /orders", 42, "hash");
        var json = JsonSerializer.Serialize(line, JsonDefaults.Compact);
        var node = JsonNode.Parse(json)!;
        Assert.Equal("intent", node["kind"]!.GetValue<string>());
        // 动作类型写在 actionKind 上：事件名已经占着 kind，两个字段撞成同一个 JSON 名会让整行写不出去。
        Assert.Equal("createModel", node["actionKind"]!.GetValue<string>());
        Assert.Equal("a1", node["actionId"]!.GetValue<string>());
        Assert.Equal(42, node["remoteId"]!.GetValue<long>());
    }

    [Fact]
    public void EveryEventLine_HasUniqueJsonNames()
    {
        // 六个事件逐行序列化：字段名不得有两个撞成同一个 JSON 名（撞名即整行写不出去）。
        foreach (var line in AllLines())
        {
            var json = JsonSerializer.Serialize(line, JsonDefaults.Compact);
            var names = Regex.Matches(json, "\"([A-Za-z]+)\":").Select(m => m.Groups[1].Value).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void EveryEventLine_CarriesTheEventNameAndThePlanId()
    {
        // 读法靠 kind 认事件、靠 planId 认计划：六个事件一个都不能少。
        var expected = new[]
        {
            JournalLines.CommentApplyIntent, JournalLines.CommentApplyResult, JournalLines.PlanStart,
            JournalLines.Intent, JournalLines.ActionResult, JournalLines.PlanEnd,
        };
        var actual = AllLines().Select(line => JsonNode.Parse(JsonSerializer.Serialize(line, JsonDefaults.Compact))!)
            .Select(node => node["kind"]!.GetValue<string>())
            .ToList();
        Assert.Equal(expected, actual);
        Assert.Equal(6, expected.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void CommentApplyLines_CarryTheFileAndThePerFileResult()
    {
        var intent = Node(JournalLines.CommentApplyIntentLine(At, "plan-1", "src/A.cs", "before", "after"));
        Assert.Equal("src/A.cs", intent["file"]!.GetValue<string>());
        Assert.Equal("before", intent["beforeHash"]!.GetValue<string>());
        var result = Node(JournalLines.CommentApplyResultLine(At, "plan-1", "src/A.cs", "applied", null));
        Assert.Equal("applied", result["state"]!.GetValue<string>());
        // 空消息不写进这一行（DefaultIgnoreCondition.WhenWritingNull）：读法按缺失处理，不读出一个 null 字面。
        Assert.Null(result["message"]);
    }

    [Fact]
    public void PlanLines_CarryTheFingerprintAndTheFinalStatus()
    {
        var start = Node(JournalLines.PlanStartLine(At, "plan-1", "abcdef0123456789"));
        Assert.Equal("abcdef0123456789", start["hash"]!.GetValue<string>());
        var end = Node(JournalLines.PlanEndLine(At, "plan-1", "partial"));
        Assert.Equal("partial", end["status"]!.GetValue<string>());
    }

    // ---------- 读法：事件名、三格、未结束动作 ----------

    [Theory]
    [InlineData(JournalLines.CommentApplyIntent, "注释回写：准备写入")]
    [InlineData(JournalLines.CommentApplyResult, "注释回写：结果")]
    [InlineData(JournalLines.PlanStart, "同步执行：开始")]
    [InlineData(JournalLines.Intent, "同步执行：准备写入")]
    [InlineData(JournalLines.ActionResult, "同步执行：结果")]
    [InlineData(JournalLines.PlanEnd, "同步执行：结束")]
    public void EventText_MapsEveryWrittenEvent(string kind, string expected)
    {
        Assert.Equal(expected, JournalEntries.EventText(kind));
    }

    [Fact]
    public void EventText_DoesNotPretendToKnowAnUnknownEvent()
    {
        // 认不出的事件名不猜它是什么，也不当成已知事件显示（把原始 kind 带上，便于对照）。
        Assert.Equal(JournalEntries.UnknownEventText, JournalEntries.EventText(null));
        Assert.Equal(JournalEntries.UnknownEventText, JournalEntries.EventText(""));
        Assert.Equal(JournalEntries.UnknownEventText + "（somethingNew）", JournalEntries.EventText("somethingNew"));
    }

    [Fact]
    public void TimeText_ShowsTheLocalTimeToTheSecond()
    {
        var at = new DateTimeOffset(2026, 11, 8, 9, 30, 5, TimeSpan.FromHours(8));
        var expected = at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, JournalEntries.TimeText(Node(JournalLines.PlanStartLine(at, "p", "h"))));
    }

    [Fact]
    public void TimeText_DoesNotPutTheCurrentTimeInPlaceOfAMissingOne()
    {
        // 缺字段、不是时间、半行：一律占位——拿当前时间顶上就是把「不知道什么时候」说成「刚刚」。
        Assert.Equal(JournalEntries.MissingTimeText, JournalEntries.TimeText(JsonNode.Parse("""{"kind":"intent"}""")));
        Assert.Equal(JournalEntries.MissingTimeText, JournalEntries.TimeText(JsonNode.Parse("""{"at":"昨天"}""")));
        Assert.Equal(JournalEntries.MissingTimeText, JournalEntries.TimeText(null));
    }

    [Fact]
    public void SummaryText_SaysWhatEachEventIsAbout()
    {
        Assert.Equal("src/A.cs", JournalEntries.SummaryText(Node(JournalLines.CommentApplyIntentLine(At, "p", "src/A.cs", "b", "a"))));
        Assert.Equal("src/A.cs · 已写入", JournalEntries.SummaryText(Node(JournalLines.CommentApplyResultLine(At, "p", "src/A.cs", "applied", null))));
        Assert.Equal("src/A.cs · 文件已变化 · 目标在确认后被修改", JournalEntries.SummaryText(Node(JournalLines.CommentApplyResultLine(At, "p", "src/A.cs", "conflict", "目标在确认后被修改"))));
        // 计划开始：计划标识 + 指纹前 8 位（这一格用来核对执行的是哪一份计划，不是把整串哈希摊开）。
        Assert.Equal("计划 p · 指纹 abcdef01", JournalEntries.SummaryText(Node(JournalLines.PlanStartLine(At, "p", "abcdef0123456789"))));
        // 写入意图：动作类型 + 资源，有远端 ID 是更新、没有是新建。
        Assert.Equal("更新接口 GET /orders（远端 42）", JournalEntries.SummaryText(Node(JournalLines.IntentLine(At, "p", "a1", "updateEndpoint", "GET /orders", 42, "h"))));
        Assert.Equal("新建模型 Order（新建）", JournalEntries.SummaryText(Node(JournalLines.IntentLine(At, "p", "a1", "createModel", "Order", null, "h"))));
        // 逐动作结果：资源 + 状态文字（与写入清单同一份）+ 新建 ID + 消息。
        Assert.Equal("GET /orders · 已写入并核对", JournalEntries.SummaryText(Node(JournalLines.ActionResultLine(At, "p", "a1", "GET /orders", "verified", null, null))));
        Assert.Equal("Order · 已写入 · 新建 ID 77", JournalEntries.SummaryText(Node(JournalLines.ActionResultLine(At, "p", "a1", "Order", "written", null, 77))));
        // 计划结束：状态文字与同步预览同一份。
        Assert.Equal("部分完成", JournalEntries.SummaryText(Node(JournalLines.PlanEndLine(At, "p", "partial"))));
    }

    [Fact]
    public void Row_CarriesTheThreeCellsInScreenOrder()
    {
        var cells = JournalEntries.Row(Node(JournalLines.ActionResultLine(At, "p", "a1", "GET /orders", "verified", null, null)));
        Assert.Equal(3, cells.Length);
        Assert.Equal("同步执行：结果", cells[1]);
        Assert.Equal("GET /orders · 已写入并核对", cells[2]);
    }

    [Theory]
    [InlineData(0, "记录 0 条")]
    [InlineData(7, "记录 7 条")]
    public void CountsLine_CountsTheWholeJournal(int count, string expected)
    {
        Assert.Equal(expected, JournalEntries.CountsLine(count));
    }

    [Fact]
    public void UnfinishedActions_FindsIntentsWithoutAResult()
    {
        // 意图先落盘、结果随后追加：只有意图的那几个动作说明进程在写入途中中断过，远端结果未知。
        var lines = new[]
        {
            Node(JournalLines.PlanStartLine(At, "p", "h")),
            Node(JournalLines.IntentLine(At, "p", "a1", "createModel", "Order", null, "h")),
            Node(JournalLines.ActionResultLine(At, "p", "a1", "Order", "verified", null, 7)),
            Node(JournalLines.IntentLine(At, "p", "a2", "createEndpoint", "GET /orders", null, "h")),
            Node(JournalLines.IntentLine(At, "p", "a3", "updateEndpoint", "GET /users", 9, "h")),
            Node(JournalLines.ActionResultLine(At, "p", "a3", "GET /users", "writeFailed", "写入失败", null)),
        };
        var unfinished = JournalEntries.UnfinishedActions(lines);
        Assert.Equal(["a2"], unfinished.Select(action => action.ActionId));
        // 动作类型与资源一并带出来：那句话要说出「是哪个动作」，光有标识用户认不出。
        Assert.Equal("createEndpoint", unfinished[0].ActionKind);
        Assert.Equal("GET /orders", unfinished[0].ResourceKey);
    }

    [Fact]
    public void UnfinishedActions_IgnoresLinesWithoutAnActionId()
    {
        // 半行、字段缺失的意图行不列进来：不拿空串当动作名。
        var lines = new[]
        {
            JsonNode.Parse("""{"kind":"intent"}"""),
            Node(JournalLines.IntentLine(At, "p", "", "createModel", "Order", null, "h")),
        };
        Assert.Empty(JournalEntries.UnfinishedActions(lines));
    }

    [Fact]
    public void UnfinishedCaption_IsSilentWhenThereIsNothingToSay()
    {
        // 没有这样的动作时给空串：界面据此不显示这一行，不显示一句「没有」。
        Assert.Equal("", JournalEntries.UnfinishedCaption([]));
        var caption = JournalEntries.UnfinishedCaption(
        [
            new JournalEntries.UnfinishedAction("a2", "createEndpoint", "GET /orders"),
            new JournalEntries.UnfinishedAction("a5", "updateModel", "Order"),
        ]);
        Assert.Contains("2 个动作", caption, StringComparison.Ordinal);
        // 列出来的是动作的名字（类型 + 资源），不是内部动作标识——用户拿标识做不了任何事。
        Assert.Contains("新建接口 GET /orders、更新模型 Order", caption, StringComparison.Ordinal);
        Assert.DoesNotContain("a2", caption, StringComparison.Ordinal);
        Assert.DoesNotContain("a5", caption, StringComparison.Ordinal);
        // 说的是「结果未知」而不是「失败」：没有结果记录不等于没写进去，重发之前要先重新检查。
        Assert.Contains("远端结果未知", caption, StringComparison.Ordinal);
        Assert.DoesNotContain("失败", caption, StringComparison.Ordinal);
        // 下一步仍在句子里（§7「禁用不得移除下一步」）。
        Assert.Contains("重新检查", caption, StringComparison.Ordinal);
    }

    [Fact]
    public void UnfinishedActionText_NamesTheActionByKindAndResource()
    {
        Assert.Equal("新建接口 GET /orders", JournalEntries.UnfinishedActionText(new JournalEntries.UnfinishedAction("a2", "createEndpoint", "GET /orders")));
        // 认不出的动作类型按原值显示（与写入清单同一口径），不猜它是什么。
        Assert.Equal("somethingNew GET /orders", JournalEntries.UnfinishedActionText(new JournalEntries.UnfinishedAction("a2", "somethingNew", "GET /orders")));
        // 缺一段时不留下多余的空格。
        Assert.Equal("GET /orders", JournalEntries.UnfinishedActionText(new JournalEntries.UnfinishedAction("a2", "", "GET /orders")));
        Assert.Equal("新建模型", JournalEntries.UnfinishedActionText(new JournalEntries.UnfinishedAction("a2", "createModel", "")));
        // 两样都没记（半行）：如实说没记，不拿内部动作标识顶上。
        Assert.Equal(JournalEntries.UnknownActionText, JournalEntries.UnfinishedActionText(new JournalEntries.UnfinishedAction("a2", "", "")));
    }

    [Fact]
    public void Row_MarksTheUnfinishedIntentInPlace()
    {
        // 清单上标出来：这一屏要回答「做到哪一步」，而「这一步没有结果」正是清单上缺的那一行。
        var intent = Node(JournalLines.IntentLine(At, "p", "a2", "createEndpoint", "GET /orders", null, "h"));
        var marked = JournalEntries.Row(intent, new HashSet<string>(["a2"], StringComparer.Ordinal));
        Assert.Equal("新建接口 GET /orders（新建） · " + JournalEntries.UnfinishedMark, marked[2]);
        // 有结果的那一行不标（集合里没有它）。
        var finished = Node(JournalLines.IntentLine(At, "p", "a1", "createModel", "Order", null, "h"));
        Assert.Equal("新建模型 Order（新建）", JournalEntries.Row(finished, new HashSet<string>(["a2"], StringComparer.Ordinal))[2]);
        // 结果行本身不标：标记讲的是「只有意图」。
        var result = Node(JournalLines.ActionResultLine(At, "p", "a2", "GET /orders", "verified", null, null));
        Assert.Equal("GET /orders · 已写入并核对", JournalEntries.Row(result, new HashSet<string>(["a2"], StringComparer.Ordinal))[2]);
        // 不传集合时与 SummaryText 逐字相同（其余调用点不受影响）。
        Assert.Equal(JournalEntries.SummaryText(intent), JournalEntries.Row(intent)[2]);
    }

    // ---------- 接线：写入点与读取点各一处 ----------

    [Fact]
    public void WritePoints_GoThroughTheSharedLineFactories()
    {
        var executor = Repo.Source("Sync", "SyncExecutor.cs");
        Assert.Contains("JournalLines.PlanStartLine(", executor, StringComparison.Ordinal);
        Assert.Contains("JournalLines.IntentLine(", executor, StringComparison.Ordinal);
        Assert.Contains("JournalLines.ActionResultLine(", executor, StringComparison.Ordinal);
        Assert.Contains("JournalLines.PlanEndLine(", executor, StringComparison.Ordinal);
        // 就地拼匿名对象的老形状不得回来（撞名那一行正是这么写出来的）。
        Assert.DoesNotContain("AppendJsonLine(journal, new {", executor, StringComparison.Ordinal);

        var comments = Repo.Source("Comments", "CommentPlanService.cs");
        Assert.Contains("JournalLines.CommentApplyIntentLine(", comments, StringComparison.Ordinal);
        Assert.Contains("JournalLines.CommentApplyResultLine(", comments, StringComparison.Ordinal);
        Assert.DoesNotContain("AppendJsonLine(journal, new {", comments, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_TakesEveryTextFromTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "JournalDialog.cs");
        Assert.Matches(@"(?m)^\s*var cells = JournalEntries\.Row\(line, _unfinishedIds\);$", source);
        Assert.Matches(@"(?m)^\s*_counts\.Text = JournalEntries\.CountsLine\(lines\.Count\);$", source);
        Assert.Matches(@"(?m)^\s*var unfinished = JournalEntries\.UnfinishedActions\(lines\);$", source);
        Assert.Matches(@"(?m)^\s*var unfinishedText = JournalEntries\.UnfinishedCaption\(unfinished\);$", source);
        // 未结束动作的标识只用来在清单上认出那几行（不上屏幕），集合从同一份 UnfinishedActions 来。
        Assert.Matches(@"(?m)^\s*_unfinishedIds = unfinished\.Select\(action => action\.ActionId\)\.ToHashSet\(StringComparer\.Ordinal\);$", source);
        Assert.Matches(@"(?m)^\s*var caption = Ui\.Caption\(JournalEntries\.Caption\);$", source);
        Assert.Matches(@"(?m)^\s*ShowHint\(JournalEntries\.EmptyText, UiTokens\.Muted\);$", source);
        // 事件名与状态文字不在界面就地拼（口径文件里各一份）。
        Assert.DoesNotContain("同步执行：", source, StringComparison.Ordinal);
        Assert.DoesNotContain("注释回写：", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PlanActionStateText", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_SelectsTheLastRecordOnOpen()
    {
        // 这一屏要回答的是「做到哪一步」，最后一行就是答案；选中经 Ui.SelectItem 唯一入口。
        // 第八十四轮起清单重建收进 PopulateList（关键词框输入即筛选），选中落点随之搬进那里：
        // 没有选中时（打开）落在最后一行，有选中时按行载荷恢复（筛一下再筛回来仍停在原来那一步）。
        var source = Repo.Source("Desktop", "Native", "JournalDialog.cs");
        var body = MethodSlice(source, "private void PopulateList");
        Assert.Single(Regex.Matches(body, @"(?m)^\s*Ui\.SelectItem\(target \?\? _list\.Items\[\^1\]\);$"));
        var filled = body.IndexOf("_list.Items.Add(row);", StringComparison.Ordinal);
        var selected = body.IndexOf("Ui.SelectItem(target ?? _list.Items[^1]);", StringComparison.Ordinal);
        Assert.True(filled >= 0 && selected > filled, "选中要排在清单填充之后。");
        // 空清单与读取失败两支都在 OnLoad 里返回，不索引不存在的最后一行；重建里另有一道行数守卫。
        var onLoad = MethodSlice(source, "private void LoadJournal(");
        var emptyBranch = onLoad.IndexOf("if (lines.Count == 0)", StringComparison.Ordinal);
        var populate = onLoad.IndexOf("PopulateList();", StringComparison.Ordinal);
        Assert.True(emptyBranch >= 0 && populate > emptyBranch, "空清单分支要排在重建之前。");
        Assert.Contains("return;", onLoad[emptyBranch..populate], StringComparison.Ordinal);
        Assert.Contains("if (_list.Items.Count == 0) return;", body, StringComparison.Ordinal);
        // 不自己写选中/焦点/滚动（各有唯一入口）。
        Assert.DoesNotContain(".Selected = true", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_IsReadOnlyAndDeclaresItsIdentityColumn()
    {
        var source = Repo.Source("Desktop", "Native", "JournalDialog.cs");
        // 只读：不写任务数据、不重发动作、不碰服务层。
        // 按代码行判：类注释里点名写入点（CommentPlanService / SyncExecutor）是在说明这份日志由谁写，不是界面在调用它。
        var code = CodeLines(source);
        Assert.DoesNotContain("WriteJson", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AppendJsonLine", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SyncExecutor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CommentPlanService", code, StringComparison.Ordinal);
        // 首列是时间、第 2 列是事件名，这一行在屏幕上被读作的名字是「摘要」，故声明第 3 列。
        Assert.Matches(@"(?m)^\s*private const int SummaryColumn = 2;$", source);
        Assert.Contains("identityColumn: SummaryColumn", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TasksPage_OpensTheJournalFromTheRemoteSection()
    {
        var source = Repo.Source("Desktop", "Native", "TasksPage.cs");
        Assert.Matches(@"(?m)^\s*_journalButton = Ui\.Ghost\(""查看任务日志""\);$", source);
        Assert.Matches(@"(?m)^\s*_journalButton\.Click \+= \(_, _\) => ShowJournal\(\);$", source);
        Assert.Matches(@"(?m)^\s*_remoteActions\.Controls\.Add\(_journalButton\);$", source);
        // 有记录才可点（与「查看构建输出」同一门控形状），没有记录时按钮灰着而不是点开一屏空白。
        Assert.Matches(@"(?m)^\s*_journalButton\.Enabled = _selectedTask is not null && File\.Exists\(_tasks\.FilePath\(_selectedTask\.Id, TaskFiles\.Journal\)\);$", source);
        Assert.Matches(@"(?m)^\s*using var dialog = new JournalDialog\(_tasks, _selectedTask\);$", source);
    }

    // ---------- 共享编译与设计基线 ----------

    [Fact]
    public void SharedCompile_IncludesBothHalves()
    {
        // 真值表跑的是主工程同一份代码：写入形状与读法口径都在共享编译清单里。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Tasks\JournalLines.cs", csproj);
        Assert.Contains(@"..\Desktop\Native\JournalEntries.cs", csproj);
    }

    [Fact]
    public void Rule_LivesWithoutWinForms()
    {
        var rule = Repo.Source("Desktop", "Native", "JournalEntries.cs");
        Assert.DoesNotContain("System.Windows.Forms", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("ListView", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_RecordsTheSlice()
    {
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 任务日志（§6） \|", ledger);
        // 没做的事不能被写成做了：这一屏不加关键词框、不重发动作、不改写入语义。
        var journalRow = Slice(ledger, "| 任务日志（§6） |", "\n| 任务日志的未结束动作（§6） |");
        Assert.Contains("第八十二轮", journalRow);
        Assert.Contains("不加关键词框", journalRow);
        Assert.Contains("不重发", journalRow);
        // 第八十三轮：那句话说出是哪个动作、清单在原处标出来，内部动作标识不上屏幕。
        var unfinishedRow = Slice(ledger, "| 任务日志的未结束动作（§6） |", "\n| 任务日志的关键词框（§6） |");
        Assert.Contains("第八十三轮", unfinishedRow);
        Assert.Contains("结果未知", unfinishedRow);
        Assert.Contains("内部动作标识", unfinishedRow);
        // 第八十四轮：记录清单的关键词框与三个手势（上一轮「不加关键词框」的挂账在此收口）。
        var keywordRow = Slice(ledger, "| 任务日志的关键词框（§6） |", "\n| 输入框（§6） |");
        Assert.Contains("第八十四轮", keywordRow);
        Assert.Contains("JournalSearchPlaceholder", keywordRow);
        Assert.Contains("结果未知", keywordRow);
        Assert.Contains("G-D5", keywordRow);
    }

    private static readonly DateTimeOffset At = new(2026, 11, 8, 9, 30, 5, TimeSpan.FromHours(8));

    private static IEnumerable<object> AllLines() =>
    [
        JournalLines.CommentApplyIntentLine(At, "p", "src/A.cs", "b", "a"),
        JournalLines.CommentApplyResultLine(At, "p", "src/A.cs", "applied", null),
        JournalLines.PlanStartLine(At, "p", "h"),
        JournalLines.IntentLine(At, "p", "a1", "createModel", "Order", null, "h"),
        JournalLines.ActionResultLine(At, "p", "a1", "Order", "verified", null, 7),
        JournalLines.PlanEndLine(At, "p", "verified"),
    ];

    /// <summary>写入点写出的那一行 → 序列化 → 解析：读法跑的是真实落盘形状，不是手搓的 JSON。</summary>
    private static JsonNode Node(object line) => JsonNode.Parse(JsonSerializer.Serialize(line, JsonDefaults.Compact))!;

    /// <summary>源码里的代码行（注释一律不算，口径在 CodeScan 一份）：注释里点名写入点是说明理由，不是界面在调用它。</summary>
    private static string CodeLines(string source) => CodeScan.Text(source);

    private static string MethodSlice(string source, string start)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf("\n    }", from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"{start} 找不到收尾大括号。");
        return source[from..to];
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
