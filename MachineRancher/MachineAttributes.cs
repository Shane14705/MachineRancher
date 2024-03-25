using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MachineRancher
{
    [AttributeUsage(AttributeTargets.Property)]
    public class MonitorRegistrationAttribute : Attribute
    {
        public string topic;
        public int samples_to_average;

        /// <summary>
        /// Registers a property to be updated via MQTT. (The type, ie: whether or not we are dealing with an array, is found by the property's type.
        /// </summary>
        /// <param name="topic">The topic this property is to be set using. Use a '*' in places where the machine's name would be.</param>
        /// <param name="samples_to_average">The number of samples to buffer and average before updating the property. Defaults to no buffering.</param>
        public MonitorRegistrationAttribute(string topic, int samples_to_average=1)
        {
            this.topic = topic;
            this.samples_to_average = samples_to_average;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class MachineFunctionAttribute : Attribute //Eventually, this could have information added to it to show what and how this functionality should be displayed to the client, what parameters it accepts, etc
        //For now, it is just a placeholder until we get the software defined interfaces all the way down
    {

    }

    /// <summary>
    /// Any machine plugins must inherit from this class to be detected. Additionally, each plugin must have a config section in appsettings.ini with the same name as the class.
    /// This config must define a "DiscoveryTopic" field, which is the MQTT topic which the monitor will subscribe to in order to detect new instances of this given machine.
    /// Beyond this, you can add any fields you would like to the section, and your plugin will be provided with access to this section.
    /// </summary>
    internal abstract class Machine
    {
        //The topic for which the application should look for new devices to be published under
        static public string DiscoveryTopic { get; set; }
        
        public abstract string Name { get; }  
        public abstract string Description { get; }
    }
}
