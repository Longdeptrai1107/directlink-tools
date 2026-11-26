using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DirectLinkDownloader
{
    public sealed class MainForm : Form
    {
        private readonly TextBox url = new TextBox();
        private readonly TextBox preferredName = new TextBox();
        private readonly NumericUpDown threads = new NumericUpDown();
        private readonly ModernButton paste = new ModernButton();
        private readonly ModernButton start = new ModernButton();
        private readonly ModernButton cancel = new ModernButton();
        private readonly ModernButton openFolder = new ModernButton();
        private readonly ProgressTrack progress = new ProgressTrack();
        private readonly SpeedChart chart = new SpeedChart();
        private readonly Label state = TextLabel("Sẵn sàng nhận liên kết", 9F, Palette.Muted, false);
        private readonly Label fileTitle = TextLabel("Chưa có lượt tải", 11.5F, Palette.Ink, true);
        private readonly Label percent = TextLabel("0%", 22F, Palette.Purple, true);
        private readonly Label speedValue = TextLabel("0 B/s", 17F, Palette.Ink, true);
        private readonly Label sizeValue = TextLabel("0 B / —", 17F, Palette.Ink, true);
        private readonly Label timeValue = TextLabel("—", 17F, Palette.Ink, true);
        private readonly Label connectionLabel = TextLabel("CHƯA KẾT NỐI", 8F, Palette.Muted, true);
        private readonly Label footer = TextLabel("File tự lưu vào Downloads. Không tự động mở hoặc chạy sau khi tải.", 9F, Palette.Muted, false);
        private readonly IconView fileIcon = new IconView { Glyph = Glyph.File, Tile = Color.FromArgb(240, 237, 255) };
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch watch = new Stopwatch();
        private readonly ToolTip tips = new ToolTip { AutoPopDelay = 10000 };
        private readonly string downloadsFolder;
        private DownloadEngine engine;
        private CancellationTokenSource cancellation;
        private long previousBytes;
        private double previousSeconds;
        private double speed;
        private bool busy;
        private bool closeWhenDone;
        private string lastSavedPath;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string text);

        public MainForm()
        {
            downloadsFolder = DownloadPaths.GetDownloadsFolder();
            Directory.CreateDirectory(downloadsFolder);
            Text = "DirectLink — Download Manager";
            Font = new Font("Segoe UI", 10F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1100, 720);
            MinimumSize = new Size(1016, 759);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Palette.Background;
            DoubleBuffered = true;
            KeyPreview = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

            TableLayoutPanel shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Palette.Background };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 206));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(shell);
            shell.Controls.Add(BuildSidebar(), 0, 0);

            Panel main = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(28, 24, 28, 20), Margin = Padding.Empty };
            shell.Controls.Add(main, 1, 0);
            TableLayoutPanel stack = new TableLayoutPanel { Dock = DockStyle.Top, Height = 650, ColumnCount = 1, RowCount = 7,
                Margin = Padding.Empty, Padding = Padding.Empty };
            int[] rows = { 72, 206, 16, 246, 16, 46, 48 };
            foreach (int row in rows) stack.RowStyles.Add(new RowStyle(SizeType.Absolute, row));
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            main.Controls.Add(stack);
            stack.Controls.Add(BuildHeader(), 0, 0);
            stack.Controls.Add(BuildInputCard(), 0, 1);
            stack.Controls.Add(BuildProgressCard(), 0, 3);
            stack.Controls.Add(BuildActions(), 0, 5);
            Panel foot = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            IconView shield = new IconView { Glyph = Glyph.Shield, Tint = Palette.Green, Bounds = new Rectangle(1, 17, 17, 17) };
            footer.SetBounds(27, 15, 750, 32);
            footer.AutoEllipsis = true;
            foot.Controls.Add(shield); foot.Controls.Add(footer);
            foot.Resize += delegate { footer.Width = Math.Max(1, foot.Width - 30); };
            stack.Controls.Add(foot, 0, 6);

            AcceptButton = start;
            timer.Interval = 250;
            timer.Tick += delegate { UpdateProgress(); };
            FormClosing += OnClosing;
            KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Control && args.KeyCode == Keys.L) { url.Focus(); url.SelectAll(); args.SuppressKeyPress = true; }
            };
            Shown += delegate
            {
                SendMessage(url.Handle, 0x1501, new IntPtr(1), "Dán link trực tiếp đến file tại đây...");
                SendMessage(preferredName.Handle, 0x1501, IntPtr.Zero, "Tự lấy tên từ máy chủ");
                url.Focus();
            };
        }

        private Panel BuildSidebar()
        {
            Color background = Color.FromArgb(22, 29, 50);
            Panel side = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = background };
            side.Controls.Add(new IconView { Glyph = Glyph.Download, Gradient = true, Tint = Color.White, Bounds = new Rectangle(22, 32, 43, 43) });
            Label brand = TextLabel("DirectLink", 16F, Color.White, true); brand.SetBounds(76, 30, 126, 29); side.Controls.Add(brand);
            Label brandNote = TextLabel("DOWNLOAD MANAGER", 6.8F, Color.FromArgb(139, 152, 183), true); brandNote.SetBounds(77, 61, 128, 18); side.Controls.Add(brandNote);
            Label workspace = TextLabel("KHÔNG GIAN LÀM VIỆC", 7.4F, Color.FromArgb(112, 128, 160), true); workspace.SetBounds(23, 118, 180, 20); side.Controls.Add(workspace);
            Card selected = new Card { Fill = Color.FromArgb(48, 43, 84), Border = Color.FromArgb(48, 43, 84), Radius = 10, Bounds = new Rectangle(16, 148, 174, 46) };
            selected.Controls.Add(new IconView { Glyph = Glyph.Download, Tint = Color.FromArgb(179, 157, 255), Bounds = new Rectangle(15, 13, 20, 20) });
            Label nav = TextLabel("Tải xuống", 10F, Color.White, true); nav.SetBounds(48, 12, 119, 24); selected.Controls.Add(nav); side.Controls.Add(selected);
            ModernButton folder = new ModernButton { Text = "Downloads", Glyph = Glyph.Folder, Dark = true, Bounds = new Rectangle(16, 202, 174, 44) };
            folder.Click += delegate { OpenDownloads(); }; side.Controls.Add(folder);
            ModernButton help = new ModernButton { Text = "Hướng dẫn", Glyph = Glyph.Help, Dark = true, Bounds = new Rectangle(16, 251, 174, 44) };
            help.Click += delegate
            {
                MessageBox.Show(this,
                    "1. Dán link HTTP/HTTPS trực tiếp đến file.\n2. Giữ tên tự động hoặc nhập tên bạn muốn.\n3. Chọn số luồng, rồi bấm Bắt đầu tải.\n\n"
                    + "File được lưu vào:\n" + downloadsFolder + "\n\n"
                    + "• Trùng tên: tự thêm (1), (2)..., không ghi đè.\n• Không hỗ trợ Range: tự tải một luồng.\n• Có thể hủy; chưa hỗ trợ tiếp tục sau khi đóng.\n"
                    + "• Tốc độ phụ thuộc đường truyền và máy chủ.\n• Cần dung lượng trống khoảng hai lần kích thước file khi ghép.\n"
                    + "• Link trang đăng nhập hoặc trang trung gian cần đổi thành link trực tiếp.\n• Ctrl+L: chọn ô link. Enter: bắt đầu tải.",
                    "DirectLink — Hướng dẫn", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }; side.Controls.Add(help);
            Card hint = new Card { Fill = Color.FromArgb(32, 41, 66), Border = Color.FromArgb(47, 58, 87), Radius = 14, Size = new Size(174, 153) };
            hint.Controls.Add(new IconView { Glyph = Glyph.Bolt, Tint = Palette.Cyan, Bounds = new Rectangle(15, 16, 23, 23) });
            Label fast = TextLabel("Tải song song", 10F, Color.White, true); fast.SetBounds(46, 16, 124, 24); hint.Controls.Add(fast);
            Label number = TextLabel("32", 31F, Color.White, true); number.SetBounds(13, 44, 77, 54); hint.Controls.Add(number);
            Label max = TextLabel("KẾT NỐI\nTỐI ĐA", 7.6F, Color.FromArgb(147, 164, 198), true); max.SetBounds(89, 57, 79, 40); hint.Controls.Add(max);
            Label note = TextLabel("Bắt đầu với 4–8 luồng.\nTối ưu tùy theo máy chủ.", 8.3F, Color.FromArgb(151, 166, 195), false); note.SetBounds(16, 106, 152, 38); hint.Controls.Add(note);
            side.Controls.Add(hint);
            Label version = TextLabel("PORTABLE   /   v2.0", 8F, Color.FromArgb(116, 134, 167), true); version.Size = new Size(174, 24); side.Controls.Add(version);
            side.Resize += delegate { hint.Location = new Point(16, Math.Max(329, side.Height - 217)); version.Location = new Point(23, side.Height - 43); };
            return side;
        }

        private Panel BuildHeader()
        {
            Panel header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Label title = TextLabel("Tải xuống", 25F, Palette.Ink, true); title.SetBounds(-2, -2, 370, 45); header.Controls.Add(title);
            Label subtitle = TextLabel("Link trực tiếp. Lưu tự động. Theo dõi trong thời gian thực.", 9.5F, Palette.Muted, false); subtitle.SetBounds(0, 45, 640, 23); header.Controls.Add(subtitle);
            Card badge = new Card { Fill = Color.FromArgb(231, 248, 241), Border = Color.FromArgb(218, 240, 230), Radius = 14, Size = new Size(113, 29) };
            badge.Controls.Add(new IconView { Glyph = Glyph.Check, Tint = Palette.Green, Bounds = new Rectangle(11, 6, 17, 17) });
            Label label = TextLabel("PORTABLE", 8F, Palette.Green, true); label.SetBounds(34, 6, 79, 19); badge.Controls.Add(label); header.Controls.Add(badge);
            header.Resize += delegate { badge.Location = new Point(header.Width - badge.Width, 9); subtitle.Width = Math.Max(1, header.Width - 4); };
            return header;
        }

        private Card BuildInputCard()
        {
            Card card = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Label heading = TextLabel("Thêm liên kết mới", 11F, Palette.Ink, true); heading.SetBounds(22, 17, 280, 24); card.Controls.Add(heading);
            Label protocol = TextLabel("HTTP / HTTPS", 8F, Palette.Muted, true); protocol.TextAlign = ContentAlignment.MiddleRight;
            protocol.SetBounds(560, 18, 190, 22); card.Controls.Add(protocol);
            Card input = new Card { Fill = Color.FromArgb(247, 248, 253), Border = Palette.Line, Radius = 10 };
            input.Controls.Add(new IconView { Glyph = Glyph.Link, Tint = Palette.Purple, Bounds = new Rectangle(13, 14, 22, 22) });
            url.BorderStyle = BorderStyle.None; url.BackColor = input.Fill; url.ForeColor = Palette.Ink; url.Font = new Font("Segoe UI", 10.5F);
            url.SetBounds(46, 14, 500, 25); url.AccessibleName = "Link tải trực tiếp"; input.Controls.Add(url);
            paste.Text = "Dán"; paste.Glyph = Glyph.Clipboard; paste.Quiet = true; paste.Size = new Size(90, 36);
            paste.Click += delegate
            {
                try { if (Clipboard.ContainsText()) { url.Text = Clipboard.GetText().Trim(); url.Focus(); url.SelectionStart = url.TextLength; } }
                catch (ExternalException) { state.Text = "Clipboard đang bận, hãy thử Ctrl+V."; }
            };
            input.Controls.Add(paste); card.Controls.Add(input);
            input.Resize += delegate { paste.Location = new Point(input.Width - 97, 7); url.Width = Math.Max(1, input.Width - 155); };
            Label nameLabel = TextLabel("TÊN FILE  ·  TÙY CHỌN", 7.8F, Palette.Muted, true); nameLabel.SetBounds(23, 115, 310, 18); card.Controls.Add(nameLabel);
            Label threadLabel = TextLabel("SỐ LUỒNG", 7.8F, Palette.Muted, true); card.Controls.Add(threadLabel);
            Card nameBox = new Card { Fill = Color.White, Border = Palette.Line, Radius = 8 };
            nameBox.Controls.Add(new IconView { Glyph = Glyph.File, Tint = Palette.Muted, Bounds = new Rectangle(12, 10, 18, 18) });
            preferredName.BorderStyle = BorderStyle.None; preferredName.ForeColor = Palette.Ink; preferredName.Font = new Font("Segoe UI", 9.5F);
            preferredName.SetBounds(40, 9, 400, 23); preferredName.AccessibleName = "Tên file tùy chọn"; nameBox.Controls.Add(preferredName); card.Controls.Add(nameBox);
            nameBox.Resize += delegate { preferredName.Width = Math.Max(1, nameBox.Width - 52); };
            Card threadBox = new Card { Fill = Color.White, Border = Palette.Line, Radius = 8 };
            threadBox.Controls.Add(new IconView { Glyph = Glyph.Bolt, Tint = Palette.Purple, Bounds = new Rectangle(11, 10, 18, 18) });
            threads.Minimum = 1; threads.Maximum = 32; threads.Value = 8; threads.BorderStyle = BorderStyle.None;
            threads.Font = new Font("Segoe UI", 10F, FontStyle.Bold); threads.ForeColor = Palette.Ink;
            threads.SetBounds(39, 9, 86, 25); threads.AccessibleName = "Số luồng tải"; threadBox.Controls.Add(threads); card.Controls.Add(threadBox);
            IconView folder = new IconView { Glyph = Glyph.Folder, Tint = Palette.Purple, Bounds = new Rectangle(23, 181, 16, 16) }; card.Controls.Add(folder);
            Label save = TextLabel("Tự lưu vào Downloads  ·  Tự đổi tên nếu trùng", 8.3F, Palette.Muted, false); save.SetBounds(46, 180, 670, 21); card.Controls.Add(save);
            save.Cursor = Cursors.Hand; save.Click += delegate { OpenDownloads(); }; tips.SetToolTip(save, downloadsFolder);
            card.Resize += delegate
            {
                input.SetBounds(22, 52, card.Width - 44, 50);
                protocol.SetBounds(card.Width - 198, 18, 176, 22);
                threadBox.SetBounds(card.Width - 158, 134, 136, 39);
                threadLabel.SetBounds(card.Width - 158, 115, 136, 18);
                nameBox.SetBounds(22, 134, card.Width - 194, 39);
                save.Width = Math.Max(1, card.Width - 67);
            };
            tips.SetToolTip(threads, "1–32 luồng. File nhỏ hoặc máy chủ không hỗ trợ Range sẽ dùng ít kết nối hơn.");
            return card;
        }

        private Card BuildProgressCard()
        {
            Card card = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Label heading = TextLabel("TIẾN TRÌNH TẢI", 8F, Palette.Muted, true); heading.SetBounds(23, 17, 210, 22); card.Controls.Add(heading);
            connectionLabel.TextAlign = ContentAlignment.MiddleRight; card.Controls.Add(connectionLabel);
            fileIcon.SetBounds(22, 55, 44, 44); card.Controls.Add(fileIcon);
            fileTitle.SetBounds(79, 54, 500, 26); fileTitle.AutoEllipsis = true; card.Controls.Add(fileTitle);
            state.SetBounds(79, 82, 500, 22); state.AutoEllipsis = true; card.Controls.Add(state);
            percent.TextAlign = ContentAlignment.MiddleRight; card.Controls.Add(percent);
            progress.BackColor = Color.White; card.Controls.Add(progress);

            Panel stats = new Panel { BackColor = Color.White };
            TableLayoutPanel columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            for (int i = 0; i < 3; i++) columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            stats.Controls.Add(columns);
            columns.Controls.Add(Stat("TỐC ĐỘ", speedValue, Glyph.Bolt, Palette.Purple), 0, 0);
            columns.Controls.Add(Stat("DUNG LƯỢNG", sizeValue, Glyph.Download, Palette.Blue), 1, 0);
            columns.Controls.Add(Stat("CÒN LẠI", timeValue, Glyph.Clock, Palette.Green), 2, 0);
            card.Controls.Add(stats); card.Controls.Add(chart);
            card.Resize += delegate
            {
                connectionLabel.SetBounds(card.Width - 245, 16, 222, 22);
                fileTitle.Width = state.Width = Math.Max(1, card.Width - 216);
                percent.SetBounds(card.Width - 127, 56, 103, 44);
                progress.SetBounds(23, 118, card.Width - 46, 10);
                stats.SetBounds(23, 145, card.Width - 46, 62);
                chart.SetBounds(23, 213, card.Width - 46, 23);
            };
            tips.SetToolTip(chart, "Biểu đồ tốc độ của lượt tải hiện tại; không hiển thị dữ liệu giả lập.");
            return card;
        }

        private static Panel Stat(string caption, Label value, Glyph glyph, Color tint)
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            panel.Controls.Add(new IconView { Glyph = glyph, Tint = tint, Bounds = new Rectangle(0, 0, 15, 15) });
            Label label = TextLabel(caption, 7.5F, Palette.Muted, true); label.SetBounds(22, -1, 185, 19); panel.Controls.Add(label);
            value.SetBounds(-2, 22, 250, 32); value.AutoEllipsis = true; panel.Controls.Add(value);
            panel.Resize += delegate { value.Width = Math.Max(1, panel.Width - 8); label.Width = Math.Max(1, panel.Width - 25); };
            return panel;
        }

        private Panel BuildActions()
        {
            Panel actions = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            start.Text = "Bắt đầu tải"; start.Primary = true; start.Glyph = Glyph.Download; start.SetBounds(0, 0, 194, 46);
            cancel.Text = "Hủy tải"; cancel.Glyph = Glyph.Stop; cancel.Enabled = false; cancel.SetBounds(206, 0, 124, 46);
            openFolder.Text = "Mở Downloads"; openFolder.Glyph = Glyph.Folder; openFolder.SetBounds(342, 0, 180, 46);
            start.Click += StartDownload;
            cancel.Click += delegate { CancelDownload(); };
            openFolder.Click += delegate { OpenDownloads(); };
            actions.Controls.AddRange(new Control[] { start, cancel, openFolder });
            return actions;
        }

        private static Label TextLabel(string text, float size, Color color, bool bold)
        {
            return new Label { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color, BackColor = Color.Transparent, UseMnemonic = false };
        }

        private void OpenDownloads()
        {
            try
            {
                Directory.CreateDirectory(downloadsFolder);
                if (!string.IsNullOrEmpty(lastSavedPath) && File.Exists(lastSavedPath)) Process.Start("explorer.exe", "/select,\"" + lastSavedPath + "\"");
                else Process.Start("explorer.exe", "\"" + downloadsFolder + "\"");
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Không mở được Downloads"); }
        }

        private async void StartDownload(object sender, EventArgs args)
        {
            if (busy) return;
            Uri address;
            if (!Uri.TryCreate(url.Text.Trim(), UriKind.Absolute, out address) || (address.Scheme != "https" && address.Scheme != "http"))
            {
                MessageBox.Show(this, "Hãy nhập link HTTP hoặc HTTPS trực tiếp đến file.", "Kiểm tra liên kết", MessageBoxButtons.OK, MessageBoxIcon.Information);
                url.Focus(); return;
            }
            try { Directory.CreateDirectory(downloadsFolder); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Không ghi được vào Downloads"); return; }

            engine = new DownloadEngine();
            cancellation = new CancellationTokenSource();
            busy = true; ToggleInputs(false);
            previousBytes = 0; previousSeconds = 0; speed = 0;
            progress.Value = 0; progress.Indeterminate = true; progress.Invalidate();
            fileTitle.Text = string.IsNullOrWhiteSpace(preferredName.Text) ? "Đang lấy tên file..." : DownloadPaths.SafeFileName(preferredName.Text);
            fileIcon.Glyph = Glyph.File; fileIcon.Tint = Palette.Purple; fileIcon.Invalidate();
            state.Text = "Đang kết nối máy chủ..."; state.ForeColor = Palette.Muted;
            percent.Text = "0%"; percent.ForeColor = Palette.Purple;
            chart.Reset(); speedValue.Text = "0 B/s"; sizeValue.Text = "0 B / —"; timeValue.Text = "—";
            tips.SetToolTip(speedValue, "Tốc độ tải hiện tại, làm mượt theo các mẫu đo thực tế.");
            connectionLabel.Text = "ĐANG KẾT NỐI";
            footer.Text = "Đang lưu vào " + downloadsFolder;
            watch.Restart(); timer.Start();
            try
            {
                DownloadResult result = await engine.DownloadToFolderAsync(address.AbsoluteUri, downloadsFolder,
                    preferredName.Text.Trim(), (int)threads.Value, cancellation.Token);
                watch.Stop(); UpdateProgress();
                lastSavedPath = result.Path;
                fileTitle.Text = Path.GetFileName(result.Path); tips.SetToolTip(fileTitle, result.Path);
                progress.Value = 1; progress.Indeterminate = false; progress.Invalidate(); percent.Text = "100%"; percent.ForeColor = Palette.Green;
                state.Text = "Hoàn tất · " + FormatDuration(watch.Elapsed.TotalSeconds) + " · Đã lưu vào Downloads";
                state.ForeColor = Palette.Green; fileIcon.Glyph = Glyph.Check; fileIcon.Tint = Palette.Green; fileIcon.Invalidate();
                sizeValue.Text = FormatBytes(result.Bytes); timeValue.Text = "Hoàn tất";
                connectionLabel.Text = "HOÀN TẤT · " + result.Connections + " LUỒNG";
                speedValue.Text = FormatBytes((long)(result.Bytes / Math.Max(0.001, watch.Elapsed.TotalSeconds))) + "/s";
                tips.SetToolTip(speedValue, "Tốc độ trung bình của lượt tải vừa hoàn tất.");
                footer.Text = "Đã lưu: " + result.Path; tips.SetToolTip(footer, result.Path);
            }
            catch (OperationCanceledException)
            {
                state.Text = "Đã hủy lượt tải"; state.ForeColor = Palette.Muted;
                footer.Text = "Có thể bắt đầu tải lại. Các file đã hoàn tất trong Downloads vẫn được giữ.";
                connectionLabel.Text = "ĐÃ NGẮT KẾT NỐI"; speedValue.Text = "0 B/s"; timeValue.Text = "—";
            }
            catch (Exception error)
            {
                state.Text = "Tải thất bại — có thể thử lại"; state.ForeColor = Color.FromArgb(214, 77, 95);
                connectionLabel.Text = "ĐÃ NGẮT KẾT NỐI"; speedValue.Text = "0 B/s"; timeValue.Text = "—";
                footer.Text = "Kiểm tra link trực tiếp, kết nối mạng và dung lượng ổ đĩa.";
                if (!closeWhenDone) MessageBox.Show(this, error.Message + "\n\nKiểm tra link trực tiếp, mạng và quyền ghi vào Downloads.",
                    "Không tải được file", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                timer.Stop(); watch.Stop(); progress.Indeterminate = false; progress.Invalidate();
                busy = false; ToggleInputs(true);
                cancellation.Dispose(); cancellation = null;
                if (closeWhenDone) Close();
            }
        }

        private void UpdateProgress()
        {
            if (engine == null) return;
            DownloadProgress p = engine.GetProgress();
            double seconds = watch.Elapsed.TotalSeconds;
            double interval = seconds - previousSeconds;
            if (interval > 0.1)
            {
                double instantaneous = Math.Max(0, p.Received - previousBytes) / interval;
                speed = speed == 0 ? instantaneous : speed * 0.65 + instantaneous * 0.35;
                previousBytes = p.Received; previousSeconds = seconds;
                chart.Add(instantaneous);
            }
            progress.Indeterminate = p.Total < 0;
            progress.Value = p.Total > 0 ? (double)p.Received / p.Total : 0;
            progress.Phase++; progress.Invalidate();
            percent.Text = p.Total > 0 ? Math.Min(100, p.Received * 100.0 / p.Total).ToString("0") + "%" : "—";
            if (!string.IsNullOrEmpty(p.FileName)) { fileTitle.Text = p.FileName; tips.SetToolTip(fileTitle, p.FileName); }
            state.Text = cancellation != null && cancellation.IsCancellationRequested ? "Đang hủy và dọn file tạm..." : p.Status;
            speedValue.Text = FormatBytes((long)speed) + "/s";
            sizeValue.Text = FormatBytes(p.Received) + (p.Total >= 0 ? " / " + FormatBytes(p.Total) : " / —");
            timeValue.Text = p.Total > 0 && speed > 1 ? FormatDuration(Math.Max(0, (p.Total - p.Received) / speed)) : "—";
            connectionLabel.Text = p.Connections > 0 ? p.Connections + " KẾT NỐI · " + (p.Connections > 1 ? "SONG SONG" : "MỘT LUỒNG") : "ĐANG KẾT NỐI";
        }

        private void ToggleInputs(bool enabled)
        {
            url.Enabled = preferredName.Enabled = paste.Enabled = threads.Enabled = start.Enabled = enabled;
            cancel.Enabled = !enabled;
        }

        private void CancelDownload()
        {
            if (cancellation == null) return;
            cancel.Enabled = false; state.Text = "Đang hủy và dọn file tạm..."; cancellation.Cancel();
        }

        private void OnClosing(object sender, FormClosingEventArgs args)
        {
            if (!busy) { timer.Dispose(); tips.Dispose(); return; }
            if (closeWhenDone) { args.Cancel = true; return; }
            if (MessageBox.Show(this, "Hủy lượt tải và đóng DirectLink?", "Đang tải", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes) { closeWhenDone = true; CancelDownload(); }
            args.Cancel = true;
        }

        private static string FormatBytes(long value)
        {
            double n = Math.Max(0, value);
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (n >= 1024 && unit < units.Length - 1) { n /= 1024; unit++; }
            return n.ToString(unit == 0 ? "0" : "0.0") + " " + units[unit];
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds > 8640000) return "> 100 ngày";
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            return t.TotalDays >= 1 ? ((int)t.TotalDays) + " ngày " + t.Hours + " giờ"
                : t.TotalHours >= 1 ? ((int)t.TotalHours) + " giờ " + t.Minutes + " phút"
                : t.TotalMinutes >= 1 ? t.Minutes + " phút " + t.Seconds + " giây" : t.Seconds + " giây";
        }
    }
}
