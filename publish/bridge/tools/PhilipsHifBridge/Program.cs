using System;
using System.IO;
using System.Net;
using System.ServiceProcess;
using System.Web.Script.Serialization;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading;
using Philips.HIF.Contracts;

namespace PhilipsHifBridge
{
    internal static class Program
    {
        private static readonly BridgeLogger Logger = new BridgeLogger();
        private static readonly PatientStore PatientStore = new PatientStore(Logger);
        private static readonly HifBridgeState State = new HifBridgeState(Logger, PatientStore);
        private static readonly object RuntimeSync = new object();
        private static HttpListener CurrentListener;

        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            try
            {
                if (HasArg(args, "--service"))
                    ServiceBase.Run(new PhilipsHifBridgeWindowsService(args));
                else
                    Run(args, null);
            }
            catch (Exception ex)
            {
                WriteFatal(ex);
                Logger.Error("[FATAL] " + ex);
                Logger.Error("Fatal log written to: " + GetFatalLogPath());
                Environment.ExitCode = -1;
            }
        }

        internal static void Run(string[] args, WaitHandle stopHandle)
        {
            var isService = stopHandle != null;
            if (isService)
                InitServiceStartLog();

            try
            {
                Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
                LogStartup(isService, "BaseDirectory=" + AppDomain.CurrentDomain.BaseDirectory);
                LogStartup(isService, "Args=" + string.Join(" ", args ?? new string[0]));

                var fileConfig = LoadServiceConfigFile();
                var tcpDefault = fileConfig.ContainsKey("tcp") ? fileConfig["tcp"] : "net.tcp://localhost:9912/";
                var httpDefault = fileConfig.ContainsKey("http") ? fileConfig["http"] : "http://localhost:5080/";
                var tcpBase = GetArg(args, "--tcp", tcpDefault);
                var httpPrefix = GetArg(args, "--http", httpDefault);
                LogStartup(isService, "tcp=" + tcpBase);
                LogStartup(isService, "http=" + httpPrefix);

                var sqlConnection = ResolveSqlConnection(args);
                LogStartup(isService, "SQL configured=" + (!string.IsNullOrWhiteSpace(sqlConnection)));

                PatientStore.Configure(sqlConnection);
                LogStartup(isService, "PatientStore configured: " + PatientStore.SafeStoreDescription);

                State.LoadPersistedPatients();
                LogStartup(isService, "Persisted patients loaded");

                HifPpiDuplexService.Initialize(State, Logger);

                using (var host = new ServiceHost(typeof(HifPpiDuplexService), new Uri(tcpBase)))
                {
                    var binding = CreatePhilipsTcpBinding();
                    host.AddServiceEndpoint(
                        typeof(IPIDuplexService),
                        binding,
                        "Philips.HIF.Services.PpisServiceDuplex/Philips.HIF.Contracts.IPIDuplexService");
                    host.AddServiceEndpoint(
                        typeof(IPatientIdentity),
                        binding,
                        "Philips.HIF.Services.PpisService/Philips.HIF.Contracts.IPatientIdentity");

                    LogStartup(isService, "Opening WCF host...");
                    host.Open();
                    LogStartup(isService, "WCF host open OK");
                    Logger.Info("[HIF] PhilipsHifBridge listening:");
                    foreach (var endpoint in host.Description.Endpoints)
                        Logger.Info("  " + endpoint.Address.Uri);

                    LogStartup(isService, "Starting HTTP listener on " + httpPrefix);
                    var httpThread = new Thread(() => RunHttp(httpPrefix));
                    httpThread.IsBackground = true;
                    httpThread.Start();
                    LogStartup(isService, "HTTP listener thread started");

                    Logger.Info(string.Format("[HTTP] POST ADT XML/HL7 to {0}adt", httpPrefix));
                    Logger.Info(string.Format("[LOG] Bridge log file: {0}", Logger.LogPath));
                    if (stopHandle == null)
                    {
                        Console.WriteLine("Press ENTER to stop.");
                        Console.ReadLine();
                    }
                    else
                    {
                        Logger.Info("[SERVICE] PhilipsHifBridge service mode started");
                        LogStartup(true, "Service mode running");
                        stopHandle.WaitOne();
                        Logger.Info("[SERVICE] Stop requested");
                        LogStartup(true, "Stop requested");
                    }

                    StopHttpListener();
                    host.Close();
                    Logger.Info("[HIF] PhilipsHifBridge stopped");
                }
            }
            catch (Exception ex)
            {
                LogStartup(isService, "STARTUP FAILED: " + ex);
                if (isService)
                    WriteServiceStartFailure(ex);
                throw;
            }
        }

