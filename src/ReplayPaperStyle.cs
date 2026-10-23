using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

public static class ReplayPaper
{
    public static readonly Color Background=Color.FromArgb(244,246,249),Surface=Color.White,Ink=Color.FromArgb(31,48,69),Muted=Color.FromArgb(99,117,139),Border=Color.FromArgb(220,227,236),Accent=Color.FromArgb(54,95,137),Soft=Color.FromArgb(234,240,247);
    public static GraphicsPath Round(RectangleF r,float radius)
    {var p=new GraphicsPath();float d=Math.Max(1,Math.Min(radius*2,Math.Min(r.Width,r.Height)));p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    public static void PaintButton(Button button,Graphics g,bool primary,bool quiet,bool selected,bool choice,bool multi,bool focus)
    {
        g.Clear(button.Parent==null?Background:button.Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        float dpi=ReplayWindowLayout.DpiScale;var r=new RectangleF(.7F,.7F,Math.Max(1,button.Width-1.4F),Math.Max(1,button.Height-1.4F));
        bool hover=button.Enabled && button.ClientRectangle.Contains(button.PointToClient(Control.MousePosition));
        Color fill=primary?Accent:selected?Soft:quiet?Background:Surface;
        if(hover && !primary)fill=Soft;
        using(var shape=Round(r,7*dpi))
        {using(var brush=new SolidBrush(fill))g.FillPath(brush,shape);if(!quiet)using(var pen=new Pen(primary||selected?Accent:Border,selected?1.5F*dpi:1))g.DrawPath(pen,shape);if(focus)using(var pen=new Pen(Accent,1*dpi)){pen.DashStyle=DashStyle.Dot;g.DrawPath(pen,shape);}}
        var text=button.ClientRectangle;text.Inflate(-(int)(14*dpi),0);
        if(choice)
        {
            float size=16*dpi,x=16*dpi,y=(button.Height-size)/2;
            using(var pen=new Pen(selected?Accent:Color.FromArgb(184,199,215),1.2F*dpi))
            {if(multi){using(var box=Round(new RectangleF(x,y,size,size),3*dpi)){using(var b=new SolidBrush(selected?Accent:Surface))g.FillPath(b,box);g.DrawPath(pen,box);}}else {using(var b=new SolidBrush(selected?Accent:Surface))g.FillEllipse(b,x,y,size,size);g.DrawEllipse(pen,x,y,size,size);}}
            if(selected){using(var pen=new Pen(Color.White,1.5F*dpi))g.DrawLines(pen,new[]{new PointF(x+4*dpi,y+8*dpi),new PointF(x+7*dpi,y+11*dpi),new PointF(x+12*dpi,y+5*dpi)});}
            text.X+=(int)(30*dpi);text.Width-=(int)(30*dpi);
        }
        Color ink=!button.Enabled?Muted:primary?Color.White:selected?Accent:quiet?Muted:Ink;
        TextRenderer.DrawText(g,button.Text,button.Font,text,ink,(choice?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter)|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
    }
    public static Label Label(string text,float size=10,Color? ink=null)
    {return new ReplayStableLabel{Text=text,Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei UI",size,FontStyle.Regular),ForeColor=ink??Muted,BackColor=Color.Transparent,Margin=Padding.Empty,TextAlign=ContentAlignment.MiddleLeft};}
    public static void ApplyInputs(Control parent)
    {
        if(!(parent is Label))parent.ForeColor=Ink;
        var form=parent as Form;if(form!=null)form.BackColor=Background;
        var page=parent as TabPage;if(page!=null)page.BackColor=Background;
        var label=parent as Label;if(label!=null){label.BackColor=Color.Transparent;if(label.ForeColor.ToArgb()!=Ink.ToArgb())label.ForeColor=Muted;}
        var edit=parent as TextBox;if(edit!=null){edit.BackColor=Surface;edit.ForeColor=Ink;edit.BorderStyle=edit.Parent is PaperInputFrame?BorderStyle.None:BorderStyle.FixedSingle;}
        var number=parent as NumericUpDown;if(number!=null){number.BackColor=Surface;number.ForeColor=Ink;number.BorderStyle=number.Parent is PaperInputFrame?BorderStyle.None:BorderStyle.FixedSingle;}
        var combo=parent as ComboBox;if(combo!=null){combo.BackColor=Surface;combo.ForeColor=Ink;combo.FlatStyle=FlatStyle.Flat;}
        if(!(parent is NumericUpDown)&&!(parent is ComboBox))foreach(Control child in parent.Controls)ApplyInputs(child);
    }
}

public sealed class PaperActionButton:Button
{
    public bool Primary {get;set;}public bool Quiet {get;set;}
    public PaperActionButton(){FlatStyle=FlatStyle.Flat;UseVisualStyleBackColor=false;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);Invalidate();}protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){ReplayPaper.PaintButton(this,e.Graphics,Primary,Quiet,false,false,false,Focused&&ShowFocusCues);}
}
public sealed class PaperInputFrame:Panel
{
    readonly Control input;
    public PaperInputFrame(Control editor)
    {input=editor;DoubleBuffered=true;BackColor=ReplayPaper.Surface;MinimumSize=new Size(0,40);Margin=Padding.Empty;input.Parent=null;input.Dock=DockStyle.None;input.Margin=Padding.Empty;var text=input as TextBox;if(text!=null)text.BorderStyle=BorderStyle.None;var number=input as NumericUpDown;if(number!=null)number.BorderStyle=BorderStyle.None;Controls.Add(input);Enter+=(s,e)=>input.Focus();}
    protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);if(input==null)return;int pad=(int)(12*ReplayWindowLayout.DpiScale);int height=input.PreferredSize.Height;input.SetBounds(pad,Math.Max(2,(Height-height)/2),Math.Max(1,Width-2*pad),height);}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Parent==null?ReplayPaper.Background:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=ReplayPaper.Round(new RectangleF(.5F,.5F,Math.Max(1,Width-1),Math.Max(1,Height-1)),7*ReplayWindowLayout.DpiScale))using(var brush=new SolidBrush(ReplayPaper.Surface))e.Graphics.FillPath(brush,path);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=ReplayPaper.Round(new RectangleF(.5F,.5F,Math.Max(1,Width-1),Math.Max(1,Height-1)),7*ReplayWindowLayout.DpiScale))using(var pen=new Pen(ReplayPaper.Border))e.Graphics.DrawPath(pen,path);}
}
public sealed class PaperCard:Panel
{
    public PaperCard(){DoubleBuffered=true;BackColor=ReplayPaper.Surface;Padding=new Padding(18);Margin=new Padding(0,0,0,16);}
    protected override void OnPaintBackground(PaintEventArgs e)
    {e.Graphics.Clear(Parent==null?ReplayPaper.Background:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=ReplayPaper.Round(new RectangleF(.5F,.5F,Math.Max(1,Width-1),Math.Max(1,Height-1)),9*ReplayWindowLayout.DpiScale)){using(var b=new SolidBrush(ReplayPaper.Surface))e.Graphics.FillPath(b,path);using(var p=new Pen(ReplayPaper.Border))e.Graphics.DrawPath(p,path);}}
}
public sealed class PaperTabControl:TabControl
{
    public PaperTabControl(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);ItemSize=new Size(95,38);Padding=new Point(14,8);}
    protected override void OnSelectedIndexChanged(EventArgs e){base.OnSelectedIndexChanged(e);Invalidate();}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(ReplayPaper.Background);}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(ReplayPaper.Background);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        for(int i=0;i<TabPages.Count;i++){var r=GetTabRect(i);r.Inflate(-3,-3);bool selected=i==SelectedIndex;if(selected)using(var p=ReplayPaper.Round(r,6*ReplayWindowLayout.DpiScale))using(var b=new SolidBrush(ReplayPaper.Soft))e.Graphics.FillPath(b,p);TextRenderer.DrawText(e.Graphics,TabPages[i].Text,Font,r,selected?ReplayPaper.Accent:ReplayPaper.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
    }
}

