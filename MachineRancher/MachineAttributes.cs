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

    internal abstract class Machine
    {
          public abstract string Name { get; }
          public abstract string Description { get; }
    }
}
