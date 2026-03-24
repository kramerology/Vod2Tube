using Microsoft.Extensions.Options;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Application
{
    /// <summary>
    /// Configures <see cref="AppSettings"/> from the <c>Settings</c> table in the
    /// database.  Registered as a scoped <see cref="IConfigureOptions{TOptions}"/>
    /// so that <see cref="IOptionsSnapshot{TOptions}"/> picks up the latest values
    /// on every DI scope (i.e. once per processed job).
    /// </summary>
    public class AppSettingsConfigurator : IConfigureOptions<AppSettings>
    {
        private readonly AppDbContext _db;

        public AppSettingsConfigurator(AppDbContext db)
        {
            _db = db;
        }

        public void Configure(AppSettings options)
        {
            var rows = _db.Settings.ToList();
            var dict = rows.ToDictionary(s => s.Key, s => s.Value);
            AppSettings.ApplyDictionary(options, dict);
        }
    }
}
