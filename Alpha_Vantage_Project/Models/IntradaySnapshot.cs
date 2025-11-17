namespace Alpha_Vantage_Project.Models;

public sealed record IntradaySnapshot(
    IntradayMetaData MetaData,
    IReadOnlyList<IntradayPoint> Points);

public sealed record IntradayMetaData(
    string Information,
    string Symbol,
    string LastRefreshed,
    string Interval,
    string TimeZone);

public sealed record IntradayPoint(
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

