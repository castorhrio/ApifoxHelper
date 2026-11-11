using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「规则提取到底能生成什么」的边界（第一百七十七轮，简报 P0-2）。
/// <para>
/// <b>为什么要有这一节</b>：<c>docs/NOTES.md</c>「初审答辩口径（真机不可得时）」的「AI 必要性」那一块
/// 此前写的是「模型只在生成草稿一处出现，三条替代路径任走一条整条链路照样走完」——这句话在
/// 「与 AI 共生」主题下会被追问「那到底哪一处非模型不可」，而当时的答案只有自述。
/// 真正的答案在源码里：<see cref="Comments.DraftService"/> 的规则提取对「方法 / 类型的用途说明」这一支
/// <b>直接不产出候选</b>（源码注释原文：「方法和类型的用途说明需要语义判断，规则提取不生成」），
/// 而同一个槽位照样进得了发给模型的材料。也就是说：<b>约束类说明规则能写，用途类说明只有模型能写</b>。
/// </para>
/// <para>
/// <b>为什么这一节读源码而不跑一遍</b>（如实写明，不假装是端到端）：<c>Comments/DraftService.cs</c>
/// 编不进测试工程——它引用 <c>Profiles/CredentialStore.cs</c>（<c>ProtectedData</c>，Windows 专用包）
/// 与 <c>Source/SourceScanService.cs</c>（Roslyn）。把这两样拉进 <c>net8.0</c> 测试工程要加两个包引用，
/// 而本轮的纪律是「不装新依赖」。所以这一节判的是<b>分派表达式与材料过滤这两处源码事实</b>，
/// 不是「跑出来的候选集合」——它挡得住「把用途槽位也交给规则提取」这类改动，挡不住「规则提取内部算错」，
/// 后者本来也不在这一节的问题里。**这一节不承诺模型写得好不好**（那要真实模型服务，本仓从未调用过）。
/// </para>
/// <para>
/// <b>这一节判四件事</b>：① 规则提取的槽位分派只覆盖「约束」槽位，用途槽位落到 <c>null</c>；
/// ② 那句「用途说明需要语义判断」的源码注释在（它是这条口径的原文出处）；
/// ③ 用途槽位没有被丢掉——<c>NeedsDraft</c> 收它们、<c>BuildMaterial</c> 按它过滤、<c>GenerateWithModelAsync</c>
/// 把材料交给 <c>ModelClient.CompleteAsync</c>；④ 答辩口径那一块引的正是这一节（文案与代码不脱节）。
/// </para>
/// </summary>
public class RuleExtractionScopeTests
{
    /// <summary>答辩口径里「AI 必要性」那一块的块名（与 <see cref="DefenseNarrativeTests"/> 同一份表）。</summary>
    private const string AiNecessityBlock = "AI 必要性";

    /// <summary>本轮为这条口径新增的 NOTES 小节标题（口径块必须引到它）。</summary>
    private const string ScopeSection = "规则提取的边界：用途说明只有模型能写";

    private static string DraftServiceText() => Repo.Source("Comments", "DraftService.cs");

    /// <summary>取一段源码：从 <paramref name="from"/> 起到 <paramref name="to"/> 之前（不含）。</summary>
    private static string Slice(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Comments/DraftService.cs 里找不到「{from}」——这一节判的分派表达式已经不在原处，先核口径再改门。");
        var end = text.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Comments/DraftService.cs 里「{from}」之后找不到「{to}」——切片边界失效，这一节会判到空区间上。");
        return text[start..end];
    }

    /// <summary>规则提取里逐槽位分派的那一段（<c>GenerateRuleAsync</c> 的循环体）。</summary>
    private static string Dispatch() =>
        Slice(DraftServiceText(), "var candidate = slot.Tag == \"returns\"", "if (candidate is not null) produced.Add(candidate);");

    /// <summary>
    /// ① 分派只覆盖约束槽位：属性 / 记录参数 / 枚举成员 / 方法参数 / returns，其余一律 <c>null</c>。
    /// <para>
    /// 这一条就是答辩「AI 必要性」那一块的实物出处——不是「模型可选」，是方法 summary 与类型 summary
    /// 这两类槽位规则提取<b>不写</b>。把分派改成「summary 也走约束提取」，这一条立刻红。
    /// </para>
    /// </summary>
    [Fact]
    public void RuleExtraction_DispatchCoversConstraintSlotsOnly()
    {
        var dispatch = Dispatch();

        // 三支产出候选：returns 走响应契约，约束槽位走证据，其余 null。
        Assert.Contains("slot.Tag == \"returns\"", dispatch, StringComparison.Ordinal);
        Assert.Contains("ReturnsFromContract(", dispatch, StringComparison.Ordinal);
        Assert.Contains("ConstraintsFromEvidence(", dispatch, StringComparison.Ordinal);
        Assert.Matches(@":\s*null;", dispatch);

        // 约束槽位的封闭清单：多一个 kind 就是多一类「规则能写」的说明，必须同时改这一节与答辩口径。
        Assert.Contains("slot.SymbolKind is \"property\" or \"recordParameter\" or \"enumMember\"", dispatch, StringComparison.Ordinal);
        Assert.Contains("slot.SymbolKind == \"method\" && slot.Tag == \"param\"", dispatch, StringComparison.Ordinal);

        // 用途槽位（方法 / 类型的 summary）在分派里一条产出路径都没有：
        // 分派区间里不许出现 "type" 这个 kind，也不许出现按 summary 标签放行的分支。
        Assert.DoesNotContain("\"type\"", dispatch, StringComparison.Ordinal);
        Assert.DoesNotContain("Tag == \"summary\"", dispatch, StringComparison.Ordinal);
    }