public sealed class PaperPriceInput:UserControl
{
    readonly TextBox editor=new TextBox();readonly Label quote=new ReplayStableLabel();
    public event EventHandler InputChanged;
    public TextBox Editor {get{return editor;}}
    public string InputText {get{return editor.Text;}set{editor.Text=value??"";}}
    public bool IsValid {get{decimal value;return TryValue(out value);}}
    public static string Display(decimal value){return Decimal.Round(value,2,MidpointRounding.AwayFromZero).ToString("0.##",CultureInfo.InvariantCulture);}
    bool TryValue(out decimal value)
    {string text=editor.Text.Trim();if(text==""){value=0;return true;}if(!System.Text.RegularExpressions.Regex.IsMatch(text,@"^(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d+)?$")){value=0;return false;}if(!Decimal.TryParse(text,NumberStyles.AllowDecimalPoint|NumberStyles.AllowThousands,CultureInfo.InvariantCulture,out value)||value<0||value>100000000)return false;value=Decimal.Round(value,2,MidpointRounding.AwayFromZero);return true;}
    public decimal Value {get{decimal value;if(!TryValue(out value))throw new ArgumentException("请输入有效价格，使用数字和小数点。");return value;}set{if(value<0||value>100000000)throw new ArgumentOutOfRangeException("value");editor.Text=Display(value);}}
    public PaperPriceInput()
    {
        AutoScaleMode=AutoScaleMode.None;Height=130;MinimumSize=new Size(0,130);BackColor=ReplayPaper.Background;
        var table=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));table.RowStyles.Add(new RowStyle(SizeType.Absolute,25));table.RowStyles.Add(new RowStyle(SizeType.Absolute,46));table.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(table);
        table.Controls.Add(ReplayPaper.Label("价格 · 最多两位小数",9),0,0);editor.Font=new Font("Segoe UI",16);editor.AccessibleName="价格输入，可直接输入或粘贴";var frame=new PaperInputFrame(editor){Dock=DockStyle.Fill,Margin=new Padding(0,2,0,2)};table.Controls.Add(frame,0,1);
        quote.Dock=DockStyle.Fill;quote.ForeColor=ReplayPaper.Muted;quote.Font=new Font("Microsoft YaHei UI",9);quote.Margin=new Padding(0,7,0,0);table.Controls.Add(quote,0,2);
        editor.Leave+=(s,e)=>Normalize();editor.TextChanged+=(s,e)=>{if(InputChanged!=null)InputChanged(this,EventArgs.Empty);};Enter+=(s,e)=>editor.Focus();
    }
    public void Normalize(){decimal value;if(TryValue(out value)&&editor.Text.Trim()!="")editor.Text=Display(value);}
    public void Reference(DesktopSnapshot state)
    {quote.Text=state!=null&&state.Number("bid")>0&&state.Number("ask")>=state.Number("bid")?"暂停时参考报价\r\nBid "+Display(state.Number("bid"))+"   /   Ask "+Display(state.Number("ask")):"参考报价暂不可用，请按图表填写。";}
}
