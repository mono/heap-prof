using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Drawing2D;
using Gtk;
using System.Reflection;
using System.Runtime.InteropServices;

class TypeGraphComponent : ShellComponent {
	DrawingArea d;
	TypeTabulator t;
	TypeList tl;
	HPaned paned;
	
	public TypeGraphComponent (TypeTabulator t)
	{
		Title = "Type Graph";
		
		this.t = t;
		tl = new TypeList (t);
		
		paned = new HPaned ();
		
		d = new PrettyGraphic (t, tl, this);
		
		
		
		ScrolledWindow sw = new ScrolledWindow ();
		sw.Add (new TypeListNodeStore (tl).GetNodeView ());
		
		paned.Pack1 (d, true, true);
		paned.Pack2 (sw, false, true);


		Add (paned);
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

[TreeNode (ColumnCount = 2)]
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


//
// A sample using inheritance to draw
//
class PrettyGraphic : DrawingArea {

	TypeTabulator t;
	TypeList tl;
	
	Gdk.Pixmap bitmap_cache;
	//System.Drawing.Bitmap bitmap_cache;
	Gdk.Rectangle current_allocation;	// The current allocation. 
	bool allocated = false;
	Plotter plot;
	TypeGraphComponent parent;
	
	public PrettyGraphic (TypeTabulator t, TypeList tl, TypeGraphComponent parent)
	{
		Events |= Gdk.EventMask.ButtonPressMask;
		
		this.t = t;
		this.tl = tl;
		this.parent = parent;
		SetSizeRequest (700, 700);
	}
			       
	protected override bool OnExposeEvent (Gdk.EventExpose args)
	{
		
		if (bitmap_cache == null) {
			bitmap_cache = new Gdk.Pixmap (GdkWindow, current_allocation.Width, current_allocation.Height, -1);
			bitmap_cache.DrawRectangle (Style.WhiteGC, true, 0, 0,
				current_allocation.Width, current_allocation.Height);
			
			using (Graphics g = Gtk.DotNet.Graphics.FromDrawable (bitmap_cache)) {
				plot = new Plotter (current_allocation.Width, current_allocation.Height, t, tl);
				plot.Draw (g);
			}
		}
		
		Gdk.Rectangle area = args.Area;
		GdkWindow.DrawDrawable (Style.BlackGC,
						bitmap_cache,
						area.X, area.Y,
						area.X, area.Y,
						area.Width, area.Height);
		
		return true;
	}
	
	protected override void OnSizeAllocated (Gdk.Rectangle allocation)
	{
		allocated = true;
		current_allocation = allocation;
		UpdateCache ();
		base.OnSizeAllocated (allocation);
	}

	void UpdateCache ()
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
				
				parent.Parent.Add (new BacktraceViewerComponent (tp.Data, t));
				
				break;
			}
		}
		
		return true;
	}
}

