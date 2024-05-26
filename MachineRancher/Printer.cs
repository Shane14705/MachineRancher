using System;
using System.Collections.Generic;
using WatsonWebsocket;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using System.IO;

namespace MachineRancher
{
    internal struct Filament
    {
        private string material_name = String.Empty;
        private float first_layer_temp = float.NaN;
        private float printing_temp = float.NaN;
        private float bed_temp = float.NaN;
        private string color = String.Empty;
        private float required_nozzle_size = float.NaN;
        private bool needs_hardened_nozzle = false;
        private float weight = float.NaN;

        public Filament()
        {
        }

        public string Material_Name { get => material_name; set => material_name = value; }
        public float First_Layer_Temp { get => first_layer_temp; set => first_layer_temp = value; }
        public float Printing_Temp { get => printing_temp; set => printing_temp = value; }
        public float Bed_Temp { get => bed_temp; set => bed_temp = value; }
        public string Color { get => color; set => color = value; }
        public float Required_Nozzle_Size { get => required_nozzle_size; set => required_nozzle_size = value; }
        public bool Needs_Hardened_Nozzle { get => needs_hardened_nozzle; set => needs_hardened_nozzle = value; }
        public float Current_Weight { get => weight; set => weight = value; }
    }

    internal class PrinterDigitalTwin
    {
        public Filament Current_Filament { get => current_filament; set => current_filament = value; }
        private Filament current_filament;

        public float Nozzle_Size { get => nozzle_size; set => nozzle_size = (float)Math.Floor(value * 10) / 10; }
        private float nozzle_size;

        public bool Has_Hardened_Nozzle { get => has_hardened_nozzle; set => has_hardened_nozzle = value; }
        private bool has_hardened_nozzle;
    }

    public enum PrinterState
    {
        Standby,
        Printing,
        Paused,
        Complete,
        Cancelled,
        Error
    };

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

        [MonitorRegistration("Printers/*/klipper/state/print_stats/state", "value")]
        public string Printer_State { get => printer_state.ToString(); set => printer_state = (PrinterState) Enum.Parse(typeof(PrinterState), (char.ToUpper(value[0]) + value.Substring(1))); }

        

        public PrinterState printer_state;

        private string websocket_addr = string.Empty;

        private int websocket_port = -1;

        private ILogger logger;

        public PrinterDigitalTwin digitaltwin = null;

