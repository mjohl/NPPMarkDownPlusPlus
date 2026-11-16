namespace com.insanitydesign.MarkdownViewerPlusPlus.Forms
{
    partial class OptionsPanelGeneral
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        protected override void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.lblFileExtensions = new System.Windows.Forms.Label();
            this.chkBoxNewFiles = new System.Windows.Forms.CheckBox();
            this.toolTipFileExtensions = new System.Windows.Forms.ToolTip(this.components);
            this.grpBoxFiles = new System.Windows.Forms.GroupBox();
            this.txtFileExtensions = new System.Windows.Forms.TextBox();
            this.grpBoxPrivacy = new System.Windows.Forms.GroupBox();
            this.chkBoxRemoteImages = new System.Windows.Forms.CheckBox();
            this.grpBoxFiles.SuspendLayout();
            this.grpBoxPrivacy.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblFileExtensions
            // 
            this.lblFileExtensions.AutoSize = true;
            this.lblFileExtensions.Location = new System.Drawing.Point(6, 16);
            this.lblFileExtensions.Name = "lblFileExtensions";
            this.lblFileExtensions.Size = new System.Drawing.Size(76, 13);
            this.lblFileExtensions.TabIndex = 0;
            this.lblFileExtensions.Text = "File extensions";
            // 
            // chkBoxNewFiles
            // 
            this.chkBoxNewFiles.AutoSize = true;
            this.chkBoxNewFiles.Location = new System.Drawing.Point(9, 62);
            this.chkBoxNewFiles.Name = "chkBoxNewFiles";
            this.chkBoxNewFiles.Size = new System.Drawing.Size(111, 17);
            this.chkBoxNewFiles.TabIndex = 2;
            this.chkBoxNewFiles.Text = "Include new files?";
            this.chkBoxNewFiles.UseVisualStyleBackColor = true;
            // 
            // toolTipFileExtensions
            // 
            this.toolTipFileExtensions.AutomaticDelay = 0;
            this.toolTipFileExtensions.IsBalloon = true;
            this.toolTipFileExtensions.ShowAlways = true;
            this.toolTipFileExtensions.ToolTipIcon = System.Windows.Forms.ToolTipIcon.Info;
            this.toolTipFileExtensions.ToolTipTitle = "File extensions";
            // 
            // grpBoxFiles
            // 
            this.grpBoxFiles.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.grpBoxFiles.Controls.Add(this.lblFileExtensions);
            this.grpBoxFiles.Controls.Add(this.chkBoxNewFiles);
            this.grpBoxFiles.Controls.Add(this.txtFileExtensions);
            this.grpBoxFiles.Dock = System.Windows.Forms.DockStyle.Top;
            this.grpBoxFiles.Location = new System.Drawing.Point(0, 0);
            this.grpBoxFiles.Name = "grpBoxFiles";
            this.grpBoxFiles.Size = new System.Drawing.Size(1255, 86);
            this.grpBoxFiles.TabIndex = 3;
            this.grpBoxFiles.TabStop = false;
            this.grpBoxFiles.Text = "Files";
            // 
            // txtFileExtensions
            // 
            this.txtFileExtensions.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtFileExtensions.Location = new System.Drawing.Point(9, 36);
            this.txtFileExtensions.Name = "txtFileExtensions";
            this.txtFileExtensions.Size = new System.Drawing.Size(1240, 20);
            this.txtFileExtensions.TabIndex = 1;
            // 
            // grpBoxPrivacy
            // 
            this.grpBoxPrivacy.Controls.Add(this.chkBoxRemoteImages);
            this.grpBoxPrivacy.Dock = System.Windows.Forms.DockStyle.Top;
            this.grpBoxPrivacy.Location = new System.Drawing.Point(0, 86);
            this.grpBoxPrivacy.Name = "grpBoxPrivacy";
            this.grpBoxPrivacy.Size = new System.Drawing.Size(1255, 48);
            this.grpBoxPrivacy.TabIndex = 4;
            this.grpBoxPrivacy.TabStop = false;
            this.grpBoxPrivacy.Text = "Privacy";
            // 
            // chkBoxRemoteImages
            // 
            this.chkBoxRemoteImages.AutoSize = true;
            this.chkBoxRemoteImages.Location = new System.Drawing.Point(9, 20);
            this.chkBoxRemoteImages.Name = "chkBoxRemoteImages";
            this.chkBoxRemoteImages.Size = new System.Drawing.Size(330, 17);
            this.chkBoxRemoteImages.TabIndex = 0;
            this.chkBoxRemoteImages.Text = "Load remote images and stylesheets (http/https) referenced in documents";
            this.chkBoxRemoteImages.UseVisualStyleBackColor = true;
            // 
            // OptionsPanelGeneral
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.Controls.Add(this.grpBoxPrivacy);
            this.Controls.Add(this.grpBoxFiles);
            this.Name = "OptionsPanelGeneral";
            this.Size = new System.Drawing.Size(1255, 626);
            this.grpBoxFiles.ResumeLayout(false);
            this.grpBoxFiles.PerformLayout();
            this.grpBoxPrivacy.ResumeLayout(false);
            this.grpBoxPrivacy.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblFileExtensions;
        private System.Windows.Forms.CheckBox chkBoxNewFiles;
        private System.Windows.Forms.ToolTip toolTipFileExtensions;
        private System.Windows.Forms.GroupBox grpBoxFiles;
        private System.Windows.Forms.TextBox txtFileExtensions;
        private System.Windows.Forms.GroupBox grpBoxPrivacy;
        private System.Windows.Forms.CheckBox chkBoxRemoteImages;
    }
}
