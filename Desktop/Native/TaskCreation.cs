using ApifoxHelper.Profiles;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 新建检查任务的共享口径：冻结页面 newTask 表单与原生 NewTaskDialog 同一规则，本文件是唯一落点。
/// 「指定接口」多行文本按行解析（与冻结页面 lines() 同一：按行去空白、丢弃空行）；
/// 「GET /path」的规整、去重与非法行丢弃在 TaskStore.Create 单一口径，此处不复制。
/// 「版本备注」留空交 null，由 TaskStore.Create 回退到配置级备注（task.json 落盘值）。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class TaskCreation
{
    /// <summary>「指定接口」提示文字：与冻结页面输入框占位文字同一字面。</summary>
    public const string KeysHint = "每行一个，例如 GET /api/finance/BillList";

    /// <summary>「版本备注」占位文字：与冻结页面输入框占位文字同一字面。</summary>
    public const string NotePlaceholder = "提交号或构建号（可选）";

    /// <summary>「版本备注」留空回退的说明（回退语义在 TaskStore.Create：配置级备注）。</summary>
    public const string NoteFallbackHint = "留空则用项目配置里的版本备注。";

    /// <summary>多行文本按行解析：按 \n 分行、逐行去空白（含行尾 \r）、丢弃空行——与冻结页面 lines() 同一结果。</summary>
    public static List<string> LineValues(string? text) =>
        (text ?? "").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();

    /// <summary>「版本备注」取值：去空白，空白为 null（由 TaskStore.Create 回退到配置级备注）。</summary>
    public static string? VersionNote(string? text)
    {
        var value = (text ?? "").Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// 模块清单的一行文字：「模块名（前缀1、前缀2）」。没有前缀的模块只给名称——只有名称的模块
    /// 是合法配置（<c>ProfileAdvanced.ParseModules</c> 认没有等号的行），此前这类模块在清单上显示成
    /// 「订单（）」：一格空括号既不是信息也不是缺省。勾选交出去的仍是模块名本身（行载荷的 Name），
    /// 这一格只讲屏幕上看到什么。
    /// </summary>
    public static string ModuleRowText(ModuleScope module) =>
        module.PathPrefixes.Count == 0
            ? module.Name
            : module.Name + "（" + string.Join("、", module.PathPrefixes) + "）";
}
