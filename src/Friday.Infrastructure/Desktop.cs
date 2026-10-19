using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace Friday.Infrastructure;

public sealed record OpenWindow(IntPtr Handle, int ProcessId, string Process, string Title);

public sealed record BrowserTab(string Browser, string Title, bool Selected, IntPtr Window);

/// <summary>
/// Masaüstünün yapısını Windows'un erişilebilirlik altyapısıyla (UI Automation) okur ve kullanır: pencereler,
/// tarayıcı sekmeleri, düğmeler, yazı alanları. Ekran görüntüsündeki pikselleri tahmin etmek yerine gerçek veriyle
/// çalışır. Friday'in kendi pencereleri her zaman dışarıda tutulur.
/// </summary>
public static partial class Desktop
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "opera", "vivaldi", "firefox"
    };

    private static readonly int OwnProcess = Environment.ProcessId;

    /// <summary>Görev çubuğunda görünen pencereler, üstteki önce.</summary>
    public static unsafe IReadOnlyList<OpenWindow> Windows()
    {
        var handles = new List<IntPtr>();
        var state = GCHandle.Alloc(handles);
        try
        {
            _ = NativeMethods.EnumWindows(&Collect, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        var windows = new List<OpenWindow>();
        foreach (var handle in handles)
        {
            if (IsAppWindow(handle) && WindowProcess(handle) is { } process && process.Id != OwnProcess)
            {
                windows.Add(new OpenWindow(handle, process.Id, process.Name, Title(handle)));
            }
        }

        return windows;
    }

    [UnmanagedCallersOnly]
    private static int Collect(IntPtr handle, IntPtr state)
    {
        ((List<IntPtr>)GCHandle.FromIntPtr(state).Target!).Add(handle);
        return 1;
    }

    /// <summary>Kullanıcının o an çalıştığı pencere; Friday öndeyse ondan sonraki pencere.</summary>
    public static OpenWindow? Foreground()
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle != IntPtr.Zero && WindowProcess(handle) is { } process && process.Id != OwnProcess && IsAppWindow(handle))
        {
            return new OpenWindow(handle, process.Id, process.Name, Title(handle));
        }

        var windows = Windows();
        return windows.Count > 0 ? windows[0] : null;
    }

    public static IReadOnlyList<BrowserTab> Tabs()
    {
        var tabs = new List<BrowserTab>();
        foreach (var window in Windows().Where(window => Browsers.Contains(window.Process)))
        {
            var root = AutomationElement.FromHandle(window.Handle);
            foreach (AutomationElement tab in root.FindAll(TreeScope.Descendants, Is(ControlType.TabItem)))
            {
                var selected = tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern)
                               && ((SelectionItemPattern)pattern).Current.IsSelected;
                tabs.Add(new BrowserTab(window.Process, CleanTitle(tab.Current.Name), selected, window.Handle));
            }
        }

        return tabs;
    }

    /// <summary>Bir penceredeki görünür yazıları toplar (belge, metin, liste, bağlantı, düğme adları).</summary>
    public static string ReadText(OpenWindow window, int maxCharacters = 5000)
    {
        var root = AutomationElement.FromHandle(window.Handle);
        var text = new StringBuilder();

        // Tarayıcı ve belge uygulamalarında tüm içerik tek bir belge öğesinde durur.
        foreach (AutomationElement document in root.FindAll(TreeScope.Descendants, Is(ControlType.Document)))
        {
            if (document.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
            {
                text.AppendLine(((TextPattern)pattern).DocumentRange.GetText(maxCharacters));
            }

            if (text.Length >= maxCharacters)
            {
                break;
            }
        }

        if (text.Length < 200)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var condition = new OrCondition(Is(ControlType.Text), Is(ControlType.Edit), Is(ControlType.ListItem),
                Is(ControlType.Hyperlink), Is(ControlType.DataItem), Is(ControlType.Button), Is(ControlType.MenuItem));
            foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, condition))
            {
                var value = element.Current.Name;
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern))
                {
                    var current = ((ValuePattern)valuePattern).Current.Value;
                    value = string.IsNullOrWhiteSpace(current) ? value : $"{value}: {current}";
                }

                if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                {
                    text.AppendLine(value.Trim());
                }

                if (text.Length >= maxCharacters)
                {
                    break;
                }
            }
        }

        var result = text.ToString().Trim();
        return result.Length > maxCharacters ? result[..maxCharacters] : result;
    }

    /// <summary>Tarayıcı penceresinin adres çubuğundaki adres.</summary>
    public static string? BrowserAddress(OpenWindow window)
    {
        var root = AutomationElement.FromHandle(window.Handle);
        foreach (AutomationElement edit in root.FindAll(TreeScope.Descendants, Is(ControlType.Edit)))
        {
            var name = edit.Current.Name;
            if ((name.Contains("adres", StringComparison.OrdinalIgnoreCase) || name.Contains("address", StringComparison.OrdinalIgnoreCase)
                 || name.Contains("URL", StringComparison.OrdinalIgnoreCase))
                && edit.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            {
                var value = ((ValuePattern)pattern).Current.Value.Trim();
                if (value.Length > 0)
                {
                    return value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;
                }
            }
        }

        return null;
    }

    public static OpenWindow? FindWindow(string query)
    {
        var wanted = AssistantTools.Simplify(query);
        if (wanted.Length == 0)
        {
            return Foreground();
        }

        return Windows()
            .Select(window => (window, score: Score(wanted, window)))
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .Select(item => item.window)
            .FirstOrDefault();
    }

    public static BrowserTab? FindTab(string query)
    {
        var wanted = AssistantTools.Simplify(query);
        return Tabs()
            .Where(tab => AssistantTools.Simplify(tab.Title).Contains(wanted, StringComparison.Ordinal))
            .OrderBy(tab => tab.Title.Length)
            .FirstOrDefault();
    }

    public static bool SelectTab(BrowserTab tab)
    {
        BringToFront(tab.Window);
        var element = FindTabElement(tab);
        if (element is null || !element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
        {
            return false;
        }

        ((SelectionItemPattern)pattern).Select();
        return true;
    }

    public static bool CloseTab(BrowserTab tab)
    {
        if (!SelectTab(tab))
        {
            return false;
        }

        // Sekmenin kendi kapatma düğmesi varsa ona basılır; yoksa seçili sekme Ctrl+W ile kapatılır.
        var element = FindTabElement(tab);
        var close = element?.FindFirst(TreeScope.Descendants, Is(ControlType.Button));
        if (close is not null && close.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
            return true;
        }

        if (NativeMethods.GetForegroundWindow() != tab.Window)
        {
            return false;
        }

        Keys.Chord(Keys.Control, (byte)'W');
        return true;
    }

    public static bool BringToFront(IntPtr window)
    {
        // Arka plandaki bir süreç pencereyi öne alabilsin diye Alt kısaca basılır (Windows kuralı).
        Keys.Tap(Keys.Alt);
        if (NativeMethods.IsIconic(window))
        {
            NativeMethods.ShowWindow(window, 9);
        }

        NativeMethods.SetForegroundWindow(window);
        Thread.Sleep(150);
        return NativeMethods.GetForegroundWindow() == window;
    }

    /// <summary>Penceredeki adı eşleşen düğmeye, bağlantıya ya da menü öğesine basar.</summary>
    public static string? Click(OpenWindow window, string name)
    {
        var wanted = AssistantTools.Simplify(name);
        var root = AutomationElement.FromHandle(window.Handle);
        var condition = new OrCondition(Is(ControlType.Button), Is(ControlType.Hyperlink), Is(ControlType.MenuItem),
            Is(ControlType.ListItem), Is(ControlType.TabItem), Is(ControlType.CheckBox));
        var target = root.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>()
            .Where(element => AssistantTools.Simplify(element.Current.Name).Contains(wanted, StringComparison.Ordinal))
            .OrderBy(element => element.Current.Name.Length)
            .FirstOrDefault();
        if (target is null)
        {
            return null;
        }

        BringToFront(window.Handle);
        if (target.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
        }
        else if (target.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
        {
            ((SelectionItemPattern)select).Select();
        }
        else if (target.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
        {
            ((TogglePattern)toggle).Toggle();
        }
        else
        {
            return null;
        }

        return target.Current.Name;
    }

    /// <summary>Öndeki penceredeki imleç konumuna metni yazar.</summary>
    public static bool Type(OpenWindow window, string text)
    {
        if (!BringToFront(window.Handle))
        {
            return false;
        }

        Keys.Text(text);
        return true;
    }

    private static AutomationElement? FindTabElement(BrowserTab tab) =>
        AutomationElement.FromHandle(tab.Window)
            .FindAll(TreeScope.Descendants, Is(ControlType.TabItem)).Cast<AutomationElement>()
            .FirstOrDefault(element => CleanTitle(element.Current.Name) == tab.Title);

    /// <summary>Chrome sekme adına bellek bilgisini ekliyor ("… - Memory usage - 403 MB"); okunurken atılır.</summary>
    internal static string CleanTitle(string title) => MemorySuffix().Replace(title, "").Trim();

    [System.Text.RegularExpressions.GeneratedRegex(@"\s+-\s+(Memory usage|Bellek kullanımı)\s+-\s+[\d.,]+\s*[KMG]B\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex MemorySuffix();

    private static int Score(string wanted, OpenWindow window)
    {
        var process = AssistantTools.Simplify(window.Process);
        var title = AssistantTools.Simplify(window.Title);
        if (process == wanted)
        {
            return 3;
        }

        if (process.Contains(wanted, StringComparison.Ordinal) || wanted.Contains(process, StringComparison.Ordinal) && process.Length > 2)
        {
            return 2;
        }

        return title.Contains(wanted, StringComparison.Ordinal) ? 1 : 0;
    }

    private static PropertyCondition Is(ControlType type) => new(AutomationElement.ControlTypeProperty, type);

    private static bool IsAppWindow(IntPtr handle)
    {
        const int extendedStyle = -20;
        const long toolWindow = 0x80;
        const int cloaked = 14;
        if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.GetWindow(handle, 4) != IntPtr.Zero)
        {
            return false;
        }

        if ((NativeMethods.GetWindowLongPtrW(handle, extendedStyle) & toolWindow) != 0)
        {
            return false;
        }

        // Arka planda gizlenmiş (cloaked) Windows 11 pencereleri görünmez sayılır.
        NativeMethods.DwmGetWindowAttribute(handle, cloaked, out var isCloaked, sizeof(int));
        return isCloaked == 0 && Title(handle).Length > 0;
    }

    private static unsafe string Title(IntPtr handle)
    {
        var buffer = stackalloc char[512];
        var length = NativeMethods.GetWindowTextW(handle, buffer, 512);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    private static (int Id, string Name)? WindowProcess(IntPtr handle)
    {
        NativeMethods.GetWindowThreadProcessId(handle, out var id);
        try
        {
            using var process = Process.GetProcessById((int)id);
            return ((int)id, process.ProcessName);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static unsafe partial int EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> callback, IntPtr parameter);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsWindowVisible(IntPtr handle);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsIconic(IntPtr handle);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetWindow(IntPtr handle, uint command);

        [LibraryImport("user32.dll")]
        internal static partial long GetWindowLongPtrW(IntPtr handle, int index);

        [LibraryImport("user32.dll")]
        internal static unsafe partial int GetWindowTextW(IntPtr handle, char* text, int maxCount);

        [LibraryImport("user32.dll")]
        internal static partial uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetForegroundWindow(IntPtr handle);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ShowWindow(IntPtr handle, int command);

        [LibraryImport("dwmapi.dll")]
        internal static partial int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int size);
    }
}
