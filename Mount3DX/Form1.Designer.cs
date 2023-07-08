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
            label6 = new Label();
            txtQueryThreads = new NumericUpDown();
            label4 = new Label();
            txtRefreshIntervalMinutes = new NumericUpDown();
            label1 = new Label();
            label5 = new Label();
            txtKeepAliveIntervalMinutes = new NumericUpDown();
            label3 = new Label();
            txt3dxServerUrl = new TextBox();
            label2 = new Label();
            btnStart = new Button();
            lblRunningStatus = new Label();
            btnOpenVirtualDrive = new Button();
            grp3dx.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)txtQueryThreads).BeginInit();
            ((System.ComponentModel.ISupportInitialize)txtRefreshIntervalMinutes).BeginInit();
            ((System.ComponentModel.ISupportInitialize)txtKeepAliveIntervalMinutes).BeginInit();
            SuspendLayout();
            // 
            // grp3dx
            // 
            grp3dx.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            grp3dx.Controls.Add(label6);
            grp3dx.Controls.Add(txtQueryThreads);
            grp3dx.Controls.Add(label4);
            grp3dx.Controls.Add(txtRefreshIntervalMinutes);
            grp3dx.Controls.Add(label1);
            grp3dx.Controls.Add(label5);
            grp3dx.Controls.Add(txtKeepAliveIntervalMinutes);
            grp3dx.Controls.Add(label3);
            grp3dx.Controls.Add(txt3dxServerUrl);
            grp3dx.Controls.Add(label2);
            grp3dx.Location = new Point(12, 12);
            grp3dx.Name = "grp3dx";
            grp3dx.Size = new Size(654, 156);
            grp3dx.TabIndex = 1;
            grp3dx.TabStop = false;
            grp3dx.Text = "3DX settings";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(86, 86);
            label6.Name = "label6";
            label6.Size = new Size(84, 15);
            label6.TabIndex = 13;
            label6.Text = "Query threads:";
            // 
            // txtQueryThreads
            // 
            txtQueryThreads.Location = new Point(176, 84);
            txtQueryThreads.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            txtQueryThreads.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            txtQueryThreads.Name = "txtQueryThreads";
            txtQueryThreads.Size = new Size(41, 23);
            txtQueryThreads.TabIndex = 12;
            txtQueryThreads.Value = new decimal(new int[] { 16, 0, 0, 0 });
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
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(223, 115);
            label5.Name = "label5";
            label5.Size = new Size(50, 15);
            label5.TabIndex = 8;
            label5.Text = "minutes";
            // 
            // txtKeepAliveIntervalMinutes
            // 
            txtKeepAliveIntervalMinutes.Location = new Point(176, 113);
            txtKeepAliveIntervalMinutes.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            txtKeepAliveIntervalMinutes.Name = "txtKeepAliveIntervalMinutes";
            txtKeepAliveIntervalMinutes.Size = new Size(41, 23);
            txtKeepAliveIntervalMinutes.TabIndex = 3;
            txtKeepAliveIntervalMinutes.Value = new decimal(new int[] { 5, 0, 0, 0 });
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(105, 115);
            label3.Name = "label3";
            label3.Size = new Size(65, 15);
            label3.TabIndex = 4;
            label3.Text = "Keep Alive:";
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
            btnStart.Location = new Point(12, 187);
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
            lblRunningStatus.Location = new Point(93, 191);
            lblRunningStatus.Name = "lblRunningStatus";
            lblRunningStatus.Size = new Size(97, 15);
            lblRunningStatus.TabIndex = 3;
            lblRunningStatus.Text = "lblRunningStatus";
            // 
            // btnOpenVirtualDrive
            // 
            btnOpenVirtualDrive.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnOpenVirtualDrive.Location = new Point(246, 187);
            btnOpenVirtualDrive.Name = "btnOpenVirtualDrive";
            btnOpenVirtualDrive.Size = new Size(205, 23);
            btnOpenVirtualDrive.TabIndex = 4;
            btnOpenVirtualDrive.Text = "Open virtual drive";
            btnOpenVirtualDrive.UseVisualStyleBackColor = true;
            btnOpenVirtualDrive.Visible = false;
            btnOpenVirtualDrive.Click += btnOpenVirtualDrive_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(678, 223);
            Controls.Add(btnOpenVirtualDrive);
            Controls.Add(lblRunningStatus);
            Controls.Add(btnStart);
            Controls.Add(grp3dx);
            Name = "Form1";
            Text = "Mount 3DX";
            FormClosed += Form1_FormClosed;
            Load += Form1_Load;
            grp3dx.ResumeLayout(false);
            grp3dx.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)txtQueryThreads).EndInit();
            ((System.ComponentModel.ISupportInitialize)txtRefreshIntervalMinutes).EndInit();
            ((System.ComponentModel.ISupportInitialize)txtKeepAliveIntervalMinutes).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private GroupBox grp3dx;
        private TextBox txt3dxServerUrl;
        private Label label2;
        private NumericUpDown txtKeepAliveIntervalMinutes;
        private Label label3;
        private Label label5;
        private Button btnStart;
        private Label lblRunningStatus;
        private Button btnOpenVirtualDrive;
        private NumericUpDown txtQueryThreads;
        private Label label4;
        private NumericUpDown txtRefreshIntervalMinutes;
        private Label label1;
        private Label label6;
    }
}