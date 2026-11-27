using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;

namespace Mount3DX
{
    //A message with an OK button, laid out like Windows' message box but drawn in the app's theme,
    //which follows Windows' light or dark mode (Windows' own message box stays light)
    public sealed partial class MessageBoxWindow : Window
    {
        //The icon's size in Windows' message box
        const int IconSize = 32;

        const int IDI_WARNING = 32515;
        const int IDI_INFORMATION = 32516;
        const uint DIB_RGB_COLORS = 0;

        readonly MessageBoxIcon icon;

        //The scale the icon was drawn for
        double iconScaling;

        //For the XAML previewer
        public MessageBoxWindow() : this("", MessageBoxIcon.Information)
        {
        }

        public MessageBoxWindow(string text, MessageBoxIcon icon)
        {
            InitializeComponent();

            this.icon = icon;
            lblMessage.Text = text;

            //for the scale of the screen it is on, and again on a screen of another scale
            ShowIcon();
            ScalingChanged += (sender, e) => ShowIcon();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            //placed by now, maybe on a screen of another scale than the one it was made on
            ShowIcon();

            btnOk.Focus();

            //the sound Windows' message box makes with this icon
            MessageBeep((uint)icon);
        }

        void BtnOk_Click(object? sender, RoutedEventArgs e) => Close();

        void ShowIcon()
        {
            var scaling = RenderScaling;
            if (scaling == iconScaling) return;
            iconScaling = scaling;

            var id = icon == MessageBoxIcon.Warning ? IDI_WARNING : IDI_INFORMATION;
            imgIcon.Source = LoadSystemIcon(id, (int)Math.Round(IconSize * scaling), scaling);
        }

        //Windows' own icon, which Windows draws at the size asked for (sharper than one scaled here).
        //Its pixels carry their transparency, as Windows' icons have since Windows XP.
        static unsafe Bitmap? LoadSystemIcon(int id, int size, double scaling)
        {
            if (LoadIconWithScaleDown(IntPtr.Zero, id, size, size, out var handle) != 0) return null;

            try
            {
                if (!GetIconInfo(handle, out var info)) return null;

                try
                {
                    var header = new BITMAPINFOHEADER
                    {
                        Size = (uint)sizeof(BITMAPINFOHEADER),
                        Width = size,
                        Height = -size, //the top row first
                        Planes = 1,
                        BitCount = 32,
                    };

                    var pixels = new byte[size * size * 4];
                    var screen = GetDC(IntPtr.Zero);
                    int rows;
                    fixed (byte* bits = pixels)
                    {
                        rows = GetDIBits(screen, info.Color, 0, (uint)size, bits, &header, DIB_RGB_COLORS);
                    }
                    ReleaseDC(IntPtr.Zero, screen);

                    if (rows != size) return null;

                    fixed (byte* bits = pixels)
                    {
                        return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, (IntPtr)bits, new PixelSize(size, size), new Vector(96 * scaling, 96 * scaling), size * 4);
                    }
                }
                finally
                {
                    DeleteObject(info.Color);
                    DeleteObject(info.Mask);
                }
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ICONINFO
        {
            public int IsIcon;
            public uint HotspotX, HotspotY;
            public IntPtr Mask, Color;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, SizeImage;
            public int XPelsPerMeter, YPelsPerMeter;
            public uint ClrUsed, ClrImportant;
        }

        //in common controls 6, which the app manifest asks for
        [LibraryImport("comctl32.dll")]
        private static partial int LoadIconWithScaleDown(IntPtr instance, IntPtr name, int width, int height, out IntPtr icon);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetIconInfo(IntPtr icon, out ICONINFO info);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DestroyIcon(IntPtr icon);

        [LibraryImport("gdi32.dll")]
        private static unsafe partial int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte* bits, BITMAPINFOHEADER* info, uint usage);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteObject(IntPtr gdiObject);

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetDC(IntPtr hwnd);

        [LibraryImport("user32.dll")]
        private static partial int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool MessageBeep(uint type);
    }
}
