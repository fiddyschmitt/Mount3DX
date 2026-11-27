using Avalonia.Controls;

namespace Mount3DX
{
    //Windows' values, which also pick the sound (MessageBeep) that Windows' message box makes
    public enum MessageBoxIcon : uint
    {
        Warning = 0x30,     //MB_ICONWARNING
        Information = 0x40, //MB_ICONINFORMATION
    }

    //A message with an OK button, in the app's theme (MessageBoxWindow)
    public static class MessageBox
    {
        //Modal to the owner, if there is one; otherwise a window of its own, in the taskbar. On the UI
        //thread. It returns straight away, while the message shows.
        public static void Show(string text, MessageBoxIcon icon, Window? owner = null)
        {
            var window = new MessageBoxWindow(text, icon);

            if (owner != null)
            {
                window.ShowInTaskbar = false;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _ = window.ShowDialog(owner);
            }
            else
            {
                window.Show();
            }
        }
    }
}
