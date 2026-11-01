using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace mml2vgmIDEx64
{
    partial class FrmAbout : Form
    {
        public FrmAbout()
        {
            InitializeComponent();
            Common.ApplyDarkScrollBarTheme(this);
            Common.ApplyDarkControlTheme(this);
            this.Text = String.Format("{0} 分家改造版のバージョン情報", AssemblyTitle);
            this.labelProductName.Text = AssemblyProduct;
            this.labelVersion.Text = String.Format("バージョン {0}", AssemblyVersion);
            this.labelCopyright.Text = AssemblyCopyright;
            this.labelCompanyName.Text = AssemblyCompany;

            this.labelBuildInfo.Text = String.Format("{0} 分家改造版{1}ビルド日時: {2}", AssemblyProduct, Environment.NewLine, AssemblyBuildDate);
            this.labelMaintainer.Text = "メンテナ: guhon";

            this.llOpenGithub.LinkColor = Color.Gold;
            this.llOpenGithub.ActiveLinkColor = Color.Orange;
            this.llOpenGithub.VisitedLinkColor = Color.Khaki;
            this.llOpenX.LinkColor = Color.Gold;
            this.llOpenX.ActiveLinkColor = Color.Orange;
            this.llOpenX.VisitedLinkColor = Color.Khaki;
        }

        #region アセンブリ属性アクセサー

        public string AssemblyTitle
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyTitleAttribute), false);
                if (attributes.Length > 0)
                {
                    AssemblyTitleAttribute titleAttribute = (AssemblyTitleAttribute)attributes[0];
                    if (titleAttribute.Title != "")
                    {
                        return titleAttribute.Title;
                    }
                }
                return System.IO.Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
            }
        }

        public string AssemblyVersion
        {
            get
            {
                return Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
        }

        public string AssemblyBuildDate
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyMetadataAttribute), false);
                foreach (AssemblyMetadataAttribute attribute in attributes)
                {
                    if (attribute.Key == "BuildDate")
                    {
                        return attribute.Value;
                    }
                }
                return "不明";
            }
        }

        public string AssemblyDescription
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyDescriptionAttribute)attributes[0]).Description;
            }
        }

        public string AssemblyProduct
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyProductAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyProductAttribute)attributes[0]).Product;
            }
        }

        public string AssemblyCopyright
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyCopyrightAttribute)attributes[0]).Copyright;
            }
        }

        public string AssemblyCompany
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCompanyAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyCompanyAttribute)attributes[0]).Company;
            }
        }
        #endregion

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            LinkLabel link = sender as LinkLabel;
            if (link != null) link.LinkVisited = true;
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = link?.Text ?? llOpenGithub.Text
            }).Dispose();

        }
    }
}
