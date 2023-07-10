namespace Mount3DX
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            grp3dx = new GroupBox();
            label4 = new Label();
            txtRefreshIntervalMinutes = new NumericUpDown();
            label1 = new Label();
            txt3dxServerUrl = new TextBox();
            label2 = new Label();
            btnStart = new Button();
            lblRunningStatus = new Label();
            btnOpenVirtualDrive = new Button();
            lblVersion = new Label();
            grp3dx.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)txtRefreshIntervalMinutes).BeginInit();
            SuspendLayout();
            // 
            // grp3dx
            // 
            grp3dx.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            grp3dx.Controls.Add(label4);
            grp3dx.Controls.Add(txtRefreshIntervalMinutes);
            grp3dx.Controls.Add(label1);
            grp3dx.Controls.Add(txt3dxServerUrl);
            grp3dx.Controls.Add(label2);
            grp3dx.Location = new Point(12, 12);
            grp3dx.Name = "grp3dx";
            grp3dx.Size = new Size(654, 111);
            grp3dx.TabIndex = 1;
            grp3dx.TabStop = false;
            grp3dx.Text = "3DX settings";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(223, 57);
            label4.Name = "label4";
            label4.Size = new Size(50, 15);
            label4.TabIndex = 11;
            label4.Text = "minutes";
            // 
            // txtRefreshIntervalMinutes
            // 
            txtRefreshIntervalMinutes.Location = new Point(176, 55);
            txtRefreshIntervalMinutes.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            txtRefreshIntervalMinutes.Name = "txtRefreshIntervalMinutes";
            txtRefreshIntervalMinutes.Size = new Size(41, 23);
            txtRefreshIntervalMinutes.TabIndex = 10;
            txtRefreshIntervalMinutes.Value = new decimal(new int[] { 30, 0, 0, 0 });
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(14, 57);
            label1.Name = "label1";
            label1.Size = new Size(156, 15);
            label1.TabIndex = 9;
            label1.Text = "Refresh document list every:";
            // 
            // txt3dxServerUrl
            // 
            txt3dxServerUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txt3dxServerUrl.Location = new Point(176, 26);
            txt3dxServerUrl.Name = "txt3dxServerUrl";
            txt3dxServerUrl.Size = new Size(472, 23);
            txt3dxServerUrl.TabIndex = 0;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(104, 29);
            label2.Name = "label2";
            label2.Size = new Size(66, 15);
            label2.TabIndex = 2;
            label2.Text = "Server URL:";
            // 
            // btnStart
            // 
            btnStart.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnStart.Location = new Point(12, 144);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(75, 23);
            btnStart.TabIndex = 0;
            btnStart.Text = "Start";
            btnStart.UseVisualStyleBackColor = true;
            btnStart.Click += BtnStart_Click;
            // 
            // lblRunningStatus
            // 
            lblRunningStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblRunningStatus.AutoSize = true;
            lblRunningStatus.BackColor = SystemColors.Control;
            lblRunningStatus.Location = new Point(93, 148);
            lblRunningStatus.Name = "lblRunningStatus";
            lblRunningStatus.Size = new Size(97, 15);
            lblRunningStatus.TabIndex = 3;
            lblRunningStatus.Text = "lblRunningStatus";
            // 
            // btnOpenVirtualDrive
            // 
            btnOpenVirtualDrive.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnOpenVirtualDrive.Location = new Point(246, 144);
            btnOpenVirtualDrive.Name = "btnOpenVirtualDrive";
            btnOpenVirtualDrive.Size = new Size(205, 23);
            btnOpenVirtualDrive.TabIndex = 4;
            btnOpenVirtualDrive.Text = "Open virtual drive";
            btnOpenVirtualDrive.UseVisualStyleBackColor = true;
            btnOpenVirtualDrive.Visible = false;
            btnOpenVirtualDrive.Click += btnOpenVirtualDrive_Click;
            // 
            // lblVersion
            // 
            lblVersion.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            lblVersion.AutoSize = true;
            lblVersion.Location = new Point(636, 156);
            lblVersion.Name = "lblVersion";
            lblVersion.Size = new Size(30, 15);
            lblVersion.TabIndex = 5;
            lblVersion.Text = "vX.X";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(678, 180);
            Controls.Add(lblVersion);
            Controls.Add(btnOpenVirtualDrive);
            Controls.Add(lblRunningStatus);
            Controls.Add(btnStart);
            Controls.Add(grp3dx);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            Text = "Mount 3DX";
            FormClosed += Form1_FormClosed;
            Load += Form1_Load;
            grp3dx.ResumeLayout(false);
            grp3dx.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)txtRefreshIntervalMinutes).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private GroupBox grp3dx;
        private TextBox txt3dxServerUrl;
        private Label label2;
        private Button btnStart;
        private Label lblRunningStatus;
        private Button btnOpenVirtualDrive;
        private Label label4;
        private NumericUpDown txtRefreshIntervalMinutes;
        private Label label1;
        private Label lblVersion;
    }
}