using WinForms = System.Windows.Forms;

namespace BuzzDesk;

/// <summary>Hidden window that receives WM_HOTKEY.</summary>
sealed class HotkeyWindow : WinForms.NativeWindow, IDisposable
{
    readonly HashSet<int> ids = [];   // ids registered right now
    public HotkeyWindow() { CreateHandle(new WinForms.CreateParams()); }

    public bool Register(int id, string spec)
    {
        Unregister(id);
        if (!Parse(spec, out uint mods, out WinForms.Keys key)) return false;
        bool ok = Native.RegisterHotKey(Handle, id, mods | Native.MOD_NOREPEAT, (uint)key);
        Log(id, spec, ok, ok ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        if (!ok) return false;
        ids.Add(id);
        return true;
    }

    public void Unregister(int id) { Native.UnregisterHotKey(Handle, id); ids.Remove(id); }

    // Last results in LocalAppData\<app>\hotkeys.log (support: "why doesn't Print Screen work?"). 1409 = taken by another app.
    static readonly Dictionary<string, bool> lastLogged = [];
    static void Log(int id, string spec, bool ok, int error)
    {
        string k = id + "|" + spec;
        if (lastLogged.TryGetValue(k, out bool prev) && prev == ok) return;   // only changes, not every retry
        lastLogged[k] = ok;
        try { System.IO.File.AppendAllText(DataFolder.File("hotkeys.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + spec + "  " + (ok ? "OK" : "FAIL " + error) + Environment.NewLine); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
    }
    public bool IsRegistered(int id) => ids.Contains(id);

    public static bool Parse(string spec, out uint mods, out WinForms.Keys key)
    {
        mods = 0; key = WinForms.Keys.None;
        if (string.IsNullOrEmpty(spec)) return false;
        foreach (var raw in spec.Split('+'))
        {
            var p = raw.Trim();
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                case "alt": mods |= Native.MOD_ALT; break;
                case "shift": mods |= Native.MOD_SHIFT; break;
                case "win": mods |= Native.MOD_WIN; break;
                default: if (!Enum.TryParse(p, true, out key)) return false; break;
            }
        }
        return key != WinForms.Keys.None;
    }

    protected override void WndProc(ref WinForms.Message m)
    {
        if (m.Msg == Native.WM_HOTKEY) App.Current.OnHotkey(m.WParam.ToInt32());
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in ids) Native.UnregisterHotKey(Handle, id);
        DestroyHandle();
    }
}
