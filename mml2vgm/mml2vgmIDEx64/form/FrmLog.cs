using System;
using System.Drawing;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace mml2vgmIDEx64
{
    public partial class FrmLog : WeifenLuo.WinFormsUI.Docking.DockContent
    {
        public Action parentUpdate = null;
        private readonly ThemeBase menuTheme = null;
        private ContextMenuStrip cmsLog = null;
        private ToolStripMenuItem tsmiCopy = null;
        private ToolStripMenuItem tsmiSelectAll = null;

        public FrmLog(Setting setting, ThemeBase theme)
        {
            InitializeComponent();
            menuTheme = theme;
            Common.ApplyDarkScrollBarTheme(this);
            ApplyColorSetting(setting);
            InitializeContextMenu(setting);
            theme.ApplyTo(toolStrip1);

            Common.SetDoubleBuffered(this);
            Common.SetDoubleBuffered(tbLog);
        }

        public void ApplyColorSetting(Setting setting)
        {
            if (setting == null) return;
            this.tbLog.BackColor = Color.FromArgb(setting.ColorScheme.Log_BackColor);
            this.tbLog.ForeColor = Color.FromArgb(setting.ColorScheme.Log_ForeColor);
            ApplyContextMenuTheme(setting);
        }

        public void ClearLog()
        {
            tbLog.Clear();
        }

        protected override string GetPersistString()
        {
            return this.Name;
        }

        private void FrmLog_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            parentUpdate?.Invoke();
        }

        private void TbLog_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.None;
        }

        private void TsbClearLog_Click(object sender, EventArgs e)
        {
            ClearLog();
        }

        private void InitializeContextMenu(Setting setting)
        {
            cmsLog = new ContextMenuStrip();
            tsmiCopy = new ToolStripMenuItem("コピー", null, TsmiCopy_Click);
            tsmiSelectAll = new ToolStripMenuItem("すべて選択", null, TsmiSelectAll_Click);

            cmsLog.Items.Add(tsmiCopy);
            cmsLog.Items.Add(tsmiSelectAll);
            cmsLog.Opening += CmsLog_Opening;
            tbLog.ContextMenuStrip = cmsLog;

            ApplyContextMenuTheme(setting);
        }

        private void ApplyContextMenuTheme(Setting setting)
        {
            if (cmsLog == null) return;
            if (menuTheme != null)
            {
                menuTheme.ApplyTo(cmsLog);
                return;
            }
            if (setting == null) return;

            Common.ApplyDarkContextMenuTheme(
                cmsLog,
                Color.FromArgb(setting.ColorScheme.Log_BackColor),
                Color.FromArgb(setting.ColorScheme.Log_ForeColor),
                Color.FromArgb(setting.ColorScheme.Panel_HeaderHighlightBackColor),
                Color.FromArgb(setting.ColorScheme.Panel_GridBorderColor));
        }

        private void CmsLog_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            bool hasText = !string.IsNullOrEmpty(tbLog.Text);
            tsmiCopy.Enabled = tbLog.SelectionLength > 0;
            tsmiSelectAll.Enabled = hasText;
        }

        private void TsmiCopy_Click(object sender, EventArgs e)
        {
            if (tbLog.SelectionLength > 0)
            {
                tbLog.Copy();
            }
        }

        private void TsmiSelectAll_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(tbLog.Text))
            {
                tbLog.SelectAll();
            }
        }
    }
}
