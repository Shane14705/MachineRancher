using System;
using System.Collections.Generic;
using WatsonWebsocket;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net;
using System.Text.RegularExpressions;

namespace MachineRancher
{
    [DiscoveryTopic("Printers/#")]
    internal class Printer : Machine
    {
        private string name;
        public override string Name { get => name; set => name = value; }

        private string description;
        public override string Description { get => name; }

        [MonitorRegistration("Printers/*/klipper/state/heater_bed/temperature", "value", 5)]
        public float Bed_Temperature { get => bed_temperature; set => bed_temperature = value; } //Note: we can do averaging here, or do it in the mqtt monitor. Probably more efficient to do in the monitor

        private float bed_temperature;

        [MonitorRegistration("Printers/*/klipper/state/extruder/temperature", "value", 5)]
        public float Extruder_Temperature { get => extruder_temperature; set => extruder_temperature = value; } //Note: we can do averaging here, or do it in the mqtt monitor. Probably more efficient to do in the monitor

        private float extruder_temperature;

        [MonitorRegistration("Printers/*/klipper/state/fan/speed", "value", 5)]
        public float Fan_Speed { get => fan_speed; set => fan_speed = value; } //Note: we can do averaging here, or do it in the mqtt monitor. Probably more efficient to do in the monitor
        
        private float fan_speed;

        [MonitorRegistration("Printers/*/moonraker/status/connections", "websocket")]
        public string Websocket
        {
            get
            {
                
                return (websocket_addr + websocket_port.ToString());
            }
            set
            {
                var temp = value.Split(":");
                websocket_addr = temp[0];
                int.TryParse(temp[1], out websocket_port);
            }
        }

        private string websocket_addr = string.Empty;

        private int websocket_port = -1;

        //[MonitorRegistration("Printers/*/moonraker/state/nozzle_size")]
        //public float Nozzle_Size { get => nozzle_size; set => nozzle_size = value; } //Note: we can do averaging here, or do it in the mqtt monitor. Probably more efficient to do in the monitor

        //private float nozzle_size;

        public Printer(string name) : base(name)
        {
        }


        /*
         * async def send_command(self, cmd):
        async with websockets.connect(self.printer_addr) as websocket:
            await websocket.send("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.gcode.script\",\"params\": { \"script\": \"" + cmd + "\"}, \"id\": " + str(random.Random().randint(0, 9999)) + "}")
            #Call the above with BED_SCREWS_ADJUST and wait to receive response we can parse...
        */
        public async Task Send_Command(string command)
        {
            throw new NotImplementedException();
        }

        public async Task Send_Command(WatsonWsClient wsclient, string command)
        {
            Random rand = new Random();
            await wsclient.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.gcode.script\",\"params\": { \"script\": \"" + command + "\"}, \"id\": " + rand.Next(0, 9999).ToString() + "}");
        }

        /// <summary>
        /// Helper function to turn the strings gathered from the bed leveling command into values by which screws can be adjusted.
        /// </summary>
        /// <param name="adjustment"></param>
        /// <returns>Float representing the number of rotations to be done to the given screw. Negative values represent counter-clockwise rotation, and positive represents clockwise.</returns>
        private float string_to_rotations(string adjustment)
        {
            string[] values = adjustment.Split(':');
            if (values.Length != 2) 
            {
                Console.WriteLine("error: unexpected adj string: " + adjustment);
                return 0;
            }

            float output = 0;

            output += float.Parse(values[0]);

            output += (float.Parse(values[1]) / 60);

            if (adjustment.Contains("CCW"))
            {
                output *= -1;
            }

            return output;
            
        }

        //TODO: Implement this, which also requires implementing the digital twin of the printer and checks against nozzle type etc
        public async Task<List<string>> RetrievePrintables()
        {
            Regex regex = new Regex("\"path\": \"([^\"]+)");
            bool response_received = false;
            List<string> ret = new List<string>();
            WatsonWsClient client = new WatsonWsClient(websocket_addr, websocket_port);
            Random rand = new Random();
            int request_id = rand.Next(0, 9999);
            client.MessageReceived += (sender, message) =>
            {
                string msg = Encoding.UTF8.GetString(message.Data.Array, 0, message.Data.Count);
                if (msg.Contains("\"id\": " + request_id.ToString()))
                {
                    foreach (Match match in regex.Matches(msg))
                    {
                        ret.Add(match.Groups[0].Value);
                    }
                    response_received = true;
                }
            };
            await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"server.files.list\",\"params\": { \"root\": \"" + "gcodes" + "\"}, \"id\": " + request_id.ToString() + "}");

            //TODO: Figure out config file so we can make these delays customizable
            var tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;
            var waitTask = Task.Run((async () =>
            {
                while (!response_received)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(50, token);
                }
            }), tokenSource.Token);

            if (waitTask != await Task.WhenAny(waitTask, Task.Delay(10000)))
            {
                tokenSource.Cancel();
                Console.WriteLine("Printable file list retrieval timed out!");
            }

            return ret;

        }

        public async Task<Dictionary<string, float>?> LevelBed()
        {
            if (string.IsNullOrWhiteSpace(websocket_addr) || websocket_port < 0)
            {
                Console.WriteLine("No websocket available!");
                return null;
            }

            WatsonWsClient client = new WatsonWsClient(websocket_addr, websocket_port);
            client.StartWithTimeoutAsync(10).Wait();
            Console.WriteLine("Connected to " + this.name + "!");

            await Send_Command(client, "G28");
            await Send_Command(client, "SCREWS_TILT_CALCULATE");
            Dictionary<string, float> leveling_info = new Dictionary<string, float>();

            client.MessageReceived += (sender, message) =>
                {
                    if (message.Data != null && message.Data.Count > 0)
                    {
                        string msg = Encoding.UTF8.GetString(message.Data.Array, 0, message.Data.Count);
                        if (msg.Contains("screw")) //TODO: Eventually, make this work for non-standard screw names? https://www.klipper3d.org/Manual_Level.html#adjusting-bed-leveling-screws-using-the-bed-probe
                        {
                            if (msg.Contains("front left"))
                            {
                                leveling_info["front left"] = 0f;
                                return;
                            }
                            if (msg.Contains("front right"))
                            {
                                string val = msg.Substring(msg.LastIndexOf(" : ") + 2).Trim();
                                leveling_info["front right"] = string_to_rotations(val);
                                return;
                            }
                            if (msg.Contains("rear right"))
                            {
                                string val = msg.Substring(msg.LastIndexOf(" : ") + 2).Trim();
                                leveling_info["rear right"] = string_to_rotations(val);
                                return;
                            }
                            if (msg.Contains("rear left"))
                            {
                                string val = msg.Substring(msg.LastIndexOf(" : ") + 2).Trim();
                                leveling_info["rear left"] = string_to_rotations(val);
                                return;
                            }
                            Console.WriteLine(msg);
                        }
                    }
                };


            //TODO: Figure out config file so we can make these delays customizable
            var tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;
            var waitTask = Task.Run((async () =>
            {
                while (leveling_info.Count != 4)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(5000, token);
                }
            }), tokenSource.Token);

            if (waitTask != await Task.WhenAny(waitTask, Task.Delay(30000)))
            {
                tokenSource.Cancel();
                Console.WriteLine("Bed leveling timed out!");
            }

            await client.StopAsync();

            return leveling_info;
 
        }
        
    }


}
