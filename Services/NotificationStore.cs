using Jellyfin.Plugin.TelegramNotifier.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.TelegramNotifier.Services;

/// <summary>
/// Persists sent notification records and pending episode groups to JSON
/// files in the Jellyfin data directory.
/// </summary>
public class NotificationStore
{
    private readonly ILogger<NotificationStore> _logger;
    private readonly string _recordsPath;
    private readonly string _pendingGroupsPath;
    private readonly string _pendingItemsPath;

    private List<NotificationRecord> _records = new();
    private List<PendingEpisodeGroup> _pendingGroups = new();
    private List<PendingItem> _pendingItems = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public NotificationStore(ILogger<NotificationStore> logger, IApplicationPaths appPaths)
    {
        _logger = logger;
        var dir = Path.Combine(appPaths.DataPath, "TelegramNotifier");
        Directory.CreateDirectory(dir);
        _recordsPath = Path.Combine(dir, "notifications.json");
        _pendingGroupsPath = Path.Combine(dir, "pending_groups.json");
        _pendingItemsPath = Path.Combine(dir, "pending_items.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_recordsPath))
                _records = JsonConvert.DeserializeObject<List<NotificationRecord>>(File.ReadAllText(_recordsPath)) ?? new();
            if (File.Exists(_pendingGroupsPath))
                _pendingGroups = JsonConvert.DeserializeObject<List<PendingEpisodeGroup>>(File.ReadAllText(_pendingGroupsPath)) ?? new();
            if (File.Exists(_pendingItemsPath))
                _pendingItems = JsonConvert.DeserializeObject<List<PendingItem>>(File.ReadAllText(_pendingItemsPath)) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load notification store");
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_recordsPath, JsonConvert.SerializeObject(_records, Formatting.Indented));
            File.WriteAllText(_pendingGroupsPath, JsonConvert.SerializeObject(_pendingGroups, Formatting.Indented));
            File.WriteAllText(_pendingItemsPath, JsonConvert.SerializeObject(_pendingItems, Formatting.Indented));
            _logger.LogDebug("NotificationStore: Saved all files successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NotificationStore: Failed to save to JSON files");
        }
    }

    // ── Sent Records ─────────────────────────────────────────────────────────

    public async Task<bool> HasSentAsync(string itemId)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try { return _records.Any(r => r.ItemId == itemId && r.WasSent); }
        finally { _lock.Release(); }
    }

    public async Task AddOrUpdateAsync(NotificationRecord record)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var cfg = Plugin.Instance!.Configuration;
            _records.RemoveAll(r => r.ItemId == record.ItemId);
            _records.Insert(0, record);
            _logger.LogInformation("NotificationStore: Added/Updated record for {ItemId}. Total records: {Count}", record.ItemId, _records.Count);
            // Trim to max
            if (_records.Count > cfg.MaxRecentEvents)
                _records = _records.Take(cfg.MaxRecentEvents).ToList();
            Save();
        }
        finally { _lock.Release(); }
    }

    public async Task<List<NotificationRecord>> GetRecentAsync(int count = 50)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try { return _records.Take(count).ToList(); }
        finally { _lock.Release(); }
    }

    public async Task<NotificationRecord?> GetByIdAsync(string itemId)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try { return _records.FirstOrDefault(r => r.ItemId == itemId); }
        finally { _lock.Release(); }
    }

    public async Task DeleteAsync(string itemId)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _records.RemoveAll(r => r.ItemId == itemId);
            Save();
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteBulkAsync(IEnumerable<string> itemIds)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var idSet = itemIds.ToHashSet();
            _records.RemoveAll(r => idSet.Contains(r.ItemId));
            Save();
        }
        finally { _lock.Release(); }
    }

    // ── Pending Episode Groups ────────────────────────────────────────────────

    public async Task<PendingEpisodeGroup?> GetPendingGroupAsync(string seriesId, int seasonNumber)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try { return _pendingGroups.FirstOrDefault(p => p.SeriesId == seriesId && p.SeasonNumber == seasonNumber); }
        finally { _lock.Release(); }
    }

    public async Task UpsertPendingGroupAsync(PendingEpisodeGroup group)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _pendingGroups.RemoveAll(p => p.SeriesId == group.SeriesId && p.SeasonNumber == group.SeasonNumber);
            _pendingGroups.Add(group);
            Save();
        }
        finally { _lock.Release(); }
    }

    public async Task RemovePendingGroupAsync(string seriesId, int seasonNumber)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _pendingGroups.RemoveAll(p => p.SeriesId == seriesId && p.SeasonNumber == seasonNumber);
            Save();
        }
        finally { _lock.Release(); }
    }

    // ── Pending Items (Delay/Scan sync) ────────────────────────────────────────

    public async Task AddPendingItemAsync(string itemId, string type)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_pendingItems.Any(i => i.ItemId == itemId))
            {
                _pendingItems.Add(new PendingItem { ItemId = itemId, Type = type, AddedAt = DateTime.UtcNow });
                Save();
            }
        }
        finally { _lock.Release(); }
    }

    public async Task<List<PendingItem>> GetPendingItemsAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try { return _pendingItems.ToList(); }
        finally { _lock.Release(); }
    }

    public async Task ClearPendingItemsAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _pendingItems.Clear();
            Save();
        }
        finally { _lock.Release(); }
    }
}
