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
        public override string Websocket { get => websocket; set => websocket = value; }

        private string websocket;

        public Printer()
        {
        }

        
    }
}
