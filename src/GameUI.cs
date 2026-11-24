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
    public sealed class Word {
        public string Text;
        public Point Center;
    }
    public string Read(int x,int y,int w,int h,bool latin=false){
        return Scan(x,y,w,h,latin,null);
    }
    public List<Word> Words(int x,int y,int w,int h){
        var words=new List<Word>();
        Scan(x,y,w,h,false,words);
        return words;
    }
    public string ReadCode(string expected){
        string last="";
        foreach(int scale in new[]{2,3,4}){
            last=Scan(730,468,260,39,true,null,scale);
            if(Key(last)==Key(expected))break;
        }
        return last;
    }
    public string ReadNick(int scale,bool latin=false){return Scan(787,522,385,50,latin,null,scale).Trim();}
    string Scan(int x,int y,int w,int h,bool latin,List<Word> words,int scale=2){
        Rectangle client=Client();
        int cw=Math.Max(1,w*client.Width/1920),ch=Math.Max(1,h*client.Height/1080);
        using(var crop=new Bitmap(cw,ch))
        using(var scaled=new Bitmap(w*scale,h*scale,PixelFormat.Format32bppArgb)){
            using(var g=Graphics.FromImage(crop))g.CopyFromScreen(client.X+x*client.Width/1920,client.Y+y*client.Height/1080,0,0,crop.Size);
            using(var g=Graphics.FromImage(scaled)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(crop,new Rectangle(0,0,scaled.Width,scaled.Height));}
            return Recognize(scaled,latin?en:ru,x,y,words,Check);
        }
    }
    internal static string Recognize(Bitmap scaled,OcrEngine engine,int x,int y,List<Word> words,Action check){
            var bits=scaled.LockBits(new Rectangle(Point.Empty,scaled.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            byte[] bytes=new byte[scaled.Width*scaled.Height*4];
            try{Marshal.Copy(bits.Scan0,bytes,0,bytes.Length);}finally{scaled.UnlockBits(bits);}
            using(var writer=new DataWriter()){
                writer.WriteBytes(bytes);
                using(var frame=SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(),BitmapPixelFormat.Bgra8,scaled.Width,scaled.Height)){
                    var op=engine.RecognizeAsync(frame);
                    try{
                        var timeout=Stopwatch.StartNew();
                        while(op.Status==Windows.Foundation.AsyncStatus.Started){if(check!=null)check();if(timeout.ElapsedMilliseconds>3000){op.Cancel();throw new TimeoutException("Таймаут OCR.");}Thread.Sleep(5);}
                        var lines=op.GetResults().Lines;
                        if(words!=null) foreach(var line in lines) foreach(var word in line.Words){
                            var r=word.BoundingRect;
                            words.Add(new Word{Text=word.Text,Center=new Point(x+(int)((r.X+r.Width/2)/2),y+(int)((r.Y+r.Height/2)/2))});
                        }
                        return String.Join("\n",lines.Select(l=>l.Text));
                    }finally{if(op.Status!=Windows.Foundation.AsyncStatus.Started)op.Close();}
                }
            }}
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
    internal static Point? InviteTarget(List<Word> words){
        var hits=words.Where(w=>Regex.IsMatch(Key(w.Text),@"^пригла[а-я]{1,4}ть$") &&
            words.Any(other=>Key(other.Text)=="профиль" && Math.Abs(other.Center.Y-w.Center.Y)<=8 &&
                other.Center.X-w.Center.X>=40 && other.Center.X-w.Center.X<=125)).ToArray();
        return hits.Length==1 ? (Point?)hits[0].Center : null;
    }
    public Point? CardAction(){return InviteTarget(Words(660,160,860,720));}
    public string Notices(){return Read(1320,0,540,175);}
    public bool HasReceipt(string text,string nick,string alternate,string latinConsensus){
        if(Sent(text,nick) || Sent(text,alternate))return true;
        const int x=1320,y=0,width=540,height=175;
        if(text.IndexOf("приглашает",StringComparison.OrdinalIgnoreCase)<0)return false;
        Rectangle client=Client();
        using(var crop=new Bitmap(Math.Max(1,width*client.Width/1920),Math.Max(1,height*client.Height/1080)))
        using(var frame=new Bitmap(width*2,height*2,PixelFormat.Format32bppArgb)){
            using(var g=Graphics.FromImage(crop))g.CopyFromScreen(client.X+x*client.Width/1920,client.Y+y,0,0,crop.Size);
            using(var g=Graphics.FromImage(frame))g.DrawImage(crop,new Rectangle(Point.Empty,frame.Size));
            var words=new List<Word>();
            string russian=Recognize(frame,ru,0,0,words,Check);
            if(Sent(russian,nick) || Sent(russian,alternate))return true;
            var english=new List<Word>();
            Recognize(frame,en,0,0,english,Check);
            foreach(var verb in words.Where(w=>Key(w.Text)=="приглашает")){
                string name=String.Join(" ",english.Where(w=>Math.Abs(w.Center.Y-verb.Center.Y)<=5 && w.Center.X>verb.Center.X+20)
                    .OrderBy(w=>w.Center.X).Select(w=>w.Text));
                if(name.Length>0 && (NickMatches(name,nick) || NickMatches(name,alternate)))return true;
                var tagged=Regex.Match(name,@"^[\[(][^\[\]\s]{1,12}\]\s+(.+)$");
                if(tagged.Success && latinConsensus.Length>=3 && Key(tagged.Groups[1].Value)==Key(latinConsensus))return true;
            }
        }
        return false;
    }
    public static bool Joined(string s){return Regex.IsMatch(s,@"присоедин|вош[её]л.{0,18}(лобби|групп)|вступил.{0,18}(лобби|групп)",RegexOptions.IgnoreCase);}
    public static bool Limited(string s){return Regex.IsMatch(s,@"слишком много приглашений|достигнут лимит приглашений|превышен лимит приглашений",RegexOptions.IgnoreCase);}
    public static string Key(string s){return Regex.Replace(s.Trim().Normalize().ToLowerInvariant(),@"\s+","");}
    static string NicknameKey(string nick){
        // OCR can render the closing bracket of a leading clan tag as I or l.
        return Key(Regex.Replace(nick.Trim(),@"^(\[[^\[\]\s]{1,12})[Il](?=\s)","$1]"));
    }
    internal static bool NickMatches(string observed,string expected){
        string a=NicknameKey(observed),b=NicknameKey(expected);
        if(a==b)return true;
        if(a.Length!=b.Length)return false;
        int substitutions=0;
        for(int i=0;i<a.Length;i++){
            if(a[i]==b[i])continue;
            if(++substitutions>1)return false;
            if((a[i]=='.' && b[i]=='-') || (a[i]=='-' && b[i]=='.')){
                if(i==0 || i+1==a.Length || !Char.IsLetterOrDigit(b[i-1]) || !Char.IsLetterOrDigit(b[i+1]))return false;
                continue;
            }
            if(a.Length<6 || !SimilarGlyph(a[i],b[i]))return false;
        }
        return true;
    }
    static bool SimilarGlyph(char a,char b){
        foreach(string group in new[]{"il1","oо0","aа","cс","eе","pр","xх"}){
            if(group.IndexOf(a)>=0 && group.IndexOf(b)>=0)return true;
        }
        return false;
    }
    public static bool Sent(string notices,string nick){
        if(String.IsNullOrWhiteSpace(nick))return false;
        foreach(string line in notices.Split('\n')){
            var m=Regex.Match(line,@"приглашает\s+(.+)$",RegexOptions.IgnoreCase);
            if(m.Success && NickMatches(m.Groups[1].Value,nick))return true;
        }return false;
    }
}





