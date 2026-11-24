using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Windows.Media.Ocr;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

internal sealed class GameUI : IDisposable
{
    [DllImport("user32")] internal static extern bool SetProcessDPIAware();
    [DllImport("user32")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32")] static extern bool GetClientRect(IntPtr h,out RectangleSize r);
    [DllImport("user32")] static extern bool ClientToScreen(IntPtr h,ref Point p);
    [DllImport("user32")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
    [DllImport("user32")] static extern short GetAsyncKeyState(int key);
    struct RectangleSize {public int L,T,R,B;}
    readonly Process game;
    readonly IntPtr window;
    readonly OcrEngine ru,en;
    public GameUI()
    {
        game=Process.GetProcessesByName("cs2").FirstOrDefault();
        if(game==null)throw new InvalidOperationException("Запустите CS2.");
        window=game.MainWindowHandle;
        ru=OcrEngine.TryCreateFromLanguage(new Language("ru"));
        en=OcrEngine.TryCreateFromLanguage(new Language("en-US")) ?? ru;
        if(ru==null)throw new InvalidOperationException("Для распознавания установите русский OCR Windows.");
        SetForegroundWindow(window);
    }
    public void Dispose(){game.Dispose();}
    public void Check(){
        if((GetAsyncKeyState(0x77)&0x8000)!=0)throw new OperationCanceledException("Пауза F8.");
        if(game.HasExited || window!=GetForegroundWindow())throw new OperationCanceledException("CS2 потеряла фокус.");
    }
    Rectangle Client(){
        Check();
        RectangleSize r;Point p=Point.Empty;
        if(!GetClientRect(window,out r)||!ClientToScreen(window,ref p)||r.R<960||Math.Abs(r.R/(double)r.B-16.0/9)>.025)
            throw new InvalidOperationException("Нужен видимый клиент CS2 в формате 16:9.");
        return new Rectangle(p.X,p.Y,r.R,r.B);
    }
    public Point Position(int x,int y){
        Rectangle r=Client();return new Point(r.X+x*r.Width/1920,r.Y+y*r.Height/1080);
    }
    public void Move(int x,int y){Point p=Position(x,y);SetCursorPos(p.X,p.Y);}
    public void Click(int x,int y){Move(x,y);Check();mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);}
    public string Read(int x,int y,int w,int h,bool latin=false){
        Rectangle client=Client();
        int cw=Math.Max(1,w*client.Width/1920),ch=Math.Max(1,h*client.Height/1080);
        using(var crop=new Bitmap(cw,ch))
        using(var scaled=new Bitmap(w*2,h*2,PixelFormat.Format32bppArgb)){
            using(var g=Graphics.FromImage(crop))g.CopyFromScreen(client.X+x*client.Width/1920,client.Y+y*client.Height/1080,0,0,crop.Size);
            using(var g=Graphics.FromImage(scaled)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(crop,new Rectangle(0,0,scaled.Width,scaled.Height));}
            var bits=scaled.LockBits(new Rectangle(Point.Empty,scaled.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            byte[] bytes=new byte[scaled.Width*scaled.Height*4];
            try{Marshal.Copy(bits.Scan0,bytes,0,bytes.Length);}finally{scaled.UnlockBits(bits);}
            using(var writer=new DataWriter()){
                writer.WriteBytes(bytes);
                using(var frame=SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(),BitmapPixelFormat.Bgra8,scaled.Width,scaled.Height)){
                    var op=(latin?en:ru).RecognizeAsync(frame);
                    try{
                        var timeout=Stopwatch.StartNew();
                        while(op.Status==Windows.Foundation.AsyncStatus.Started){Check();if(timeout.ElapsedMilliseconds>3000){op.Cancel();throw new TimeoutException("Таймаут OCR.");}Thread.Sleep(5);}
                        return String.Join("\n",op.GetResults().Lines.Select(l=>l.Text));
                    }finally{if(op.Status!=Windows.Foundation.AsyncStatus.Started)op.Close();}
                }
            }
        }
    }
    [DllImport("user32")] static extern void keybd_event(byte vk,byte scan,uint flags,UIntPtr extra);
    public void InsertCode(string code){
        Check();
        System.Windows.Forms.Clipboard.SetText(code);
        foreach(byte key in new byte[]{65,86}){
            keybd_event(17,29,0,UIntPtr.Zero);
            try {Thread.Sleep(45);keybd_event(key,0,0,UIntPtr.Zero);Thread.Sleep(45);keybd_event(key,0,2,UIntPtr.Zero);}
            finally{keybd_event(17,29,2,UIntPtr.Zero);}
            Thread.Sleep(60);
        }
    }
    public string Notices(){return Read(1320,0,540,175);}
    public static bool Joined(string s){return Regex.IsMatch(s,@"присоедин|вош[её]л.{0,18}(лобби|групп)|вступил.{0,18}(лобби|групп)",RegexOptions.IgnoreCase);}
    public static bool Limited(string s){return Regex.IsMatch(s,@"слишком много приглашений|достигнут лимит приглашений|превышен лимит приглашений",RegexOptions.IgnoreCase);}
    public static string Key(string s){return Regex.Replace(s.Trim().Normalize().ToLowerInvariant(),@"\s+","");}
    public static bool Sent(string notices,string nick){
        if(String.IsNullOrWhiteSpace(nick))return false;
        foreach(string line in notices.Split('\n')){
            var m=Regex.Match(line,@"приглашает\s+(.+)$",RegexOptions.IgnoreCase);
            if(m.Success && Key(m.Groups[1].Value)==Key(nick))return true;
        }return false;
    }
}

