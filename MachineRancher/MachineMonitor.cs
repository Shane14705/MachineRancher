using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        private readonly IConfiguration configuration;
        private readonly ILogger logger;

        public MachineMonitor(ILogger<MachineMonitor> logger, IHostApplicationLifetime appLifetime, IConfiguration configuration)
        {
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
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.Run(Monitor);
        }

        public Task StopAsync(CancellationToken cancellationToken) 
        {
            throw new NotImplementedException(); 
        }

        public async Task Monitor()
        {
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

                await managedMqttClient.StartAsync(managedMqttClientOptions);

                await managedMqttClient.SubscribeAsync("Printers/#");

                managedMqttClient.ApplicationMessageReceivedAsync += e =>
                {
                    logger.LogInformation("Received application message.");
                    logger.LogInformation("Topic: " + e.ApplicationMessage.Topic);
                    logger.LogInformation("Payload: " + e.ApplicationMessage.ConvertPayloadToString());
                    return Task.CompletedTask;
                };

                while (true) await Task.Delay(100);

            }

        }
    }
}
