using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        private string websocket_addr;

        private int websocket_port;

        //[MonitorRegistration("Printers/*/moonraker/state/nozzle_size")]
        //public float Nozzle_Size { get => nozzle_size; set => nozzle_size = value; } //Note: we can do averaging here, or do it in the mqtt monitor. Probably more efficient to do in the monitor

        //private float nozzle_size;

        public Printer()
        {
        }

        
    }
}
