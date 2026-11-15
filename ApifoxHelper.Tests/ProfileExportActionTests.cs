using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Infrastructure;
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
/// <b>本文件判三件事</b>：① 判定本身（写出去的字节永远取自磁盘那一份、文件名与 REST 下载同一口径、
/// 状态行点名导出的是哪一版并如实说未保存改动不在其中）；② <b>页面真的经过这里</b>——
/// <c>ProjectsPage</c> 的导出路径必须调 <see cref="ProfileExportAction.Plan"/>、第一个实参必须是
/// <c>_profiles.Get(...)</c> 读回来的那一份，且不再自己拼字节与状态行（对账门：判的是「被承诺的实物」，
/// 不是判据判自己）；③ 配置已被删除时给出的是 <see cref="UserException"/> 那一句而不是 NRE
/// （第三百三十二轮，简报 P0-2：先查证 <see cref="ProfileStore.Get"/> 的契约，再据实钉住）。
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
    /// 配置已被删除时，导出路径给出的是 <see cref="UserException"/> 那一句，不是 NRE（第三百三十二轮，简报 P0-2）。
    /// <para>
    /// <b>为什么要有这一条</b>：<c>ExportProfile</c> 里那句 <c>_profiles.Get(_profile.Id)</c> 的契约此前
    /// 没有任何断言——<see cref="ProfileExportAction.Plan"/> 内部三处调 <see cref="ProfileExport.Render"/>，
    /// 若 <c>Get</c> 返回 <c>null</c>，用户点「导出配置」得到的会是一句 NRE（「未将对象引用设置到对象的实例」），
    /// 而不是「项目配置不存在。」。**先查证再动手**：<see cref="ProfileStore.Get"/> 的契约是
    /// <b>抛 <see cref="UserException"/>（404）</b>，不返回 <c>null</c>——所以这里钉的是「抛的是那一句」，
    /// 不是给 <c>Plan</c> 补一条 null 分支（补了反而会把一个不会发生的状态写进产品）。
    /// </para>
    /// <para>
    /// 两半一起判：① 契约本身（真的建一份配置、真的删掉它、真的读一次）；② 页面那一头接得住——
    /// <c>ExportProfile</c> 的 <c>catch (UserException ex)</c> 排在兜底 <c>catch (Exception)</c> 之前，
    /// 且把 <c>ex.Message</c> 原样交给状态行（否则用户看到的是「导出失败：项目配置不存在。」，
    /// 多一层前缀，与别处同一件事的说法不一致）。
    /// </para>
    /// </summary>
    [Fact]
    public void ExportPath_WhenTheSavedProfileIsGone_SaysSoInsteadOfCrashing()
    {
        var root = Path.Combine(Path.GetTempPath(), "apifox-helper-exportgone-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = Path.Combine(root, "ws");
            Directory.CreateDirectory(workspace);
            File.WriteAllText(Path.Combine(workspace, "Api.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var store = new ProfileStore(new FileStore(root));
            var saved = store.Save(new ProjectProfile
            {
                Name = "订单服务",
                WorkspaceRoot = workspace,
                ApiProject = "Api.csproj",
            }, isNew: true);

            var file = Path.Combine(root, "profiles", saved.Id + ".json");
            Assert.True(File.Exists(file), "保存之后磁盘上应当有这一份配置：夹具没建起来，下面那一发就判不到任何东西。");
            File.Delete(file);

            // ① 契约：配置没了，Get 抛的是 UserException 那一句（404），不是 NRE、也不是 null。
            var ex = Assert.Throws<UserException>(() => store.Get(saved.Id));
            Assert.Equal("项目配置不存在。", ex.Message);
            Assert.Equal(404, ex.StatusCode);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        // ② 页面那一头接得住：UserException 那一支排在兜底之前，且原样把那一句交给状态行。
        var body = ExportMethodBody(Repo.Source("Desktop", "Native", "ProjectsPage.cs"));
        Assert.NotNull(body);
        Assert.Contains("catch (UserException ex)", body!, StringComparison.Ordinal);
        Assert.Contains("SetStatus(ex.Message", body!, StringComparison.Ordinal);
        Assert.True(
            body!.IndexOf("catch (UserException ex)", StringComparison.Ordinal)
                < body.IndexOf("catch (Exception ex)", StringComparison.Ordinal),
            "ExportProfile 里兜底 catch 排在了 UserException 那一支之前：配置被删掉时用户看到的是"
                + "「导出失败：项目配置不存在。」，而不是别处同一件事那一句。");
    }

    /// <summary>
    /// 上面那条对账门<b>抓不抓得住</b>：四发注入各报一处，合规写法不报。
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

        // ④ 经过口径却传错参数：把屏幕上的草稿当第一个实参传进去（第三百三十二轮，简报 P0-1）。
        // 这一发是前三发抓不住的那一格——口径调了、字节也没自己拼、状态行也来自口径，
        // 唯独第一个实参不是 `_profiles.Get(...)` 读回来的那一份，于是导出的仍是草稿。
        var wrongArg = good.Replace("var plan = ProfileExportAction.Plan(saved, _profile);",
            "var plan = ProfileExportAction.Plan(_profile, _profile);", StringComparison.Ordinal);
        Assert.Contains("第一个实参", ExportPathProblem(wrongArg)!, StringComparison.Ordinal);
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

        // 第一个实参必须是**磁盘上那一份**（`_profiles.Get(...)` 的返回值，或就地调它）。
        // 只判「调了 Plan」不够：`Plan(_profile, _profile)` 同样调了口径、同样不自己拼字节，
        // 但导出的正是屏幕上还没保存的草稿——第三百三十二轮补的就是这一发（简报 P0-1）。
        var planCall = Regex.Match(body, @"ProfileExportAction\.Plan\(\s*(?<arg>[^,)]+)", RegexOptions.CultureInvariant);
        if (!planCall.Success)
        {
            return "导出路径调了 ProfileExportAction.Plan，但判据读不出它的第一个实参："
                + "「导出的是磁盘那一份」这句话就没有落点了。";
        }

        var firstArg = planCall.Groups["arg"].Value.Trim();
        if (!firstArg.StartsWith("_profiles.Get(", StringComparison.Ordinal))
        {
            var fromDisk = Regex
                .Matches(body, @"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*_profiles\.Get\(", RegexOptions.CultureInvariant)
                .Select(m => m.Groups["name"].Value)
                .ToHashSet(StringComparer.Ordinal);
            if (!fromDisk.Contains(firstArg))
            {
                return "ProfileExportAction.Plan 的第一个实参不是磁盘上那一份（不是 _profiles.Get(...) 的返回值）："
                    + "把屏幕上的草稿当第一个实参传进去，导出的就是还没保存的那一份，而状态行仍说「已保存版本」。";
            }
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

    /// <summary>
    /// 取 <c>ExportProfile</c> 方法体（从签名那一行到它自己的收尾大括号）。
    /// <para>
    /// <b>假定（第三百三十二轮，简报 P2-1：只登记，不加门）</b>：本判据按花括号配对取方法体，
    /// 因此<b>假定 <c>ExportProfile</c> 方法体内没有含花括号的字符串字面量</b>（如 <c>SetStatus("{x}")</c>）——
    /// 有的话配对会提前收尾，取到错误的方法体，于是静默判不到本方法里的 <c>ProfileExport.Render</c>。
    /// 将来若真出现这种字面量，须改用 Roslyn 语法解析，而不是在这里加一层转义猜测。
    /// </para>
    /// </summary>
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
