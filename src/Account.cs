// Hallow VPN - the NordVPN SERVICE credentials (Nord Account > NordVPN > "Set up NordVPN manually" > Service credentials). They are
// NOT the Nord login (email + password) - Hallow VPN refuses an email address as the username. Kept in %APPDATA%\HallowVPN\account.dat,
// encrypted with Windows DPAPI for this Windows user only (DataProtectionScope.CurrentUser). Never logged, never in status, the pipe,
// crash reports (Crash.Secrets) or on a command line: they go to Windows' RasDial in this process only.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HallowVPN
{
    public class Credentials
    {
        public string User, Pass;
        public static string Mask(string user) { return string.IsNullOrEmpty(user) ? "" : user.Length <= 4 ? new string('•', user.Length) : user.Substring(0, 3) + new string('•', 6) + user.Substring(user.Length - 2); }
    }

    public static class Account
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HallowVPN.account.v1");
        public static string FilePath { get { return Path.Combine(VpnSettings.Dir, "account.dat"); } }

        // null = fine; otherwise why it can't be service credentials
        public static string Check(string user, string pass)
        {
            user = (user ?? "").Trim(); pass = pass ?? "";
            if (user.Length == 0 || pass.Length == 0) return "Paste both the service username and the service password.";
            if (user.Contains("@")) return "That looks like your Nord login (an email address). Hallow VPN needs the SERVICE credentials instead - they're on Nord's website under NordVPN > Set up NordVPN manually.";
            if (user.Length > 256 || pass.Length > 256) return "That's too long for a service username or password.";
            if (Regex.IsMatch(user, "\\s") ) return "The service username has no spaces - check that you copied it whole.";
            if (pass.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0) return "The service password has no line breaks - check that you copied it whole.";
            return null;
        }

        public static byte[] Protect(Credentials c)
        {
            var json = Family.Json(new System.Collections.Generic.Dictionary<string, object> { { "u", c.User }, { "p", c.Pass } });
            var plain = Encoding.UTF8.GetBytes(json);
            try { return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }
        public static Credentials Unprotect(byte[] data)
        {
            byte[] plain = null;
            try
            {
                plain = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
                var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(Encoding.UTF8.GetString(plain));
                object u, p;
                if (d == null || !d.TryGetValue("u", out u) || !d.TryGetValue("p", out p) || !(u is string) || !(p is string)) return null;
                return new Credentials { User = (string)u, Pass = (string)p };
            }
            catch { return null; }
            finally { if (plain != null) Array.Clear(plain, 0, plain.Length); }
        }

        public static string Save(string user, string pass)
        {
            var err = Check(user, pass); if (err != null) return err;
            if (VpnSettings.Frozen) return null;
            try
            {
                Directory.CreateDirectory(VpnSettings.Dir);
                var tmp = FilePath + ".tmp";
                File.WriteAllBytes(tmp, Protect(new Credentials { User = user.Trim(), Pass = pass }));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
                Crash.Secrets = new[] { user.Trim(), pass };
                return null;
            }
            catch (Exception e) { return "Couldn't save them: " + e.Message; }
        }
        public static Credentials Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var c = Unprotect(File.ReadAllBytes(FilePath));
                if (c != null) Crash.Secrets = new[] { c.User, c.Pass };
                return c;
            }
            catch { return null; }
        }
        public static bool Exists { get { try { return File.Exists(FilePath); } catch { return false; } } }
        public static void Remove()
        {
            if (VpnSettings.Frozen) return;
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
            Crash.Secrets = new string[0];
        }
    }
}
