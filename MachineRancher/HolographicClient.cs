using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MachineRancher
{
    [ClientTypeDescriptor("HolographicInterface")]
    internal class HolographicClient : Interface
    {
        public override string Name { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override string Description => throw new NotImplementedException();

        public override Guid Websocket_ID { get => websocket_id; set => websocket_id = value; }

        public override Channel<ArraySegment<Byte>> To_Interface => to_self;

        public override Channel<ArraySegment<Byte>> To_Rancher => to_server;

        public override SendClient SendClient { get => send_client; set => send_client = value; }

        private SendClient send_client;
        private Guid websocket_id;

        private Channel<ArraySegment<Byte>> to_self = Channel.CreateUnbounded<ArraySegment<Byte>>();
        private Channel<ArraySegment<Byte>> to_server = Channel.CreateUnbounded<ArraySegment<Byte>>();

        public HolographicClient()
        {

        }

        public override async Task Main()
        {
            while (true)
            {
                await foreach (ArraySegment<Byte> incoming_msg in to_self.Reader.ReadAllAsync())
                {
                    await send_client(incoming_msg);
                }
            }
        }


    }
}
