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
        private ContextMenuStrip cmsErrorList = null;
        private ToolStripMenuItem tsmiCopy = null;
        private ToolStripMenuItem tsmiCopyAll = null;

        public FrmErrorList(Setting setting)
        {
            InitializeComponent();
            this.dataGridView1.BackgroundColor = Color.FromArgb(setting.ColorScheme.ErrorList_BackColor);
            this.dataGridView1.DefaultCellStyle.BackColor = Color.FromArgb(setting.ColorScheme.ErrorList_BackColor);
            this.dataGridView1.ForeColor = Color.FromArgb(setting.ColorScheme.ErrorList_ForeColor);
            this.dataGridView1.MultiSelect = true;
            this.dataGridView1.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
            this.dataGridView1.KeyDown += DataGridView1_KeyDown;
            InitializeContextMenu();

            Common.SetDoubleBuffered(dataGridView1);
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
            string f = dataGridView1.Rows[rowIndex].Cells[1].Value.ToString();
            string l = dataGridView1.Rows[rowIndex].Cells[2].Value.ToString();
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
            tsmiCopy = new ToolStripMenuItem("コピー", null, TsmiCopy_Click);
            tsmiCopyAll = new ToolStripMenuItem("全件コピー", null, TsmiCopyAll_Click);

            cmsErrorList.Items.Add(tsmiCopy);
            cmsErrorList.Items.Add(tsmiCopyAll);
            cmsErrorList.Opening += CmsErrorList_Opening;

            dataGridView1.ContextMenuStrip = cmsErrorList;
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
            tsmiCopyAll.Enabled = (dataGridView1.Rows.Count > 0);
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
                if (row.IsNewRow) continue;

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
    }
}
