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
            grp3dx = new GroupBox();
            label5 = new Label();
            label4 = new Label();
            txtKeepAliveIntervalMinutes = new NumericUpDown();
            chkKeepAlive = new CheckBox();
            label3 = new Label();
            txt3dxServerUrl = new TextBox();
            label2 = new Label();
            label1 = new Label();
            txt3dxCookieString = new TextBox();
            grpVfs = new GroupBox();
            txtMapToDriveLetter = new TextBox();
            label7 = new Label();
            btnStart = new Button();
            lblRunningStatus = new Label();
            grp3dx.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)txtKeepAliveIntervalMinutes).BeginInit();
            grpVfs.SuspendLayout();
            SuspendLayout();
            // 
            // grp3dx
            // 
            grp3dx.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            grp3dx.Controls.Add(label5);
            grp3dx.Controls.Add(label4);
            grp3dx.Controls.Add(txtKeepAliveIntervalMinutes);
            grp3dx.Controls.Add(chkKeepAlive);
            grp3dx.Controls.Add(label3);
            grp3dx.Controls.Add(txt3dxServerUrl);
            grp3dx.Controls.Add(label2);
            grp3dx.Controls.Add(label1);
            grp3dx.Controls.Add(txt3dxCookieString);
            grp3dx.Location = new Point(12, 12);
            grp3dx.Name = "grp3dx";
            grp3dx.Size = new Size(654, 129);
            grp3dx.TabIndex = 1;
            grp3dx.TabStop = false;
            grp3dx.Text = "3DX settings";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(302, 87);
            label5.Name = "label5";
            label5.Size = new Size(50, 15);
            label5.TabIndex = 8;
            label5.Text = "minutes";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(184, 87);
            label4.Name = "label4";
            label4.Size = new Size(65, 15);
            label4.TabIndex = 7;
            label4.Text = "Ping every:";
            // 
            // txtKeepAliveIntervalMinutes
            // 
            txtKeepAliveIntervalMinutes.Location = new Point(255, 84);
            txtKeepAliveIntervalMinutes.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            txtKeepAliveIntervalMinutes.Name = "txtKeepAliveIntervalMinutes";
            txtKeepAliveIntervalMinutes.Size = new Size(41, 23);
            txtKeepAliveIntervalMinutes.TabIndex = 3;
            txtKeepAliveIntervalMinutes.Value = new decimal(new int[] { 5, 0, 0, 0 });
            // 
            // chkKeepAlive
            // 
            chkKeepAlive.AutoSize = true;
            chkKeepAlive.Checked = true;
            chkKeepAlive.CheckState = CheckState.Checked;
            chkKeepAlive.Location = new Point(101, 86);
            chkKeepAlive.Name = "chkKeepAlive";
            chkKeepAlive.Size = new Size(68, 19);
            chkKeepAlive.TabIndex = 2;
            chkKeepAlive.Text = "Enabled";
            chkKeepAlive.UseVisualStyleBackColor = true;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(16, 86);
            label3.Name = "label3";
            label3.Size = new Size(65, 15);
            label3.TabIndex = 4;
            label3.Text = "Keep Alive:";
            // 
            // txt3dxServerUrl
            // 
            txt3dxServerUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txt3dxServerUrl.Location = new Point(101, 26);
            txt3dxServerUrl.Name = "txt3dxServerUrl";
            txt3dxServerUrl.Size = new Size(547, 23);
            txt3dxServerUrl.TabIndex = 0;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(15, 29);
            label2.Name = "label2";
            label2.Size = new Size(66, 15);
            label2.TabIndex = 2;
            label2.Text = "Server URL:";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(15, 58);
            label1.Name = "label1";
            label1.Size = new Size(80, 15);
            label1.TabIndex = 1;
            label1.Text = "Cookie string:";
            // 
            // txt3dxCookieString
            // 
            txt3dxCookieString.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txt3dxCookieString.Location = new Point(101, 55);
            txt3dxCookieString.Name = "txt3dxCookieString";
            txt3dxCookieString.Size = new Size(547, 23);
            txt3dxCookieString.TabIndex = 1;
            // 
            // grpVfs
            // 
            grpVfs.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            grpVfs.Controls.Add(txtMapToDriveLetter);
            grpVfs.Controls.Add(label7);
            grpVfs.Location = new Point(12, 157);
            grpVfs.Name = "grpVfs";
            grpVfs.Size = new Size(654, 98);
            grpVfs.TabIndex = 2;
            grpVfs.TabStop = false;
            grpVfs.Text = "Virtual File System";
            // 
            // txtMapToDriveLetter
            // 
            txtMapToDriveLetter.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtMapToDriveLetter.Location = new Point(129, 25);
            txtMapToDriveLetter.Name = "txtMapToDriveLetter";
            txtMapToDriveLetter.Size = new Size(247, 23);
            txtMapToDriveLetter.TabIndex = 5;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(16, 28);
            label7.Name = "label7";
            label7.Size = new Size(107, 15);
            label7.TabIndex = 4;
            label7.Text = "Map to drive letter:";
            // 
            // btnStart
            // 
            btnStart.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnStart.Location = new Point(12, 261);
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
            lblRunningStatus.Location = new Point(93, 265);
            lblRunningStatus.Name = "lblRunningStatus";
            lblRunningStatus.Size = new Size(97, 15);
            lblRunningStatus.TabIndex = 3;
            lblRunningStatus.Text = "lblRunningStatus";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(678, 297);
            Controls.Add(lblRunningStatus);
            Controls.Add(btnStart);
            Controls.Add(grpVfs);
            Controls.Add(grp3dx);
            Name = "Form1";
            Text = "Mount 3DX";
            FormClosed += Form1_FormClosed;
            Load += Form1_Load;
            grp3dx.ResumeLayout(false);
            grp3dx.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)txtKeepAliveIntervalMinutes).EndInit();
            grpVfs.ResumeLayout(false);
            grpVfs.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private GroupBox grp3dx;
        private Label label1;
        private TextBox txt3dxCookieString;
        private TextBox txt3dxServerUrl;
        private Label label2;
        private NumericUpDown txtKeepAliveIntervalMinutes;
        private CheckBox chkKeepAlive;
        private Label label3;
        private Label label5;
        private Label label4;
        private GroupBox grpVfs;
        private TextBox txtMapToDriveLetter;
        private Label label7;
        private Button btnStart;
        private Label lblRunningStatus;
    }
}