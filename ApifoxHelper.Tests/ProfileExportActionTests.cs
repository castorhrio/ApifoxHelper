using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Profiles;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「导出配置（不含凭据）」那颗按钮的口径（第三百三十一轮，具名 pass：clarify）。
/// <para>
/// <b>为什么要有这个文件</b>：这颗按钮的接线从第一百四十八轮起就在，但「点下去导出的是哪一份配置」
/// 从来没有被钉住过——<c>ProfileAnalysisTests</c> 判的是 <see cref="ProfileExport"/> 自己的文件名与字节，
/// 判不到按钮取的是磁盘上那一份还是屏幕上正在编辑的那一份。两种写法在源码上只差一个变量名，
/// 在用户那里差一整次导出：编辑到一半点「导出配置」，拿到的可能是还没保存的草稿。
/// </para>
/// <para>
/// <b>本文件判两件事</b>：① 判定本身（写出去的字节永远取自磁盘那一份、文件名与 REST 下载同一口径、
/// 状态行点名导出的是哪一版并如实说未保存改动不在其中）；② <b>页面真的经过这里</b>——
/// <c>ProjectsPage</c> 的导出路径必须调 <see cref="ProfileExportAction.Plan"/>，且不再自己拼字节与状态行
/// （对账门：判的是「被承诺的实物」，不是判据判自己）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机上另存为对话框长什么样、默认文件名对不对——那要一台 Windows 机器
/// （G-D5 仍 0 台）；也不判导出文件在真机上的落点。这条路径中间隔着一个原生模态对话框，
/// 冒烟器点不到它，所以这里没有对应的冒烟段（理由写在 <see cref="ProfileExportAction"/> 的类注释里）。
/// </para>
/// </summary>
public class ProfileExportActionTests
{
    private static ProjectProfile Saved() => new()
    {
        Id = "pabc123",
        Name = "订单服务",
        WorkspaceRoot = "/repo/orders",
        ApiProject = "orders-api",
        AllowedSourcePaths = ["src/Api"],
    };

    /// <summary>屏幕上的那一份：从磁盘那一份复制出来，再改一个字段（= 有未保存改动）。</summary>
    private static ProjectProfile Edited(ProjectProfile saved)
    {
        var onScreen = System.Text.Json.JsonSerializer.Deserialize<ProjectProfile>(
            System.Text.Json.JsonSerializer.Serialize(saved, JsonDefaults.Options), JsonDefaults.Options)!;
        onScreen.Name = "订单服务（改到一半）";
        return onScreen;
    }

    [Fact]
    public void Plan_WritesTheSavedCopy_NotTheEditsOnScreen()
    {
        var saved = Saved();
        var onScreen = Edited(saved);

        var plan = ProfileExportAction.Plan(saved, onScreen);

        // 写出去的字节就是磁盘那一份的字节：与 REST 下载同一口径，不是屏幕上的草稿。
        Assert.Equal(ProfileExport.Render(saved), plan.Bytes);
        Assert.NotEqual(ProfileExport.Render(onScreen), plan.Bytes);
        Assert.Equal(ProfileExport.FileName(saved), plan.FileName);
        Assert.Equal("application/json", ProfileExport.ContentType);
    }

