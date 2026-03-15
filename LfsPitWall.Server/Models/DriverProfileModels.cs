namespace LfsPitWall.Server.Models;

public sealed class DriverProfileStats
{
    public long DistanceMeters { get; set; }
    public long FuelBurntCentilitres { get; set; }
    public int Laps { get; set; }
    public int HostsJoined { get; set; }
    public int Wins { get; set; }
    public int SecondPlaces { get; set; }
    public int ThirdPlaces { get; set; }
    public int Finishes { get; set; }
    public int QualifyingSessions { get; set; }
    public int PolePositions { get; set; }
    public int DragRaces { get; set; }
    public int DragWins { get; set; }
    public int OnlineStatus { get; set; }
    public string CurrentOrLastHostName { get; set; } = "";
    public long? LastActivityUnixSeconds { get; set; }
    public string CurrentOrLastTrack { get; set; } = "";
    public string CurrentOrLastCar { get; set; } = "";

    public int Podiums => Wins + SecondPlaces + ThirdPlaces;
}

public sealed class DriverProfileRecord
{
    public string Username { get; set; } = "";
    public bool IsAvailable { get; set; } = true;
    public string UnavailableReason { get; set; } = "";
    public string CountryName { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public DriverProfileStats Stats { get; set; } = new();
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSuccessAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DriverProfileSummary
{
    public static DriverProfileSummary Empty { get; } = new();

    public string CountryName { get; init; } = "";
    public string CountryCode { get; init; } = "";
    public bool HasProfile { get; init; }
    public bool IsRefreshQueued { get; init; }
}

public sealed class DriverProfileSnapshot
{
    public byte PlayerId { get; set; }
    public string Username { get; set; } = "";
    public string DriverNameHtml { get; set; } = "";
    public string CarName { get; set; } = "";
    public string CountryName { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string CurrentOrLastHostNameHtml { get; set; } = "";
    public bool IsAvailable { get; set; }
    public bool IsRefreshQueued { get; set; }
    public bool CanRefresh { get; set; }
    public string UnavailableReason { get; set; } = "";
    public DateTime? LastSuccessAtUtc { get; set; }
    public DriverProfileStats? Stats { get; set; }
}