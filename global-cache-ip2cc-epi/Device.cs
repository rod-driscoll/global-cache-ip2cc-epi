using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.CrestronIO;
using PepperDash.Essentials.Core.Queues;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Feedback = PepperDash.Essentials.Core.Feedback;
using Thread = Crestron.SimplSharpPro.CrestronThread.Thread;

namespace global_cache_ip2cc_epi
{
    // iTach default DHCP, link local fallback: http://169.254.1.70
    // use iHelp to find on network
    public class Device : EssentialsDevice, ISwitchedOutputCollection,
        IOnline, ICommunicationMonitor, IHasFeedback, IDisposable
    {
        #region variables
        public uint LogLevel { get; set; }
        public Config config { get; private set; }
        int numRelays = 3;
        //int tcpBasePort = 4998; //e.g. getdevices\r getstates\r setstate\r

        public Dictionary<uint, ISwitchedOutput> SwitchedOutputs { get; private set; }

        private CTimer _pollTimer;
        private const int _pollTime = 60000;

        public FeedbackCollection<Feedback> Feedbacks { get; private set; } 
        public BoolFeedback IsOnline
        {
            get { return CommunicationMonitor.IsOnlineFeedback; }
        }
        public StatusMonitorBase CommunicationMonitor { get; private set; }
        private readonly IBasicCommunication _coms;
        private readonly GenericQueue _commandQueue;

        #endregion variables

        public Device(string key, string name, Config config, IBasicCommunication coms) 
            : base(key, name)
        {
            Debug.LogMessage(LogEventLevel.Debug, this, "Constructor starting");
            _coms = coms; 
            this.config = config;
            //this.config.Control.TcpSshProperties.Port = this.config.Control.TcpSshProperties.Port == 0 ? tcpBasePort : this.config.Control.TcpSshProperties.Port; -- this won't work, need to set port_ in coms on creation
            this.config.PulseTime = this.config.PulseTime == 0 ? 200 : this.config.PulseTime;
            if (this.config.Monitor == null)
                this.config.Monitor = GetDefaultMonitorConfig();

            CommunicationMonitor = new GenericCommunicationMonitor(this, _coms, this.config.Monitor);
            var gather = new CommunicationGather(_coms, "\x0D");
            new StringResponseProcessor(gather, s => { ProcessResponse(s); });
            _commandQueue = new GenericQueue(key + "-command_-queue", 213, Thread.eThreadPriority.MediumPriority, 50);

            Feedbacks = new FeedbackCollection<Feedback>();
            SwitchedOutputs = new Dictionary<uint, ISwitchedOutput>();
            for (uint i = 1; i <= numRelays; i++)
            {
                var relay_ = new Ip2ccRelay(this, (uint)i, String.Format("{0}--relay-{1}", this.Key, i), String.Format("ip2cc-relay-{0}", i));
                SwitchedOutputs.Add(i, relay_);
                Debug.LogMessage(LogEventLevel.Debug, this, "created device {0}", relay_.Key);
                DeviceManager.AddDevice(relay_);
                Feedbacks.Add(relay_.OutputIsOnFeedback);
            }

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
            Debug.LogMessage(LogEventLevel.Verbose, this, "CommunicationMonitor_StatusChange: {0} - {1}", e.Status, e.Message);
        }

        public override bool CustomActivate()
        {
            Feedbacks.RegisterForConsoleUpdates(this);
            Feedbacks.FireAllFeedbacks();

            _pollTimer = new CTimer(o =>
            {
                Debug.LogMessage(LogEventLevel.Debug, this, "Polling, IsOnline: {0}, Status: {1}, IsConnected: {2}, ", CommunicationMonitor.IsOnlineFeedback.BoolValue, CommunicationMonitor.Status, _coms.IsConnected);
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
            Debug.LogMessage(LogEventLevel.Debug, this, "CommunicationMonitor {0} Start, IsOnline: {1}", CommunicationMonitor.Key, CommunicationMonitor.IsOnlineFeedback.BoolValue);
            var device_ = DeviceManager.GetDeviceForKey(CommunicationMonitor.Key);
            if (device_ != null)
                Debug.LogMessage(LogEventLevel.Information, this, "CommunicationMonitor key: {0}", device_.Key);
            return base.CustomActivate();
        }

        private void ProcessResponse(string response)
        {
            Debug.LogMessage(LogEventLevel.Debug, this, "ParseRx: {0}", response);
            string pattern_ = @"^(\w+),(\d+):(\d+),(\d+)";
            Regex regex_ = new Regex(pattern_);
            Match match_ = regex_.Match(response);
            if (match_.Success)
            {
                string command_ = match_.Groups[1].Value;
                string module_ = match_.Groups[2].Value;
                string port_ = match_.Groups[3].Value;
                string parameter_ = match_.Groups[4].Value;
                Console.WriteLine($"command_: {command_}");
                Console.WriteLine($"Numbers: {module_}, {port_}, {parameter_}");
                if(module_ == "1")
                {
                    if(command_.EndsWith("state")) // "state" or "setstate"
                    {
                        uint index_ = Convert.ToUInt32(port_);
                        if (SwitchedOutputs.ContainsKey(index_))
                        {
                            var relay_ = SwitchedOutputs[index_] as Ip2ccRelay;
                            if (relay_ != null)
                            {
                                relay_.SetFeedback((parameter_.Equals('1')));
                            }
                        }
                    }
                }
            }
            else
                Debug.LogMessage(LogEventLevel.Information, this, "Unknown response");
        }

        public void SendCommand(string command)
        {
            Debug.LogMessage(LogEventLevel.Debug, this, "SendCommand({0})", command);
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
            //Debug.LogMessage(LogEventLevel.Debug, this, "SetRelay({0},{1}) Tx: {2}", relay, state, cmd_);
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
            Debug.LogMessage(LogEventLevel.Debug, this, "PulseOutput({0})", relay);
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
            Debug.LogMessage(LogEventLevel.Debug, this, "Dispose");
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

