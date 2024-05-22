using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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

        public HolographicClient(Guid websocket_id, SendClient send_func) : base(websocket_id, send_func)
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
            while (!token.IsCancellationRequested)
            {
                await foreach (string incoming_msg in to_self.Reader.ReadAllAsync(token))
                {
                    string[] args = incoming_msg.Split('~');
                    if (args[0].Equals("discovered_machine"))
                    {
                        Task.Run(async () => await RequestMachine(incoming_msg, token));
                        
                    }

                    if (args[0].Equals("list_machines"))
                    {
                        foreach (var machine in current_machines.Keys) {
                            await send_client(machine.Name);
                        }
                        
                    }

                    if (args[0].StartsWith("get_stats"))
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
                    }

                }
            }
        }

        private async Task<bool> RequestMachine(string machine_name, CancellationToken token)
        {
            to_server.Writer.TryWrite(machine_name);
            Machine new_machine = await shared_machines.Reader.ReadAsync(token);
            if (new_machine != null)
            {
                current_machines.Add(new_machine, 0);
                return true;
            }
            else
            {
                logger.LogWarning("Invalid machine request: " + machine_name.Split("~")[1] + " could not be found by the rancher!");
                return false;
            }
        }

    }
}
