namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 配置编辑对话框（新建 / 编辑）的表单口径：哪些字段是「创建所必需」、哪些是可选的、
/// 高级入口收在哪一行、空标签格怎么占位、未保存时「读取项目列表」怎么说话。
/// <para>
/// <b>为什么要有这个文件</b>（第一百四十八轮）：新建配置对话框在真机截图里是「名称、仓库根目录之后
/// 到 API 项目之间一大段空白，四个高级按钮彼此间距极大，右侧还出滚动条」——用户的原话是「很抽象、没法用」。
/// 根因不是审美：<c>AddRow(grid, "", …)</c> 在空标签格里放的是 <c>new Panel { Margin = 0 }</c>，
/// 而 <see cref="System.Windows.Forms.Panel"/> 的默认尺寸是 200×100——每一行「没有标签」的行
/// （两条说明、一条长说明、四个高级入口、一个导出入口）都被撑到 100px 高，七行就是 700px 的死空白；
/// 同时那条七十余字的长说明在 495px 宽的字段列里放不下，把网格撑宽、右侧出横向滚动条。
/// 这两件事都不是「调一下间距」能解决的，得先把「一行该多高、哪些行属于首用路径」写成可断言的形状。
/// </para>
/// <para>
/// 本文件只放判定与文案，不依赖 WinForms，以共享源码编入测试工程在 Linux 断言；
/// 真实行高与控件位置由冒烟器在 Windows/wine 下量（<c>RunProfileFormLayout</c>）。
/// </para>
/// </summary>
public static class ProfileFormLayout
{
    /// <summary>
    /// 空标签格的占位尺寸（0 = 不占位）。空标签格此前放的是默认尺寸的 <c>Panel</c>（200×100），
    /// 行高因此被撑到 100px；占位必须是零尺寸，行高才由字段列那一格决定。
    /// </summary>
    public const int SpacerSize = 0;

    /// <summary>单行字段行的行内上下留白上限（像素）：DESIGN.md §4 的 Xs + Sm + Md = 4 + 8 + 12。</summary>
    public const int RowPadding = 24;

    /// <summary>分区标题：创建所必需的三项。</summary>
    public const string CoreSectionTitle = "必填";

    /// <summary>分区标题：可选设置（保存后再补也不影响创建）。</summary>
    public const string OptionalSectionTitle = "可选设置（可保存后再补）";

    /// <summary>分区标题：高级设置（四个入口收在同一行，不占首用视线）。</summary>
    public const string AdvancedSectionTitle = "高级设置（可选）";

    /// <summary>
    /// 创建所必需的字段（顺序即界面顺序）：名称、仓库根目录、API 项目。
    /// 这三项填完就能保存——「读取项目列表需要先保存」因此不是死胡同，而是保存之后的事。
    /// </summary>
    public static readonly string[] CoreFields = ["名称", "仓库根目录", "API 项目"];

    /// <summary>可选字段（顺序即界面顺序）：保存不依赖它们，留空即取缺省。</summary>
    public static readonly string[] OptionalFields =
    [
        "允许回写范围",
        "版本备注",
        "Swagger 来源",
        "Swagger 位置",
        "Apifox 项目 ID",
        "分支",
    ];

    /// <summary>高级入口（同一行、同一容器）：模块与模板、序列化与分析、模型服务、导出配置。</summary>
    public static readonly string[] AdvancedEntries =
    [
        "模块与模板…",
        "序列化与分析…",
        "模型服务…",
        "导出配置（不含凭据）",
    ];

    /// <summary>未保存时「读取项目列表」旁边的说明：讲清下一步，而不是等用户点了才报错。</summary>
    public const string ProjectListHintUnsaved = "保存后可读取项目列表；也可以直接填写项目 ID。";

    /// <summary>已保存时「读取项目列表」旁边的说明。</summary>
    public const string ProjectListHintSaved = "按已保存的 CLI 设置读取项目列表。";

    /// <summary>
    /// 「读取项目列表」是否可用：列表按已保存配置的 CLI 设置读取（与冻结页面同一门控）。
    /// 未保存时按钮<b>停用</b>并给出 <see cref="ProjectListHintUnsaved"/>——此前按钮是启用的，
    /// 点下去只得到一句「请先保存配置」，用户得自己猜「那我现在能不能保存」。
    /// </summary>
    public static bool ProjectListEnabled(bool saved) => saved;

    /// <summary>「读取项目列表」旁边的说明（两态各一句，界面不就地拼字）。</summary>
    public static string ProjectListHint(bool saved) => saved ? ProjectListHintSaved : ProjectListHintUnsaved;

    /// <summary>
    /// 单行字段行的行高上限：正文行高 + <see cref="RowPadding"/>。超过它的行说明那一行里
    /// 混进了不该有的东西（默认尺寸的占位面板、多行说明、被拉高的容器），冒烟器据此判失败。
    /// </summary>
    public static int MaxRowHeight(int bodyLineHeight) => bodyLineHeight + RowPadding;

    /// <summary>
    /// 高级入口那一行里相邻两个按钮的最大水平间距（像素）：DESIGN.md §4 的 Sm。
    /// 四个入口此前各占一整行、彼此隔着 100px 的空白，读起来像四颗孤立的按钮。
    /// </summary>
    public const int AdvancedEntryGap = 8;
}
