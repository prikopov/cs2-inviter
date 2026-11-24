using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

public static class FriendCode
{
    public const ulong SteamBase = 0x0110000100000000UL;
    const ulong Mask = 0x4353474F00000000UL;
    const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string ConvertId(ulong id)
    {
        if (id < SteamBase || id - SteamBase > uint.MaxValue)
            throw new ArgumentOutOfRangeException("id");
        uint account = (uint)id;
        ulong input = Mask | account;
        byte[] bytes = new byte[8];
        for (int i = 0; i < 8; i++) bytes[i] = (byte)(input >> (8*i));
        byte checksum;
        using (var md5 = MD5.Create()) checksum = md5.ComputeHash(bytes)[0];
        ulong packed = 0;
        for (int i = 0; i < 8; i++) {
            packed = (packed << 5) | ((ulong)(account & 15) << 1) | (uint)((checksum >> i) & 1);
            account >>= 4;
        }
        ulong swapped = 0;
        for (int i = 0; i < 8; i++) { swapped = (swapped << 8) | (byte)packed; packed >>= 8; }
        var chars = new char[13];
        for (int i = 0; i < chars.Length; i++) { chars[i] = Alphabet[(int)(swapped & 31)]; swapped >>= 5; }
        string raw = new string(chars);
        string formatted = raw.Insert(9,"-").Insert(4,"-");
        return formatted.StartsWith("AAAA-",StringComparison.Ordinal) ? formatted.Substring(5) : formatted;
    }
}


public sealed class Entry {
    public ulong Id {get;set;}
    public string State {get;set;}
    public string Nick {get;set;}
}
public sealed class Saved {
    public int Delay {get;set;}
    public List<Entry> Rows {get;set;}
}
internal sealed class CardStability {
    Point? anchor;
    long since;
    public bool Ready(Point? current,long milliseconds){
        if(!current.HasValue || !anchor.HasValue ||
           Math.Abs(current.Value.X-anchor.Value.X)>3 || Math.Abs(current.Value.Y-anchor.Value.Y)>3){
            anchor=current;since=milliseconds;return false;
        }
        return milliseconds-since>=600;
    }
}

