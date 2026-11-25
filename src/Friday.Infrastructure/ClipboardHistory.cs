using System.Runtime.InteropServices;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Pano geçmişi (isteğe bağlı, varsayılan kapalı): kopyalanan son 20 metin yalnızca bellekte tutulur, diske yazılmaz,
/// Friday kapanınca silinir. Saniyede bir yalnızca Windows'un "pano değişti mi" sayacına bakılır; değişmediyse panoya
/// dokunulmaz.
/// </summary>
public sealed partial class ClipboardHistory : IDisposable
{
    private const int MaxItems = 20;
    private const int MaxCharacters = 4000;

    private readonly object _gate = new();
    private readonly List<string> _items = [];
    private Timer? _timer;
    private uint _sequence;

    public bool IsRunning => _timer is not null;

    /// <summary>En yeniden eskiye.</summary>
    public IReadOnlyList<string> Items
    {
        get
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }

    public void Enable(bool enabled)
    {
        lock (_gate)
        {
            if (enabled && _timer is null)
            {
                _sequence = NativeMethods.GetClipboardSequenceNumber();
                _timer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
            else if (!enabled)
            {
                _timer?.Dispose();
                _timer = null;
                _items.Clear();
            }
        }
    }

    /// <summary>Yeni bir metin geldiyse başa ekler; aynısı varsa başa taşır.</summary>
    public void Add(string text)
    {
        var clean = text.Length > MaxCharacters ? text[..MaxCharacters] : text;
        if (clean.Trim().Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            _items.Remove(clean);
            _items.Insert(0, clean);
            if (_items.Count > MaxItems)
            {
                _items.RemoveAt(_items.Count - 1);
            }
        }
    }

    private void Poll()
    {
        var sequence = NativeMethods.GetClipboardSequenceNumber();
        if (sequence == _sequence)
        {
            return;
        }

        _sequence = sequence;
        // Parola yöneticileri kopyaladıkları parolayı "geçmişe alma" diye işaretler; Windows'un kendi pano geçmişi de bunlara
        // uyar. Eskiden Friday bunları da saklıyordu; sonra "panodaki 2. metin" ile modele ya da telefona gidebiliyordu.
        if (Private())
        {
            return;
        }

        var text = ClipboardText.Get();
        if (!PrivacyShield.LooksLikeCredential(text) && PrivacyShield.Secret(text) is null)
        {
            Add(text);
        }
    }

    /// <summary>Panodaki içerik geçmişe alınmamak üzere işaretlenmiş mi (parola yöneticileri, gizli pencereler)?</summary>
    private static bool Private() =>
        PrivateFormats.Value.Any(format => format != 0 && NativeMethods.IsClipboardFormatAvailable(format));

    private static readonly Lazy<uint[]> PrivateFormats = new(() =>
    [
        NativeMethods.RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing"),
        NativeMethods.RegisterClipboardFormatW("CanIncludeInClipboardHistory"),
        NativeMethods.RegisterClipboardFormatW("Clipboard Viewer Ignore"),
    ]);

    public void Dispose() => Enable(false);

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial uint GetClipboardSequenceNumber();

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial uint RegisterClipboardFormatW(string name);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsClipboardFormatAvailable(uint format);
    }
}
