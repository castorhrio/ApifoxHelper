using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 证据措辞的诚实性（第一百五十三轮，简报 R6：wine ≠ 真机）。
/// <para>
/// <b>为什么要有这个文件</b>：本仓至今**没有任何 Windows 真机证据**——全部 GUI 证据来自
/// Linux 容器里的 wine 10.0 + Xvfb。但此前 <c>docs/NOTES.md</c> 有一处把 wine 量到的列宽
/// 写成「真机实测」，冒烟器与冒烟用例的类摘要也把 wine 结果写成「真实执行证据」。
/// 这类措辞不会让代码出错，却会让答辩口径失真：读者以为真机已经跑过，于是 G-D5 那道门槛
/// 被一句话悄悄跨过去了。本文件把「wine 证据只能叫 wine 证据」钉成机械断言。
/// </para>
/// <para>
/// 真机到位后怎么办：把真机证据写进 NOTES.md 的「Windows 真机 / wine 模拟器验证清单」B 段，
/// 并**连同本文件一起改**——那时「真机实测」才有出处，这条断言就该换成「真机证据必须带截图路径」。
/// </para>
/// </summary>
public class WordingHonestyTests
{
    [Fact]
    public void Notes_DoNotCallWineResultsRealMachineMeasurements()
    {
        // 本仓没有 Windows 真机证据：任何「真机实测」都是把 wine 结果说成真机。
        var notes = Repo.Source("docs", "NOTES.md");
        Assert.DoesNotContain("真机实测", notes);
    }

    [Fact]
    public void SmokeProgram_CallsItsEvidenceWineEvidence()
    {
        var program = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        Assert.Contains("wine 模拟器跑过一遍", program);
        Assert.DoesNotContain("真机/模拟器跑过一遍", program);
    }

    [Fact]
    public void SmokeTests_CallTheirEvidenceWineEvidence()
    {
        var tests = Repo.Source("ApifoxHelper.Tests", "NativeDialogSmokeTests.cs");
        Assert.Contains("wine 模拟器执行证据", tests);
        Assert.DoesNotContain("真实执行证据", tests);
    }

    // ---------- 真机字样的机械门（第一百五十四轮，控制仓简报 P0-1 的降级手段） ----------

    /// <summary>
    /// 真机证据目录（截图落点）。约定：<c>docs/evidence/real-machine/</c> 下的 <c>*.png</c>。
    /// </summary>
    private static string RealMachineEvidenceDir => Path.Combine(Repo.Root, "docs", "evidence", "real-machine");

    /// <summary>
    /// 「真机」字样必须有出处（第一百五十四轮）。
    /// <para>
    /// 本仓至今没有 Windows 真机证据，所以「真机实测」是禁词（上一条断言）。但禁词只是把话说诚实，
    /// 它挡不住另一种失真：真机真的跑过之后，NOTES.md 里写「真机已验证」却不给截图路径——
    /// 读者仍然无法核对，G-D5 那道门槛还是被一句话跨过去。
    /// </para>
    /// <para>
    /// 这条断言把两种状态都钉住：<b>没有截图</b>时「真机实测 / 真机已验证」仍禁；
    /// <b>有截图</b>时 NOTES.md 必须出现 <c>docs/evidence/real-machine/</c> 的出处路径。
    /// 两个方向都能红：无截图时写「真机实测」→ 红；补了截图却不写出处 → 红。
    /// </para>
    /// </summary>
    [Fact]
    public void RealMachineClaims_RequireEvidencePaths()
    {
        var shots = Directory.Exists(RealMachineEvidenceDir)
            ? Directory.GetFiles(RealMachineEvidenceDir, "*.png")
            : [];
        var notes = Repo.Source("docs", "NOTES.md");

        if (shots.Length == 0)
        {
            // 没有截图：真机字样一律禁（与上一条同口径，这里把禁词表钉全）。
            Assert.DoesNotContain("真机实测", notes);
            Assert.DoesNotContain("真机已验证", notes);
            Assert.DoesNotContain("真机证据已归档", notes);
            return;
        }

        // 有截图：真机字样必须有出处路径，否则读者无法核对。
        Assert.Contains("docs/evidence/real-machine/", notes);
    }

    /// <summary>
    /// 真机不可得时，挂账要写具体（第一百五十四轮）：缺哪台机器、哪几个 DPI、谁负责。
    /// <para>
    /// 存在理由：此前挂账只写「GUI 真机运行与 100%/200% DPI 截图仍未验证」——这句话诚实，
    /// 但读不出「卡在哪、下一步谁做什么」，于是每一轮都原样抄一遍。这条断言要求那一节把三件事
    /// 都写出来，缺一件即红。
    /// </para>
    /// </summary>
    [Fact]
    public void RealMachineGap_NamesTheMachineTheScalesAndTheOwner()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var start = notes.IndexOf("## Windows 真机验证：仍不可得", StringComparison.Ordinal);
        Assert.True(start >= 0, "NOTES.md 缺少「## Windows 真机验证：仍不可得」一节（真机不可得时的挂账落点）。");
        var end = notes.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = end < 0 ? notes[start..] : notes[start..end];

