using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Drawing2D;
using Gtk;
using System.Reflection;
using System.Runtime.InteropServices;

// because some people are too fucking scared of something not in ECMA
using GRect = Gdk.Rectangle;

public class HeapScroller : Bin {
	int border = 8;
	int box_spacing = 2;
	int box_top_padding = 6;
	private Glass glass;

	Gdk.Window event_window;

	public GRect background;
	public GRect legend;
	
	protected override bool OnButtonPressEvent (Gdk.EventButton args)
	{
		double x = args.X + Allocation.X;
                double y = args.Y + Allocation.Y;
		
		if (glass.IsInside (x, y))
			glass.StartDrag (x, y, args.Time);
		else {
			int x_new = (int) x - background.X;
			
			if (x_new < 0)
				x_new = 0;
			else if (x_new + glass.Width > background.Width)
				x_new = background.Width - glass.Width;

			glass.Position = x_new;
			
			ScrollChanged ();
		}
		
		return base.OnButtonPressEvent (args);
	}
	
	protected override bool OnButtonReleaseEvent (Gdk.EventButton args) 
	{
		double x = args.X + Allocation.X;
                double y = args.Y + Allocation.Y;
		
		if (glass.Dragging)
			glass.EndDrag (x, y);
		
		return base.OnButtonReleaseEvent (args);
	}

	protected override bool OnMotionNotifyEvent (Gdk.EventMotion args) 
	{
		double x = args.X + Allocation.X;
                double y = args.Y + Allocation.Y;
		
		
		GRect box = glass.Bounds ();

		if (glass.Dragging) {
			glass.UpdateDrag (x, y);

		} else {
			if (glass.IsInside (x, y))
				glass.State = StateType.Prelight;
			else 
				glass.State = StateType.Normal;
		}

		return base.OnMotionNotifyEvent (args);
	}

	protected override void OnRealized ()
	{
		WidgetFlags |= WidgetFlags.Realized;
		GdkWindow = ParentWindow;

		base.OnRealized ();
		
		Gdk.WindowAttr attr = Gdk.WindowAttr.Zero;
		attr.WindowType = Gdk.WindowType.Child;

		attr.X = Allocation.X;
		attr.Y = Allocation.Y;
		attr.Width = Allocation.Width;
		attr.Height = Allocation.Height;
		attr.Wclass = Gdk.WindowClass.InputOnly;
		attr.EventMask = (int) Events;
		attr.EventMask |= (int) (Gdk.EventMask.ButtonPressMask | 
			Gdk.EventMask.ButtonReleaseMask | 
			Gdk.EventMask.PointerMotionMask);
			
		event_window = new Gdk.Window (GdkWindow, attr, (int) (Gdk.WindowAttributesType.X | Gdk.WindowAttributesType.Y));
		event_window.UserData = this.Handle;
	}

	protected override void OnUnrealized () 
	{
		event_window.Dispose ();
		event_window = null;
	}
	
	protected void ScrollChanged ()
	{
		if (OnScrolled != null)
			OnScrolled ();
	}
	
	public abstract class Manipulator {
		protected HeapScroller selector;
		public bool Dragging;

		int x_0;
		int x_n;
			
		public abstract int Width {
			get;
		}
			

		public void StartDrag (double x, double y, uint time) 
		{
			State = StateType.Active;
			Dragging = true;
			x_n = Position;
			x_0 = (int)x;
			
			Console.WriteLine ("Position: {0}", Position);
		}

		public void UpdateDrag (double x, double y)
		{
			
			GRect then = Bounds ();
			
			int x_new = position + (int) x - x_0;
			
			if (x_new < 0)
				x_n = 0;
			else if (x_new + Width > selector.background.Width)
				x_n = selector.background.Width - Width;
			else
				x_n = x_new;
			
			GRect now = Bounds ();
			
			if (selector.Visible) {
				selector.GdkWindow.InvalidateRect (then, false);
				selector.GdkWindow.InvalidateRect (now, false);
			}
		}

		public void EndDrag (double x, double y)
		{
			GRect box = Bounds ();

			Position = x_n;
			State = StateType.Prelight;
			
			Dragging = false;

			selector.ScrollChanged ();
		}

		private StateType state;
		public StateType State {
			get {
				return state;
			}
			set {
				if (state != value)
					selector.GdkWindow.InvalidateRect (Bounds (), false);
				state = value;
			}
		}

		private int position;
		public int Position {
			get {
				return position;
			}
			set {
				GRect then = Bounds ();
				position = value;
				GRect now = Bounds ();
				
				if (selector.Visible) {
					selector.GdkWindow.InvalidateRect (then, false);
					selector.GdkWindow.InvalidateRect (now, false);
				}
			}
		}

		public abstract void Draw (GRect area);
		public abstract GRect Bounds ();

		public virtual bool IsInside (double x, double y)
		{
			return Bounds ().Contains ((int) x, (int) y);
		}

		public Manipulator (HeapScroller selector) 
		{
			this.selector = selector;
		}
		
		protected int XPos {
			get {
				if (! Dragging)
					return Position;
				
				return x_n;
			}
		}
	}
	
