using System.IO;
using System.Linq;
using System.Text.Json;
using OpenOTSTray.Models;

namespace OpenOTSTray.Services;

/// <summary>
/// What actually gets written to disk. Deliberately excludes Password, Passphrase and
/// EmailBody — only enough to redraw the card and re-check its open/burned status.
/// The link is the one field worth keeping (so Copy Link / Send Email still work after
/// a restart), and it's stored DPAPI-encrypted, never in the clear.
/// </summary>
internal class StoredHistoryEntry
{
    public string Kind { get; set; } = "";
    public string TitleLabel { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Region { get; set; } = "";
    public string ReceiptIdentifier { get; set; } = "";
    public bool IsOpened { get; set; }
    public string? EncryptedLink { get; set; }
}

public class HistoryStorageService
{
    private static readonly string HistoryDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenOTSTray");
    private static readonly string HistoryPath = Path.Combine(HistoryDir, "history.json");

    public List<PasswordResultItem> Load()
    {
        try
        {
            if (!File.Exists(HistoryPath))
                return new List<PasswordResultItem>();

            var json = File.ReadAllText(HistoryPath);
            var entries = JsonSerializer.Deserialize<List<StoredHistoryEntry>>(json);
            if (entries == null)
                return new List<PasswordResultItem>();

            return entries.Select(entry => new PasswordResultItem
            {
                Kind = entry.Kind,
                Icon = entry.IsOpened ? "\U0001F513" : "\U0001F512",
                TitleLabel = entry.TitleLabel,
                Subtitle = entry.Subtitle,
                Region = entry.Region,
                ReceiptIdentifier = entry.ReceiptIdentifier,
                IsOpened = entry.IsOpened,
                Link = LinkProtector.Unprotect(entry.EncryptedLink),
            }).ToList();
        }
        catch
        {
            // history file missing/corrupted: start with an empty history rather than crash
            return new List<PasswordResultItem>();
        }
    }

    public void Save(IEnumerable<PasswordResultItem> history)
    {
        try
        {
            Directory.CreateDirectory(HistoryDir);

            var entries = history
                .Where(item => item.Succeeded)
                .Select(item => new StoredHistoryEntry
                {
                    Kind = item.Kind,
                    TitleLabel = item.TitleLabel,
                    Subtitle = item.Subtitle,
                    Region = item.Region,
                    ReceiptIdentifier = item.ReceiptIdentifier,
                    IsOpened = item.IsOpened,
                    EncryptedLink = LinkProtector.Protect(item.Link),
                })
                .ToList();

            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(HistoryPath, json);
        }
        catch
        {
            // best-effort: history still works in-memory for this session even if the write fails
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(HistoryPath))
                File.Delete(HistoryPath);
        }
        catch
        {
            // best-effort
        }
    }
}
