using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using System.Collections.Immutable;

namespace MachineRancher
{
    enum PRINTER_UI_STATE
    {
        Status,
        Leveling,
        Confirmation,
        Print_Menu
    };

    [ClientTypeDescriptor("HolographicInterface")]
    internal class HolographicClient : Interface
    {
        public override string Name { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override string Description => throw new NotImplementedException();

        public override Guid Websocket_ID { get => websocket_id; set => websocket_id = value; }

        public override Channel<string> To_Interface => to_self;

        public override Channel<string> To_Rancher => to_server;

        //TODO: Turn "stat_update" messages into a function which, when called, takes deltas from the printer values when it was last called so as to only send the changed values
        public override SendClient SendClient { get => send_client; set => send_client = value; }

        public override Channel<Machine> SharedMachines => shared_machines;

        private SendClient send_client;
        private Guid websocket_id;
        private CancellationTokenSource cancellationTokenSource;

        private Channel<string> to_self = Channel.CreateUnbounded<string>();
        private Channel<string> to_server = Channel.CreateUnbounded<string>();
        private Channel<Machine> shared_machines = Channel.CreateUnbounded<Machine>();

        //Idea: Different state enums for diff type of machines, interpret int as a member of the correct enum, find correct enum by checking full type of machine. 0 is starting status for all discovered machines (ie status overlay state)
        private Dictionary<Machine, int> current_machines = new Dictionary<Machine, int>();
        private ILogger logger;

        public HolographicClient(Guid websocket_id, SendClient send_func, IConfigurationSection config) : base(websocket_id, send_func, config)
        {
            using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddConsole());
            this.logger = factory.CreateLogger<HolographicClient>();
            
        }

