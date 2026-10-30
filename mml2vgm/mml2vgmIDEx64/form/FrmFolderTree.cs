using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using System.ComponentModel;
using NAudio.Wave;

namespace mml2vgmIDEx64
{
    public partial class FrmFolderTree : WeifenLuo.WinFormsUI.Docking.DockContent
    {
        private static readonly Color FileListHeaderBackColor = Color.FromArgb(80, 80, 160);
        private static readonly Color FileListHeaderForeColor = Color.FromArgb(230, 255, 255);
        private static readonly Color FolderTreeSplitterColor = Color.FromArgb(60, 60, 72);
        private static readonly Color FileListActiveSelectionBackColor = Color.FromArgb(89, 89, 121);
        private static readonly Color FileListActiveSelectionForeColor = Color.FromArgb(241, 241, 241);
        private static readonly Color FileListHotBackColor = Color.FromArgb(62, 62, 80);
        private static readonly Color FileListHotForeColor = Color.FromArgb(225, 225, 225);
        private static readonly Color FileListInactiveSelectionBackColor = Color.FromArgb(72, 72, 76);
        private static readonly Color FileListInactiveSelectionForeColor = Color.FromArgb(210, 210, 210);
        private static readonly Color FileListHeaderSortGlyphColor = Color.White;
        private static readonly HashSet<string> AudioFileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".wav", ".wave", ".mp3", ".mp2", ".ogg", ".flac", ".aif", ".aiff", ".wma", ".m4a", ".aac", ".ape", ".opus"
        };
        public Action parentUpdate = null;
        public Action<string[]> parentExecFile = null;
        public Action<string[]> parentDeleteFile = null;
        public Func<Setting> parentGetSetting = null;
        public string basePath = "";
        private bool useMouse;
        private int fileListSortColumn = -1;
        private bool fileListSortAscending = true;
        private FileSizeDisplayUnit fileSizeDisplayUnit = FileSizeDisplayUnit.Byte;
        private readonly Setting setting;
        private readonly ContextMenuStrip cmsFileListHeaderMenu = new ContextMenuStrip();
        private readonly Dictionary<ColumnHeader, int> fileListColumnDisplayWidths = new Dictionary<ColumnHeader, int>();
        private const string FileListHeaderMenuShowAllText = "Show all";
        private IntPtr lvFileListHeaderHandle = IntPtr.Zero;
        private FileListHeaderNativeWindow lvFileListHeaderWindow = null;
        private const int LVM_FIRST = 0x1000;
        private const int LVM_GETHEADER = LVM_FIRST + 31;
        private const int WM_CONTEXTMENU = 0x007B;

        public FrmFolderTree(Setting setting, DockPanel dockPanel)
        {
            InitializeComponent();
            AddFileIconVariants();
            this.setting = setting;
            this.tvFolderTree.BackColor = Color.FromArgb(setting.ColorScheme.FolderTree_BackColor);
            this.tvFolderTree.ForeColor = Color.FromArgb(setting.ColorScheme.FolderTree_ForeColor);
            this.tvFolderTree.HotColor = Color.FromArgb(setting.ColorScheme.FolderTree_BackColor);
            this.tvFolderTree.HotColor = Color.FromArgb(
                (int)(tvFolderTree.HotColor.R * 1.3),
                (int)(tvFolderTree.HotColor.G * 1.3),
                (int)(tvFolderTree.HotColor.B * 1.3));
            lvFileList.BackColor = Color.FromArgb(setting.ColorScheme.FolderTree_BackColor);
            lvFileList.ForeColor = Color.FromArgb(setting.ColorScheme.FolderTree_ForeColor);
            splitContainer1.BackColor = FolderTreeSplitterColor;

            tvFolderTree.TreeViewNodeSorter = new NodeSorter();

            dockPanel.Theme.ApplyTo(cmsMenu);
            dockPanel.Theme.ApplyTo(cmsFileListHeaderMenu);
            dockPanel.Theme.ApplyTo(toolStrip1);
            cmsMenu.Opening += CmsMenu_Opening;
            lvFileList.ColumnWidthChanged += LvFileList_ColumnWidthChanged;
            lvFileList.HandleDestroyed += LvFileList_HandleDestroyed;
            InitializeFileListColumnDisplayWidths();

            Common.SetDoubleBuffered(tvFolderTree);
            Common.ApplyDarkScrollBarTheme(this);
            ApplyFolderTextFont();
            UpdateFileSizeDisplay();
        }

        private enum FileSizeDisplayUnit
        {
            Byte,
            KB,
            MB
        }

        public void ApplyFolderTextFont()
        {
            Setting currentSetting = parentGetSetting?.Invoke() ?? setting;
            Font folderFont = new Font(
                currentSetting.other.FolderFontName,
                currentSetting.other.FolderFontSize,
                currentSetting.other.FolderFontStyle);
            tvFolderTree.Font = folderFont;
            lvFileList.Font = folderFont;
        }

