using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace mml2vgmIDEx64
{
    public partial class FrmErrorList : WeifenLuo.WinFormsUI.Docking.DockContent
    {
        public Action parentUpdate = null;
        public Action<string, long, bool> parentJumpDocument = null;
        private readonly WeifenLuo.WinFormsUI.Docking.ThemeBase menuTheme = null;
        private ContextMenuStrip cmsErrorList = null;
        private ToolStripMenuItem tsmiCopy = null;
        private ToolStripMenuItem tsmiCopyAll = null;
        private Font headerFont = null;
        private StatusStrip ssErrorList = null;
        private ToolStripStatusLabel tslErrorCount = null;
        private ToolStripStatusLabel tslWarningCount = null;
        private ToolStripDropDownButton tsddbFilter = null;
        private readonly Setting currentSetting = null;
        private string currentFilter = "All";

        public FrmErrorList(Setting setting, WeifenLuo.WinFormsUI.Docking.ThemeBase theme)
        {
            currentSetting = setting;
            menuTheme = theme;
            InitializeComponent();
            InitializeStatusBar();
            Common.ApplyDarkScrollBarTheme(this);
            ApplyColorSetting(setting);
            ApplyUnifiedHeaderHeight();
            this.ClmNo.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            this.dataGridView1.MultiSelect = true;
            this.dataGridView1.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
            this.dataGridView1.KeyDown += DataGridView1_KeyDown;
            this.dataGridView1.RowsAdded += DataGridView1_RowsAdded;
            this.dataGridView1.RowsRemoved += DataGridView1_RowsRemoved;
            InitializeContextMenu();

            Common.SetDoubleBuffered(dataGridView1);
            Disposed += (sender, e) => headerFont?.Dispose();
        }

        public void ApplyColorSetting(Setting setting)
        {
            if (setting == null) return;
            this.dataGridView1.BackgroundColor = Color.FromArgb(setting.ColorScheme.ErrorList_BackColor);
            this.dataGridView1.DefaultCellStyle.BackColor = Color.FromArgb(setting.ColorScheme.ErrorList_BackColor);
            this.dataGridView1.ForeColor = Color.FromArgb(setting.ColorScheme.ErrorList_ForeColor);
            this.dataGridView1.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(setting.ColorScheme.Panel_HeaderBackColor);
            this.dataGridView1.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(setting.ColorScheme.Panel_HeaderForeColor);
            this.dataGridView1.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(setting.ColorScheme.Panel_HeaderBackColor);
            this.dataGridView1.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(setting.ColorScheme.Panel_HeaderForeColor);
            Font previousHeaderFont = headerFont;
            headerFont = new Font(setting.other.PartCounterFontName, setting.other.PartCounterFontSize, setting.other.PartCounterFontStyle);
            this.dataGridView1.ColumnHeadersDefaultCellStyle.Font = headerFont;
            previousHeaderFont?.Dispose();
            this.dataGridView1.GridColor = Color.FromArgb(setting.ColorScheme.Panel_GridBorderColor);

            if (ssErrorList != null)
            {
                ssErrorList.BackColor = Color.Black;
                ssErrorList.ForeColor = Color.FromArgb(setting.ColorScheme.Panel_HeaderForeColor);
                tsddbFilter.ForeColor = Color.FromArgb(setting.ColorScheme.Panel_HeaderForeColor);
            }

            ApplyFilterDropDownTheme(setting);
            ApplyContextMenuTheme(setting);

            ApplyUnifiedHeaderHeight();
        }

        private void ApplyUnifiedHeaderHeight()
        {
            Font activeHeaderFont = dataGridView1.ColumnHeadersDefaultCellStyle.Font ?? dataGridView1.Font;
            int headerHeight = Math.Max(21, activeHeaderFont.Height + 8);
            dataGridView1.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dataGridView1.ColumnHeadersHeight = headerHeight;
        }
        protected override string GetPersistString()
        {
            return this.Name;
        }

        private void FrmErrorList_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            parentUpdate?.Invoke();

        }

        private void DataGridView1_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0) return;
            int rowIndex = e.RowIndex;
            JumpDocument(rowIndex, false);
        }

        private void DataGridView1_CellMouseDoubleClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0) return;
            int rowIndex = e.RowIndex;
            JumpDocument(rowIndex, true);
        }

        private void DataGridView1_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count < 1 || dataGridView1.SelectedRows[0].Index < 0) return;
            int rowIndex = dataGridView1.SelectedRows[0].Index;
            JumpDocument(rowIndex, false);
        }

        private void JumpDocument(int rowIndex, bool wantFocus)
        {
            if (rowIndex < 0 || rowIndex > dataGridView1.Rows.Count - 1) return;
            string f = dataGridView1.Rows[rowIndex].Cells["ClmFileName"].Value?.ToString() ?? "";
            string l = dataGridView1.Rows[rowIndex].Cells["ClmLine"].Value?.ToString() ?? "";
            long n = 0;

            if (long.TryParse(l, out n))
            {
                parentJumpDocument?.Invoke(f, n, wantFocus);
            }
            else
            {
                parentJumpDocument?.Invoke("", -1, wantFocus);
            }
        }

        public void AddIssueRow(string level, string fileName, string line, string message)
        {
            int no = dataGridView1.Rows.Count + 1;
            int rowIndex = dataGridView1.Rows.Add(
                no.ToString(),
                level,
                fileName,
                line,
                message
            );
            if (level == "Error" || level == "Warning")
            {
                Color levelColor = level == "Error" ? Color.FromArgb(255, 112, 112) : Color.Yellow;
                dataGridView1.Rows[rowIndex].Cells["ClmEtype"].Style.ForeColor = levelColor;
                dataGridView1.Rows[rowIndex].Cells["ClmEtype"].Style.SelectionForeColor = levelColor;
            }
            ApplyLevelFilter();
            UpdateIssueCountStatus();
        }

        private void InitializeStatusBar()
        {
            ssErrorList = new StatusStrip();
            ssErrorList.SizingGrip = false;

            tslErrorCount = new ToolStripStatusLabel();
            tslErrorCount.ForeColor = Color.FromArgb(255, 112, 112);
            tslWarningCount = new ToolStripStatusLabel();
            tslWarningCount.ForeColor = Color.Yellow;
            tsddbFilter = new ToolStripDropDownButton();
            tsddbFilter.DropDownDirection = ToolStripDropDownDirection.AboveLeft;
            tsddbFilter.DisplayStyle = ToolStripItemDisplayStyle.Text;
            tsddbFilter.DropDownItems.Add("All", null, FilterMenuItem_Click);
            tsddbFilter.DropDownItems.Add("Error", null, FilterMenuItem_Click);
            tsddbFilter.DropDownItems.Add("Warning", null, FilterMenuItem_Click);

            string savedFilter = currentSetting?.other?.ErrorListFilter;
            if (savedFilter == "Error" || savedFilter == "Warning")
            {
                currentFilter = savedFilter;
            }
            else
            {
                currentFilter = "All";
            }

            tslErrorCount.Image = Properties.Resources.Error;
            tslErrorCount.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            tslWarningCount.Image = Properties.Resources.Warning;
            tslWarningCount.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;

            ssErrorList.Items.Add(tslErrorCount);
            ssErrorList.Items.Add(tslWarningCount);
            ssErrorList.Items.Add(new ToolStripStatusLabel { Spring = true });
            ssErrorList.Items.Add(tsddbFilter);

            this.Controls.Add(ssErrorList);
            ssErrorList.BringToFront();

            UpdateFilterStatusText();
            UpdateIssueCountStatus();
            ApplyLevelFilter();
        }

        private void ApplyFilterDropDownTheme(Setting setting)
        {
            if (tsddbFilter == null || setting == null) return;

            Color backColor = Color.FromArgb(setting.ColorScheme.ErrorList_BackColor);
            Color foreColor = Color.FromArgb(setting.ColorScheme.ErrorList_ForeColor);
            Color selectedColor = Color.FromArgb(setting.ColorScheme.Panel_HeaderHighlightBackColor);
            Color borderColor = Color.FromArgb(setting.ColorScheme.Panel_GridBorderColor);
            Color statusBackColor = Color.Black;
            Color statusForeColor = Color.FromArgb(setting.ColorScheme.Panel_HeaderForeColor);

            if (ssErrorList != null)
            {
                ssErrorList.RenderMode = ToolStripRenderMode.Professional;
                ssErrorList.Renderer = new DarkToolStripRenderer(statusBackColor, statusForeColor, selectedColor, borderColor);
            }

            tsddbFilter.DropDown.RenderMode = ToolStripRenderMode.Professional;
            tsddbFilter.DropDown.Renderer = new DarkToolStripRenderer(backColor, foreColor, selectedColor, borderColor);
            tsddbFilter.DropDown.BackColor = backColor;
            tsddbFilter.DropDown.ForeColor = foreColor;

            foreach (ToolStripItem item in tsddbFilter.DropDownItems)
            {
                item.BackColor = backColor;
                item.ForeColor = foreColor;
            }
        }

        private void FilterMenuItem_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripItem item)) return;
            string next = item.Text;
            if (next != "All" && next != "Error" && next != "Warning") return;

            currentFilter = next;

            if (currentSetting?.other != null) currentSetting.other.ErrorListFilter = currentFilter;
            UpdateFilterStatusText();
            ApplyLevelFilter();
        }

        private void ApplyLevelFilter()
        {
            bool showError = currentFilter != "Warning";
            bool showWarning = currentFilter != "Error";

            dataGridView1.CurrentCell = null;
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (row.IsNewRow) continue;

                string level = row.Cells["ClmEtype"].Value?.ToString() ?? "";
                row.Visible = (showError && level == "Error")
                    || (showWarning && level == "Warning")
                    || (level != "Error" && level != "Warning");
            }
        }

        private void UpdateFilterStatusText()
        {
            if (tsddbFilter == null) return;
            tsddbFilter.Text = $"Filter: {currentFilter}";
            tsddbFilter.ToolTipText = "フィルタを選択";
        }

        private void UpdateIssueCountStatus()
        {
            if (tslErrorCount == null || tslWarningCount == null) return;

            int errorCount = 0;
            int warningCount = 0;

            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (row.IsNewRow) continue;
                string level = row.Cells["ClmEtype"].Value?.ToString() ?? "";
                if (level == "Error") errorCount++;
                else if (level == "Warning") warningCount++;
            }

            tslErrorCount.Text = $"Error: {errorCount}";
            tslWarningCount.Text = $"Warning: {warningCount}";
        }

        private void DataGridView1_RowsAdded(object sender, DataGridViewRowsAddedEventArgs e)
        {
            UpdateIssueCountStatus();
        }

        private void DataGridView1_RowsRemoved(object sender, DataGridViewRowsRemovedEventArgs e)
        {
            UpdateIssueCountStatus();
        }

        private void FrmErrorList_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.None;
        }

        private void FrmErrorList_Load(object sender, EventArgs e)
        {

        }

        private void DataGridView1_KeyDown(object sender, KeyEventArgs e)
        {
            if (!e.Control) return;

            if (e.KeyCode == Keys.A)
            {
                dataGridView1.SelectAll();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode != Keys.C) return;

            CopySelectionToClipboard();

            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void InitializeContextMenu()
        {
            cmsErrorList = new ContextMenuStrip();
            cmsErrorList.ShowImageMargin = false;
            tsmiCopy = new ToolStripMenuItem("コピー", null, TsmiCopy_Click);
            tsmiCopyAll = new ToolStripMenuItem("全件コピー", null, TsmiCopyAll_Click);

            cmsErrorList.Items.Add(tsmiCopy);
            cmsErrorList.Items.Add(tsmiCopyAll);
            cmsErrorList.Opening += CmsErrorList_Opening;

            dataGridView1.ContextMenuStrip = cmsErrorList;
            ApplyContextMenuTheme(currentSetting);
        }

        private void ApplyContextMenuTheme(Setting setting)
        {
            if (cmsErrorList == null) return;

            if (menuTheme != null)
            {
                menuTheme.ApplyTo(cmsErrorList);
                cmsErrorList.ShowImageMargin = false;
                return;
            }
            if (setting == null) return;

            Common.ApplyDarkContextMenuTheme(
                cmsErrorList,
                Color.FromArgb(setting.ColorScheme.ErrorList_BackColor),
                Color.FromArgb(setting.ColorScheme.ErrorList_ForeColor),
                Color.FromArgb(setting.ColorScheme.Panel_HeaderHighlightBackColor),
                Color.FromArgb(setting.ColorScheme.Panel_GridBorderColor));
        }

        private void TsmiCopy_Click(object sender, EventArgs e)
        {
            CopySelectionToClipboard();
        }

        private void TsmiCopyAll_Click(object sender, EventArgs e)
        {
            CopyAllRowsToClipboard();
        }

        private void CmsErrorList_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            tsmiCopy.Enabled = (dataGridView1.SelectedCells.Count > 0 || dataGridView1.CurrentCell != null);
            bool hasVisibleRow = false;
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (row.IsNewRow) continue;
                if (row.Visible)
                {
                    hasVisibleRow = true;
                    break;
                }
            }
            tsmiCopyAll.Enabled = hasVisibleRow;
        }

        private void CopySelectionToClipboard()
        {
            if (dataGridView1.SelectedCells.Count < 1 && dataGridView1.CurrentCell != null)
            {
                dataGridView1.CurrentCell.Selected = true;
            }

            DataObject clipboardContent = dataGridView1.GetClipboardContent();
            if (clipboardContent != null)
            {
                Clipboard.SetDataObject(clipboardContent);
            }
        }

        private void CopyAllRowsToClipboard()
        {
            if (dataGridView1.Rows.Count < 1) return;

            StringBuilder sb = new StringBuilder();

            bool firstColumn = true;
            foreach (DataGridViewColumn column in dataGridView1.Columns)
            {
                if (!column.Visible) continue;
                if (!firstColumn) sb.Append('\t');
                sb.Append(column.HeaderText);
                firstColumn = false;
            }
            sb.AppendLine();

            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (row.IsNewRow || !row.Visible) continue;

                firstColumn = true;
                foreach (DataGridViewColumn column in dataGridView1.Columns)
                {
                    if (!column.Visible) continue;
                    if (!firstColumn) sb.Append('\t');
                    object value = row.Cells[column.Index].FormattedValue;
                    sb.Append(value == null ? "" : value.ToString());
                    firstColumn = false;
                }
                sb.AppendLine();
            }

            Clipboard.SetText(sb.ToString());
        }

        private sealed class DarkToolStripColorTable : ProfessionalColorTable
        {
            private readonly Color backColor;
            private readonly Color foreColor;
            private readonly Color selectedColor;
            private readonly Color borderColor;

            public DarkToolStripColorTable(Color backColor, Color foreColor, Color selectedColor, Color borderColor)
            {
                this.backColor = backColor;
                this.foreColor = foreColor;
                this.selectedColor = selectedColor;
                this.borderColor = borderColor;
            }

            public override Color ToolStripDropDownBackground => backColor;
            public override Color MenuBorder => borderColor;
            public override Color MenuItemBorder => borderColor;
            public override Color MenuItemSelected => selectedColor;
            public override Color MenuItemSelectedGradientBegin => selectedColor;
            public override Color MenuItemSelectedGradientEnd => selectedColor;
            public override Color MenuItemPressedGradientBegin => selectedColor;
            public override Color MenuItemPressedGradientMiddle => selectedColor;
            public override Color MenuItemPressedGradientEnd => selectedColor;
            public override Color ImageMarginGradientBegin => backColor;
            public override Color ImageMarginGradientMiddle => backColor;
            public override Color ImageMarginGradientEnd => backColor;
            public override Color ButtonSelectedHighlight => selectedColor;
            public override Color ButtonSelectedHighlightBorder => borderColor;
            public override Color ButtonPressedHighlight => selectedColor;
            public override Color ButtonPressedHighlightBorder => borderColor;
            public override Color ButtonCheckedHighlight => selectedColor;
            public override Color ButtonCheckedHighlightBorder => borderColor;
            public override Color ButtonCheckedGradientBegin => selectedColor;
            public override Color ButtonCheckedGradientMiddle => selectedColor;
            public override Color ButtonCheckedGradientEnd => selectedColor;
            public override Color ButtonPressedGradientBegin => selectedColor;
            public override Color ButtonPressedGradientMiddle => selectedColor;
            public override Color ButtonPressedGradientEnd => selectedColor;
            public override Color ButtonSelectedGradientBegin => selectedColor;
            public override Color ButtonSelectedGradientMiddle => selectedColor;
            public override Color ButtonSelectedGradientEnd => selectedColor;
            public override Color StatusStripGradientBegin => backColor;
            public override Color StatusStripGradientEnd => backColor;
        }

        private sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
        {
            public DarkToolStripRenderer(Color backColor, Color foreColor, Color selectedColor, Color borderColor)
                : base(new DarkToolStripColorTable(backColor, foreColor, selectedColor, borderColor))
            {
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.ForeColor;
                base.OnRenderItemText(e);
            }
        }
    }
}
