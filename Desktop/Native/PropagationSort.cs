namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 生成文档核对条目清单的列排序键（DESIGN.md §6「列表列排序」，第一百零三轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）、注释候选的候选清单（第一百轮）、响应契约的契约清单（第一百零一轮）
/// 与注释缺口清单（第一百零二轮）点击列头即按该列排序（同一列再点反向、换一列从升序开始、表头带方向标记），
/// 而生成文档核对的条目清单（位置、标注、源码、XML、Swagger 五列）点击列头没有任何反应——
/// 这一屏存在的理由正是「注释有没有真的写进源码、出现在 XML、被 Swagger 读到」，
/// 条目按「注释位置 × 三环」展开，真实项目几十上百条，「把 XML 未收录的那几条集中看」
/// 「按源码状态排」「把同一个标注的放一起」只能逐行扫；同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 排序状态、方向翻转与表头标记仍取 <see cref="ListSort"/> 那一份（本文件只给「这一列按什么排」），
/// 界面不就地写 switch、不就地拼排序键——排序键与单元格取值同源，避免「显示与排序两套口径」。
/// 键的取法与单元格一致：位置列取 <c>entry.Display</c>（与首格同一份）、
/// 标注列取 <see cref="PropagationEntries.TagText"/>（与第二格同一份）、
/// 三环状态列取 <c>TaskDisplay.PropagationSourceText</c> / <c>PropagationXmlText</c> /
/// <c>PropagationSwaggerText</c>（与那三格同一份中文映射）。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class PropagationSort
{
    /// <summary>
    /// 排序键（与建列顺序一一对应：位置、标注、源码、XML、Swagger）。
    /// 六个参数就是这一行在屏幕上被读作的那五格——调用方传单元格的同一份取值，不另算一套。
    /// 列号落在建列之外交 null——与 <see cref="ListSort.Order"/> 的「缺键视为最小」同一处理，
    /// 不索引不存在的列、也不把邻列的值当成这一列。
    /// </summary>
    public static IComparable? Key(string display, string tagText, string sourceText, string xmlText, string swaggerText, int column) => column switch
    {
        0 => display,
        1 => tagText,
        2 => sourceText,
        3 => xmlText,
        4 => swaggerText,
        _ => null,
    };
}
