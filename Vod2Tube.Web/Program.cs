using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Vod2Tube.Application;
using Vod2Tube.Application.Services;
using Vod2Tube.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddTransient<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

builder.Services.AddScoped<ChannelService>();
builder.Services.AddScoped<PipelineService>();
builder.Services.AddScoped<TwitchGraphQLService>();
builder.Services.AddScoped<TwitchDownloadService>();
builder.Services.AddScoped<ChatDownloader>();
builder.Services.AddScoped<ChatRenderer>();
builder.Services.AddScoped<FinalRenderer>();
builder.Services.AddScoped<VideoUploader>();
builder.Services.AddScoped<VodDownloader>();


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

// Ensure the database schema is created (consistent with Console project)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapRazorComponents<Vod2Tube.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