        private void FrmFolderTree_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            parentUpdate?.Invoke();
        }

        private void TvFolderTree_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (e.Node.ImageIndex == 1 && e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Text == "!dmy")
            {
                TreeNode node = e.Node;
                TreeNode ts;

                // 展開するノードのフルパスを取得
                string path1 = Path.GetDirectoryName(basePath);
                path1 = string.IsNullOrEmpty(path1) ? basePath : path1;
                string fullpath = Path.Combine(path1, Path.GetFullPath(node.FullPath));
                node.Nodes.Clear();

                DirectoryInfo dm = new DirectoryInfo(fullpath);

                try
                {
                    foreach (DirectoryInfo ds in dm.GetDirectories())
                    {
                        ts = new TreeNode(ds.Name, 1, 1);
                        ts.Nodes.Add("!dmy");
                        node.Nodes.Add(ts);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    LogFolderOperationException("TvFolderTree_BeforeExpand", null, fullpath);
                    MessageBox.Show("アクセスが拒否されました。", "権限不足", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception ex)
                {
                    LogFolderOperationException("TvFolderTree_BeforeExpand", ex, fullpath);
                }
            }
        }

        private void TvFolderTree_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            UpdateFileList(e.Node);
        }

        private void TvFolderTree_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                UpdateFileList(tvFolderTree.SelectedNode);
                e.Handled = true;
            }
        }

        private void TvFolderTree_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            useMouse = true;

            if (e.Button == MouseButtons.Right)
            {
                if (!e.Node.Checked)
                {
                    if ((Control.ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
                    {
                        CheckClear(tvFolderTree.Nodes);
                    }
                    tvFolderTree.SelectedNode = e.Node;
                }
                e.Node.Checked = true;
                tvFolderTree.SelectedNode = e.Node;
                TreeNode tn = e.Node;
                if (tn == null) return;
                if (tn.ImageIndex == 1) return;

                List<string> lstFullPathExt = new List<string>();
                GetCheckTreeNodesFullPathExt(lstFullPathExt, tvFolderTree.SelectedNode.Parent.Nodes);

                CheckExtension(cmsMenu.Items, lstFullPathExt.ToArray());

                cmsMenu.Show((Control)sender, e.X, e.Y);

            }
            else if (e.Button == MouseButtons.Left)
            {
                tvFolderTree.SelectedNode = e.Node;

                if ((Control.ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
                {
                    CheckClear(tvFolderTree.Nodes);
                }

                e.Node.Checked = true;
            }
        }

        private void TvFolderTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            UpdateFileList(e.Node);
            if (useMouse)
            {
                useMouse = false;
                return;
            }
            tvFolderTree.SelectedNode = e.Node;

            if ((Control.ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
            {
                CheckClear(tvFolderTree.Nodes);
            }

            e.Node.Checked = true;

        }

        private void TvFolderTree_ItemDrag(object sender, ItemDragEventArgs e)
        {
            TreeView tv = (TreeView)sender;
            tv.SelectedNode = ((TreeNode)e.Item);
            ((TreeNode)e.Item).Checked = true;
            tv.Focus();

            List<string> lstFile = new List<string>();
            GetCheckTreeNodesFullPath(ref lstFile, tv.Nodes);

            DataObject dataObj = new DataObject(DataFormats.FileDrop, lstFile.ToArray());
            DragDropEffects dde = tv.DoDragDrop(dataObj, DragDropEffects.All);

            if ((dde & DragDropEffects.Move) == DragDropEffects.Move)
                refresh();
        }

        private void TvFolderTree_DragOver(object sender, DragEventArgs e)
        {
            //ドラッグされているデータがfileか調べる
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                //Ctrl:8
                //Shift:4

                if ((e.KeyState & 4) != 0 && (e.AllowedEffect & DragDropEffects.Move) != 0)
                    //Shiftキーが押されていればMove
                    e.Effect = DragDropEffects.Move;
                else if ((e.AllowedEffect & DragDropEffects.Copy) != 0)
                    //何も押されていなければCopy
                    e.Effect = DragDropEffects.Copy;
                else
                    e.Effect = DragDropEffects.None;
            }
            else
                e.Effect = DragDropEffects.None;

            if (e.Effect == DragDropEffects.None) return;

            //マウス下のNodeを選択する
            TreeView tv = (TreeView)sender;
            //マウスのあるNodeを取得する
            TreeNode target = tv.GetNodeAt(tv.PointToClient(new Point(e.X, e.Y)));

            string[] source = (string[])e.Data.GetData(DataFormats.FileDrop);

            if (target == null) return;

            if (target.ImageIndex != 1)//fileはフォルダーの上にのみDropできるようにする
            {
                e.Effect = DragDropEffects.None;
                return;
            }

        }

        private void TvFolderTree_DragDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            TreeView tv = (TreeView)sender;
            string[] source = (string[])e.Data.GetData(DataFormats.FileDrop);

            //ドロップ先のTreeNodeを取得する
            TreeNode target = tv.GetNodeAt(tv.PointToClient(new Point(e.X, e.Y)));

            if (target == null) return;

            if (target.ImageIndex != 1)//fileはフォルダーの上にのみDropできるようにする
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            if ((e.Effect & DragDropEffects.Copy) != 0)
            {
                //ファイルコピー
                CopyFile_DragDrop(source, target);
                return;
            }
            else if ((e.Effect & DragDropEffects.Move) != 0)
            {
                //ファイルムーブ
                CopyFile_DragDrop(source, target, true);
                return;
            }

            e.Effect = DragDropEffects.None;

        }

        private void TsmiOpen_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) return;

            List<string> lstFullPath = GetSelectedFilePaths();
            if (lstFullPath.Count < 1) return;
            parentExecFile?.Invoke(lstFullPath.ToArray());

        }

        private void TsmiDelete_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) return;

            List<string> lstFullPath = GetSelectedFilePaths();
            if (lstFullPath.Count < 1) return;

            DialogResult ret = DialogResult.Cancel;

            if (lstFullPath.Count == 1)
            {
                ret = MessageBox.Show(
                    string.Format("「{0}」を削除します。よろしいですか。", lstFullPath[0])
                    , "ファイル/フォルダーの削除", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            }
            else
            {
                string fns = "";
                for (int i = 0; i < Math.Min(lstFullPath.Count, 3); i++)
                {
                    fns += "    " + lstFullPath[i] + "\n";
                }
                if (lstFullPath.Count > 3)
                {
                    fns += string.Format("\n    他、{0} ファイル/フォルダー\n", lstFullPath.Count - 3);
                }

                ret = MessageBox.Show(
                    string.Format("以下のファイル/フォルダーを削除します。よろしいですか。\n\n{0}", fns)
                    , "ファイル/フォルダーの削除", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            }

            if (ret == DialogResult.Cancel) return;

            parentDeleteFile?.Invoke(lstFullPath.ToArray());

        }

        private void TsmiOpenSoundEngine_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) return;

            Setting currentSetting = parentGetSetting?.Invoke() ?? setting;
            string soundEnginePath = currentSetting?.other?.SoundEnginePath ?? "";
            if (string.IsNullOrWhiteSpace(soundEnginePath) || !File.Exists(soundEnginePath))
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "SoundEngine実行ファイル(*.exe)|*.exe|すべてのファイル(*.*)|*.*";
                ofd.Title = "SoundEngineの実行ファイルを選択してください";
                ofd.CheckPathExists = true;
                ofd.CheckFileExists = true;
                ofd.Multiselect = false;

                if (!string.IsNullOrWhiteSpace(soundEnginePath))
                {
                    string dir = Path.GetDirectoryName(soundEnginePath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        ofd.InitialDirectory = dir;
                    }
                }
                else if (currentSetting?.other != null
                    && !string.IsNullOrWhiteSpace(currentSetting.other.DefaultDataPath)
                    && Directory.Exists(currentSetting.other.DefaultDataPath))
                {
                    ofd.InitialDirectory = currentSetting.other.DefaultDataPath;
                }
                else
                {
                    ofd.RestoreDirectory = true;
                }

                if (ofd.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                soundEnginePath = ofd.FileName;
                if (!File.Exists(soundEnginePath))
                {
                    MessageBox.Show("指定されたSoundEngineの実行ファイルが見つかりません。"
                        , "SoundEngine path error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                currentSetting.other.SoundEnginePath = soundEnginePath;
                currentSetting.Save();
            }

            List<string> lstFullPath = GetSelectedFilePaths();
            if (lstFullPath.Count < 1) return;

            foreach (string fullPath in lstFullPath)
            {
                if (!File.Exists(fullPath)) continue;

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = soundEnginePath,
                    Arguments = $"\"{fullPath}\"",
                    UseShellExecute = true
                }).Dispose();
            }
        }



        private void CopyFile_DragDrop(string[] source, TreeNode target, bool doDelete = false)
        {
            List<string> lstFail = new List<string>();

            foreach (string fileOrFolder in source)
            {
                try
                {
                    if ((File.GetAttributes(fileOrFolder) & FileAttributes.Directory) != 0)
                    {
                        // folder
                        string tmp =
                            (fileOrFolder[fileOrFolder.Length - 1] == Path.DirectorySeparatorChar)
                            ? fileOrFolder.Substring(0, fileOrFolder.Length - 1)
                            : fileOrFolder;
                        string folder = Path.GetFileName(tmp);
                        string path1 = Path.GetDirectoryName(basePath);
                        path1 = string.IsNullOrEmpty(path1) ? basePath : path1;
                        string targetPath = Path.Combine(path1, Path.GetFullPath(target.FullPath), folder);

                        CopyDirectory(fileOrFolder, targetPath);

                        if (doDelete) Directory.Delete(fileOrFolder, true);
                    }
                    else
                    {
                        // file
                        string file = Path.GetFileName(fileOrFolder);
                        string path1 = Path.GetDirectoryName(basePath);
                        path1 = string.IsNullOrEmpty(path1) ? basePath : path1;
                        string targetPath = Path.Combine(path1, Path.GetFullPath(target.FullPath), file);

                        if (File.Exists(targetPath))
                        {
                            DialogResult res = MessageBox.Show(
                                string.Format("次のファイルはコピー先に既に存在します。上書きしますか？\n\n{0}"
                                , file)
                                , "上書き確認"
                                , MessageBoxButtons.YesNoCancel
                                , MessageBoxIcon.Question);
                            if (res == DialogResult.Cancel) return;
                            if (res == DialogResult.Yes) File.Copy(fileOrFolder, targetPath, true);
                        }
                        else
                        {
                            File.Copy(fileOrFolder, targetPath);
                        }

                        if (doDelete) File.Delete(fileOrFolder);
                    }
                }
                catch (FileNotFoundException fe)
                {
                    LogFolderOperationException("CopyFile_DragDrop", fe, fileOrFolder);
                    lstFail.Add(string.Format("'{0}'", fileOrFolder));
                }
                catch (DirectoryNotFoundException de)
                {
                    LogFolderOperationException("CopyFile_DragDrop", de, fileOrFolder);
                    lstFail.Add(string.Format("'{0}'", fileOrFolder));
                }
                catch (Exception ex)
                {
                    LogFolderOperationException("CopyFile_DragDrop", ex, fileOrFolder);
                    lstFail.Add(string.Format("'{0}'", fileOrFolder));
                }
            }

            refresh();
        }

        /// <summary>
        /// From dobon.net : https://dobon.net/vb/dotnet/file/copyfolder.html
        /// </summary>
        public static void CopyDirectory(string sourceDirName, string destDirName)
        {
            //コピー先のディレクトリがないときは作る
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
                //属性もコピー
                File.SetAttributes(destDirName, File.GetAttributes(sourceDirName));
            }

            //コピー先のディレクトリ名の末尾に"\"をつける
            if (destDirName[destDirName.Length - 1] != Path.DirectorySeparatorChar)
                destDirName = destDirName + Path.DirectorySeparatorChar;

            //コピー元のディレクトリにあるファイルをコピー
            string[] files = Directory.GetFiles(sourceDirName);
            foreach (string file in files)
            {
                string targetPath = destDirName + Path.GetFileName(file);

                if (File.Exists(targetPath))
                {
                    DialogResult res = MessageBox.Show(
                        string.Format("次のファイルはコピー先に既に存在します。上書きしますか？\n\n{0}"
                        , Path.GetFileName(file))
                        , "上書き確認"
                        , MessageBoxButtons.YesNoCancel
                        , MessageBoxIcon.Question);
                    if (res == DialogResult.Cancel)
                    {
                        return;
                    }
                    if (res == DialogResult.Yes)
                    {
                        File.Copy(file, targetPath, true);
                    }
                }
                else
                {
                    File.Copy(file, targetPath);
                }
            }

            //コピー元のディレクトリにあるディレクトリについて、再帰的に呼び出す
            string[] dirs = Directory.GetDirectories(sourceDirName);
            foreach (string dir in dirs)
                CopyDirectory(dir, destDirName + Path.GetFileName(dir));
        }

        public void refresh()
        {
            if (string.IsNullOrEmpty(basePath)) return;
            refreshRemoveCheck(tvFolderTree.Nodes);
            if (tvFolderTree.Nodes.Count > 0)
            {
                refreshAddCheck(tvFolderTree.Nodes[0]);
            }
            tvFolderTree.Sort();
            tvFolderTree.ExpandAll();
            UpdateFileList(tvFolderTree.SelectedNode ?? (tvFolderTree.Nodes.Count > 0 ? tvFolderTree.Nodes[0] : null));
        }

        private void refreshRemoveCheck(TreeNodeCollection nodes)
        {
            int i = 0;
            while (i < nodes.Count)
            {
                try
                {
                    TreeNode tn = nodes[i++];
                    if (tn.Nodes.Count > 0 && tn.Nodes[0].Text != "!dmy")
                    {
                        refreshRemoveCheck(tn.Nodes);
                    }
                    string path1 = Path.GetDirectoryName(basePath);
                    path1 = string.IsNullOrEmpty(path1) ? basePath : path1;
                    string fullpath = Path.Combine(path1, Path.GetFullPath(tn.FullPath));
                    if (tn.ImageIndex == 1)
                    {
                        //folder
                        if (!Directory.Exists(fullpath))
                        {
                            nodes.Remove(tn);
                            i--;
                        }
                    }
                    else
                    {
                        //file
                        if (!File.Exists(fullpath))
                        {
                            nodes.Remove(tn);
                            i--;
                        }
                    }
                }
                catch
                {
                    ;
                }
            }
        }

        private void refreshAddCheck(TreeNode tn)
        {
            string path1 = Path.GetDirectoryName(basePath);
            path1 = string.IsNullOrEmpty(path1) ? basePath : path1;

            string fullpath = Path.Combine(path1, Path.GetFullPath(tn.FullPath));
            DirectoryInfo dm = new DirectoryInfo(fullpath);
            TreeNode ts;

            try
            {
                foreach (DirectoryInfo ds in dm.GetDirectories())
                {
                    int i = 0;
                    bool flg = false;
                    while (i < tn.Nodes.Count)
                    {
                        TreeNode ttn = tn.Nodes[i++];
                        if (ttn.Nodes.Count > 0 && ttn.Nodes[0].Text != "!dmy")
                        {
                            refreshAddCheck(ttn);
                        }
                        else
                        {
                            if (ttn.ImageIndex == 1 && ttn.Nodes.Count == 0)
                                refreshAddCheck(ttn);

                        }

                        string fpath = Path.Combine(path1, Path.GetFullPath(ttn.FullPath));
                        if (ds.FullName == fpath)
                        {
                            flg = true;
                        }
                    }
                    if (!flg)
                    {
                        ts = new TreeNode(ds.Name, 1, 1);
                        ts.Nodes.Add("!dmy");
                        tn.Nodes.Add(ts);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                LogFolderOperationException("refreshAddCheck", null, fullpath);
                MessageBox.Show("アクセスが拒否されました。", "権限不足", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                LogFolderOperationException("refreshAddCheck", ex, fullpath);
            }

        }

        private void refreshFilter(TreeNodeCollection nodes)
        {
            if (tsmiFilterAll.Checked) return;

            int i = 0;
            while (i < nodes.Count)
            {
                TreeNode tn = nodes[i++];

                if (tn.ImageIndex == 1)
                {
                    if (!tsmiFilterFolder.Checked)
                    {
                        nodes.Remove(tn);
                        i--;
                    }
                    else
                    {
                        if (tn.Nodes.Count > 0)
                        {
                            refreshFilter(tn.Nodes);
                        }
                    }
                }
                else
                {
                    if (tsmiFilterFile.Checked)
                    {
                        continue;
                    }
                    else
                    {
                        if (Path.GetExtension(tn.Text).ToLower() == ".gwi")
                        {
                            if (tsmiFilterGwi.Checked)
                            {
                                continue;
                            }
                        }
                        else if (Path.GetExtension(tn.Text).ToLower() == ".muc")
                        {
                            if (tsmiFilterMuc.Checked)
                            {
                                continue;
                            }
                        }
                        else if (Path.GetExtension(tn.Text).ToLower() == ".mml")
                        {
                            if (tsmiFilterMml.Checked)
                            {
                                continue;
                            }
                        }
                        else if (Path.GetExtension(tn.Text).ToLower() == ".wav")
                        {
                            if (tsmiFilterWav.Checked)
                            {
                                continue;
                            }
                        }
                        else if (tn.Text == "!dmy")
                        {
                            continue;
                        }
                        nodes.Remove(tn);
                        i--;
                    }
                }
            }

        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ToolStripMenuItem extendItem
        {
            set
            {
                SetContextMenuItem(value);
            }
        }

        private void SetContextMenuItem(ToolStripMenuItem value)
        {
            if (value == null || value.DropDownItems.Count == 0) return;
            while (0 < value.DropDownItems.Count)
            {
                cmsMenu.Items.Add(value.DropDownItems[0]);
            }
        }

        protected override string GetPersistString()
        {
            return this.Name;
        }

        private void CheckClear(TreeNodeCollection nodes)
        {
            foreach (TreeNode tn in nodes)
            {
                tn.Checked = false;
                if (tn.Nodes.Count > 0) CheckClear(tn.Nodes);
            }
        }

        public void GetCheckTreeNodesFullPath(ref List<string> lstFullPath, TreeNodeCollection nodes, bool force = false)
        {
            foreach (TreeNode tn in nodes)
            {
                if (tn.Nodes.Count > 0)
                {
                    GetCheckTreeNodesFullPath(ref lstFullPath, tn.Nodes);
                }
                if (!tn.Checked && !tn.IsSelected) continue;

                string fullpath = Corex64.Common.PathCombine(Corex64.Common.GetDirectoryName(basePath), Path.GetFullPath(tn.FullPath));
                lstFullPath.Add(fullpath);
            }
        }

        private void GetCheckTreeNodesFullPathExt(List<string> lstFullPathExt, TreeNodeCollection nodes)
        {
            foreach (TreeNode tn in nodes)
            {
                if (tn.Nodes.Count > 0)
                {
                    GetCheckTreeNodesFullPathExt(lstFullPathExt, tn.Nodes);
                    continue;
                }
                if (!tn.Checked) continue;
                lstFullPathExt.Add(Path.GetExtension(Path.GetFullPath(tn.FullPath)));
            }
        }

        private void GetCheckTreeNodes(List<TreeNode> lstNode, TreeNodeCollection nodes)
        {
            foreach (TreeNode tn in nodes)
            {
                if (tn.Nodes.Count > 0)
                {
                    GetCheckTreeNodes(lstNode, tn.Nodes);
                    continue;
                }
                if (!tn.Checked && !tn.IsSelected) continue;

                lstNode.Add(tn);
            }
        }

        private void CheckExtension(ToolStripItemCollection pItems, string[] exts)
        {
            foreach (ToolStripItem item in pItems)
            {
                if (item.Tag == null) continue;
                if (item is ToolStripSeparator) continue;

                ToolStripMenuItem tsmi = (ToolStripMenuItem)item;
                if (tsmi.DropDownItems.Count > 0)
                {
                    CheckExtension(tsmi.DropDownItems, exts);
                }

                Tuple<int, string, string[], string, string> tp = (Tuple<int, string, string[], string, string>)item.Tag;
                if (tp == null) continue;
                if (tp.Item1 < 0) continue;

                bool match = false;
                foreach (string ext in exts)
                {
                    match = false;
                    foreach (string s in tp.Item3)
                    {
                        if (s == ".*")
                        {
                            match = true;
                            break;
                        }

                        string regexPattern = TransW2Reg(s);
                        if (new Regex(regexPattern).IsMatch(ext))
                        {
                            match = true;
                            break;
                        }
                    }

                    if (!match) break;
                }

                item.Enabled = match;
            }
        }

        private string TransW2Reg(string wildcardPtn)
        {
            //参考：
            //  ワイルドカードを使用した文字列マッチ
            //    大谷 和広さま(@kazuhirox)
            //https://qiita.com/kazuhirox/items/5e314d5e7732041a3fe7

            var regexPattern = System.Text.RegularExpressions.Regex.Replace(
              wildcardPtn,
              ".",
              m =>
              {
                  string s = m.Value;
                  if (s.Equals("?"))
                  {
                      //?は任意の1文字を示す正規表現(.)に変換
                      return ".";
                  }
                  else if (s.Equals("*"))
                  {
                      //*は0文字以上の任意の文字列を示す正規表現(.*)に変換
                      return ".*";
                  }
                  else
                  {
                      //上記以外はエスケープする
                      return System.Text.RegularExpressions.Regex.Escape(s);
                  }
              }
              );

            return regexPattern;
        }

        public class NodeSorter : IComparer
        {
            // Compare the length of the strings, or the strings
            // themselves, if they are the same length.
            public int Compare(object x, object y)
            {
                TreeNode tx = x as TreeNode;
                TreeNode ty = y as TreeNode;

                if (tx.ImageIndex != ty.ImageIndex)
                    return tx.ImageIndex == 1 ? -1 : 1;

                // If they are the same length, call Compare.
                return string.Compare(tx.Text, ty.Text);
            }
        }

        private void TsbReload_Click(object sender, EventArgs e)
        {
            refresh();
        }

        private const string FILTER_ALL = "*.* (All)";
        private const string FILTER_FILE = "* (File)";
        private const string FILTER_FOLDER = "* (Folder)";

        private void TsmiFilterAll_Click(object sender, EventArgs e)
        {
            //All以外は全てチェックオフ
            foreach (ToolStripItem tsi in tsddbFilter.DropDownItems)
            {
                if (!(tsi is ToolStripMenuItem)) continue;
                ToolStripMenuItem item = (ToolStripMenuItem)tsi;
                if (item.Text == FILTER_ALL) continue;
                item.CheckState = CheckState.Unchecked;
            }

            refresh();
        }

        private void TsmiFilterFolder_Click(object sender, EventArgs e)
        {
            foreach (ToolStripItem tsi in tsddbFilter.DropDownItems)
            {
                if (!(tsi is ToolStripMenuItem)) continue;
                ToolStripMenuItem item = (ToolStripMenuItem)tsi;
                if (item.Text == FILTER_ALL)
                    item.CheckState = CheckState.Unchecked;
            }

            refresh();
        }

        private void TsmiFilterFile_Click(object sender, EventArgs e)
        {
            foreach (ToolStripItem tsi in tsddbFilter.DropDownItems)
            {
                if (!(tsi is ToolStripMenuItem)) continue;
                ToolStripMenuItem item = (ToolStripMenuItem)tsi;
                if (item.Text == FILTER_ALL)
                    item.CheckState = CheckState.Unchecked;
                else if (item.Text == FILTER_FILE) continue;
                else if (item.Text == FILTER_FOLDER) continue;
                else item.CheckState = CheckState.Unchecked;
            }

            refresh();
        }

        private void TsmiFilterGwi_Click(object sender, EventArgs e)
        {
            FilterItemCheckStateRefresh();

            refresh();
        }

        private void tsmiFilterMuc_Click(object sender, EventArgs e)
        {
            FilterItemCheckStateRefresh();

            refresh();
        }

        private void tsmiFilterMml_Click(object sender, EventArgs e)
        {
            FilterItemCheckStateRefresh();

            refresh();
        }

        private void TsmiFilterWav_Click(object sender, EventArgs e)
        {
            FilterItemCheckStateRefresh();

            refresh();
        }

        private void FilterItemCheckStateRefresh()
        {
            foreach (ToolStripItem tsi in tsddbFilter.DropDownItems)
            {
                if (!(tsi is ToolStripMenuItem)) continue;
                ToolStripMenuItem item = (ToolStripMenuItem)tsi;
                if (item.Text != FILTER_ALL && item.Text != FILTER_FILE) continue;
                item.CheckState = CheckState.Unchecked;
            }
        }

        private void TvFolderTree_AfterExpand(object sender, TreeViewEventArgs e)
        {
            UpdateFileList(tvFolderTree.SelectedNode);
        }

        private void UpdateFileList(TreeNode node)
        {
            lvFileList.BeginUpdate();
            lvFileList.Items.Clear();
            try
            {
                if (node == null) return;

                string path1 = Path.GetDirectoryName(basePath);
                path1 = string.IsNullOrEmpty(path1) ? basePath : path1;
                string directoryPath = Path.Combine(path1, Path.GetFullPath(node.FullPath));
                DirectoryInfo directory = new DirectoryInfo(directoryPath);
                if (ShouldShowFolders())
                {
                    foreach (DirectoryInfo subDirectory in directory.GetDirectories()
                        .OrderBy(directoryInfo => directoryInfo.Name, StringComparer.CurrentCultureIgnoreCase))
                    {
                        ListViewItem item = new ListViewItem(subDirectory.Name, 1);
                        item.SubItems.Add(subDirectory.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"));
                        item.SubItems.Add(string.Empty);
                        item.Tag = subDirectory.FullName;
                        lvFileList.Items.Add(item);
                    }
                }
                foreach (FileInfo file in directory.GetFiles()
                    .OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (!IsFileVisible(file)) continue;

                    ListViewItem item = new ListViewItem(file.Name, GetFileIconIndex(file.Extension));
                    item.SubItems.Add(file.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"));
                    ListViewItem.ListViewSubItem fileSizeSubItem = item.SubItems.Add(FormatFileSize(file.Length));
                    fileSizeSubItem.Tag = file.Length;
                    string[] waveInfo = GetWaveInfo(file.FullName);
                    foreach (string value in waveInfo)
                    {
                        item.SubItems.Add(value);
                    }
                    item.Tag = file.FullName;
                    lvFileList.Items.Add(item);
                }
            }
            catch (UnauthorizedAccessException)
            {
                LogFolderOperationException("UpdateFileList", null, node?.FullPath);
                MessageBox.Show("アクセスが拒否されました。", "権限不足", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (DirectoryNotFoundException)
            {
                LogFolderOperationException("UpdateFileList", null, node?.FullPath);
            }
            catch (Exception ex)
            {
                LogFolderOperationException("UpdateFileList", ex, node?.FullPath);
            }
            finally
            {
                lvFileList.EndUpdate();
                if (lvFileList.ListViewItemSorter != null) lvFileList.Sort();
                if (lvFileList.Items.Count > 0)
                {
                    ListViewItem firstItem = lvFileList.Items[0];
                    firstItem.Selected = true;
                    firstItem.Focused = true;
                    firstItem.EnsureVisible();
                    if (lvFileList.CanFocus) lvFileList.Focus();
                }
            }
        }

        private int GetFileIconIndex(string extension)
        {
            if (extension.Equals(".gwi", StringComparison.OrdinalIgnoreCase)) return 2;
            return AudioFileExtensions.Contains(extension) ? 0 : 3;
        }

        private void AddFileIconVariants()
        {
            using (Bitmap source = new Bitmap(imgList.Images[0]))
            using (Bitmap gwiIcon = RecolorFileIconFrame(source, Color.FromArgb(255, 105, 180)))
            using (Bitmap otherIcon = RecolorFileIconFrame(source, Color.FromArgb(150, 150, 150)))
            {
                imgList.Images.Add("FileGwi.png", gwiIcon);
                imgList.Images.Add("FileOther.png", otherIcon);
            }
        }

        private static Bitmap RecolorFileIconFrame(Bitmap source, Color frameColor)
        {
            Bitmap icon = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    Color pixel = source.GetPixel(x, y);
                    float hue = pixel.GetHue();
                    if (pixel.A > 0 && pixel.GetSaturation() > 0.2f && hue >= 155f && hue <= 255f)
                    {
                        float intensity = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) / 255f;
                        pixel = Color.FromArgb(pixel.A,
                            (int)(frameColor.R * intensity),
                            (int)(frameColor.G * intensity),
                            (int)(frameColor.B * intensity));
                    }
                    icon.SetPixel(x, y, pixel);
                }
            }
            return icon;
        }

        private string FormatFileSize(long sizeInBytes)
        {
            switch (fileSizeDisplayUnit)
            {
                case FileSizeDisplayUnit.KB:
                    return string.Format(CultureInfo.CurrentCulture, "{0:N1} KB", sizeInBytes / 1024d);
                case FileSizeDisplayUnit.MB:
                    return string.Format(CultureInfo.CurrentCulture, "{0:N2} MB", sizeInBytes / 1024d / 1024d);
                default:
                    return string.Format(CultureInfo.CurrentCulture, "{0:N0} bytes", sizeInBytes);
            }
        }

        private void UpdateFileSizeDisplay()
        {
            switch (fileSizeDisplayUnit)
            {
                case FileSizeDisplayUnit.KB:
                    tsbSizeUnit.Text = "Size: KB";
                    columnHeaderSize.Text = "Size (KB)";
                    break;
                case FileSizeDisplayUnit.MB:
                    tsbSizeUnit.Text = "Size: MB";
                    columnHeaderSize.Text = "Size (MB)";
                    break;
                default:
                    tsbSizeUnit.Text = "Size: B";
                    columnHeaderSize.Text = "Size (B)";
                    break;
            }
        }

        private void TsbSizeUnit_Click(object sender, EventArgs e)
        {
            switch (fileSizeDisplayUnit)
            {
                case FileSizeDisplayUnit.Byte:
                    fileSizeDisplayUnit = FileSizeDisplayUnit.KB;
                    break;
                case FileSizeDisplayUnit.KB:
                    fileSizeDisplayUnit = FileSizeDisplayUnit.MB;
                    break;
                default:
                    fileSizeDisplayUnit = FileSizeDisplayUnit.Byte;
                    break;
            }

            UpdateFileSizeDisplay();
            UpdateFileList(tvFolderTree.SelectedNode ?? (tvFolderTree.Nodes.Count > 0 ? tvFolderTree.Nodes[0] : null));
        }

        private void LvFileList_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == columnHeaderHeaderFill.Index) return;

            if (fileListSortColumn == e.Column)
            {
                fileListSortAscending = !fileListSortAscending;
            }
            else
            {
                fileListSortColumn = e.Column;
                fileListSortAscending = true;
            }

            lvFileList.ListViewItemSorter = new FileListItemComparer(fileListSortColumn, fileListSortAscending);
            lvFileList.Sort();
        }

        private void LvFileList_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
        {
            if (e.ColumnIndex == columnHeaderHeaderFill.Index) e.Cancel = true;
        }

        private void LvFileList_ColumnWidthChanged(object sender, ColumnWidthChangedEventArgs e)
        {
            if (e.ColumnIndex < 0 || e.ColumnIndex >= lvFileList.Columns.Count) return;

            ColumnHeader column = lvFileList.Columns[e.ColumnIndex];
            if (column == columnHeaderHeaderFill) return;
            if (column.Width > 0) fileListColumnDisplayWidths[column] = column.Width;
        }

        private void LvFileList_ColumnReordered(object sender, ColumnReorderedEventArgs e)
        {
            BeginInvoke((MethodInvoker)EnsureHeaderFillColumnAtEnd);
        }

        private void EnsureHeaderFillColumnAtEnd()
        {
            if (IsDisposed || !lvFileList.IsHandleCreated) return;

            int lastIndex = lvFileList.Columns.Count - 1;
            if (lastIndex < 0) return;
            if (columnHeaderHeaderFill.DisplayIndex == lastIndex) return;

            columnHeaderHeaderFill.DisplayIndex = lastIndex;
        }

        private void LvFileList_SizeChanged(object sender, EventArgs e)
        {
            UpdateHeaderFillWidth();
        }

        private void LvFileList_HandleCreated(object sender, EventArgs e)
        {
            UpdateHeaderFillWidth();
            AttachFileListHeaderWindow();
        }

        private void LvFileList_HandleDestroyed(object sender, EventArgs e)
        {
            DetachFileListHeaderWindow();
        }

        private void SplitContainer1_SizeChanged(object sender, EventArgs e)
        {
            UpdateHeaderFillWidth();
        }

        private void FrmFolderTree_Shown(object sender, EventArgs e)
        {
            UpdateHeaderFillWidth();
        }

        private void UpdateHeaderFillWidth()
        {
            if (lvFileList.Columns.Count == 0) return;
            int fixedWidth = 0;
            foreach (ColumnHeader column in lvFileList.Columns)
            {
                if (column != columnHeaderHeaderFill) fixedWidth += column.Width;
            }
            columnHeaderHeaderFill.Width = Math.Max(0, lvFileList.ClientSize.Width - fixedWidth);
        }

        private void LvFileList_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush backgroundBrush = new SolidBrush(FileListHeaderBackColor))
            {
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
            }

            TextFormatFlags alignment = e.Header.TextAlign == HorizontalAlignment.Right
                ? TextFormatFlags.Right
                : e.Header.TextAlign == HorizontalAlignment.Center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
            bool hasSortIcon = (fileListSortColumn == e.ColumnIndex && e.ColumnIndex != columnHeaderHeaderFill.Index);
            Rectangle textBounds = Rectangle.Inflate(e.Bounds, -6, 0);
            if (hasSortIcon)
            {
                textBounds.Width = Math.Max(0, textBounds.Width - 14);
            }
            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font,
                textBounds, FileListHeaderForeColor,
                alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (hasSortIcon)
            {
                DrawFileListSortGlyph(e.Graphics, e.Bounds, fileListSortAscending);
            }
        }

        private static void DrawFileListSortGlyph(Graphics graphics, Rectangle bounds, bool ascending)
        {
            int glyphWidth = 8;
            int glyphHeight = 6;
            int glyphX = bounds.Right - glyphWidth - 6;
            int glyphY = bounds.Top + (bounds.Height - glyphHeight) / 2;

            Point[] points = ascending
                ? new[]
                {
                    new Point(glyphX, glyphY + glyphHeight),
                    new Point(glyphX + glyphWidth, glyphY + glyphHeight),
                    new Point(glyphX + glyphWidth / 2, glyphY)
                }
                : new[]
                {
                    new Point(glyphX, glyphY),
                    new Point(glyphX + glyphWidth, glyphY),
                    new Point(glyphX + glyphWidth / 2, glyphY + glyphHeight)
                };

            using (SolidBrush brush = new SolidBrush(FileListHeaderSortGlyphColor))
            {
                graphics.FillPolygon(brush, points);
            }
        }

        private void LvFileList_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            if (e.Item.Selected)
            {
                Color backColor = lvFileList.Focused ? FileListActiveSelectionBackColor : FileListInactiveSelectionBackColor;
                using (SolidBrush backgroundBrush = new SolidBrush(backColor))
                {
                    e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
                }
                return;
            }

            if ((e.State & ListViewItemStates.Hot) != 0)
            {
                using (SolidBrush backgroundBrush = new SolidBrush(FileListHotBackColor))
                {
                    e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
                }
                return;
            }

            e.DrawDefault = true;
        }

        private void LvFileList_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            if (e.Item.Selected)
            {
                Color backColor = lvFileList.Focused ? FileListActiveSelectionBackColor : FileListInactiveSelectionBackColor;
                Color foreColor = lvFileList.Focused ? FileListActiveSelectionForeColor : FileListInactiveSelectionForeColor;
                using (SolidBrush backgroundBrush = new SolidBrush(backColor))
                {
                    e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
                }

                TextFormatFlags alignment = e.Header.TextAlign == HorizontalAlignment.Right
                    ? TextFormatFlags.Right
                    : e.Header.TextAlign == HorizontalAlignment.Center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;

                Rectangle textBounds = e.Bounds;
                if (e.ColumnIndex == 0 && e.Item.ImageList != null && e.Item.ImageIndex >= 0 && e.Item.ImageIndex < e.Item.ImageList.Images.Count)
                {
                    Image icon = e.Item.ImageList.Images[e.Item.ImageIndex];
                    int iconY = e.Bounds.Top + Math.Max(0, (e.Bounds.Height - icon.Height) / 2);
                    e.Graphics.DrawImage(icon, e.Bounds.Left + 2, iconY, icon.Width, icon.Height);
                    textBounds = Rectangle.FromLTRB(e.Bounds.Left + icon.Width + 6, e.Bounds.Top, e.Bounds.Right, e.Bounds.Bottom);
                }

                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.SubItem.Font,
                    textBounds, foreColor,
                    alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                return;
            }

            if ((e.ItemState & ListViewItemStates.Hot) != 0)
            {
                using (SolidBrush backgroundBrush = new SolidBrush(FileListHotBackColor))
                {
                    e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
                }

                TextFormatFlags alignment = e.Header.TextAlign == HorizontalAlignment.Right
                    ? TextFormatFlags.Right
                    : e.Header.TextAlign == HorizontalAlignment.Center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;

                Rectangle textBounds = e.Bounds;
                if (e.ColumnIndex == 0 && e.Item.ImageList != null && e.Item.ImageIndex >= 0 && e.Item.ImageIndex < e.Item.ImageList.Images.Count)
                {
                    Image icon = e.Item.ImageList.Images[e.Item.ImageIndex];
                    int iconY = e.Bounds.Top + Math.Max(0, (e.Bounds.Height - icon.Height) / 2);
                    e.Graphics.DrawImage(icon, e.Bounds.Left + 2, iconY, icon.Width, icon.Height);
                    textBounds = Rectangle.FromLTRB(e.Bounds.Left + icon.Width + 6, e.Bounds.Top, e.Bounds.Right, e.Bounds.Bottom);
                }

                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.SubItem.Font,
                    textBounds, FileListHotForeColor,
                    alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                return;
            }

            e.DrawDefault = true;
        }

        private sealed class FileListItemComparer : IComparer
        {
            private readonly int column;
            private readonly bool ascending;

            public FileListItemComparer(int column, bool ascending)
            {
                this.column = column;
                this.ascending = ascending;
            }

            public int Compare(object x, object y)
            {
                ListViewItem left = (ListViewItem)x;
                ListViewItem right = (ListViewItem)y;
                IComparable leftKey = GetSortKey(left, column);
                IComparable rightKey = GetSortKey(right, column);
                int result;

                if (leftKey == null || rightKey == null)
                {
                    result = leftKey == null ? (rightKey == null ? 0 : -1) : 1;
                }
                else
                {
                    result = leftKey.CompareTo(rightKey);
                }

                return ascending ? result : -result;
            }

            private static IComparable GetSortKey(ListViewItem item, int column)
            {
                string text = item.SubItems[column].Text;
                if (string.IsNullOrWhiteSpace(text)) return null;

                switch (column)
                {
                    case 1:
                        if (DateTime.TryParseExact(text, "yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out DateTime lastWriteTime)) return lastWriteTime;
                        break;
                    case 2:
                        object sizeTag = item.SubItems[column].Tag;
                        if (sizeTag is long sizeFromTag) return sizeFromTag;
                        if (long.TryParse(text.Replace(" bytes", string.Empty), NumberStyles.Number,
                            CultureInfo.CurrentCulture, out long size)) return size;
                        break;
                    case 3:
                        string[] durationParts = text.Split(':');
                        if (durationParts.Length == 3
                            && int.TryParse(durationParts[0], out int hours)
                            && int.TryParse(durationParts[1], out int minutes)
                            && int.TryParse(durationParts[2], out int seconds))
                        {
                            return TimeSpan.FromHours(hours).Add(TimeSpan.FromMinutes(minutes)).Add(TimeSpan.FromSeconds(seconds));
                        }
                        break;
                    case 4:
                        if (long.TryParse(text.Replace(" Hz", string.Empty), NumberStyles.Number,
                            CultureInfo.CurrentCulture, out long sampleRate)) return sampleRate;
                        break;
                    case 5:
                        if (int.TryParse(text.Replace(" bit", string.Empty), out int bitsPerSample)) return bitsPerSample;
                        break;
                    case 6:
                        if (text == "Mono" || text == "モノラル") return 1;
                        if (text == "Stereo" || text == "ステレオ") return 2;
                        if (int.TryParse(text.Replace(" ch", string.Empty), out int channels)) return channels;
                        break;
                }

                return text;
            }
        }

        private bool IsFileVisible(FileInfo file)
        {
            if (tsmiFilterAll.Checked || tsmiFilterFile.Checked) return true;
            string extension = file.Extension;
            return (tsmiFilterGwi.Checked && extension.Equals(".gwi", StringComparison.OrdinalIgnoreCase))
                || (tsmiFilterMuc.Checked && extension.Equals(".muc", StringComparison.OrdinalIgnoreCase))
                || (tsmiFilterMml.Checked && extension.Equals(".mml", StringComparison.OrdinalIgnoreCase))
                || (tsmiFilterWav.Checked && extension.Equals(".wav", StringComparison.OrdinalIgnoreCase));
        }

        private string[] GetWaveInfo(string filePath)
        {
            string[] waveInfo = new string[4];
            if (!Path.GetExtension(filePath).Equals(".wav", StringComparison.OrdinalIgnoreCase)) return waveInfo;

            try
            {
                using (WaveFileReader reader = new WaveFileReader(filePath))
                {
                    TimeSpan duration = reader.TotalTime;
                    waveInfo[0] = string.Format("{0:00}:{1:00}:{2:00}",
                        (int)duration.TotalHours, duration.Minutes, duration.Seconds);
                    waveInfo[1] = reader.WaveFormat.SampleRate.ToString("N0");
                    waveInfo[2] = string.Format("{0} bit", reader.WaveFormat.BitsPerSample);
                    waveInfo[3] = reader.WaveFormat.Channels == 1
                        ? "Mono"
                        : reader.WaveFormat.Channels == 2 ? "Stereo" : string.Format("{0} ch", reader.WaveFormat.Channels);
                }
            }
            catch (Exception ex)
            {
                LogFolderOperationException("GetWaveInfo", ex, filePath);
            }

            return waveInfo;
        }

            private bool ShouldShowFolders()
            {
                return tsmiFilterAll.Checked || tsmiFilterFolder.Checked;
            }

        private List<string> GetSelectedFilePaths()
        {
            List<string> paths = new List<string>();
            foreach (ListViewItem item in lvFileList.SelectedItems)
            {
                if (item.Tag is string path && File.Exists(path)) paths.Add(path);
            }
            return paths;
        }

        private void LvFileList_ItemActivate(object sender, EventArgs e)
        {
            if (lvFileList.SelectedItems.Count == 1
                && lvFileList.SelectedItems[0].Tag is string selectedPath
                && Directory.Exists(selectedPath))
            {
                SelectFolderNode(selectedPath, tvFolderTree.Nodes);
                return;
            }

            List<string> paths = GetSelectedFilePaths();
            if (paths.Count > 0) parentExecFile?.Invoke(paths.ToArray());
        }

        private void LvFileList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Back) return;

            TreeNode parentNode = tvFolderTree.SelectedNode?.Parent;
            if (parentNode == null) return;

            tvFolderTree.SelectedNode = parentNode;
            parentNode.EnsureVisible();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private bool SelectFolderNode(string targetPath, TreeNodeCollection nodes)
        {
            string path1 = Path.GetDirectoryName(basePath);
            path1 = string.IsNullOrEmpty(path1) ? basePath : path1;
            foreach (TreeNode node in nodes)
            {
                string nodePath = Path.Combine(path1, Path.GetFullPath(node.FullPath));
                if (string.Equals(nodePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    tvFolderTree.SelectedNode = node;
                    node.EnsureVisible();
                    return true;
                }
                if (SelectFolderNode(targetPath, node.Nodes)) return true;
            }
            return false;
        }

        private void LvFileList_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            if (IsFileListHeaderArea(e.Location))
            {
                ShowFileListHeaderMenu(e.Location);
                return;
            }

            ListViewItem item = lvFileList.GetItemAt(e.X, e.Y);
            if (item == null) return;
            if (!item.Selected)
            {
                lvFileList.SelectedItems.Clear();
                item.Selected = true;
            }
            List<string> paths = GetSelectedFilePaths();
            CheckExtension(cmsMenu.Items, paths.ConvertAll(Path.GetExtension).ToArray());
            cmsMenu.Show(lvFileList, e.Location);
        }

        private void CmsMenu_Opening(object sender, CancelEventArgs e)
        {
            e.Cancel = GetSelectedFilePaths().Count == 0;
        }

        private void InitializeFileListColumnDisplayWidths()
        {
            foreach (ColumnHeader column in lvFileList.Columns)
            {
                if (column == columnHeaderHeaderFill) continue;
                fileListColumnDisplayWidths[column] = Math.Max(1, column.Width);
            }
        }

        private void AttachFileListHeaderWindow()
        {
            if (!lvFileList.IsHandleCreated) return;

            IntPtr headerHandle = SendMessage(lvFileList.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
            if (headerHandle == IntPtr.Zero) return;
            if (lvFileListHeaderHandle == headerHandle && lvFileListHeaderWindow != null) return;

            DetachFileListHeaderWindow();
            lvFileListHeaderWindow = new FileListHeaderNativeWindow(this);
            lvFileListHeaderWindow.AssignHandle(headerHandle);
            lvFileListHeaderHandle = headerHandle;
        }

        private void DetachFileListHeaderWindow()
        {
            if (lvFileListHeaderWindow != null && lvFileListHeaderHandle != IntPtr.Zero)
            {
                lvFileListHeaderWindow.ReleaseHandle();
            }
            lvFileListHeaderHandle = IntPtr.Zero;
            lvFileListHeaderWindow = null;
        }

        private bool IsFileListHeaderArea(Point location)
        {
            return lvFileList.View == View.Details && location.Y >= 0 && location.Y <= lvFileList.Font.Height + 8;
        }

        private void ShowFileListHeaderMenuFromCursor()
        {
            Point location = lvFileList.PointToClient(Cursor.Position);
            if (location.X < 0 || location.Y < 0) return;

            ShowFileListHeaderMenu(location);
        }

        private void ShowFileListHeaderMenu(Point location)
        {
            int columnIndex = GetFileListColumnIndex(location.X);
            if (columnIndex < 0 || columnIndex >= lvFileList.Columns.Count) return;

            ColumnHeader targetColumn = lvFileList.Columns[columnIndex];
            if (targetColumn == columnHeaderHeaderFill) return;

            cmsFileListHeaderMenu.Items.Clear();
            if (!string.IsNullOrEmpty(targetColumn.Text) && targetColumn.Width > 0)
            {
                ToolStripMenuItem hideItem = new ToolStripMenuItem(string.Format(CultureInfo.CurrentCulture, "Hide {0}", targetColumn.Text));
                hideItem.Tag = targetColumn;
                hideItem.Click += FileListHeaderMenuItem_Click;
                cmsFileListHeaderMenu.Items.Add(hideItem);
            }

            ToolStripMenuItem showAllItem = new ToolStripMenuItem(FileListHeaderMenuShowAllText);
            showAllItem.Click += FileListHeaderMenuItem_Click;
            cmsFileListHeaderMenu.Items.Add(showAllItem);
            cmsFileListHeaderMenu.Items.Add("-");

            foreach (ColumnHeader column in lvFileList.Columns)
            {
                if (column == targetColumn) continue;
                if (column == columnHeaderHeaderFill) continue;
                if (string.IsNullOrEmpty(column.Text)) continue;

                ToolStripMenuItem item = new ToolStripMenuItem(column.Text);
                item.Tag = column;
                item.Checked = column.Width > 0;
                item.Click += FileListHeaderMenuItem_Click;
                cmsFileListHeaderMenu.Items.Add(item);
            }

            cmsFileListHeaderMenu.Show(lvFileList, location);
        }

        private int GetFileListColumnIndex(int x)
        {
            int left = 0;
            ColumnHeader[] orderedColumns = new ColumnHeader[lvFileList.Columns.Count];
            foreach (ColumnHeader column in lvFileList.Columns)
            {
                int displayIndex = column.DisplayIndex;
                if (displayIndex < 0 || displayIndex >= orderedColumns.Length) continue;
                orderedColumns[displayIndex] = column;
            }

            for (int i = 0; i < orderedColumns.Length; i++)
            {
                ColumnHeader column = orderedColumns[i];
                if (column == null) continue;
                int width = column.Width;
                if (x >= left && x < left + width) return column.Index;
                left += width;
            }

            return -1;
        }

        private void FileListHeaderMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item == null) return;

            ColumnHeader column = item.Tag as ColumnHeader;
            if (column != null)
            {
                ToggleFileListColumn(column);
                UpdateHeaderFillWidth();
                return;
            }

            if (item.Text == FileListHeaderMenuShowAllText)
            {
                foreach (ColumnHeader c in lvFileList.Columns)
                {
                    if (c == columnHeaderHeaderFill) continue;
                    int width = fileListColumnDisplayWidths.ContainsKey(c) ? fileListColumnDisplayWidths[c] : Math.Max(1, c.Width);
                    c.Width = Math.Max(1, width);
                }
                UpdateHeaderFillWidth();
            }
        }

        private void ToggleFileListColumn(ColumnHeader column)
        {
            if (column == null || column == columnHeaderHeaderFill) return;

            if (column.Width > 0)
            {
                fileListColumnDisplayWidths[column] = column.Width;
                column.Width = 0;
                return;
            }

            int width = fileListColumnDisplayWidths.ContainsKey(column) ? fileListColumnDisplayWidths[column] : 80;
            column.Width = Math.Max(1, width);
        }

        private static void LogFolderOperationException(string operation, Exception ex, string target)
        {
            string safeTarget = string.IsNullOrWhiteSpace(target) ? "(unknown)" : target;
            (string operationId, string operationName) = GetFolderOperationLogInfo(operation);
            if (ex == null)
            {
                Log.ForcedWrite("【フォルダー操作異常】ID={0} 操作={1} 対象=[{2}] 例外情報=なし", operationId, operationName, safeTarget);
                return;
            }

            Log.ForcedWrite(
                "【フォルダー操作異常】ID={0} 操作={1} 対象=[{2}] 例外種別={3} メッセージ={4}",
                operationId,
                operationName,
                safeTarget,
                ex.GetType().Name,
                ex.Message);
            if (ex != null) Log.ForcedWrite(ex);
        }

        private static (string operationId, string operationName) GetFolderOperationLogInfo(string operation)
        {
            switch (operation)
            {
                case "TvFolderTree_BeforeExpand":
                    return ("FT-001", "ツリー展開");
                case "refreshAddCheck":
                    return ("FT-002", "ツリー再読込");
                case "CopyFile_DragDrop":
                    return ("FT-003", "ドラッグ&ドロップコピー/移動");
                case "UpdateFileList":
                    return ("FT-004", "ファイル一覧更新");
                case "GetWaveInfo":
                    return ("FT-005", "WAV情報取得");
                default:
                    return ("FT-999", string.IsNullOrWhiteSpace(operation) ? "不明な操作" : operation);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private sealed class FileListHeaderNativeWindow : NativeWindow
        {
            private readonly FrmFolderTree owner;

            public FileListHeaderNativeWindow(FrmFolderTree owner)
            {
                this.owner = owner;
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_CONTEXTMENU)
                {
                    owner.ShowFileListHeaderMenuFromCursor();
                    return;
                }

                base.WndProc(ref m);
            }
        }

        private void tvFolderTree_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {

        }
    }
}
