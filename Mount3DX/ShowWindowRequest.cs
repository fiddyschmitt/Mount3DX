using System.Runtime.InteropServices;

namespace Mount3DX
{
    //Launched again while it runs, Mount 3DX brings up the running one's window, which may be only in
    //the tray, rather than saying that it is running. The second launch asks through an event named in
    //the user's session, and goes.
    public static partial class ShowWindowRequest
    {
        //Local\: the user's own session. Mount 3DX runs once per machine (its ports are machine-wide),
        //and a copy running for another user can't show its window here.
        const string EventName = @"Local\Mount3DX.ShowWindow";

        //Lets any process bring a window to the front, until the user next clicks or types
        const uint ASFW_ANY = unchecked((uint)-1);

        //The running one makes the event first thing, so that a launch straight after it isn't missed:
        //the event stays set until the window listens. Null if it can't be made, and a second launch
        //then says that Mount 3DX is running, as it used to.
        public static EventWaitHandle? Create()
        {
            try
            {
                return new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
            {
                return null;
            }
        }

        //showWindow is called on a pool thread, once for each launch
        public static RegisteredWaitHandle Listen(EventWaitHandle requests, Action showWindow) =>
            ThreadPool.RegisterWaitForSingleObject(requests, (state, timedOut) => showWindow(), null, Timeout.Infinite, executeOnlyOnce: false);

        //From a second launch. False if the running one can't be asked: it runs for another user, or as
        //administrator, or is a version from before this.
        public static bool Send()
        {
            try
            {
                if (!EventWaitHandle.TryOpenExisting(EventName, out var requests)) return false;

                using (requests)
                {
                    //Launched by the user, this one may bring a window to the front, and passes that on,
                    //so the running one's window comes up in front rather than behind
                    AllowSetForegroundWindow(ASFW_ANY);
                    return requests.Set();
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AllowSetForegroundWindow(uint processId);
    }
}