        private static Binding CreatePhilipsTcpBinding()
        {
            var reliable = new ReliableSessionBindingElement(true);
            reliable.InactivityTimeout = TimeSpan.FromMinutes(10);
            reliable.Ordered = true;
            reliable.MaxPendingChannels = 16384;

            var encoding = new BinaryMessageEncodingBindingElement();
            encoding.ReaderQuotas.MaxDepth = int.MaxValue;
            encoding.ReaderQuotas.MaxStringContentLength = int.MaxValue;
            encoding.ReaderQuotas.MaxArrayLength = int.MaxValue;
            encoding.ReaderQuotas.MaxBytesPerRead = int.MaxValue;
            encoding.ReaderQuotas.MaxNameTableCharCount = int.MaxValue;

            var transport = new TcpTransportBindingElement();
            transport.MaxBufferPoolSize = 524288;
            transport.MaxReceivedMessageSize = int.MaxValue;
            transport.MaxBufferSize = int.MaxValue;
            transport.ConnectionPoolSettings.MaxOutboundConnectionsPerEndpoint = 10000;

            var binding = new CustomBinding(
                new TransactionFlowBindingElement(),
                reliable,
                encoding,
                transport);
            binding.CloseTimeout = TimeSpan.FromSeconds(2);
            binding.OpenTimeout = TimeSpan.FromSeconds(8);
            binding.ReceiveTimeout = TimeSpan.MaxValue;
            binding.SendTimeout = TimeSpan.FromMinutes(6);
            return binding;
        }

        private static void RunHttp(string prefix)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();
            lock (RuntimeSync)
                CurrentListener = listener;

