using Glazor.Demo.Components;
using Glazor.Demo.Data;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();   // the library's stylesheets and scripts are served from its package in any environment
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// the Chinook sample database, copied to the output folder with the app and from there to a temporary file at each start:
// the paged page edits it, and every run starts from the sample data. The file is this process's own: a second demo
// started beside it would copy the sample data over the one the first is editing. Those of processes that are gone go here
const string chinookPrefix = "glazor-demo-chinook-";
HashSet<int> running = [.. Process.GetProcesses().Select(p => p.Id)];
foreach (string old in Directory.EnumerateFiles(Path.GetTempPath(), chinookPrefix + "*.sqlite"))
{
    if (int.TryParse(Path.GetFileNameWithoutExtension(old)[chinookPrefix.Length..], out int pid) && !running.Contains(pid))
        File.Delete(old);
}
string chinook = Path.Combine(Path.GetTempPath(), $"{chinookPrefix}{Environment.ProcessId}.sqlite");
File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "chinook.sqlite"), chinook, overwrite: true);
builder.Services.AddDbContextFactory<ChinookContext>(o => o.UseSqlite($"Data Source={chinook}"));
string sakila = Path.Combine(AppContext.BaseDirectory, "Data", "sakila.sqlite");
builder.Services.AddDbContextFactory<SakilaContext>(o => o.UseSqlite($"Data Source={sakila};Mode=ReadOnly"));

var app = builder.Build();
Glazor.Misc.LoggerSupport.SetLoggerFactory(app.Services.GetRequiredService<ILoggerFactory>());

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
