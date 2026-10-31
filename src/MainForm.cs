using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DupFinder.Core;

namespace DupFinder
{
    public sealed class MainForm : Form
    {
        private enum KeepMode
        {
            Newest,
            Oldest,
            Largest,
            ShortestPath,
            None
        }

        // ---- 目录区 ----
        private TextBox _txtDir;
        private Button _btnBrowse;
        private Button _btnScan;
        private Button _btnCancel;

        // ---- 选项区 ----
        private CheckBox _chkRecursive;
        private CheckBox _chkIgnoreExt;
        private CheckBox _chkFiles;
        private CheckBox _chkDirs;
        private CheckBox _chkSkipSystem;
        private CheckBox _chkFolderSize;
        private NumericUpDown _numMin;
        private ComboBox _cmbKeep;
        private Button _btnSmart;

        // ---- 结果区 ----
        private TreeView _tree;
        private Button _btnSelectAll;
        private Button _btnSelectNone;
        private Button _btnInvert;
        private Button _btnExpand;
        private Button _btnCollapse;
        private Button _btnExport;
        private Button _btnDelete;

        // ---- 状态区 ----
        private ToolStripStatusLabel _lblStatus;
        private ToolStripProgressBar _progress;

        private ScanResult _result;
        private string _root;
        private CancellationTokenSource _cts;
        private bool _suppressCheck;
        private Control _dirPanel;
        private Control _optionsPanel;
        private Control _actionPanel;
        private Control _treeHost;

        public MainForm(string initialDirectory)
        {
            BuildUi();
            if (!string.IsNullOrEmpty(initialDirectory)) _txtDir.Text = initialDirectory;
        }

        #region 界面构建

        private void BuildUi()
        {
            Text = "重名文件/文件夹清理工具  DupFinder 1.0";
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, SystemFonts.MessageBoxFont.Size);
            AllowDrop = true;

            // 高分屏（125%/150%/200%+ 缩放）下：Screen.WorkingArea 是物理像素，
            // 而窗口 Size 是逻辑像素，必须先除以缩放系数，否则窗口物理尺寸会超出屏幕、
            // 右侧与底部被屏幕边缘切掉。
            float scale = Math.Max(1f, NativeDpi.SystemScale());
            Rectangle area = Screen.PrimaryScreen != null
                ? Screen.PrimaryScreen.WorkingArea
                : new Rectangle(0, 0, 1280, 720);
            int width = (int)Math.Min(1080, (area.Width - 40) / scale);
            int height = (int)Math.Min(720, (area.Height - 40) / scale);
            if (width < 480) width = (int)Math.Max(320, (area.Width - 20) / scale);
            if (height < 360) height = (int)Math.Max(260, (area.Height - 20) / scale);
            Size = new Size(width, height);
            MinimumSize = new Size(Math.Min(680, width), Math.Min(460, height));

            // 用 Dock 布局 + AutoSize，所有区域高度随内容/窗口宽度自适应，不会被裁切
            _treeHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
            _treeHost.Controls.Add(BuildTree());

            _optionsPanel = BuildOptionsPanel();
            _dirPanel = BuildDirPanel();
            _actionPanel = BuildActionPanel();

            var status = new StatusStrip { SizingGrip = false };
            _lblStatus = new ToolStripStatusLabel { Text = "请选择要扫描的目录", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _progress = new ToolStripProgressBar { Visible = false, Width = 220, Style = ProgressBarStyle.Continuous };
            status.Items.Add(_lblStatus);
            status.Items.Add(_progress);

            // Dock 顺序：后添加的先占据外缘（顶部/底部）
            Controls.Add(_treeHost);
            Controls.Add(_optionsPanel);
            Controls.Add(_dirPanel);
            Controls.Add(_actionPanel);
            Controls.Add(status);

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
        }

