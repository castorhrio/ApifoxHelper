using ApifoxHelper.Api;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// Apifox 目标选择（F01：「读取项目列表」「读取分支」）的共享口径：行解析、可见分支过滤、
/// 列表项文字、选择后回填的值与入口校验文案。冻结页面（loadProjects/loadBranches）与原生
/// 配置对话框同一规则，本文件是唯一落点。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// 列表读取本身仍经既有 ApifoxCli.ListProjectsAsync/ListBranchesAsync 只读链路，本文件不复制；
/// 读取使用已保存配置的 CLI 设置（与冻结页面路由按 profileId 读取同一语义），未保存的配置不能读取。
/// </summary>
public static class ApifoxPicker
{
    /// <summary>项目列表行：id/name/teamName 的读取与 /api/apifox/projects 路由同一字段。</summary>
    public sealed record ProjectRow(string Id, string Name, string? TeamName);

    /// <summary>分支列表行：isMain/isArchived 缺失按 false（与 /api/apifox/branches 路由同一取值）。</summary>
    public sealed record BranchRow(string Id, string Name, bool IsMain, bool IsArchived);

    /// <summary>把 CLI 返回的项目数组解析为行；非对象元素跳过，与路由的 OfType&lt;JsonObject&gt; 同一口径。</summary>
    public static List<ProjectRow> ProjectRows(JsonArray? rows) =>
        (rows ?? []).OfType<JsonObject>()
            .Select(p => new ProjectRow(
                Id: p["id"]?.ToString() ?? "",
                Name: p["name"]?.ToString() ?? "",
                TeamName: p["teamName"]?.ToString() ?? p["team"]?["name"]?.ToString()))
            .ToList();

    /// <summary>把 CLI 返回的分支数组解析为行；非对象元素跳过。</summary>
    public static List<BranchRow> BranchRows(JsonArray? rows) =>
        (rows ?? []).OfType<JsonObject>()
            .Select(b => new BranchRow(
                Id: b["id"]?.ToString() ?? "",
                Name: b["name"]?.ToString() ?? "",
                IsMain: b["isMain"]?.GetValue<bool>() == true,
                IsArchived: b["isArchived"]?.GetValue<bool>() == true))
            .ToList();

    /// <summary>已归档分支不进入选择列表（与冻结页面同一过滤）。</summary>
    public static List<BranchRow> VisibleBranches(IEnumerable<BranchRow> rows) =>
        rows.Where(b => !b.IsArchived).ToList();

    /// <summary>项目下拉项文字：名称（ID），有团队名时以「 · 」缀后（与冻结页面同一格式）。</summary>
    public static string ProjectText(ProjectRow row) =>
        row.Name + "（" + row.Id + "）" + (string.IsNullOrEmpty(row.TeamName) ? "" : " · " + row.TeamName);

    /// <summary>分支下拉项文字：名称（主分支在名称后括注）· ID，与冻结页面同一格式。</summary>
    public static string BranchText(BranchRow row) =>
        row.Name + (row.IsMain ? "（主分支）" : "") + " · ID " + row.Id;

    /// <summary>选择分支后填入分支名的值：主分支留空（留空表示主分支），其余填分支名。</summary>
    public static string BranchValue(BranchRow row) => row.IsMain ? "" : row.Name;

    /// <summary>入口门控：列表按已保存配置读取，未保存（新建）的配置不能读取（与冻结页面同一门控与文案）。</summary>
    public static bool CanLoad(bool saved) => saved;

    /// <summary>未保存时读取列表的状态行文案（与冻结页面 toast 同一字面）。</summary>
    public const string SavedRequired = "请先保存配置";

    /// <summary>读取分支时项目 ID 为空的状态行文案（与冻结页面 toast 同一字面）。</summary>
    public const string ProjectIdRequired = "请先填写项目 ID";

    /// <summary>项目 ID 非数字的状态行文案。<b>值不在这里</b>：单一落点是 <c>Api/ErrorCopy.InvalidProjectIdMessage</c>
    /// （第一百七十四轮，简报 P1-1）——此前这一句与 /api/apifox/branches 路由各写一遍，注释里写着「同文案」
    /// 而实现是两份；现在名字留在这里（界面与真值表都认它），字面只有一处。</summary>
    public const string ProjectIdInvalid = ErrorCopy.InvalidProjectIdMessage;

    /// <summary>分支入口的完整校验：先要求已保存，再要求项目 ID 已填且为数字（与冻结页面同一顺序）。</summary>
    public static string? BranchGate(bool saved, string projectIdText) =>
        !CanLoad(saved) ? SavedRequired
        : projectIdText.Trim().Length == 0 ? ProjectIdRequired
        : !projectIdText.Trim().All(char.IsDigit) ? ProjectIdInvalid
        : null;

    /// <summary>读取项目列表失败的状态行前缀（与 /api/apifox/projects 路由同文案，后接 CLI 失败描述）。</summary>
    public const string ProjectsFailedPrefix = "读取项目列表失败：";

    /// <summary>读取分支失败的状态行前缀（与 /api/apifox/branches 路由同文案，后接 CLI 失败描述）。</summary>
    public const string BranchesFailedPrefix = "读取分支失败：";

    /// <summary>项目列表读取完成的状态行（含已归档分支是否被过滤的如实说明由分支列表另行给出）。</summary>
    public static string ProjectsLoaded(int count) => "已读取 " + count + " 个项目，在「Apifox 项目 ID」下拉列表中选择。";

    /// <summary>分支列表读取完成的状态行；主分支选择后分支名留空需要交代（与「留空表示主分支」口径一致）。</summary>
    public static string BranchesLoaded(int count) => "已读取 " + count + " 个分支（已归档不在列表），在「分支」下拉列表中选择；选「（主分支）」则分支名留空。";
}
