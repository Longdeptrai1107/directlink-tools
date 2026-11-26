using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using DirectLinkDownloader;

namespace DirectLinkShare
{
    public sealed class ShareForm : Form
    {
        private readonly ModernButton choose = new ModernButton();
        private readonly ModernButton create = new ModernButton();
        private readonly ModernButton stop = new ModernButton();
        private readonly ModernButton copy = new ModernButton();
        private readonly ModernButton reveal = new ModernButton();
        private readonly TextBox link = new TextBox();
        private readonly Label fileName = LabelOf("Chọn hoặc thả một file vào đây", 12F, Palette.Ink, true);
        private readonly Label fileInfo = LabelOf("File nằm trên máy bạn; không cần tải lên kho lưu trữ.", 9F, Palette.Muted, false);
        private readonly Label state = LabelOf("CHƯA CHIA SẺ", 8F, Palette.Muted, true);
        private readonly Label detail = LabelOf("Link HTTPS sẽ sẵn sàng khi kết nối Cloudflare thành công.", 8.7F, Palette.Muted, false);
        private readonly Label sent = LabelOf("0 B", 18F, Palette.Ink, true);
        private readonly Label active = LabelOf("0", 18F, Palette.Ink, true);
        private readonly Label speed = LabelOf("0 B/s", 18F, Palette.Ink, true);
        private readonly Label requests = LabelOf("0", 18F, Palette.Ink, true);
        private readonly Label uptime = LabelOf("LOCALHOST → INTERNET", 7.8F, Palette.Muted, true);
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch watch = new Stopwatch();
        private readonly ToolTip tips = new ToolTip { AutoPopDelay = 15000 };
        private readonly IconView selectedIcon = new IconView { Glyph = Glyph.Upload, Gradient = true, Tint = Color.White };
        private ShareSession session;
        private CancellationTokenSource startCancellation;
        private string selectedPath;
        private string previousLog = "Chưa có phiên chia sẻ. Chọn một file rồi bấm Tạo link.";
        private long lastBytes;
        private double lastSeconds;
        private double smoothedSpeed;
        private bool starting;
        private bool sharing;
        private bool closeRequested;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr parameter, string text);

