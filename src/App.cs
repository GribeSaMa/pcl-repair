using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("PCL 一键修复")]
[assembly: AssemblyDescription("PCL 下载修复工具（第三方）")]
[assembly: AssemblyProduct("PCL 一键修复工具")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace PclRepair
{
    static class App
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) {
                MessageBox.Show("操作没有完成：" + e.Exception.Message, "PCL 一键修复", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            Application.Run(new MainForm());
        }
    }

    public sealed class MainForm : Form
    {
        private readonly Color blue = Color.FromArgb(36, 99, 235);
        private readonly Color ink = Color.FromArgb(24, 39, 62);
        private readonly Color muted = Color.FromArgb(102, 116, 137);
        private readonly List<LauncherInfo> entries = new List<LauncherInfo>();
        private readonly HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly DataGridView grid = new DataGridView();
        private readonly Label heading = new Label();
        private readonly Label summary = new Label();
        private readonly Label detail = new Label();
        private readonly Label notice = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Button refresh = new Button();
        private readonly Button deep = new Button();
        private readonly Button browse = new Button();
        private readonly Button repair = new Button();
        private readonly Button launch = new Button();
        private readonly Button folder = new Button();
        private readonly Button cancel = new Button();
        private CancellationTokenSource cancellation;
        private bool scanning;
        private bool repairing;
        private bool closing;
        private string repairedOutput;
        private float uiScale = 1F;

        public MainForm()
        {
            SuspendLayout();
            Text = "PCL 一键修复";
            ClientSize = new Size(900, 624);
            MinimumSize = new Size(800, 600);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 251);
            ForeColor = ink;
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            BuildLayout();
            Shown += async delegate { await StartScan(false); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (repairing) { e.Cancel = true; summary.Text = "正在完成文件校验，请稍候再关闭。"; return; }
                closing = true; if (cancellation != null) cancellation.Cancel();
            };
            DragEnter += delegate(object sender, DragEventArgs e) {
                if (!scanning && !repairing && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += async delegate(object sender, DragEventArgs e) {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null && paths.Length > 0) await AddManual(paths[0]);
            };
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) uiScale = graphics.DpiX / 96F;
            ScaleLayout(this, uiScale);
            ClientSize = new Size((int)(900 * uiScale), (int)(624 * uiScale));
            MinimumSize = new Size((int)(800 * uiScale), (int)(600 * uiScale));
            ResumeLayout(true);
        }

        private static void ScaleLayout(Control parent, float scale)
        {
            parent.SuspendLayout();
            foreach (Control child in parent.Controls)
            {
                ScaleLayout(child, scale);
                child.Bounds = new Rectangle((int)Math.Round(child.Left * scale), (int)Math.Round(child.Top * scale),
                    (int)Math.Round(child.Width * scale), (int)Math.Round(child.Height * scale));
                child.Padding = ScalePadding(child.Padding, scale);
                child.Margin = ScalePadding(child.Margin, scale);
            }
            var table = parent as TableLayoutPanel;
            if (table != null)
            {
                foreach (RowStyle row in table.RowStyles) if (row.SizeType == SizeType.Absolute) row.Height *= scale;
                foreach (ColumnStyle column in table.ColumnStyles) if (column.SizeType == SizeType.Absolute) column.Width *= scale;
            }
            var grid = parent as DataGridView;
            if (grid != null)
            {
                grid.ColumnHeadersHeight = (int)(grid.ColumnHeadersHeight * scale);
                grid.RowTemplate.Height = (int)(grid.RowTemplate.Height * scale);
                grid.DefaultCellStyle.Padding = ScalePadding(grid.DefaultCellStyle.Padding, scale);
                grid.ColumnHeadersDefaultCellStyle.Padding = ScalePadding(grid.ColumnHeadersDefaultCellStyle.Padding, scale);
                foreach (DataGridViewColumn column in grid.Columns)
                    if (column.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill) column.Width = (int)(column.Width * scale);
            }
            parent.ResumeLayout(false);
        }

        private static Padding ScalePadding(Padding value, float scale)
        {
            return new Padding((int)(value.Left * scale), (int)(value.Top * scale), (int)(value.Right * scale), (int)(value.Bottom * scale));
        }

        private Label LabelAt(Control parent, string text, int x, int y, int width, int height, float size, Color color)
        {
            var label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height),
                ForeColor = color, Font = new Font("Microsoft YaHei UI", size), AutoEllipsis = true };
            parent.Controls.Add(label);
            return label;
        }

        private void StyleButton(Button button, string text, int width, bool primary)
        {
            button.Text = text;
            button.Size = new Size(width, 38);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(215, 222, 232);
            button.BackColor = primary ? blue : Color.White;
            button.ForeColor = primary ? Color.White : ink;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
            button.Margin = new Padding(0, 0, 10, 0);
        }

        private void BuildLayout()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Color.White };
            Controls.Add(header);
            var logo = new Panel { Location = new Point(28, 27), Size = new Size(52, 52), BackColor = blue };
            logo.Paint += delegate(object sender, PaintEventArgs e) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.ScaleTransform(uiScale, uiScale);
                using (var pen = new Pen(Color.White, 3.3F))
                {
                    e.Graphics.DrawLine(pen, 26, 11, 26, 33);
                    e.Graphics.DrawLine(pen, 18, 25, 26, 33);
                    e.Graphics.DrawLine(pen, 34, 25, 26, 33);
                    e.Graphics.DrawLines(pen, new[] { new Point(13, 33), new Point(13, 40), new Point(39, 40), new Point(39, 33) });
                }
            };
            header.Controls.Add(logo);
            LabelAt(header, "PCL 一键修复", 96, 23, 400, 35, 20, ink).Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold);
            LabelAt(header, "自动定位启动器，恢复原版游戏下载入口", 98, 65, 550, 25, 10, muted);
            var tag = LabelAt(header, "单文件  ·  免安装  ·  离线修复", 626, 40, 244, 28, 9, muted);
            header.Resize += delegate { tag.Left = header.ClientSize.Width - tag.Width - (int)(28 * uiScale); };

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 18, 28, 18),
                ColumnCount = 1, RowCount = 7, BackColor = BackColor };
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
            Controls.Add(body);
            body.BringToFront();

            heading.Text = "正在定位电脑上的 PCL…";
            heading.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            heading.Dock = DockStyle.Fill;
            body.Controls.Add(heading, 0, 0);

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            StyleButton(refresh, "重新查找", 110, false);
            StyleButton(deep, "深度查找", 110, false);
            StyleButton(browse, "手动选择 PCL", 142, false);
            StyleButton(cancel, "停止查找", 110, false);
            toolbar.Controls.AddRange(new Control[] { refresh, deep, browse, cancel });
            body.Controls.Add(toolbar, 0, 1);
            refresh.Click += async delegate { await StartScan(false); };
            deep.Click += async delegate { await StartScan(true); };
            browse.Click += async delegate {
                using (var dialog = new OpenFileDialog { Title = "选择 PCL 启动器", Filter = "启动器程序 (*.exe)|*.exe", CheckFileExists = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) await AddManual(dialog.FileName);
            };
            cancel.Click += delegate { if (cancellation != null) cancellation.Cancel(); };

            grid.Dock = DockStyle.Fill;
            grid.Margin = Padding.Empty;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.AutoGenerateColumns = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true;
            grid.MultiSelect = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 38;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(235, 240, 248), ForeColor = muted, Padding = new Padding(10, 0, 5, 0) };
            grid.DefaultCellStyle = new DataGridViewCellStyle { ForeColor = ink, BackColor = Color.White,
                SelectionBackColor = Color.FromArgb(227, 237, 255), SelectionForeColor = ink, Padding = new Padding(10, 0, 5, 0) };
            grid.RowTemplate.Height = 46;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Color.FromArgb(238, 241, 247);
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Path", HeaderText = "启动器位置", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Version", HeaderText = "版本", Width = 144, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "状态", Width = 146, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.SelectionChanged += delegate { UpdateSelection(); };
            body.Controls.Add(grid, 0, 2);

            var status = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            summary.Location = new Point(0, 10);
            summary.Size = new Size(830, 22);
            summary.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            summary.ForeColor = muted;
            detail.Location = new Point(0, 33);
            detail.Size = new Size(830, 20);
            detail.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            detail.AutoEllipsis = true;
            detail.ForeColor = muted;
            detail.Font = new Font("Microsoft YaHei UI", 8F);
            status.Controls.AddRange(new Control[] { summary, detail });
            body.Controls.Add(status, 0, 3);
            progress.Dock = DockStyle.Fill;
            progress.Height = 4;
            progress.Margin = new Padding(0, 3, 0, 5);
            progress.Style = ProgressBarStyle.Marquee;
            progress.Visible = false;
            body.Controls.Add(progress, 0, 4);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 0) };
            StyleButton(repair, "一键修复", 154, true);
            StyleButton(launch, "打开修复版", 138, false);
            StyleButton(folder, "打开位置", 114, false);
            actions.Controls.AddRange(new Control[] { repair, launch, folder });
            body.Controls.Add(actions, 0, 5);
            repair.Click += async delegate { await RepairSelected(); };
            launch.Click += delegate {
                string output = repairedOutput;
                if (String.IsNullOrEmpty(output)) return;
                try
                {
                    if (RepairEngine.HashFile(output) != RepairEngine.FixedHash) throw new IOException("文件内容已改变，请重新查找后再试。");
                    Process.Start(new ProcessStartInfo(output) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(output) });
                }
                catch (Exception e) { ShowError(e); }
            };
            folder.Click += delegate {
                var item = Selected();
                if (item != null) try { Process.Start("explorer.exe", "/select,\"" + item.Path + "\""); } catch (Exception e) { ShowError(e); }
            };
            notice.Text = "支持 PCL 2.99.0.0（413）的原版游戏下载。修复版另存同目录，原文件保留。\r\n第三方本地工具 · 不支持整合包导入恢复 · 可将启动器拖入窗口选择";
            notice.ForeColor = muted;
            notice.Font = new Font("Microsoft YaHei UI", 8.5F);
            notice.Dock = DockStyle.Fill;
            notice.Margin = new Padding(0, 10, 0, 0);
            body.Controls.Add(notice, 0, 6);
            UpdateButtons();
        }

        private LauncherInfo Selected()
        {
            return grid.SelectedRows.Count == 0 ? null : grid.SelectedRows[0].Tag as LauncherInfo;
        }

        private void UpdateSelection()
        {
            var item = Selected();
            repairedOutput = item != null && item.State == LauncherState.Repaired ? item.OutputPath : null;
            if (!scanning && !repairing && item != null)
                detail.Text = item.State == LauncherState.Repaired ? "修复版：" + item.OutputPath : item.Path;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var item = Selected();
            refresh.Enabled = deep.Enabled = !scanning && !repairing;
            browse.Enabled = !scanning && !repairing;
            cancel.Visible = scanning;
            repair.Enabled = !scanning && !repairing && item != null && item.State == LauncherState.Ready;
            repair.Text = repairing ? "正在修复…" : "一键修复";
            launch.Enabled = !repairing && !scanning && !String.IsNullOrEmpty(repairedOutput);
            folder.Enabled = !repairing && item != null;
        }

        private void AddResult(LauncherInfo info)
        {
            if (!known.Add(info.Path)) return;
            // Show one item per launcher pair; prefer the original as the selectable source.
            var same = entries.FirstOrDefault(x => String.Equals(x.OutputPath, info.OutputPath, StringComparison.OrdinalIgnoreCase) &&
                ((x.IsOriginalBuild && String.Equals(info.Path, info.OutputPath, StringComparison.OrdinalIgnoreCase) && info.State == LauncherState.Repaired) ||
                 (info.IsOriginalBuild && String.Equals(x.Path, x.OutputPath, StringComparison.OrdinalIgnoreCase) && x.State == LauncherState.Repaired)));
            if (same != null)
            {
                if (String.Equals(same.Path, same.OutputPath, StringComparison.OrdinalIgnoreCase) && !String.Equals(info.Path, info.OutputPath, StringComparison.OrdinalIgnoreCase))
                {
                    entries.Remove(same);
                    entries.Add(info);
                    RebuildRows(info.Path);
                }
                return;
            }
            entries.Add(info);
            var rowIndex = grid.Rows.Add(info.Path, info.Version, info.Status);
            grid.Rows[rowIndex].Tag = info;
            PaintStatus(grid.Rows[rowIndex], info);
            heading.Text = "已找到 " + entries.Count + " 处 PCL";
            UpdateButtons();
        }

        private void PaintStatus(DataGridViewRow row, LauncherInfo info)
        {
            row.Cells[2].Style.ForeColor = info.State == LauncherState.Ready ? blue :
                info.State == LauncherState.Repaired ? Color.FromArgb(20, 130, 90) : Color.FromArgb(159, 101, 36);
        }

        private void RebuildRows(string selected)
        {
            grid.Rows.Clear();
            foreach (var item in entries.OrderBy(x => x.State == LauncherState.Ready ? 0 : x.State == LauncherState.Repaired ? 1 : 2))
            {
                var row = grid.Rows[grid.Rows.Add(item.Path, item.Version, item.Status)];
                row.Tag = item;
                PaintStatus(row, item);
                if (String.Equals(item.Path, selected, StringComparison.OrdinalIgnoreCase)) row.Selected = true;
            }
            UpdateSelection();
        }

        private async Task StartScan(bool full)
        {
            if (scanning || repairing) return;
            scanning = true;
            summary.ForeColor = muted;
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            entries.Clear(); known.Clear(); grid.Rows.Clear(); repairedOutput = null;
            heading.Text = full ? "正在深度查找本地磁盘…" : "正在查找常用位置…";
            summary.Text = full ? "查找期间可以停止；完成后选择需要修复的启动器。" : "优先查找工具所在目录、桌面、下载、文档及常用游戏目录。";
            detail.Text = "也可以停止查找后，手动选择或拖入 PCL 启动器。";
            progress.Visible = true;
            UpdateButtons();
            bool stopped = false;
            try
            {
                var roots = full ? DriveInfo.GetDrives().Where(d => { try { return d.IsReady && d.DriveType == DriveType.Fixed; } catch (IOException) { return false; } }).Select(d => d.RootDirectory.FullName).ToArray()
                    : LauncherScanner.QuickRoots(Application.StartupPath).ToArray();
                var report = new Progress<Tuple<string, int>>(p => { if (!closing && scanning) detail.Text = "已检查 " + p.Item2 + " 个文件夹" + (p.Item1.Length == 0 ? "" : " · " + p.Item1); });
                var found = new Progress<LauncherInfo>(i => { if (!closing && scanning) AddResult(i); });
                await Task.Run(() => LauncherScanner.Scan(roots, full ? 64 : 6, token,
                    i => ((IProgress<LauncherInfo>)found).Report(i),
                    (p, count) => ((IProgress<Tuple<string, int>>)report).Report(Tuple.Create(p, count)), full));
            }
            catch (OperationCanceledException) { stopped = true; }
            catch (Exception e) { if (!closing) ShowError(e); }
            finally
            {
                scanning = false;
                cancellation.Dispose(); cancellation = null;
                if (!closing)
                {
                    progress.Visible = false;
                    heading.Text = entries.Count == 0 ? (stopped ? "查找已停止" : "暂未找到 PCL") : "已找到 " + entries.Count + " 处 PCL";
                    summary.Text = entries.Count == 0 ? "请选择“深度查找”，或通过“手动选择 PCL”指定文件。" : (stopped ? "已停止查找，现有结果可以使用。" : "查找完成。选择启动器后，点击“一键修复”。");
                    detail.Text = "原文件保留；修复版生成在启动器同一文件夹。";
                    RebuildRows(null);
                    if (grid.Rows.Count > 0) { grid.Rows[0].Selected = true; UpdateSelection(); }
                    UpdateButtons();
                }
            }
        }

        private async Task AddManual(string path)
        {
            if (scanning || repairing) return;
            try
            {
                if (!File.Exists(path) || !String.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("请选择 PCL 的 .exe 启动器文件。");
                repairing = true; UpdateButtons();
                var info = await Task.Run(() => RepairEngine.Inspect(path));
                if (closing) return;
                AddResult(info);
                RebuildRows(info.Path);
                heading.Text = "已找到 " + entries.Count + " 处 PCL";
                summary.Text = info.State == LauncherState.Unsupported ? "这个文件不匹配当前支持的构建，不会进行修改。" : "已选择启动器，可在下方进行操作。";
            }
            catch (Exception e) { if (!closing) ShowError(e); }
            finally { repairing = false; if (!closing) { UpdateSelection(); UpdateButtons(); } }
        }

        private async Task RepairSelected()
        {
            var item = Selected();
            if (item == null || scanning || repairing) return;
            repairing = true; progress.Visible = true; UpdateButtons();
            summary.ForeColor = muted;
            summary.Text = "正在校验版本并生成修复副本…";
            try
            {
                string result = await Task.Run(() => RepairEngine.Repair(item.Path));
                if (closing) return;
                item.State = LauncherState.Repaired;
                item.OutputPath = result;
                RebuildRows(item.Path);
                summary.ForeColor = Color.FromArgb(20, 130, 90);
                summary.Text = "修复完成，已校验文件。点击“打开修复版”即可使用。";
                detail.Text = result;
                heading.Text = "下载入口已修复";
            }
            catch (UnauthorizedAccessException)
            {
                if (!closing) ShowError(new IOException("启动器所在文件夹无法写入。请将 PCL 复制到普通文件夹，再选择该文件修复。"));
            }
            catch (Exception e) { if (!closing) ShowError(e); }
            finally { repairing = false; if (!closing) { progress.Visible = false; UpdateSelection(); UpdateButtons(); } }
        }

        private void ShowError(Exception error)
        {
            summary.ForeColor = Color.FromArgb(172, 87, 43);
            summary.Text = "操作未完成，原文件保留。";
            MessageBox.Show(this, error.Message, "PCL 一键修复", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
