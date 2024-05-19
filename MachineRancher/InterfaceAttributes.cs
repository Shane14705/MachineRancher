using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MachineRancher
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ClientTypeDescriptorAttribute : Attribute
    {
        public string value;
        static int count = 0;

        /// <summary>
        /// Tells the rancher what it's websocket should listen for in order to find new interfaces of this type.
        /// 
        /// </summary>
        /// <param name="value">Device Type Descriptor</param>
        public ClientTypeDescriptorAttribute(string value)
        {
            this.value = value;
        }

    }

    /// <summary>
    /// Any interface plugins must inherit from this class to be detected. Additionally, each plugin must have a config section in appsettings.ini with the same name as the class.
    /// Classes that inherit from this must have a "ClientTypeDescriptor" attribute to be detected as well.
    /// Beyond this, you can add any fields you would like to the section, and your plugin will be provided with access to this section.
    /// </summary>
    internal abstract class Interface
    {
        public abstract string Name { get; set; }
        public abstract string Description { get; }
        public abstract string Websocket { get; set; }
    }
}
