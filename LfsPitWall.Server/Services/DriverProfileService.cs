using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using LfsPitWall.Server.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace LfsPitWall.Server.Services;

public sealed class DriverProfileService : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILogger<DriverProfileService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PubstatOptions _options;
    private readonly string _cacheRootPath;
    private readonly Channel<string> _refreshQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly ConcurrentDictionary<string, DriverProfileCacheState> _cacheStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _queuedUsernames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<IReadOnlyDictionary<string, string>> CountryCodeByName = new(BuildCountryCodeByName);

    public DriverProfileService(
        ILogger<DriverProfileService> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<PubstatOptions> options,
        IHostEnvironment hostEnvironment)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _cacheRootPath = Path.GetFullPath(Path.Combine(hostEnvironment.ContentRootPath, _options.CacheRootPath));
    }

    public bool IsConfigured => _options.IsConfigured();

    public DriverProfileSummary GetDriverSummary(string? username)
    {
        var normalizedUsername = NormalizeUsername(username);
        if (normalizedUsername == null)
        {
            return DriverProfileSummary.Empty;
        }

        var cacheState = GetOrLoadCacheState(normalizedUsername);
        QueueRefreshIfNeeded(normalizedUsername, cacheState);

        if (cacheState.Record == null || !cacheState.Record.IsAvailable)
        {
            return new DriverProfileSummary
            {
                HasProfile = false,
                IsRefreshQueued = cacheState.IsRefreshQueued
            };
        }

        return new DriverProfileSummary
        {
            CountryName = cacheState.Record.CountryName,
            CountryCode = cacheState.Record.CountryCode,
            HasProfile = true,
            IsRefreshQueued = false
        };
    }

    public DriverProfileSnapshot GetDriverProfile(byte playerId, string? username, string driverNameHtml, string carName)
    {
        var normalizedUsername = NormalizeUsername(username);
        if (normalizedUsername == null)
        {
            return new DriverProfileSnapshot
            {
                PlayerId = playerId,
                DriverNameHtml = driverNameHtml,
                CarName = carName,
                CanRefresh = false,
                UnavailableReason = "No LFS username available for this driver."
            };
        }

        var cacheState = GetOrLoadCacheState(normalizedUsername);
        QueueRefreshIfNeeded(normalizedUsername, cacheState);

        if (cacheState.Record == null)
        {
            var unavailableReason = !IsConfigured
                ? "Pubstat is not configured on this host."
                : cacheState.IsRefreshQueued
                    ? "Profile is being fetched from LFS World."
                    : (cacheState.UnavailableReason ?? "Driver profile is unavailable.");

            return new DriverProfileSnapshot
            {
                PlayerId = playerId,
                Username = normalizedUsername,
                DriverNameHtml = driverNameHtml,
                CarName = carName,
                CanRefresh = IsConfigured,
                IsRefreshQueued = cacheState.IsRefreshQueued,
                UnavailableReason = unavailableReason
            };
        }

        if (!cacheState.Record.IsAvailable)
        {
            return new DriverProfileSnapshot
            {
                PlayerId = playerId,
                Username = normalizedUsername,
                DriverNameHtml = driverNameHtml,
                CarName = carName,
                CanRefresh = IsConfigured,
                IsRefreshQueued = cacheState.IsRefreshQueued,
                UnavailableReason = cacheState.Record.UnavailableReason
            };
        }

        return new DriverProfileSnapshot
        {
            PlayerId = playerId,
            Username = normalizedUsername,
            DriverNameHtml = driverNameHtml,
            CarName = carName,
            CountryName = cacheState.Record.CountryName,
            CountryCode = cacheState.Record.CountryCode,
            CurrentOrLastHostNameHtml = LfsColorConverter.ConvertToHtml(cacheState.Record.Stats.CurrentOrLastHostName),
            IsAvailable = true,
            IsRefreshQueued = cacheState.IsRefreshQueued,
            CanRefresh = IsConfigured,
            LastSuccessAtUtc = cacheState.Record.LastSuccessAtUtc,
            Stats = cacheState.Record.Stats
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(_cacheRootPath);

        if (!IsConfigured)
        {
            _logger.LogInformation("Driver profile enrichment is disabled because Pubstat is not configured.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var username = await _refreshQueue.Reader.ReadAsync(stoppingToken);
                await RefreshProfileAsync(username, stoppingToken);

                var delaySeconds = _options.GetClampedRequestIntervalSeconds();
                if (delaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected driver profile refresh loop error");
            }
        }
    }

    private DriverProfileCacheState GetOrLoadCacheState(string username)
    {
        return _cacheStates.GetOrAdd(username, LoadCacheStateFromDisk);
    }

    private DriverProfileCacheState LoadCacheStateFromDisk(string username)
    {
        var cachePath = GetCachePath(username);
        if (!File.Exists(cachePath))
        {
            return new DriverProfileCacheState();
        }

        try
        {
            var json = File.ReadAllText(cachePath);
            var record = JsonSerializer.Deserialize<DriverProfileRecord>(json, SerializerOptions);
            return new DriverProfileCacheState
            {
                Record = record,
                UnavailableReason = record?.UnavailableReason,
                NextRefreshAllowedAtUtc = record is { IsAvailable: false }
                    ? record.LastSuccessAtUtc.AddHours(12)
                    : DateTime.MinValue
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read cached driver profile for {Username}", username);
            return new DriverProfileCacheState();
        }
    }

    private void QueueRefreshIfNeeded(string username, DriverProfileCacheState cacheState)
    {
        if (!IsConfigured || !IsRefreshDue(cacheState))
        {
            return;
        }

        if (!_queuedUsernames.TryAdd(username, 0))
        {
            cacheState.IsRefreshQueued = true;
            return;
        }

        cacheState.IsRefreshQueued = true;
        cacheState.NextRefreshAllowedAtUtc = DateTime.UtcNow.AddSeconds(_options.GetClampedRequestIntervalSeconds());
        _refreshQueue.Writer.TryWrite(username);
    }

    private bool IsRefreshDue(DriverProfileCacheState cacheState)
    {
        var now = DateTime.UtcNow;
        if (cacheState.NextRefreshAllowedAtUtc > now)
        {
            return false;
        }

        if (cacheState.Record == null)
        {
            return true;
        }

        if (!cacheState.Record.IsAvailable)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(cacheState.Record.CountryCode) && string.IsNullOrWhiteSpace(cacheState.Record.CountryName))
        {
            return true;
        }

        return cacheState.Record.LastSuccessAtUtc.AddDays(_options.GetClampedStaleAfterDays()) <= now;
    }

    private async Task RefreshProfileAsync(string username, CancellationToken cancellationToken)
    {
        try
        {
            var fetchResult = await FetchDriverProfileAsync(username, cancellationToken);
            var record = fetchResult.Record;
            if (record == null)
            {
                _logger.LogInformation("Pubstat returned no driver profile data for {Username}", username);

                var missingRecord = new DriverProfileRecord
                {
                    Username = username,
                    IsAvailable = false,
                    UnavailableReason = fetchResult.UnavailableReason ?? "No LFS World profile was found for this username.",
                    FetchedAtUtc = DateTime.UtcNow,
                    LastSuccessAtUtc = DateTime.UtcNow
                };

                var missingCachePath = GetCachePath(username);
                Directory.CreateDirectory(Path.GetDirectoryName(missingCachePath)!);
                await File.WriteAllTextAsync(missingCachePath, JsonSerializer.Serialize(missingRecord, SerializerOptions), cancellationToken);

                if (_cacheStates.TryGetValue(username, out var missingProfileState))
                {
                    missingProfileState.Record = missingRecord;
                    missingProfileState.UnavailableReason = missingRecord.UnavailableReason;
                    missingProfileState.IsRefreshQueued = false;
                    missingProfileState.NextRefreshAllowedAtUtc = DateTime.UtcNow.AddHours(12);
                }
                else
                {
                    _cacheStates[username] = new DriverProfileCacheState
                    {
                        Record = missingRecord,
                        UnavailableReason = missingRecord.UnavailableReason,
                        IsRefreshQueued = false,
                        NextRefreshAllowedAtUtc = DateTime.UtcNow.AddHours(12)
                    };
                }

                return;
            }

            var cachePath = GetCachePath(username);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(record, SerializerOptions), cancellationToken);

            _cacheStates.AddOrUpdate(
                username,
                _ => new DriverProfileCacheState { Record = record },
                (_, existing) =>
                {
                    existing.Record = record;
                    existing.UnavailableReason = null;
                    existing.IsRefreshQueued = false;
                    existing.NextRefreshAllowedAtUtc = DateTime.MinValue;
                    return existing;
                });

            _logger.LogInformation("Refreshed driver profile for {Username} ({CountryCode})", username, record.CountryCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh driver profile for {Username}", username);

            if (_cacheStates.TryGetValue(username, out var cacheState))
            {
                cacheState.UnavailableReason ??= "Failed to load driver profile from LFS World.";
                cacheState.IsRefreshQueued = false;
                cacheState.NextRefreshAllowedAtUtc = DateTime.UtcNow.AddSeconds(_options.GetClampedRequestIntervalSeconds());
            }
        }
        finally
        {
            _queuedUsernames.TryRemove(username, out _);

            if (_cacheStates.TryGetValue(username, out var cacheState))
            {
                cacheState.IsRefreshQueued = false;
            }
        }
    }

    private async Task<DriverProfileFetchResult> FetchDriverProfileAsync(string username, CancellationToken cancellationToken)
    {
        var requestUri = BuildPubstatUri(username);
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(20);

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        return new DriverProfileFetchResult(
            ParseDriverProfile(username, responseText),
            ParseUnavailableReason(responseText));
    }

    private string BuildPubstatUri(string username)
    {
        var query = new Dictionary<string, string?>
        {
            ["idk"] = _options.IdentKey,
            ["action"] = "pst",
            ["racer"] = username
        };

        if (_options.UsePremiumEndpoint)
        {
            query["ps"] = "1";
        }

        return QueryHelpers.AddQueryString(_options.PubstatUrl, query);
    }

    private static DriverProfileRecord? ParseDriverProfile(string username, string responseText)
    {
        var lines = responseText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count < 14)
        {
            return null;
        }

        return new DriverProfileRecord
        {
            Username = username,
            IsAvailable = true,
            CountryName = (lines.ElementAtOrDefault(12) ?? "").Trim(),
            CountryCode = ResolveCountryCode(lines.ElementAtOrDefault(12)),
            Stats = new DriverProfileStats
            {
                DistanceMeters = ParseLong(lines, 0),
                FuelBurntCentilitres = ParseLong(lines, 1),
                Laps = ParseInt(lines, 2),
                HostsJoined = ParseInt(lines, 3),
                Wins = ParseInt(lines, 4),
                SecondPlaces = ParseInt(lines, 5),
                ThirdPlaces = ParseInt(lines, 6),
                Finishes = ParseInt(lines, 7),
                QualifyingSessions = ParseInt(lines, 8),
                PolePositions = ParseInt(lines, 9),
                DragRaces = ParseInt(lines, 10),
                DragWins = ParseInt(lines, 11),
                OnlineStatus = ParseInt(lines, 13),
                CurrentOrLastHostName = lines.ElementAtOrDefault(14)?.Trim() ?? "",
                LastActivityUnixSeconds = ParseNullableLong(lines, 15),
                CurrentOrLastTrack = lines.ElementAtOrDefault(16)?.Trim() ?? "",
                CurrentOrLastCar = lines.ElementAtOrDefault(17)?.Trim() ?? ""
            },
            FetchedAtUtc = DateTime.UtcNow,
            LastSuccessAtUtc = DateTime.UtcNow
        };
    }

    private static string? ParseUnavailableReason(string responseText)
    {
        var normalized = responseText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Contains("has chosen to hide his online statistics", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return null;
    }

    private string GetCachePath(string username)
    {
        return Path.Combine(_cacheRootPath, $"{SanitizeFileSegment(username)}.json");
    }

    private static string? NormalizeUsername(string? username)
    {
        return string.IsNullOrWhiteSpace(username)
            ? null
            : username.Trim();
    }

    private static string NormalizeCountryCode(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? "";
        return normalized.Length == 2 ? normalized : "";
    }

    private static string ResolveCountryCode(string? value)
    {
        var normalizedCode = NormalizeCountryCode(value);
        if (!string.IsNullOrEmpty(normalizedCode))
        {
            return normalizedCode;
        }

        var countryName = value?.Trim();
        if (string.IsNullOrWhiteSpace(countryName))
        {
            return "";
        }

        return CountryCodeByName.Value.TryGetValue(countryName, out var countryCode)
            ? countryCode
            : "";
    }

    private static IReadOnlyDictionary<string, string> BuildCountryCodeByName()
    {
        var countryCodeByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                if (!countryCodeByName.ContainsKey(region.EnglishName))
                {
                    countryCodeByName[region.EnglishName] = region.TwoLetterISORegionName;
                }
            }
            catch
            {
                // Ignore cultures that do not map cleanly to a region.
            }
        }

        countryCodeByName["Czech Republic"] = "CZ";
        countryCodeByName["Russia"] = "RU";
        countryCodeByName["South Korea"] = "KR";
        countryCodeByName["North Korea"] = "KP";
        countryCodeByName["Taiwan"] = "TW";
        countryCodeByName["Venezuela"] = "VE";

        return countryCodeByName;
    }

    private static string SanitizeFileSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var buffer = value
            .Trim()
            .Select(character => invalidChars.Contains(character) ? '_' : character)
            .ToArray();
        return new string(buffer);
    }

    private static int ParseInt(IReadOnlyList<string> lines, int index)
    {
        return int.TryParse(lines.ElementAtOrDefault(index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static long ParseLong(IReadOnlyList<string> lines, int index)
    {
        return long.TryParse(lines.ElementAtOrDefault(index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static long? ParseNullableLong(IReadOnlyList<string> lines, int index)
    {
        return long.TryParse(lines.ElementAtOrDefault(index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private sealed class DriverProfileCacheState
    {
        public DriverProfileRecord? Record { get; set; }
        public DateTime NextRefreshAllowedAtUtc { get; set; }
        public bool IsRefreshQueued { get; set; }
        public string? UnavailableReason { get; set; }
    }

    private sealed record DriverProfileFetchResult(DriverProfileRecord? Record, string? UnavailableReason);
}