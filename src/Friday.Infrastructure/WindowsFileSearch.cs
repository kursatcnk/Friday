using System.Data.OleDb;
using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

public sealed record FoundFile(string Path, string Name, DateTime Modified, long Size);

/// <summary>Windows Arama dizinini sorgular; dosyalar Friday tarafından ayrıca taranmaz ya da okunmaz.</summary>
public static class WindowsFileSearch
{
    private const string Connection = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows';";

    public static IReadOnlyList<FoundFile> Find(string text, string kind, int days, string folder)
    {
        var sql = FileSearchQuery.Build(text, kind, days, ResolveFolder(folder), DateTime.Now);
        var results = new List<FoundFile>();
        using var connection = new OleDbConnection(Connection);
        connection.Open();
#pragma warning disable CA2100 // Sorgu FileSearchQuery'de kaçışlanmış ve süzülmüş girdilerle kurulur.
        using var command = new OleDbCommand(sql, connection);
#pragma warning restore CA2100
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new FoundFile(
                reader.GetString(0),
                reader.IsDBNull(1) ? Path.GetFileName(reader.GetString(0)) : reader.GetString(1),
                reader.IsDBNull(2) ? DateTime.MinValue : Convert.ToDateTime(reader.GetValue(2), CultureInfo.InvariantCulture).ToLocalTime(),
                reader.IsDBNull(3) ? 0 : Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture)));
        }

        return results;
    }

    /// <summary>"indirilenler", "belgeler" gibi sesli klasör adlarını gerçek yollara çevirir.</summary>
    public static string ResolveFolder(string folder)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return AssistantTools.Simplify(folder) switch
        {
            "" or "hepsi" or "tum" or "tumu" => "",
            "indirilenler" or "indirdiklerim" or "downloads" => Path.Combine(profile, "Downloads"),
            "belgeler" or "belgelerim" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "masaustu" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "resimler" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "videolar" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "muzik" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            _ => Directory.Exists(folder) ? folder : ""
        };
    }
}