        /*Plan: Maintain a list of the most recent "x" number of qr codes (machines) discovered and send information required for status overlays
         *  - Hololens will send message everytime a new qr code is discovered
         *  - When logged into a specific printer, create a new "state" for that machine providing whatever a first task is (for now, we will focus on printer only)
         *      - Maintain state, so when we switch between printers we can resume where we were
         */
        protected override async Task MainLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await foreach (string incoming_msg in to_self.Reader.ReadAllAsync(token))
                    {
                        logger.LogInformation(incoming_msg);
                        string[] args = incoming_msg.Split('~');
                        switch (args[0])
                        {
                            case "discovered_machine":
                                {
                                    Machine target = this.current_machines.Keys.Where((machine) => { return machine.Name.Equals(args[1]); }).FirstOrDefault();
                                    if (target == null)
                                    {
                                        Task.Run(async () => await RequestMachine(incoming_msg, token));
                                    }
                                    else
                                    {
                                        await send_client("error~already_exists~" + args[1]);
                                    }
                                    break;
                                }

                            case "request_print":
                                {
                                    //Remark: Another spot where duplicate machine names can be problematic
                                    Machine target = this.current_machines.Keys.Where((machine) => { return machine.Name.Equals(args[1]); }).FirstOrDefault();

                                    if (target != null)
                                    {
                                        switch (target.GetType().Name)
                                        {
                                            case ("Printer"):
                                                Printer printer = (Printer)target;

                                                async void req_print()
                                                {
                                                    (bool, string) output = await printer.TryPrint(args[2]);
                                                    if (output.Item1)
                                                    {
                                                        current_machines[target] = (int)PRINTER_UI_STATE.Status;
                                                        await send_client("login_state~" + printer.Name + "~" + printer.Printer_State + "~" + current_machines[target].ToString());
                                                        await send_client("stat_update~" + printer.Name + "~" + printer.Bed_Temperature.ToString() + "~" + printer.Extruder_Temperature.ToString() + "~" + printer.Fan_Speed + "~" + printer.Printer_State);
                                                    }
                                                    else
                                                    {
                                                        await SendNotification("Unable to Print!", output.Item2);
                                                        //await send_client("notification~Unable to Print!~" + output.Item2);
                                                    }
                                                };
                                                Task.Run(req_print);
                                                break;

                                            default:
                                                await send_client("No implementation of toggle_printing for this machine type");
                                                break;

                                        }
                                    }
                                    break;
                                }

                            case "start_leveling":
                                {
                                    //Remark: Another spot where duplicate machine names can be problematic
                                    Machine target = this.current_machines.Keys.Where((machine) => { return machine.Name.Equals(args[1]); }).FirstOrDefault();

                                    if (target != null)
                                    {
                                        switch (target.GetType().Name)
                                        {
                                            case ("Printer"):
                                                //current_machines[target] = (int)PRINTER_UI_STATE.Print_Menu;
                                                Printer printer = (Printer)target;
                                                Task.Run(async () =>
                                                {
                                                    Dictionary<string, float> results = await printer.LevelBed();
                                                    if (main_token.IsCancellationRequested) //prevent zombie leveling info from being sent
                                                    {
                                                        return;
                                                    }
                                                    if (results.Keys.Count == 4)
                                                    {
                                                        await send_client("level_info~" + printer.Name + "~" + results["front left"] + "~" + results["front right"] + "~" + results["rear left"] + "~" + results["rear right"]);
                                                    }
                                                    else
                                                    {
                                                        logger.LogError("Bed leveling did not return 4 values.");
                                                    }
                                                });
                                                break;

                                            default:
                                                await send_client("No implementation of start_leveling for this machine type");
                                                break;

                                        }
                                    }
                                    break;
                                }


                            case "get_stats":
                                {
                                    //Remark: Another spot where duplicate machine names can be problematic
                                    Machine target = this.current_machines.Keys.Where((machine) => { return machine.Name.Equals(args[1]); }).FirstOrDefault();

                                    if (target != null)
                                    {
                                        switch (target.GetType().Name)
                                        {
                                            case ("Printer"):
                                                Printer printer = (Printer)target;

                                                await send_client(printer.Bed_Temperature.ToString() + "~" + printer.Extruder_Temperature.ToString() + "~" + printer.Fan_Speed);
                                                break;

                                            default:
                                                await send_client("No implementation of get_stats for this machine type");
                                                break;

                                        }
                                    }
                                    break;
                                }

                            //Interface functions
                            case "login":
                            case "advance":
                            case "reverse":
                                {
                                    //Remark: Another spot where duplicate machine names can be problematic
                                    Machine target = this.current_machines.Keys.Where((machine) => { return machine.Name.Equals(args[1]); }).FirstOrDefault();
                                    if (target != null)
                                    {
                                        switch (target.GetType().Name)
                                        {
                                            case ("Printer"):
                                                Printer printer = (Printer)target;
                                                PRINTER_UI_STATE printer_interface_state = (PRINTER_UI_STATE)this.current_machines[target];
                                                logger.LogInformation("Current state of " + printer.Name + "=" + this.current_machines[target].ToString());
                                                if (args[0].Equals("advance"))
                                                {
                                                    switch (printer_interface_state)
                                                    {
                                                        case PRINTER_UI_STATE.Status:
                                                            logger.LogInformation("Previous state: status, switching to leveling");
                                                            this.current_machines[target] = (int)PRINTER_UI_STATE.Leveling;
                                                            break;

                                                        case PRINTER_UI_STATE.Leveling:
                                                            logger.LogInformation("Previous state: leveling, switching to confirmation");
                                                            this.current_machines[target] = (int)PRINTER_UI_STATE.Confirmation;
                                                            break;

                                                        case PRINTER_UI_STATE.Confirmation:
                                                            logger.LogInformation("Previous state: confirmation, switching to menu");
                                                            this.current_machines[target] = (int)PRINTER_UI_STATE.Print_Menu;
                                                            break;

                                                        case PRINTER_UI_STATE.Print_Menu:
                                                            logger.LogInformation("Previous state: menu, switching to status");
                                                            this.current_machines[target] = (int)PRINTER_UI_STATE.Status;
                                                            break;
                                                    }
                                                }
                                                else if (args[0].Equals("reverse"))
                                                {
                                                    switch (printer_interface_state)
                                                    {
                                                        case PRINTER_UI_STATE.Leveling:
                                                            this.current_machines[target] = (int)PRINTER_UI_STATE.Status;
                                                            break;

                                                        case PRINTER_UI_STATE.Confirmation:
                                                            this.current_machines[target] = (int)PRINTER_UI_STATE.Leveling;
                                                            break;

                                                        case PRINTER_UI_STATE.Print_Menu:
                                                            this.current_machines[target] = (int)PRINTER_UI_STATE.Confirmation;
                                                            break;
                                                    }
                                                }
                                                await send_client("login_state~" + printer.Name + "~" + printer.Printer_State + "~" + this.current_machines[target].ToString());


                                                //switch (printer.printer_state)
                                                //{
                                                //    case (PrinterState.Printing):
                                                //    case (PrinterState.Standby):
                                                //    case (PrinterState.Cancelled):
                                                //    case (PrinterState.C)

                                                //}
                                                //await send_client(printer.Bed_Temperature.ToString() + "~" + printer.Extruder_Temperature.ToString() + "~" + printer.Fan_Speed);
                                                break;

                                            default:
                                                await send_client("No implementation of login/advance/reverse for this machine type");
                                                break;

                                        }
                                    }
                                    break;
                                }

                            case "toggle_printing":
                                {
                                    //Remark: Another spot where duplicate machine names can be problematic
                                    Machine target = this.current_machines.Keys.Where((machine) => { return machine.Name.Equals(args[1]); }).FirstOrDefault();

                                    if (target != null)
                                    {
                                        switch (target.GetType().Name)
                                        {
                                            case ("Printer"):
                                                Printer printer = (Printer)target;

                                                Task.Run(async () =>
                                                {
                                                    await printer.Toggle_Printing();
                                                    await send_client("stat_update~" + printer.Name + "~" + printer.Bed_Temperature.ToString() + "~" + printer.Extruder_Temperature.ToString() + "~" + printer.Fan_Speed + "~" + printer.Printer_State);
                                                });
                                                break;

                                            default:
                                                await send_client("No implementation of toggle_printing for this machine type");
                                                break;

                                        }
                                    }
                                    break;
                                }

                            case "request_digitaltwin":
                                {
                                    //Remark: Another spot where duplicate machine names can be problematic
                                    Machine target = this.current_machines.Keys.Where((machine) => { return machine.Name.Equals(args[1]); }).FirstOrDefault();

                                    if (target != null)
                                    {
                                        switch (target.GetType().Name)
                                        {
                                            case ("Printer"):
                                                Printer printer = (Printer)target;
                                                async void temp() {
                                                    await printer.refresh_digitaltwin();
                                                    string info = JsonSerializer.Serialize<PrinterDigitalTwin>(printer.digitaltwin);

                                                    logger.LogInformation("Serialized printer digital twin: " + info);
                                                    await send_client("digitaltwin~" + printer.Name + "~" + info);
                                                };
                                                Task.Run(temp);
                                                break;

                                            default:
                                                await send_client("No implementation of request_digitaltwin for this machine type");
                                                break;

                                        }
                                    }
                                    break;
                                }

                            case "upload_state":
                                {
                                    //Remark: Another spot where duplicate machine names can be problematic
                                    Machine target = this.current_machines.Keys.Where((machine) => { return machine.Name.Equals(args[1]); }).FirstOrDefault();

                                    if (target != null)
                                    {
                                        switch (target.GetType().Name)
                                        {
                                            case ("Printer"):
                                                Printer printer = (Printer)target;

                                                Task.Run(async () =>
                                                {
                                                    logger.LogInformation("Incoming upload state: " + args[2]);
                                                    await printer.UploadDigitalTwin(args[2]);
                                                    await printer.refresh_digitaltwin();

                                                    string info = JsonSerializer.Serialize<PrinterDigitalTwin>(printer.digitaltwin);
                                                    await send_client("digitaltwin~" + printer.Name + "~" + info);
                                                });
                                                break;

                                            default:
                                                await send_client("No implementation of upload_state for this machine type");
                                                break;

                                        }
                                    }
                                    break;
                                }

                            case "cancel_print":
                                {
                                    //Remark: Another spot where duplicate machine names can be problematic
                                    Machine target = this.current_machines.Keys.Where((machine) => { return machine.Name.Equals(args[1]); }).FirstOrDefault();

                                    if (target != null)
                                    {
                                        switch (target.GetType().Name)
                                        {
                                            case ("Printer"):
                                                Printer printer = (Printer)target;

                                                Task.Run(async () =>
                                                {
                                                    if (args[2] != null)
                                                    {
                                                        await printer.Cancel_Print(args[2]);
                                                    }
                                                    else
                                                    {
                                                        //Instead of await printer.Cancel_Print();
                                                        //We need to toggle the print and send a vis graph to require the user to give a "reason" before cancelling
                                                        await printer.Toggle_Printing();
                                                        printer.CancelLogging();
                                                        onPrinterFailureDetected(printer, printer.generateVisData());
                                                        //TODO: Need a way to flush the log anytime data is visualized
                                                        //TODO: Also need to handle case where print is paused then resumed,im pretty sure this breaks logging


                                                        logger.LogInformation("Attempted cancel without reason on printer " + printer.Name + ", sending vis data to request a cancellation WITH reason.");
                                                        //printer.onPrintFailureDetected?.Invoke(printer, printer.generateVisData());
                                                    }
                                                    
                                                    await send_client("stat_update~" + printer.Name + "~" + printer.Bed_Temperature.ToString() + "~" + printer.Extruder_Temperature.ToString() + "~" + printer.Fan_Speed + "~" + printer.Printer_State);
                                                });
                                                break;

                                            default:
                                                await send_client("No implementation of cancel_print for this machine type");
                                                break;

                                        }
                                    }
                                    break;
                                }

                            case "estop":
                                {
                                    //Remark: Another spot where duplicate machine names can be problematic
                                    Machine target = this.current_machines.Keys.Where((machine) => { return machine.Name.Equals(args[1]); }).FirstOrDefault();

                                    if (target != null)
                                    {
                                        switch (target.GetType().Name)
                                        {
                                            case ("Printer"):
                                                Printer printer = (Printer)target;

                                                Task.Run(async () =>
                                                {
                                                    await printer.EStop();
                                                    await send_client("stat_update~" + printer.Name + "~" + printer.Bed_Temperature.ToString() + "~" + printer.Extruder_Temperature.ToString() + "~" + printer.Fan_Speed + "~" + printer.Printer_State);
                                                });
                                                break;

                                            default:
                                                await send_client("No implementation of cancel_print for this machine type");
                                                break;

                                        }
                                    }
                                    break;
                                }

                            case "retrieve_printables":
                                {
                                    //Remark: Another spot where duplicate machine names can be problematic
                                    Machine target = this.current_machines.Keys.Where((machine) => { return machine.Name.Equals(args[1]); }).FirstOrDefault();

                                    if (target != null)
                                    {
                                        switch (target.GetType().Name)
                                        {
                                            case ("Printer"):
                                                Printer printer = (Printer)target;

                                                Task.Run(async () =>
                                                {
                                                    List<string> results = await printer.RetrievePrintables();
                                                    if (!main_token.IsCancellationRequested)
                                                    {
                                                        await send_client("available_printables~" + printer.Name + "~" + string.Join('~', results));
                                                    }
                                                });
                                                break;

                                            default:
                                                await send_client("No implementation of retrieve_printables for this machine type");
                                                break;

                                        }
                                    }
                                    break;
                                }
                        }
                    }
                }
            }
            finally
            {
                unregister_events();
                logger.LogInformation("Holographic Interface client canceled.");
            }
        }

        //REMARK: This function is where we should implement any machine specific cleanup
        private void unregister_events()
        {
            foreach (Machine machine in this.current_machines.Keys)
            {
                switch (machine.GetType().Name)
                {
                    case ("Printer"):
                        Printer printer = (Printer)machine;

                        printer.onPrintFailureDetected -= onPrinterFailureDetected;
                        break;

                    default:
                        break;
                }
            }
        }

        private async Task SendNotification(string title, string message)
        {
            await SendNotification(title, message, null);
        }

        private async Task SendNotification(string title, string message, string button_text)
        {
            string command = "notification~" + title + "~" + message;
            if (button_text != null)
            {
                command += "~" + button_text;
            }

            await SendClient(command);
        }

        private async Task<bool> RequestMachine(string machine_name, CancellationToken token)
        {
            to_server.Writer.TryWrite(machine_name);
            Machine new_machine = await shared_machines.Reader.ReadAsync(token);
            if (new_machine != null)
            {
                logger.LogInformation("Holographic Client discovered new machine: " + new_machine.Name);
                current_machines.Add(new_machine, 0);
                switch (new_machine.GetType().Name)
                {
                    case ("Printer"):
                        {
                            logger.LogInformation("Beginning sending status updates to holographic client.");
                            Printer printer = (Printer)new_machine;
                            printer.onPrintFailureDetected += onPrinterFailureDetected;
                            await send_client("machine_confirmed~" + printer.Name);
                            //Start sending status updates
                            //Remark: Main flaw here is the use of the main token, meaning that we cannot shut off status updates for individual machines unless we kill the entire client
                            Task.Run(async () =>
                            {
                                while (!main_token.IsCancellationRequested)
                                {
                                    await send_client("stat_update~" + printer.Name + "~" + printer.Bed_Temperature.ToString() + "~" + printer.Extruder_Temperature.ToString() + "~" + printer.Fan_Speed + "~" + printer.Printer_State);
                                    await Task.Delay(5000, main_token);
                                }
                            });
                            break;
                        }
                }
                return true;
            }
            else
            {
                await send_client("error~unrecognized~" + machine_name.Split("~")[1]);
                logger.LogWarning("Invalid machine request: " + machine_name.Split("~")[1] + " could not be found by the rancher!");
                return false;
            }
        }

        private async void onPrinterFailureDetected(Printer failed_machne, List<List<float>> data)
        {
            this.send_client("stat_update~" + failed_machne.Name + "~" + failed_machne.Bed_Temperature.ToString() + "~" + failed_machne.Extruder_Temperature.ToString() + "~" + failed_machne.Fan_Speed + "~" + failed_machne.Printer_State);
            int samples_per_point = Math.Clamp((data.Count / int.Parse(this.config["VisGraphNumPoints"])), 1, int.MaxValue);

            string vis_command = "vis_data~" + failed_machne.Name + "~";
            
            //TODO: NEED TO FIX: Sometimes averaging comes out weird due to remainders (ie: 10 chunks of n size, 1 chunk of n-1 size)
            foreach (List<float>[] dataset in data.Chunk(samples_per_point))
            {
                List<float> current_frame = new List<float>();
                for (int i = 0; i < dataset[0].Count; i++)
                {
                    current_frame.Add(dataset.Average(row => row[i]));
                }
                vis_command += (String.Join(',', current_frame) + "~");
            }
            vis_command = vis_command.TrimEnd('~');

            await SendNotification("Failure Detected!", "A possible failure has been detected at " + failed_machne.Name + " so the print has been paused and a visualization of recent data has been posted. Please investigate.");

            await SendClient(vis_command);

            
            
        }
    }
}
