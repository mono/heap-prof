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
	Profile Profile;
	
	public Plotter (int xsize, int ysize, TypeTabulator d, TypeList tl)
	{
		this.xsize = xsize;
		this.ysize = ysize;
		this.d = d;
		this.tl = tl;
		this.Profile = d.Profile;
		
		FixupData ();
	}
	
	public ArrayList data;

	int end_t;
	
	void FixupData ()
	{
		int start_t = d.StartTime;
		int end_t = ((TimeData) d.Data [d.Data.Count - 1]).Time;
		int del_t = end_t - start_t;
		
		data = new ArrayList ();
		int size_threshold = (Profile.MaxSize / ysize) * 3;
		
		foreach (TimeData td in d.Data) {
			if (td.HeapSize < size_threshold)
				continue;
			
			TimePoint p = new TimePoint ();
			
			data.Add (p);
			
			p.Data = td;
			p.Time = td.Time;
			p.X = (td.Time - start_t) * xsize / del_t;
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
		Point [] poly = new Point [data.Count + 2];
		
		for (int i = 0; i < poly.Length; i ++)
			poly [i].Y = ysize;
		
		poly [0] = new Point (0, 0);
		poly [poly.Length - 1] = new Point (xsize, 0);
		poly [poly.Length - 2] = new Point (xsize, ysize);
		
		for (int i = -1; i <  tl.TypeIndexes.Length; i ++) {
			Brush b;
			if (i == -1)
				b = Brushes.DarkGray;
			else
				b = tl.TypeBrushes [i];
			
			g.FillPolygon (b, poly);
			int j = 1;
			foreach (TimePoint tp in data) {
				int psize;
				
				if (i == -1)
					psize = tp.OtherSize;
				else
					psize = tp.TypeData [i];
				
				poly [j].X = tp.X;
				poly [j].Y -= checked ((int)((long)psize * (long) ysize / (long)Profile.MaxSize));
				j ++;
			}
		}
		
		g.FillPolygon (Brushes.White, poly);
	
		{
			Point [] line = new Point [data.Count];
			
			int j = 0;
			foreach (TimePoint tp in data) {
				
				
				int psize = tp.HeapSize;
				line [j].X = tp.X;
				line [j].Y = ysize - checked ((int)((long)psize * (long) ysize / (long)Profile.MaxSize));
				j ++;
			}
			
			g.DrawLines (Pens.Black, line);
		}

#if DEBUG_GRAPH_SIZE
		{
			Point [] line = new Point [data.Count];
			
			int j = 0;
			foreach (TimePoint tp in data) {
				
				
				int psize = tp.Data.TotalSize;
				line [j].X = tp.X;
				line [j].Y = ysize - checked ((int)((long)psize * (long) ysize / (long)Profile.MaxSize));
				j ++;
			}
			
			g.DrawLines (Pens.Black, line);
		}
#endif
	}

}

class RandomBrush {
	int i;
	
	static Brush [] brushes;
	
	const int N_COLORS = 26;
	const int MOD = 7;

	static RandomBrush () {
		
		Color [] c = Generate (N_COLORS);
		
		brushes = new Brush [c.Length];
		
		for (int i = 0; i < c.Length; i ++)
			brushes [i] = new SolidBrush (c [i]);
	}
	
	static Color [] Generate (int range)
	{
		
		float hue_step = 1.0f / range;
		float hue_base = 0.0f;
		
		int mod_check;
		
		Color [] ret = new Color [range];
		for (int i = 0; i < range; i ++) {
			
			float hue_offset, hue;
			
			hue = hue_step * ((MOD * i) % N_COLORS);
			
			
			Console.WriteLine (hue);
			ret [i] = HSV2RGB (hue, .60f, .80f);
		}
		
		return ret;
	}
	
	public static Color HSV2RGB (float H, float S, float V) {
		if (S == 0.0)
			return new Color ();
		
		H *= 6.0f;
		int i = (int) Math.Floor (H);
		float f = H - (float)i;
		float p = V*(1.0f - S);
		float q = V*(1.0f - S * f);
		float t = V*(1.0f - (S* (1.0f - f)));
		
		
		int i_v = (int) (V * 255.0f);
		int i_p = (int) (p * 255.0f);
		int i_q = (int) (q * 255.0f);
		int i_t = (int) (t * 255.0f);
		switch (i) {
			case 0: return Color.FromArgb (i_v, i_t, i_p);
			case 1: return Color.FromArgb (i_q, i_v, i_p);
			case 2: return Color.FromArgb (i_p, i_v, i_t);
			case 3: return Color.FromArgb (i_p, i_q, i_v);
			case 4: return Color.FromArgb (i_t, i_p, i_v);
			case 5: return Color.FromArgb (i_v, i_p, i_q);
			default:
				return new Color ();
		}
	}
	
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
	
	public TypeList (Profile p)
	{
		int num = 0;
		
		int cutoff = (int) (p.MaxSize * TypeTabulator.Threshold);
		
		foreach (long l in p.Metadata.TypeMax)
			if (l >= cutoff)
				num ++;
			
		Sizes = new long [num];
		TypeIndexes = new int [num];
		Names = new string [num];
		TypeBrushes = new Brush [num];
			
		num = 0;
		for (int i = 0; i < p.Metadata.TypeMax.Length; i ++) {
			if (p.Metadata.TypeMax [i] >= cutoff) {
				Sizes [num] = p.Metadata.TypeMax [i];
				TypeIndexes [num] = i;
				num ++;
			}
		}
		
		Array.Sort (Sizes, TypeIndexes);
		
		RandomBrush rb = new RandomBrush ();
		
		for (int i = 0; i < Sizes.Length; i ++) {
			TypeBrushes [i] = rb.Next ();
			Names [i] = p.GetTypeName (TypeIndexes [i]);
		}
	}
}
