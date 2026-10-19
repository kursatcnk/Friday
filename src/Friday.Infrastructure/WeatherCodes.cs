namespace Friday.Infrastructure;

/// <summary>Open-Meteo'nun WMO hava kodlarının Türkçe karşılıkları.</summary>
internal static class WeatherCodes
{
    public static string Describe(int code) => code switch
    {
        0 => "açık",
        1 => "çoğunlukla açık",
        2 => "parçalı bulutlu",
        3 => "kapalı",
        45 or 48 => "sisli",
        51 or 53 or 55 => "çiseleyen yağmurlu",
        56 or 57 => "dondurucu çiselemeli",
        61 => "hafif yağmurlu",
        63 => "yağmurlu",
        65 => "kuvvetli yağmurlu",
        66 or 67 => "dondurucu yağmurlu",
        71 => "hafif karlı",
        73 => "karlı",
        75 => "yoğun karlı",
        77 => "kar taneli",
        80 or 81 => "sağanak yağışlı",
        82 => "şiddetli sağanaklı",
        85 or 86 => "kar sağanaklı",
        95 => "gök gürültülü fırtınalı",
        96 or 99 => "dolu ile gök gürültülü fırtınalı",
        _ => "belirsiz"
    };
}
