// Hallow VPN - crash reports, kept on this PC (family v3). Nothing is ever sent anywhere.
// <data folder>\crashes\<yyyy-MM-dd HH-mm-ss>.txt: app, version, Windows, time, the exception (with inner ones and the stack).
// Never settings or file contents. The newest 20 are kept.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HallowVPN
{
    public static class Crash
    {
        public static string Dir { get { return Path.Combine(VpnSettings.Dir, "crashes"); } }
        public static volatile string[] Secrets = new string[0];   // the Nord service username / password: never written into a report
        static int writing;

        public static void Install()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Write(e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, e) => { Write(e.Exception); e.SetObserved(); };
        }

        public static void Write(Exception ex)
        {
            if (ex == null || System.Threading.Interlocked.Exchange(ref writing, 1) == 1) return;   // one report per crash
            try
            {
                Directory.CreateDirectory(Dir);
                var now = DateTime.Now;
                var sb = new StringBuilder();
                sb.AppendLine("Hallow VPN " + Program.Version + " closed unexpectedly");
                sb.AppendLine("Time: " + now.ToString("yyyy-MM-ddTHH:mm:ss"));
                sb.AppendLine("Windows: " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (64-bit)" : ""));
                sb.AppendLine(".NET: " + Environment.Version);
                sb.AppendLine();
                sb.AppendLine(ex.ToString());   // type, message, stack, inner exceptions
                var text = sb.ToString();
                foreach (var sec in Secrets ?? new string[0]) if (!string.IsNullOrEmpty(sec) && sec.Length >= 4) text = text.Replace(sec, "<secret>");
                var name = now.ToString("yyyy-MM-dd HH-mm-ss"); var f = Path.Combine(Dir, name + ".txt");
                for (int i = 2; File.Exists(f); i++) f = Path.Combine(Dir, name + " (" + i + ").txt");
                File.WriteAllText(f, text, new UTF8Encoding(false));
                foreach (var old in Files().Skip(20)) try { File.Delete(old); } catch { }
            }
            catch { }
        }

        // crash files, newest first
        public static string[] Files()
        {
            try { return Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.txt").OrderByDescending(File.GetLastWriteTimeUtc).ToArray() : new string[0]; }
            catch { return new string[0]; }
        }
        public static string Last() { var f = Files(); return f.Length == 0 ? null : f[0]; }
        public static DateTime? LastTime() { var f = Last(); try { return f == null ? (DateTime?)null : File.GetLastWriteTime(f); } catch { return null; } }
    }
}
