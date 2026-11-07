using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「模型服务」对话框（项目配置的注释草稿生成方式与模型服务密钥编辑）的共享口径：
/// 生成方式三档（与冻结页面 GENERATION_MODES 同取值同顺序）、地址/模型/数字字段解析、
/// 密钥占位与结果文案（与冻结页面同一口径）。
/// 越界钳制与 openai 方式的地址/模型必填校验仍由 ProfileStore 单一口径在「保存」时执行，
/// 本文件不复制；密钥的 DPAPI 加密存储仍在 CredentialStore 单一口径。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class GenerationEntries
{
    /// <summary>生成方式三档（与冻结页面同一取值与顺序）。</summary>
    public static readonly (string Value, string Label)[] Modes =
    [
        ("none", "仅规则提取与手工填写"),
        ("openai", "OpenAI 兼容模型服务"),
        ("file", "导出材料交给编码助手"),
    ];

    /// <summary>生成方式下拉框选中项；未知取值返回 -1（对话框按原值追加一项显示，不静默改值）。</summary>
    public static int ModeIndex(string mode)
    {
        for (var i = 0; i < Modes.Length; i++)
        {
            if (Modes[i].Value == mode) return i;
        }
        return -1;
    }

    /// <summary>数字字段留空时的默认值（与冻结页面「+值 || 默认」同一结果）。</summary>
    public const int DefaultTimeoutSeconds = 120;
    public const int DefaultMaxSlotsPerRequest = 30;

    /// <summary>解析后的生成设置；BaseUrl/Model 去空白后为空即 null（与冻结页面「|| null」同一结果）。</summary>
    public sealed record Fields(string? BaseUrl, string? Model, int TimeoutSeconds, int MaxSlotsPerRequest);

    /// <summary>
    /// 解析生成设置字段。数字留空用默认值（与冻结页面一致）；非数字内联拒绝、不静默兜底——
    /// 冻结页面会把非法数字静默变成默认值，原生明确拒绝让用户知道值没有被采纳。
    /// </summary>
    public static Fields ParseFields(string baseUrlText, string modelText, string timeoutText, string batchText) => new(
        TextOrNull(baseUrlText),
        TextOrNull(modelText),
        ParseNumber(timeoutText, "超时秒数", DefaultTimeoutSeconds),
        ParseNumber(batchText, "每批数量", DefaultMaxSlotsPerRequest));

    private static int ParseNumber(string text, string label, int fallback) =>
        text.Trim().Length == 0 ? fallback
        : int.TryParse(text.Trim(), out var value) ? value
        : throw new UserException(label + "应为数字。");

    private static string? TextOrNull(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>把生成设置应用到配置对象；越界钳制与 openai 必填校验仍由 ProfileStore 在「保存」时执行。</summary>
    public static void Apply(ProjectProfile profile, string mode, Fields fields)
    {
        profile.Generation.Mode = mode;
        profile.Generation.BaseUrl = fields.BaseUrl;
        profile.Generation.Model = fields.Model;
        profile.Generation.TimeoutSeconds = fields.TimeoutSeconds;
        profile.Generation.MaxSlotsPerRequest = fields.MaxSlotsPerRequest;
    }

    /// <summary>密钥输入框占位文字（与冻结页面同一口径）。</summary>
    public static string KeyPlaceholder(bool hasKey) => hasKey ? "已保存（输入新值可替换）" : "未保存";

    /// <summary>密钥说明（与冻结页面同一口径）。</summary>
    public const string KeyHint = "用 Windows DPAPI 加密保存在本机，不会显示、导出或写入报告";

    /// <summary>配置尚未保存时密钥不可用的原因（与冻结页面按钮提示同一口径）。</summary>
    public const string KeyNeedsProfile = "先保存配置，再保存密钥。";

    /// <summary>保存密钥后的状态行（与冻结页面提示同一口径）。</summary>
    public static string KeySavedMessage(bool hasKey) => hasKey ? "密钥已加密保存" : "密钥已清除";
}
