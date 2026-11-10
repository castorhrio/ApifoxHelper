using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 说明行是「一行门控提示」，不是段落（第一百五十五轮，具名 pass：distill）。
/// <para>
/// <b>为什么要有这个文件</b>：任务详情区（<c>TasksPage.BuildDetail</c>）的五个分区各挂了一段说明行，
/// 长度分别是 179 / 153 / 91 / 370 / 79 字——而全原生界面其余 40 多处说明行最长 61 字。
/// 那五段把「哪个按钮什么时候可用、为什么、替代路径是什么」写成教程，读起来像文档而不是界面：
/// ① 模板脸：每段都是「动作清单 + 分号串联 + 兜底说明」的同一句式，与内容本身抢注意力；
/// ② 视觉焦点：详情区是主工作区，五段长文把真正的状态行与动作按钮往下推（第三十一轮为此不得不
/// 给详情区加滚动），用户要先读一段说明才看得见下一步；
/// ③ 泛 AI 文案：句子本身没有空话禁词，但「工具默认不构建、不 restore、不启动服务」这类
/// 机制自述属于「说机制不说结果」，与 §6.2 #12 同侧。
/// </para>
/// <para>
/// 本轮把它们蒸馏成一行门控提示（保留「什么时候可用」与一条安全承诺，删掉教程与替代路径清单），
/// 并把「说明行不得是段落」落成机械门：长度上限与句数上限各一条，任何一屏再写回长文即红。
/// 上限取 80 字 / 2 句——现有最长 61 字、最多 2 句，留出余量但不给段落留位置。
/// </para>
/// </summary>
public class CaptionBrevityTests
{
    /// <summary>说明行长度上限（字）。现有最长 61 字，段落从 79 字起。</summary>
    private const int MaxCaptionLength = 80;

    /// <summary>说明行句数上限（按句号计）。一句讲这块框是什么，第二句讲什么时候可用。</summary>
    private const int MaxCaptionSentences = 2;

    /// <summary>界面里所有 <c>Ui.Caption("…")</c> 的字面量（文件 + 行号 + 文字）。</summary>
    private static List<(string File, int Line, string Text)> CaptionLiterals()
    {
        var found = new List<(string, int, string)>();
        foreach (var source in Repo.NativeSources())
        {
            var lines = source.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in Regex.Matches(lines[i], @"Ui\.Caption\(""([^""]*)""\)"))
                {
                    found.Add((source.Name, i + 1, match.Groups[1].Value));
                }
            }
        }
        return found;
    }

    /// <summary>
    /// 说明行不得是段落：长度与句数各一条上限。
    /// <para>
    /// 变异验证（本轮实测）：把任务详情区任意一段改回原来的长文，这条即红并指名文件与行号。
    /// </para>
    /// </summary>
    [Fact]
    public void CaptionLiterals_AreOneLineNotParagraphs()
    {
        var captions = CaptionLiterals();
        // 地板：提取口径失效时断言会静默空转（比没有断言更糟）。
        Assert.True(captions.Count >= 30, $"只提取到 {captions.Count} 处 Ui.Caption 字面量，提取口径可能已失效。");

        var tooLong = captions
            .Where(c => c.Text.Length > MaxCaptionLength)
            .Select(c => $"{c.File}:{c.Line}（{c.Text.Length} 字）")
            .ToList();
        Assert.True(
            tooLong.Count == 0,
            $"以下说明行超过 {MaxCaptionLength} 字，是段落不是一行提示（蒸馏成「什么时候可用」那一句）："
            + string.Join("、", tooLong));

        var tooManySentences = captions
            .Where(c => c.Text.Count(ch => ch == '。') > MaxCaptionSentences)
            .Select(c => $"{c.File}:{c.Line}（{c.Text.Count(ch => ch == '。')} 句）")
            .ToList();
        Assert.True(
            tooManySentences.Count == 0,
            $"以下说明行超过 {MaxCaptionSentences} 句：说明行讲「这块是什么 + 什么时候可用」，教程与替代路径不在这里："
            + string.Join("、", tooManySentences));
    }

    /// <summary>
    /// 任务详情区的说明行必须说清「什么时候可用」（门控），而不是把动作清单与替代路径讲一遍。
    /// <para>
    /// 只判这一屏：它是主工作区，也是本轮蒸馏的落点。判据是「含门控词」——「可用」「需」二者之一，
    /// 说明这一行讲的是下一步的条件；把说明行改成一句纯描述（例如「这里是源码注释区」）即红。
    /// </para>
    /// </summary>
    [Fact]
    public void TaskDetailCaptions_StateTheGateNotTheTutorial()
    {
        var source = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var captions = Regex.Matches(source, @"AddDetailRow\(grid, Ui\.Caption\(""([^""]*)""\)\)")
            .Select(m => m.Groups[1].Value)
            .ToList();
        // 地板：五个分区各一段（源码注释 / 生成文档 / 响应契约 / Apifox 差异与同步 / 报告与交接）。
        Assert.True(captions.Count >= 5, $"任务详情区只找到 {captions.Count} 段说明行，提取口径可能已失效。");

        foreach (var caption in captions)
        {
            Assert.True(caption.Length <= MaxCaptionLength, $"任务详情区说明行 {caption.Length} 字，超过 {MaxCaptionLength} 字：{caption}");
            Assert.True(
                caption.Contains("可用", StringComparison.Ordinal) || caption.Contains("需", StringComparison.Ordinal),
                $"任务详情区说明行没有说清「什么时候可用」：{caption}");
        }
    }

    /// <summary>设计基线要记下这次蒸馏（口径与理由），否则下一轮又会把教程写回说明行。</summary>
    [Fact]
    public void DesignDoc_RecordsTheCaptionBrevityRule()
    {
        var design = Repo.DesignDocText;
        Assert.Contains("说明行长度（§6）", design);
        Assert.Contains("第一百五十五轮", design);
        Assert.Contains("distill", design);
    }
}
