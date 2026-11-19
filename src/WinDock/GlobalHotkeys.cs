using System.Runtime.InteropServices;
using WinDock.Core;

namespace WinDock;

internal enum HotkeyAction { AutoHide, Settings }

internal sealed class GlobalHotkeys(Func<int, int, int, bool> register, Action<int> unregister) : IDisposable
{
    internal const int Message = 0x0312;
    private const int NoRepeat = 0x4000;
    private readonly Dictionary<HotkeyAction, (int Id, DockHotkey Key)> registered = [];
    private int nextId;

    internal GlobalHotkeys(nint hwnd) : this(
        (id, modifiers, key) => RegisterHotKey(hwnd, id, modifiers, key),
        id => UnregisterHotKey(hwnd, id)) { }

    internal bool IsRegistered(HotkeyAction action) => registered.ContainsKey(action);

    internal bool TrySet(HotkeyAction action, DockHotkey? key)
    {
        var previous = registered.GetValueOrDefault(action);
        if (previous.Key == key) return true;
        if (key is null)
        {
            unregister(previous.Id);
            registered.Remove(action);
            return true;
        }

        if (!key.IsValid) return false;
        nextId = nextId % 0xBFFF + 1;
        while (registered.Values.Any(binding => binding.Id == nextId))
            nextId = nextId % 0xBFFF + 1;
        if (!register(nextId, key.Modifiers | NoRepeat, key.VirtualKey)) return false;
        registered[action] = (nextId, key);
        if (previous.Key is not null) unregister(previous.Id);
        return true;
    }

    internal bool TryResolve(int id, out HotkeyAction action, out DockHotkey? key)
    {
        foreach (var binding in registered)
        {
            if (binding.Value.Id != id) continue;
            action = binding.Key;
            key = binding.Value.Key;
            return true;
        }
        action = default;
        key = null;
        return false;
    }

    public void Dispose()
    {
        foreach (var binding in registered.Values) unregister(binding.Id);
        registered.Clear();
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, int modifiers, int key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
