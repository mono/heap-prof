using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Drawing2D;
using Gtk;
using System.Reflection;
using System.Runtime.InteropServices;

//
// A sample using inheritance to draw
//
class HeapScroller : DrawingArea {

	Profile p;
	
	Gdk.Pixmap bitmap_cache;
	
	Gdk.Rectangle current_allocation;	// The current allocation. 
	bool allocated = false;
	
	public HeapScroller (Profile p)
	{
		this.p = p;
		Events |= Gdk.EventMask.ButtonPressMask;
		
		SetSizeRequest (100, 100);
	}
			       
	protected override bool OnExposeEvent (Gdk.EventExpose args)
	{
		
		if (bitmap_cache == null) {
			bitmap_cache = new Gdk.Pixmap (GdkWindow, current_allocation.Width, current_allocation.Height, -1);
			bitmap_cache.DrawRectangle (Style.WhiteGC, true, 0, 0,
				current_allocation.Width, current_allocation.Height);
			
			using (Graphics g = Gtk.DotNet.Graphics.FromDrawable (bitmap_cache)) {
				Plot (g);
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
		
		return true;
	}
	
	
	void Plot (Graphics g)
	{
		int maxx = current_allocation.Width;
		int maxy = current_allocation.Height;
		
		
		Timeline [] tl = p.Timeline;
		
		int maxt = tl [tl.Length - 1].Time;
		int maxsz = 0;
		
		foreach (Timeline t in tl)
			if (t.Event == EventType.HeapResize)
				maxsz = t.SizeHigh;
		
		
		int lastx = 0;
		int lasty = maxy;
		
		foreach (Timeline t in tl) {
			if (t.Event != EventType.HeapResize)
				continue;

			int x = (int) ((long) t.Time * (long) maxx / (long) maxt);
			int y = maxy - (int) ((long) t.SizeHigh * (long) maxy / (long) maxsz);

			g.DrawLine (Pens.Black, lastx, lasty, x, lasty);
			g.DrawLine (Pens.Black, x, lasty, x, y);
			
			lastx = x;
			lasty = y;
		}
		
		lastx = 0;
		lasty = maxy;
		
		foreach (Timeline t in tl) {
			if (t.Event != EventType.GC)
				continue;

			int x = (int) ((long) t.Time * (long) maxx / (long) maxt);
			int hy = maxy - (int) ((long) t.SizeHigh * (long) maxy / (long) maxsz);
			int ly = maxy - (int) ((long) t.SizeLow * (long) maxy / (long) maxsz);
			
			g.DrawLine (Pens.Blue, lastx, lasty, x, hy);
			g.DrawLine (Pens.Blue, x, hy, x, ly);
			
			lastx = x;
			lasty = ly;
		}
	}
}
