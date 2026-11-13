using System.Globalization;

namespace Friday.Infrastructure;

/// <summary>
/// D:\Friday\data\logs altına günlük bir metin dosyası. Hatalar ve çökmeler burada kalır; kullanıcının söyledikleri
/// yazılmaz (onlar isteğe bağlı sohbet geçmişindedir). 14 günden eski dosyalar açılışta silinir.
/// </summary>
public static class FridayLog
{
    private static readonly object Gate = new();
    private static string? _folder;

    public static void Initialize(string folder)
    {
        Directory.CreateDirectory(folder);
        _folder = folder;
        foreach (var old in Directory.EnumerateFiles(folder, "friday-*.log")
                     .Where(file => File.GetLastWriteTime(file) < DateTime.Now.AddDays(-14)))
        {
            try
            {
                File.Delete(old);
            }
            catch (IOException)
            {
                // Açık olan eski dosya bir sonraki açılışta silinir.
            }
        }
    }

    public static void Info(string message) => Write("BİLGİ", message);

    public static void Warn(string message) => Write("UYARI", message);

    public static void Error(string message, Exception exception) => Write("HATA", $"{message}: {exception}");

    private static void Write(string level, string message)
    {
        if (_folder is null)
        {
            return;
        }

        var now = DateTime.Now;
        var line = string.Create(CultureInfo.InvariantCulture, $"{now:HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path.Combine(_folder, $"friday-{now:yyyy-MM-dd}.log"), line);
            }
            catch (IOException)
            {
                // Günlük yazılamıyorsa Friday çalışmaya devam eder.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
