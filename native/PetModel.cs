// Shared production model and protocol. No window, native code, or process launch.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ZCodePet {
    internal static class AtlasContract {
        internal const int Width = 192, Height = 208;
        // Codex desktop renders the mascot at a 7.04rem width (7.04*16 = 112.64px, height 112.64*208/192).
        internal const double BaseWidth = 112.64, BaseHeight = 122.016;
        // Codex plays the idle row on sixfold slowed durations; other rows use the raw table.
        internal const int IdleSlowdown = 6;
        internal static readonly string[] States = { "idle", "running-right", "running-left", "waving", "jumping", "failed", "waiting", "running", "review" };
        internal static readonly int[][] Durations = {
            new[] {280,110,110,140,140,320}, new[] {120,120,120,120,120,120,120,220},
            new[] {120,120,120,120,120,120,120,220}, new[] {140,140,140,280},
            new[] {140,140,140,140,280}, new[] {140,140,140,140,140,140,140,240},
            new[] {150,150,150,150,150,260}, new[] {120,120,120,120,120,220}, new[] {150,150,150,150,150,280}
        };
        internal static int Row(string name) { return Array.IndexOf(States, name); }
        internal static int Cycle(int row) { int n = 0; foreach (int d in Durations[row]) n += d; return n; }
        internal static int Frame(int row, double elapsed, double speed) {
            double t = Math.Max(0, elapsed) * speed % Cycle(row);
            for (int c = 0; c < Durations[row].Length; c++) { if (t < Durations[row][c]) return c; t -= Durations[row][c]; }
            return 0;
        }
        internal static int DisplayCycle(int row) { return row == 0 ? Cycle(0) * IdleSlowdown : Cycle(row); }
        internal static int DisplayFrame(int row, double elapsed, double speed) {
            double t = Math.Max(0, elapsed) * speed % DisplayCycle(row);
            for (int c = 0; c < Durations[row].Length; c++) {
                double d = Durations[row][c] * (row == 0 ? IdleSlowdown : 1);
                if (t < d) return c; t -= d;
            }
            return 0;
        }
        // Codex desktop playback: idle loops on the slowed durations; every other state plays
        // exactly three loops and then settles into the slow idle loop while the state persists.
        internal static void SequenceFrame(string state, double elapsed, double speed, out int row, out int frame) {
            int r = Row(state);
            if (r <= 0) { row = 0; frame = DisplayFrame(0, elapsed, speed); return; }
            double loops = 3.0 * Cycle(r);
            if (elapsed * speed < loops) { row = r; frame = Frame(r, elapsed, speed); return; }
            row = 0; frame = DisplayFrame(0, elapsed - loops / speed, speed);
        }
        internal static int NextFrameDelay(string state, double elapsed, double speed) {
            int row=Row(state);double time=Math.Max(0,elapsed)*speed;
            if(row<=0) row=0;
            else if(time>=3.0*Cycle(row)) {time-=3.0*Cycle(row);row=0;}
            time%=DisplayCycle(row);
            foreach(int duration in Durations[row]) {
                double scaled=duration*(row==0?IdleSlowdown:1);
                if(time<scaled) return Math.Max(1,(int)Math.Ceiling((scaled-time)/speed));
                time-=scaled;
            }
            return 1;
        }
        internal static int Gaze(double x, double y) {
            double degrees = Math.Atan2(x, -y) * 180 / Math.PI;
            if (degrees < 0) degrees += 360;
            return ((int)Math.Floor((degrees + 11.25) / 22.5)) % 16;
        }
    }

    // A pointer interaction is visual feedback, not a synthetic task state.
    internal sealed class HoverLeaveAnimation {
        internal long StartedAt=-1;
        internal void Start(long now) {StartedAt=now;}
        internal void Clear() {StartedAt=-1;}
        internal bool Active(long now,double speed) {return StartedAt>=0 && now>=StartedAt && (now-StartedAt)*speed<3.0*AtlasContract.Cycle(7);}
        internal int Frame(long now,double speed) {return AtlasContract.Frame(7,now-StartedAt,speed);}
    }

    internal sealed class Settings {
        internal const int CurrentVersion = 2;
        internal double Speed = 1, Scale = 1;
        // Codex desktop pets never track the mouse; their look input is the editor caret or a
        // computer-use cursor event stream. Pointer following is an opt-in extra here and must
        // never mask task-state animations (the running row shows while a task is working).
        internal bool FollowPointer = false, ReducedMotion = false;
        internal Settings Clone() { return (Settings)MemberwiseClone(); }
        internal Dictionary<string,object> AsJson() { return new Dictionary<string,object> {
            {"speed",Speed},{"scale",Scale},{"followPointer",FollowPointer},{"reducedMotion",ReducedMotion}
        }; }
        internal static void Apply(Settings s, IDictionary<string,object> fields, bool hasOp) {
            foreach (KeyValuePair<string,object> kv in fields) {
                switch (kv.Key) {
                    case "op": if (!hasOp) throw new ArgumentException("Unknown setting: op"); break;
                    case "speed": s.Speed = Protocol.Number(kv.Value, .5, 2, "speed"); break;
                    case "scale": s.Scale = Protocol.Number(kv.Value, .5, 2, "scale"); break;
                    case "followPointer": s.FollowPointer = Protocol.Boolean(kv.Value,"followPointer"); break;
                    case "reducedMotion": s.ReducedMotion = Protocol.Boolean(kv.Value,"reducedMotion"); break;
                    default: throw new ArgumentException("Unknown setting: " + kv.Key);
                }
            }
        }
        internal static Settings Load(string dir) {
            Settings s = new Settings(); string path = Path.Combine(dir,"settings.json");
            if (!File.Exists(path)) return s;
            try {
                if (new FileInfo(path).Length > 4096) return s;
                return FromDocument(s,Protocol.Parse(File.ReadAllText(path,Encoding.UTF8)));
            } catch (ArgumentException) { return s; } catch (IOException) { return s; }
              catch (UnauthorizedAccessException) { return s; }
        }
        // Split from Load so managed tests can exercise the migration rules without the
        // Framework JSON parser (which cannot lazily load inside a .NET Core PowerShell host).
        internal static Settings FromDocument(Settings defaults, Dictionary<string,object> document) {
            object marker;
            bool versioned = document.TryGetValue("settingsVersion",out marker) && marker is int && (int)marker >= CurrentVersion;
            document.Remove("settingsVersion");
            // v0.2.0 and earlier persisted followPointer:true purely from the old default;
            // unversioned files drop that field so the Codex-parity default applies once.
            if (!versioned) document.Remove("followPointer");
            Settings candidate = defaults.Clone(); Settings.Apply(candidate,document,false); return candidate;
        }
        internal void Save(string dir) {
            Directory.CreateDirectory(dir); string path = Path.Combine(dir,"settings.json");
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                Dictionary<string,object> document = AsJson(); document.Add("settingsVersion",CurrentVersion);
                File.WriteAllText(temp,Protocol.Encode(document),new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal static bool AutoStartAllowed(string dir) {
            string path=Path.Combine(dir,"autostart.json");if(!File.Exists(path)) return true;
            try {
                if(new FileInfo(path).Length>1024) return false;
                Dictionary<string,object> document=Protocol.Parse(File.ReadAllText(path,Encoding.UTF8));
                object value;return document.TryGetValue("autoStart",out value) && value is bool && (bool)value;
            } catch(ArgumentException) {return false;}catch(IOException) {return false;}catch(UnauthorizedAccessException) {return false;}
        }
        internal static void SaveAutoStart(string dir,bool allowed) {
            Directory.CreateDirectory(dir);string path=Path.Combine(dir,"autostart.json"),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                File.WriteAllText(temp,allowed?"{\"autoStart\":true}":"{\"autoStart\":false}",new UTF8Encoding(false));
                if(File.Exists(path)) File.Replace(temp,path,null);else File.Move(temp,path);
            } finally {try {if(File.Exists(temp)) File.Delete(temp);}catch(IOException) {}catch(UnauthorizedAccessException) {}}
        }
    }

    internal static class Protocol {
        internal const int MaxMessage = 8192;
        internal static Dictionary<string,object> Parse(string text) {
            JavaScriptSerializer js = new JavaScriptSerializer(); js.MaxJsonLength = MaxMessage; js.RecursionLimit = 12;
            Dictionary<string,object> result;
            try {result = js.DeserializeObject(text) as Dictionary<string,object>;}
            catch(ArgumentException) {throw new ArgumentException("Malformed JSON object");}
            catch(InvalidOperationException) {throw new ArgumentException("Unsupported JSON object");}
            if (result == null) throw new ArgumentException("Request must be a JSON object"); return result;
        }
        internal static string Encode(object obj) { return new JavaScriptSerializer().Serialize(obj); }
        internal static string String(IDictionary<string,object> d, string key) {
            object o; if (!d.TryGetValue(key,out o) || !(o is string)) throw new ArgumentException("Missing or invalid " + key);
            return (string)o;
        }
        internal static bool Boolean(object value, string key) {
            if (!(value is bool)) throw new ArgumentException(key + " must be boolean"); return (bool)value;
        }
        internal static double Number(object value, double min, double max, string key) {
            if (!(value is int || value is long || value is double || value is decimal || value is float)) throw new ArgumentException(key + " must be numeric");
            double v = Convert.ToDouble(value,CultureInfo.InvariantCulture);
            if (Double.IsNaN(v) || Double.IsInfinity(v) || v < min || v > max) throw new ArgumentException(key + " out of range"); return v;
        }
        internal static void Fields(IDictionary<string,object> d, params string[] allowed) {
            foreach (string key in d.Keys) if (Array.IndexOf(allowed,key) < 0) throw new ArgumentException("Unknown field: " + key);
        }
        internal static Dictionary<string,object> Error(string message) {
            if(message.Length>512) message=message.Substring(0,512);
            return new Dictionary<string,object> { {"ok",false},{"error",message} };
        }
    }

    internal sealed class PetModel {
        // ZCode hooks expose no turn-level failed/cancel event and no read-receipt, so the two
        // animations whose real Codex triggers cannot fire rotate through the idle queue instead
        // (user-requested substitution, documented as a deliberate deviation in the README).
        internal static readonly string[] IdleQueue = { "failed", "review" };
        internal const long IdleQueueDelayMs = 20000, IdleQueueGapMs = 40000;
        internal bool IdleQueueEnabled = true;
        private int idleQueueIndex;
        private long idleQueueAt = IdleQueueDelayMs;
        internal readonly Dictionary<string,string> Sessions = new Dictionary<string,string>(StringComparer.Ordinal);
        private readonly Dictionary<string,long> updatedAt = new Dictionary<string,long>(StringComparer.Ordinal);
        private readonly Dictionary<string,long> leaseUntil = new Dictionary<string,long>(StringComparer.Ordinal);
        internal int ExpiredSessions;
        internal string TemporaryState;
        internal long TemporaryUntil;
        internal bool QueueTemporary;
        internal string Session(string id, string state, long now = 0) {
            if (!Regex.IsMatch(id,"\\A[a-f0-9]{64}\\z")) throw new ArgumentException("sessionId must be 64 lowercase hex characters");
            if (Array.IndexOf(new[] {"idle","running","waiting","failed","review","ended"},state) < 0) throw new ArgumentException("Invalid session state");
            Prune(now);
            if (state == "ended") Remove(id);
            else {
                if (!Sessions.ContainsKey(id) && Sessions.Count >= 128) {
                    string eviction=null;long oldest=Int64.MaxValue;
                    foreach(KeyValuePair<string,string> entry in Sessions) {
                        if((entry.Value=="idle" || entry.Value=="review") && updatedAt[entry.Key]<oldest) {oldest=updatedAt[entry.Key];eviction=entry.Key;}
                    }
                    if(eviction==null) throw new ArgumentException("Active session limit reached");Remove(eviction);
                }
                Sessions[id] = state;updatedAt[id]=now;
                leaseUntil[id]=now+(state=="review"?30000:state=="failed"?5000:1800000);
            }
            return Resolve(now);
        }
        private void Remove(string id) {Sessions.Remove(id);updatedAt.Remove(id);leaseUntil.Remove(id);}
        private void Prune(long now) {
            List<string> expired=new List<string>();foreach(KeyValuePair<string,long> entry in leaseUntil) if(now>=entry.Value) expired.Add(entry.Key);
            foreach(string id in expired) {if(Sessions[id]=="running" || Sessions[id]=="waiting") ExpiredSessions++;Remove(id);}
        }
        internal string Resolve(long now) {
            Prune(now);
            if (TemporaryState != null && now < TemporaryUntil) {
                // External temporaries (greeting, pet_set_state) defer the queue; a queued
                // animation playing out its own loops must not erode its following gap.
                if (!QueueTemporary) idleQueueAt = now + IdleQueueDelayMs;
                return TemporaryState;
            }
            string[] order = {"waiting","failed","review","running","idle"};
            foreach (string state in order) foreach (string value in Sessions.Values) if (state == value) { idleQueueAt = now + IdleQueueDelayMs; return state; }
            if (IdleQueueEnabled && now >= idleQueueAt) {
                string queued = IdleQueue[idleQueueIndex % IdleQueue.Length];
                idleQueueIndex++;
                TemporaryState = queued;
                QueueTemporary = true;
                TemporaryUntil = now + 3L * AtlasContract.Cycle(AtlasContract.Row(queued));
                idleQueueAt = TemporaryUntil + IdleQueueGapMs;
                return queued;
            }
            return "idle";
        }
    }

}
