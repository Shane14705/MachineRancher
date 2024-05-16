using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.XPath;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;

namespace MachineRancher
{
    internal class MachineMonitor : IHostedService
    {
        private string server_address;
        private int server_port;
        private string server_username;
        private string server_password;
        
        public Dictionary<string, Type> machine_plugins;

        private List<Machine> machines;

        private readonly IConfiguration configuration;
        private readonly ILogger logger;

        public MachineMonitor(ILogger<MachineMonitor> logger, IHostApplicationLifetime appLifetime, IConfiguration configuration)
        {
            this.machine_plugins = new Dictionary<string, Type>();
            this.machines = new List<Machine>();
            //var configuration = new ConfigurationBuilder()
            //    .AddIniFile("appsettings.ini", optional: false, reloadOnChange: false)
            //    .Build();
            this.configuration = configuration;
            var mqttsection = this.configuration.GetSection("MQTTSetup");
            this.logger = logger;
            this.server_address = mqttsection["MQTTServerIP"];
            if (!Int32.TryParse(mqttsection["MQTTServerPort"], out this.server_port))
            {
                this.logger.LogCritical("ERROR: Could not parse MQTTServerPort from appsettings.ini");
            }
            this.server_username = mqttsection["MQTTUsername"];
            this.server_password = mqttsection["MQTTPassword"];
            this.configuration = configuration;

            var plugins = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsSubclassOf(typeof(Machine)))
                .ToList();
            foreach (var plugin in plugins)
            {
                DiscoveryTopicAttribute[] disc_topic = (MachineRancher.DiscoveryTopicAttribute[]) plugin.GetCustomAttributes(typeof(DiscoveryTopicAttribute), true);
                //Type plugin_type = AppDomain.CurrentDomain.GetAssemblies()
                //    .SelectMany(assembly => assembly.GetTypes())
                //    .First(t => t.Name == plugin.FullName);
                machine_plugins.Add(disc_topic[0].path, plugin);
                this.logger.LogCritical(plugin.FullName);

            }
            //.Select(type => Activator.CreateInstance(type) as Machine)
            //this.machine_plugins.Keys.ForEach(x => Console.WriteLine(x.Name));
        }
        
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.Run(Monitor);
        }

        public Task StopAsync(CancellationToken cancellationToken) 
        {
            throw new NotImplementedException(); 
        }

        //TODO: When we discover a new printer, we need to check all of its attributes to subscribe it to specific values
        public async Task<List<Machine>> DiscoverMachines()
        {
            Regex regex = new Regex(@"^(([^/]*)\/([^/]*))\/");
            var mqttFactory = new MQTTnet.MqttFactory();
            List<Machine> machines = new List<Machine>();
            List<String> machineNames = new List<String>();
            using (var managedMqttClient = mqttFactory.CreateManagedMqttClient())
            {
                var mqttClientOptions = new MqttClientOptionsBuilder()
                    .WithTcpServer(this.server_address, this.server_port)
                    .WithCredentials(server_username, server_password)
                    .Build();
                var managedMqttClientOptions = new ManagedMqttClientOptionsBuilder()
                    .WithClientOptions(mqttClientOptions)
                    .Build();

                managedMqttClient.ApplicationMessageReceivedAsync += e =>
                {
                    //logger.LogInformation("Received application message.");
                    //logger.LogInformation("Topic: " + e.ApplicationMessage.Topic);
                    //logger.LogInformation("Payload: " + e.ApplicationMessage.ConvertPayloadToString());
                    
                    Match match = regex.Match(e.ApplicationMessage.Topic);
                    if (match.Success && !machineNames.Contains(match.Groups[0].Value))
                    {
                        //logger.LogInformation("Matched!");
                        //logger.LogInformation(match.Groups[1].Value);
                        machineNames.Add(match.Groups[0].Value);
                    }
                    return Task.CompletedTask;
                };

                await managedMqttClient.StartAsync(managedMqttClientOptions);
                foreach (string path in this.machine_plugins.Keys)
                {
                    logger.LogInformation("Subscribing to: " + path);

                    await managedMqttClient.SubscribeAsync(path);

                }

                await Task.Delay(1000);

            }
            
            List<(string, string)> discoveredDevices = new List<(string, string)>();

            machineNames.ForEach(str =>
            {
                var temp = str.Split("/");
                discoveredDevices.Add((temp[0] + "/#", temp[1]));
            });

            foreach ((string, string) pair in discoveredDevices)
            {
                if (machine_plugins.Keys.Contains(pair.Item1))
                {
                    Machine new_machine = (Machine)Activator.CreateInstance(Type.GetType(machine_plugins[pair.Item1].FullName));
                    machines.Add(new_machine);
                    new_machine.Name = pair.Item2;
                }
            }
            logger.LogInformation(String.Join(", ", machineNames));

            return machines;
            
        }

        public async Task Monitor()
        {
            this.machines = await DiscoverMachines();
            logger.LogInformation(this.machines.Count.ToString());

            Dictionary<string, (Machine, PropertyInfo, MonitorRegistrationAttribute)> monitored_properties = new Dictionary<string, (Machine, PropertyInfo, MonitorRegistrationAttribute)> ();

            foreach (Machine machine in this.machines)
            {
                foreach (var property in machine.GetType().GetProperties())
                {
                    MonitorRegistrationAttribute[] topics = (MonitorRegistrationAttribute[]) property.GetCustomAttributes(typeof(MonitorRegistrationAttribute), false);
                    if (topics.Length != 1)
                    {
                        logger.LogWarning("Machine " + machine.Name + ": Parameter " + property.Name + " does not have exactly one MQTT path to link with, and will be ignored.");
                        continue;
                    }
                    topics[0].topic = topics[0].topic.Replace("*", machine.Name);
                    
                    monitored_properties.Add(topics[0].topic, (machine, property, topics[0]));
                }
            }

            logger.LogInformation("Found " + monitored_properties.Keys.Count.ToString() + " properties and linked them to MQTT Topics. Setting up callbacks now!");

            var mqttFactory = new MQTTnet.MqttFactory();

            using (var managedMqttClient = mqttFactory.CreateManagedMqttClient())
            {
                var mqttClientOptions = new MqttClientOptionsBuilder()
                    .WithTcpServer(this.server_address, this.server_port)
                    .WithCredentials(server_username, server_password)
                    .Build();
                var managedMqttClientOptions = new ManagedMqttClientOptionsBuilder()
                    .WithClientOptions(mqttClientOptions)
                    .Build();
                
                managedMqttClient.ApplicationMessageReceivedAsync += e =>
                {
                    if (monitored_properties.ContainsKey(e.ApplicationMessage.Topic))
                    {
                        var target = monitored_properties[e.ApplicationMessage.Topic];
                        var str = e.ApplicationMessage.ConvertPayloadToString();
                        logger.LogInformation(str);

                        Dictionary<string, object> dict = JsonSerializer.Deserialize<Dictionary<string, object>>(str);
                        if (target.Item2.PropertyType == typeof(float))
                        {
                            float temp;
                            float.TryParse(dict[target.Item3.json_key].ToString(), out temp);
                            target.Item2.SetValue(target.Item1, temp);
                        }

                        else if (target.Item2.PropertyType == typeof(int))
                        {
                            int temp;
                            int.TryParse(dict[target.Item3.json_key].ToString(), out temp);
                            target.Item2.SetValue(target.Item1, temp);
                        }

                        else if (target.Item2.PropertyType == typeof(string))
                        {
                            target.Item2.SetValue(target.Item1, str);
                        }

                        else
                        {
                            logger.LogCritical("Machine " + target.Item1.Name + ": Parameter " + target.Item2.Name + "is not of a supported type! It is " + target.Item2.PropertyType.FullName + ", and supported types are float, int, and string.");
                        }

                    }
                    else
                    {
                        logger.LogWarning("Received a message for a topic we shouldn't be subscribed to: \nTopic: " + e.ApplicationMessage.Topic + "\nPayload: " + e.ApplicationMessage.ConvertPayloadToString());
                    }
                    //logger.LogInformation("Received application message.");
                    //logger.LogInformation("Topic: " + e.ApplicationMessage.Topic);
                    //logger.LogInformation("Payload: " + e.ApplicationMessage.ConvertPayloadToString());


                    return Task.CompletedTask;
                };

                await managedMqttClient.StartAsync(managedMqttClientOptions);
                foreach (string topic in monitored_properties.Keys)
                {
                    logger.LogInformation("Subscribing to: " + topic);

                    await managedMqttClient.SubscribeAsync(topic);
                    
                }
                //REMINDER: NEED TO ADJUST WEBSOCKET TOPIC TO BE JSON AND STRING OF "IP:PORT", ALSO NEED TO MAKE ALL OTHER TOPICS BE JSON AS WELL (LIKE NOZZLE SIZE)
                //TODO: TOMORROW, WE IMPLEMENT FUNCTIONS (ie: printer leveling)
                while (true) await Task.Delay(1000);

            }
            
        }
    }
}
