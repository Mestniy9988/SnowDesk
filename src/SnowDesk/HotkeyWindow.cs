namespace SnowDesk;

/// <summary>Hidden window that receives Ctrl+Alt+S without activating SnowDesk.</summary>
sealed class HotkeyWindow : NativeWindow, IDisposable
{
    public const string Chord = "Ctrl+Alt+S";
    const int Id = 1;
    const uint ModAlt = 0x0001;
    const uint ModControl = 0x0002;
    const uint ModNoRepeat = 0x4000;
    const int WmHotkey = 0x0312;
    bool _registered;

    public event Action? Pressed;

    public bool Start()
    {
        if (Handle == IntPtr.Zero)
        {
            CreateHandle(new CreateParams
            {
                Caption = "SnowDesk Hotkey",
                X = -32000,
                Y = -32000,
                Width = 0,
                Height = 0,
                Style = unchecked((int)0x80000000),
                ExStyle = 0x00000080 | 0x08000000,
            });
        }

        _registered = NativeMethods.RegisterHotKey(Handle, Id, ModAlt | ModControl | ModNoRepeat, (uint)'S');
        AppLog.Write(_registered ? "hotkey Ctrl+Alt+S registered" : "hotkey Ctrl+Alt+S NOT registered");
        return _registered;
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero && _registered)
            NativeMethods.UnregisterHotKey(Handle, Id);
        if (Handle != IntPtr.Zero)
            DestroyHandle();
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey)
            Pressed?.Invoke();
        base.WndProc(ref m);
    }
}
