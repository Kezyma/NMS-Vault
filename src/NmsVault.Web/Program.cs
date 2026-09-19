using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NmsVault.Web;
using NmsVault.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Bound to the host's own base address so the same code works from a dev server, a project
// page under a repository name, and a custom domain without knowing which it is.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<GalleryData>();
builder.Services.AddScoped<TechData>();

await builder.Build().RunAsync();
