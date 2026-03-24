using Microsoft.EntityFrameworkCore;
using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Application.Services
{
    /// <summary>
    /// Reads and writes application settings to the <c>Settings</c> table.
    /// </summary>
    public class SettingsService
    {
        private readonly AppDbContext _db;

        public SettingsService(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Returns the current settings.  Any key not present in the database
        /// will fall back to its <see cref="AppSettings"/> default value.
        /// </summary>
        public async Task<AppSettings> GetSettingsAsync()
        {
            var rows = await _db.Settings.AsNoTracking().ToListAsync();
            var dict = rows.ToDictionary(s => s.Key, s => s.Value);
            var settings = new AppSettings();
            AppSettings.ApplyDictionary(settings, dict);
            return settings;
        }

        /// <summary>
        /// Persists all settings from <paramref name="settings"/> to the
        /// database, inserting or updating as required.
        /// </summary>
        public async Task SaveSettingsAsync(AppSettings settings)
        {
            var dict = settings.ToDictionary();

            var existing = await _db.Settings.ToListAsync();
            var lookup = existing.ToDictionary(s => s.Key);

            foreach (var (key, value) in dict)
            {
                if (lookup.TryGetValue(key, out var row))
                {
                    row.Value = value;
                }
                else
                {
                    _db.Settings.Add(new Setting { Key = key, Value = value });
                }
            }

            await _db.SaveChangesAsync();
        }
    }
}
