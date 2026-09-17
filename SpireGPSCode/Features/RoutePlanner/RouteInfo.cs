using MegaCrit.Sts2.Core.Map;

namespace SpireGPS.Features.RoutePlanner;

public sealed class RouteInfo
{
    public int Index { get; }
    public IReadOnlyList<MapPoint> Points { get; }
    public int Monsters { get; }
    public int Unknowns { get; }
    public int Elites { get; }
    public int Shops { get; }
    public int RestSites { get; }
    public int Treasures { get; }

    public RouteInfo(int index, IReadOnlyList<MapPoint> points)
    {
        Index = index;
        Points = points;

        foreach (var point in points.Skip(1))
        {
            if (point.PointType == MapPointType.Boss) continue;

            switch (point.PointType)
            {
                case MapPointType.Monster: Monsters++; break;
                case MapPointType.Unknown: Unknowns++; break;
                case MapPointType.Elite: Elites++; break;
                case MapPointType.Shop: Shops++; break;
                case MapPointType.RestSite: RestSites++; break;
                case MapPointType.Treasure: Treasures++; break;
            }
        }
    }

    public string Signature => string.Join(" → ", Points
        .Skip(1)
        .Where(p => p.PointType != MapPointType.Boss)
        .Select(p => p.PointType switch
        {
            MapPointType.Monster => "M",
            MapPointType.Unknown => "?",
            MapPointType.Elite => "E",
            MapPointType.Shop => "$",
            MapPointType.RestSite => "R",
            MapPointType.Treasure => "T",
            _ => p.PointType.ToString()
        }));
}
