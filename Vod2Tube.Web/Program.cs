using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Vod2Tube.Application;
using Vod2Tube.Application.Services;
using Vod2Tube.Infrastructure;


AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
{
    Console.Error.WriteLine($"[FATAL] Unhandled exception: {args.ExceptionObject}");
};

TaskScheduler.UnobservedTaskException += (sender, args) =>
{
    Console.Error.WriteLine($"[FATAL] Unobserved task exception: {args.Exception}");
    args.SetObserved();
};

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddTransient<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// Settings
builder.Services.AddOptions<AppSettings>();
builder.Services.AddSingleton<IConfigureOptions<AppSettings>, AppSettingsConfigurator>();
builder.Services.AddScoped<SettingsService>();

builder.Services.AddScoped<ChannelService>();
builder.Services.AddScoped<PipelineService>();
builder.Services.AddScoped<TwitchGraphQLService>();
builder.Services.AddScoped<TwitchDownloadService>();
builder.Services.AddScoped<ChatDownloader>();
builder.Services.AddScoped<ChatRenderer>();
builder.Services.AddScoped<FinalRenderer>();
builder.Services.AddScoped<VideoUploader>();
builder.Services.AddScoped<VodDownloader>();
builder.Services.AddScoped<YouTubeAccountService>();

builder.Services.AddHostedService<VodPopulator>();
builder.Services.AddHostedService<JobManager>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.MapRazorComponents<Vod2Tube.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
