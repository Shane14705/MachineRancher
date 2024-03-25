using MachineRancher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


HostApplicationBuilder builder = Host.CreateApplicationBuilder();
builder.Services.AddHostedService<MachineMonitor>();


builder.Configuration.AddIniFile("appsettings.ini", optional: false, reloadOnChange: false);

builder.Build().Run();