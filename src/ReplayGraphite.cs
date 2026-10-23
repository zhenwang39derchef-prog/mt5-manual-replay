using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

public static class GraphiteTheme
{
    public static readonly Color Background=Color.FromArgb(244,246,249),Surface=Color.White,Soft=Color.FromArgb(231,237,244);
    public static readonly Color Text=Color.FromArgb(30,47,65),Muted=Color.FromArgb(91,111,134),Border=Color.FromArgb(211,221,232),Accent=Color.FromArgb(53,96,139);
    public static readonly Color BuySurface=Color.FromArgb(228,243,236),BuyText=Color.FromArgb(12,112,95),SellSurface=Color.FromArgb(251,235,238),SellText=Color.FromArgb(192,46,65);

    public static void Apply(Control control)
    {
        if(control is Form || control is Panel || control is TableLayoutPanel || control is TabPage){control.BackColor=Background;control.ForeColor=Text;}
        var label=control as Label;
        if(label!=null)
        {
            bool main=label.ForeColor.ToArgb()==Text.ToArgb();
            bool accent=label.ForeColor.ToArgb()==Accent.ToArgb();
            label.BackColor=Color.Transparent;label.ForeColor=accent?Accent:main?Text:Muted;
        }
        var number=control as NumericUpDown;
        if(number!=null){number.BackColor=Surface;number.ForeColor=Text;number.BorderStyle=BorderStyle.FixedSingle;number.Font=new Font("Segoe UI",10F);}
        var button=control as DesktopActionButton;
        if(button!=null)
        {
            string role=button.AccessibleName??"";
            button.FlatStyle=FlatStyle.Flat;button.UseVisualStyleBackColor=false;button.FlatAppearance.BorderColor=Border;
            button.BackColor=Soft;button.ForeColor=Text;
            if(role=="buy"){button.BackColor=BuySurface;button.ForeColor=BuyText;}
            else if(role=="sell"){button.BackColor=SellSurface;button.ForeColor=SellText;}
            else if(role=="close_all"){button.BackColor=Background;button.ForeColor=SellText;}
            else if(button.Quiet || button.Text.StartsWith("记录") || button.Text.StartsWith("上一") || button.Text.StartsWith("下一") || button.Text=="初始")
            {button.Quiet=true;button.BackColor=Background;button.ForeColor=Muted;}
        }
        var checkbox=control as CheckBox;
        if(checkbox!=null){checkbox.ForeColor=Muted;checkbox.BackColor=Background;}
        var track=control as TrackBar;
        if(track!=null){track.BackColor=Background;track.ForeColor=Accent;}
        var grid=control as DataGridView;
        if(grid!=null)
        {
            grid.BackgroundColor=Background;grid.GridColor=Border;grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;
            grid.DefaultCellStyle.BackColor=Background;grid.DefaultCellStyle.ForeColor=Text;grid.DefaultCellStyle.SelectionBackColor=Soft;grid.DefaultCellStyle.SelectionForeColor=Text;
            grid.ColumnHeadersDefaultCellStyle.BackColor=Surface;grid.ColumnHeadersDefaultCellStyle.ForeColor=Muted;
        }
        var tabs=control as TabControl;
        if(tabs!=null)
        {
            tabs.DrawMode=TabDrawMode.OwnerDrawFixed;
            tabs.DrawItem+=(sender,e)=>
            {
                bool selected=(e.State&DrawItemState.Selected)!=0;
                using(var brush=new SolidBrush(Background))e.Graphics.FillRectangle(brush,e.Bounds);
                TextRenderer.DrawText(e.Graphics,tabs.TabPages[e.Index].Text,e.Font,e.Bounds,selected?Text:Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                if(selected)using(var pen=new Pen(Accent,2))e.Graphics.DrawLine(pen,e.Bounds.Left+6,e.Bounds.Bottom-2,e.Bounds.Right-6,e.Bounds.Bottom-2);
            };
        }
        foreach(Control child in control.Controls)Apply(child);
    }

    public static void AddSeparators(Control control)
    {
        control.Paint+=(sender,e)=>
        {
            int x=(int)(6*ReplayWindowLayout.DpiScale);
            using(var pen=new Pen(Border))
            {e.Graphics.DrawLine(pen,x,0,Math.Max(x,control.Width-x),0);e.Graphics.DrawLine(pen,x,control.Height-1,Math.Max(x,control.Width-x),control.Height-1);}
        };
    }

    static GraphicsPath Rounded(RectangleF rectangle,float radius)
    {
        var path=new GraphicsPath();float diameter=Math.Max(1,Math.Min(radius*2,Math.Min(rectangle.Width,rectangle.Height)));
        path.AddArc(rectangle.Left,rectangle.Top,diameter,diameter,180,90);
        path.AddArc(rectangle.Right-diameter,rectangle.Top,diameter,diameter,270,90);
        path.AddArc(rectangle.Right-diameter,rectangle.Bottom-diameter,diameter,diameter,0,90);
        path.AddArc(rectangle.Left,rectangle.Bottom-diameter,diameter,diameter,90,90);path.CloseFigure();return path;
    }

    public static void PaintButton(DesktopActionButton button,Graphics graphics,bool focused)
    {
        if(button.Width<2 || button.Height<2)return;
        Color background=button.Parent==null?Background:button.Parent.BackColor;
        if(background==Color.Transparent)background=Background;
        graphics.Clear(background);graphics.SmoothingMode=SmoothingMode.AntiAlias;
        bool hover=button.Enabled && button.ClientRectangle.Contains(button.PointToClient(Control.MousePosition));
        Color fill=hover?ControlPaint.Light(button.BackColor,.08F):button.BackColor;
        var rectangle=new RectangleF(.5F,.5F,button.Width-1,button.Height-1);
        using(var path=Rounded(rectangle,4*ReplayWindowLayout.DpiScale))
        {
            using(var brush=new SolidBrush(fill))graphics.FillPath(brush,path);
            if(!button.Quiet && button.AccessibleName!="buy" && button.AccessibleName!="sell")
                using(var pen=new Pen(Border))graphics.DrawPath(pen,path);
            if(focused)using(var pen=new Pen(Accent,Math.Max(1,ReplayWindowLayout.DpiScale)))graphics.DrawPath(pen,path);
        }
        var text=button.ClientRectangle;text.Inflate(-(int)(5*ReplayWindowLayout.DpiScale),0);
        TextRenderer.DrawText(graphics,button.Text,button.Font,text,button.Enabled?button.ForeColor:Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    }
}

public sealed class GraphiteAccountLabel:Label
{
    public GraphiteAccountLabel(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);}
    protected override void OnPaint(PaintEventArgs e)
    {
        var match=Regex.Match(Text??"",@"余额\s+([-\d.,]+)\s+净值\s+([-\d.,]+)");
        string balance=match.Success?Format(match.Groups[1].Value):"—",equity=match.Success?Format(match.Groups[2].Value):"—";
        float scale=ReplayWindowLayout.DpiScale;int half=Width/2,top=(int)(1*scale),valueTop=(int)(17*scale);
        using(var labelFont=new Font("Microsoft YaHei UI",9F))
        using(var valueFont=new Font("Segoe UI",14F))
        {
            var labelFlags=TextFormatFlags.Left|TextFormatFlags.Top|TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(e.Graphics,"余额 · USD",labelFont,new Rectangle(0,top,half,Math.Max(1,valueTop-top)),GraphiteTheme.Muted,labelFlags);
            TextRenderer.DrawText(e.Graphics,"净值 · USD",labelFont,new Rectangle(half,top,Width-half,Math.Max(1,valueTop-top)),GraphiteTheme.Muted,labelFlags);
            TextRenderer.DrawText(e.Graphics,balance,valueFont,new Rectangle(0,valueTop,half,Math.Max(1,Height-valueTop)),GraphiteTheme.Text,labelFlags);
            TextRenderer.DrawText(e.Graphics,equity,valueFont,new Rectangle(half,valueTop,Width-half,Math.Max(1,Height-valueTop)),GraphiteTheme.Text,labelFlags);
        }
    }
    static string Format(string value){decimal number;return Decimal.TryParse(value,NumberStyles.Number,CultureInfo.InvariantCulture,out number)?number.ToString("#,##0.00",CultureInfo.InvariantCulture):value;}
}

public sealed class GraphiteTabControl:TabControl
{
    public GraphiteTabControl(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(GraphiteTheme.Background);}
    protected override void OnSelectedIndexChanged(EventArgs e){base.OnSelectedIndexChanged(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(GraphiteTheme.Background);
        var display=DisplayRectangle;
        using(var pen=new Pen(GraphiteTheme.Border))e.Graphics.DrawRectangle(pen,display.Left-1,display.Top-1,display.Width+1,display.Height+1);
        for(int i=0;i<TabPages.Count;i++)
        {
            var rectangle=GetTabRect(i);bool selected=i==SelectedIndex;
            using(var brush=new SolidBrush(selected?GraphiteTheme.Surface:GraphiteTheme.Background))e.Graphics.FillRectangle(brush,rectangle);
            TextRenderer.DrawText(e.Graphics,TabPages[i].Text,Font,rectangle,selected?GraphiteTheme.Text:GraphiteTheme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            if(selected)using(var pen=new Pen(GraphiteTheme.Accent,Math.Max(1,ReplayWindowLayout.DpiScale)))e.Graphics.DrawLine(pen,rectangle.Left+6,rectangle.Bottom-1,rectangle.Right-6,rectangle.Bottom-1);
        }
    }
}