        // ① 缺哪台机器：要写清运行时与版本，不是「一台 Windows」。
        Assert.Contains("Microsoft.WindowsDesktop.App", section);
        // ② 哪几个 DPI：G-D5 要求的三档。
        Assert.Contains("100%", section);
        Assert.Contains("125%", section);
        Assert.Contains("200%", section);
        // ③ 谁负责：写清由谁提供机器、本环境为什么做不了。
        Assert.Contains("负责", section);
        // ④ 截图落点与命名约定（补上证据时照这个放，机械门才认得出）。
        Assert.Contains("docs/evidence/real-machine/", section);
        // ⑤ 到期日与降级方案（第一百五十五轮，简报 P0-1 的降级交付）：真机不可得时不能只写
        // 「仍未验证」——那句话诚实，但读不出「到哪天为止、到那天怎么办」，于是被原样抄了三十多轮。
        // 到期日写死、降级方案写死（README 公开声明 + 答辩按 wine 证据讲 + G-D5 标未达成），缺一件即红。
        Assert.Contains("2026-11-20", section);
        Assert.Contains("降级", section);
        Assert.Contains("README", section);
        Assert.Contains("已知限制", section);
    }

    /// <summary>
    /// 真机缺口要写进 README 的「已知限制」（第一百五十六轮，简报 P0-1 降级方案的前半段）。
    /// <para>
    /// 存在理由：第一百五十五轮把「到期日 2026-11-20 + 降级方案」写进了 <c>docs/NOTES.md</c>，
    /// 但降级方案的第一条是「在 <c>README.md</c> 的『已知限制』一节公开声明」——而 README 里
    /// 一个字都没有。挂账写在开发笔记里，读的人只有维护者；使用者与初审答辩看的是 README，
    /// 那里不写，等于「本版已在 Windows 真机验证过」这句话仍然没人反驳。
    /// </para>
    /// <para>
    /// 这条断言只认 README 的「已知限制」一节（不是全文）：声明必须落在使用者会读到的那一节里，
    /// 写在别处等于没写。缺声明、缺三档 DPI、缺到期日、缺降级指向，任一件即红。
    /// </para>
    /// </summary>
    [Fact]
    public void Readme_StatesTheRealMachineGap()
    {
        var readme = Repo.Source("README.md");
        var start = readme.IndexOf("## 已知限制", StringComparison.Ordinal);
        Assert.True(start >= 0, "README.md 缺少「## 已知限制」一节（真机缺口的公开声明落点）。");
        var end = readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = end < 0 ? readme[start..] : readme[start..end];

        // ① 说清「没在真机上跑过」，而不是含糊的「部分场景未覆盖」。
        Assert.Contains("未在 Windows 真机上运行过", section);
        // ② 说清现有证据是什么（wine 模拟器），不把模拟器证据说成真机证据。
        Assert.Contains("wine", section);
        Assert.Contains("模拟器", section);
        // ③ 说清哪几档 DPI 没复核（G-D5 的三档）。
        Assert.Contains("100%", section);
        Assert.Contains("125%", section);
        Assert.Contains("200%", section);
        // ④ 到期日与降级指向：到哪天为止、到那天怎么办。
        Assert.Contains("2026-11-20", section);
        Assert.Contains("降级", section);
        Assert.Contains("docs/NOTES.md", section);
    }

    /// <summary>
    /// 降级答辩脚本存在即门（第一百六十二轮，简报 P0-1 的降级交付）。
    /// <para>
    /// 存在理由：第一百五十五轮把「到期日 + 降级方案」写进了挂账节，第一百五十六轮把公开声明写进了
    /// README——但降级方案里那句「答辩按 wine 证据逐条讲」此前**只是一句承诺**：真到答辩那天，
    /// 讲的人还得自己从一千六百多行开发笔记里翻「哪一屏的证据在哪一段」。承诺与可执行之间差的就是这份脚本。
    /// </para>
    /// <para>
    /// 这条断言钉住脚本的骨架，缺一件即红：① 脚本节存在；② 到期日与「未达成（环境供给缺口）」写死
    /// （G-D5 不许标「部分达成」）；③ 责任人写死；④ 逐屏表至少 12 行（地板：主链路 16 屏，
    /// 少到这个数以下说明表被删残了）；⑤ 每行都给出证据出处（含「wine」或「源码」字样）——
    /// 没有出处的行等于「我记得跑过」；⑥ 至少一行如实标「源码 + 编译证据」（防止把只有源码证据的屏
    /// 说成 wine 量到，那正是本文件存在的理由）。
    /// </para>
    /// </summary>
    [Fact]
    public void DegradationScript_ExistsAndCoversEveryScreen()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var start = notes.IndexOf("## 降级答辩脚本", StringComparison.Ordinal);
        Assert.True(start >= 0, "NOTES.md 缺少「## 降级答辩脚本」一节（真机不可得时的答辩落点）。");
        var end = notes.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = end < 0 ? notes[start..] : notes[start..end];

        // ② 到期日与 G-D5 的如实标注。
        Assert.Contains("2026-11-20", section);
        Assert.Contains("未达成（环境供给缺口）", section);
        // ③ 责任人。
        Assert.Contains("负责", section);

        // ④⑤ 逐屏表：每行都要有证据出处。
        var rows = section.Split('\n')
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal))
            .Where(line => !line.Contains("---", StringComparison.Ordinal))
            .Where(line => !line.Contains("屏 / 主张", StringComparison.Ordinal))
            .ToList();
        Assert.True(rows.Count >= 12, $"降级答辩脚本只有 {rows.Count} 行逐屏条目（地板 12 行）。");
        var withoutEvidence = rows
            .Where(row => !row.Contains("wine", StringComparison.Ordinal) && !row.Contains("源码", StringComparison.Ordinal))
            .ToList();
        Assert.True(
            withoutEvidence.Count == 0,
            "这些逐屏条目没有给出证据出处（既没写 wine 也没写源码）："
            + string.Join(" / ", withoutEvidence.Select(r => r[..Math.Min(40, r.Length)])));

        // ⑥ 至少一行如实标「源码 + 编译证据」：只有源码证据的屏不许说成 wine 量到。
        Assert.Contains("源码 + 编译证据", section);
    }

    /// <summary>
    /// 说明行证据行必须带「这一档是多少缩放」与结论，且写在判失败之前（第一百六十二轮，简报 P0-2）。
    /// <para>
    /// 存在理由：wine 实测把 <c>LogPixels</c> 改成 120 / 192 之后 <c>Control.DeviceDpi</c> 恒报 96，
    /// 只写 DeviceDpi 的三档证据行会长得一模一样；而「放不下」抛异常在前、证据行在后时，
    /// 失败那一档的数字全丢（200% 档报告里只剩一句「放不下」）。两件事都只有源码切片能钉住。
    /// </para>
    /// </summary>
    [Fact]
    public void SmokeProgram_ReportsTheScaleAndTheVerdictInTheCaptionEvidenceLine()
    {
        var program = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        var start = program.IndexOf("private static void RunCaptionFit", StringComparison.Ordinal);
        Assert.True(start >= 0, "冒烟器里找不到 RunCaptionFit 段。");
        var end = program.IndexOf("private static void RunFullJourney", start, StringComparison.Ordinal);
        var section = end < 0 ? program[start..] : program[start..end];

        // ① 证据行带缩放档（不是只有 DeviceDpi）与结论。
        Assert.Contains("CaptionScalePercent()", section);
        Assert.Contains("结论：", section);
        // ② 证据行写在判失败之前：先 _lines.Add，再 throw。
        var addAt = section.IndexOf("_lines.Add(", StringComparison.Ordinal);
        var throwAt = section.IndexOf("throw new InvalidOperationException", StringComparison.Ordinal);
        Assert.True(addAt >= 0 && throwAt > addAt, "证据行必须写在判失败之前，否则失败那一档的量测数字会丢。");
        // ③ 缩放换算必须经行距比还原字号像素高（直接拿行高除以磅值会得出「125%」这种假数）。
        var scaleStart = program.IndexOf("private static int CaptionScalePercent", StringComparison.Ordinal);
        Assert.True(scaleStart >= 0, "找不到 CaptionScalePercent（缩放换算的单一落点）。");
        var scaleBody = program[scaleStart..program.IndexOf("private static (int Available, string Source) CaptionAvailableWidth", scaleStart, StringComparison.Ordinal)];
        Assert.Contains("GetLineSpacing", scaleBody);
        Assert.Contains("GetEmHeight", scaleBody);
    }

    [Fact]
    public void Checklist_SeparatesRealMachineFromWine()
    {
        // 清单标题必须把两者分开写：一个笼统的「真机/模拟器」标题读起来像「真机也跑过了」。
        var notes = Repo.Source("docs", "NOTES.md");
        Assert.Contains("## Windows 真机 / wine 模拟器验证清单", notes);
        Assert.DoesNotContain("## 真机/模拟器验证清单", notes);
    }
}