        public ShareForm()
        {
            Text = "DirectLink Share — Cloudflare Tunnel";
            Font = new Font("Segoe UI", 10F);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1100, 720); MinimumSize = new Size(1016, 759);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Palette.Background; DoubleBuffered = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 206)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(root);
            root.Controls.Add(Sidebar(), 0, 0);
            Panel main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 24, 28, 20), Margin = Padding.Empty, AutoScroll = true };
            root.Controls.Add(main, 1, 0);
            TableLayoutPanel stack = new TableLayoutPanel { Dock = DockStyle.Top, Height = 656, ColumnCount = 1, RowCount = 9, Margin = Padding.Empty };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new int[] { 72, 158, 16, 148, 16, 120, 16, 46, 64 }) stack.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            main.Controls.Add(stack);
            stack.Controls.Add(Header(), 0, 0); stack.Controls.Add(FileCard(), 0, 1);
            stack.Controls.Add(LinkCard(), 0, 3); stack.Controls.Add(StatsCard(), 0, 5);
            stack.Controls.Add(Actions(), 0, 7); stack.Controls.Add(PrivacyNote(), 0, 8);
            timer.Interval = 500; timer.Tick += delegate { RefreshStatistics(); }; timer.Start();
            FormClosing += OnWindowClosing;
            Shown += delegate { SendMessage(link.Handle, 0x1501, new IntPtr(1), "Link HTTPS trực tiếp sẽ xuất hiện tại đây..."); };
            AcceptButton = create;
            UpdateControls();
        }

        private Panel Sidebar()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.FromArgb(22, 29, 50) };
            panel.Controls.Add(new IconView { Glyph = Glyph.Upload, Gradient = true, Tint = Color.White, Bounds = new Rectangle(22, 32, 43, 43) });
            Label brand = LabelOf("DirectLink", 16F, Color.White, true); brand.SetBounds(76, 30, 126, 30); panel.Controls.Add(brand);
            Label product = LabelOf("SHARE · CLOUDFLARE", 7F, Color.FromArgb(139, 152, 183), true); product.SetBounds(77, 61, 127, 18); panel.Controls.Add(product);
            Label area = LabelOf("CHIA SẺ TỪ MÁY CỦA BẠN", 7.4F, Color.FromArgb(112, 128, 160), true); area.SetBounds(23, 118, 178, 20); panel.Controls.Add(area);
            Card selected = new Card { Fill = Color.FromArgb(48, 43, 84), Border = Color.FromArgb(48, 43, 84), Radius = 10, Bounds = new Rectangle(16, 148, 174, 46) };
            selected.Controls.Add(new IconView { Glyph = Glyph.Link, Tint = Color.FromArgb(179, 157, 255), Bounds = new Rectangle(15, 13, 20, 20) });
            Label selectedText = LabelOf("Chia sẻ file", 10F, Color.White, true); selectedText.SetBounds(48, 12, 121, 24); selected.Controls.Add(selectedText); panel.Controls.Add(selected);
            ModernButton log = new ModernButton { Text = "Nhật ký", Glyph = Glyph.File, Dark = true, Bounds = new Rectangle(16, 202, 174, 44) };
            log.Click += delegate { ShowLogs(); }; panel.Controls.Add(log);
            ModernButton help = new ModernButton { Text = "Hướng dẫn", Glyph = Glyph.Help, Dark = true, Bounds = new Rectangle(16, 251, 174, 44) };
            help.Click += delegate { ShowHelp(); }; panel.Controls.Add(help);
            Card cloud = new Card { Fill = Color.FromArgb(32, 41, 66), Border = Color.FromArgb(47, 58, 87), Radius = 14, Size = new Size(174, 156) };
            cloud.Controls.Add(new IconView { Glyph = Glyph.Cloud, Tint = Color.FromArgb(255, 170, 78), Bounds = new Rectangle(16, 16, 28, 28) });
            Label title = LabelOf("Cloudflare", 11F, Color.White, true); title.SetBounds(16, 55, 150, 25); cloud.Controls.Add(title);
            Label name = LabelOf("QUICK TUNNEL", 7.8F, Color.FromArgb(255, 177, 97), true); name.SetBounds(17, 83, 145, 20); cloud.Controls.Add(name);
            Label text = LabelOf("HTTPS · Link tạm thời\nMáy chủ chạy trên máy bạn.", 8.3F, Color.FromArgb(151, 166, 195), false); text.SetBounds(16, 112, 154, 37); cloud.Controls.Add(text); panel.Controls.Add(cloud);
            Label version = LabelOf("PORTABLE   /   SHARE 1.0", 7.8F, Color.FromArgb(116, 134, 167), true); version.Size = new Size(178, 24); panel.Controls.Add(version);
            panel.Resize += delegate { cloud.Location = new Point(16, Math.Max(329, panel.Height - 220)); version.Location = new Point(23, panel.Height - 43); };
            return panel;
        }

        private Panel Header()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Label title = LabelOf("Chia sẻ file", 25F, Palette.Ink, true); title.SetBounds(-2, -2, 440, 45); panel.Controls.Add(title);
            Label subtitle = LabelOf("Chọn file trên máy. Nhận link HTTPS tải trực tiếp.", 9.5F, Palette.Muted, false); subtitle.SetBounds(0, 45, 640, 23); panel.Controls.Add(subtitle);
            Card tag = new Card { Fill = Color.FromArgb(255, 242, 224), Border = Color.FromArgb(251, 229, 201), Radius = 14, Size = new Size(151, 29) };
            tag.Controls.Add(new IconView { Glyph = Glyph.Cloud, Tint = Color.FromArgb(218, 127, 27), Bounds = new Rectangle(10, 5, 19, 19) });
            Label tagText = LabelOf("QUICK TUNNEL", 8F, Color.FromArgb(190, 110, 24), true); tagText.SetBounds(36, 6, 111, 19); tag.Controls.Add(tagText); panel.Controls.Add(tag);
            panel.Resize += delegate { tag.Location = new Point(panel.Width - tag.Width, 9); subtitle.Width = panel.Width - 1; };
            return panel;
        }

        private Card FileCard()
        {
            Card card = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty, AllowDrop = true };
            Label heading = LabelOf("1. Chọn file chia sẻ", 11F, Palette.Ink, true); heading.SetBounds(22, 17, 290, 25); card.Controls.Add(heading);
            choose.Text = "Chọn file"; choose.Glyph = Glyph.Folder; choose.Quiet = true; choose.Size = new Size(127, 34);
            choose.Click += delegate { SelectFile(); }; card.Controls.Add(choose);
            selectedIcon.SetBounds(22, 62, 46, 46); card.Controls.Add(selectedIcon);
            fileName.SetBounds(82, 62, 600, 27); fileName.AutoEllipsis = true; card.Controls.Add(fileName);
            fileInfo.SetBounds(83, 93, 600, 22); fileInfo.AutoEllipsis = true; card.Controls.Add(fileInfo);
            Label hint = LabelOf("Kéo thả 1 file vào đây  ·  Không chia sẻ cả thư mục  ·  Hỗ trợ tải nhiều luồng", 8.5F, Palette.Muted, false);
            hint.SetBounds(23, 129, 730, 23); card.Controls.Add(hint);
            card.DragEnter += delegate(object sender, DragEventArgs args)
            {
                if (!starting && !sharing && args.Data.GetDataPresent(DataFormats.FileDrop)) args.Effect = DragDropEffects.Copy;
            };
            card.DragDrop += delegate(object sender, DragEventArgs args)
            {
                if (starting || sharing) return;
                string[] paths = args.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null && paths.Length == 1 && File.Exists(paths[0])) SetSelectedFile(paths[0]);
                else MessageBox.Show(this, "Hãy chọn đúng một file, không chọn thư mục.", "Chọn file");
            };
            card.Resize += delegate
            {
                choose.Location = new Point(card.Width - 149, 13);
                fileName.Width = fileInfo.Width = card.Width - 105; hint.Width = card.Width - 45;
            };
            return card;
        }

        private Card LinkCard()
        {
            Card card = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Label heading = LabelOf("2. Link tải trực tiếp", 11F, Palette.Ink, true); heading.SetBounds(22, 17, 280, 25); card.Controls.Add(heading);
            state.TextAlign = ContentAlignment.MiddleRight; card.Controls.Add(state);
            Card field = new Card { Fill = Color.FromArgb(247, 248, 253), Border = Palette.Line, Radius = 10 };
            field.Controls.Add(new IconView { Glyph = Glyph.Link, Tint = Palette.Purple, Bounds = new Rectangle(13, 12, 21, 21) });
            link.ReadOnly = true; link.BorderStyle = BorderStyle.None; link.BackColor = field.Fill; link.ForeColor = Palette.Ink;
            link.Font = new Font("Segoe UI", 9.5F); link.SetBounds(44, 13, 500, 24); link.AccessibleName = "Link tải trực tiếp công khai";
            link.Click += delegate { link.SelectAll(); }; field.Controls.Add(link);
            copy.Text = "Sao chép"; copy.Glyph = Glyph.Clipboard; copy.Quiet = true; copy.Size = new Size(123, 34); copy.Enabled = false;
            copy.Click += delegate
            {
                if (!sharing || string.IsNullOrEmpty(link.Text)) return;
                try { Clipboard.SetText(link.Text); detail.Text = "Đã sao chép. Gửi link này hoặc dán vào DirectLink Downloader để tải."; }
                catch (ExternalException) { MessageBox.Show(this, "Clipboard đang bận. Bạn có thể chọn link và nhấn Ctrl+C.", "Sao chép link"); }
            }; field.Controls.Add(copy); card.Controls.Add(field);
            field.Resize += delegate { copy.Location = new Point(field.Width - 130, 6); link.Width = Math.Max(1, field.Width - 186); };
            detail.AutoEllipsis = true; detail.SetBounds(23, 112, 700, 24); card.Controls.Add(detail);
            card.Resize += delegate { state.SetBounds(card.Width - 257, 17, 233, 24); field.SetBounds(22, 54, card.Width - 44, 46); detail.Width = card.Width - 46; };
            return card;
        }

        private Card StatsCard()
        {
            Card card = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Label heading = LabelOf("TRẠNG THÁI TRUYỀN", 8F, Palette.Muted, true); heading.SetBounds(23, 14, 250, 23); card.Controls.Add(heading);
            uptime.TextAlign = ContentAlignment.MiddleRight; card.Controls.Add(uptime);
            TableLayoutPanel columns = new TableLayoutPanel { ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            foreach (int i in new int[] { 1, 2, 3, 4 }) columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            columns.Controls.Add(Stat("ĐÃ GỬI", sent, Glyph.Upload, Palette.Purple), 0, 0);
            columns.Controls.Add(Stat("KẾT NỐI ĐANG TẢI", active, Glyph.Link, Palette.Blue), 1, 0);
            columns.Controls.Add(Stat("TỐC ĐỘ GỬI", speed, Glyph.Bolt, Palette.Green), 2, 0);
            columns.Controls.Add(Stat("YÊU CẦU XONG", requests, Glyph.Check, Color.FromArgb(225, 144, 45)), 3, 0);
            card.Controls.Add(columns);
            card.Resize += delegate { uptime.SetBounds(card.Width - 273, 14, 250, 23); columns.SetBounds(23, 49, card.Width - 46, 61); };
            tips.SetToolTip(requests, "Số yêu cầu GET đã truyền xong. Một trình tải nhiều luồng có thể tạo nhiều yêu cầu cho cùng một file.");
            return card;
        }

        private static Panel Stat(string caption, Label value, Glyph glyph, Color tint)
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            panel.Controls.Add(new IconView { Glyph = glyph, Tint = tint, Bounds = new Rectangle(0, 0, 15, 15) });
            Label label = LabelOf(caption, 7.2F, Palette.Muted, true); label.SetBounds(22, -1, 190, 20); panel.Controls.Add(label);
            value.SetBounds(-2, 22, 190, 34); value.AutoEllipsis = true; panel.Controls.Add(value);
            panel.Resize += delegate { label.Width = Math.Max(1, panel.Width - 25); value.Width = Math.Max(1, panel.Width - 8); };
            return panel;
        }

        private Panel Actions()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            create.Text = "Tạo link công khai"; create.Glyph = Glyph.Upload; create.Primary = true; create.SetBounds(0, 0, 225, 46); create.Click += StartShare;
            stop.Text = "Dừng chia sẻ"; stop.Glyph = Glyph.Stop; stop.SetBounds(237, 0, 169, 46); stop.Enabled = false;
            stop.Click += delegate { if (starting) CancelStart(); else StopShare("Đã dừng. Link cũ không còn tải được file."); };
            reveal.Text = "Vị trí file"; reveal.Glyph = Glyph.Folder; reveal.SetBounds(418, 0, 144, 46);
            reveal.Click += delegate
            {
                if (selectedPath == null) return;
                try { Process.Start("explorer.exe", "/select,\"" + selectedPath + "\""); }
                catch (Exception error) { MessageBox.Show(this, error.Message, "Vị trí file"); }
            };
            panel.Controls.AddRange(new Control[] { create, stop, reveal }); return panel;
        }

        private static Panel PrivacyNote()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            panel.Controls.Add(new IconView { Glyph = Glyph.Shield, Tint = Color.FromArgb(210, 132, 33), Bounds = new Rectangle(0, 18, 20, 20) });
            Label note = LabelOf("Ai có link đều có thể tải file bạn chọn. Giữ ứng dụng và máy tính hoạt động.\nTốc độ phụ thuộc mạng upload của bạn và Cloudflare; đóng app sẽ ngừng chia sẻ.", 8.8F, Palette.Muted, false);
            note.SetBounds(30, 15, 780, 48); panel.Controls.Add(note);
            panel.Resize += delegate { note.Width = panel.Width - 31; }; return panel;
        }

        private static Label LabelOf(string text, float size, Color color, bool bold)
        {
            return new Label { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color, BackColor = Color.Transparent, UseMnemonic = false };
        }

        private void SelectFile()
        {
            if (starting || sharing) return;
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "Chọn đúng một file để chia sẻ công khai", Filter = "Tất cả file (*.*)|*.*", Multiselect = false })
                if (dialog.ShowDialog(this) == DialogResult.OK) SetSelectedFile(dialog.FileName);
        }

        private void SetSelectedFile(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists) throw new IOException("File không tồn tại.");
                selectedPath = info.FullName;
                fileName.Text = info.Name; fileInfo.Text = Bytes(info.Length) + "  ·  " + info.FullName;
                tips.SetToolTip(fileName, info.FullName); tips.SetToolTip(fileInfo, info.FullName);
                state.Text = "SẴN SÀNG"; state.ForeColor = Palette.Purple;
                detail.Text = "Bấm Tạo link công khai để bắt đầu chia sẻ file này qua Cloudflare.";
                UpdateControls();
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Không chọn được file"); }
        }

        private async void StartShare(object sender, EventArgs args)
        {
            if (starting || sharing || selectedPath == null) return;
            session = new ShareSession(); startCancellation = new CancellationTokenSource();
            starting = true; sharing = false; link.Text = ""; copy.Enabled = false;
            state.Text = "ĐANG TẠO TUNNEL"; state.ForeColor = Color.FromArgb(204, 132, 37);
            detail.Text = "Đang mở localhost và kết nối Cloudflare. Có thể mất khoảng một phút...";
            sent.Text = "0 B"; active.Text = "0"; requests.Text = "0"; speed.Text = "0 B/s";
            lastBytes = 0; lastSeconds = 0; smoothedSpeed = 0; watch.Restart(); UpdateControls();
            try
            {
                string publicLink = await session.StartAsync(selectedPath, startCancellation.Token);
                link.Text = publicLink; tips.SetToolTip(link, publicLink);
                sharing = true; copy.Enabled = true; state.Text = "ĐANG CHIA SẺ · HTTPS"; state.ForeColor = Palette.Green;
                detail.Text = "Link đã được kiểm tra từ Internet. Sao chép và gửi cho người cần tải.";
            }
            catch (OperationCanceledException)
            {
                previousLog = session.GetLogs(); session.Dispose(); session = null;
                state.Text = "ĐÃ HỦY"; state.ForeColor = Palette.Muted; detail.Text = "Đã hủy tạo link. File không còn được phục vụ.";
                uptime.Text = "ĐÃ NGẮT KẾT NỐI"; active.Text = "0"; speed.Text = "0 B/s";
                watch.Stop();
            }
            catch (Exception error)
            {
                previousLog = session.GetLogs(); session.Dispose(); session = null;
                state.Text = "KẾT NỐI THẤT BẠI"; state.ForeColor = Color.FromArgb(214, 77, 95);
                uptime.Text = "NGOẠI TUYẾN"; active.Text = "0"; speed.Text = "0 B/s";
                detail.Text = "Chọn Nhật ký để xem lỗi. Kiểm tra Internet rồi thử tạo link lại."; watch.Stop();
                if (!closeRequested) MessageBox.Show(this, error.Message, "Chưa tạo được link", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                starting = false; startCancellation.Dispose(); startCancellation = null; UpdateControls();
                if (closeRequested) Close();
            }
        }

        private void CancelStart()
        {
            if (startCancellation == null) return;
            stop.Enabled = false; detail.Text = "Đang hủy kết nối và đóng máy chủ cục bộ..."; startCancellation.Cancel();
        }

        private void StopShare(string message)
        {
            sharing = false; copy.Enabled = false; link.Text = ""; tips.SetToolTip(link, "");
            if (session != null) { previousLog = session.GetLogs(); session.Dispose(); session = null; }
            watch.Stop(); active.Text = "0"; speed.Text = "0 B/s";
            uptime.Text = "ĐÃ NGẮT KẾT NỐI";
            state.Text = "ĐÃ NGỪNG CHIA SẺ"; state.ForeColor = Palette.Muted; detail.Text = message; UpdateControls();
        }

        private void RefreshStatistics()
        {
            if (session == null) return;
            if (sharing && !session.IsAlive) { StopShare("Tiến trình tunnel đã dừng. Bấm Tạo link để mở một phiên mới."); return; }
            ShareStatistics stats = session.GetStatistics();
            double elapsed = watch.Elapsed.TotalSeconds;
            double interval = elapsed - lastSeconds;
            if (interval > 0.25)
            {
                double current = Math.Max(0, stats.BytesSent - lastBytes) / interval;
                smoothedSpeed = smoothedSpeed * 0.5 + current * 0.5;
                lastBytes = stats.BytesSent; lastSeconds = elapsed;
            }
            sent.Text = Bytes(stats.BytesSent); active.Text = stats.ActiveConnections.ToString(); requests.Text = stats.CompletedRequests.ToString();
            speed.Text = Bytes((long)smoothedSpeed) + "/s";
            uptime.Text = (sharing ? "ONLINE  ·  " : "ĐANG KẾT NỐI  ·  ") + watch.Elapsed.ToString(@"hh\:mm\:ss");
        }

        private void UpdateControls()
        {
            choose.Enabled = !starting && !sharing;
            create.Enabled = !starting && !sharing && selectedPath != null;
            stop.Enabled = starting || sharing; reveal.Enabled = selectedPath != null;
        }

        private void ShowLogs()
        {
            using (Form dialog = new Form { Text = "DirectLink Share — Nhật ký kết nối", Size = new Size(950, 540), StartPosition = FormStartPosition.CenterParent })
            using (System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 500 })
            {
                TextBox output = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = false,
                    ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F), BackColor = Color.FromArgb(22, 29, 50), ForeColor = Color.FromArgb(211, 223, 245) };
                Action update = delegate { string text = session == null ? previousLog : session.GetLogs(); if (output.Text != text) { output.Text = text; output.SelectionStart = output.TextLength; output.ScrollToCaret(); } };
                dialog.Controls.Add(output); refresh.Tick += delegate { update(); }; update(); refresh.Start(); dialog.ShowDialog(this);
            }
        }

        private void ShowHelp()
        {
            using (Form dialog = new Form { Text = "DirectLink Share — Hướng dẫn & Giấy phép", Size = new Size(780, 560), StartPosition = FormStartPosition.CenterParent })
            {
                TabControl tabs = new TabControl { Dock = DockStyle.Fill };
                TabPage guide = new TabPage("Hướng dẫn"); TabPage license = new TabPage("Giấy phép cloudflared"); tabs.TabPages.Add(guide); tabs.TabPages.Add(license);
                string text = "DIRECTLINK SHARE\r\n\r\n1. Chọn hoặc kéo thả đúng một file.\r\n2. Bấm Tạo link công khai.\r\n3. Chờ trạng thái Đang chia sẻ, bấm Sao chép và gửi link.\r\n\r\n"
                    + "Link trả trực tiếp nội dung file, hỗ trợ HEAD và HTTP Range cho các trình tải nhiều luồng. Không có trang quảng cáo hoặc đăng nhập do ứng dụng chèn vào.\r\n\r\n"
                    + "Ai có link đều có thể tải file; chỉ chia sẻ file bạn muốn công khai. Máy chủ chỉ nghe trên loopback và chỉ phục vụ file được chọn. Không mở cả thư mục.\r\n\r\n"
                    + "Giữ máy và ứng dụng hoạt động. Dừng chia sẻ/đóng app sẽ ngừng phục vụ file và đóng tunnel. Mỗi phiên có một link mới; không phải link lưu trữ vĩnh viễn.\r\n\r\n"
                    + "Tốc độ bị giới hạn bởi mạng upload của bạn, mạng của người nhận và Cloudflare. Quick Tunnel không có cam kết thời gian hoạt động, phù hợp thử nghiệm/chia sẻ tạm thời. Không thể cam kết max speed.\r\n\r\n"
                    + "cloudflared " + TunnelProcess.Version + " chính thức được nén và nhúng trong EXE, xác minh SHA-256 rồi giải nén vào thư mục tạm riêng khi chạy. Không cần cài cloudflared hay tài khoản Cloudflare. Tường lửa cần cho phép kết nối ra Cloudflare qua TCP 7844.\r\n\r\n"
                    + "Nếu mất kết nối, xem Nhật ký. Nếu file bị phần mềm khác mở để ghi, hãy đóng file đó trước khi chia sẻ. Ứng dụng giữ file chỉ đọc trong phiên để các phần tải không bị trộn phiên bản.";
                guide.Controls.Add(new TextBox { Text = text, Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 10F) });
                string licenseText;
                using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("DirectLinkShare.cloudflared.LICENSE"))
                using (StreamReader reader = new StreamReader(resource)) licenseText = reader.ReadToEnd();
                license.Controls.Add(new TextBox { Text = licenseText.Replace("\n", "\r\n"), Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F) });
                dialog.Controls.Add(tabs); dialog.ShowDialog(this);
            }
        }

        private void OnWindowClosing(object sender, FormClosingEventArgs args)
        {
            if (starting) { closeRequested = true; args.Cancel = true; CancelStart(); return; }
            if (session != null) { session.Dispose(); session = null; }
            timer.Stop(); timer.Dispose(); tips.Dispose();
        }

        private static string Bytes(long value)
        {
            double n = Math.Max(0, value); string[] units = { "B", "KB", "MB", "GB", "TB" }; int index = 0;
            while (n >= 1024 && index < units.Length - 1) { n /= 1024; index++; }
            return n.ToString(index == 0 ? "0" : "0.0") + " " + units[index];
        }
    }
}
