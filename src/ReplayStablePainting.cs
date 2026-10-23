using System.Windows.Forms;

public class ReplayBufferedForm:Form
{public ReplayBufferedForm(){DoubleBuffered=true;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}}
public sealed class ReplayBufferedTable:TableLayoutPanel
{public ReplayBufferedTable(){DoubleBuffered=true;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}}
public sealed class ReplayBufferedPanel:Panel
{public ReplayBufferedPanel(){DoubleBuffered=true;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}}
public sealed class ReplayBufferedGrid:DataGridView
{public ReplayBufferedGrid(){DoubleBuffered=true;}}
public sealed class ReplayStableLabel:Label
{public ReplayStableLabel(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);}}
