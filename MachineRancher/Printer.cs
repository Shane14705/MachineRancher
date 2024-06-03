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
using Microsoft.Extensions.Configuration;

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

    //TODO: TURN ALL WEBSOCKET USES IN HERE TO REFERENCE ONE WEBSOCKET INSTANCE THAT ONLY GETS CLOSED OR OPENED WHEN NO OPERATIONS ARE USING IT

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

                return websocket.Host.ToString() + ":" + websocket.Port.ToString();
            }
            set
            {
                websocket = new Uri("ws://" + value + "/websocket");
            }
        }

        //TODO: REPLACE PRINTER STATE WITH THIS https://moonraker.readthedocs.io/en/latest/printer_objects/#idle_timeout FOR DETERMINING WHICH UI TO SHOW

        [MonitorRegistration("Printers/*/klipper/state/print_stats/state", "value")]
        public string Printer_State { get => printer_state.ToString(); set => printer_state = (PrinterState) Enum.Parse(typeof(PrinterState), (char.ToUpper(value[0]) + value.Substring(1))); }

        

        public PrinterState printer_state;

        private Uri websocket = null;

        private ILogger logger;

        public PrinterDigitalTwin digitaltwin = null;

        private string current_logfile = String.Empty;

        public delegate void handleFailureDetection(Printer failed_machne, List<List<float>> sample_frames);
        public event handleFailureDetection onPrintFailureDetected;

        public Printer(string name, IConfigurationSection config) : base(name, config)
        {
            using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddConsole());
            this.logger = factory.CreateLogger<Printer>();
        }

        //TODO: We can eventually have this function verify state uploads are successful by including a call to refresh_digitaltwin
        public async Task UploadDigitalTwin(string json)
        {
            WatsonWsClient client = new WatsonWsClient(this.websocket);
            client.StartWithTimeoutAsync(int.Parse(this.config["MoonrakerConnectionTimeoutSeconds"])).Wait();
            Random rand = new Random();
            int request_id = rand.Next(0, 9999);
        
            await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.printer_state.set\", \"params\": \"" + json + "\", \"id\": " + request_id.ToString() + "}");

            await client.StopAsync();
        }

        public async Task Send_Command(WatsonWsClient wsclient, string command)
        {
            Random rand = new Random();
            await wsclient.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.gcode.script\",\"params\": { \"script\": \"" + command + "\"}, \"id\": " + rand.Next(0, 9999).ToString() + "}");
        }

        public async Task Toggle_Printing()
        {
            Random rand = new Random();
            WatsonWsClient client = new WatsonWsClient(this.websocket);
            client.StartWithTimeoutAsync(int.Parse(this.config["MoonrakerConnectionTimeoutSeconds"])).Wait();
            switch (printer_state)
            {
                case PrinterState.Printing:
                    if (logging_token != null)
                    {
                        logging_token.Cancel();
                        logging_token = null;
                    }
                    await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.print.pause\", \"id\": " + rand.Next(0, 9999).ToString() + "}");

                    break;

                case PrinterState.Paused:
                    if (logging_token != null)
                    {
                        logging_token.Cancel();
                        logging_token = null;
                    }
                    
                    if (current_logfile != null)
                    {
                        logging_token = new CancellationTokenSource();
                        await refresh_digitaltwin();
                        isHeated = !(Bed_Temperature < digitaltwin.Current_Filament.Bed_Temp || Extruder_Temperature < digitaltwin.Current_Filament.Printing_Temp);
                        Task.Run(async () => await log_machine(logging_token.Token, current_logfile));
                    }
                    
                    await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.print.resume\", \"id\": " + rand.Next(0, 9999).ToString() + "}");
                    break;
            }

            await client.StopAsync();
        }

        private void deserialize_digitaltwin(string json)
        {
            //logger.LogInformation(json);
            //logger.LogInformation(JsonSerializer.Deserialize<Dictionary<string, object>>(json)["result"].ToString());
            this.digitaltwin = JsonSerializer.Deserialize<PrinterDigitalTwin>(
                JsonSerializer.Deserialize<Dictionary<string, object>>(json)["result"].ToString()
                );
        }

        public async Task<bool> refresh_digitaltwin()
        {
            WatsonWsClient client = new WatsonWsClient(this.websocket);
            client.StartWithTimeoutAsync(int.Parse(this.config["MoonrakerConnectionTimeoutSeconds"])).Wait();
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

            if (waitTask != await Task.WhenAny(waitTask, Task.Delay(int.Parse(this.config["DigitalTwinRefreshTimeout"]))))
            {
                tokenSource.Cancel();
                logger.LogWarning("Digital twin retrieval timed out!");
                await client.StopAsync();
                return false;
            }
            else
            {
                await client.StopAsync();
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
            string[] values = adjustment.Substring(0, adjustment.Length - 3).Split(' ');
            if (values.Length != 3)
            {
                logger.LogError("Unexpected bed level screw adjustment string: " + adjustment);
                return 0;
            }

            float output = 0;
            //logger.LogInformation(adjustment);
            string[] temp = values[2].Split(':');
            output += float.Parse(temp[0]);

            output += (float.Parse(temp[1]) / 60);

            if (adjustment[1].Equals("CCW"))
            {
                output *= -1;
            }

            return output;
            
        }

        public async Task<List<string>> RetrievePrintables()
        {
            Regex regex = new Regex("\"path\": \"([^\"]+)");
            bool response_received = false;
            List<string> ret = new List<string>();
            WatsonWsClient client = new WatsonWsClient(this.websocket);
            client.StartWithTimeoutAsync(int.Parse(this.config["MoonrakerConnectionTimeoutSeconds"])).Wait();
            Random rand = new Random();
            int request_id = rand.Next(0, 9999);
            client.MessageReceived += (sender, message) =>
            {
                string msg = Encoding.UTF8.GetString(message.Data.Array, 0, message.Data.Count);
                if (msg.Contains("\"id\": " + request_id.ToString()))
                {
                    foreach (Match match in regex.Matches(msg))
                    {
                        ret.Add(match.Groups[0].Value.Split(": \"")[1]);
                    }
                    response_received = true;
                }
            };
            await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"server.files.list\",\"params\": { \"root\": \"" + "gcodes" + "\"}, \"id\": " + request_id.ToString() + "}");

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

            if (waitTask != await Task.WhenAny(waitTask, Task.Delay(int.Parse(this.config["RetrieveAvailablePrintsTimeout"]))))
            {
                tokenSource.Cancel();
                logger.LogWarning("Printable file list retrieval timed out!");
            }

            await client.StopAsync();
            return ret;

        }

        private bool currently_leveling = false;
        public async Task<Dictionary<string, float>?> LevelBed()
        {
            if (websocket == null)
            {
                logger.LogCritical("No websocket available!");
                return null;
            }

            currently_leveling = true;
            WatsonWsClient client = new WatsonWsClient(this.websocket);
            
            Dictionary<string, float> leveling_info = new Dictionary<string, float>();
            void handler(object? sender, MessageReceivedEventArgs message)
            {
                //logger.LogInformation("message received!");
                if (message.Data != null && message.Data.Count > 0)
                {
                    string msg = Encoding.UTF8.GetString(message.Data.Array, 0, message.Data.Count);
                    //logger.LogInformation(msg);
                    if (msg.Contains("screw")) //TODO: Eventually, make this work for non-standard screw names? https://www.klipper3d.org/Manual_Level.html#adjusting-bed-leveling-screws-using-the-bed-probe
                    {
                        if (msg.Contains("front left"))
                        {
                            leveling_info["front left"] = 0f;

                        }
                        if (msg.Contains("front right"))
                        {
                            string val = msg.Substring(msg.LastIndexOf(" : ") + 2).Trim();
                            leveling_info["front right"] = string_to_rotations(val);

                        }
                        if (msg.Contains("rear right"))
                        {
                            string val = msg.Substring(msg.LastIndexOf(" : ") + 2).Trim();
                            leveling_info["rear right"] = string_to_rotations(val);

                        }
                        if (msg.Contains("rear left"))
                        {
                            string val = msg.Substring(msg.LastIndexOf(" : ") + 2).Trim();
                            leveling_info["rear left"] = string_to_rotations(val);

                        }
                    }
                }
            };
            client.MessageReceived += handler;
                

            client.StartWithTimeoutAsync(int.Parse(this.config["MoonrakerConnectionTimeoutSeconds"])).Wait();
            //logger.LogInformation("Connected to " + this.name + "!");

            await Send_Command(client, "G28");
            await Send_Command(client, "SCREWS_TILT_CALCULATE");

            var tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;
            var waitTask = Task.Run((async () =>
            {
                while (!token.IsCancellationRequested && leveling_info.Count != 4)
                {
                    await Task.Delay(1000, token);
                }
            }), tokenSource.Token);

            if (waitTask != await Task.WhenAny(waitTask, Task.Delay(int.Parse(this.config["BedLevelingResultsTimeout"]))))
            {
                tokenSource.Cancel();
                logger.LogError("Bed leveling timed out!");
            }

            currently_leveling = false;
            client.MessageReceived -= handler;
            await client.StopAsync();

            return leveling_info;
 
        }

        public async Task Cancel_Print()
        {
            Random rand = new Random();
            WatsonWsClient client = new WatsonWsClient(this.websocket);
            client.StartWithTimeoutAsync(int.Parse(this.config["MoonrakerConnectionTimeoutSeconds"])).Wait();

            await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.print.cancel\", \"id\": " + rand.Next(0, 9999).ToString() + "}");

            await client.StopAsync();
            if (logging_token != null)
            {
                logging_token.Cancel();
                logging_token = null;
                current_logfile = String.Empty;
            }
        }

        public async Task EStop()
        {
            logger.LogWarning("Estop triggered on printer " + this.name + "!");
            Random rand = new Random();
            WatsonWsClient client = new WatsonWsClient(this.websocket);
            client.StartWithTimeoutAsync(int.Parse(this.config["MoonrakerConnectionTimeoutSeconds"])).Wait();

            await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.emergency_stop\", \"id\": " + rand.Next(0, 9999).ToString() + "}");

            await client.StopAsync();
            if (logging_token != null)
            {
                logging_token.Cancel();
                logging_token = null;
                current_logfile = String.Empty;
            }
        }

        struct PrintRequirements
        {
            public float nozzle_diameter { get => requested_diameter; set => requested_diameter = (float)Math.Floor(value * 10) / 10; }
            private float requested_diameter = 0;

            public string filament_type { get => filament_name; set => filament_name = value; }
            private string filament_name = String.Empty;

            public float filament_weight_total { get => weight_required; set => weight_required = value; }
            private float weight_required = 0;

            public PrintRequirements()
            {
            }
        }

        private async Task<PrintRequirements> GetPrintRequirements(string filename)
        {
            WatsonWsClient client = new WatsonWsClient(this.websocket);
            client.StartWithTimeoutAsync(int.Parse(this.config["MoonrakerConnectionTimeoutSeconds"])).Wait();
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
                        JsonSerializer.Deserialize<Dictionary<string, object>>(msg)["result"].ToString()
                        );

                    response_received = true;
                }
            };
            await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"server.files.metascan\", \"params\" : { \"filename\": \"" + filename + "\"}, \"id\": " + request_id.ToString() + "}");

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

            if (waitTask != await Task.WhenAny(waitTask, Task.Delay(int.Parse(this.config["RetrievePrintRequirementsTimeout"]))))
            {
                tokenSource.Cancel();
                logger.LogWarning("Print requirements retrieval timed out!");
            }

            await client.StopAsync();
            
            return new_reqs;
        }

        private (bool, string) check_print_compat(PrintRequirements reqs, PrinterDigitalTwin digitaltwin)
        {
            (bool, string) result = (false, string.Empty);
            List<string> conflicts = new List<string>();

            if (reqs.nozzle_diameter != float.NaN && digitaltwin.Nozzle_Size != 0) 
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
            if (reqs.filament_weight_total != float.NaN && digitaltwin.Current_Filament.Current_Weight != 0)
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

        private CancellationTokenSource logging_token = null;
        private bool isHeated = false;
        public async Task<(bool, string)> TryPrint(string filename)
        {
            PrintRequirements reqs = await GetPrintRequirements(filename);
            await refresh_digitaltwin();

            (bool, string) result = check_print_compat(reqs, digitaltwin);

            if (result.Item1)
            {
                Random rand = new Random();
                WatsonWsClient client = new WatsonWsClient(this.websocket);
                client.StartWithTimeoutAsync(int.Parse(this.config["MoonrakerConnectionTimeoutSeconds"])).Wait();

                await client.SendAsync("{ \"jsonrpc\": \"2.0\", \"method\":\"printer.print.start\",\"params\": { \"filename\": \"" + filename + "\"}, \"id\": " + rand.Next(0, 9999).ToString() + "}");
                this.printer_state = PrinterState.Printing;

                if (int.Parse(this.config["LoggingFrequency"]) > 0)
                {
                    if (logging_token != null)
                    {
                        logging_token.Cancel();
                        current_logfile = String.Empty;
                    }
                    logging_token = new CancellationTokenSource();
                    current_logfile = filename.Substring(0, filename.Length - 6) + "_" + DateTime.Now.ToString("MM_dd_yyyy_HH_mm_ss") + ".csv";

                    isHeated = !(Bed_Temperature < digitaltwin.Current_Filament.Bed_Temp || Extruder_Temperature < digitaltwin.Current_Filament.Printing_Temp);
                    Task.Run(async () => await log_machine(logging_token.Token, current_logfile));
                }
                
                await client.StopAsync();
            }

            return result;
        }

        private async Task log_machine(CancellationToken token, string filename)
        {
            await refresh_digitaltwin();
            bool failureDetected = false;
            using (StreamWriter log = File.AppendText(Path.Combine(this.config["LogFolderPath"], filename)))
            {
                //log.WriteLine("TIME,BED TEMP,EXTRUDER TEMP,FAN SPEED");
                while (!token.IsCancellationRequested && this.printer_state == PrinterState.Printing)
                {
                    log.WriteLine(DateTime.Now.ToString("MM_dd_yyyy_HH_mm_ss") + "," + this.Bed_Temperature + "," + this.Extruder_Temperature + "," + this.Fan_Speed);

                    if (!isHeated && (this.Extruder_Temperature >= digitaltwin.Current_Filament.Printing_Temp) && (this.Bed_Temperature >= digitaltwin.Current_Filament.Bed_Temp))
                    {
                        isHeated = true;
                    }

                    if (isHeated)
                    {
                        if ((Math.Abs(digitaltwin.Current_Filament.Bed_Temp - this.Bed_Temperature) > int.Parse(this.config["TemperatureNominalFluctuationDegrees"])) ||
                            (Math.Abs(digitaltwin.Current_Filament.Printing_Temp - this.Extruder_Temperature) > int.Parse(this.config["TemperatureNominalFluctuationDegrees"])))
                        {
                            //ERROR DETECTED, WE KNOW PRINTER MUST NOT BE PAUSED SINCE WE DONT LOG DURING PAUSE. SO WE PAUSE PRINT AND ALERT USER
                            await Toggle_Printing();
                            failureDetected = true;
                            await log.FlushAsync();
                            break;
                        }
                    }
                    await Task.Delay(int.Parse(this.config["LoggingFrequency"]), token);
                }
            }

            if (failureDetected)
            {
                var samples_to_average = File.ReadLines(Path.Combine(this.config["LogFolderPath"], filename)).TakeLast(
                    (int) Math.Floor(
                        (int.Parse(this.config["SampleMinutesOnFailure"]) * (60000 / (float.Parse(this.config["LoggingFrequency"]))))
                    )
                );

                List<List<float>> rows = new List<List<float>>();
                foreach ( var row in samples_to_average )
                {
                    var temp = row.Split(',');
                    List<float> new_row = new List<float>();
                    foreach ( var column in temp )
                    {
                        float output;
                        if (float.TryParse(column, out output))
                        {
                            new_row.Add(output);
                        }
                    }
                    rows.Add(new_row);
                }

                onPrintFailureDetected?.Invoke(this, rows);
            }
        }
            
    }
}
