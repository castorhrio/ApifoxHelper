namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 注释草稿其余入口（模型生成、材料导出/结果导入、手工填写）的口径（与冻结页面同一规则），不依赖 WinForms，
/// 以共享源码编入测试工程在 Linux 断言：模型生成在配置启用 OpenAI 兼容模型服务后可用，
/// 发送材料前先确认范围（外部发送保留原生确认）；导入与手工的结果按数量与拒绝原因如实报告。
/// 生成、导入与手工校验本身仍由既有 DraftService 单一口径执行，本文件不复制候选校验。
/// </summary>
public static class DraftEntries
{
    /// <summary>材料导出的默认文件名（与冻结页面下载同名）。</summary>
    public static string MaterialFileName(string taskId) => "material-" + taskId + ".json";

    /// <summary>「模型生成候选」可用：配置的草稿生成方式为 OpenAI 兼容模型服务（与冻结页面同一规则）；密钥缺失由服务在执行时拒绝。</summary>
    public static bool CanGenerateModel(string? generationMode) => generationMode == "openai";

    /// <summary>
    /// 确认发送材料对话框正文（与冻结页面同一口径）：发送位置数、涉及源码文件与脱敏说明如实列出；
    /// 文件清单最多显示 20 个，超出一行说明剩余数量（截断如实显示，不静默省略）。
    /// </summary>
    public static string ModelConfirmText(int slots, IReadOnlyList<string> files)
    {
        const int maxFiles = 20;
        var listed = files.Take(maxFiles).ToList();
        var text = "将向配置的模型服务发送 " + slots + " 个注释位置的材料，涉及以下源码文件中的证据片段（已排除配置/凭据文件并脱敏）：\n"
            + (listed.Count == 0 ? "（无证据片段）" : string.Join("\n", listed));
        if (files.Count > listed.Count) text += "\n（其余 " + (files.Count - listed.Count) + " 个文件略）";
        return text + "\n\n模型只返回候选文字与证据编号，不会修改文件或调用 Apifox。候选需逐条审阅后才会进入补丁。\n\n确认发送？";
    }

    /// <summary>材料导出完成的状态行：说明交给编码助手处理后从哪里导回。</summary>
    public static string MaterialExportDoneText(int slots) =>
        "材料已导出（" + slots + " 个注释位置）：交给编码助手按其中的说明生成候选后，点「导入编码助手结果」导回。";

    /// <summary>导入结果的状态行：导入与拒绝数量如实报告，拒绝原因最多列出 3 条（与冻结页面同一口径）。</summary>
    public static string ImportDoneText(int imported, int rejected, IReadOnlyList<string> errors)
    {
        var text = "导入 " + imported + " 条候选，拒绝 " + rejected + " 条";
        var shown = errors.Take(3).ToList();
        if (shown.Count > 0) text += "：" + string.Join("；", shown);
        return text;
    }

    /// <summary>手工候选已添加的状态行：指引到候选审阅逐条确认。</summary>
    public static string ManualAddedText => "已添加候选，请在「查看注释候选」中审阅后接受。";
}
