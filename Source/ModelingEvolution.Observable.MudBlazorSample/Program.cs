using ModelingEvolution.Observable.MudBlazorSample;
using ModelingEvolution.Observable.MudBlazorSample.Components;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddSingleton<LiveRows>();
builder.Services.AddHostedService<Fold>();

var app = builder.Build();
app.MapStaticAssets();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