        /// <summary>
        /// 把窗口收缩到「它实际所在显示器」的可用区域内并居中。
        /// 多显示器时各屏 DPI 不同（主屏 100%、副屏 150%），必须按该屏的真实缩放系数换算，
        /// 否则窗口物理尺寸会超出屏幕，右侧和底部被切掉。
        /// </summary>
        private void FitToScreen()
        {
            try
            {
                Screen screen = Screen.FromControl(this);
                Rectangle wa = screen.WorkingArea;                 // 物理像素
                float s = Math.Max(1f, NativeDpi.ScaleForWindow(Handle));
                int maxW = Math.Max(480, (int)((wa.Width - 24) / s));
                int maxH = Math.Max(360, (int)((wa.Height - 24) / s));

                int w = Math.Min(Width, maxW);
                int h = Math.Min(Height, maxH);

                // 上（目录+选项）下（按钮+状态栏）区域吃掉空间后，列表区至少留 140
                int need = _dirPanel.Height + _optionsPanel.Height + _actionPanel.Height + 40 + 140;
                if (h < need) h = Math.Min(need, maxH);

                if (w != Width || h != Height) Size = new Size(w, h);

                int left = wa.Left + (wa.Width - (int)(w * s)) / 2;
                int top = wa.Top + (wa.Height - (int)(h * s)) / 2;
                Location = new Point(Math.Max(wa.Left, left), Math.Max(wa.Top, top));

                MinimumSize = new Size(Math.Min(680, w), Math.Min(460, h));
            }
            catch (Exception) { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (Environment.GetEnvironmentVariable("DUPFINDER_SHOT") == "1")
            {
                try
                {
                    using (var bmp = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)))
                    {
                        DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                        bmp.Save(Path.Combine(Path.GetTempPath(), "dupfinder_shot.png"), ImageFormat.Png);
                    }
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "dupfinder_shot_err.txt"), ex.ToString()); }
                    catch (Exception) { }
                }
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            FitToScreen();

            if (Environment.GetEnvironmentVariable("DUPFINDER_DEBUG") == "1")
            {
                var sb = new StringBuilder();
                var scr = Screen.FromControl(this);
                sb.AppendLine("Size=" + Size + " ClientSize=" + ClientSize + " DeviceDpi=" + DeviceDpi +
                              " Location=" + Location);
                sb.AppendLine("Screen=" + scr.DeviceName + " bounds=" + scr.Bounds + " workarea=" + scr.WorkingArea +
                              " scale=" + NativeDpi.ScaleForWindow(Handle));
                sb.AppendLine("dir    = " + _dirPanel.Bounds);
                sb.AppendLine("options= " + _optionsPanel.Bounds);
                sb.AppendLine("tree   = " + _treeHost.Bounds);
                sb.AppendLine("action = " + _actionPanel.Bounds);
                foreach (Control c in Controls)
                    sb.AppendLine("  child: " + c.GetType().Name + " " + c.Bounds + " dock=" + c.Dock);
                try
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "dupfinder_layout.txt"), sb.ToString());
                }
                catch (Exception) { }
            }
        }

        private Panel BuildDirPanel()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 6, 8, 0) };

            var tlp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // 标签
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // 文本框（自适应剩余宽度）
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // 浏览
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // 扫描
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // 取消

            var lbl = new Label
            {
                Text = "扫描目录：",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 8, 6, 0)
            };

            _txtDir = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 8, 0) };
            _txtDir.AllowDrop = true;
            _txtDir.DragEnter += OnDragEnter;
            _txtDir.DragDrop += OnDragDrop;

            _btnBrowse = NewSmallButton("浏览...");
            _btnBrowse.Click += (s, e) => BrowseFolder();

            _btnScan = NewSmallButton("开始扫描");
            _btnScan.Click += (s, e) => StartScan();

            _btnCancel = NewSmallButton("取消");
            _btnCancel.Enabled = false;
            _btnCancel.Click += (s, e) => { if (_cts != null) _cts.Cancel(); };

            tlp.Controls.Add(lbl, 0, 0);
            tlp.Controls.Add(_txtDir, 1, 0);
            tlp.Controls.Add(_btnBrowse, 2, 0);
            tlp.Controls.Add(_btnScan, 3, 0);
            tlp.Controls.Add(_btnCancel, 4, 0);

            panel.Controls.Add(tlp);
            return panel;
        }

        private Control BuildOptionsPanel()
        {
            // 直接用 AutoSize 的 FlowLayoutPanel：窗口变窄时自动换行增高。
            // 不要再套一层 AutoSize 的 Panel——Panel 对 Dock.Top 子控件的高度计算偏小会把内容裁掉。
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(8, 2, 8, 4)
            };

            _chkRecursive = NewOptionCheck("递归子目录", true, "连同所有子目录一起扫描");
            _chkIgnoreExt = NewOptionCheck("忽略扩展名匹配", true,
                "去掉扩展名后比对，例如 aaa 文件夹、aaa.zip、aaa.exe 视为同名");
            _chkFiles = NewOptionCheck("包含文件", true, "把文件纳入重名比对");
            _chkDirs = NewOptionCheck("包含文件夹", true, "把文件夹纳入重名比对");
            _chkSkipSystem = NewOptionCheck("跳过系统目录", true,
                "跳过回收站、System Volume Information、.git、.svn 等目录");
            _chkFolderSize = NewOptionCheck("统计文件夹体积", false, "勾选后扫描会明显变慢");

            var lblMin = new Label { Text = "最少同名数：", AutoSize = true, Margin = new Padding(10, 8, 0, 2), Anchor = AnchorStyles.Left };
            _numMin = new NumericUpDown { Minimum = 2, Maximum = 99, Value = 2, Width = 54, Margin = new Padding(2, 5, 10, 2) };

            var lblKeep = new Label { Text = "每组保留：", AutoSize = true, Margin = new Padding(10, 8, 0, 2), Anchor = AnchorStyles.Left };
            _cmbKeep = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 116,
                DropDownWidth = 160,
                Margin = new Padding(2, 4, 8, 2)
            };
            _cmbKeep.Items.AddRange(new object[]
            {
                new TagItem<KeepMode>(KeepMode.Newest, "最新修改"),
                new TagItem<KeepMode>(KeepMode.Oldest, "最早修改"),
                new TagItem<KeepMode>(KeepMode.Largest, "体积最大"),
                new TagItem<KeepMode>(KeepMode.ShortestPath, "路径最短"),
                new TagItem<KeepMode>(KeepMode.None, "不保留(全选)")
            });
            _cmbKeep.SelectedIndex = 0;

            _btnSmart = new Button
            {
                Text = "智能选择",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(88, 28),
                Padding = new Padding(8, 0, 8, 0),
                Margin = new Padding(2, 3, 4, 2)
            };
            _btnSmart.Click += (s, e) => SmartSelect();

            flow.Controls.Add(_chkRecursive);
            flow.Controls.Add(_chkIgnoreExt);
            flow.Controls.Add(_chkFiles);
            flow.Controls.Add(_chkDirs);
            flow.Controls.Add(_chkSkipSystem);
            flow.Controls.Add(_chkFolderSize);
            flow.Controls.Add(lblMin);
            flow.Controls.Add(_numMin);
            flow.Controls.Add(lblKeep);
            flow.Controls.Add(_cmbKeep);
            flow.Controls.Add(_btnSmart);

            return flow;
        }

        private static CheckBox NewOptionCheck(string text, bool checkedState, string tip)
        {
            var chk = new CheckBox
            {
                Text = text,
                Checked = checkedState,
                AutoSize = true,
                Margin = new Padding(2, 6, 12, 2)
            };
            if (!string.IsNullOrEmpty(tip))
            {
                var tooltip = new ToolTip();
                tooltip.SetToolTip(chk, tip);
            }
            return chk;
        }

        private static Button NewSmallButton(string text)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(84, 30),
                Padding = new Padding(8, 0, 8, 0),
                Margin = new Padding(0, 3, 6, 0),
                Anchor = AnchorStyles.Left
            };
        }

        private Control BuildTree()
        {
            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                CheckBoxes = true,
                FullRowSelect = true,
                HideSelection = false,
                ShowNodeToolTips = true,
                ShowLines = false,
                BorderStyle = BorderStyle.FixedSingle
            };
            _tree.AfterCheck += OnAfterCheck;
            _tree.NodeMouseDoubleClick += OnNodeDoubleClick;
            _tree.MouseUp += OnTreeMouseUp;

            var panel = new Panel { Dock = DockStyle.Fill };
            panel.Controls.Add(_tree);
            return panel;
        }

        private Control BuildActionPanel()
        {
            // 同样是 AutoSize 的 FlowLayoutPanel：窄窗口下按钮自动换行，高度随之增长
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(8, 2, 8, 4)
            };

            _btnSelectAll = NewActionButton("全选");
            _btnSelectAll.Click += (s, e) => SetAllChecked(true);

            _btnSelectNone = NewActionButton("全不选");
            _btnSelectNone.Click += (s, e) => SetAllChecked(false);

            _btnInvert = NewActionButton("反选");
            _btnInvert.Click += (s, e) => InvertSelection();

            _btnExpand = NewActionButton("展开全部");
            _btnExpand.Click += (s, e) => _tree.ExpandAll();

            _btnCollapse = NewActionButton("折叠全部");
            _btnCollapse.Click += (s, e) => { _tree.CollapseAll(); };

            _btnExport = NewActionButton("导出清单");
            _btnExport.Click += (s, e) => ExportList();

            _btnDelete = new Button
            {
                Text = "删除选中项到回收站",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(160, 32),
                Padding = new Padding(12, 0, 12, 0),
                Margin = new Padding(14, 3, 4, 3),
                Enabled = false,
                BackColor = Color.FromArgb(0xC0, 0x2C, 0x2C),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnDelete.FlatAppearance.BorderSize = 0;
            _btnDelete.Click += (s, e) => DeleteSelected();

            flow.Controls.Add(_btnSelectAll);
            flow.Controls.Add(_btnSelectNone);
            flow.Controls.Add(_btnInvert);
            flow.Controls.Add(_btnExpand);
            flow.Controls.Add(_btnCollapse);
            flow.Controls.Add(_btnExport);
            flow.Controls.Add(_btnDelete);

            SetActionsEnabled(false);
            return flow;
        }

        private static Button NewActionButton(string text)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(88, 30),
                Padding = new Padding(8, 0, 8, 0),
                Margin = new Padding(0, 3, 6, 3)
            };
        }

        private void SetActionsEnabled(bool enabled)
        {
            _btnSelectAll.Enabled = enabled;
            _btnSelectNone.Enabled = enabled;
            _btnInvert.Enabled = enabled;
            _btnExpand.Enabled = enabled;
            _btnCollapse.Enabled = enabled;
            _btnExport.Enabled = enabled;
            _btnDelete.Enabled = false;
        }

        private sealed class TagItem<T>
        {
            public T Value { get; private set; }
            private readonly string _text;
            public TagItem(T value, string text) { Value = value; _text = text; }
            public override string ToString() { return _text; }
        }

        #endregion

        #region 扫描

        private void BrowseFolder()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择要扫描的目录";
                if (!string.IsNullOrWhiteSpace(_txtDir.Text) && Directory.Exists(_txtDir.Text))
                    dlg.SelectedPath = _txtDir.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _txtDir.Text = dlg.SelectedPath;
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            if (Directory.Exists(files[0])) _txtDir.Text = files[0];
        }

        private async void StartScan()
        {
            string dir = (_txtDir.Text ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(dir))
            {
                MessageBox.Show(this, "请先选择要扫描的目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!Directory.Exists(dir))
            {
                MessageBox.Show(this, "目录不存在：\r\n" + dir, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var options = new ScanOptions
            {
                Root = dir,
                Recursive = _chkRecursive.Checked,
                IgnoreExtension = _chkIgnoreExt.Checked,
                IncludeFiles = _chkFiles.Checked,
                IncludeDirectories = _chkDirs.Checked,
                SkipSystemDirs = _chkSkipSystem.Checked,
                ComputeFolderSize = _chkFolderSize.Checked,
                MinGroupSize = (int)_numMin.Value
            };

            _btnScan.Enabled = false;
            _btnCancel.Enabled = true;
            _btnDelete.Enabled = false;
            _progress.Visible = true;
            _progress.Style = ProgressBarStyle.Marquee;
            _tree.Nodes.Clear();
            _result = null;
            _root = dir;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var progress = new Progress<ScanProgress>(p =>
            {
                _lblStatus.Text = "扫描中：已发现文件 " + p.Files + " 个，文件夹 " + p.Directories + " 个...";
            });

            try
            {
                var result = await Task.Run(() => new Scanner().Scan(options, token, progress));

                _result = result;
                Render(result);
                SetActionsEnabled(result.Groups.Count > 0);

                string text = "完成：" + result.Groups.Count + " 个重名组 / " + result.TotalItems + " 项" +
                              "（扫描文件 " + result.ScannedFiles + " 个、文件夹 " + result.ScannedDirectories + " 个，用时 " +
                              result.Elapsed.TotalSeconds.ToString("0.0") + " 秒）";
                if (result.Cancelled) text = "已取消 —— " + text;
                _lblStatus.Text = text;

                if (result.Warnings.Count > 0)
                {
                    MessageBox.Show(this,
                        "扫描完成，但有 " + result.Warnings.Count + " 条警告（通常是无访问权限的目录）：\r\n\r\n" +
                        string.Join("\r\n", result.Warnings.ToArrayOrEmpty()),
                        "扫描警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (result.Groups.Count == 0)
                {
                    MessageBox.Show(this, "没有发现重名的文件或文件夹。", "扫描完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "扫描失败：" + ex.Message;
                MessageBox.Show(this, "扫描失败：\r\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _progress.Visible = false;
                _btnScan.Enabled = true;
                _btnCancel.Enabled = false;
                _cts = null;
            }
        }

        private void Render(ScanResult result)
        {
            _tree.BeginUpdate();
            _suppressCheck = true;
            try
            {
                _tree.Nodes.Clear();
                foreach (var group in result.Groups)
                {
                    var node = new TreeNode(group.Key + "    (" + group.Items.Count + " 项)")
                    {
                        Tag = group,
                        Checked = false,
                        ToolTipText = group.Items.Count + " 个同名项：\r\n" + group.Key
                    };
                    foreach (var item in group.Items)
                    {
                        var child = new TreeNode(BuildItemText(item, result))
                        {
                            Tag = item,
                            Checked = false,
                            ToolTipText = item.Path
                        };
                        node.Nodes.Add(child);
                    }
                    _tree.Nodes.Add(node);
                }
                if (result.Groups.Count > 0 && result.Groups.Count <= 20) _tree.ExpandAll();
            }
            finally
            {
                _suppressCheck = false;
                _tree.EndUpdate();
            }
            UpdateSelectionStatus();
        }

        private string BuildItemText(DuplicateEntry item, ScanResult result)
        {
            string rel = FormatUtil.Relative(item.Path, _root);
            string kind = item.IsDirectory ? "[目录]" : "[文件]";
            string size = item.IsDirectory
                ? (result != null && AnyFolderSize(result) ? FormatUtil.Size(item.Size).PadLeft(9) : "        -")
                : FormatUtil.Size(item.Size).PadLeft(9);
            string time = item.Modified == DateTime.MinValue ? "                " : item.Modified.ToString("yyyy-MM-dd HH:mm");
            return kind + "  " + size + "  " + time + "  " + rel;
        }

        private bool AnyFolderSize(ScanResult result)
        {
            foreach (var g in result.Groups)
                foreach (var i in g.Items)
                    if (i.IsDirectory && i.Size > 0) return true;
            return false;
        }

        #endregion

        #region 选择与勾选

        private void OnAfterCheck(object sender, TreeViewEventArgs e)
        {
            if (_suppressCheck) return;

            _suppressCheck = true;
            try
            {
                if (e.Node.Tag is DuplicateGroup)
                {
                    bool check = e.Node.Checked;
                    foreach (TreeNode child in e.Node.Nodes)
                    {
                        child.Checked = check;
                        var entry = child.Tag as DuplicateEntry;
                        if (entry != null) entry.Selected = check;
                    }
                }
                else
                {
                    var entry = e.Node.Tag as DuplicateEntry;
                    if (entry != null) entry.Selected = e.Node.Checked;

                    TreeNode parent = e.Node.Parent;
                    if (parent != null)
                    {
                        bool all = true, any = false;
                        foreach (TreeNode child in parent.Nodes)
                        {
                            if (child.Checked) any = true; else all = false;
                        }
                        parent.Checked = all || any ? all : false;
                    }
                }
            }
            finally
            {
                _suppressCheck = false;
            }
            UpdateSelectionStatus();
        }

        private void SetAllChecked(bool check)
        {
            _suppressCheck = true;
            try
            {
                foreach (TreeNode group in _tree.Nodes)
                {
                    group.Checked = check;
                    foreach (TreeNode child in group.Nodes)
                    {
                        child.Checked = check;
                        var entry = child.Tag as DuplicateEntry;
                        if (entry != null) entry.Selected = check;
                    }
                }
            }
            finally { _suppressCheck = false; }
            UpdateSelectionStatus();
        }

        private void InvertSelection()
        {
            _suppressCheck = true;
            try
            {
                foreach (TreeNode group in _tree.Nodes)
                {
                    bool allChecked = true;
                    foreach (TreeNode child in group.Nodes)
                    {
                        child.Checked = !child.Checked;
                        var entry = child.Tag as DuplicateEntry;
                        if (entry != null) entry.Selected = child.Checked;
                        if (!child.Checked) allChecked = false;
                    }
                    group.Checked = allChecked && group.Nodes.Count > 0;
                }
            }
            finally { _suppressCheck = false; }
            UpdateSelectionStatus();
        }

        private void SmartSelect()
        {
            if (_result == null) return;
            var mode = _cmbKeep.SelectedItem is TagItem<KeepMode>
                ? ((TagItem<KeepMode>)_cmbKeep.SelectedItem).Value
                : KeepMode.Newest;

            _suppressCheck = true;
            try
            {
                foreach (TreeNode groupNode in _tree.Nodes)
                {
                    var group = groupNode.Tag as DuplicateGroup;
                    if (group == null) continue;

                    TreeNode keeperNode = null;
                    if (mode != KeepMode.None) keeperNode = FindKeeper(groupNode, group, mode);

                    bool anyChecked = false;
                    for (int i = 0; i < groupNode.Nodes.Count; i++)
                    {
                        TreeNode child = groupNode.Nodes[i];
                        bool select = !ReferenceEquals(child, keeperNode);
                        child.Checked = select;
                        var entry = child.Tag as DuplicateEntry;
                        if (entry != null) entry.Selected = select;
                        if (select) anyChecked = true;
                    }
                    groupNode.Checked = false; // 组节点不整体勾选，避免误操作
                    if (!anyChecked) groupNode.Checked = false;
                }
            }
            finally { _suppressCheck = false; }
            UpdateSelectionStatus();
        }

        private TreeNode FindKeeper(TreeNode groupNode, DuplicateGroup group, KeepMode mode)
        {
            TreeNode best = null;
            DuplicateEntry bestEntry = null;
            for (int i = 0; i < groupNode.Nodes.Count; i++)
            {
                var entry = groupNode.Nodes[i].Tag as DuplicateEntry;
                if (entry == null) continue;
                if (bestEntry == null)
                {
                    best = groupNode.Nodes[i];
                    bestEntry = entry;
                    continue;
                }
                bool better;
                switch (mode)
                {
                    case KeepMode.Newest:
                        better = entry.Modified > bestEntry.Modified;
                        break;
                    case KeepMode.Oldest:
                        better = entry.Modified < bestEntry.Modified;
                        break;
                    case KeepMode.Largest:
                        better = entry.Size > bestEntry.Size;
                        break;
                    case KeepMode.ShortestPath:
                        better = entry.Path.Length < bestEntry.Path.Length;
                        break;
                    default:
                        better = false;
                        break;
                }
                if (better)
                {
                    best = groupNode.Nodes[i];
                    bestEntry = entry;
                }
            }
            return best;
        }

        private void UpdateSelectionStatus()
        {
            int selected = 0;
            long bytes = 0;
            int dirCount = 0;

            foreach (TreeNode group in _tree.Nodes)
            {
                foreach (TreeNode child in group.Nodes)
                {
                    if (!child.Checked) continue;
                    var entry = child.Tag as DuplicateEntry;
                    if (entry == null) continue;
                    selected++;
                    bytes += entry.Size;
                    if (entry.IsDirectory) dirCount++;
                }
            }

            bool has = selected > 0;
            _btnDelete.Enabled = has;

            if (_result == null)
            {
                _lblStatus.Text = has ? "已选 " + selected + " 项" : "请选择要扫描的目录";
                return;
            }

            string sizeText = bytes > 0 ? "，约 " + FormatUtil.Size(bytes) : "";
            string dirText = dirCount > 0 ? "（含 " + dirCount + " 个目录）" : "";
            _lblStatus.Text = "共 " + _result.Groups.Count + " 组 / " + _result.TotalItems + " 项 —— 已选 " +
                              selected + " 项" + dirText + sizeText;
        }

        #endregion

        #region 删除

        private async void DeleteSelected()
        {
            var selected = new List<DuplicateEntry>();
            foreach (TreeNode group in _tree.Nodes)
            {
                foreach (TreeNode child in group.Nodes)
                {
                    if (!child.Checked) continue;
                    var entry = child.Tag as DuplicateEntry;
                    if (entry != null) selected.Add(entry);
                }
            }

            if (selected.Count == 0)
            {
                MessageBox.Show(this, "请先勾选要删除的项。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var toDelete = new List<string>();
            var blocked = new List<string>();
            var missing = new List<string>();

            foreach (var entry in selected)
            {
                string reason;
                if (!Safety.IsSafeToDelete(entry.Path, _root, out reason))
                {
                    blocked.Add(entry.Path + "（" + reason + "）");
                    continue;
                }
                if (!File.Exists(entry.Path) && !Directory.Exists(entry.Path))
                {
                    missing.Add(entry.Path);
                    continue;
                }
                toDelete.Add(entry.Path);
            }

            if (toDelete.Count == 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("没有可删除的项。");
                if (blocked.Count > 0) sb.AppendLine("被安全策略拦截 " + blocked.Count + " 项。");
                if (missing.Count > 0) sb.AppendLine("已不存在 " + missing.Count + " 项。");
                MessageBox.Show(this, sb.ToString(), "无法删除", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshTree();
                return;
            }

            int dirTarget = 0;
            long bytes = 0;
            foreach (var entry in selected)
            {
                if (!toDelete.Contains(entry.Path)) continue;
                if (entry.IsDirectory) dirTarget++;
                bytes += entry.Size;
            }

            var preview = new StringBuilder();
            preview.AppendLine("即将把以下 " + toDelete.Count + " 项移入回收站（可恢复）：");
            if (dirTarget > 0)
                preview.AppendLine("⚠ 其中包含 " + dirTarget + " 个文件夹，文件夹将连同内部所有内容一起移入回收站！");
            if (bytes > 0) preview.AppendLine("合计约 " + FormatUtil.Size(bytes) + "。");
            preview.AppendLine();
            int shown = Math.Min(12, toDelete.Count);
            for (int i = 0; i < shown; i++) preview.AppendLine("  · " + toDelete[i]);
            if (toDelete.Count > shown) preview.AppendLine("  ... 其余 " + (toDelete.Count - shown) + " 项已省略");
            if (blocked.Count > 0) preview.AppendLine().AppendLine("另有 " + blocked.Count + " 项被安全策略拦截，不会删除。");
            preview.AppendLine().AppendLine("确定继续吗？");

            var confirm = MessageBox.Show(this, preview.ToString(), "确认删除到回收站",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            _btnDelete.Enabled = false;
            _btnScan.Enabled = false;
            _progress.Visible = true;
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 0;
            _progress.Maximum = Math.Max(1, toDelete.Count);
            _lblStatus.Text = "正在移入回收站...";

            var progress = new Progress<int>(done =>
            {
                _progress.Value = Math.Min(done, _progress.Maximum);
                _lblStatus.Text = "正在移入回收站... " + done + " / " + toDelete.Count;
            });

            DeleteResult result = null;
            try
            {
                result = await Task.Run(() =>
                    RecycleBin.DeleteAll(toDelete, delegate (int done, int total)
                    {
                        if (progress != null) ((IProgress<int>)progress).Report(done);
                    }));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "删除过程中出错：\r\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _progress.Visible = false;
                _btnScan.Enabled = true;
            }

            if (result != null)
            {
                var summary = new StringBuilder();
                summary.AppendLine("已移入回收站：" + result.Succeeded.Count + " 项");
                if (result.Failed.Count > 0)
                {
                    summary.AppendLine("失败：" + result.Failed.Count + " 项");
                    int n = Math.Min(10, result.Failed.Count);
                    for (int i = 0; i < n; i++) summary.AppendLine("  × " + result.Failed[i]);
                    if (result.Failed.Count > n) summary.AppendLine("  ... 其余 " + (result.Failed.Count - n) + " 项已省略");
                }
                if (blocked.Count > 0)
                {
                    summary.AppendLine("被安全策略拦截：" + blocked.Count + " 项");
                    int n = Math.Min(5, blocked.Count);
                    for (int i = 0; i < n; i++) summary.AppendLine("  ! " + blocked[i]);
                }
                if (missing.Count > 0) summary.AppendLine("删除前已不存在：" + missing.Count + " 项");

                MessageBox.Show(this, summary.ToString(), "删除完成",
                    MessageBoxButtons.OK,
                    result.Failed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

                RemoveDeleted(result.Succeeded);
            }

            RefreshTree();
            UpdateSelectionStatus();
        }

        private void RemoveDeleted(List<string> deleted)
        {
            var set = new HashSet<string>(deleted, StringComparer.OrdinalIgnoreCase);

            foreach (TreeNode groupNode in _tree.Nodes)
            {
                for (int i = groupNode.Nodes.Count - 1; i >= 0; i--)
                {
                    var entry = groupNode.Nodes[i].Tag as DuplicateEntry;
                    if (entry != null && set.Contains(entry.Path)) groupNode.Nodes.RemoveAt(i);
                }
                var g = groupNode.Tag as DuplicateGroup;
                if (g != null)
                {
                    g.Items.RemoveAll(x => set.Contains(x.Path));
                    groupNode.Text = g.Key + "    (" + g.Items.Count + " 项)";
                }
            }

            for (int i = _tree.Nodes.Count - 1; i >= 0; i--)
            {
                if (_tree.Nodes[i].Nodes.Count < 2) _tree.Nodes.RemoveAt(i);
            }

            if (_result != null)
            {
                _result.Groups.RemoveAll(g => g.Items.Count < 2);
            }
        }

        private void RefreshTree()
        {
            // 移除已不存在于磁盘上的项
            foreach (TreeNode groupNode in _tree.Nodes)
            {
                for (int i = groupNode.Nodes.Count - 1; i >= 0; i--)
                {
                    var entry = groupNode.Nodes[i].Tag as DuplicateEntry;
                    if (entry == null) continue;
                    bool exists = entry.IsDirectory ? Directory.Exists(entry.Path) : File.Exists(entry.Path);
                    if (!exists)
                    {
                        groupNode.Nodes.RemoveAt(i);
                        var g = groupNode.Tag as DuplicateGroup;
                        if (g != null) g.Items.Remove(entry);
                    }
                }
            }
            for (int i = _tree.Nodes.Count - 1; i >= 0; i--)
            {
                if (_tree.Nodes[i].Nodes.Count < 2) _tree.Nodes.RemoveAt(i);
            }
        }

        #endregion

        #region 辅助功能

        private void OnNodeDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            var entry = e.Node.Tag as DuplicateEntry;
            if (entry != null) RevealInExplorer(entry.Path);
        }

        private void OnTreeMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            TreeNode node = _tree.GetNodeAt(e.X, e.Y);
            if (node == null) return;
            _tree.SelectedNode = node;

            var menu = new ContextMenuStrip();
            var entry = node.Tag as DuplicateEntry;

            if (entry != null)
            {
                menu.Items.Add("在资源管理器中显示", null, (s, a) => RevealInExplorer(entry.Path));
                menu.Items.Add("复制完整路径", null, (s, a) =>
                {
                    try { Clipboard.SetText(entry.Path); } catch (Exception) { }
                });
                menu.Items.Add("取消勾选本项", null, (s, a) => { node.Checked = false; });
                menu.Items.Add("从列表移除（不删除）", null, (s, a) =>
                {
                    var g = node.Parent != null ? node.Parent.Tag as DuplicateGroup : null;
                    if (g != null) g.Items.Remove(entry);
                    node.Remove();
                    if (node.Parent != null && node.Parent.Nodes.Count < 2) node.Parent.Remove();
                    UpdateSelectionStatus();
                });
            }
            else
            {
                menu.Items.Add("勾选本组全部", null, (s, a) => { node.Checked = true; });
                menu.Items.Add("取消勾选本组", null, (s, a) => { node.Checked = false; });
            }

            menu.Show(_tree, e.Location);
        }

        private static void RevealInExplorer(string path)
        {
            try
            {
                bool isDir = Directory.Exists(path);
                if (!isDir && !File.Exists(path))
                {
                    MessageBox.Show("该路径已不存在：\r\n" + path, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = isDir ? "\"" + path + "\"" : "/select,\"" + path + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开资源管理器：\r\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportList()
        {
            if (_result == null || _result.Groups.Count == 0)
            {
                MessageBox.Show(this, "请先扫描。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "CSV 文件 (*.csv)|*.csv|文本文件 (*.txt)|*.txt";
                dlg.FileName = "重名清单_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var sb = new StringBuilder();
                sb.AppendLine("名称,类型,完整路径,大小(字节),修改时间,是否勾选");
                foreach (var g in _result.Groups)
                {
                    foreach (var item in g.Items)
                    {
                        sb.Append(Csv(g.Key)).Append(',')
                          .Append(item.IsDirectory ? "目录" : "文件").Append(',')
                          .Append(Csv(item.Path)).Append(',')
                          .Append(item.Size).Append(',')
                          .Append(item.Modified == DateTime.MinValue ? "" : item.Modified.ToString("yyyy-MM-dd HH:mm:ss")).Append(',')
                          .Append(item.Selected ? "是" : "否")
                          .AppendLine();
                    }
                }

                try
                {
                    File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                    MessageBox.Show(this, "已导出到：\r\n" + dlg.FileName, "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败：\r\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static string Csv(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        #endregion
    }

    internal static class ListExtensions
    {
        public static string[] ToArrayOrEmpty(this List<string> list)
        {
            if (list == null || list.Count == 0) return new string[0];
            var arr = list.ToArray();
            int max = Math.Min(arr.Length, 20);
            if (arr.Length <= 20) return arr;
            var trimmed = new string[max + 1];
            Array.Copy(arr, trimmed, max);
            trimmed[max] = "... 其余 " + (arr.Length - max) + " 条已省略";
            return trimmed;
        }
    }
}
