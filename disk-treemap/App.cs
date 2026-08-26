using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiskTreemap
{
    public sealed class MainForm : Form
    {
        private readonly ComboBox driveBox = new ComboBox();
        private readonly Label pathLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly Label summaryLabel = new Label();
        private readonly Button scanButton = new Button();
        private readonly Button folderButton = new Button();
        private readonly Button upButton = new Button();
        private readonly Button explorerButton = new Button();
        private readonly Button stopButton = new Button();
        private readonly TreemapView map = new TreemapView();
        private CancellationTokenSource cts;
        private string currentPath;

        private static readonly Color Bg = Color.FromArgb(10,12,16);
        private static readonly Color Panel = Color.FromArgb(18,21,27);
        private static readonly Color Panel2 = Color.FromArgb(25,29,37);
        private static readonly Color Border = Color.FromArgb(48,55,67);
        private static readonly Color TextMain = Color.FromArgb(239,243,248);
        private static readonly Color TextMuted = Color.FromArgb(146,154,166);
        private static readonly Color Accent = Color.FromArgb(116,232,255);

        public MainForm()
        {
            Text = "磁盘空间地图 · V0.1";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(920,620);
            Size = new Size(1260,820);
            BackColor = Bg;
            ForeColor = TextMain;
            Font = new Font("Segoe UI",9F);
            BuildUi();
            LoadDrives();
            Shown += async (s,e) => { if (!string.IsNullOrEmpty(currentPath)) await ScanAsync(currentPath); };
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(18), BackColor=Bg, ColumnCount=1, RowCount=5 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var header = new TableLayoutPanel { Dock=DockStyle.Top, AutoSize=true, ColumnCount=2, Margin=new Padding(0,0,0,14) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var stack = new FlowLayoutPanel { AutoSize=true, Dock=DockStyle.Fill, FlowDirection=FlowDirection.TopDown, WrapContents=false, Margin=new Padding(0) };
            stack.Controls.Add(new Label { Text="磁盘空间地图", AutoSize=true, ForeColor=TextMain, Font=new Font("Segoe UI",23F,FontStyle.Bold), Margin=new Padding(0) });
            stack.Controls.Add(new Label { Text="越大的矩形，占用越大 · 文件单击直接定位 · 文件夹单击继续钻取", AutoSize=true, ForeColor=TextMuted, Font=new Font("Segoe UI",10F), Margin=new Padding(2,5,0,0) });
            header.Controls.Add(stack,0,0);
            summaryLabel.Text="等待扫描"; summaryLabel.AutoSize=true; summaryLabel.ForeColor=Accent; summaryLabel.Font=new Font("Consolas",10.5F,FontStyle.Bold); summaryLabel.Margin=new Padding(16,10,0,0);
            header.Controls.Add(summaryLabel,1,0);
            root.Controls.Add(header,0,0);

            var bar = new FlowLayoutPanel { Dock=DockStyle.Top, AutoSize=true, WrapContents=true, BackColor=Panel, Padding=new Padding(10), Margin=new Padding(0,0,0,10) };
            driveBox.DropDownStyle=ComboBoxStyle.DropDownList; driveBox.Width=250; driveBox.BackColor=Panel2; driveBox.ForeColor=TextMain; driveBox.FlatStyle=FlatStyle.Flat; driveBox.Margin=new Padding(0,3,8,3);
            driveBox.SelectedIndexChanged += (s,e) => { var d=driveBox.SelectedItem as DriveEntry; if(d!=null) currentPath=d.Root; UpdatePath(); };
            bar.Controls.Add(driveBox);
            SetupButton(scanButton,"扫描当前",Accent,Color.FromArgb(7,24,29));
            SetupButton(folderButton,"选择文件夹",Panel2,TextMain);
            SetupButton(upButton,"上一级",Panel2,TextMain);
            SetupButton(explorerButton,"资源管理器",Panel2,TextMain);
            SetupButton(stopButton,"停止",Color.FromArgb(70,31,35),Color.FromArgb(255,130,140));
            stopButton.Enabled=false;
            scanButton.Click += async (s,e) => { if(!string.IsNullOrEmpty(currentPath)) await ScanAsync(currentPath); };
            folderButton.Click += async (s,e) => { using(var d=new FolderBrowserDialog()){ d.Description="选择要分析的磁盘或文件夹"; d.ShowNewFolderButton=false; if(d.ShowDialog(this)==DialogResult.OK){ currentPath=d.SelectedPath; UpdatePath(); await ScanAsync(currentPath); } } };
            upButton.Click += async (s,e) => { try{ var p=Directory.GetParent((currentPath??"").TrimEnd(Path.DirectorySeparatorChar)); if(p!=null){ currentPath=p.FullName; UpdatePath(); await ScanAsync(currentPath); } }catch{} };
            explorerButton.Click += (s,e) => OpenFolder(currentPath);
            stopButton.Click += (s,e) => { if(cts!=null) cts.Cancel(); };
            bar.Controls.Add(scanButton); bar.Controls.Add(folderButton); bar.Controls.Add(upButton); bar.Controls.Add(explorerButton); bar.Controls.Add(stopButton);
            root.Controls.Add(bar,0,1);

            pathLabel.Dock=DockStyle.Top; pathLabel.AutoSize=true; pathLabel.BackColor=Panel2; pathLabel.ForeColor=TextMuted; pathLabel.Font=new Font("Consolas",9.5F); pathLabel.Padding=new Padding(12,9,12,9); pathLabel.Margin=new Padding(0,0,0,10); pathLabel.AutoEllipsis=true;
            root.Controls.Add(pathLabel,0,2);

            map.Dock=DockStyle.Fill; map.BackColor=Panel; map.Margin=new Padding(0);
            map.Activated += async (s,item) => { if(item==null) return; if(item.IsFolder){ currentPath=item.Path; UpdatePath(); await ScanAsync(currentPath); } else RevealFile(item.Path); };
            map.Hovered += (s,item) => { statusLabel.Text = item==null ? "提示：单击文件会在资源管理器中定位；单击文件夹会进入该文件夹。" : item.Name+"  ·  "+FormatBytes(item.Size)+"  ·  "+item.Path; };
            root.Controls.Add(map,0,3);

            statusLabel.Dock=DockStyle.Top; statusLabel.AutoSize=true; statusLabel.ForeColor=TextMuted; statusLabel.Padding=new Padding(2,10,2,0); statusLabel.Text="准备就绪";
            root.Controls.Add(statusLabel,0,4);
        }

        private void SetupButton(Button b,string text,Color back,Color fore)
        {
            b.Text=text; b.AutoSize=true; b.FlatStyle=FlatStyle.Flat; b.FlatAppearance.BorderColor=Border; b.FlatAppearance.BorderSize=1; b.BackColor=back; b.ForeColor=fore; b.Font=new Font("Segoe UI",9F,FontStyle.Bold); b.Padding=new Padding(10,5,10,5); b.Margin=new Padding(4,0,4,0);
        }

        private void LoadDrives()
        {
            foreach(var d in DriveInfo.GetDrives().Where(x=>x.IsReady))
            {
                try{ driveBox.Items.Add(new DriveEntry{ Root=d.RootDirectory.FullName, Text=string.Format("{0} {1} · 可用 {2}/{3}", d.Name, string.IsNullOrWhiteSpace(d.VolumeLabel)?"本地磁盘":d.VolumeLabel, FormatBytes(d.AvailableFreeSpace), FormatBytes(d.TotalSize)) }); }catch{}
            }
            if(driveBox.Items.Count>0)
            {
                int idx=0;
                for(int i=0;i<driveBox.Items.Count;i++){ var x=driveBox.Items[i] as DriveEntry; if(x!=null && x.Root.StartsWith("C:\\",StringComparison.OrdinalIgnoreCase)){ idx=i; break; } }
                driveBox.SelectedIndex=idx;
            }
        }

        private async Task ScanAsync(string path)
        {
            if(string.IsNullOrEmpty(path)||!Directory.Exists(path)){ MessageBox.Show(this,"这个路径现在不可用。","磁盘空间地图"); return; }
            if(cts!=null){ cts.Cancel(); cts.Dispose(); }
            cts=new CancellationTokenSource(); var token=cts.Token;
            SetBusy(true); map.SetItems(new List<DiskItem>()); summaryLabel.Text="扫描中…"; statusLabel.Text="正在读取目录结构…";
            var progress=new Progress<ScanProgress>(p=> statusLabel.Text=string.Format("正在扫描：{0} · 已检查 {1:N0} 项 · 跳过 {2:N0} 项",p.Name,p.Visited,p.Skipped));
            try
            {
                var r=await Task.Run(()=>Scanner.Scan(path,token,progress),token);
                if(token.IsCancellationRequested) return;
                currentPath=path; UpdatePath(); map.SetItems(r.Items); summaryLabel.Text=string.Format("可视化 {0} · {1:N0} 项",FormatBytes(r.Total),r.Items.Count); statusLabel.Text=string.Format("完成 · 跳过 {0:N0} 个无权限/已消失项目。",r.Skipped);
            }
            catch(OperationCanceledException){ statusLabel.Text="扫描已停止。"; summaryLabel.Text="已停止"; }
            catch(Exception ex){ statusLabel.Text="扫描失败："+ex.Message; MessageBox.Show(this,ex.Message,"扫描失败"); }
            finally{ SetBusy(false); }
        }

        private void SetBusy(bool busy){ scanButton.Enabled=!busy; folderButton.Enabled=!busy; upButton.Enabled=!busy; driveBox.Enabled=!busy; stopButton.Enabled=busy; UseWaitCursor=busy; }
        private void UpdatePath(){ pathLabel.Text="当前位置  ›  "+(currentPath??"未选择"); }
        private static void RevealFile(string p){ try{ Process.Start("explorer.exe","/select,\""+p+"\""); }catch{} }
        private static void OpenFolder(string p){ if(string.IsNullOrEmpty(p))return; try{ Process.Start("explorer.exe","\""+p+"\""); }catch{} }

        public static string FormatBytes(long bytes)
        {
            string[] u={"B","KB","MB","GB","TB","PB"}; double v=bytes; int i=0; while(v>=1024&&i<u.Length-1){v/=1024;i++;} return v.ToString(v>=100?"0":v>=10?"0.0":"0.00")+" "+u[i];
        }

        private sealed class DriveEntry{ public string Root; public string Text; public override string ToString(){return Text;} }
    }

    internal static class Scanner
    {
        public static ScanResult Scan(string path,CancellationToken token,IProgress<ScanProgress> progress)
        {
            var r=new ScanResult(); FileSystemInfo[] entries;
            try{ entries=new DirectoryInfo(path).GetFileSystemInfos(); }catch{ throw new IOException("无法读取该目录。请尝试选择有权限访问的文件夹。"); }
            long visited=0;
            foreach(var e in entries)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if((e.Attributes&FileAttributes.ReparsePoint)!=0){r.Skipped++;continue;}
                    bool folder=(e.Attributes&FileAttributes.Directory)!=0;
                    long size=folder?DirSize(e.FullName,token,r,ref visited,progress,e.Name):((FileInfo)e).Length;
                    if(!folder) visited++;
                    if(size>0){ r.Items.Add(new DiskItem{ Name=e.Name,Path=e.FullName,Size=size,IsFolder=folder,Fill=ColorFor(e.FullName,folder)}); r.Total+=size; }
                }
                catch(OperationCanceledException){throw;} catch{r.Skipped++;}
                progress.Report(new ScanProgress{Name=e.Name,Visited=visited,Skipped=r.Skipped});
            }
            r.Items=r.Items.OrderByDescending(x=>x.Size).ToList(); return r;
        }

        private static long DirSize(string root,CancellationToken token,ScanResult r,ref long visited,IProgress<ScanProgress> progress,string top)
        {
            long total=0; var stack=new Stack<string>(); stack.Push(root);
            while(stack.Count>0)
            {
                token.ThrowIfCancellationRequested(); string dir=stack.Pop();
                string[] files; try{files=Directory.GetFiles(dir);}catch{r.Skipped++;files=new string[0];}
                foreach(var f in files){ token.ThrowIfCancellationRequested(); try{total+=new FileInfo(f).Length;visited++;}catch{r.Skipped++;} if((visited&1023)==0) progress.Report(new ScanProgress{Name=top,Visited=visited,Skipped=r.Skipped}); }
                string[] dirs; try{dirs=Directory.GetDirectories(dir);}catch{r.Skipped++;dirs=new string[0];}
                foreach(var d in dirs){ token.ThrowIfCancellationRequested(); try{var di=new DirectoryInfo(d); if((di.Attributes&FileAttributes.ReparsePoint)!=0){r.Skipped++;continue;} stack.Push(d);visited++;}catch{r.Skipped++;} }
            }
            return total;
        }

        private static Color ColorFor(string p,bool folder)
        {
            if(folder)return Color.FromArgb(70,135,188); string e=Path.GetExtension(p).ToLowerInvariant();
            if(new[]{".mp4",".mkv",".mov",".avi",".webm"}.Contains(e))return Color.FromArgb(151,103,210);
            if(new[]{".jpg",".jpeg",".png",".gif",".webp",".bmp",".psd"}.Contains(e))return Color.FromArgb(210,103,151);
            if(new[]{".zip",".7z",".rar",".iso",".tar",".gz"}.Contains(e))return Color.FromArgb(205,146,69);
            if(new[]{".exe",".msi",".dll",".sys"}.Contains(e))return Color.FromArgb(89,163,117);
            if(new[]{".mp3",".wav",".flac",".aac",".ogg"}.Contains(e))return Color.FromArgb(80,171,159);
            return Color.FromArgb(91,119,150);
        }
    }

    internal sealed class TreemapView : Control
    {
        private readonly List<DiskItem> items=new List<DiskItem>();
        private readonly List<Cell> cells=new List<Cell>();
        private readonly ToolTip tip=new ToolTip();
        private DiskItem hover;
        public event EventHandler<DiskItem> Activated;
        public event EventHandler<DiskItem> Hovered;
        public TreemapView(){DoubleBuffered=true;ResizeRedraw=true;Cursor=Cursors.Hand;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);}
        public void SetItems(List<DiskItem> x){items.Clear();if(x!=null)items.AddRange(x.Where(i=>i.Size>0));Build();Invalidate();}
        protected override void OnResize(EventArgs e){base.OnResize(e);Build();}
        private void Build(){cells.Clear();if(items.Count==0||Width<10||Height<10)return;Layout(items,0,items.Count,new RectangleF(3,3,Width-6,Height-6),cells);}
        private static void Layout(List<DiskItem> src,int start,int count,RectangleF r,List<Cell> outp)
        {
            if(count<=0||r.Width<1||r.Height<1)return; if(count==1){outp.Add(new Cell{Item=src[start],Rect=Inset(r,1.2f)});return;}
            long total=0;for(int i=start;i<start+count;i++)total+=src[i].Size; if(total<=0)return;
            long acc=0,best=long.MaxValue;int split=1;for(int i=1;i<count;i++){acc+=src[start+i-1].Size;long d=Math.Abs(total-2*acc);if(d<best){best=d;split=i;}else break;}
            long first=0;for(int i=start;i<start+split;i++)first+=src[i].Size;float q=Math.Max(.02f,Math.Min(.98f,(float)first/total)); RectangleF a,b;
            if(r.Width>=r.Height){float w=r.Width*q;a=new RectangleF(r.X,r.Y,w,r.Height);b=new RectangleF(r.X+w,r.Y,r.Width-w,r.Height);}else{float h=r.Height*q;a=new RectangleF(r.X,r.Y,r.Width,h);b=new RectangleF(r.X,r.Y+h,r.Width,r.Height-h);} Layout(src,start,split,a,outp);Layout(src,start+split,count-split,b,outp);
        }
        private static RectangleF Inset(RectangleF r,float p){return r.Width>p*2&&r.Height>p*2?new RectangleF(r.X+p,r.Y+p,r.Width-p*2,r.Height-p*2):r;}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Color.FromArgb(16,19,24));
            if(cells.Count==0){using(var f=new Font("Segoe UI",16F,FontStyle.Bold))using(var b=new SolidBrush(Color.FromArgb(120,130,145))){var t="选择磁盘或文件夹，然后开始扫描";var s=e.Graphics.MeasureString(t,f);e.Graphics.DrawString(t,f,b,(Width-s.Width)/2,(Height-s.Height)/2);}return;}
            foreach(var c in cells){var r=c.Rect;if(r.Width<.6f||r.Height<.6f)continue;var col=c.Item==hover?Light(c.Item.Fill):c.Item.Fill;using(var b=new SolidBrush(col))using(var p=new Pen(Color.FromArgb(65,7,10,14),1)){e.Graphics.FillRectangle(b,r);e.Graphics.DrawRectangle(p,r.X,r.Y,Math.Max(0,r.Width-1),Math.Max(0,r.Height-1));} if(r.Width>=66&&r.Height>=36){using(var f=new Font("Segoe UI",r.Width>180&&r.Height>90?10F:8.5F,FontStyle.Bold)){var rr=Rectangle.Round(new RectangleF(r.X+6,r.Y+5,r.Width-12,r.Height-10));TextRenderer.DrawText(e.Graphics,c.Item.Name+Environment.NewLine+MainForm.FormatBytes(c.Item.Size),f,rr,Color.White,TextFormatFlags.EndEllipsis|TextFormatFlags.WordBreak|TextFormatFlags.NoPadding);}} }
        }
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);var x=Hit(e.Location);if(!ReferenceEquals(x,hover)){hover=x;Invalidate();tip.SetToolTip(this,x==null?null:x.Name+"\n"+MainForm.FormatBytes(x.Size)+"\n"+x.Path);if(Hovered!=null)Hovered(this,x);}}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=null;tip.SetToolTip(this,null);Invalidate();if(Hovered!=null)Hovered(this,null);}
        protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(e.Button!=MouseButtons.Left)return;var x=Hit(e.Location);if(x!=null&&Activated!=null)Activated(this,x);}
        private DiskItem Hit(Point p){for(int i=0;i<cells.Count;i++)if(cells[i].Rect.Contains(p))return cells[i].Item;return null;}
        private static Color Light(Color c){return Color.FromArgb(c.A,Math.Min(255,c.R+24),Math.Min(255,c.G+24),Math.Min(255,c.B+24));}
        private sealed class Cell{public DiskItem Item;public RectangleF Rect;}
    }

    internal sealed class DiskItem{public string Name;public string Path;public long Size;public bool IsFolder;public Color Fill;}
    internal sealed class ScanResult{public List<DiskItem> Items=new List<DiskItem>();public long Total;public long Skipped;}
    internal sealed class ScanProgress{public string Name;public long Visited;public long Skipped;}
}
