using MachineRancher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;


//TODO: Load machine plugins first, then connect to mqtt once .

var machine_plugins = AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => type.IsSubclassOf(typeof(Machine)))
        .ToList();
//.Select(type => Activator.CreateInstance(type) as Machine)
machine_plugins.ForEach(x => Console.WriteLine(x.Name));

var interface_plugins = AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => type.IsSubclassOf(typeof(Interface)))
        .ToList();
//.Select(type => Activator.CreateInstance(type) as Machine)
interface_plugins.ForEach(x => Console.WriteLine(x.Name));
//Console.WriteLine(machine_plugins.ToArray());


HostApplicationBuilder builder = Host.CreateApplicationBuilder();
builder.Services.AddHostedService<MachineMonitor>();


builder.Configuration.AddIniFile("appsettings.ini", optional: false, reloadOnChange: false);

var host = builder.Build();

var config = host.Services.GetService<IConfiguration>();
var logger = host.Services.GetService<ILogger<Program>>();

foreach (var plugin in machine_plugins)
{
    var current_section = config.GetSection(plugin.Name);
    if (current_section.Exists())
    {
        logger.LogInformation("Discovered plugin and configuration for " + plugin.Name + " machine plugin.");
    }
    else
    {
        logger.LogError("Could not find a matching configuration section for " + plugin.Name + " machine plugin.");
        return;
    }
}

foreach (var plugin in interface_plugins)
{
    var current_section = config.GetSection(plugin.Name);
    if (current_section.Exists())
    {
        logger.LogInformation("Discovered plugin and configuration for " + plugin.Name + " interface plugin.");
    }
    else
    {
        logger.LogError("Could not find a matching configuration section for " + plugin.Name + " interface plugin.");
        return;
    }
}


host.Run();