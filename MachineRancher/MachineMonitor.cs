using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;

namespace MachineRancher
{
    internal class MachineMonitor
    {
        private string server_address;
        private int server_port;
        private string server_username;
        private string server_password;

        public MachineMonitor() 
        {
            var configuration = new ConfigurationBuilder()
                .AddIniFile("appsettings.ini", optional: false, reloadOnChange: false)
                .Build();
            var mqttsection = configuration.GetSection("MQTTSetup");

            this.server_address = mqttsection["MQTTServerIP"];
            if (!Int32.TryParse(mqttsection["MQTTServerPort"], out this.server_port))
            {
                Console.WriteLine("ERROR: Could not parse MQTTServerPort from appsettings.ini");
            }
            this.server_username = mqttsection["MQTTUsername"];
            this.server_password = mqttsection["MQTTPassword"];

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
                    Console.WriteLine("Received application message.");
                    Console.WriteLine("Topic: " + e.ApplicationMessage.Topic);
                    Console.WriteLine("Payload: " + e.ApplicationMessage.ConvertPayloadToString());
                    return Task.CompletedTask;
                };

                Console.WriteLine("Press enter to exit.");
                Console.ReadLine();


            }

        }
    }
}