            while (true)
            {
                try
                {
                    var context = listener.GetContext();
                    HandleHttp(context);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Warning("[HTTP] " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        internal static void StopHttpListener()
        {
            lock (RuntimeSync)
            {
                if (CurrentListener == null)
                    return;

                try
                {
                    CurrentListener.Stop();
                    CurrentListener.Close();
                }
                catch
                {
                }
                finally
                {
                    CurrentListener = null;
                }
            }
        }

        private static void HandleHttp(HttpListenerContext context)
        {
            if (context.Request.Url.AbsolutePath.Equals("/status", StringComparison.OrdinalIgnoreCase))
            {
                WriteText(context, 200, State.StatusText);
                return;
            }

            if (context.Request.Url.AbsolutePath.Equals("/logs", StringComparison.OrdinalIgnoreCase))
            {
                var since = ParseLong(context.Request.QueryString["sinceId"]);
                var take = ParseInt(context.Request.QueryString["take"], 200);
                if (string.Equals(context.Request.QueryString["format"], "text", StringComparison.OrdinalIgnoreCase))
                {
                    WriteText(context, 200, Logger.GetText(since, take));
                    return;
                }

                WriteJson(context, 200, new
                {
                    items = Logger.GetEntries(since, take)
                });
                return;
            }

            if (!context.Request.Url.AbsolutePath.Equals("/adt", StringComparison.OrdinalIgnoreCase) ||
                !context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                WriteText(context, 404, "Use GET /status, GET /logs, or POST /adt");
                return;
            }

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                body = reader.ReadToEnd();

            var change = PiChangeFactory.Create(body);
            var ok = State.PushToSubscriber(change, change.Descriptor, out var result);
            WriteText(context, ok ? 200 : 502, result);
            Logger.Info(string.Format("[HTTP] POST /adt result={0}: {1}", ok, result));
        }

        private static void WriteText(HttpListenerContext context, int statusCode, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? "");
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }

        private static void WriteJson(HttpListenerContext context, int statusCode, object value)
        {
            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            var json = serializer.Serialize(value);
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }

        private static long ParseLong(string value)
        {
            long parsed;
            return long.TryParse(value, out parsed) ? parsed : 0;
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static string GetArg(string[] args, string name, string fallback)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return fallback;
        }

        private static System.Collections.Generic.Dictionary<string, string> LoadServiceConfigFile()
        {
            var result = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bridge-service.config");
                if (!File.Exists(path))
                    return result;

                foreach (var line in File.ReadAllLines(path))
                {
                    var trimmed = (line ?? "").Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                        continue;

                    var index = trimmed.IndexOf('=');
                    if (index <= 0)
                        continue;

                    result[trimmed.Substring(0, index).Trim()] = trimmed.Substring(index + 1).Trim();
                }

                Logger.Info("[CONFIG] Loaded bridge-service.config");
            }
            catch (Exception ex)
            {
                Logger.Warning("[CONFIG] Failed to read bridge-service.config: " + ex.Message);
            }

            return result;
        }

        private static string ResolveSqlConnection(string[] args)
        {
            var argValue = GetArg(args, "--sql", "");
            if (!string.IsNullOrWhiteSpace(argValue))
                return argValue;

            var envValue = Environment.GetEnvironmentVariable("HL7GATEWAY_SQLSERVER");
            if (!string.IsNullOrWhiteSpace(envValue))
                return envValue;

            return TryReadServiceConnectionString();
        }

        private static string TryReadServiceConnectionString()
        {
            foreach (var path in GetCandidateServiceConfigPaths())
            {
                try
                {
                    if (!File.Exists(path))
                        continue;

                    var serializer = new JavaScriptSerializer();
                    var json = serializer.DeserializeObject(File.ReadAllText(path)) as System.Collections.Generic.Dictionary<string, object>;
                    var connectionStrings = json != null && json.ContainsKey("ConnectionStrings")
                        ? json["ConnectionStrings"] as System.Collections.Generic.Dictionary<string, object>
                        : null;
                    if (connectionStrings != null && connectionStrings.ContainsKey("SqlServer"))
                    {
                        var value = Convert.ToString(connectionStrings["SqlServer"]);
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            Logger.Info("[STORE] SQL connection loaded from " + path);
                            return value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("[STORE] Failed to read SQL connection from " + path + ": " + ex.Message);
                }
            }

            Logger.Warning("[STORE] SQL connection string not found; local JSON fallback will be used");
            return "";
        }

        private static string[] GetCandidateServiceConfigPaths()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return new[]
            {
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", "Service", "appsettings.json")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "Service", "appsettings.json")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Service", "appsettings.json")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "HL7Gateway.Service", "appsettings.json")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "HL7Gateway.Service", "appsettings.json"))
            };
        }

        private static bool HasArg(string[] args, string name)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            WriteFatal(ex ?? new Exception("Unhandled non-Exception object: " + e.ExceptionObject));
        }

        private static void WriteFatal(Exception ex)
        {
            try
            {
                var text = "PhilipsHifBridge fatal error\r\n" +
                           "Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n\r\n" +
                           ex + "\r\n";
                File.WriteAllText(GetFatalLogPath(), text, Encoding.UTF8);
            }
            catch
            {
                // Avoid throwing while reporting the original startup failure.
            }
        }

        internal static void InitServiceStartLog()
        {
            try
            {
                File.WriteAllText(GetServiceStartLogPath(),
                    "==== PhilipsHifBridge service start " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====\r\n",
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        internal static void LogStartup(bool isService, string message)
        {
            if (!isService)
                return;

            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message;
            try
            {
                File.AppendAllText(GetServiceStartLogPath(), line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
            }

            Logger.Info("[STARTUP] " + message);
        }

        internal static void WriteServiceStartFailure(Exception ex)
        {
            var text = "Service startup failed\r\n" +
                       "Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n\r\n" +
                       ex + "\r\n";
            try
            {
                File.AppendAllText(GetServiceStartLogPath(), text, Encoding.UTF8);
            }
            catch
            {
            }

            WriteFatal(ex);
            Logger.Error("[SERVICE] startup failed: " + ex);
        }

        private static string GetServiceStartLogPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "service-start.log");
        }

        private static string GetFatalLogPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bridge-fatal.log");
        }
    }
}
