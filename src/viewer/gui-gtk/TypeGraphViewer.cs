using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Drawing2D;
using Gtk;
using System.Reflection;
using System.Runtime.InteropServices;

class TypeGraphComponent : ShellComponent {
	TypeGrpah d;
	TypeTabulator t;
	TypeList tl;
	HPaned paned;
	VBox box;
	HeapScroller scroller;
	public Profile Profile;
	
	public TypeGraphComponent (Profile p)
	{
		Profile = p;
		
		Title = "Type Graph";
		
		box = new VBox ();
		box.Spacing = 12;
		
		paned = new HPaned ();
		

		Add (box);

		scroller = new HeapScroller (p);
		scroller.OnScrolled += delegate { t = null; d.UpdateCache (); d.QueueDraw (); };
		
		box.PackStart (scroller, false, false, 0);

		// FIXME: HACKISH
		TypeTabulator xxx = new TypeTabulator (p);
		xxx.Read ();
		xxx.Process ();
		tl = new TypeList (xxx);

		d = new TypeGrpah (tl, this);
		
		ScrolledWindow sw = new ScrolledWindow ();
		sw.Add (new TypeListNodeStore (tl).GetNodeView ());
		
		paned.Pack1 (d, true, true);
		paned.Pack2 (sw, false, true);

		box.PackStart (paned, true, true, 0);
	}
	
	public int StartTime {
		get { return scroller.StartTime; }
	}
	
	public int EndTime {
		get { return scroller.EndTime; }
	}
	
	public TypeTabulator CurrentTabulator {
		get {
			if (t == null) {
				Console.WriteLine ("start: {0}", StartTime);
				Console.WriteLine ("end: {0}", EndTime);
				t = new TypeTabulator (Profile, StartTime, EndTime);
				t.Read ();
				t.Process ();
			}
			
			return t;
		}
	}
}


class TypeListNodeStore : NodeStore {
	TypeList tl;
	static ColorCellRenderer r;
	
	public TypeListNodeStore (TypeList tl) : base (typeof (TypeListTreeNode))
	{
		this.tl = tl;
		
		for (int i =  tl.Sizes.Length - 1; i >= 0; i --)
			AddNode (new TypeListTreeNode (tl, i));
	}
	
	public NodeView GetNodeView ()
	{
		r = new ColorCellRenderer ();
		NodeView nv = new NodeView (this);
		nv.SetSizeRequest (250, 700);
		nv.HeadersVisible = false;
		nv.AppendColumn ("Color",r, new NodeCellDataFunc (GetColorData));
		nv.AppendColumn ("Type",  new CellRendererText (),  "text", 1);
		
		return nv;
	}
	
	private void GetColorData (TreeViewColumn col, CellRenderer cell, ITreeNode node)
	{
		ColorCellRenderer c = (ColorCellRenderer) cell;
		c.Idx = ((TypeListTreeNode) node).idx;
		c.List = tl;
	}

}

class ColorCellRenderer : CellRenderer {

	public int Idx;
	public TypeList List;
	
	Gdk.Color ColorFromBrush (Brush b)
	{
		Color c = ((SolidBrush) b).Color;
		return new Gdk.Color (c.R, c.G, c.B);
	}

	public override void GetSize (Widget widget, ref Gdk.Rectangle cell_area, out int x_offset, out int y_offset, out int width, out int height)
	{
		int calc_width = (int) this.Xpad * 2 + 10;
		int calc_height = (int) this.Ypad * 2 + 10;

		width = calc_width;
		height = calc_height;

		x_offset = 0;
		y_offset = 0;
		if (!cell_area.Equals (Gdk.Rectangle.Zero)) {
			x_offset = (int) (this.Xalign * (cell_area.Width - calc_width));
			x_offset = Math.Max (x_offset, 0);
			
			y_offset = (int) (this.Yalign * (cell_area.Height - calc_height));
			y_offset = Math.Max (y_offset, 0);
		}
	}

	protected override void Render (Gdk.Drawable window, Widget widget, Gdk.Rectangle background_area,
		Gdk.Rectangle cell_area, Gdk.Rectangle expose_area, CellRendererState flags)
	{
		int width = 0, height = 0, x_offset = 0, y_offset = 0;
		GetSize (widget, ref cell_area, out x_offset, out y_offset, out width, out height);

		width -= (int) this.Xpad * 2;
		height -= (int) this.Ypad * 2;

		Gdk.Rectangle clipping_area = new Gdk.Rectangle ((int) (cell_area.X + x_offset + this.Xpad),
			(int) (cell_area.Y + y_offset + this.Ypad), width - 1, height - 1);
		
		
		using (Gdk.GC gc = new Gdk.GC (window)) {
			gc.RgbFgColor = ColorFromBrush (List.TypeBrushes [Idx]);
			window.DrawRectangle (gc,
						true,
						clipping_area);
	
		}
	}
}

[TreeNode (ColumnCount = 2, ListOnly = true)]
class TypeListTreeNode : TreeNode {

	TypeList tl;
	public int idx;

	public TypeListTreeNode (TypeList tl, int idx)
	{
		this.tl = tl;
		this.idx = idx;
	}

