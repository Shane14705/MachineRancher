using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Channels;
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

    public delegate Task<bool> SendClient(string message);
    /// <summary>
    /// Any interface plugins must inherit from this class to be detected. Additionally, each plugin must have a config section in appsettings.ini with the same name as the class.
    /// Classes that inherit from this must have a "ClientTypeDescriptor" attribute to be detected as well.
    /// Beyond this, you can add any fields you would like to the section, and your plugin will be provided with access to this section.
    /// </summary>
    internal abstract class Interface
    {
        public abstract string Name { get; set; }
        public abstract string Description { get; }
        public abstract Guid Websocket_ID { get; set; }

        public abstract SendClient SendClient { get; set; }
        public abstract Channel<string> To_Interface { get; }

        public abstract Channel<string> To_Rancher { get; }

        public abstract Channel<Machine> SharedMachines { get; }

        protected CancellationTokenSource CancellationTokenSource = new CancellationTokenSource();
        public CancellationToken main_token;

        protected abstract Task MainLoop(CancellationToken token);
        public Interface(Guid websocket_id, SendClient send_func)
        {
            this.Websocket_ID = websocket_id;
            this.main_token = this.CancellationTokenSource.Token;
            this.SendClient = send_func;
        }

        public void StartAsync()
        {
            Task.Run(() => this.MainLoop(this.main_token));
        }

        /// <summary>
        /// Requests a cancellation of all async operations in the current instance (including the Main loop). Can be extended to perform other cleanup on a per plugin basis.
        /// </summary>
        public void Kill()
        {
            if (CancellationTokenSource != null)
            {
                CancellationTokenSource.Cancel();
            }
        }
    }
}
