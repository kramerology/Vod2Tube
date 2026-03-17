using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Vod2Tube.Application.Services;
using Vod2Tube.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=Vod2Tube.db"),
    ServiceLifetime.Scoped);

builder.Services.AddScoped<ChannelService>();
builder.Services.AddScoped<PipelineService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<Vod2Tube.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
