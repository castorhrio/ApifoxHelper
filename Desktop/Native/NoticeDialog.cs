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

        // 正文按对话框宽度折行：MaximumSize 的宽度取可视宽度，高度交给 AutoSize（长内容由宿主滚动）。
        var wrapWidth = ClientSize.Width - 2 * UiTokens.Lg;

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

        var title = Ui.Section(heading);
        title.Margin = new Padding(0, 0, 0, UiTokens.Md);
        grid.Controls.Add(title);

        var bodyLabel = Ui.Body(body);
        bodyLabel.MaximumSize = new Size(wrapWidth, 0);
        bodyLabel.Margin = new Padding(0, 0, 0, UiTokens.Md);
        grid.Controls.Add(bodyLabel);

        if (mono is not null)
        {
            // 可框选复制的只读文本框（不是 Label）：路径要能被选中、Ctrl+A、Ctrl+C，
            // 而「只挑其中一段」这件事按钮做不到。高度按实际字体与可用宽度量出（Ui.MonoBoxHeight）。
            var monoBox = Ui.MonoBox(mono, wrapWidth);
            monoBox.Margin = new Padding(0, 0, 0, UiTokens.Md);
            grid.Controls.Add(monoBox);
        }

        if (note is not null)
        {
            var noteLabel = Ui.Caption(note);
            noteLabel.MaximumSize = new Size(wrapWidth, 0);
            grid.Controls.Add(noteLabel);
        }

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
