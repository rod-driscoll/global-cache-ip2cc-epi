using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.CrestronIO;
using System;

namespace global_cache_ip2cc_epi
{
    public class Ip2ccRelay : EssentialsDevice, ISwitchedOutput
    {
        public Device Parent { get; private set; }
        public BoolFeedback OutputIsOnFeedback { get; private set; }
        public bool State { get; private set; }

        public uint Index { get; private set; }

        public Ip2ccRelay(Device parent, uint index, string key, string name) : base(key, name)
        {
            this.Parent = parent;
            this.Index = index;
            OutputIsOnFeedback = new BoolFeedback(String.Format("Relay {0}", index), () => State);
        
           }

        public void Off()
        {
            Parent.SetRelay(Index, false);
        }

        public void On()
        {
            Parent.SetRelay(Index, true);
        }

        public void SetFeedback(bool value)
        {
            if(value != State)
            {
                State = value;
                OutputIsOnFeedback.FireUpdate();
            }
        }
    }
}