sealed partial class QueueWindow : Form
{
    const string Ready="В очереди", Unknown="Проверить отправку", Done="Отправлено";
    readonly string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CS2Inviter","queue.json");
    readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=16000000};
    readonly ListView list=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=false};
    readonly Label status=new Label{Dock=DockStyle.Bottom,Height=55,Padding=new Padding(10)};
    readonly NumericUpDown delay=new NumericUpDown{Minimum=1,Maximum=86400,Value=15,Width=70};
    readonly List<Entry> rows=new List<Entry>();
    readonly Button start=new Button{Text="Старт / продолжить",AutoSize=true};
    CancellationTokenSource run;
    bool closeRequested;
    public QueueWindow(){
        BuildInterface();
        if(File.Exists(path)){
            var saved=json.Deserialize<Saved>(File.ReadAllText(path));
            rows.AddRange(saved.Rows);delay.Value=Math.Max(1,Math.Min(86400,saved.Delay));
        }
        RefreshRows();status.Text="Откройте главное меню CS2. F8 или переключение из игры ставит очередь на паузу.";
        list.DoubleClick+=(s,e)=>CopyCode();delay.ValueChanged+=(s,e)=>Save();
        FormClosing+=(s,e)=>{if(run!=null){e.Cancel=true;closeRequested=true;run.Cancel();}};
    }
    static void Add(FlowLayoutPanel panel,string title,Action action){
        var b=new Button{Text=title,AutoSize=true};b.Click+=(s,e)=>action();panel.Controls.Add(b);
    }
    void RefreshRows(){
        list.BeginUpdate();try{list.Items.Clear();foreach(var r in rows)
            list.Items.Add(new ListViewItem(new[]{r.Id.ToString(),FriendCode.ConvertId(r.Id),r.Nick??"",r.State}){Tag=r});}
        finally{list.EndUpdate(); UpdateQueueView();}
    }
    void Save(){
        Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+".tmp";
        using(var f=new FileStream(tmp,FileMode.Create,FileAccess.Write,FileShare.None))
        using(var w=new StreamWriter(f)){w.Write(json.Serialize(new Saved{Delay=(int)delay.Value,Rows=rows}));w.Flush();f.Flush(true);}
        if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
    }
    void Import(){
        if(run!=null)return;
        using(var dlg=new OpenFileDialog{Filter="Список ID (*.txt)|*.txt|Все файлы|*.*"}){
            if(dlg.ShowDialog(this)!=DialogResult.OK)return;
            var seen=new HashSet<ulong>(rows.Select(r=>r.Id));int added=0,bad=0,dups=0;
            foreach(string token in File.ReadAllText(dlg.FileName).Split(new[]{' ','\r','\n','\t',',',';'},StringSplitOptions.RemoveEmptyEntries)){
                ulong id;if(!ulong.TryParse(token,NumberStyles.None,CultureInfo.InvariantCulture,out id)||id<FriendCode.SteamBase||id-FriendCode.SteamBase>uint.MaxValue){bad++;continue;}
                if(!seen.Add(id)){dups++;continue;}rows.Add(new Entry{Id=id,State=Ready});added++;
            }Save();RefreshRows();status.Text=String.Format("Добавлено {0}, повторов {1}, ошибочных ID {2}.",added,dups,bad);
        }
    }
    Entry Selected(){return list.SelectedItems.Count==1?(Entry)list.SelectedItems[0].Tag:null;}
    void CopyCode(){var r=Selected();if(r!=null)Clipboard.SetText(FriendCode.ConvertId(r.Id));}
    void Resolve(bool sent){
        var r=Selected();if(run!=null||r==null||r.State!=Unknown)return;
        if(MessageBox.Show(this,(sent?"Отметить отправку для ":"Разрешить новую попытку для ")+r.Id+"?","Проверка результата",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        r.State=sent?Done:Ready;Save();RefreshRows();
    }
    static void Guard(GameUI game,CancellationToken token){token.ThrowIfCancellationRequested();game.Check();if(GameUI.Joined(game.Notices()))throw new OperationCanceledException("В лобби вошёл игрок.");}
    static async Task Wait(GameUI g,CancellationToken t,int ms){
        var sw=Stopwatch.StartNew();do{await Task.Delay(Math.Min(300,Math.Max(1,ms-(int)sw.ElapsedMilliseconds)),t);Guard(g,t);}while(sw.ElapsedMilliseconds<ms);
    }
    static async Task<string> Until(GameUI g,CancellationToken t,Func<string> observe,Func<string,bool> accept,int timeout,string failure){
        var sw=Stopwatch.StartNew();do{Guard(g,t);string value=observe();if(accept(value))return value;await Task.Delay(180,t);}while(sw.ElapsedMilliseconds<timeout);
        throw new InvalidOperationException(failure);
    }
    static bool Has(string s,string text){return GameUI.Key(s).Contains(GameUI.Key(text));}
    static async Task CloseSearch(GameUI g,CancellationToken t,Func<string> title){
        var timeout=Stopwatch.StartNew();
        bool dismissed=false;
        int cancelClicks=0,missing=0;
        while(timeout.ElapsedMilliseconds<6500){
            Guard(g,t);
            if(g.CardAction().HasValue){
                missing=0;
                if(!dismissed){
                    // Stay inside the game client, clear of the profile and menu buttons.
                    g.Click(4,4);
                    dismissed=true;
                    await Task.Delay(250,t);
                }
            }else if(Has(title(),"по коду друга")){
                missing=0;
                var matches=g.Words(700,530,605,235).Where(w=>GameUI.Key(w.Text)=="отмена").ToArray();
                if(matches.Length==1 && cancelClicks<2){
                    g.Click(matches[0].Center.X,matches[0].Center.Y);
                    cancelClicks++;await Task.Delay(350,t);
                }
            }else if(Has(g.Read(650,0,630,75),"играть") && ++missing>=2){
                return;
            }
            await Task.Delay(130,t);
        }
        throw new InvalidOperationException("Не удалось подтвердить закрытие поиска. Очередь на паузе.");
    }
    async Task<bool> Invite(GameUI g,Entry entry,CancellationToken t){
        string code=FriendCode.ConvertId(entry.Id);
        string initial=g.Notices();
        Func<string> title=()=>g.Read(680,350,560,145);
        if(!Has(title(),"по коду друга")){
            if(!Has(g.Read(650,0,630,75),"играть"))throw new InvalidOperationException("Откройте главное меню CS2.");
            g.Move(1893,32);await Wait(g,t,550);
            // Tooltip confirms the actual action before opening the dialog.
            await Until(g,t,()=>g.Read(1600,45,320,110),x=>Has(x,"добавить в друзья"),3500,"Не распознана кнопка добавления друга.");
            g.Click(1893,32);
        }
        await Until(g,t,title,x=>Has(x,"по коду друга"),3500,"Поиск по коду не открылся.");
        g.InsertCode(code);
        await Until(g,t,()=>g.Read(730,468,260,39,true),x=>GameUI.Key(x)==GameUI.Key(code),3000,"CS2 не подтвердила введённый код.");
        string nick=await Until(g,t,()=>g.Read(787,522,385,50,true).Trim(),x=>x.Length>0&&!Has(x,"unknown")&&!x.Contains("\n"),6000,"Профиль не загрузился.");
        string alternate=g.Read(787,522,385,50).Trim();
        g.Click(820,542);
        await Wait(g,t,1000);
        string before=g.Notices();
        Func<string,bool> sent=n=>GameUI.Sent(n,nick) || (!alternate.Contains("\n") && GameUI.Sent(n,alternate));
        if(sent(before)||sent(initial))throw new InvalidOperationException("На экране ещё видна предыдущая отправка этому игроку.");
        var deadline=Stopwatch.StartNew();
        var stability=new CardStability();
        bool clicked=false;
        while(deadline.ElapsedMilliseconds<8000){
            Guard(g,t);
            Point? current=g.CardAction();
            if(stability.Ready(current,deadline.ElapsedMilliseconds)){
                entry.Nick=nick;entry.State=Unknown;Save();
                // Persist before sending, then sample once more after the disk write.
                var latest=g.CardAction();
                if(latest.HasValue && stability.Ready(latest,deadline.ElapsedMilliseconds)){
                    t.ThrowIfCancellationRequested();
                    g.Click(latest.Value.X,latest.Value.Y);
                    clicked=true;
                    break;
                }
                entry.State=Ready;Save();
            }
            await Task.Delay(120,t);
        }
        if(!clicked)throw new InvalidOperationException("Положение кнопки приглашения не подтвердилось. Очередь на паузе.");
        RefreshRows();
        await Task.Delay(250,t);
        await CloseSearch(g,t,title);
        var watch=Stopwatch.StartNew();
        while(watch.ElapsedMilliseconds<8000){
            t.ThrowIfCancellationRequested();g.Check();string notice=g.Notices();
            bool joined=GameUI.Joined(notice);
            if(sent(notice)){entry.State=Done;Save();RefreshRows();if(joined)throw new OperationCanceledException("В лобби вошёл игрок.");return true;}
            if(joined)throw new OperationCanceledException("В лобби вошёл игрок.");
            if(GameUI.Limited(notice)&&!GameUI.Limited(before))return false;
            await Task.Delay(160,t);
        }
        throw new InvalidOperationException("Результат неясен. Проверьте чат лобби и выберите действие для этой строки.");
    }
    async Task Run(){
        if(run!=null)return;run=new CancellationTokenSource();var token=run.Token;start.Enabled=false;delay.Enabled=false;
        try{
            if(!rows.Any(r=>r.State!=Done)){status.Text="Нет ожидающих игроков.";return;}
            using(var g=new GameUI()){
                await Task.Delay(400,token);
                int backoff=60;
                foreach(var r in rows.Where(r=>r.State!=Done).ToArray()){
                    if(r.State==Unknown)throw new InvalidOperationException("Сначала проверьте отправку для "+r.Id+".");
                    while(true){
                        status.Text="Поиск и приглашение "+r.Id;
                        if(await Invite(g,r,token))break;
                        r.State=Ready;Save();RefreshRows();
                        int seconds=Math.Max((int)delay.Value,backoff);
                        status.Text="Лимит CS2. Этот ID останется текущим. Пауза "+seconds+" с.";
                        await Wait(g,token,seconds*1000);backoff=Math.Min(1800,backoff*2);
                    }
                    backoff=60;
                    if(rows.Any(other=>other.State!=Done))await Wait(g,token,(int)delay.Value*1000);
                }
            }
            status.Text="Очередь завершена.";
        } catch(Exception ex){status.Text="Пауза. "+ex.Message;}
        finally{run.Dispose();run=null;start.Enabled=true;delay.Enabled=true;UpdateQueueView();if(closeRequested)Close();}
    }
    [STAThread] static void Main(){
        GameUI.SetProcessDPIAware();Application.EnableVisualStyles();
        bool first;using(var mutex=new Mutex(true,"Local\\CS2InviterQueue",out first)){
            if(!first)return;
            try{Application.Run(new QueueWindow());}catch(Exception ex){MessageBox.Show(ex.Message,"CS2Inviter");}
        }
    }
}






