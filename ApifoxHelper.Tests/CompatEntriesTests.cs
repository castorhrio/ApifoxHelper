using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Sync;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 连接检查与写入兼容探测（F02）的口径（冻结页面与原生对话框同一规则，CompatEntries 单一落点）：
/// 「运行写入探测」在测试项目/测试接口已填且勾选确认时才可用；确认文案如实说明副作用并指明目标；
/// 结果按通过/未通过如实显示，未通过不得显示成已开放写入；徽标一律颜色 + 文字双通道。
/// CapabilityReport 的 JSON 形状断言钉住 /api/capabilities 路由契约（冻结页面读取同一份）。
/// </summary>
public class CompatEntriesTests
{
    [Theory]
    [InlineData("123", "456", true, true)]
    [InlineData("", "456", true, false)]
    [InlineData("12a", "456", true, false)]
    [InlineData("123", "", true, false)]
    [InlineData("123", "45x", true, false)]
    [InlineData("123", "456", false, false)]
    public void CanRun_RequiresNumericTargetsAndConfirm(string projectId, string endpointId, bool confirmed, bool expected) =>
        Assert.Equal(expected, CompatEntries.CanRun(projectId, endpointId, confirmed));

    [Fact]
    public void ProbeConfirmText_DescribesSideEffectsAndTarget()
    {
        var text = CompatEntries.ProbeConfirmText("913", "feature-x", "42");
        Assert.Contains("原样写回说明", text);
        Assert.Contains("临时修改说明并恢复", text);
        Assert.Contains("原样写回参数/请求体/响应", text);
        Assert.Contains("核对其余字段未变化", text);
        Assert.Contains("项目 913", text);
        Assert.Contains("分支 feature-x", text);
        Assert.Contains("接口 ID 42", text);
        Assert.EndsWith("确认执行？", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ProbeConfirmText_BlankBranchMeansMainBranch(string? branch)
    {
        var text = CompatEntries.ProbeConfirmText("913", branch, "42");
        Assert.Contains("主分支", text);
    }

    [Fact]
    public void ResultHeadline_PassedStatesVersionAndOpensWrite()
    {
        var (text, tone) = CompatEntries.ResultHeadline(true, "2.2.6");
        Assert.Equal("ok", tone);
        Assert.Equal("CLI 2.2.6 写入探测通过，已开放同步写入。", text);
    }

    [Fact]
    public void ResultHeadline_FailedKeepsWriteClosedAndNeverClaimsPassed()
    {
        var (text, tone) = CompatEntries.ResultHeadline(false, "2.2.6");
        Assert.Equal("bad", tone);
        Assert.Equal("探测未通过，写入保持关闭。", text);
        Assert.DoesNotContain("已开放", text);
    }

    [Theory]
    [InlineData(true, "通过", "ok")]
    [InlineData(false, "失败", "bad")]
    public void CheckBadge_TextAndToneTogether(bool passed, string text, string tone)
    {
        var badge = CompatEntries.CheckBadge(passed);
        Assert.Equal(text, badge.Text);
        Assert.Equal(tone, badge.Tone);
    }

    [Theory]
    [InlineData(true, "可用", "ok")]
    [InlineData(false, "不可用", "bad")]
    public void CliBadge_TextAndToneTogether(bool found, string text, string tone)
    {
        var badge = CompatEntries.CliBadge(found);
        Assert.Equal(text, badge.Text);
        Assert.Equal(tone, badge.Tone);
    }

    [Theory]
    [InlineData(true, "已登录", "ok")]
    [InlineData(false, "未登录", "bad")]
    public void LoginBadge_TextAndToneTogether(bool loggedIn, string text, string tone)
    {
        var badge = CompatEntries.LoginBadge(loggedIn);
        Assert.Equal(text, badge.Text);
        Assert.Equal(tone, badge.Tone);
    }

    [Theory]
    [InlineData(true, "可读取", "ok")]
    [InlineData(false, "失败", "bad")]
    public void ProjectBadge_TextAndToneTogether(bool readable, string text, string tone)
    {
        var badge = CompatEntries.ProjectBadge(readable);
        Assert.Equal(text, badge.Text);
        Assert.Equal(tone, badge.Tone);
    }

    /// <summary>仅只读是门禁状态而非故障：用 warn 不用 bad（只读检查完全可用）。</summary>
    [Theory]
    [InlineData(true, "已通过探测", "ok")]
    [InlineData(false, "仅只读", "warn")]
    public void WriteCompatBadge_ReadOnlyIsWarnNotBad(bool verified, string text, string tone)
    {
        var badge = CompatEntries.WriteCompatBadge(verified);
        Assert.Equal(text, badge.Text);
        Assert.Equal(tone, badge.Tone);
    }

    [Fact]
    public void JobScope_MatchesFrozenPageRouteKey() =>
        Assert.Equal("profile:p1", CompatEntries.JobScope("p1"));

    [Fact]
    public void UserVisibleTexts_AvoidBannedPhrases()
    {
        var texts = new List<string>
        {
            CompatEntries.ProbeConfirmText("913", "main", "42"),
            CompatEntries.ResultHeadline(true, "2.2.6").Text,
            CompatEntries.ResultHeadline(false, "2.2.6").Text,
            CompatEntries.CheckBadge(true).Text,
            CompatEntries.CheckBadge(false).Text,
            CompatEntries.CliBadge(true).Text,
            CompatEntries.CliBadge(false).Text,
            CompatEntries.LoginBadge(true).Text,
            CompatEntries.LoginBadge(false).Text,
            CompatEntries.ProjectBadge(true).Text,
            CompatEntries.ProjectBadge(false).Text,
            CompatEntries.WriteCompatBadge(true).Text,
            CompatEntries.WriteCompatBadge(false).Text,
        };
        foreach (var text in texts)
        {
            Assert.False(Regex.IsMatch(text, "赋能|闭环|一站式|无缝|智能"), $"文案出现禁词：{text}");
        }
    }

    // ---------- CapabilityReport 的 JSON 形状（/api/capabilities 路由契约） ----------

    /// <summary>Web 端读取 c.cli.* / c.login.* / c.project.* / c.writeCompat.*：键名缺一即页面读取失败。</summary>
    [Fact]
    public void CapabilityReport_SerializesWithRouteContractKeys()
    {
        var report = new CapabilityReport
        {
            Cli = new CapabilityCli { Found = true, Node = "node", Script = "cli.js", Version = "2.2.6" },
            Login = new CapabilityLogin { LoggedIn = true, Message = "已登录" },
            Project = new CapabilityProject { Readable = true, Total = 7, Message = "可读取" },
            WriteCompat = new CapabilityWriteCompat
            {
                Verified = true,
                Record = new WriteCompatRecord
                {
                    CliVersion = "2.2.6",
                    ProjectId = "913",
                    Branch = "main",
                    EndpointId = 42,
                    Passed = true,
                    Checks = [new ProbeCheck { Name = "读取测试接口", Passed = true, Detail = "GET /api/ping" }],
                },
                Message = "CLI 2.2.6 已于 2026-11-07 10:00 通过写入探测",
            },
        };
        var node = JsonDefaults.ToNode(report);
        Assert.NotNull(node);
        Assert.True((bool)node!["cli"]!["found"]!);
        Assert.Equal("2.2.6", node["cli"]!["version"]!.ToString());
        Assert.Equal("node", node["cli"]!["node"]!.ToString());
        Assert.Equal("cli.js", node["cli"]!["script"]!.ToString());
        Assert.True((bool)node["login"]!["loggedIn"]!);
        Assert.Equal("已登录", node["login"]!["message"]!.ToString());
        Assert.True((bool)node["project"]!["readable"]!);
        Assert.Equal(7, (int)node["project"]!["total"]!);
        Assert.True((bool)node["writeCompat"]!["verified"]!);
        Assert.Equal("CLI 2.2.6 已于 2026-11-07 10:00 通过写入探测", node["writeCompat"]!["message"]!.ToString());
        var record = node["writeCompat"]!["record"]!;
        Assert.Equal("2.2.6", record["cliVersion"]!.ToString());
        Assert.Equal("913", record["projectId"]!.ToString());
        Assert.Equal("main", record["branch"]!.ToString());
        Assert.Equal(42, (long)record["endpointId"]!);
        Assert.True((bool)record["passed"]!);
        Assert.Equal("读取测试接口", record["checks"]![0]!["name"]!.ToString());
    }

    /// <summary>未登录/未设置项目时 login、project 为空：路由序列化下整个键省略（WhenWritingNull），冻结页面按 null 分支显示。</summary>
    [Fact]
    public void CapabilityReport_NullLoginAndProjectAreOmitted()
    {
        var report = new CapabilityReport
        {
            Cli = new CapabilityCli { Found = false, Error = "未找到 Apifox CLI" },
            WriteCompat = new CapabilityWriteCompat
            {
                Message = "当前 CLI 版本尚未通过写入探测：只允许只读检查。",
            },
        };
        var node = JsonDefaults.ToNode(report);
        Assert.NotNull(node);
        Assert.False((bool)node!["cli"]!["found"]!);
        Assert.Equal("未找到 Apifox CLI", node["cli"]!["error"]!.ToString());
        Assert.Null(node["login"]);
        Assert.Null(node["project"]);
        Assert.False((bool)node["writeCompat"]!["verified"]!);
        Assert.Null(node["writeCompat"]!["record"]);
        Assert.Equal("当前 CLI 版本尚未通过写入探测：只允许只读检查。", node["writeCompat"]!["message"]!.ToString());
    }
}
