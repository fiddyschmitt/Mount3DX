namespace Mount3DX
{
    //The small window that offers to open the document whose 3DX link was just copied. It shows the
    //document's whole name, sized to fit it, at the bottom right of the primary screen, above the
    //taskbar. It never takes the focus: the link was probably copied to be pasted somewhere, so the
    //window it was copied in stays active, even when a button here is clicked. It hides when a button
    //is clicked, or after a few seconds; the countdown pauses while the mouse is over it.
    public sealed class LinkPopup : Form
    {
        static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

        //Once the mouse has been over it, it stays at least this long after the mouse leaves
        static readonly TimeSpan LingerAfterHover = TimeSpan.FromSeconds(2);

        //The layout, in pixels at 100% scaling
        const int EdgeSpace = 10;
        const int Spacing = 8;
        const int ButtonWidth = 136;
        const int ButtonHeight = 27;
        const int ScreenMargin = 12;

        readonly Label lblName;
        readonly PopupButton btnOpen;
        readonly PopupButton btnOpenFolder;
        readonly Font nameFont;
        readonly System.Windows.Forms.Timer countdown;

        TimeSpan remaining;
        long lastTick;
        bool hovered;

        //Raised with the document's id, so that it is found again wherever it is by then
        public event Action<string>? OpenRequested;
        public event Action<string>? OpenFolderRequested;

        public string? ObjectId { get; private set; }

        public LinkPopup()
        {
            lblName = new Label();
            btnOpen = new PopupButton();
            btnOpenFolder = new PopupButton();
            countdown = new System.Windows.Forms.Timer { Interval = 100 };
            SuspendLayout();

            nameFont = new Font(Font, FontStyle.Bold);
            lblName.Font = nameFont;
            lblName.UseMnemonic = false;

            btnOpen.Text = "Open";
            btnOpen.Click += (sender, e) => OnButtonClicked(OpenRequested);

            btnOpenFolder.Text = "Open folder";
            btnOpenFolder.Click += (sender, e) => OnButtonClicked(OpenFolderRequested);

            BackColor = SystemColors.Window;
            Controls.Add(lblName);
            Controls.Add(btnOpen);
            Controls.Add(btnOpenFolder);
            FormBorderStyle = FormBorderStyle.None;
            ResizeRedraw = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = Form1.PROGRAM_NAME;
            ResumeLayout(false);

            countdown.Tick += (sender, e) => CountDown();
        }

        //Shows the document, or replaces the one showing, and starts the countdown again
        public void ShowFor(LinkedDocument document)
        {
            ObjectId = document.ObjectId;
            lblName.Text = document.Name;
            FitAndPlace();

            remaining = Lifetime;
            hovered = false;
            lastTick = Environment.TickCount64;
            countdown.Start();

            if (!Visible)
            {
                Show();
            }
        }

        public void Dismiss()
        {
            countdown.Stop();
            ObjectId = null;
            Hide();
        }

        //Sizes the window to the whole name: on one line where the screen is wide enough, and over as
        //many lines as it takes where it isn't. The buttons keep their size in the bottom right
        //corner, so they are in the same place on the screen whatever the name. Then the window goes
        //in the bottom right corner of the primary screen's working area, which leaves out the
        //taskbar (that screen has the notification area).
        void FitAndPlace()
        {
            var area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
            var edge = LogicalToDeviceUnits(EdgeSpace);
            var spacing = LogicalToDeviceUnits(Spacing);
            var button = new Size(LogicalToDeviceUnits(ButtonWidth), LogicalToDeviceUnits(ButtonHeight));
            var screenMargin = LogicalToDeviceUnits(ScreenMargin);

            var buttonsWidth = 2 * button.Width + spacing;
            var widestName = Math.Max(buttonsWidth, area.Width - 2 * screenMargin - 2 * edge);

            var name = lblName.GetPreferredSize(Size.Empty);
            if (name.Width > widestName)
            {
                name = lblName.GetPreferredSize(new Size(widestName, 0));
            }

            var contentWidth = Math.Max(Math.Min(name.Width, widestName), buttonsWidth);
            var width = contentWidth + 2 * edge;
            var buttonsTop = edge + name.Height + spacing;
            var height = buttonsTop + button.Height + edge;

            lblName.SetBounds(edge, edge, contentWidth, name.Height);
            btnOpenFolder.SetBounds(width - edge - button.Width, buttonsTop, button.Width, button.Height);
            btnOpen.SetBounds(btnOpenFolder.Left - spacing - button.Width, buttonsTop, button.Width, button.Height);

            SetBounds(area.Right - width - screenMargin, area.Bottom - height - screenMargin, width, height);
        }

        void OnButtonClicked(Action<string>? handler)
        {
            var objectId = ObjectId;
            Dismiss();

            if (objectId != null)
            {
                handler?.Invoke(objectId);
            }
        }

        void CountDown()
        {
            var now = Environment.TickCount64;
            var elapsed = TimeSpan.FromMilliseconds(now - lastTick);
            lastTick = now;

            if (Bounds.Contains(Cursor.Position))
            {
                hovered = true;
                return;
            }

            if (hovered)
            {
                hovered = false;
                if (remaining < LingerAfterHover)
                {
                    remaining = LingerAfterHover;
                }
                return;
            }

            remaining -= elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                Dismiss();
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            //in case creating the window changed its scaling
            FitAndPlace();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOPMOST = 0x00000008;
                const int WS_EX_TOOLWINDOW = 0x00000080;   //not in Alt+Tab
                const int WS_EX_NOACTIVATE = 0x08000000;   //clicks don't activate it
                const int CS_DROPSHADOW = 0x00020000;

                var createParams = base.CreateParams;
                createParams.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                createParams.ClassStyle |= CS_DROPSHADOW;
                return createParams;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x0021;
            const int MA_NOACTIVATE = 3;

            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = MA_NOACTIVATE;
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ControlPaint.DrawBorder(e.Graphics, ClientRectangle, SystemColors.ControlDark, ButtonBorderStyle.Solid);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                countdown.Dispose();
                nameFont.Dispose();
            }

            base.Dispose(disposing);
        }

        //A button that can't take the focus, which would activate the popup
        sealed class PopupButton : Button
        {
            public PopupButton()
            {
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
            }
        }
    }
}
