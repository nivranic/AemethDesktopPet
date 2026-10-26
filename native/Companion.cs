// Original companion implementation. Uses the public atlas layout, not Codex application code.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Management;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ZCodePet {
    internal sealed class Atlas : IDisposable {
        private readonly Bitmap image;
        private readonly Dictionary<string,Bitmap> cache = new Dictionary<string,Bitmap>();
        internal Atlas(string path) {
            using (FileStream stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            using (Image source = Image.FromStream(stream,true,true)) {
                if (source.Width != 1536 || source.Height != 2288 || source.RawFormat.Guid != ImageFormat.Png.Guid) throw new ArgumentException("Atlas must be a 1536x2288 PNG");
                image = new Bitmap(1536,2288,PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(image)) { g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; g.DrawImageUnscaled(source,0,0); }
            }
        }
        internal Bitmap Render(int row, int column, double scale) {
            int w = (int)Math.Round(AtlasContract.BaseWidth * scale), h = (int)Math.Round(AtlasContract.BaseHeight * scale);
            string key = row + ":" + column + ":" + w + ":" + h; Bitmap found;
            if (cache.TryGetValue(key,out found)) return found;
            using (Bitmap cell = image.Clone(new Rectangle(column*192,row*208,192,208),PixelFormat.Format32bppArgb)) {
                BitmapData src = cell.LockBits(new Rectangle(0,0,192,208),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                byte[] bytes = new byte[192*208*4];
                try { for (int y=0;y<208;y++) Marshal.Copy(IntPtr.Add(src.Scan0,y*src.Stride),bytes,y*192*4,192*4); }
                finally { cell.UnlockBits(src); }
                Bitmap output = new Bitmap(w,h,PixelFormat.Format32bppPArgb);
                BitmapData target = output.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
                try {
                    byte[] line = new byte[w*4];
                    for (int y=0;y<h;y++) {
                        int sy = y*208/h;
                        for (int x=0;x<w;x++) {
                            int offset=(sy*192+x*192/w)*4, alpha=bytes[offset+3];
                            line[x*4]=(byte)((bytes[offset]*alpha+127)/255);
                            line[x*4+1]=(byte)((bytes[offset+1]*alpha+127)/255);
                            line[x*4+2]=(byte)((bytes[offset+2]*alpha+127)/255); line[x*4+3]=(byte)alpha;
                        }
                        Marshal.Copy(line,0,IntPtr.Add(target.Scan0,y*target.Stride),line.Length);
                    }
                } finally { output.UnlockBits(target); }
                cache.Add(key,output); return output;
            }
        }
        internal void ClearCache() { foreach (Bitmap b in cache.Values) b.Dispose(); cache.Clear(); }
        public void Dispose() { ClearCache(); image.Dispose(); }
    }

    internal static class Native {
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { internal int X,Y; internal POINT(int x,int y) { X=x;Y=y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct SIZE { internal int X,Y; internal SIZE(int x,int y) { X=x;Y=y; } }
        [StructLayout(LayoutKind.Sequential,Pack=1)] internal struct BLEND { internal byte Operation,Flags,Alpha,Format; }
        [DllImport("user32.dll",SetLastError=true)] internal static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr dst,ref POINT point,ref SIZE size,IntPtr src,ref POINT origin,int key,ref BLEND blend,int flags);
        [DllImport("user32.dll")] internal static extern IntPtr SetCursor(IntPtr cursor);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
        [DllImport("user32.dll",SetLastError=true)] internal static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd,int id);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct STARTUPINFOW { internal int cb; internal IntPtr reserved1,reserved2,reserved3,reserved4; internal int desktopX,desktopY,desktopXSize,desktopYSize,desktopFlags,showWindow,reserved5; internal IntPtr standardInput,standardOutput,standardError; }
        [StructLayout(LayoutKind.Sequential)] internal struct PROCESS_INFORMATION { internal IntPtr process,thread; internal int processId,threadId; }
        [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode,ExactSpelling=true)] internal static extern bool CreateProcessW(string image,string arguments,IntPtr processAttributes,IntPtr threadAttributes,bool inheritHandles,uint creationFlags,IntPtr environment,string currentDirectory,ref STARTUPINFOW startup,out PROCESS_INFORMATION process);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        // Win11 round-corner preference for the menu popups (DWMWA_WINDOW_CORNER_PREFERENCE).
        internal static void RoundWindow(IntPtr handle) {
            if(handle==IntPtr.Zero) return;
            try { int preference=2;DwmSetWindowAttribute(handle,33,ref preference,4); } catch(Exception) {}
        }
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
    }

    // Best-effort lifecycle log so any future "pet vanished" report can be attributed:
    // external kills leave no .NET crash record, but launch/hide/show/exit transitions do.
    internal static class LifeLog {
        private static readonly object gate = new object();
        internal static void Write(string dataDir,string message) {
            try {
                lock (gate) {
                    if (String.IsNullOrEmpty(dataDir)) return;
                    string path = Path.Combine(dataDir,"lifecycle.log");
                    FileInfo file = new FileInfo(path);
                    if (file.Exists && file.Length > 262144) File.Delete(path);
                    File.AppendAllText(path,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)
                        + " pid=" + Process.GetCurrentProcess().Id + " " + message + Environment.NewLine,new UTF8Encoding(false));
                }
            } catch (Exception) {}
        }
    }

    internal sealed class PipeServer : IDisposable {
        private readonly string name;
        private readonly Func<Dictionary<string,object>,Dictionary<string,object>> dispatch;
        private readonly Action quitAfterResponse;
        private readonly Action<Exception> fatalError;
        private readonly Thread thread;
        private readonly object gate = new object();
        private volatile bool stopped;
        private NamedPipeServerStream current;
        internal PipeServer(string pipe,Func<Dictionary<string,object>,Dictionary<string,object>> call,Action quit,Action<Exception> failed) {
            name=pipe;dispatch=call;quitAfterResponse=quit;fatalError=failed;
            // Fail startup synchronously if this name is already owned; no background busy loop.
            current=Create();
            thread = new Thread(Run);thread.IsBackground=true;thread.Name="ZCodePet IPC";thread.Start();
        }
        internal static PipeSecurity LocalUserSecurity() {
            PipeSecurity security = new PipeSecurity(); security.SetAccessRuleProtection(true,false);
            security.SetOwner(WindowsIdentity.GetCurrent().User);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
            return security;
        }
        private NamedPipeServerStream Create() {
            PipeSecurity security=LocalUserSecurity();
            return new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security);
        }
        private void Run() {
            NamedPipeServerStream first=current;
            while (!stopped) {
                NamedPipeServerStream pipe=null;
                try {
                    try {pipe=first??Create();first=null;}
                    catch(IOException e) {if(!stopped) fatalError(e);return;}
                    catch(UnauthorizedAccessException e) {if(!stopped) fatalError(e);return;}
                    lock(gate) { if (stopped) { pipe.Dispose(); return; } current=pipe; }
                    pipe.WaitForConnection(); if (stopped) return;
                    Dictionary<string,object> reply; bool quit=false;
                    try {
                        string line=ReadLine(pipe); Dictionary<string,object> request=Protocol.Parse(line);
                        reply=dispatch(request); quit=Protocol.String(request,"op")=="quit" && (bool)reply["ok"];
                    } catch (DecoderFallbackException) { reply=Protocol.Error("Request must be UTF-8"); }
                      catch (ArgumentException e) { reply=Protocol.Error(e.Message); }
                      catch (TimeoutException) { reply=Protocol.Error("Request timeout"); }
                    byte[] output=Encoding.UTF8.GetBytes(Protocol.Encode(reply)+"\n");
                    IAsyncResult write=pipe.BeginWrite(output,0,output.Length,null,null);
                    using (WaitHandle wait=write.AsyncWaitHandle) { if (!wait.WaitOne(1500)) throw new TimeoutException(); pipe.EndWrite(write); }
                    if (quit) { quitAfterResponse(); return; }
                } catch (IOException) { if (stopped) return; }
                  catch (TimeoutException) { if (stopped) return; }
                  catch (ObjectDisposedException) { if (stopped) return; }
                  catch (UnauthorizedAccessException) { return; }
                finally { lock(gate) { if (current==pipe) current=null; } if (pipe!=null) pipe.Dispose(); }
            }
        }
        private static string ReadLine(NamedPipeServerStream pipe) {
            Stopwatch deadline=Stopwatch.StartNew(); List<byte> result=new List<byte>(); byte[] chunk=new byte[1024];
            while (result.Count < Protocol.MaxMessage) {
                IAsyncResult read=pipe.BeginRead(chunk,0,Math.Min(chunk.Length,Protocol.MaxMessage-result.Count),null,null);
                int received;
                using (WaitHandle wait=read.AsyncWaitHandle) {
                    int remaining=1500-(int)deadline.ElapsedMilliseconds;
                    if (remaining<=0 || !wait.WaitOne(remaining)) throw new TimeoutException(); received=pipe.EndRead(read);
                }
                if (received==0) throw new ArgumentException("Request needs a newline");
                for (int i=0;i<received;i++) {
                    if (chunk[i]==10) return new UTF8Encoding(false,true).GetString(result.ToArray()).TrimEnd('\r');
                    result.Add(chunk[i]);
                }
            }
            throw new ArgumentException("Request exceeds 8192 bytes");
        }
        public void Dispose() { stopped=true; lock(gate) { if (current!=null) current.Dispose(); } if (Thread.CurrentThread!=thread) thread.Join(2000); }
    }

    internal static class LoginStartup {
        private const string KeyPath="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string ValueName="ZCodePixelPet";
        internal static string Command { get { return "\""+Application.ExecutablePath+"\" --wait-host"; } }
        internal static bool Owns(string command) {
            string prefix="\""+Application.ExecutablePath+"\"";
            return command!=null && (command.Equals(prefix,StringComparison.OrdinalIgnoreCase) || command.StartsWith(prefix+" ",StringComparison.OrdinalIgnoreCase));
        }
        internal static bool Enabled() {
            using(RegistryKey key=Registry.CurrentUser.OpenSubKey(KeyPath,false)) {return key!=null && Owns(key.GetValue(ValueName) as string);}
        }
        internal static void SetEnabled(bool enabled) {
            using(RegistryKey key=Registry.CurrentUser.CreateSubKey(KeyPath)) {
                object raw=key.GetValue(ValueName);string existing=raw as string;
                if(raw!=null && !Owns(existing)) throw new InvalidOperationException("登录项属于另一份桌宠，请先移除旧版本的登录启动。");
                if(enabled) key.SetValue(ValueName,Command,RegistryValueKind.String);
                else if(existing!=null) key.DeleteValue(ValueName,false);
            }
        }
    }

    // On-demand shell recovery for the freeze symptoms on this machine (taskbar half-alive,
    // new windows fail, Task Manager cannot open): those episodes never write explorer hang
    // or crash records, and restarting explorer.exe is the only in-session repair that works
    // with normal user rights. The one-shot watchdog runs as an independent process (an
    // orphaned child survives the pet completely), so even if the pet itself dies mid-restart
    // the shell still comes back. Everything is user-triggered and logged to lifecycle.log.
    internal static class ExplorerRestart {
        private const string WatchdogMutexName="Local\\ZCodePet-explorer-restart-watch";
        internal const string WatchdogMode="--explorer-restart-watch";
        internal static string DefaultDataDir() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZCodePixelPet"); }
        // Direct CreateProcessW with the module path in its own parameter; the command line
        // is the quoted module path plus at most one literal flag word, so the child sees a
        // normal argv. No shell, no quoting puzzles, nothing assembled from request data, and
        // it keeps working while the shell is gone, unlike shell-based launching.
        internal static bool Launch(string image,string arguments,string dataDir,string tag) {
            try {
                string commandLine="\""+image+"\"";
                if(!String.IsNullOrEmpty(arguments)) commandLine=commandLine+" "+arguments;
                Native.STARTUPINFOW startup=new Native.STARTUPINFOW();
                startup.cb=Marshal.SizeOf(typeof(Native.STARTUPINFOW));
                Native.PROCESS_INFORMATION started;
                if(!Native.CreateProcessW(image,commandLine,IntPtr.Zero,IntPtr.Zero,false,0,IntPtr.Zero,null,ref startup,out started)) {
                    LifeLog.Write(dataDir,"explorer-restart launch failed ("+tag+") win32="+Marshal.GetLastWin32Error());
                    return false;
                }
                Native.CloseHandle(started.process);Native.CloseHandle(started.thread);
                LifeLog.Write(dataDir,"explorer-restart launched ("+tag+") image="+image+" pid="+started.processId);
                return true;
            } catch(Exception e) { LifeLog.Write(dataDir,"explorer-restart launch failed ("+tag+") "+e.GetType().Name); return false; }
        }
        internal static List<Process> SessionExplorers(int session) {
            List<Process> matched=new List<Process>();
            foreach(Process process in Process.GetProcessesByName("explorer")) {
                bool keep=false;
                try { keep=process.SessionId==session; } catch(Exception) { keep=false; }
                if(keep) matched.Add(process); else process.Dispose();
            }
            return matched;
        }
        internal static bool HasSessionExplorer(int session) {
            List<Process> matched=SessionExplorers(session);
            bool present=matched.Count>0;
            foreach(Process process in matched) process.Dispose();
            return present;
        }
        internal static void TerminateSessionExplorers(int session,string dataDir) {
            List<Process> matched=SessionExplorers(session);
            if(matched.Count==0) { LifeLog.Write(dataDir,"explorer-restart no explorer running"); return; }
            foreach(Process process in matched) {
                int pid=process.Id;
                try { process.Kill(); LifeLog.Write(dataDir,"explorer-restart killed pid="+pid); }
                catch(Exception e) { LifeLog.Write(dataDir,"explorer-restart kill failed pid="+pid+" "+e.GetType().Name); }
                finally { process.Dispose(); }
            }
        }
        // Full Windows-directory path for the shell, so the launch never depends on the
        // process search path or on a working shell.
        internal static bool TryStart(string dataDir,string tag) {
            return Launch(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),null,dataDir,tag);
        }
        // Service-hosted creation path for the watchdog: if direct launching keeps failing
        // (for example while desktop resources are exhausted), WMI creates the shell from a
        // system process context instead. The target is the fixed literal shell name.
        internal static bool TryStartDetached(string dataDir,string tag) {
            try {
                using(ManagementClass creator=new ManagementClass("Win32_Process")) {
                    object returned=creator.InvokeMethod("Create",new object[]{"explorer.exe",null,null,null});
                    uint code=Convert.ToUInt32(returned);
                    LifeLog.Write(dataDir,"explorer-restart wmi-create "+tag+" rc="+code);
                    return code==0;
                }
            } catch(Exception e) { LifeLog.Write(dataDir,"explorer-restart wmi-create "+tag+" failed "+e.GetType().Name); return false; }
        }
        internal static void SpawnWatchdog(string dataDir) {
            string image=Application.ExecutablePath;
            Thread worker=new Thread(delegate() { Launch(image,WatchdogMode,dataDir,"watchdog"); });
            worker.IsBackground=true;worker.Name="ZCodePet explorer watchdog spawn";worker.Start();
        }
        // Headless mode of the executable: give the in-app restart path six seconds to finish,
        // then keep verifying for ~90 seconds that a session explorer exists, starting one
        // (direct CreateProcess first, service-hosted WMI second) whenever it is missing.
        internal static int WatchdogMain(string dataDir) {
            bool owned=false;
            using(Mutex guard=new Mutex(true,WatchdogMutexName,out owned)) {
                if(!owned) return 0;
                string logDir=String.IsNullOrEmpty(dataDir)?DefaultDataDir():dataDir;
                LifeLog.Write(logDir,"explorer-watch begin");
                Thread.Sleep(6000);
                for(int attempt=1;attempt<=18;attempt++) {
                    if(HasSessionExplorer(Process.GetCurrentProcess().SessionId)) { LifeLog.Write(logDir,"explorer-watch exit shell-present attempt="+attempt); return 0; }
                    if(!TryStart(logDir,"watchdog")) TryStartDetached(logDir,"watchdog");
                    Thread.Sleep(5000);
                }
                LifeLog.Write(logDir,"explorer-watch exit timeout");
                return 1;
            }
        }
    }

    // Flat light "modern minimal" palette for the context menus: single background, subtle
    // rounded hover, thin separator, no gradient image gutter; corner rounding comes from
    // DWM on Win11 (Native.RoundWindow).
    internal sealed class ModernMenuColors : ProfessionalColorTable {
        internal static readonly Color Background=Color.FromArgb(250,250,250);
        internal static readonly Color Border=Color.FromArgb(226,226,226);
        internal static readonly Color Hover=Color.FromArgb(231,231,231);
        internal static readonly Color Separator=Color.FromArgb(234,234,234);
        internal static readonly Color Accent=Color.FromArgb(64,64,64);
        public override Color ToolStripDropDownBackground { get { return Background; } }
        public override Color ImageMarginGradientBegin { get { return Background; } }
        public override Color ImageMarginGradientMiddle { get { return Background; } }
        public override Color ImageMarginGradientEnd { get { return Background; } }
        public override Color MenuBorder { get { return Border; } }
        public override Color MenuItemBorder { get { return Color.Transparent; } }
        public override Color MenuItemSelected { get { return Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return Hover; } }
        public override Color MenuItemPressedGradientEnd { get { return Hover; } }
        public override Color MenuItemPressedGradientMiddle { get { return Hover; } }
        public override Color SeparatorDark { get { return Separator; } }
        public override Color SeparatorLight { get { return Separator; } }
        public override Color CheckBackground { get { return Color.Transparent; } }
        public override Color CheckSelectedBackground { get { return Color.Transparent; } }
        public override Color CheckPressedBackground { get { return Color.Transparent; } }
        public override Color ButtonSelectedBorder { get { return Color.Transparent; } }
        public override Color ButtonSelectedGradientBegin { get { return Hover; } }
        public override Color ButtonSelectedGradientEnd { get { return Hover; } }
        public override Color ButtonSelectedGradientMiddle { get { return Hover; } }
        public override Color ButtonPressedGradientBegin { get { return Hover; } }
        public override Color ButtonPressedGradientEnd { get { return Hover; } }
        public override Color ButtonPressedGradientMiddle { get { return Hover; } }
        public override Color ButtonCheckedGradientBegin { get { return Hover; } }
        public override Color ButtonCheckedGradientEnd { get { return Hover; } }
        public override Color ButtonCheckedHighlight { get { return Hover; } }
        public override Color ButtonCheckedHighlightBorder { get { return Color.Transparent; } }
        public override Color ToolStripBorder { get { return Border; } }
        public override Color ToolStripGradientBegin { get { return Background; } }
        public override Color ToolStripGradientMiddle { get { return Background; } }
        public override Color ToolStripGradientEnd { get { return Background; } }
        public override Color ToolStripContentPanelGradientBegin { get { return Background; } }
        public override Color ToolStripContentPanelGradientEnd { get { return Background; } }
        public override Color ToolStripPanelGradientBegin { get { return Background; } }
        public override Color ToolStripPanelGradientEnd { get { return Background; } }
        public override Color StatusStripGradientBegin { get { return Background; } }
        public override Color StatusStripGradientEnd { get { return Background; } }
        public override Color GripDark { get { return Separator; } }
        public override Color GripLight { get { return Separator; } }
    }

    internal sealed class ModernMenuRenderer : ToolStripProfessionalRenderer {
        internal ModernMenuRenderer() : base(new ModernMenuColors()) { RoundedEdges=false; }
        private static void FillRounded(Graphics graphics,Rectangle bounds,int radius,Brush fill) {
            using(System.Drawing.Drawing2D.GraphicsPath path=new System.Drawing.Drawing2D.GraphicsPath()) {
                int d=radius*2;
                path.AddArc(bounds.X,bounds.Y,d,d,180,90);
                path.AddArc(bounds.Right-d,bounds.Y,d,d,270,90);
                path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);
                path.AddArc(bounds.X,bounds.Bottom-d,d,d,90,90);
                path.CloseFigure();
                graphics.FillPath(fill,path);
            }
        }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
            using(SolidBrush brush=new SolidBrush(ModernMenuColors.Background)) e.Graphics.FillRectangle(brush,e.AffectedBounds);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
            Rectangle bounds=new Rectangle(0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);
            using(Pen pen=new Pen(ModernMenuColors.Border)) e.Graphics.DrawRectangle(pen,bounds);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
            ToolStripMenuItem item=e.Item as ToolStripMenuItem;
            if(item==null || !item.Enabled || (!item.Selected && !item.Pressed)) return;
            // Inset from the popup border on both sides like modern system menus, instead of
            // a full-width classic bar.
            Rectangle bounds=new Rectangle(4,1,e.Item.Width-9,e.Item.Height-3);
            using(SolidBrush brush=new SolidBrush(ModernMenuColors.Hover)) FillRounded(e.Graphics,bounds,5,brush);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
            int y=e.Item.Height/2;
            using(Pen pen=new Pen(ModernMenuColors.Separator)) e.Graphics.DrawLine(pen,10,y,e.Item.Width-10,y);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
            Rectangle r=e.ArrowRectangle;
            if(r.Width<8 || r.Height<8) return;
            using(Pen pen=new Pen(ModernMenuColors.Accent,1.6f)) {
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int mid=r.Top+r.Height/2;
                e.Graphics.DrawLine(pen,r.Left+3,mid-3,r.Left+6,mid);
                e.Graphics.DrawLine(pen,r.Left+6,mid,r.Left+3,mid+3);
            }
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            Rectangle r=e.ImageRectangle;
            if(r.Width<12 || r.Height<12) return;
            using(Pen pen=new Pen(ModernMenuColors.Accent,1.7f)) {
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawLine(pen,r.Left+3,r.Top+8,r.Left+6,r.Top+11);
                e.Graphics.DrawLine(pen,r.Left+6,r.Top+11,r.Left+11,r.Top+4);
            }
        }
    }

    internal sealed class PetWindow : Form {
        private readonly Atlas atlas;
        private readonly string dataDir;
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private readonly PetModel model=new PetModel();
        private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer hostTimer=new System.Windows.Forms.Timer();
        private readonly int desktopSession=Process.GetCurrentProcess().SessionId;
        private Settings settings;
        private PipeServer server;
        private string state="idle";
        private long stateSince;
        private int lastRow=-1,lastFrame=-1;
        private Bitmap displayed;
        private bool shutdown,initialHidden,dragging,hovered;
        private string transient;
        private readonly HoverLeaveAnimation hoverLeave=new HoverLeaveAnimation();
        private int lastDragScreenX;
        private Point dragCursor,dragLocation;
        private int gaze=-1;
        private int absentHostChecks;
        private bool greeted;
        private NotifyIcon tray;
        private IntPtr trayIconHandle;
        private readonly bool watchHost,waitHost;
        private readonly string hostProcess;
        private const int HotkeyId=0x5049;
        private ContextMenuStrip windowMenu;
        private IntPtr menuPreviousForeground;
        private bool hotkeyRegistered;
        private long lastExplorerRestartAt=-100000;
        private readonly System.Windows.Forms.Timer explorerStartTimer=new System.Windows.Forms.Timer();
        private int explorerStartAttempts;
        private Font menuFont;
        internal PetWindow(string atlasPath,string pipe,string dir,bool startHidden,bool watchHost,bool waitHost,string hostProcess) {
            atlas=new Atlas(atlasPath); dataDir=dir;settings=Settings.Load(dir);initialHidden=startHidden;
            model.IdleQueueEnabled=!settings.ReducedMotion;
            this.watchHost=watchHost;this.waitHost=waitHost;this.hostProcess=hostProcess;
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;Text="Aemeath Mini Pixel Pet";
            // Codex mascot uses cursor-interaction (=pointer/hand) and grabbing while pressed;
            // HTTRANSPARENT areas never hit this window, so the hand only shows over the sprite.
            Cursor=Cursors.Hand;
            Size=new Size((int)Math.Round(AtlasContract.BaseWidth*settings.Scale),(int)Math.Round(AtlasContract.BaseHeight*settings.Scale));
            Rectangle area=Screen.PrimaryScreen.WorkingArea;Location=new Point(area.Right-Width-24,area.Bottom-Height-24);
            BuildMenu();BuildTray();if(!startHidden) ManualAutostart(true);timer.Interval=250;timer.Tick+=Tick;
            IntPtr handle=Handle;
            explorerStartTimer.Tick+=ExplorerStartTick;
            hotkeyRegistered=Native.RegisterHotKey(Handle,HotkeyId,0x2|0x1|0x4000,0x52);
            LifeLog.Write(dataDir,hotkeyRegistered?"hotkey ctrl+alt+r registered":"hotkey ctrl+alt+r busy");
            server=new PipeServer(pipe,DispatchOnUI,delegate { if (!shutdown) try { BeginInvoke((MethodInvoker)Close); } catch (InvalidOperationException) {} },
                delegate(Exception error) {LifeLog.Write(dataDir,"ipc-fatal "+error.GetType().Name);if(!shutdown) try {BeginInvoke((MethodInvoker)Close);}catch(InvalidOperationException) {}});
            Render(0,0);
            if(watchHost||waitHost) {hostTimer.Interval=5000;hostTimer.Tick+=CheckHost;hostTimer.Start();}
            LifeLog.Write(dataDir,"window-ready waitHost="+waitHost+" watchHost="+watchHost+" hidden="+initialHidden);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams p=base.CreateParams;p.ExStyle|=0x80000|0x80|0x08000000;return p; } }
        protected override void SetVisibleCore(bool value) { if (initialHidden && value) { initialHidden=false;base.SetVisibleCore(false);return; } base.SetVisibleCore(value); }
        protected override void OnShown(EventArgs e) { base.OnShown(e);Render(lastRow<0?0:lastRow,lastFrame<0?0:lastFrame); }
        protected override void OnVisibleChanged(EventArgs e) {
            base.OnVisibleChanged(e);
            if(settings==null || shutdown) return;
            if(!Visible) {timer.Stop();hovered=false;dragging=false;transient=null;hoverLeave.Clear();Capture=false;}
            else Tick(null,EventArgs.Empty);
        }
        private void CheckHost(object sender,EventArgs args) {
            Process[] hosts=null;
            try {
                hosts=Process.GetProcessesByName(hostProcess);
                bool present=false;foreach(Process process in hosts) if(process.SessionId==desktopSession) {present=true;break;}
                absentHostChecks=present?0:absentHostChecks+1;
                if (waitHost) {
                    // Resident tray app: ZCode presence is only a display trigger. When the host is
                    // gone the window hides but the process stays in the tray; user show/hide
                    // choices persist via autostart.json and never close the app.
                    if (absentHostChecks>=2) { if (Visible) { Hide(); LifeLog.Write(dataDir,"hide host-absent"); } }
                    else {
                        if (!Visible && Settings.AutoStartAllowed(dataDir)) { Show(); LifeLog.Write(dataDir,"show host-present"); }
                    }
                } else if(absentHostChecks>=2) { LifeLog.Write(dataDir,"close host-absent watch-mode"); Close(); }
                // Topmost is not exclusive: another topmost overlay raised later (e.g. the ChatGPT
                // desktop app's overlay window) can cover the pet. Re-assert z-order while visible,
                // but never while the menu is open - the menu is topmost for its lifetime and the
                // pet would otherwise be re-raised straight over it.
                if (Visible && !MenuOpen) Native.SetWindowPos(Handle,(IntPtr)(-1),0,0,0,0,0x0001|0x0002|0x0010|0x0200);
            } catch(System.ComponentModel.Win32Exception) {absentHostChecks=0;}
              catch(InvalidOperationException) {absentHostChecks=0;}
            finally {if(hosts!=null) foreach(Process p in hosts) p.Dispose();}
        }
        private void Tick(object sender,EventArgs args) {
            if (shutdown || !Visible) return;
            long now=clock.ElapsedMilliseconds;
            // Codex plays a one-shot waving greeting on first-awake; mirror it on this
            // instance's first visible moment when no task state is active (hover still wins).
            if (Visible && !greeted) {
                greeted=true;
                if (model.Resolve(now)=="idle" && !hovered && transient==null) {
                    model.TemporaryState="waving";model.QueueTemporary=false;model.TemporaryUntil=now+3*AtlasContract.Cycle(3);
                }
            }
            // Display-state pipeline mirrors the Codex desktop mascot:
            // transient (drag direction) > hover (jumping) > aggregated task state.
            ResolveState(now);
            int row,frame;ComputeFrame(now,out row,out frame);
            // The window repositions every animation frame, which lets the system fall back to
            // the arrow cursor; Codex shows pointer/grabbing, so reassert it while hovered/dragging.
            if (Visible && dragging) Native.SetCursor(Cursors.SizeAll.Handle);
            else if (Visible && hovered) Native.SetCursor(Cursors.Hand.Handle);
            if (Visible && (row!=lastRow || frame!=lastFrame)) Render(row,frame);
            bool leaveActive=HoverLeaveActive(now);
            timer.Interval=settings.ReducedMotion?500:(settings.FollowPointer||dragging?33:Math.Max(16,Math.Min(250,AtlasContract.NextFrameDelay(leaveActive?"running":state,now-(leaveActive?hoverLeave.StartedAt:stateSince),settings.Speed))));
            timer.Start();
        }
        private void ResolveState(long now) {
            string next=model.Resolve(now);
            if(transient!=null) next=transient;else if(hovered) next="jumping";
            if(next!=state) {state=next;stateSince=now;}
        }
        private bool HoverLeaveActive(long now) {return Visible && !hovered && !dragging && transient==null && hoverLeave.Active(now,settings.Speed);}
        private void ComputeFrame(long now,out int row,out int frame) {
            // Always acknowledge pointer leave, including idle. This does not alter task state.
            if(HoverLeaveActive(now)) {gaze=-1;row=7;frame=settings.ReducedMotion?0:hoverLeave.Frame(now,settings.Speed);return;}
            // Codex looks at its editor caret (or a computer-use cursor) via a static frame, only in
            // idle/running/waving, and the desktop pet has no such stream. Pointer following is an
            // opt-in extra (defaults off) so it can never mask task-state animations on unhover.
            gaze=-1;
            if (Visible && settings.FollowPointer && !settings.ReducedMotion && !hovered && transient==null &&
                (state=="idle" || state=="running" || state=="waving") && !dragging) {
                Point pointer=Cursor.Position;
                double dx=pointer.X-(Left+Width/2.0),dy=pointer.Y-(Top+Height/2.0);
                double distance=Math.Sqrt(dx*dx+dy*dy);
                if (distance>Width*0.62 && distance<=Width*2.5) gaze=AtlasContract.Gaze(dx,dy);
            }
            if (gaze>=0) {row=9+gaze/8;frame=gaze%8;}
            else {AtlasContract.SequenceFrame(state,now-stateSince,settings.Speed,out row,out frame);}
            if (settings.ReducedMotion) frame=0;
        }
        private void Render(int row,int frame) {
            displayed=atlas.Render(row,frame,settings.Scale);Size=displayed.Size;
            IntPtr screen=Native.GetDC(IntPtr.Zero), memory=IntPtr.Zero, bitmap=IntPtr.Zero, old=IntPtr.Zero;
            try {
                memory=Native.CreateCompatibleDC(screen);bitmap=displayed.GetHbitmap(Color.FromArgb(0));old=Native.SelectObject(memory,bitmap);
                Native.POINT point=new Native.POINT(Left,Top),origin=new Native.POINT(0,0);Native.SIZE size=new Native.SIZE(Width,Height);
                Native.BLEND blend=new Native.BLEND();blend.Alpha=255;blend.Format=1;
                if (!Native.UpdateLayeredWindow(Handle,screen,ref point,ref size,memory,ref origin,0,ref blend,2)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                lastRow=row;lastFrame=frame;
            } finally { if (old!=IntPtr.Zero) Native.SelectObject(memory,old);if (bitmap!=IntPtr.Zero) Native.DeleteObject(bitmap);if (memory!=IntPtr.Zero) Native.DeleteDC(memory);if (screen!=IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero,screen); }
        }
        protected override void WndProc(ref Message m) {
            if (m.Msg==0x84 && displayed!=null) {
                long packed=m.LParam.ToInt64();Point p=PointToClient(new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff)));
                if (p.X<0 || p.Y<0 || p.X>=displayed.Width || p.Y>=displayed.Height || displayed.GetPixel(p.X,p.Y).A<=8) {m.Result=new IntPtr(-1);return;}
            }
            if (m.Msg==0x21) {m.Result=new IntPtr(3);return;}
            if (m.Msg==0x312 && m.WParam.ToInt64()==HotkeyId) {RestartExplorer("hotkey");return;}
            base.WndProc(ref m);
        }
        protected override void OnMouseEnter(EventArgs e) {base.OnMouseEnter(e);if (!hovered) {hoverLeave.Clear();hovered=true;Tick(null,EventArgs.Empty);}}
        protected override void OnMouseLeave(EventArgs e) {base.OnMouseLeave(e);if (hovered) {hovered=false;if(!dragging) hoverLeave.Start(clock.ElapsedMilliseconds);Tick(null,EventArgs.Empty);}}
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e); if (e.Button==MouseButtons.Left) {
                hoverLeave.Clear();dragging=true;dragCursor=Cursor.Position;dragLocation=Location;Capture=true;
                Cursor=Cursors.SizeAll;Native.SetCursor(Cursors.SizeAll.Handle); // capture suppresses WM_SETCURSOR, so set it explicitly
                // Codex clears the drag transient on every drag start.
                if (transient!=null) transient=null;
                lastDragScreenX=dragCursor.X;
                Tick(null,EventArgs.Empty);
            }
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);if (dragging) {
                Native.SetCursor(Cursors.SizeAll.Handle);
                Point p=Cursor.Position;
                Location=Clamp(new Point(dragLocation.X+p.X-dragCursor.X,dragLocation.Y+p.Y-dragCursor.Y));
                // Codex switches to a directional run animation on >=4px horizontal samples.
                int delta=p.X-lastDragScreenX;
                if (Math.Abs(delta)>=4) {
                    string direction=delta>0?"running-right":"running-left";
                    if (direction!=transient) transient=direction;
                    lastDragScreenX=p.X;
                }
                Tick(null,EventArgs.Empty);
            }
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            // Codex clears the drag transient on pointerup before anything else.
            base.OnMouseUp(e);
            if (e.Button==MouseButtons.Left) {dragging=false;Capture=false;Cursor=Cursors.Hand;Native.SetCursor(Cursors.Hand.Handle);if (transient!=null) {transient=null;Tick(null,EventArgs.Empty);}}
            else if (e.Button==MouseButtons.Right) ShowPetMenu();
        }
        private bool MenuOpen { get { return windowMenu!=null && windowMenu.Visible; } }
        // Flat modern chrome shared by the pet menu and the tray menu: Segoe UI, roomy items,
        // no image gutter, custom renderer, DWM rounded corners, and topmost while open so the
        // always-topmost pet can never cover the menu or its submenus.
        private void StyleMenu(ContextMenuStrip menu) {
            if(menuFont==null) menuFont=new Font("Segoe UI",9.5F,FontStyle.Regular,GraphicsUnit.Point);
            menu.Renderer=new ModernMenuRenderer();
            menu.Font=menuFont;
            menu.ShowImageMargin=false;menu.ShowCheckMargin=true;
            menu.Padding=new Padding(6,7,6,7);
            menu.Opened+=MenuOpenedChrome;
            foreach(ToolStripItem item in menu.Items) StyleItem(item);
        }
        private void StyleItem(ToolStripItem item) {
            item.Padding=new Padding(8,4,8,4);
            item.Font=menuFont;
            ToolStripMenuItem parent=item as ToolStripMenuItem;
            if(parent==null || !parent.HasDropDownItems) return;
            ToolStripDropDown child=parent.DropDown;
            child.Renderer=new ModernMenuRenderer();
            child.Font=menuFont;
            child.Padding=new Padding(6,7,6,7);
            child.Opened+=MenuOpenedChrome;
            foreach(ToolStripItem nested in child.Items) {nested.Padding=new Padding(8,4,8,4);nested.Font=menuFont;}
        }
        private void MenuOpenedChrome(object sender,EventArgs e) {
            ToolStripDropDown menu=sender as ToolStripDropDown;
            if(menu==null) return;
            // Deferred until the dropdown finished its show transition: mutating the popup
            // window synchronously inside Opened makes ToolStripDropDown tear it down again.
            try { BeginInvoke((MethodInvoker)delegate {
                if(menu.IsDisposed) return;
                Native.RoundWindow(menu.Handle);
                // Topmost while open; the pet's own topmost re-assert pauses while MenuOpen.
                Native.SetWindowPos(menu.Handle,(IntPtr)(-1),0,0,0,0,0x0001|0x0002|0x0010);
                AvoidPetOverlap(menu);
            }); } catch(InvalidOperationException) {}
        }
        // Last-resort positional safety on top of MenuSpawnPoint: if a menu (tray menu or a
        // screen-edge-clamped one) still intersects the pet, shift it clear.
        private void AvoidPetOverlap(ToolStripDropDown menu) {
            try {
                Rectangle pet=new Rectangle(Left,Top,Width,Height);
                Rectangle bounds=menu.Bounds;
                if(!bounds.IntersectsWith(pet)) return;
                Rectangle work=Screen.FromPoint(Location).WorkingArea; int gap=8;
                int x=bounds.X-(bounds.Right-pet.Left+gap);
                if(x>=work.Left) {menu.Location=new Point(x,bounds.Y);return;}
                int y=bounds.Y-(bounds.Bottom-pet.Top+gap);
                if(y>=work.Top) {menu.Location=new Point(bounds.X,y);return;}
                menu.Location=new Point(work.Left,Math.Max(work.Top,Math.Min(bounds.Y,work.Bottom-bounds.Height)));
            } catch(ObjectDisposedException) {}
        }
        // Open the pet menu beside the sprite (left of it, vertically centred) instead of at
        // the cursor: the cursor is on the pet, and a menu under the topmost pet is covered.
        private Point MenuSpawnPoint() {
            try {
                Size size=windowMenu.GetPreferredSize(Size.Empty);
                if(size.Width<40 || size.Height<40) size=new Size(210,300);
                Rectangle work=Screen.FromPoint(Location).WorkingArea; int gap=10;
                int x=Left-size.Width-gap;
                if(x<work.Left) x=Right+gap;
                if(x+size.Width>work.Right) x=Math.Max(work.Left,work.Right-size.Width);
                int y=Top+(Height-size.Height)/2;
                if(y<work.Top) y=work.Top;
                if(y+size.Height>work.Bottom) y=work.Bottom-size.Height;
                return new Point(x,Math.Max(work.Top,y));
            } catch(Exception) { return Cursor.Position; }
        }
        private void ShowPetMenu() {
            if (windowMenu==null || windowMenu.Visible) return;
            try {
                menuPreviousForeground=Native.GetForegroundWindow();
                Point spawn=MenuSpawnPoint();
                windowMenu.Show(spawn);
                // Context menus only honour ESC and outside-click dismissal while they hold
                // the foreground. This thread just received the right click (the last input
                // event), so raising the dropdown succeeds the same way tray menus do it.
                Native.SetForegroundWindow(windowMenu.Handle);
                windowMenu.Focus();
            } catch(ObjectDisposedException e) { LifeLog.Write(dataDir,"menu show failed "+e.GetType().Name); }
              catch(InvalidOperationException e) { LifeLog.Write(dataDir,"menu show failed "+e.GetType().Name); }
        }
        private void MenuClosedRestoreForeground(object sender,ToolStripDropDownClosedEventArgs e) {
            IntPtr previous=menuPreviousForeground;menuPreviousForeground=IntPtr.Zero;
            if (previous!=IntPtr.Zero && previous!=Handle) Native.SetForegroundWindow(previous);
        }
        // Maximum-reliability shell restart, ordered so every step still works while the
        // shell is already wedged: watchdog first (independent process, finishes the job if
        // the pet dies), then TerminateProcess on the session's explorers (message pumps
        // are irrelevant to it), then a delayed direct CreateProcess launch. Ctrl+Alt+R is
        // the second entry point for cases where the menu cannot be reached.
        private void RestartExplorer(string source) {
            long now=clock.ElapsedMilliseconds;
            if (now-lastExplorerRestartAt<15000) {LifeLog.Write(dataDir,"explorer-restart ignored debounced source="+source);return;}
            lastExplorerRestartAt=now;
            LifeLog.Write(dataDir,"explorer-restart trigger source="+source);
            ExplorerRestart.SpawnWatchdog(dataDir);
            ExplorerRestart.TerminateSessionExplorers(desktopSession,dataDir);
            explorerStartAttempts=0;
            explorerStartTimer.Interval=800;
            explorerStartTimer.Stop();explorerStartTimer.Start();
            if (model.Resolve(now)=="idle" && !hovered && transient==null) {
                model.TemporaryState="waving";model.QueueTemporary=false;model.TemporaryUntil=now+3*AtlasContract.Cycle(3);
            }
            Tick(null,EventArgs.Empty);
        }
        private void ExplorerStartTick(object sender,EventArgs args) {
            explorerStartTimer.Stop();
            if (ExplorerRestart.HasSessionExplorer(desktopSession)) return;
            if (explorerStartAttempts>=4) return; // the watchdog keeps retrying on its own
            explorerStartAttempts++;
            if (ExplorerRestart.TryStart(dataDir,"inline-"+explorerStartAttempts)) return;
            explorerStartTimer.Interval=1500;
            explorerStartTimer.Start();
        }
        protected override void OnMouseCaptureChanged(EventArgs e) {base.OnMouseCaptureChanged(e);if (!Capture) {dragging=false;Cursor=Cursors.Hand;Native.SetCursor(Cursors.Hand.Handle);if (transient!=null) {transient=null;Tick(null,EventArgs.Empty);}}}
        private Point Clamp(Point point) {
            Rectangle area=Screen.FromPoint(new Point(point.X+Width/2,point.Y+Height/2)).WorkingArea;
            return new Point(Math.Max(area.Left,Math.Min(point.X,area.Right-Width)),Math.Max(area.Top,Math.Min(point.Y,area.Bottom-Height)));
        }
        private void AddSettingsItems(ContextMenuStrip menu) {
            ToolStripMenuItem speeds=new ToolStripMenuItem("播放速度");
            foreach(double value in new[] {.5,1,1.5,2}) {double captured=value;speeds.DropDownItems.Add(value.ToString("0.0",CultureInfo.InvariantCulture)+"×",null,delegate {MenuConfigure("speed",captured);});}
            menu.Items.Add(speeds);ToolStripMenuItem scales=new ToolStripMenuItem("显示大小");
            foreach(double value in new[] {.5,.75,1,1.5,2}) {double captured=value;scales.DropDownItems.Add(value.ToString("0.##",CultureInfo.InvariantCulture)+"×",null,delegate {MenuConfigure("scale",captured);});}
            menu.Items.Add(scales);
            ToolStripMenuItem follow=new ToolStripMenuItem("鼠标观察");follow.Click+=delegate {MenuConfigure("followPointer",!settings.FollowPointer);};menu.Items.Add(follow);
            ToolStripMenuItem reduced=new ToolStripMenuItem("减少动态效果");reduced.Click+=delegate {MenuConfigure("reducedMotion",!settings.ReducedMotion);};menu.Items.Add(reduced);
            ToolStripMenuItem login=new ToolStripMenuItem("随 Windows 登录启动");
            login.Click+=delegate {try {LoginStartup.SetEnabled(!LoginStartup.Enabled());}catch(Exception e) {MessageBox.Show(this,e.Message,"无法更改登录启动",MessageBoxButtons.OK,MessageBoxIcon.Error);}};
            menu.Items.Add(login);
            menu.Items.Add("关于与隐私",null,delegate {
                MessageBox.Show(this,"Aemeath Mini Pixel Pet "+Application.ProductVersion+Environment.NewLine+
                    "本地独立桌宠，非 ZCode 或角色权利方的官方产品。"+Environment.NewLine+
                    "默认不联网，无遥测、下载器或自动更新；仅接收本机当前用户的任务状态和会话摘要标识。"+Environment.NewLine+
                    "不收集聊天正文、键盘输入或屏幕内容。鼠标观察可选，默认关闭。"+Environment.NewLine+
                    "右键菜单或 Ctrl+Alt+R 可重启资源管理器（强杀后立即拉回，含独立看护兜底；会关闭已打开的文件夹窗口）。"+Environment.NewLine+
                    "设置与限量日志保存在："+dataDir+Environment.NewLine+
                    "登录自启由菜单单独控制；隐藏保留托盘，退出结束进程。"+Environment.NewLine+
                    "代码与素材许可说明见随包的 LICENSE、PRIVACY.md 和 THIRD-PARTY-NOTICES.md。",
                    "关于与隐私",MessageBoxButtons.OK,MessageBoxIcon.Information);
            });
            menu.Opening+=delegate {foreach(ToolStripItem item in menu.Items) {ToolStripMenuItem m=item as ToolStripMenuItem;if(m==null) continue;
                if(m.Text=="鼠标观察") m.Checked=settings.FollowPointer;if(m.Text=="减少动态效果") m.Checked=settings.ReducedMotion;}
                try {login.Checked=LoginStartup.Enabled();login.Enabled=true;} catch(System.Security.SecurityException) {login.Enabled=false;}catch(UnauthorizedAccessException) {login.Enabled=false;}
            };
        }
        private void BuildMenu() {
            ContextMenuStrip menu=new ContextMenuStrip();AddSettingsItems(menu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("重启资源管理器",null,delegate {RestartExplorer("menu");});
            menu.Items.Add("隐藏（托盘图标仍在）",null,delegate {ManualAutostart(false);Hide();});
            menu.Items.Add("退出桌宠",null,delegate {ManualAutostart(false);Close();});
            menu.AutoClose=true;
            menu.Closed+=MenuClosedRestoreForeground;
            windowMenu=menu;
            StyleMenu(menu);
            // Deliberately NOT assigned to Form.ContextMenuStrip: WinForms would show the menu
            // from this no-activate window without giving it the foreground, which is exactly
            // why ESC and outside clicks could not close it before.
        }
        private void BuildTray() {
            tray=new NotifyIcon();
            Bitmap face=atlas.Render(0,0,32.0/AtlasContract.BaseWidth);trayIconHandle=face.GetHicon();tray.Icon=Icon.FromHandle(trayIconHandle);
            tray.Text="Aemeath Mini 像素桌宠";
            ContextMenuStrip menu=new ContextMenuStrip();AddSettingsItems(menu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("重启资源管理器",null,delegate {RestartExplorer("tray-menu");});
            menu.Items.Add("显示宠物",null,delegate {ManualAutostart(true);Show();Render(lastRow<0?0:lastRow,lastFrame<0?0:lastFrame);Tick(null,EventArgs.Empty);});
            menu.Items.Add("隐藏宠物",null,delegate {ManualAutostart(false);Hide();});
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出桌宠",null,delegate {ManualAutostart(false);Close();});
            tray.ContextMenuStrip=menu;
            StyleMenu(menu);
            tray.DoubleClick+=delegate {
                if (Visible) {ManualAutostart(false);Hide();}
                else {ManualAutostart(true);Show();Render(lastRow<0?0:lastRow,lastFrame<0?0:lastFrame);Tick(null,EventArgs.Empty);}
            };
            tray.Visible=true;
            LifeLog.Write(dataDir,"tray-ready");
        }
        private void MenuConfigure(string key,object value) {
            Dictionary<string,object> answer=HandleCommand(new Dictionary<string,object>{{"op","configure"},{key,value}});
            if (!(bool)answer["ok"]) MessageBox.Show(this,(string)answer["error"],"ZCode Pet",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
        private void ManualAutostart(bool allowed) {
            try {Settings.SaveAutoStart(dataDir,allowed);}
            catch(IOException) {MessageBox.Show(this,"无法保存自动显示设置；可通过插件再次调整。","ZCode Pet",MessageBoxButtons.OK,MessageBoxIcon.Error);}
              catch(UnauthorizedAccessException) {MessageBox.Show(this,"无法保存自动显示设置；可通过插件再次调整。","ZCode Pet",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
        private string TryPersistDisplayChoice(bool allowed) {
            try {Settings.SaveAutoStart(dataDir,allowed);return null;}
            catch(IOException) {return "Display preference could not be saved; the requested action still completed.";}
            catch(UnauthorizedAccessException) {return "Display preference could not be saved; the requested action still completed.";}
        }
        private Dictionary<string,object> DispatchOnUI(Dictionary<string,object> request) {
            if (shutdown) return Protocol.Error("Companion is shutting down");
            Dictionary<string,object> result=null;Exception error=null;
            using (ManualResetEvent complete=new ManualResetEvent(false)) {
                try {
                    BeginInvoke((MethodInvoker)delegate {try {result=HandleCommand(request);} catch(Exception e) {error=e;} finally {try {complete.Set();} catch(ObjectDisposedException) {}} });
                    if (!complete.WaitOne(2000)) return Protocol.Error("UI dispatch timeout");
                } catch (InvalidOperationException) {return Protocol.Error("Companion is shutting down");}
            }
            if (error!=null) return Protocol.Error("Command failed: " + error.GetType().Name);
            return result??Protocol.Error("Command failed");
        }
        private Dictionary<string,object> HandleCommand(Dictionary<string,object> request) {
            try {
                string op=Protocol.String(request,"op"),preferenceWarning=null;
                switch(op) {
                    case "show": {
                        Protocol.Fields(request,"op","automatic");object automatic;
                        if(request.TryGetValue("automatic",out automatic) && Protocol.Boolean(automatic,"automatic") && !Settings.AutoStartAllowed(dataDir)) break;
                        if(!request.ContainsKey("automatic") || !Protocol.Boolean(request["automatic"],"automatic")) preferenceWarning=TryPersistDisplayChoice(true);
                        Show();Tick(null,EventArgs.Empty);Render(lastRow<0?AtlasContract.Row(state):lastRow,lastFrame<0?0:lastFrame);break;
                    }
                    case "hide": Protocol.Fields(request,"op");preferenceWarning=TryPersistDisplayChoice(false);Hide();LifeLog.Write(dataDir,"hide op");break;
                    case "status": Protocol.Fields(request,"op");break;
                    case "quit": Protocol.Fields(request,"op");preferenceWarning=TryPersistDisplayChoice(false);LifeLog.Write(dataDir,"quit op");break;
                    case "session":
                        Protocol.Fields(request,"op","sessionId","state");model.TemporaryState=null;model.Session(Protocol.String(request,"sessionId"),Protocol.String(request,"state"),clock.ElapsedMilliseconds);Tick(null,EventArgs.Empty);break;
                    case "state": {
                        Protocol.Fields(request,"op","state","durationMs");string target=Protocol.String(request,"state");
                        if (AtlasContract.Row(target)<0) throw new ArgumentException("Invalid animation state");object duration;
                        if (!request.TryGetValue("durationMs",out duration)) throw new ArgumentException("Missing durationMs");
                        double ms=Protocol.Number(duration,1000,30000,"durationMs");if (ms!=Math.Floor(ms)) throw new ArgumentException("durationMs must be an integer");
                        model.TemporaryState=target;model.QueueTemporary=false;model.TemporaryUntil=clock.ElapsedMilliseconds+(long)ms;Tick(null,EventArgs.Empty);break;
                    }
                    case "configure": {
                        Settings candidate=settings.Clone();Settings.Apply(candidate,request,true);candidate.Save(dataDir);
                        bool resize=candidate.Scale!=settings.Scale;settings=candidate;
                        model.IdleQueueEnabled=!settings.ReducedMotion;
                        if (resize) {displayed=null;atlas.ClearCache();Size=new Size((int)Math.Round(AtlasContract.BaseWidth*settings.Scale),(int)Math.Round(AtlasContract.BaseHeight*settings.Scale));Location=Clamp(Location);}
                        gaze=-1;lastRow=-1;lastFrame=-1;Tick(null,EventArgs.Empty);break;
                    }
                    default: throw new ArgumentException("Unknown operation");
                }
                Dictionary<string,object> status=Status();if(preferenceWarning!=null) status.Add("warning",preferenceWarning);return status;
            } catch(ArgumentException e) {return Protocol.Error(e.Message);}
              catch(IOException) {return Protocol.Error("Could not save companion settings");}
              catch(UnauthorizedAccessException) {return Protocol.Error("Could not access companion settings");}
        }
        private Dictionary<string,object> Status() {
            long now=clock.ElapsedMilliseconds;ResolveState(now);int row,frame;ComputeFrame(now,out row,out frame);
            Dictionary<string,object> result=settings.AsJson();result.Add("ok",true);result.Add("state",state);result.Add("visible",Visible);
            result.Add("frame",frame);result.Add("row",row);result.Add("sessionCount",model.Sessions.Count);result.Add("expiredSessions",model.ExpiredSessions);result.Add("pid",Process.GetCurrentProcess().Id);
            result.Add("animationTimerRunning",timer.Enabled);result.Add("animationIntervalMs",timer.Enabled?timer.Interval:0);
            result.Add("menuOpen",MenuOpen);result.Add("hotkeyRegistered",hotkeyRegistered);
            result.Add("interaction",HoverLeaveActive(now)?"hover-leave":"none");return result;
        }
        protected override void OnFormClosed(FormClosedEventArgs e) {
            Cleanup();base.OnFormClosed(e);
        }
        protected override void Dispose(bool disposing) {if(disposing) Cleanup();base.Dispose(disposing);}
        private void Cleanup() {
            if(shutdown) return;
            shutdown=true;LifeLog.Write(dataDir,"exit");
            if(tray!=null) {tray.Visible=false;if(tray.ContextMenuStrip!=null) tray.ContextMenuStrip.Dispose();tray.Dispose();}
            if(trayIconHandle!=IntPtr.Zero) {Native.DestroyIcon(trayIconHandle);trayIconHandle=IntPtr.Zero;}
            timer.Stop();timer.Dispose();hostTimer.Stop();hostTimer.Dispose();if(server!=null) server.Dispose();displayed=null;atlas.Dispose();
            if(hotkeyRegistered) {Native.UnregisterHotKey(Handle,HotkeyId);hotkeyRegistered=false;}
            explorerStartTimer.Stop();explorerStartTimer.Dispose();
            if(menuFont!=null) {menuFont.Dispose();menuFont=null;}
            if(windowMenu!=null) windowMenu.Dispose();
        }
    }

    internal static class Program {
        [STAThread] private static int Main(string[] args) {
            string atlas=null,pipe=null,dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZCodePixelPet"),renderDir=null,hostProcess="ZCode";bool selfTest=false,startHidden=false,pipeSelfTest=false,watchHost=false,waitHost=false,explorerRestartWatch=false;
            try {
                for(int i=0;i<args.Length;i++) {
                    switch(args[i]) {
                        case "--atlas":atlas=Value(args,ref i);break;case "--pipe":pipe=Value(args,ref i);break;
                        case "--data-dir":dir=Value(args,ref i);break;case "--render-dir":renderDir=Value(args,ref i);break;
                        case "--self-test":selfTest=true;break;case "--start-hidden":startHidden=true;break;case "--pipe-self-test":pipeSelfTest=true;break;
                        case "--watch-host":watchHost=true;break;case "--wait-host":waitHost=true;break;
                        case "--host-process":hostProcess=Value(args,ref i);break;
                        case "--explorer-restart-watch":explorerRestartWatch=true;break;
                        default:throw new ArgumentException("Unknown argument: " + args[i]);
                    }
                }
                if(String.IsNullOrEmpty(atlas)) atlas=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","assets","aemeath-mini","spritesheet.png");
                atlas=Path.GetFullPath(atlas);
                if(String.IsNullOrEmpty(dir)) throw new ArgumentException("--data-dir must not be empty");dir=Path.GetFullPath(dir);
                if(String.IsNullOrEmpty(pipe)) {
                    using(SHA256 sha=SHA256.Create()) {
                        pipe="ZCodePet-"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(dir.ToLowerInvariant()))).Replace("-","").ToLowerInvariant().Substring(0,20);
                    }
                }
                if(explorerRestartWatch) return ExplorerRestart.WatchdogMain(dir);
                if(selfTest) {if(String.IsNullOrEmpty(renderDir)) throw new ArgumentException("--render-dir is required for self-test");SelfTest(atlas,Path.GetFullPath(renderDir));return 0;}
                if(String.IsNullOrEmpty(pipe) || !Regex.IsMatch(pipe,"\\A[A-Za-z0-9._-]{1,100}\\z")) throw new ArgumentException("Invalid --pipe name");
                if(!Regex.IsMatch(hostProcess,"\\A[A-Za-z0-9._ -]{1,50}\\z")) throw new ArgumentException("Invalid --host-process name");
                if(watchHost&&waitHost) throw new ArgumentException("--watch-host and --wait-host are mutually exclusive");
                LifeLog.Write(dir,"launch version="+Application.ProductVersion+" waitHost="+waitHost+" startHidden="+startHidden);
                MutexSecurity security=new MutexSecurity();security.SetAccessRuleProtection(true,false);security.AddAccessRule(new MutexAccessRule(WindowsIdentity.GetCurrent().User,MutexRights.FullControl,AccessControlType.Allow));
                bool created;
                using(Mutex instance=new Mutex(true,"Local\\"+pipe,out created,security)) {
                    if(!created) {
                        // A second desktop launch should reveal the existing pet, not silently exit.
                        // Background login launches must respect the user's hidden state.
                        if(startHidden||waitHost||pipeSelfTest) return 3;
                        Dictionary<string,object> reply=Client(pipe,"{\"op\":\"show\"}");
                        if(!(bool)reply["ok"]) throw new InvalidOperationException("Existing companion could not be shown");
                        return 0;
                    }
                    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    using(PetWindow window=new PetWindow(atlas,pipe,dir,startHidden||pipeSelfTest||waitHost,watchHost,waitHost,hostProcess)) {
                        Thread test=null;Exception testError=null;
                        if(pipeSelfTest) {
                            if(String.IsNullOrEmpty(renderDir)) throw new ArgumentException("--render-dir is required for pipe self-test");
                            string evidence=Path.GetFullPath(renderDir);
                            test=new Thread(delegate() {try {PipeTest(pipe,evidence,dir);} catch(Exception e) {testError=e;try {window.BeginInvoke((MethodInvoker)window.Close);}catch(InvalidOperationException) {}} });test.IsBackground=true;test.Start();
                        }
                        Application.Run(window);if(test!=null) test.Join(6000);if(testError!=null) throw testError;
                    }
                    instance.ReleaseMutex();
                }
                return 0;
            } catch(Exception e) {
                if(!String.IsNullOrEmpty(renderDir)) {try {Directory.CreateDirectory(renderDir);File.WriteAllText(Path.Combine(renderDir,"failure.json"),Protocol.Encode(Protocol.Error(e.GetType().Name+": "+e.Message)),new UTF8Encoding(false));}catch(Exception) {}}
                else if(!String.IsNullOrEmpty(dir)) {try {Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"startup-error.json"),Protocol.Encode(Protocol.Error(e.GetType().Name+": "+e.Message)),new UTF8Encoding(false));}catch(Exception) {}}
                if(args.Length==0) MessageBox.Show("桌宠启动失败："+e.Message+Environment.NewLine+"请检查素材文件和用户目录中的 startup-error.json。","Aemeath Mini Pixel Pet",MessageBoxButtons.OK,MessageBoxIcon.Error);
                return 1;
            }
        }
        private static string Value(string[] args,ref int i) {if(i+1>=args.Length) throw new ArgumentException("Missing argument value");return args[++i];}
        private static void Assert(bool condition,string description,List<string> checks) {if(!condition) throw new InvalidOperationException(description);checks.Add(description);}
        private static void SelfTest(string path,string output) {
            Directory.CreateDirectory(output);List<string> checks=new List<string>();
            PipeSecurity pipeSecurity=PipeServer.LocalUserSecurity();bool deniesNetwork=false,allowsUser=false;
            foreach(PipeAccessRule rule in pipeSecurity.GetAccessRules(true,false,typeof(SecurityIdentifier))) {
                if(rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.NetworkSid,null)) && rule.AccessControlType==AccessControlType.Deny) deniesNetwork=true;
                if(rule.IdentityReference.Equals(WindowsIdentity.GetCurrent().User) && rule.AccessControlType==AccessControlType.Allow) allowsUser=true;
            }
            Assert(pipeSecurity.AreAccessRulesProtected && deniesNetwork && allowsUser,"IPC allows the local current user and explicitly denies network logons",checks);
            string busyPipe="ZCodePet-conflict-"+Guid.NewGuid().ToString("N");
            using(NamedPipeServerStream occupied=new NamedPipeServerStream(busyPipe,PipeDirection.InOut,1)) {
                bool pipeRejected=false;Stopwatch elapsed=Stopwatch.StartNew();
                try {using(PipeServer duplicate=new PipeServer(busyPipe,delegate(Dictionary<string,object> r) {return Protocol.Error("unused");},delegate {},delegate(Exception e) {})) {}}
                catch(IOException) {pipeRejected=true;}catch(UnauthorizedAccessException) {pipeRejected=true;}
                Assert(pipeRejected && elapsed.ElapsedMilliseconds<2000,"An occupied pipe rejects startup promptly instead of spinning a background thread",checks);
            }
            Assert(LoginStartup.Owns(LoginStartup.Command) && !LoginStartup.Owns("\"elsewhere.exe\" --wait-host"),"Login setting only owns its own quoted executable path",checks);
            Assert(ExplorerRestart.WatchdogMode=="--explorer-restart-watch","Explorer watchdog mode flag is the documented constant",checks);
            Assert(ExplorerRestart.DefaultDataDir().EndsWith("ZCodePixelPet",StringComparison.OrdinalIgnoreCase),"Explorer watchdog logging defaults to the pet data directory",checks);
            Assert(AtlasContract.Cycle(0)==1100,"Raw idle cycle is 1100 ms without a sixfold slowdown",checks);
            Assert(AtlasContract.Frame(0,279,1)==0 && AtlasContract.Frame(0,280,1)==1,"Idle frame boundary",checks);
            Assert(AtlasContract.Frame(0,1100,1)==0,"Idle loops at its exact duration",checks);
            Assert(AtlasContract.Frame(7,820*17+120,1)==1,"Work still loops after seventeen cycles",checks);
            Assert(AtlasContract.Frame(0,140,2)==1 && AtlasContract.Frame(0,560,.5)==1,"Speed adjustment changes timing without changing frames",checks);
            Assert(AtlasContract.DisplayCycle(0)==6600,"Displayed idle cycle is sixfold slowed like Codex",checks);
            Assert(AtlasContract.DisplayFrame(0,1679,1)==0 && AtlasContract.DisplayFrame(0,1680,1)==1,"Slowed idle frame boundary",checks);
            Assert(AtlasContract.DisplayFrame(0,839,2)==0 && AtlasContract.DisplayFrame(0,840,2)==1,"Speed applies to the slowed idle loop",checks);
            int seqRow,seqFrame;
            AtlasContract.SequenceFrame("idle",2000,1,out seqRow,out seqFrame);
            Assert(seqRow==0,"Idle sequence stays on the idle row",checks);
            AtlasContract.SequenceFrame("jumping",3*840-1,1,out seqRow,out seqFrame);
            Assert(seqRow==4,"Transient jumping still plays in its third loop",checks);
            AtlasContract.SequenceFrame("jumping",3*840,1,out seqRow,out seqFrame);
            Assert(seqRow==0,"Transient jumping settles into idle after exactly three loops",checks);
            AtlasContract.SequenceFrame("waving",3*700-1,1,out seqRow,out seqFrame);
            Assert(seqRow==3,"First-awake waving still plays in its third loop",checks);
            AtlasContract.SequenceFrame("waving",3*700+1,1,out seqRow,out seqFrame);
            Assert(seqRow==0,"First-awake waving settles into idle after three loops",checks);
            AtlasContract.SequenceFrame("running",3*820-1,1,out seqRow,out seqFrame);
            Assert(seqRow==7,"Work animation still plays in its third loop",checks);
            AtlasContract.SequenceFrame("running",3*820+1,1,out seqRow,out seqFrame);
            Assert(seqRow==0,"Work animation settles into idle after three loops",checks);
            AtlasContract.SequenceFrame("running",3*820+2000,1,out seqRow,out seqFrame);
            int idleRow,idleFrame;AtlasContract.SequenceFrame("idle",2000,1,out idleRow,out idleFrame);
            Assert(seqRow==idleRow && seqFrame==idleFrame,"Settled idle uses the slowed idle timing",checks);
            Assert(AtlasContract.Gaze(0,-1)==0 && AtlasContract.Gaze(1,0)==4 && AtlasContract.Gaze(0,1)==8 && AtlasContract.Gaze(-1,0)==12,"Clockwise look cardinal mapping",checks);
            for(int i=0;i<16;i++) {double angle=i*Math.PI/8;Assert(AtlasContract.Gaze(Math.Sin(angle),-Math.Cos(angle))==i,"Look sector "+i,checks);}
            PetModel model=new PetModel();string a=new String('a',64),b=new String('b',64),c=new String('c',64);
            model.Session(a,"running");model.Session(b,"waiting");model.Session(c,"failed");Assert(model.Resolve(0)=="waiting","Waiting takes priority over failure and work",checks);
            model.Session(b,"ended");Assert(model.Resolve(0)=="failed","Ended session removed from aggregation",checks);
            model.Session(c,"review");Assert(model.Resolve(0)=="review","Review takes priority over work",checks);
            model.TemporaryState="waving";model.TemporaryUntil=1000;Assert(model.Resolve(999)=="waving" && model.Resolve(1000)=="review","Temporary animation expires back to session state",checks);
            Assert(model.Resolve(30000)=="running","Review expires after thirty seconds without marking other work complete",checks);
            Assert(model.Sessions.Count==1,"Expired review record is removed",checks);
            model.IdleQueueEnabled=false;
            Assert(model.Resolve(1800000)=="idle" && model.ExpiredSessions==1,"Inactive work display lease expires without a completed event",checks);
            PetModel queue=new PetModel();
            Assert(queue.Resolve(19999)=="idle","Idle queue waits out its delay",checks);
            Assert(queue.Resolve(20000)=="failed","Idle queue starts with the question-mark animation",checks);
            Assert(queue.Resolve(20000+3*1220-1)=="failed","Queued animation still plays in its third loop",checks);
            Assert(queue.Resolve(20000+3*1220)=="idle","Queued animation settles back to idle after three loops",checks);
            Assert(queue.Resolve(20000+3*1220+39999)=="idle","Gap holds between queued animations",checks);
            Assert(queue.Resolve(20000+3*1220+40000)=="review","Idle queue advances to the review animation",checks);
            queue.Session(a,"running",20000+3*1220+40000+3090);
            Assert(queue.Resolve(20000+3*1220+40000+3091)=="running","Active task suspends the idle queue",checks);
            queue.Session(a,"ended",20000+3*1220+40000+3090+5000);
            long resume=20000+3*1220+40000+3090+5000;
            Assert(queue.Resolve(resume+15000)=="idle" && queue.Resolve(resume+15001)=="failed","Queue resumes after activity ends",checks);
            bool rejected=false;try {Settings.Apply(new Settings(),Protocol.Parse("{\"speed\":3}"),false);}catch(ArgumentException) {rejected=true;}Assert(rejected,"Invalid speed rejected",checks);
            rejected=false;try {Settings.Apply(new Settings(),Protocol.Parse("{\"followPointer\":\"false\"}"),false);}catch(ArgumentException) {rejected=true;}Assert(rejected,"String boolean rejected",checks);
            rejected=false;try {Settings.Apply(new Settings(),Protocol.Parse("{\"atlas\":\"other.png\"}"),false);}catch(ArgumentException) {rejected=true;}Assert(rejected,"Configuration cannot change atlas or paths",checks);
            Assert(!new Settings().FollowPointer,"Pointer follow defaults off so task animations are never masked",checks);
            string migrationDir=Path.Combine(output,"settings-migration");Directory.CreateDirectory(migrationDir);
            File.WriteAllText(Path.Combine(migrationDir,"settings.json"),"{\"speed\":1.5,\"scale\":1,\"followPointer\":true,\"reducedMotion\":false}",new UTF8Encoding(false));
            Settings migrated=Settings.Load(migrationDir);
            Assert(!migrated.FollowPointer && migrated.Speed==1.5,"Unversioned file drops the old defaulted followPointer but keeps other fields",checks);
            migrated.FollowPointer=true;migrated.Save(migrationDir);
            Settings kept=Settings.Load(migrationDir);
            Assert(kept.FollowPointer && kept.Speed==1.5,"Versioned file preserves an explicit followPointer choice",checks);
            int count=0;List<object> timings=new List<object>();
            int cellW=(int)Math.Round(AtlasContract.BaseWidth),cellH=(int)Math.Round(AtlasContract.BaseHeight);
            using(Atlas atlas=new Atlas(path)) {
                for(int row=0;row<9;row++) {
                    for(int frame=0;frame<AtlasContract.Durations[row].Length;frame++) {Bitmap image=atlas.Render(row,frame,1);Assert(image.Width==cellW && image.Height==cellH,"Render "+row+"/"+frame,checks);count++;}
                    atlas.Render(row,0,1).Save(Path.Combine(output,AtlasContract.States[row]+".png"),ImageFormat.Png);
                    timings.Add(new Dictionary<string,object>{{"state",AtlasContract.States[row]},{"row",row},{"durationsMs",AtlasContract.Durations[row]},{"cycleMs",AtlasContract.Cycle(row)}});
                }
                using(Bitmap looks=new Bitmap(8*cellW,2*cellH,PixelFormat.Format32bppArgb)) {
                    using(Graphics g=Graphics.FromImage(looks)) {for(int i=0;i<16;i++) {g.DrawImageUnscaled(atlas.Render(9+i/8,i%8,1),(i%8)*cellW,(i/8)*cellH);count++;}}
                    looks.Save(Path.Combine(output,"look-directions.png"),ImageFormat.Png);
                }
                Bitmap reference=atlas.Render(0,0,AtlasContract.Width/AtlasContract.BaseWidth), source=atlas.Render(0,0,1), doubled=atlas.Render(0,0,2);
                Assert(reference.Width==AtlasContract.Width && reference.Height==AtlasContract.Height,"Reference render restores full atlas cell size",checks);
                // With a fractional Codex-size base, scale levels are compared against a full-size reference
                // instead of against each other; each sample must hit the same atlas pixel (nearest neighbour).
                for(int y=0;y<source.Height;y+=13) for(int x=0;x<source.Width;x+=11) {
                    Color s=source.GetPixel(x,y),r=reference.GetPixel(AtlasContract.Width*x/source.Width,AtlasContract.Height*y/source.Height);
                    Assert(s.A==r.A && Math.Abs(s.R-r.R)<=1 && Math.Abs(s.G-r.G)<=1 && Math.Abs(s.B-r.B)<=1,"Nearest sample 1x "+x+","+y,checks);
                }
                for(int y=0;y<doubled.Height;y+=19) for(int x=0;x<doubled.Width;x+=17) {
                    Color d=doubled.GetPixel(x,y),r=reference.GetPixel(AtlasContract.Width*x/doubled.Width,AtlasContract.Height*y/doubled.Height);
                    Assert(d.A==r.A && Math.Abs(d.R-r.R)<=1 && Math.Abs(d.G-r.G)<=1 && Math.Abs(d.B-r.B)<=1,"Nearest sample 2x "+x+","+y,checks);
                }
                doubled.Save(Path.Combine(output,"idle-2x-nearest.png"),ImageFormat.Png);
            }
            Assert(count==73,"All 57 animation frames and sixteen look cells render",checks);
            File.WriteAllText(Path.Combine(output,"self-test.json"),Protocol.Encode(new Dictionary<string,object>{{"ok",true},{"frameCount",count},{"timings",timings},{"checks",checks},{"windowsShown",false},{"runtime",Environment.Version.ToString()}}),new UTF8Encoding(false));
        }
        private static Dictionary<string,object> Client(string pipe,string json) {
            using(NamedPipeClientStream client=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous)) {
                client.Connect(4000);byte[] bytes=Encoding.UTF8.GetBytes(json+"\n");client.Write(bytes,0,bytes.Length);client.Flush();
                byte[] buf=new byte[4096];IAsyncResult result=client.BeginRead(buf,0,buf.Length,null,null);int n;
                using(WaitHandle wait=result.AsyncWaitHandle) {if(!wait.WaitOne(4000)) throw new TimeoutException("Test response timeout");n=client.EndRead(result);}
                return Protocol.Parse(Encoding.UTF8.GetString(buf,0,n).Trim());
            }
        }
        private static void PipeTest(string pipe,string dir,string dataDir) {
            Directory.CreateDirectory(dir);List<string> checks=new List<string>();List<object> responses=new List<object>();
            Dictionary<string,object> status=Client(pipe,"{\"op\":\"status\"}");responses.Add(status);Assert((bool)status["ok"] && !(bool)status["visible"],"Hidden launch status matches actual Form.Visible",checks);
            Assert(!(bool)status["animationTimerRunning"] && Convert.ToInt32(status["animationIntervalMs"])==0,"Hidden launch performs no animation timer wakeups",checks);
            Dictionary<string,object> bad=Client(pipe,"not-json");responses.Add(bad);Assert(!(bool)bad["ok"],"Malformed JSON gets bounded error response",checks);
            bad=Client(pipe,"{\"op\":\"configure\",\"speed\":3}");responses.Add(bad);Assert(!(bool)bad["ok"],"Out of range settings rejected over IPC",checks);
            status=Client(pipe,"{\"op\":\"status\"}");Assert((bool)status["ok"],"Pipe remains usable after malformed requests",checks);
            string a=new String('a',64),b=new String('b',64);
            status=Client(pipe,"{\"op\":\"session\",\"sessionId\":\""+a+"\",\"state\":\"running\"}");Assert((string)status["state"]=="running","Session event enters work animation",checks);
            status=Client(pipe,"{\"op\":\"session\",\"sessionId\":\""+b+"\",\"state\":\"waiting\"}");Assert((string)status["state"]=="waiting","Concurrent waiting session takes priority",checks);
            status=Client(pipe,"{\"op\":\"session\",\"sessionId\":\""+b+"\",\"state\":\"ended\"}");Assert((string)status["state"]=="running" && Convert.ToInt32(status["sessionCount"])==1,"Ended session resumes remaining work",checks);
            status=Client(pipe,"{\"op\":\"configure\",\"speed\":1.5,\"scale\":0.75,\"followPointer\":false,\"reducedMotion\":true}");responses.Add(status);Assert((bool)status["ok"] && Convert.ToDouble(status["speed"])==1.5 && (bool)status["reducedMotion"],"Whitelisted configuration is applied and persisted",checks);
            status=Client(pipe,"{\"op\":\"hide\"}");Assert(!(bool)status["visible"],"Hide status reflects actual visibility",checks);
            using(FileStream lockedPreference=new FileStream(Path.Combine(dataDir,"autostart.json"),FileMode.Open,FileAccess.Read,FileShare.None)) {
                status=Client(pipe,"{\"op\":\"hide\"}");Assert((bool)status["ok"] && !(bool)status["visible"] && status.ContainsKey("warning"),"Hide completes and reports a warning when the preference file is locked",checks);
                status=Client(pipe,"{\"op\":\"quit\"}");Assert((bool)status["ok"] && status.ContainsKey("warning"),"Quit still completes when the preference file is locked",checks);
            }
            File.WriteAllText(Path.Combine(dir,"pipe-self-test.json"),Protocol.Encode(new Dictionary<string,object>{{"ok",true},{"checks",checks},{"responses",responses},{"windowsShown",false},{"showNotExercised",true}}),new UTF8Encoding(false));
        }
    }
}
