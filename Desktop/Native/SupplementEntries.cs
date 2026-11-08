using ApifoxHelper.Contracts;
using ApifoxHelper.Sync;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 人工补充说明的编辑位与保存切片口径（F10，与冻结页面差异详情的「业务说明补充」同一规则）：
/// 编辑位按检查结果的目标/远端构造——接口标题、接口说明、query/path 参数、请求体、200 响应各一个说明位，
/// 现状文字的取值先后也与冻结页面一致（标题远端优先、说明目标优先）。
/// 保存只替换本接口、本次编辑位的条目：勾选「保留现状」记 KeepAsIs（值为空）；填写了文字记去空白后的值；
/// 两者都空等于未补充，不写条目。其他接口与本接口未编辑位的既有条目（含示例）原样保留。
/// 校验与落盘由 SupplementService 单一口径执行，本文件不复制写入边界。
/// 本文件不依赖 WinForms：以共享源码方式编入测试工程，在 Linux 上断言。
/// </summary>
public static class SupplementEntries
{
    /// <summary>一个说明编辑位：显示标签、任务数据定位（位置 + 字段）与现状文字（输入框的当前值提示）。</summary>
    public sealed record Editor(string Label, string Location, string Field, string CurrentText);

    /// <summary>一个编辑位的用户输入：文本框内容与「保留现状」勾选。</summary>
    public sealed record EditorValue(Editor Editor, string Text, bool Keep);

    /// <summary>
    /// 编辑位清单与冻结页面同一口径：标题与说明总有；query/path 参数、请求体与 200 响应按检查结果的目标构造；
    /// 没有目标结构（仅源码）的接口只有标题与说明两个编辑位。
    /// </summary>
    public static List<Editor> EditorsFor(EndpointCheck endpoint)
    {
        var target = endpoint.Target;
        var remote = endpoint.Remote;
        var editors = new List<Editor>
        {
            new("接口标题", "operation", "name", remote?.Summary ?? target?.Summary ?? ""),
            new("接口说明", "operation", "description", target?.Description ?? remote?.Description ?? ""),
        };
        if (target is null) return editors;
        foreach (var parameter in target.Parameters.Values.Where(p => p.In is "query" or "path"))
        {
            editors.Add(new Editor("参数 " + parameter.Name, parameter.In + "." + parameter.Name, "description", parameter.Description ?? ""));
        }
        if (target.RequestBody is not null)
        {
            editors.Add(new Editor("请求体", "requestBody", "description", target.RequestBody.Description ?? ""));
        }
        if (target.Responses.TryGetValue("200", out var ok))
        {
            editors.Add(new Editor("200 响应", "responses.200", "description", ok.Description ?? ""));
        }
        return editors;
    }

    /// <summary>该接口在本编辑位的已保存条目（决定输入框初始值与勾选状态）。</summary>
    public static SupplementEntry? ExistingEntry(SupplementStore store, string resourceKey, Editor editor) =>
        store.Entries.FirstOrDefault(s => s.ResourceKey == resourceKey
            && s.Location == editor.Location && s.Field == editor.Field);

    /// <summary>
    /// 接口清单的关键词匹配字段（DESIGN.md §6「列表筛选」）：那一行在屏幕上被读作的两格——
    /// 接口身份（方法 + 路径，与首列同一份拼法）与远端名称（第二列，缺失按空处理）。
    /// 「补充」那一列是数字（「N 处」），不进关键词（与接口追踪明细的三个计数列同一取舍）。
    /// 与清单单元格同源，不另造一套字段。
    /// </summary>
    public static string[] KeywordFields(EndpointCheck check) => [check.Method + " " + check.Path, check.RemoteName ?? ""];

    /// <summary>该接口已保存的补充条目数（列表列如实显示；保留现状也算一处）。</summary>
    public static int EndpointEntryCount(SupplementStore store, string resourceKey) =>
        store.Entries.Count(s => s.ResourceKey == resourceKey);

    /// <summary>
    /// 保存本接口的切片：先移除本接口在本次编辑位上的既有条目，再按输入写入（保留现状 → KeepAsIs 且值为空；
    /// 填写文字 → 去空白后的值；两者皆空 → 不写条目）。返回新 store，不改传入对象；
    /// 其他接口与本接口未编辑位的既有条目（含示例）原样保留。
    /// </summary>
    public static SupplementStore MergeEndpoint(SupplementStore store, string resourceKey, IReadOnlyList<EditorValue> values)
    {
        var edited = values.Select(v => v.Editor).ToList();
        var entries = store.Entries
            .Where(s => !(s.ResourceKey == resourceKey && edited.Any(e => e.Location == s.Location && e.Field == s.Field)))
            .ToList();
        foreach (var value in values)
        {
            var text = value.Text.Trim();
            if (value.Keep)
            {
                entries.Add(new SupplementEntry { ResourceKey = resourceKey, Location = value.Editor.Location, Field = value.Editor.Field, Value = null, KeepAsIs = true });
            }
            else if (text.Length > 0)
            {
                entries.Add(new SupplementEntry { ResourceKey = resourceKey, Location = value.Editor.Location, Field = value.Editor.Field, Value = text });
            }
        }
        return new SupplementStore { Entries = entries };
    }

    /// <summary>保存结果文案：内容有变化时说明生效条件（重新检查），无变化时如实说明未修改。</summary>
    public static string SavedText(bool changed) => changed
        ? "已保存：补充内容已写入任务，已有的检查结果与同步计划已过期，重新检查后生效。"
        : "补充内容没有变化，未修改任务。";
}
