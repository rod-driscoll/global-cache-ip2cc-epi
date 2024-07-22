using PepperDash.Core;
using PepperDash.Essentials.Core.Queues;
using System;

namespace global_cache_ip2cc_epi
{
    public static class Commands
    {
        public class Command : IQueueMessage
        {
            public IBasicCommunication Coms { get; set; }
            public string Message { get; set; }

            public void Dispatch()
            {
                if (Coms == null || String.IsNullOrEmpty(Message))
                    return;
                Coms.SendText(Message + "\x0D");
            }

            public override string ToString()
            {
                return Message;
            }
        }

        public const string QueryRelays = "getstate,1:1\rgetstate,1:2\rgetstate,1:3\r";
    }
}
