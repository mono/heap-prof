using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Drawing2D;

class TimePoint {
	public int Time;
	public int X;
	public int OtherSize;
	public int [] TypeData;
	public TimeData Data;
	public int HeapSize;
}

class Plotter {
	int xsize, ysize;
	TypeTabulator d;
	TypeList tl;
	
	public Plotter (int xsize, int ysize, TypeTabulator d, TypeList tl)
	{
		this.xsize = xsize;
		this.ysize = ysize;
		this.d = d;
		this.tl = tl;
		
		FixupData ();
	}
	
	public ArrayList data;

	int end_t;
	
	void FixupData ()
	{
		end_t = ((TimeData) d.Data [d.Data.Count - 1]).Time;
		
		data = new ArrayList ();
		int size_threshold = d.MaxSize / ysize;
		
		foreach (TimeData td in d.Data) {
			if (td.HeapSize < size_threshold)
				continue;
			
			TimePoint p = new TimePoint ();
			
			data.Add (p);
			
			p.Data = td;
			p.Time = td.Time;
			p.X = td.Time * xsize / end_t;
			p.OtherSize = td.OtherSize;
			p.TypeData = new int [tl.TypeIndexes.Length];
			p.HeapSize = td.HeapSize;
			
			for (int i = 0; i < tl.TypeIndexes.Length; i ++) {
				int ty = tl.TypeIndexes [i];
				
				if (td.TypeData [ty] < size_threshold) {
					p.OtherSize += td.TypeData [ty];
					continue;
				}
				
				p.TypeData [i] = td.TypeData [ty];
			}
		}
	}
	
	public void Draw (Graphics g)
	{
		
		int [] offsets = new int [data.Count];
		Point [] prev = new Point [data.Count];
		
		for (int i = 0; i < prev.Length; i ++)
			prev [i].Y = ysize;
		
		prev [0].X = xsize;
		
		for (int i = -1; i <  tl.TypeIndexes.Length; i ++) {
			Point [] line = new Point [data.Count];
			
			int j = 0;
			foreach (TimePoint tp in data) {
				
				
				int psize;
				
				if (i == -1)
					psize = tp.OtherSize;
				else
					psize = tp.TypeData [i];
				
				line [j].X = tp.X;
				line [j].Y = ysize - checked (offsets [j] + (int)((long)psize * (long) ysize / (long)d.MaxSize));
				offsets [j] = ysize - line [j].Y;
				j ++;
			}
			
			GraphicsPath path = new GraphicsPath ();
			path.AddLines (line);
			path.AddLine (line [line.Length - 1], prev [0]);
			path.AddLines (prev);
			//path.CloseFigure ();
			
			Brush b;
			if (i == -1)
				b = Brushes.DarkGray;
			else
				b = tl.TypeBrushes [i];
			
			g.FillPath (b, path);
			
			prev = line;
			Array.Reverse (prev, 0, prev.Length);
		}
	
		{
			Point [] line = new Point [data.Count];
			
			int j = 0;
			foreach (TimePoint tp in data) {
				
				
				int psize = tp.HeapSize;
				line [j].X = tp.X;
				line [j].Y = ysize - checked ((int)((long)psize * (long) ysize / (long)d.MaxSize));
				j ++;
			}
			
			g.DrawLines (Pens.Black, line);
		}
	}

}

class RandomBrush {
	int i;
	
	static Brush [] brushes = {
		Brushes.IndianRed,
		Brushes.BurlyWood,
		Brushes.Chocolate,
		Brushes.DarkGoldenrod,
		Brushes.PaleGoldenrod,
		Brushes.DarkOrange,
		Brushes.DarkSalmon,
		
	};
	
	public Brush Next ()
	{
		return brushes [i ++ % brushes.Length];
	}
}

class TypeList {
	public long [] Sizes;
	public int [] TypeIndexes;
	public Brush [] TypeBrushes;
	public string [] Names;
	
	public TypeList (TypeTabulator d)
	{
		int num = 0;
		
		foreach (bool b in d.IsSizeLongEnough)
			if (b)
				num ++;
			
		Sizes = new long [num];
		TypeIndexes = new int [num];
		Names = new string [num];
		TypeBrushes = new Brush [num];
			
		num = 0;
		for (int i = 0; i < d.TotalTypeSizes.Length; i ++) {
			if (d.IsSizeLongEnough [i]) {
				Sizes [num] = d.TotalTypeSizes [i];
				TypeIndexes [num] = i;
				num ++;
			}
		}
		
		Array.Sort (Sizes, TypeIndexes);
		
		RandomBrush rb = new RandomBrush ();
		
		for (int i = 0; i < Sizes.Length; i ++) {
			TypeBrushes [i] = rb.Next ();
			Names [i] = d.GetTypeName (TypeIndexes [i]);
		}
	}
}