	private class Glass : Manipulator {
		private int handle_height = 15;

		private int border {
			get {
				return selector.box_spacing * 2;
			}
		}
		
		private GRect InnerBounds ()
		{
			GRect box = GRect.Zero;
			box.Height = selector.background.Height;
			box.Y = selector.background.Y;
			
			box.X = selector.background.X + XPos;
			box.Width = Width;
			
			return box;
		}
		
		public override GRect Bounds () 
		{
			GRect box = InnerBounds ();

			box.Inflate (border, border);
			box.Height += handle_height;
			
			return box;
		}

		public override void Draw (GRect area)
		{
			GRect inner = InnerBounds ();
			GRect bounds = Bounds ();
			
			if (bounds.Intersect (area, out area)) {
				GRect box = inner;
				box.Width -= 1;
				box.Height -= 1;
				for (int i = 0; i < border; i ++) {
					box.Inflate (1, 1);
					
					selector.GdkWindow.DrawRectangle (selector.Style.BackgroundGC (State), false, box);
				}
			
				Style.PaintHandle (selector.Style, selector.GdkWindow, State, ShadowType.None, 
						    area, selector, "glass", bounds.X, inner.Y + inner. Height, 
						    bounds.Width, handle_height + border, Orientation.Horizontal);

				Style.PaintShadow (selector.Style, selector.GdkWindow, State, ShadowType.Out, 
						   area, selector, null, bounds.X, bounds.Y, bounds.Width, bounds.Height);

				Style.PaintShadow (selector.Style, selector.GdkWindow, State, ShadowType.In, 
						   area, selector, null, inner.X, inner.Y, inner.Width, inner.Height);

			}
		}
		
		public override int Width {
			get { return (selector.TimeSpan * selector.background.Width) / selector.maxt; }
		}
		
		public Glass (HeapScroller selector) : base (selector) {}
	}
	
	protected override void OnMapped ()
	{
		base.OnMapped ();
		if (event_window != null)
			event_window.Show ();
	}
	
	protected override void OnUnmapped ()
	{
		base.OnUnmapped ();
		if (event_window != null)
			event_window.Hide ();
	}
	
	protected override bool OnExposeEvent (Gdk.EventExpose args)
	{
		if (bitmap_cache == null) {
			bitmap_cache = new Gdk.Pixmap (GdkWindow, background.Width, background.Height, -1);
			bitmap_cache.DrawRectangle (Style.WhiteGC, true, 0, 0,
				background.Width, background.Height);
			
			using (Graphics g = Gtk.DotNet.Graphics.FromDrawable (bitmap_cache)) {
				Plot (g);
			}
		}
		
		GRect area;
		if (args.Area.Intersect (background, out area))
			GdkWindow.DrawRectangle (Style.BaseGC (State), true, area);
		
		GdkWindow.DrawDrawable (Style.BlackGC,
						bitmap_cache,
						0, 0,
						background.X, background.Y,
						background.Width, background.Height);

		Style.PaintShadow (this.Style, GdkWindow, State, ShadowType.In, area, 
				   this, null, background.X, background.Y, 
				   background.Width, background.Height);
		       
		if (glass != null) {
			glass.Draw (args.Area);
		}

		return base.OnExposeEvent (args);
	}
	
	protected override void OnSizeAllocated (GRect alloc)
	{
		base.OnSizeAllocated (alloc);
		int legend_height = 20;
		
		background = GRect.Inflate (alloc, -border, -border);
		background.Height -= legend_height;

		legend = new GRect (border, background.Bottom, background.Width, legend_height);

		if (event_window != null) {
			event_window.MoveResize (alloc.X, alloc.Y, alloc.Width, alloc.Height);
			event_window.Move (alloc.X, alloc.Y);
		}
			
		UpdateCache ();
	}

	public HeapScroller (Profile p)
	{
		this.p = p;
		Events |= Gdk.EventMask.ButtonPressMask;
		
		Timeline [] tl = p.Timeline;
		
		maxt = tl [tl.Length - 1].Time;
		
		time_span = maxt / 5;
		
		SetSizeRequest (100, 100);
		
		WidgetFlags |= WidgetFlags.NoWindow;

		glass = new Glass (this);
	}
	
	Profile p;
	
	Gdk.Pixmap bitmap_cache;

	void Plot (Graphics g)
	{
		int maxx = background.Width;
		int maxy = background.Height;
		
		Timeline [] tl = p.Timeline;

		int maxsz = p.MaxSize;
		
		
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
	
	int maxt;
	int time_span;
	
	public int StartTime {
		get { return (glass.Position * maxt) / background.Width; }
	}
	
	public int EndTime {
		get { return StartTime + time_span; }
	}
	
	public int TimeSpan {
		get { return time_span; }
	}
	
	void UpdateCache ()
	{
		if (bitmap_cache != null)
			bitmap_cache.Dispose ();
			
		bitmap_cache = null;
	}

	public delegate void ScrollChangedDelegate ();
	public event ScrollChangedDelegate OnScrolled;
}