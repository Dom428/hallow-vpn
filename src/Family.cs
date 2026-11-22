// Hallow VPN - the Hallow family link: a named pipe other Hallow apps talk to ("HallowFamily.VPN.<user>": ping, status, connect,
// disconnect, menu, open, invoke, quit), "HallowVPN.exe --cmd <line>" for scripts and status.json for apps that only read. Only this
// Windows user can open the pipe, never over the network. Status carries the server and state only - never the Nord service credentials.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowVPN
{
    public static class Family
    {
        // in developer tests (HALLOW_VPN_TESTDIR) the pipes get a ".test" suffix, so tests never reach the real apps
        public static string PipeName(string app) { return "HallowFamily." + app + "." + Environment.UserName + (VpnSettings.TestDir != null ? ".test-" + Path.GetFileName(VpnSettings.TestDir.TrimEnd(Path.DirectorySeparatorChar)) : ""); }
        // where another Hallow app keeps its files (Clips is the exception: local app data)
        public static string DataDir(string app)
        {
            if (VpnSettings.TestDir != null) return Path.Combine(VpnSettings.TestDir, "Hallow" + app);
            return Path.Combine(Environment.GetFolderPath(app == "Clips" ? Environment.SpecialFolder.LocalApplicationData : Environment.SpecialFolder.ApplicationData), "Hallow" + app);
        }

        // ---- server: one line in, one line out. handler gets the line and returns the reply ("ok ...", "error ...").
        public static void Serve(string app, Func<string, string> handler)
        {
            new Thread(() =>
            {
                var name = PipeName(app); int fails = 0;
                while (true)
                {
                    NamedPipeServerStream pipe = null;
                    try
                    {
                        var sec = new PipeSecurity();
                        sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                        sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                        pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 8, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, sec);
                        pipe.WaitForConnection(); fails = 0;
                        var p = pipe; pipe = null;
                        new Thread(() => Handle(p, handler)) { IsBackground = true, Name = "family pipe" }.Start();
                    }
                    catch
                    {
                        if (pipe != null) try { pipe.Dispose(); } catch { }
                        Thread.Sleep(Math.Min(30000, 500 * ++fails));
                    }
                }
            }) { IsBackground = true, Name = "family server" }.Start();
        }
        static void Handle(NamedPipeServerStream p, Func<string, string> handler)
        {
            try
            {
                var line = ReadLine(p, 2000); string reply;
                if (line == null) reply = "error no command";
                else { try { reply = handler(line.Trim()) ?? "error unknown command"; } catch (Exception e) { reply = "error " + OneLine(e.Message); } }
                var b = new UTF8Encoding(false).GetBytes(OneLine(reply) + "\n");
                p.Write(b, 0, b.Length); p.Flush();
                try { p.WaitForPipeDrain(); } catch { }
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        public static string OneLine(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); }

        // reads up to '\n' (or the end) within ms; null on timeout
        static string ReadLine(Stream s, int ms)
        {
            string result = null;
            var t = new Thread(() =>
            {
                try
                {
                    var buf = new List<byte>(); var one = new byte[1];
                    while (buf.Count < 4 * 1024 * 1024) { int n = s.Read(one, 0, 1); if (n <= 0 || one[0] == (byte)'\n') break; buf.Add(one[0]); }
                    result = Encoding.UTF8.GetString(buf.ToArray()).TrimEnd('\r');
                }
                catch { }
            }) { IsBackground = true };
            t.Start();
            if (!t.Join(ms)) { try { s.Dispose(); } catch { } return null; }
            return result;
        }

        // ---- client
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool WaitNamedPipe(string name, int timeout);
        // is the app's pipe there? (answers at once when it isn't, unlike Connect, which waits out its timeout)
        public static bool Running(string app)
        {
            for (int i = 0; ; i++)
            {
                if (WaitNamedPipe(@"\\.\pipe\" + PipeName(app), 1)) return true;
                if (Marshal.GetLastWin32Error() != 2) return true;   // 2 = no such pipe; anything else (busy, timeout) means it exists
                if (i >= 1) return false;
                Thread.Sleep(40);   // v1.1: once more - a server is briefly gone between two connections
            }
        }
        // sends a line to another running Hallow app; null = not running (or no answer in time)
        public static string Send(string app, string line, int replyMs)
        {
            try
            {
                if (!Running(app)) return null;
                using (var c = new NamedPipeClientStream(".", PipeName(app), PipeDirection.InOut))
                {
                    c.Connect(1500);
                    var b = new UTF8Encoding(false).GetBytes(OneLine(line) + "\n"); c.Write(b, 0, b.Length); c.Flush();
                    return ReadLine(c, replyMs);
                }
            }
            catch { return null; }
        }
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        // the app's status (from its pipe; null when it isn't running, is too old to have the link, or answers nonsense)
        public static Dictionary<string, object> Status(string app)
        {
            try
            {
                var r = Send(app, "status", 2000);
                if (r == null || !r.StartsWith("ok {")) return null;
                return js.Deserialize<Dictionary<string, object>>(r.Substring(3));
            }
            catch { return null; }
        }
        // "ok" / "ok {...}" -> null (fine); "error <message>" -> the message; no answer -> notRunning
        public static string Error(string reply, string notRunning)
        {
            if (reply == null) return notRunning;
            if (reply == "ok" || reply.StartsWith("ok ")) return null;
            return reply.StartsWith("error ") ? reply.Substring(6) : reply;
        }

        // ---- "HallowVPN.exe --cmd <line>": prints the reply. 0 = ok, 1 = error, 2 = Hallow VPN isn't running.
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int n);
        public static int RunCmd(string line)
        {
            var h = GetStdHandle(-11);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) AttachConsole(-1);   // not redirected: print to the console it was started from
            string reply = string.IsNullOrWhiteSpace(line) ? "error no command" : Send("VPN", line.Trim(), 5000);
            int code = reply == null ? 2 : reply == "ok" || reply.StartsWith("ok ") ? 0 : 1;
            try
            {
                var o = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
                o.WriteLine(reply ?? "error Hallow VPN isn't running");
            }
            catch { }
            return code;
        }

        // ---- status.json, written atomically (status.json.tmp, then replace)
        public static string Json(object o) { return js.Serialize(o); }
        public static string Time(DateTime t) { return t.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture); }
        static readonly object fileLock = new object();
        public static void WriteStatus(string json)
        {
            lock (fileLock)
            {
                try
                {
                    Directory.CreateDirectory(VpnSettings.Dir);
                    var f = Path.Combine(VpnSettings.Dir, "status.json"); var tmp = f + ".tmp";
                    File.WriteAllText(tmp, json, new UTF8Encoding(false));
                    for (int i = 0; ; i++)
                    {
                        try { if (File.Exists(f)) File.Replace(tmp, f, null); else File.Move(tmp, f); break; }
                        catch { if (i >= 5) throw; Thread.Sleep(50); }
                    }
                }
                catch { }
            }
        }
    }
}
