using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

sealed partial class QueueWindow {
    static readonly Color Ink=Color.FromArgb(27,30,33), Paper=Color.FromArgb(243,241,235), Orange=Color.FromArgb(249,151,32);
    readonly Label counter=new Label();
    readonly Label empty=new Label();
    readonly Button copy=new Button(), confirm=new Button(), retry=new Button();
    static void Style(Button b,string text,Color bg,Color fg,int width) {
        b.AutoSize=false;b.Text=text;b.Size=new Size(width,36);b.Margin=new Padding(0,0,8,0);
        b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;b.BackColor=bg;b.ForeColor=fg;
        b.Font=new Font("Segoe UI",9,FontStyle.Bold);b.Cursor=Cursors.Hand;b.UseVisualStyleBackColor=false;
    }
    sealed class Header:Panel {
        readonly Bitmap art;
        readonly Font brand=new Font("Segoe UI",10,FontStyle.Bold);
        readonly Font title=new Font("Arial",34,FontStyle.Bold);
        internal Header(){
            DoubleBuffered=true;
            using(var stream=typeof(Header).Assembly.GetManifestResourceStream("cover.png"))
            using(var image=Image.FromStream(stream))art=new Bitmap(image);
        }
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.Clear(Ink);g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            int split=(int)(Width*.43);
            var dst=new Rectangle(split-38,0,Width-split+38,Height);
            float cropHeight=(float)art.Width*Height/dst.Width;
            g.DrawImage(art,dst,0,0,art.Width,cropHeight,GraphicsUnit.Pixel);
            var wedge=new[]{new Point(0,0),new Point(split+60,0),new Point(split,Height),new Point(0,Height)};
            using(var brush=new SolidBrush(Ink))g.FillPolygon(brush,wedge);
            using(var brush=new SolidBrush(Orange)){
                g.FillPolygon(brush,new[]{new Point(split+60,0),new Point(split+70,0),new Point(split+10,Height),new Point(split,Height)});
                g.FillRectangle(brush,26,32,22,4);
                g.DrawString("CS2  /  INVITER",brand,brush,55,24);
            }
            g.DrawString("Приглашения",title,Brushes.White,20,51);
        }
        protected override void Dispose(bool disposing){if(disposing){art.Dispose();brand.Dispose();title.Dispose();}base.Dispose(disposing);}
    }
    void BuildInterface(){
        Text="CS2 Inviter";ClientSize=new Size(940,640);MinimumSize=new Size(820,550);
        StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",9);BackColor=Paper;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Margin=Padding.Empty};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(int h in new[]{138,70,40,0,51,50})layout.RowStyles.Add(h==0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,h));
        Controls.Add(layout);
        layout.Controls.Add(new Header{Dock=DockStyle.Fill,Margin=Padding.Empty},0,0);
        var tools=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(24,16,0,0),BackColor=Color.White,WrapContents=false};
        var import=new Button();Style(import,"Загрузить TXT",Ink,Color.White,136);import.Click+=(s,e)=>Import();
        Style(start,"Старт / продолжить",Orange,Ink,172);start.Click+=async(s,e)=>await Run();
        var pause=new Button();Style(pause,"Пауза  ·  F8",Paper,Ink,108);pause.Click+=(s,e)=>{if(run!=null)run.Cancel();};
        var delayLabel=new Label{Text="Задержка, с",AutoSize=true,Margin=new Padding(15,9,8,0)};
        delay.Font=new Font("Segoe UI",11);delay.Margin=new Padding(0,4,0,0);
        tools.Controls.AddRange(new Control[]{import,start,pause,delayLabel,delay});layout.Controls.Add(tools,0,1);
        counter.Dock=DockStyle.Fill;counter.TextAlign=ContentAlignment.MiddleLeft;counter.Font=new Font("Segoe UI",10,FontStyle.Bold);
        counter.Margin=new Padding(24,0,0,0);layout.Controls.Add(counter,0,2);
        var body=new Panel{Dock=DockStyle.Fill,BackColor=Color.White,Margin=new Padding(24,0,24,0)};
        list.BorderStyle=BorderStyle.None;list.HideSelection=false;list.Font=new Font("Segoe UI",10);
        list.HeaderStyle=ColumnHeaderStyle.Nonclickable;list.OwnerDraw=true;
        list.DrawColumnHeader+=(s,e)=>{
            using(var b=new SolidBrush(Ink))e.Graphics.FillRectangle(b,e.Bounds);
            var r=e.Bounds;r.Inflate(-9,0);TextRenderer.DrawText(e.Graphics,e.Header.Text,list.Font,r,Color.White,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
        };
        list.DrawItem+=(s,e)=>{};
        list.DrawSubItem+=(s,e)=>{
            bool selected=e.Item.Selected;
            Color bg=selected?Color.FromArgb(255,226,182):(e.ItemIndex%2==0?Color.White:Paper);
            using(var b=new SolidBrush(bg))e.Graphics.FillRectangle(b,e.Bounds);
            var r=e.Bounds;r.Inflate(-9,0);TextRenderer.DrawText(e.Graphics,e.SubItem.Text,list.Font,r,Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        };
        foreach(string name in new[]{"SteamID64","Код друга","Профиль","Статус"})list.Columns.Add(name);
        body.Resize+=(s,e)=>{int w=Math.Max(100,body.ClientSize.Width-20);int[] percent={26,19,29,26};for(int i=0;i<4;i++)list.Columns[i].Width=w*percent[i]/100;
            empty.SetBounds(0,40,body.Width,Math.Max(1,body.Height-60));};
        body.Controls.Add(list);
        empty.Text="Загрузите список SteamID64 из TXT";empty.ForeColor=Color.DimGray;empty.BackColor=Color.White;
        empty.TextAlign=ContentAlignment.MiddleCenter;body.Controls.Add(empty);empty.BringToFront();layout.Controls.Add(body,0,3);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(24,7,0,0),WrapContents=false};
        Style(copy,"Копировать код",Color.White,Ink,143);copy.Click+=(s,e)=>CopyCode();
        Style(confirm,"Подтвердить отправку",Color.White,Ink,185);confirm.Click+=(s,e)=>Resolve(true);
        Style(retry,"Вернуть в очередь",Color.White,Ink,157);retry.Click+=(s,e)=>Resolve(false);
        actions.Controls.AddRange(new Control[]{copy,confirm,retry});layout.Controls.Add(actions,0,4);
        status.Dock=DockStyle.Fill;status.Margin=Padding.Empty;status.Padding=new Padding(24,0,16,0);
        status.TextAlign=ContentAlignment.MiddleLeft;status.BackColor=Ink;status.ForeColor=Color.WhiteSmoke;
        layout.Controls.Add(status,0,5);list.SelectedIndexChanged+=(s,e)=>UpdateQueueView();
    }
    void UpdateQueueView(){
        counter.Text="Очередь приглашений   /   "+rows.Count;
        empty.Visible=rows.Count==0;var selected=Selected();copy.Enabled=selected!=null;
        confirm.Enabled=retry.Enabled=selected!=null&&selected.State==Unknown&&run==null;
    }
}