    /// <summary>
    /// ② 那句源码注释在：它是「用途说明需要语义判断」这条口径的原文，删了它这一块就只剩自述。
    /// </summary>
    [Fact]
    public void RuleExtraction_SaysInSourceWhyPurposeSlotsAreSkipped()
    {
        Assert.Contains("方法和类型的用途说明需要语义判断，规则提取不生成", DraftServiceText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// ③ 「规则提取不写」不等于「没人写」：用途槽位照样进得了发给模型的材料。
    /// <para>
    /// 三处一起钉：<c>NeedsDraft</c> 收 missing / empty / inheritDocUnresolved（用途槽位正是 missing）、
    /// <c>BuildMaterial</c> 按 <c>NeedsDraft</c> 过滤、<c>GenerateWithModelAsync</c> 把材料交给
    /// <c>ModelClient.CompleteAsync</c>。把 <c>BuildMaterial</c> 的过滤改成只收约束槽位，这一条立刻红。
    /// </para>
    /// </summary>
    [Fact]
    public void PurposeSlots_AreStillHandedToTheModelMaterial()
    {
        var text = DraftServiceText();

        Assert.Contains("private static bool NeedsDraft(DocSlot slot) => slot.Status is \"missing\" or \"empty\" or \"inheritDocUnresolved\";",
            text, StringComparison.Ordinal);

        var material = Slice(text, "public JsonObject BuildMaterial(", "public static List<string> EvidenceFiles(");
        Assert.Contains("Where(s => NeedsDraft(s) || slotIds.Contains(s.Id))", material, StringComparison.Ordinal);

        var model = Slice(text, "public async Task<object> GenerateWithModelAsync(", "public async Task<DraftImportResult> ImportResultAsync(");
        Assert.Contains("BuildMaterial(taskId, slotIds, out var included)", model, StringComparison.Ordinal);
        Assert.Contains("await model.CompleteAsync(settings, key, Instructions, user, job.Token)", model, StringComparison.Ordinal);
    }

    /// <summary>
    /// ④ 模型那一支要的是「用途」而不是「把标识符翻成中文」：提示词里那句要求还在。
    /// <para>
    /// 这一条把「非模型不可」落到模型被要求做的事上——如果提示词只要求写约束，那模型这一支与规则提取
    /// 就是同一件事的两种实现，答辩口径里「只有模型能写用途」这句话就站不住。
    /// </para>
    /// </summary>
    [Fact]
    public void ModelInstructions_AskForPurposeNotJustConstraints()
    {
        Assert.Contains("要说明用途、取值来源、单位、范围、条件或返回含义", DraftServiceText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑤ 答辩口径那一块引的正是这一节：文案与代码不脱节。
    /// <para>
    /// <see cref="DefenseNarrativeTests"/> 只判「引的小节真的存在」，判不出「引的是不是讲这件事的那一节」。
    /// 这一条补上后半句：AI 必要性那一块的出处里必须出现本轮新增的
    /// <c>docs/NOTES.md</c>「<see cref="ScopeSection"/>」。
    /// </para>
    /// </summary>
    [Fact]
    public void AiNecessityBlock_CitesTheRuleExtractionScopeSection()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var heading = "## " + ScopeSection;
        Assert.Contains(heading, notes, StringComparison.Ordinal);

        // 只在「初审答辩口径」那一节里找：第一百九十四轮在「降级答辩脚本」一节补了五块讲稿模板，
        // 那里也有一行叫「AI 必要性」（同一块的口播版）。不切节的话这里会数到两行，
        // 而它要判的是**口径表**那一行引的是不是这一节。
        var start = notes.IndexOf("## 初审答辩口径", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/NOTES.md 缺少「## 初审答辩口径」一节。");
        var rest = notes[start..];
        var end = rest.IndexOf("\n## ", StringComparison.Ordinal);
        var section = end < 0 ? rest : rest[..end];

        var row = section.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("| " + AiNecessityBlock + " |", StringComparison.Ordinal))
            .ToList();
        Assert.True(row.Count == 1, $"docs/NOTES.md 的答辩口径表里「{AiNecessityBlock}」应当恰好一行，实到 {row.Count} 行。");
        Assert.Contains(ScopeSection, row[0], StringComparison.Ordinal);
    }
}
