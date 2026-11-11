using System.Drawing;
using System.Windows.Forms;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 提示对话框（DESIGN.md §6「对话框」）：说一件事、不需要用户做选择——标题（Section 档）、
/// 正文（Body 档）、可选的等宽行（路径）与说明行（Caption 档），一个「关闭」ghost 按钮，
/// Enter 与 Esc 都是关闭。与其余信息视图同一套令牌与形状（FixedDialog、面板底、Body 字体、
/// 长内容区经 <see cref="Ui.ScrollHost"/>），不再用系统 MessageBox 的默认观感。
/// 确认类动作（执行同步、覆盖式写入、退出）仍用原生确认对话框（§6 例外条款），不经过这里。
/// 文字取 <see cref="NoticeText"/> 一份，本对话框不就地拼写。
/// 等宽行是路径时，调用方给 <c>copyText</c> 即多一颗「复制路径」按钮（口径在 <see cref="NoticeCopy"/>）：
/// 说明行指着「手动打开上面的路径」，没有这颗按钮那一步只能照着屏幕敲。
/// 等宽行本身是<b>可框选的只读文本框</b>（<see cref="Ui.MonoBox"/>，第一百九十一轮）：整行一键交出走按钮，
/// 只挑其中一段走框选，两条路并存——此前它是 <c>Label</c>，一个字都选不中。
/// </summary>
internal sealed class NoticeDialog : DpiAwareForm
{
    public NoticeDialog(string heading, string body, string? mono = null, string? note = null, string? copyText = null)
    {
        Text = heading;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        DpiScale.Apply(this, new Size(560, 260));
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = UiTokens.Panel,
            Padding = new Padding(UiTokens.Lg),
            Margin = new Padding(0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // 折行宽与量宽取同一处：内容网格的实际可用宽度（客户区宽 - 左右内边距，算式在 Ui.ContentWidth）。
        // 第一百九十二轮之前这里是「窗体客户区宽减两档 Lg」——与 grid.Padding 是两笔独立的账，
        // 数值碰巧相等，改一处就错位（正文按窗体宽折、等宽行按网格宽量，长路径会被裁掉半行）。
        // 构造这一刻 grid 还没有客户区（宽 0），先按窗体客户区宽种一次，布局完成后再按实际可用宽度重算。
        grid.Width = ClientSize.Width;
        var wrapWidth = Ui.ContentWidth(grid);

        var title = Ui.Section(heading);
        title.Margin = new Padding(0, 0, 0, UiTokens.Md);
        grid.Controls.Add(title);

        var bodyLabel = Ui.Body(body);
        bodyLabel.MaximumSize = new Size(wrapWidth, 0);
        bodyLabel.Margin = new Padding(0, 0, 0, UiTokens.Md);
        grid.Controls.Add(bodyLabel);

        TextBox? monoBox = null;
        if (mono is not null)
        {
            // 可框选复制的只读文本框（不是 Label）：路径要能被选中、Ctrl+A、Ctrl+C，
            // 而「只挑其中一段」这件事按钮做不到。高度按实际字体与可用宽度量出（Ui.MonoBoxHeight）。
            monoBox = Ui.MonoBox(mono, wrapWidth);
            monoBox.Margin = new Padding(0, 0, 0, UiTokens.Md);
            grid.Controls.Add(monoBox);
        }

        Label? noteLabel = null;
        if (note is not null)
        {
            noteLabel = Ui.Caption(note);
            noteLabel.MaximumSize = new Size(wrapWidth, 0);
            grid.Controls.Add(noteLabel);
        }

        // 布局完成后按 grid 的实际可用宽度重算一次：量宽与控件实际拿到的宽度必须是同一个数
        // （门在 NoticeCopyTests.NoticeDialog_MeasuresTheMonoLineWithTheGridsActualAvailableWidth，
        // 运行时那一半在冒烟段「等宽行的量宽就是它实际拿到的宽度」）。
        // 宽度没变就不动——在 Layout 里改尺寸会再触发一次 Layout，不设这道闸会自己绕圈。
        var applied = wrapWidth;
        void ApplyWrapWidth()
        {
            var width = Ui.ContentWidth(grid);
            if (width == applied) return;
            applied = width;
            bodyLabel.MaximumSize = new Size(width, 0);
            if (monoBox is not null)
            {
                // 宽度变了高度必须跟着重量：只改宽不重量，长路径会被裁掉半行。
                monoBox.Width = width;
                monoBox.Height = Ui.MonoBoxHeight(monoBox.Text, width);
            }
            if (noteLabel is not null) noteLabel.MaximumSize = new Size(width, 0);
        }

        grid.Layout += (_, _) => ApplyWrapWidth();

        // 停靠方向由工厂一处决定（Dock = Fill），这里不再补设。
        var host = Ui.ScrollHost();
        host.Controls.Add(grid);

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;
        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = UiTokens.Panel,
            Padding = new Padding(UiTokens.Lg, UiTokens.Sm, UiTokens.Lg, UiTokens.Md),
        };
        // RightToLeft 流向：先加的在最右（Windows 惯例：确认类按钮在右）。
        buttonRow.Controls.Add(closeButton);
        // 复制路径：说明行指着「手动打开上面的路径」，没有这颗按钮那一步只能照着屏幕一个字一个字敲
        // （等宽行虽已可框选，但「整行一键交出」仍要一颗按钮）。只在调用方给了可复制的文字时出现
        // （判定在 NoticeCopy.Target：空白与没给都不给按钮，一颗点了什么也不会发生的按钮不该出现）。
        if (NoticeCopy.Target(copyText) is string target)
        {
            var copyButton = Ui.Ghost(NoticeCopy.Label);
            copyButton.Click += (_, _) =>
                copyButton.Text = Ui.CopyToClipboard(target) ? NoticeCopy.CopiedLabel : NoticeCopy.FailedLabel;
            buttonRow.Controls.Add(copyButton);
        }

        // 停靠按集合倒序生效：按钮行最先停靠（最底部），内容区填满剩余空间。
        Controls.Add(host);
        Controls.Add(buttonRow);
    }
}
