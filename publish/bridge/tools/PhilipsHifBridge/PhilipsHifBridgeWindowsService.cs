using System;
using System.ServiceProcess;
using System.Threading;

namespace PhilipsHifBridge
{
    internal sealed class PhilipsHifBridgeWindowsService : ServiceBase
    {
        private readonly string[] _args;
        private readonly ManualResetEvent _stopEvent = new ManualResetEvent(false);
        private Thread _workerThread;
        private Exception _startupError;

        public PhilipsHifBridgeWindowsService(string[] args)
        {
            _args = args ?? new string[0];
            ServiceName = "PhilipsHifBridge";
            CanStop = true;
            CanShutdown = true;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            RequestAdditionalTime(120000);
            _startupError = null;
            _stopEvent.Reset();

            _workerThread = new Thread(RunBridge)
            {
                IsBackground = true,
                Name = "PhilipsHifBridgeRuntime"
            };
            _workerThread.Start();

            if (!_workerThread.Join(TimeSpan.FromSeconds(30)))
                return;

            if (_startupError != null)
            {
                Program.WriteServiceStartFailure(_startupError);
                throw new InvalidOperationException("PhilipsHifBridge failed to start. See service-start.log and bridge-fatal.log", _startupError);
            }
        }

        protected override void OnStop()
        {
            StopRuntime();
        }

        protected override void OnShutdown()
        {
            StopRuntime();
        }

        private void StopRuntime()
        {
            _stopEvent.Set();
            Program.StopHttpListener();

            if (_workerThread != null && _workerThread.IsAlive)
                _workerThread.Join(TimeSpan.FromSeconds(10));
        }

        private void RunBridge()
        {
            try
            {
                Program.Run(_args, _stopEvent);
            }
            catch (Exception ex)
            {
                _startupError = ex;
                Program.WriteServiceStartFailure(ex);
            }
        }
    }
}
