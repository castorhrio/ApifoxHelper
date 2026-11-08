using ApifoxHelper.Sync;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 检查结果里数据模型清单的文字口径（DESIGN.md §6「列表筛选」，第八十五轮）：
/// 关键词匹配的是那一行在屏幕上被读作的那三格——模型名、映射、状态。
/// 映射与状态都取 <see cref="TaskDisplay"/> 的同一份映射（与清单单元格同源，两处必然一起漂或一起对），
/// 「用于接口」「范围外影响」是数字、不进关键词（与另十二张长清单「数字列不进关键词」同一取舍）。
/// 这一屏没有筛选下拉（模型清单没有状态筛选），故走只有关键词框的那一支。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class ModelEntries
{
    /// <summary>
    /// 一行模型在关键词里能被找到的那三格：模型名、映射文字、状态文字。
    /// 顺序与清单前三列一致（模型 | 映射 | 状态），界面不另造一套字段。
    /// </summary>
    public static string?[] KeywordFields(ModelCheck model) =>
    [
        model.Name,
        TaskDisplay.MappingStateText(model.MappingState),
        TaskDisplay.ModelStatusText(model.Status),
    ];
}
