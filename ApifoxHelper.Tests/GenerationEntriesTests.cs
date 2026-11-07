using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「模型服务」（项目配置的注释草稿生成方式与模型服务密钥编辑）的口径断言（GenerationEntries 单一落点，
/// 冻结页面与原生对话框同一取值与文案）：
/// 生成方式三档与冻结页面 GENERATION_MODES 同取值同顺序、未知回退；数字字段留空用默认值（与冻结页面
/// 「+值 || 默认」同一结果）、非数字明确拒绝（冻结页面会静默变成默认值，原生不静默丢值）；
/// Apply 写全部字段；密钥占位/说明/结果文案与冻结页面同一口径。
/// </summary>
public class GenerationEntriesTests
{
    // ── 生成方式三档 ──────────────────────────────────────────────────────

    [Fact]
    public void Modes_ThreeValuesInFrozenPageOrder()
    {
        Assert.Equal(
            [("none", "仅规则提取与手工填写"), ("openai", "OpenAI 兼容模型服务"), ("file", "导出材料交给编码助手")],
            GenerationEntries.Modes);
    }

    [Theory]
    [InlineData("none", 0)]
    [InlineData("openai", 1)]
    [InlineData("file", 2)]
    [InlineData("custom-x", -1)]
    [InlineData("", -1)]
    public void ModeIndex_KnownValuesAndUnknownMinusOne(string mode, int expected) =>
        Assert.Equal(expected, GenerationEntries.ModeIndex(mode));

    // ── 字段解析 ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseFields_TrimsTextsAndParsesNumbers()
    {
        var fields = GenerationEntries.ParseFields(" https://api.example.com/v1 ", " gpt-x ", "90", "20");
        Assert.Equal("https://api.example.com/v1", fields.BaseUrl);
        Assert.Equal("gpt-x", fields.Model);
        Assert.Equal(90, fields.TimeoutSeconds);
        Assert.Equal(20, fields.MaxSlotsPerRequest);
    }

    [Fact]
    public void ParseFields_BlankTextsAreNull()
    {
        var fields = GenerationEntries.ParseFields("  ", "", "90", "20");
        Assert.Null(fields.BaseUrl);
        Assert.Null(fields.Model);
    }

    [Fact]
    public void ParseFields_BlankNumbersUseDefaults()
    {
        // 与冻结页面一致：数字留空（+"" || 默认）取默认值。
        var fields = GenerationEntries.ParseFields("", "", "", "");
        Assert.Equal(GenerationEntries.DefaultTimeoutSeconds, fields.TimeoutSeconds);
        Assert.Equal(120, fields.TimeoutSeconds);
        Assert.Equal(GenerationEntries.DefaultMaxSlotsPerRequest, fields.MaxSlotsPerRequest);
        Assert.Equal(30, fields.MaxSlotsPerRequest);
    }

    [Fact]
    public void ParseFields_NonNumericTimeoutRejectedNotSilentlyDefaulted()
    {
        // 冻结页面会把非法数字静默变成默认值；原生明确拒绝，让用户知道值没有被采纳。
        var ex = Assert.Throws<UserException>(() => GenerationEntries.ParseFields("", "", "abc", "20"));
        Assert.Equal("超时秒数应为数字。", ex.Message);
        // 字段标识定位失败输入框（校验失败描边，DESIGN.md §6）：标识即配置 JSON 属性名。
        Assert.Equal(GenerationEntries.FieldTimeoutSeconds, ex.Field);
        Assert.Equal("timeoutSeconds", ex.Field);
    }

    [Fact]
    public void ParseFields_NonNumericBatchRejectedNotSilentlyDefaulted()
    {
        var ex = Assert.Throws<UserException>(() => GenerationEntries.ParseFields("", "", "90", "1x"));
        Assert.Equal("每批数量应为数字。", ex.Message);
        Assert.Equal(GenerationEntries.FieldMaxSlotsPerRequest, ex.Field);
        Assert.Equal("maxSlotsPerRequest", ex.Field);
    }

    // ── 应用到配置对象 ────────────────────────────────────────────────────

    [Fact]
    public void Apply_WritesAllGenerationFields()
    {
        var profile = new ProjectProfile { Generation = new GenerationSettings { Mode = "none", BaseUrl = "old", Model = "old-model", TimeoutSeconds = 60, MaxSlotsPerRequest = 10 } };
        var fields = new GenerationEntries.Fields("https://api.example.com/v1", "gpt-x", 90, 20);
        GenerationEntries.Apply(profile, "openai", fields);
        Assert.Equal("openai", profile.Generation.Mode);
        Assert.Equal("https://api.example.com/v1", profile.Generation.BaseUrl);
        Assert.Equal("gpt-x", profile.Generation.Model);
        Assert.Equal(90, profile.Generation.TimeoutSeconds);
        Assert.Equal(20, profile.Generation.MaxSlotsPerRequest);
    }

    [Fact]
    public void Apply_NullUrlAndModelClearPreviousValues()
    {
        var profile = new ProjectProfile { Generation = new GenerationSettings { Mode = "openai", BaseUrl = "https://old", Model = "old" } };
        var fields = new GenerationEntries.Fields(null, null, 120, 30);
        GenerationEntries.Apply(profile, "none", fields);
        Assert.Equal("none", profile.Generation.Mode);
        Assert.Null(profile.Generation.BaseUrl);
        Assert.Null(profile.Generation.Model);
    }

    // ── 密钥文案（与冻结页面同一口径） ────────────────────────────────────

    [Theory]
    [InlineData(true, "已保存（输入新值可替换）")]
    [InlineData(false, "未保存")]
    public void KeyPlaceholder_MatchesFrozenPage(bool hasKey, string expected) =>
        Assert.Equal(expected, GenerationEntries.KeyPlaceholder(hasKey));

    [Fact]
    public void KeyHint_StatesDpapiStorageAndNeverShownOrExported()
    {
        Assert.Equal("用 Windows DPAPI 加密保存在本机，不会显示、导出或写入报告", GenerationEntries.KeyHint);
    }

    [Fact]
    public void KeyNeedsProfile_GatesNewProfiles()
    {
        Assert.Equal("先保存配置，再保存密钥。", GenerationEntries.KeyNeedsProfile);
    }

    [Theory]
    [InlineData(true, "密钥已加密保存")]
    [InlineData(false, "密钥已清除")]
    public void KeySavedMessage_MatchesFrozenPage(bool hasKey, string expected) =>
        Assert.Equal(expected, GenerationEntries.KeySavedMessage(hasKey));

    // ── 文案禁词 ──────────────────────────────────────────────────────────

    [Fact]
    public void UserVisibleTexts_NoForbiddenWords()
    {
        var texts = GenerationEntries.Modes.Select(m => m.Label)
            .Append(GenerationEntries.KeyHint)
            .Append(GenerationEntries.KeyNeedsProfile)
            .ToList();
        foreach (var text in texts)
        {
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, "赋能|闭环|一站式|无缝|智能"), $"文案出现禁词：{text}");
        }
    }
}
