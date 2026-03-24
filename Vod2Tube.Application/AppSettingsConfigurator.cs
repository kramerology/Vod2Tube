using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Application
{
    /// <summary>
    /// Configures <see cref="AppSettings"/> from the <c>Settings</c> table in the
    /// database.  Registered as a singleton <see cref="IConfigureOptions{TOptions}"/>
    /// that creates a short-lived scope for each call, so it works correctly with
    /// both <see cref="IOptions{TOptions}"/> (singleton, called once) and
    /// <see cref="IOptionsSnapshot{TOptions}"/> (scoped, called once per scope).
    /// </summary>
    public class AppSettingsConfigurator : IConfigureOptions<AppSettings>
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public AppSettingsConfigurator(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public void Configure(AppSettings options)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = db.Settings.ToList();
            var dict = rows.ToDictionary(s => s.Key, s => s.Value);
            AppSettings.ApplyDictionary(options, dict);
        }
    }
}
