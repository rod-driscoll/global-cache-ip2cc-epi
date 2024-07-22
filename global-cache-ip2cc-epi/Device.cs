using Crestron.SimplSharp;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Queues;
using System;
using Feedback = PepperDash.Essentials.Core.Feedback;
using Thread = Crestron.SimplSharpPro.CrestronThread.Thread;

namespace global_cache_ip2cc_epi
{
    // iTach default DHCP, link local fallback: http://169.254.1.70
    // use iHelp to find on network
    public class Device : EssentialsDevice, 
        IOnline, ICommunicationMonitor, IHasFeedback, IDisposable
    {
        #region variables
        public uint LogLevel { get; set; }
        public Config config { get; private set; }

        int relayPulseTime = 200;
        int numRelays = 3;
        uint tcpBasePort = 4999;
        public FeedbackCollection<Feedback> Feedbacks { get; private set; }
        public bool[] relayState { get; private set; }

        private CTimer _pollTimer;
        private const int _pollTime = 6000;

        private readonly IBasicCommunication _coms;
        private readonly GenericQueue _commandQueue;

        #endregion variables

        public Device(string key, string name, Config config, IBasicCommunication coms) 
            : base(key, name)
        {
            Debug.Console(1, this, "Constructor starting");
            this.config = config;
            _coms = coms;

            if (config.Monitor == null)
                config.Monitor = GetDefaultMonitorConfig();

            CommunicationMonitor = new GenericCommunicationMonitor(this, coms, config.Monitor);
            //DeviceManager.AddDevice(CommunicationMonitor);
            var gather = new CommunicationGather(coms, "\x0D");

            _commandQueue = new GenericQueue(key + "-command-queue", 213, Thread.eThreadPriority.MediumPriority, 50);

            Feedbacks = new FeedbackCollection<Feedback>();
            relayState= new bool[numRelays];
            for (int i = 0; i < numRelays; i++)
                Feedbacks.Add(new BoolFeedback(String.Format("Relay {0}",i+1), () => relayState[i]));

            CrestronEnvironment.ProgramStatusEventHandler += type =>
            {
                if (type != eProgramStatusEventType.Stopping) return;
                if (_pollTimer == null) return;
                _pollTimer.Stop();
                _pollTimer.Dispose();
            };
        }

        #region methods

        private static CommunicationMonitorConfig GetDefaultMonitorConfig()
        {
            return new CommunicationMonitorConfig()
            {
                PollInterval = 30000,
                PollString = Commands.QueryRelays,
                TimeToWarning = 120000,
                TimeToError = 360000,
            };
        }

        void CommunicationMonitor_StatusChange(object sender, MonitorStatusChangeEventArgs e)
        {
            Debug.Console(0, this, "CommunicationMonitor_StatusChange: {0} - {1}", e.Status, e.Message);
        }

        public StatusMonitorBase CommunicationMonitor { get; private set; }

        public BoolFeedback IsOnline
        {
            get { return CommunicationMonitor.IsOnlineFeedback; }
        }

        public override bool CustomActivate()
        {
            Feedbacks.RegisterForConsoleUpdates(this);
            Feedbacks.FireAllFeedbacks();

            _pollTimer = new CTimer(o =>
            {
                Debug.Console(2, this, "Polling, IsOnline: {0}, Status: {1}, IsConnected: {2}, ", CommunicationMonitor.IsOnlineFeedback.BoolValue, CommunicationMonitor.Status, _coms.IsConnected);
                if (!CommunicationMonitor.IsOnlineFeedback.BoolValue)
                {
                    CommunicationMonitor.Stop();
                    CommunicationMonitor.Start();
                }
                if (!_coms.IsConnected)
                    _coms.Connect();

                _commandQueue.Enqueue(new Commands.Command
                {
                    Coms = _coms,
                    Message = Commands.QueryRelays
                });

            }, null, 5189, _pollTime);

            CommunicationMonitor.StatusChange += new EventHandler<MonitorStatusChangeEventArgs>(CommunicationMonitor_StatusChange);
            CommunicationMonitor.Start();
            if (!_coms.IsConnected)
                _coms.Connect();
            Debug.Console(1, this, "CommunicationMonitor {0} Start, IsOnline: {1}", CommunicationMonitor.Key, CommunicationMonitor.IsOnlineFeedback.BoolValue);
            var device_ = DeviceManager.GetDeviceForKey(CommunicationMonitor.Key);
            if (device_ != null)
                Debug.Console(2, this, "CommunicationMonitor key: {0}", device_.Key);
            return base.CustomActivate();
        }

        public void SendCommand(string command)
        {
            _commandQueue.Enqueue(new Commands.Command
            {
                Coms = _coms,
                Message = command,
            });
        }

        public string MakeCommand(string command, uint module, uint port, string data)
        {
            var data_ = String.IsNullOrEmpty(data) ? String.Empty : String.Format(",{0}", data);
            var msg_ = String.Format("{0},{1}:{2}{3}", command, module, port, data_);
            return msg_;
        }
        public string MakeCommand(string command, uint module, uint port)
        {
            var msg_ = MakeCommand(command, module, port, String.Empty);
            return msg_;
        }

        public void SetRelay(uint relay, bool state)
        {
            string cmd_ = MakeCommand("setstate", 1, relay, state?"1":"0"); // "setstate,1:1,1\n" --relay 1 on
            SendCommand(cmd_);
        }

        public void GetDevices()
        {
            SendCommand("getdevices");
        }
        public void GetVersion()
        {
            SendCommand("getversion");
        }
        public void GetRelayState(uint relay)
        {
            string cmd_ = MakeCommand("getstate", 1, relay); // "getstate,1:1\n" --query relay 1
            SendCommand(cmd_);
        }

        void PulseOutput(uint relay, int pulseTime)
        {
            SetRelay(relay, true);
            CTimer pulseTimer = new CTimer(new CTimerCallbackFunction((o) => SetRelay(relay, false)), pulseTime);
        }

        void DoublePulseOutput(uint relay, int pulseTime)
        {
            SetRelay(relay, true);
            CTimer pulseTimer1 = new CTimer(new CTimerCallbackFunction((o) => SetRelay(relay, false)), pulseTime * 1);
            CTimer pulseTimer2 = new CTimer(new CTimerCallbackFunction((o) => SetRelay(relay,  true)), pulseTime * 2);
            CTimer pulseTimer3 = new CTimer(new CTimerCallbackFunction((o) => SetRelay(relay, false)), pulseTime * 3);
        }

        public void Dispose()
        {
            Debug.Console(1, this, "Dispose");
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer.Dispose();
                _pollTimer = null;
            }
        }

    }
       
    #endregion methods
}

