using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class MainForm : Form {
    private readonly PclService service;
    private readonly Button search = ButtonOf("自动查找", "SearchButton"), choose = ButtonOf("选择 PCL 文件", "ChooseFileButton"), folder = ButtonOf("搜索文件夹", "ChooseFolderButton"), cancel = ButtonOf("取消搜索", "CancelButton");
    private readonly Button unlock = ButtonOf("恢复下载", "UnlockButton"), restore = ButtonOf("还原备份", "RestoreButton"), about = ButtonOf("关于", "AboutButton");
    private readonly DataGridView grid = new DataGridView();
    private readonly Label status = new Label(), detail = new Label();
    private readonly TextBox pathText = new TextBox(), log = new TextBox();
    private readonly ProgressBar progress = new ProgressBar();
    private readonly CheckBox showLog = new CheckBox();
    private readonly TableLayoutPanel layout = new TableLayoutPanel();
    private CancellationTokenSource cancellation;
    private bool busy, closingRequested;
    private PclInfo selected;
    private static readonly Color Ink = Color.FromArgb(32, 42, 58), Blue = Color.FromArgb(35, 96, 207);
    private static Button ButtonOf(string text, string name) {
        return new Button { Text = text, Name = name, AccessibleName = text, AutoSize = true, MinimumSize = new Size(94, 34), Padding = new Padding(10, 3, 10, 3), Margin = new Padding(0, 0, 10, 0), FlatStyle = FlatStyle.Flat, BackColor = Color.White, UseVisualStyleBackColor = false };
    }
    internal MainForm(PclService svc) {
        service = svc; Text = "PCL 下载恢复工具"; Name = "PclDownloadUnlockWindow";
        Font = new Font("Microsoft YaHei UI", 9F); ForeColor = Ink; BackColor = Color.FromArgb(246, 248, 251);
        ClientSize = new Size(920, 600); MinimumSize = new Size(820, 560); StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        layout.Dock = DockStyle.Fill; layout.Padding = new Padding(24, 20, 24, 18); layout.ColumnCount = 1; layout.RowCount = 9;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Controls.Add(new Label { Text = "恢复 PCL 的版本下载", AutoSize = true, Font = new Font(Font.FontFamily, 17, FontStyle.Bold), Margin = Padding.Empty }, 0, 0);
        heading.Controls.Add(new Label { Text = "自动查找  ·  备份后恢复  ·  随时还原", AutoSize = true, ForeColor = Color.FromArgb(95, 106, 121), Margin = new Padding(0, 4, 0, 0) }, 0, 1);
        layout.Controls.Add(heading, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 5, 0, 0) };
        toolbar.Controls.AddRange(new Control[] { search, choose, folder, cancel }); layout.Controls.Add(toolbar, 0, 1);
        grid.Name = "PclCandidates"; grid.AccessibleName = "PCL 安装列表"; grid.Dock = DockStyle.Fill; grid.Margin = Padding.Empty;
        grid.AutoGenerateColumns = false; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
        grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.RowHeadersVisible = false;
        grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.FixedSingle; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.ColumnHeadersHeight = 34; grid.RowTemplate.Height = 34; grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 240, 247); grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(221, 233, 253); grid.DefaultCellStyle.SelectionForeColor = Ink;
        grid.DefaultCellStyle.Padding = new Padding(5, 0, 0, 0); grid.GridColor = Color.FromArgb(229, 234, 241);
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "安装位置", DataPropertyName = "Path", FillWeight = 65 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "实际版本", DataPropertyName = "Version", FillWeight = 15 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", DataPropertyName = "State", FillWeight = 20 });
        layout.Controls.Add(grid, 0, 2);
        pathText.Name = "SelectedPath"; pathText.AccessibleName = "所选 PCL 路径"; pathText.ReadOnly = true; pathText.Dock = DockStyle.Fill; pathText.BackColor = Color.White; pathText.Margin = new Padding(0, 9, 0, 0); layout.Controls.Add(pathText, 0, 3);
        detail.Name = "SelectionDetail"; detail.Dock = DockStyle.Fill; detail.Text = "请选择 PCL。首次打开会自动搜索常见位置，不会修改文件。"; detail.Padding = new Padding(0, 6, 0, 0); detail.Margin = Padding.Empty; layout.Controls.Add(detail, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 0) };
        unlock.BackColor = Blue; unlock.ForeColor = Color.White; unlock.FlatAppearance.BorderColor = Blue;
        unlock.MinimumSize = new Size(124, 38); restore.MinimumSize = new Size(110, 38);
        actions.Controls.AddRange(new Control[] { unlock, restore, about }); layout.Controls.Add(actions, 0, 5);
        var statePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        statePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); statePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        status.Name = "OperationStatus"; status.AccessibleName = "操作状态"; status.Dock = DockStyle.Fill; status.AutoEllipsis = true; status.TextAlign = ContentAlignment.MiddleLeft; status.Text = "就绪";
        progress.Dock = DockStyle.Fill; progress.Margin = new Padding(4, 8, 0, 8); progress.Visible = false; progress.Style = ProgressBarStyle.Marquee;
        statePanel.Controls.Add(status, 0, 0); statePanel.Controls.Add(progress, 1, 0); layout.Controls.Add(statePanel, 0, 6);
        var foot = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        showLog.Text = "显示详细记录"; showLog.AutoSize = true; showLog.Margin = new Padding(0, 4, 14, 0);
        foot.Controls.Add(showLog); foot.Controls.Add(new Label { Text = "支持已验证的 PCL 2.99.0.0 构建；仅恢复 Minecraft 版本下载。", AutoSize = true, ForeColor = Color.FromArgb(105, 114, 128), Margin = new Padding(0, 5, 0, 0) });
        layout.Controls.Add(foot, 0, 7);
        log.Name = "DetailsLog"; log.AccessibleName = "详细记录"; log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.Dock = DockStyle.Fill; log.BackColor = Color.White; log.Margin = Padding.Empty;
        layout.Controls.Add(log, 0, 8); Controls.Add(layout);
        showLog.CheckedChanged += delegate { layout.RowStyles[8].Height = showLog.Checked ? 110 : 0; };
        search.Click += delegate { StartScan(PclService.DefaultRoots()); };
        choose.Click += delegate { ChooseFile(); };
        folder.Click += delegate { using (var dialog = new FolderBrowserDialog { Description = "选择要搜索 PCL 的文件夹", ShowNewFolderButton = false }) if (dialog.ShowDialog(this) == DialogResult.OK) StartScan(new[] { dialog.SelectedPath }); };
        cancel.Click += delegate { if (cancellation != null) cancellation.Cancel(); };
        grid.SelectionChanged += delegate { UpdateSelection(); };
        unlock.Click += delegate { Operate(false); }; restore.Click += delegate { Operate(true); };
        about.Click += delegate { ShowAbout(); };
        FormClosing += delegate(object sender, FormClosingEventArgs e) {
            if (!busy) return;
            e.Cancel = true; closingRequested = true;
            if (cancel.Enabled && cancellation != null) cancellation.Cancel();
            else SetStatus("正在完成文件操作，请稍候；完成后窗口会关闭。");
        };
        Shown += delegate { StartScan(PclService.DefaultRoots()); };
        SetBusy(false, false);
    }
    private void Log(string text) { log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); }
    private void SetStatus(string text) { status.Text = text; }
    private void Report(string text) {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Action(delegate { SetStatus(text); Log(text); }));
    }
    private void SetBusy(bool value, bool cancelable) {
        busy = value; search.Enabled = choose.Enabled = folder.Enabled = about.Enabled = grid.Enabled = !value;
        cancel.Enabled = value && cancelable; progress.Visible = value;
        unlock.Enabled = !value && selected != null && selected.CanUnlock;
        restore.Enabled = !value && selected != null && selected.IsPatched && selected.HasBackup;
        UseWaitCursor = value && !cancelable;
    }
    private void UpdateSelection() {
        selected = grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as PclInfo;
        pathText.Text = selected == null ? "" : selected.Path;
        detail.Text = selected == null ? "未找到 PCL。可以选择启动器 EXE，或搜索指定文件夹。" : selected.Detail;
        SetBusy(busy, busy && cancellation != null && cancel.Enabled);
    }
    private void SetCandidates(List<PclInfo> items, string selectPath) {
        grid.DataSource = null; grid.DataSource = items;
        if (items.Count > 0) {
            if (items.Count > 1 && selectPath == null) { grid.ClearSelection(); grid.CurrentCell = null; }
            else { int row = selectPath == null ? 0 : Math.Max(0, items.FindIndex(x => string.Equals(x.Path, selectPath, StringComparison.OrdinalIgnoreCase))); grid.CurrentCell = grid.Rows[row].Cells[0]; }
        }
        UpdateSelection();
        if (items.Count > 1 && selected == null) detail.Text = "找到多个 PCL 安装，请在列表中选择一个；工具不会同时修改多个副本。";
    }
    private async void RunWork(Func<CancellationToken, object> action, Action<object> complete, bool cancelable) {
        if (busy) return;
        cancellation = new CancellationTokenSource(); SetBusy(true, cancelable); SetStatus(cancelable ? "正在搜索…" : "正在检查…");
        try {
            object result = await Task.Run(() => action(cancellation.Token)); complete(result);
        } catch (OperationCanceledException) { SetStatus("搜索已取消，未修改任何文件。"); Log(status.Text); }
        catch (Exception ex) {
            SetStatus("操作未完成，请查看详细记录。"); showLog.Checked = true; Log(PclService.FriendlyError(ex));
            MessageBox.Show(this, PclService.FriendlyError(ex), "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } finally {
            cancellation.Dispose(); cancellation = null; SetBusy(false, false);
            if (closingRequested) Close();
        }
    }
    private void StartScan(IEnumerable<string> roots) {
        RunWork(token => service.Discover(roots, token, Report), value => {
            var result = (ScanResult)value; SetCandidates(result.Candidates, null);
            string text = "找到 " + result.Candidates.Count + " 个 PCL，已检查 " + result.Directories + " 个目录。";
            if (result.Truncated) text += "已达到搜索上限，可改为搜索指定文件夹。";
            SetStatus(text); Log(text);
        }, true);
    }
    private void ChooseFile() {
        using (var dialog = new OpenFileDialog { Title = "选择 PCL 启动器", Filter = "Windows 程序 (*.exe)|*.exe", CheckFileExists = true, Multiselect = false, RestoreDirectory = true, InitialDirectory = selected == null ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : Path.GetDirectoryName(selected.Path) }) {
            if (dialog.ShowDialog(this) != DialogResult.OK) return; string file = dialog.FileName;
            RunWork(token => service.Inspect(file), value => {
                var info = (PclInfo)value; SetCandidates(new List<PclInfo> { info }, info.Path); SetStatus(info.State); Log(info.Path + Environment.NewLine + info.Detail + Environment.NewLine + "SHA256: " + info.Hash); if (info.LastRecord != null) Log("上次操作记录：" + info.LastRecord);
            }, false);
        }
    }
    private void Operate(bool restoring) {
        if (selected == null) return; string file = selected.Path;
        RunWork(token => {
            var result = restoring ? service.Restore(file, Report) : service.Unlock(file, Report);
            PclInfo refreshed;
            try { refreshed = service.Inspect(file); }
            catch (Exception ex) {
                refreshed = new PclInfo { Path = file, Version = "未识别", State = "重新检查未完成", Detail = "文件操作已完成，但后续检查未能完成。请重新选择文件检查。" };
                result.Warning = (result.Warning == null ? "" : result.Warning + Environment.NewLine) + refreshed.Detail + " " + ex.Message;
            }
            return Tuple.Create(result, refreshed);
        }, value => {
            var data = (Tuple<OperationResult, PclInfo>)value; var result = data.Item1;
            SetCandidates(new List<PclInfo> { data.Item2 }, file); SetStatus(restoring ? "原始文件已还原" : "版本下载入口已恢复");
            Log(result.Message); if (result.Backup != null) Log("备份：" + result.Backup); if (result.Record != null) Log("操作记录：" + result.Record); if (result.Warning != null) { Log(result.Warning); showLog.Checked = true; }
            detail.Text = result.Message + (restoring ? "" : " 可打开 PCL 的“下载”页面测试。");
            if (result.Backup != null) detail.Text += "\n备份保存在 PCL 同一目录。";
        }, false);
    }
    private void ShowAbout() {
        using (var dialog = new Form { Text = "关于 PCL 下载恢复工具", Size = new Size(650, 480), MinimumSize = new Size(540, 360), StartPosition = FormStartPosition.CenterParent, Font = Font, MinimizeBox = false, MaximizeBox = false }) {
            string license;
            using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("PclDownloadUnlock.LICENSE-dnlib.txt"))) license = reader.ReadToEnd();
            var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, Text = "PCL 下载恢复工具 1.0.1\r\n\r\n面向 Windows 10/11，使用 .NET Framework 4.8。\r\n当前支持已核对 SHA256 的 PCL 2.99.0.0 构建。\r\n恢复版本下载入口，不修改整合包安装、账号登录或更新设置。\r\n文件校验成功不代表已验证 Minecraft 实际下载。\r\n\r\n本程序包含 dnlib 4.5.0，以下为依赖许可：\r\n\r\n" + license };
            dialog.Controls.Add(text); dialog.ShowDialog(this);
        }
    }
}