	[TreeNodeValue (Column = 0)]
	public Brush Color {
		get { return tl.TypeBrushes [idx]; }
	}
	
	[TreeNodeValue (Column = 1)]
	public string Name {
		get { return tl.Names [idx]; }
	}

}

class TypeGrpah : DrawingArea {
	TypeList tl;
	
	Gdk.Pixmap bitmap_cache;
	//System.Drawing.Bitmap bitmap_cache;
	Gdk.Rectangle allocation;	// The current allocation. 
	bool allocated = false;
	Plotter plot;
	TypeGraphComponent parent;
	
	Gdk.Rectangle graph_area, x_scale, y_scale;
	
	public TypeGrpah (TypeList tl, TypeGraphComponent parent)
	{
		Events |= Gdk.EventMask.ButtonPressMask;

		this.tl = tl;
		this.parent = parent;
		SetSizeRequest (700, 700);
	}
	
	protected override bool OnExposeEvent (Gdk.EventExpose args)
	{
		
		if (bitmap_cache == null) {
			bitmap_cache = new Gdk.Pixmap (GdkWindow, graph_area.Width, graph_area.Height, -1);
			bitmap_cache.DrawRectangle (Style.WhiteGC, true, 0, 0,
				graph_area.Width, graph_area.Height);
			
			using (Graphics g = Gtk.DotNet.Graphics.FromDrawable (bitmap_cache)) {
				plot = new Plotter (graph_area.Width, graph_area.Height, parent.CurrentTabulator, tl);
				plot.Draw (g);
			}
		}
		Gdk.Rectangle area = args.Area;
		
		GdkWindow.DrawDrawable (Style.BlackGC,
						bitmap_cache,
						0, 0,
						graph_area.X, graph_area.Y,
						graph_area.Width, graph_area.Height);

		DrawXScale (area);
		
		return true;
	}
	
	string FormatTime (int ms)
	{
		TimeSpan dt = TimeSpan.FromMilliseconds (ms);
		
		double seconds = (double) (dt.Ticks % TimeSpan.TicksPerMinute) / (double) TimeSpan.TicksPerSecond;
		
		string format;
		
		if (dt.TotalDays > 1)
			format = "{0}.{1}:{2}:{3}";
		else if (dt.TotalHours > 1)
			format = "{1}:{2}:{3}";
		else if (dt.TotalMinutes > 1)
			format = "{2}:{3}";
		else
			format = "{3}";
		
		return String.Format (format, dt.Days, dt.Hours, dt.Minutes, seconds);
	}
	
	void DrawXScale (Gdk.Rectangle area)
	{
		const int tick_space = 15;
		const int major_tick_freq = 5;
		const int minor_tick_height = 5;
		const int major_tick_height = 10;
		
		int t_0 = parent.StartTime;
		int dt = ((parent.EndTime - parent.StartTime) / x_scale.Width) * tick_space;
		
		for (int i = 0; i < x_scale.Width / tick_space; i ++) {
			if (i % major_tick_freq == 0) {
				
				Pango.Layout layout = this.CreatePangoLayout (FormatTime (t_0 + dt * i));
				
				int w, h;
				
				layout.GetPixelSize (out w, out h);
				
				GdkWindow.DrawLayout (Style.BlackGC,  x_scale.X + i * tick_space - w / 2,  x_scale.Y + major_tick_height, layout);			
				GdkWindow.DrawLine (Style.BlackGC, x_scale.X + i * tick_space , x_scale.Y, x_scale.X + i * tick_space, x_scale.Y + major_tick_height);
			} else {
				GdkWindow.DrawLine (Style.BlackGC, x_scale.X + i * tick_space , x_scale.Y, x_scale.X + i * tick_space, x_scale.Y + minor_tick_height);
			}
		}
	}
	
	protected override void OnSizeAllocated (Gdk.Rectangle allocation)
	{
		
		int x_scale_size = 30, y_scale_size = 10;
		
		y_scale = new Gdk.Rectangle (0, 0, y_scale_size, allocation.Height - x_scale_size);
		x_scale = new Gdk.Rectangle (y_scale_size, allocation.Height - x_scale_size, allocation.Width - y_scale_size, x_scale_size);
		graph_area = new Gdk.Rectangle (y_scale_size, 0, allocation.Width - y_scale_size, allocation.Height - x_scale_size);
		
		Console.WriteLine (allocation);
		Console.WriteLine (x_scale);
		
		this.allocation = allocation;
		
		UpdateCache ();
		base.OnSizeAllocated (allocation);
	}
	
	public void UpdateCache ()
	{
		if (bitmap_cache != null)
			bitmap_cache.Dispose ();
			
		bitmap_cache = null;
	}
	
	protected override bool OnButtonPressEvent (Gdk.EventButton e)
	{
		if (e.Button != 3)
			return false;
		
		Console.WriteLine ("Button press at ({0}, {1})", e.X, e.Y);
		
		foreach (TimePoint tp in plot.data) {
			if (tp.X >= e.X) {
				Console.WriteLine ("Found {0}", tp.Time);
				
				parent.Parent.Add (new BacktraceViewerComponent (tp.Data, parent.Profile));
				
				break;
			}
		}
		
		return true;
	}
}

