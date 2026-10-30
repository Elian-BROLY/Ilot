using System.Windows.Media;

namespace Ilot.Controls;

/// <summary>Pictogrammes météo vectoriels, dans un carré de 24 unités.</summary>
internal static class WeatherIcons
{
    private const string Sun = "M12,7 a5,5 0 1 1 0,10 a5,5 0 1 1 0,-10 Z "
        + "M11.2,1 h1.6 v3.5 h-1.6 Z M11.2,19.5 h1.6 v3.5 h-1.6 Z M1,11.2 h3.5 v1.6 h-3.5 Z M19.5,11.2 h3.5 v1.6 h-3.5 Z "
        + "M4.1,5.2 l1.1,-1.1 l2.5,2.5 l-1.1,1.1 Z M16.3,17.4 l1.1,-1.1 l2.5,2.5 l-1.1,1.1 Z "
        + "M18.8,4.1 l1.1,1.1 l-2.5,2.5 l-1.1,-1.1 Z M6.6,16.3 l1.1,1.1 l-2.5,2.5 l-1.1,-1.1 Z";
    private const string SmallSun = "M8,3 a4,4 0 1 1 -0.01,0 Z";
    private const string Cloud = "M6.5,19 a4.5,4.5 0 0 1 -0.6,-8.96 a6.5,6.5 0 0 1 12.4,0.46 a4.3,4.3 0 0 1 -0.5,8.5 Z";
    private const string HighCloud = "M6.5,14 a4.5,4.5 0 0 1 -0.6,-8.96 a6.5,6.5 0 0 1 12.4,0.46 a4.3,4.3 0 0 1 -0.5,8.5 Z";
    private const string RainDrops = "M8,16 l1.4,0.5 l-1.6,5 l-1.4,-0.5 Z M12.5,16 l1.4,0.5 l-1.6,5 l-1.4,-0.5 Z M17,16 l1.4,0.5 l-1.6,5 l-1.4,-0.5 Z";
    private const string SnowFlakes = "M8,17 a1.3,1.3 0 1 1 -0.01,0 Z M12.5,19.5 a1.3,1.3 0 1 1 -0.01,0 Z M17,17 a1.3,1.3 0 1 1 -0.01,0 Z";
    private const string FogLines = "M3,16 h18 v1.6 h-18 Z M5,20 h14 v1.6 h-14 Z";
    private const string Bolt = "M13,15 h3.5 l-3,4 h2.5 l-5.5,5 l1.5,-4 h-2.5 Z";

    private static readonly Dictionary<string, Geometry> Cache = [];

    public static Geometry ForCode(int code) => Get(code switch
    {
        0 => Sun,
        1 or 2 => SmallSun + " " + Cloud,
        3 => Cloud,
        45 or 48 => HighCloud + " " + FogLines,
        >= 51 and <= 67 or >= 80 and <= 82 => HighCloud + " " + RainDrops,
        >= 71 and <= 77 or 85 or 86 => HighCloud + " " + SnowFlakes,
        >= 95 => HighCloud + " " + Bolt,
        _ => Cloud,
    });

    private static Geometry Get(string data)
    {
        if (!Cache.TryGetValue(data, out var geometry))
        {
            geometry = Geometry.Parse("F1 " + data);
            geometry.Freeze();
            Cache[data] = geometry;
        }
        return geometry;
    }
}
