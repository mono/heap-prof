using System;
using System.Collections;

	
class TimeData {
	public int Time;
	public int [] TypeData;
	public int [] ContextData;
	public int OtherSize;
	public int TotalSize;
	public int HeapSize;
}

class TypeTabulator : ProfileReader {
	
	const int DeltaT = 50;
	const double Threshold = .005;

	
	public ArrayList Data;
	public int MaxSize;
	public long [] TotalTypeSizes;
	public bool [] IsSizeLongEnough;
	
	int [] current_type_data;
	int [] current_context_data;
	int last_time;
	int cur_heap_size;
	
	public TypeTabulator (string basename) : base (basename) {
		Data = new ArrayList ();
		current_type_data = new int [TypeTableSize];
		current_context_data = new int [ContextTableSize];
	}
	
	void Split (int time)
	{
		TimeData td = new TimeData ();
		td.Time = time - 1;
		td.TypeData = (int []) current_type_data.Clone ();
		td.ContextData = (int []) current_context_data.Clone ();
		td.HeapSize = cur_heap_size;
		
		foreach (int i in td.TypeData)
			td.TotalSize += i;
		
		MaxSize = Math.Max (td.HeapSize, MaxSize);
		
		Data.Add (td);
	}
	
	void SplitIfNeeded (int time)
	{
		if (time < last_time + DeltaT)
			return;
		
		Split (time - 1);

		last_time = time;
	}
	
	protected override void AllocationSeen (int time, Context ctx, long pos)
	{
		//SplitIfNeeded (time);
		
		current_type_data [ctx.Type] += ctx.Size;
		current_context_data [ctx.Id] ++;
	}
	
	protected override void GcSeen (int time, int gc_num)
	{
		// Splitting twice here gives nice graphs, since you get a strait line
		Split (time);
		ReadGcFreed ();
		Split (time);
		
		last_time = time;
	}
	
	protected override void GcHeapResize (int time, int new_size)
	{
		// Splitting twice here gives nice graphs, since you get a strait line
		Split (time);
		cur_heap_size = new_size;
		Split (time);
		
		last_time = time;
	}
	
	protected override void GcFreedSeen (int time, Context ctx, long pos)
	{
		current_type_data [ctx.Type] -= ctx.Size;
		current_context_data [ctx.Id] --;
	}
	
	public void Dump ()
	{
		long [] sizes = (long []) TotalTypeSizes.Clone ();
		int [] indexes = new int [sizes.Length];
		
		for (int i = 0; i < indexes.Length; i ++)
			indexes [i] = i;
		
		Array.Sort (sizes, indexes);
		
		Array.Reverse (sizes, 0, sizes.Length);
		Array.Reverse (indexes, 0, indexes.Length);
		
		
		foreach (TimeData d in Data) {

			if (d.TotalSize == 0)
				continue;
			
			Console.WriteLine ("Heap at {0} ms", d.Time);
			Console.WriteLine ("Total heap size {0}", d.TotalSize);
			
			foreach (int ty in indexes) {
				if (!IsSizeLongEnough [ty])
					continue;
				
				Console.WriteLine ("{0} ({2:p}) -- {1}", d.TypeData [ty], GetTypeName (ty), (double) d.TypeData [ty] / (double) d.TotalSize);
			}
			
			Console.WriteLine ();
		}
	}
	
	public void Process ()
	{
		int cutoff = (int) (MaxSize * Threshold);
		
		TotalTypeSizes = new long [TypeTableSize];
		IsSizeLongEnough = new bool [TypeTableSize];
		
		foreach (TimeData d in Data) {
			for (int i = 0; i < d.TypeData.Length; i ++) {
				TotalTypeSizes [i] += d.TypeData [i];
				
				if (d.TypeData [i] > cutoff)
					IsSizeLongEnough [i] = true;
			}
		}
		
		foreach (TimeData d in Data) {
			for (int i = 0; i < d.TypeData.Length; i ++) {
				if (! IsSizeLongEnough [i]) {
					d.OtherSize += d.TypeData [i];
					d.TypeData [i] = 0;
				}
			}
		}
	}
}