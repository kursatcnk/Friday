using System.IO;
using Microsoft.Win32;

namespace Friday.Desktop;

/// <summary>
/// "Windows açılınca başlat": yalnızca kullanıcının kendi kayıt defteri dalına (HKCU\…\Run) tek bir satır yazılır;
/// yönetici izni gerekmez, sürücüye dosya kopyalanmaz. Friday bu satırla tepside sessizce açılır.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Friday";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string;
        }
    }

    /// <returns>İstenen duruma geçilebildiyse true.</returns>
    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                key.SetValue(Name, $"\"{Environment.ProcessPath}\" --tray");
            }
            else if (key.GetValue(Name) is not null)
            {
                key.DeleteValue(Name);
            }

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Friday.Infrastructure.FridayLog.Error("Başlangıç kaydı değiştirilemedi", exception);
            return false;
        }
    }
}
