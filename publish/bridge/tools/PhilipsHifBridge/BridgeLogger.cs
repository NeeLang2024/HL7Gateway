using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PhilipsHifBridge
{
    internal sealed class BridgeLogger
    {
        private const int MaxEntries = 1000;
        private readonly object _sync = new object();
        private readonly List<BridgeLogEntry> _entries = new List<BridgeLogEntry>();
        private readonly string _logPath;
        private long _nextId = 1;

        public BridgeLogger()
        {
            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bridge.log");
        }

        public string LogPath
        {
            get { return _logPath; }
        }

        public void Info(string message)
        {
            Write("Information", message);
        }

        public void Warning(string message)
        {
            Write("Warning", message);
        }

        public void Error(string message)
        {
            Write("Error", message);
        }

        public void Write(string level, string message)
        {
            var entry = new BridgeLogEntry
            {
                Id = 0,
                Time = DateTime.Now,
                Level = string.IsNullOrWhiteSpace(level) ? "Information" : level,
                Message = message ?? ""
            };

            lock (_sync)
            {
                entry.Id = _nextId++;
                _entries.Add(entry);
                if (_entries.Count > MaxEntries)
                    _entries.RemoveRange(0, _entries.Count - MaxEntries);
            }

            var line = FormatLine(entry);
            Console.WriteLine(line);
            AppendFile(line);
        }

        public List<BridgeLogEntry> GetEntries(long sinceId, int take)
        {
            lock (_sync)
            {
                var limit = take <= 0 || take > 500 ? 200 : take;
                return _entries
                    .Where(e => e.Id > sinceId)
                    .OrderByDescending(e => e.Id)
                    .Take(limit)
                    .OrderBy(e => e.Id)
                    .Select(e => e.Clone())
                    .ToList();
            }
        }

        public string GetText(long sinceId, int take)
        {
            var entries = GetEntries(sinceId, take);
            var builder = new StringBuilder();
            foreach (var entry in entries)
                builder.AppendLine(FormatLine(entry));
            return builder.ToString();
        }

        private void AppendFile(string line)
        {
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Keep the bridge running even if the log file is temporarily unavailable.
            }
        }

        private static string FormatLine(BridgeLogEntry entry)
        {
            return string.Format("{0:yyyy-MM-dd HH:mm:ss}\t{1}\t{2}",
                entry.Time,
                entry.Level,
                entry.Message);
        }
    }

    internal sealed class BridgeLogEntry
    {
        public long Id { get; set; }
        public DateTime Time { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }

        public BridgeLogEntry Clone()
        {
            return new BridgeLogEntry
            {
                Id = Id,
                Time = Time,
                Level = Level,
                Message = Message
            };
        }
    }
}