        public Printer(string name) : base(name)
        {
            using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddConsole());
            this.logger = factory.CreateLogger<Printer>();
        }

        //TODO: We can eventually have this function verify state uploads are successful by including a call to refresh_digitaltwin
        public async Task UploadDigitalTwin(string json)
        {
            WatsonWsClient client = new WatsonWsClient(websocket_addr, websocket_port);
            Random rand = new Random();
            int request_id = rand.Next(0, 9999);
        
            await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.printer_state.set\", \"params\": \"" + json + "\", \"id\": " + request_id.ToString() + "}");

        }

        public async Task Send_Command(WatsonWsClient wsclient, string command)
        {
            Random rand = new Random();
            await wsclient.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.gcode.script\",\"params\": { \"script\": \"" + command + "\"}, \"id\": " + rand.Next(0, 9999).ToString() + "}");
        }

        public async Task Toggle_Printing()
        {
            Random rand = new Random();
            WatsonWsClient client = new WatsonWsClient(websocket_addr, websocket_port);
            client.StartWithTimeoutAsync(10).Wait();
            switch (printer_state)
            {
                case PrinterState.Printing:
                    await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.print.pause\", \"id\": " + rand.Next(0, 9999).ToString() + "}");
                    break;

                case PrinterState.Paused:
                    await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.print.resume\", \"id\": " + rand.Next(0, 9999).ToString() + "}");
                    break;
            }
        }

        private void deserialize_digitaltwin(string json)
        {
            logger.LogInformation(json);
            this.digitaltwin = JsonSerializer.Deserialize<PrinterDigitalTwin>(
                JsonSerializer.Deserialize<Dictionary<string, string>>(json)["result"]
                );
        }

        public async Task<bool> refresh_digitaltwin()
        {
            WatsonWsClient client = new WatsonWsClient(websocket_addr, websocket_port);
            Random rand = new Random();
            int request_id = rand.Next(0, 9999);
            bool response_received = false;
            client.MessageReceived += (sender, message) =>
            {
                string msg = Encoding.UTF8.GetString(message.Data.Array, 0, message.Data.Count);
                if (msg.Contains("\"id\": " + request_id.ToString()))
                {
                    deserialize_digitaltwin(msg);

                    response_received = true;
                }
            };
            await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.printer_state.get\", \"id\": " + request_id.ToString() + "}");

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
                logger.LogWarning("Digital twin retrieval timed out!");
                return false;
            }
            else
            {
                return true;
            }
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
                logger.LogError("Unexpected bed level screw adjustment string: " + adjustment);
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
                logger.LogWarning("Printable file list retrieval timed out!");
            }

            return ret;

        }

        private bool currently_leveling = false;
        public async Task<Dictionary<string, float>?> LevelBed()
        {
            if (string.IsNullOrWhiteSpace(websocket_addr) || websocket_port < 0)
            {
                logger.LogCritical("No websocket available!");
                return null;
            }

            currently_leveling = true;
            WatsonWsClient client = new WatsonWsClient(websocket_addr, websocket_port);
            client.StartWithTimeoutAsync(10).Wait();
            logger.LogInformation("Connected to " + this.name + "!");

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
                logger.LogError("Bed leveling timed out!");
            }

            currently_leveling = false;
            await client.StopAsync();

            return leveling_info;
 
        }

        public async Task Cancel_Print()
        {
            Random rand = new Random();
            WatsonWsClient client = new WatsonWsClient(websocket_addr, websocket_port);
            client.StartWithTimeoutAsync(10).Wait();

            await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.print.cancel\", \"id\": " + rand.Next(0, 9999).ToString() + "}");
        }

        public async Task EStop()
        {
            Random rand = new Random();
            WatsonWsClient client = new WatsonWsClient(websocket_addr, websocket_port);
            client.StartWithTimeoutAsync(10).Wait();

            await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.emergency_stop\", \"id\": " + rand.Next(0, 9999).ToString() + "}");
        }

        struct PrintRequirements
        {
            public float nozzle_diameter { get => requested_diameter; set => requested_diameter = (float)Math.Floor(value * 10) / 10; }
            private float requested_diameter = float.NaN;

            public string filament_type { get => filament_name; set => filament_name = value; }
            private string filament_name = String.Empty;

            public float filament_weight_total { get => weight_required; set => weight_required = value; }
            private float weight_required = float.NaN;

            public PrintRequirements()
            {
            }
        }

        private async Task<PrintRequirements> GetPrintRequirements(string filename)
        {
            WatsonWsClient client = new WatsonWsClient(websocket_addr, websocket_port);
            Random rand = new Random();
            int request_id = rand.Next(0, 9999);
            bool response_received = false;
            PrintRequirements new_reqs = new();
            client.MessageReceived += (sender, message) =>
            {
                string msg = Encoding.UTF8.GetString(message.Data.Array, 0, message.Data.Count);
                if (msg.Contains("\"id\": " + request_id.ToString()))
                {
                    logger.LogInformation(msg);
                    new_reqs = JsonSerializer.Deserialize<PrintRequirements>(
                        JsonSerializer.Deserialize<Dictionary<string, string>>(msg)["result"]
                        );

                    response_received = true;
                }
            };
            await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"server.files.metascan\", \"params\" : { \"filename\": \"" + filename + "\"}, \"id\": " + request_id.ToString() + "}");

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
                logger.LogWarning("Print requirements retrieval timed out!");
            }
            
            return new_reqs;
        }

        private (bool, string) check_print_compat(PrintRequirements reqs, PrinterDigitalTwin digitaltwin)
        {
            (bool, string) result = (false, string.Empty);
            List<string> conflicts = new List<string>();

            if (reqs.nozzle_diameter != float.NaN && digitaltwin.Nozzle_Size != float.NaN) 
            {
                if (reqs.nozzle_diameter != digitaltwin.Nozzle_Size)
                {
                    conflicts.Add("Nozzle Diameter Mismatch: Print requires " + reqs.nozzle_diameter.ToString() + " vs Current Size: " + digitaltwin.Nozzle_Size.ToString());
                }
            }

            if (reqs.filament_type != String.Empty && digitaltwin.Current_Filament.Material_Name != String.Empty)
            {
                if (reqs.filament_type != String.Empty && reqs.filament_type != digitaltwin.Current_Filament.Material_Name)
                {
                    conflicts.Add("Filament Type Mismatch: Print requires " + reqs.filament_type + " vs Current Type: " + digitaltwin.Current_Filament.Material_Name);
                }
            }
            if (reqs.filament_weight_total != float.NaN && digitaltwin.Current_Filament.Current_Weight != float.NaN)
            {
                if (reqs.filament_weight_total > digitaltwin.Current_Filament.Current_Weight)
                {
                    conflicts.Add("Not Enough Filament: Print requires " + reqs.filament_weight_total + "kg vs Current Amount: " + digitaltwin.Current_Filament.Current_Weight + "kg");
                }
            }

            if (conflicts.Count == 0)
            {
                result.Item1 = true;
                return result;
            }
            else
            {
                result.Item1 = false;
                result.Item2 = string.Join("\n", conflicts);
                return result;
            }
            

        }

        public async Task<(bool, string)> TryPrint(string filename)
        {
            //TODO HERE: DO PRINT COMPATIBILITY CHECK!
            PrintRequirements reqs = await GetPrintRequirements(filename);
            await refresh_digitaltwin();

            (bool, string) result = check_print_compat(reqs, digitaltwin);

            if (result.Item1)
            {
                Random rand = new Random();
                WatsonWsClient client = new WatsonWsClient(websocket_addr, websocket_port);
                client.StartWithTimeoutAsync(10).Wait();

                await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.print.start\",\"params\": { \"filename\": \"" + filename + "\"}, \"id\": " + rand.Next(0, 9999).ToString() + "}");
            }

            return result;
        }
    }


}
