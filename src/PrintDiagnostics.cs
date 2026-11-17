using System.Diagnostics;
using System.Text;

namespace Blueprint;

/// <summary>
/// Small local breadcrumbs, flushed after each event. Do not pass user text,
/// paths, craft names or exception messages. Native fail-fast crashes bypass
/// managed handlers; Windows crash dumps remain the source of native stacks.
/// </summary>
static class PrintDiagnostics
{
    const long FileLimit = 512 * 1024;
    const int HistoryCount = 3;
    static readonly object Gate = new();
    static readonly Dictionary<string, string> States = new();
    static readonly HashSet<string> Seen = new();
    static StreamWriter? writer;
    static string? file;
    static bool hooksInstalled;

    public static void Start(string directory, string version)
    {
        lock (Gate)
        {
            if (writer != null)
                return;
            try
            {
                Directory.CreateDirectory(directory);
                file = Path.Combine(directory, "PawPrints.log");
                Rotate();
                Open();
                States.Clear();
                Seen.Clear();
                if (!hooksInstalled)
                {
                    AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
                    hooksInstalled = true;
                }
                Record($"session.start version={version} pid={Environment.ProcessId}");
            }
            catch { DisableWriter(); }
        }
    }

    static void Open() => writer = new StreamWriter(
        new FileStream(file!, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
        new UTF8Encoding(false)) { AutoFlush = true };

    static void Rotate()
    {
        for (int i = HistoryCount; i >= 1; i--)
        {
            string source = i == 1 ? file! : file + "." + (i - 1);
            if (File.Exists(source))
                File.Move(source, file + "." + i, overwrite: true);
        }
    }

    public static void Record(string code)
    {
        lock (Gate)
        {
            if (writer == null)
                return;
            try
            {
                if (writer.BaseStream.Length >= FileLimit)
                {
                    writer.Dispose();
                    writer = null;
                    Rotate();
                    Open();
                }
                code = code.Replace('\r', ' ').Replace('\n', ' ');
                if (code.Length > 4096)
                    code = code[..4096];
                writer!.WriteLine($"{DateTimeOffset.UtcNow:O} tid={Environment.CurrentManagedThreadId} {code}");
            }
            catch { DisableWriter(); }
        }
    }

    public static void State(string key, string value)
    {
        lock (Gate)
        {
            if (writer == null || States.GetValueOrDefault(key) == value)
                return;
            States[key] = value;
            Record(key + "=" + value);
        }
    }

    public static void Once(string code)
    {
        lock (Gate)
        {
            if (writer != null && Seen.Count < 128 && Seen.Add(code))
                Record(code);
        }
    }

    public static void Error(string operation, Exception error)
    {
        // Stack frames contain method names only: no message, source file,
        // arguments or user-provided text that may appear in exceptions.
        try
        {
            Record($"error operation={operation} type={error.GetType().FullName} hresult=0x{error.HResult:X8}");
            foreach (var frame in new StackTrace(error, false).GetFrames().Take(24))
            {
                var method = frame.GetMethod();
                Record($"stack {method?.DeclaringType?.FullName}.{method?.Name}");
            }
            if (error.InnerException is { } inner)
                Record($"inner type={inner.GetType().FullName} hresult=0x{inner.HResult:X8}");
        }
        catch { /* Diagnostics must never become a second failure. */ }
    }

    static void OnUnhandled(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception error)
            Error("unhandled-managed", error);
    }

    public static void Stop()
    {
        lock (Gate)
        {
            Record("session.stop");
            DisableWriter();
            if (hooksInstalled)
            {
                AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
                hooksInstalled = false;
            }
        }
    }

    static void DisableWriter()
    {
        try { writer?.Dispose(); }
        catch { }
        writer = null;
    }
}