    [Fact]
    public void Plan_WithUnsavedEdits_SaysTheyAreNotInTheFile()
    {
        var saved = Saved();
        var plan = ProfileExportAction.Plan(saved, Edited(saved));

        // 状态行点名导出的配置，并明说屏幕上的改动不在其中——用户唯一能核对「拿到的是哪一版」的地方。
        Assert.Contains("订单服务", plan.StatusText, StringComparison.Ordinal);
        Assert.Contains("已保存版本", plan.StatusText, StringComparison.Ordinal);
        Assert.Contains("不在其中", plan.StatusText, StringComparison.Ordinal);
        Assert.Contains("模型服务密钥", plan.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_WithoutUnsavedEdits_DoesNotClaimThereAreEdits()
    {
        var saved = Saved();
        var plan = ProfileExportAction.Plan(saved, saved);

        Assert.Contains("订单服务", plan.StatusText, StringComparison.Ordinal);
        Assert.Contains("已保存版本", plan.StatusText, StringComparison.Ordinal);
        // 没有未保存改动时不许说「屏幕上的改动不在其中」：那是给用户添一句不存在的提醒。
        Assert.DoesNotContain("不在其中", plan.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void HasUnsavedEdits_IsTrueOnlyWhenTheRenderedBytesDiffer()
    {
        var saved = Saved();
        Assert.False(ProfileExportAction.HasUnsavedEdits(saved, saved));
        Assert.True(ProfileExportAction.HasUnsavedEdits(Edited(saved), saved));

        // 改回来之后又相等：判的是「现在这一份与磁盘那一份是否一样」，不是「有没有人动过」。
        var back = Edited(saved);
        back.Name = saved.Name;
        Assert.False(ProfileExportAction.HasUnsavedEdits(back, saved));
    }

    /// <summary>
    /// 对账门：<c>ProjectsPage</c> 的导出路径真的经过 <see cref="ProfileExportAction.Plan"/>，
    /// 且不再自己拼字节与状态行。
    /// </summary>
    [Fact]
    public void ExportPath_GoesThroughTheSharedPlan()
    {
        var source = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.Null(ExportPathProblem(source));
    }

    /// <summary>
    /// 上面那条对账门<b>抓不抓得住</b>：三发注入各报一处，合规写法不报。
    /// </summary>
    [Fact]
    public void ExportPathDetector_ReportsTheBreaksItMustCatch()
    {
        // 夹具必须带方法签名：判据是按签名取方法体的，少了它判据会先报「找不到方法」，
        // 三发注入就都变成同一句话，验不出各自那一条。
        const string good = """
            private void ExportProfile()
            {
                try
                {
                    var saved = _profiles.Get(_profile.Id);
                    var plan = ProfileExportAction.Plan(saved, _profile);
                    File.WriteAllBytes(dialog.FileName, plan.Bytes);
                    SetStatus(plan.StatusText, UiTokens.OkText);
                }
                catch (UserException ex)
                {
                    SetStatus(ex.Message, UiTokens.BadText);
                }
            }
            """;
        Assert.Null(ExportPathProblem(good));

        // ① 绕开口径自己拼字节（第一百四十八轮起的那一版写法）。
        var bypass = good.Replace("File.WriteAllBytes(dialog.FileName, plan.Bytes);",
            "File.WriteAllBytes(dialog.FileName, ProfileExport.Render(saved));", StringComparison.Ordinal);
        Assert.Contains("ProfileExport.Render", ExportPathProblem(bypass)!, StringComparison.Ordinal);

        // ② 绕开口径自己拼状态行。
        var ownStatus = good.Replace("SetStatus(plan.StatusText, UiTokens.OkText);",
            "SetStatus(\"配置已导出，模型服务密钥不在其中。\", UiTokens.OkText);", StringComparison.Ordinal);
        Assert.Contains("状态行", ExportPathProblem(ownStatus)!, StringComparison.Ordinal);

        // ③ 干脆不经过口径（导出的是屏幕上那一份）。
        var noPlan = good.Replace("var plan = ProfileExportAction.Plan(saved, _profile);",
            "var plan = new ProfileExportAction.ExportPlan(ProfileExport.FileName(_profile), ProfileExport.Render(_profile), \"已导出。\");",
            StringComparison.Ordinal);
        Assert.Contains("ProfileExportAction.Plan", ExportPathProblem(noPlan)!, StringComparison.Ordinal);
    }

    /// <summary>判据本体：导出路径的源码片段进，问题出（<c>null</c> = 合规）。</summary>
    private static string? ExportPathProblem(string source)
    {
        var body = ExportMethodBody(source);
        if (body is null)
        {
            return "ProjectsPage.cs 里找不到「导出配置」那颗按钮的处理方法（ExportProfile）："
                + "按钮的落点没了，这一条对账门就判不到任何东西。";
        }

        if (!body.Contains("ProfileExportAction.Plan", StringComparison.Ordinal))
        {
            return "导出路径没有经过 ProfileExportAction.Plan：写什么、叫什么名字、写完说什么三件事"
                + "又散回页面里了，屏幕上有未保存改动时用户无从分辨拿到的是哪一版。";
        }

        if (body.Contains("ProfileExport.Render", StringComparison.Ordinal))
        {
            return "导出路径自己拼了字节（ProfileExport.Render）：写出去的那一份不再由口径一处决定，"
                + "「导出的是磁盘那一份」这句话就没有落点了。";
        }

        // 只判**成功那一句**（`UiTokens.OkText` 那一支）：失败那两句（`UserException` 与兜底）
        // 带的是异常自己的话，本来就不该由口径给——把它们一起判红，等于逼着失败路径去编一句
        // 不存在的「已导出」。判据要的是「成功那一句从口径来」，不是「这一屏不许有字面量」。
        if (Regex.IsMatch(body, @"SetStatus\(""[^""]*""\s*,\s*UiTokens\.OkText", RegexOptions.CultureInvariant))
        {
            return "导出路径自己拼了成功那一句状态行（SetStatus 里是就地字面量 + UiTokens.OkText）："
                + "成功那一句必须来自口径，否则「屏幕上的改动不在其中」这句提醒会随写法漂走。";
        }

        return null;
    }

    /// <summary>取 <c>ExportProfile</c> 方法体（从签名那一行到它自己的收尾大括号）。</summary>
    private static string? ExportMethodBody(string source)
    {
        var start = source.IndexOf("private void ExportProfile()", StringComparison.Ordinal);
        if (start < 0) return null;
        var depth = 0;
        var seen = false;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '{') { depth++; seen = true; }
            else if (source[i] == '}')
            {
                depth--;
                if (seen && depth == 0) return source[start..(i + 1)];
            }
        }

        return null;
    }
}
