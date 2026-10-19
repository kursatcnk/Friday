namespace Friday.Core;

public enum RoutineAction
{
    App,
    Website,
    Search,
    Media,
    Say,
    Briefing,
    LinguaFlow
}

/// <summary>Rutin adımı: settings.json'da "uygulama:Spotify", "site:github.com", "soyle:Hazırım" gibi yazılır.</summary>
public sealed record RoutineStep(RoutineAction Action, string Value)
{
    public static RoutineStep Parse(string text)
    {
        var separator = text.IndexOf(':', StringComparison.Ordinal);
        var kind = (separator < 0 ? text : text[..separator]).Trim().ToLowerInvariant();
        var value = separator < 0 ? "" : text[(separator + 1)..].Trim();
        var action = kind switch
        {
            "uygulama" or "app" => RoutineAction.App,
            "site" => RoutineAction.Website,
            "ara" => RoutineAction.Search,
            "medya" => RoutineAction.Media,
            "soyle" or "söyle" => RoutineAction.Say,
            "ozet" or "özet" => RoutineAction.Briefing,
            "linguaflow" => RoutineAction.LinguaFlow,
            _ => throw new FormatException($"Bilinmeyen rutin adımı: {text}")
        };

        if (value.Length == 0 && action is not (RoutineAction.Briefing or RoutineAction.LinguaFlow))
        {
            throw new FormatException($"Rutin adımında değer eksik: {text}");
        }

        return new RoutineStep(action, value);
    }
}
