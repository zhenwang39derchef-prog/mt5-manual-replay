using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

public sealed class PlanChoiceButton : Button
{
    bool selected;public bool Multi {get;set;}
    public bool Selected {get{return selected;}set{if(selected==value)return;selected=value;Invalidate();}}
    public PlanChoiceButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);Invalidate();}protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){ReplayPaper.PaintButton(this,e.Graphics,false,false,Selected,true,Multi,Focused&&ShowFocusCues);}
}

public sealed class PlanReasonChoices:UserControl
{
    public sealed class ChoiceCollection:System.Collections.ObjectModel.Collection<TrainingChoice>
    {readonly PlanReasonChoices owner;public ChoiceCollection(PlanReasonChoices control){owner=control;}protected override void InsertItem(int index,TrainingChoice item){base.InsertItem(index,item);owner.Build();}}
    readonly HashSet<int> selected=new HashSet<int>();readonly List<PlanChoiceButton> buttons=new List<PlanChoiceButton>();
    public ChoiceCollection Items {get;private set;}
    public IList<TrainingChoice> CheckedItems {get{return Items.Where((x,i)=>selected.Contains(i)).ToList().AsReadOnly();}}
    public bool GetItemChecked(int index){return selected.Contains(index);}
    public void SetItemChecked(int index,bool value)
    {if(index<0||index>=Items.Count)throw new ArgumentOutOfRangeException("index");if(value)selected.Add(index);else selected.Remove(index);buttons[index].Selected=value;buttons[index].AccessibleDescription=value?"已选择":"未选择";}
    public PlanReasonChoices(){Items=new ChoiceCollection(this);AutoScaleMode=AutoScaleMode.None;AutoScroll=true;BackColor=ReplayPaper.Background;Margin=Padding.Empty;}
    void Build()
    {
        foreach(Control c in Controls.Cast<Control>().ToArray())c.Dispose();Controls.Clear();buttons.Clear();
        var grid=new ReplayBufferedTable{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,RowCount=(Items.Count+1)/2,Margin=Padding.Empty};grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));Controls.Add(grid);
        for(int r=0;r<grid.RowCount;r++)grid.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
        for(int i=0;i<Items.Count;i++){int index=i;var button=new PlanChoiceButton{Text=Items[i].Text,Multi=true,Selected=selected.Contains(i),Dock=DockStyle.Fill,Margin=new Padding(4),AccessibleName=Items[i].Text,AccessibleRole=AccessibleRole.CheckButton,AccessibleDescription=selected.Contains(i)?"已选择":"未选择"};button.Click+=(s,e)=>SetItemChecked(index,!GetItemChecked(index));buttons.Add(button);grid.Controls.Add(button,i%2,i/2);}
    }
    protected override void OnEnter(EventArgs e){base.OnEnter(e);if(buttons.Count>0)buttons[0].Focus();}
}

public sealed class PlanAnswerChoices : UserControl
{
    readonly List<TrainingChoice> items=new List<TrainingChoice>();
    readonly List<PlanChoiceButton> answers=new List<PlanChoiceButton>();
    int selected=-1;
    public IList<TrainingChoice> Items {get{return items.AsReadOnly();}}
    public TrainingChoice SelectedItem {get{return selected>=0?items[selected]:null;}}
    public string Code {get{return SelectedItem==null?"":SelectedItem.Code;}}
    public event EventHandler SelectedIndexChanged;
    public int SelectedIndex
    {
        get{return selected;}
        set
        {
            if(value<-1 || value>=items.Count)throw new ArgumentOutOfRangeException("value");if(selected==value)return;
            selected=value;for(int i=0;i<answers.Count;i++){answers[i].Selected=i==selected;answers[i].AccessibleDescription=i==selected?"已选择":"未选择";answers[i].FlatAppearance.BorderSize=i==selected?2:1;answers[i].BackColor=i==selected?GraphiteTheme.Soft:GraphiteTheme.Surface;answers[i].ForeColor=i==selected?GraphiteTheme.Accent:GraphiteTheme.Text;}
            if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);
        }
    }
    public PlanAnswerChoices(IEnumerable<TrainingChoice> choices)
    {
        AutoScaleMode=AutoScaleMode.None;Dock=DockStyle.Fill;AutoScroll=true;Margin=Padding.Empty;BackColor=GraphiteTheme.Background;
        items.AddRange(choices.Where(x=>x.Code!=""));
        var grid=new ReplayBufferedTable{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,RowCount=(items.Count+1)/2,Margin=Padding.Empty};
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));Controls.Add(grid);
        for(int row=0;row<grid.RowCount;row++)grid.RowStyles.Add(new RowStyle(SizeType.Absolute,64));
        for(int i=0;i<items.Count;i++)
        {
            int index=i;var answer=new PlanChoiceButton{Text=items[i].Text,AccessibleName=items[i].Text,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleLeft,Dock=DockStyle.Fill,Margin=new Padding(4),Padding=new Padding(10,3,8,3),BackColor=ReplayPaper.Surface,ForeColor=ReplayPaper.Ink};
            answer.FlatAppearance.BorderColor=GraphiteTheme.Border;answer.Click+=(s,e)=>SelectedIndex=index;answers.Add(answer);grid.Controls.Add(answer,i%2,i/2);
        }
    }
    protected override void OnEnter(EventArgs e){base.OnEnter(e);if(answers.Count>0)answers[Math.Max(0,selected)].Focus();}
}
