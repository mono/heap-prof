using System;
using System.IO;

public class Profile {
	
	public string Filename;
	public Metadata Metadata;
		
	public Profile (string fn)
	{
		Filename = fn;
	}
	
	public void ReadMetadata ()
	{
		if (Metadata != null)
			return;
		
		Metadata = new Metadata (this);
	}
	
	public int [] GetContextObjsForTime (int max_t)
	{
		ContextDataTabulator tab = new ContextDataTabulator (this, max_t);
		tab.Read ();
		return tab.ContextData;
		
	}
	
	class ContextDataTabulator : ProfileReader {
		public int [] ContextData;
		public ContextDataTabulator (Profile p, int s) : base (p, s, s)
		{
			if (p.Metadata.GetTimelineBefore (EventType.Checkpoint, s) == -1)
				ContextData = new int [ContextTableSize];
		}
			
		protected override void Checkpoint (int time, int event_num)
		{
			if (ContextData != null) {
				base.Checkpoint (time, event_num);
				return;
			}
			int [] dummy;
			
			ReadCheckpoint (out dummy, out ContextData);
		}
		
		protected override void AllocationSeen (int time, Context ctx, long pos)
		{
			ContextData [ctx.Id] ++;
		}
		
		protected override void GcSeen (int time, int gc_num)
		{
			ReadGcFreed ();
		}
		
		protected override void GcFreedSeen (int time, Context ctx, long pos)
		{
			ContextData [ctx.Id] --;
		}
	}
	
	public string GetTypeName (int idx)
	{
		return Metadata.GetTypeName (idx);
	}
	
	public string GetMethodName (int idx)
	{
		return Metadata.GetMethodName (idx);
	}
	
	public int [] GetBacktrace (int idx)
	{
		return Metadata.GetBacktrace (idx);
	}
	
	public Context GetContext (int idx)
	{
		return Metadata.GetContext (idx);
	}
	
	public int TypeTableSize {
		get { return Metadata.TypeTableSize; }
	}
	
	public int ContextTableSize {
		get { return Metadata.ContextTableSize; }
	}
	
	public Timeline [] Timeline {
		get { return Metadata.Timeline; }
	}
	
	int max_size = -1;
	public int MaxSize {
		get {
			
			if (max_size != -1)
				return max_size;
			
			Timeline [] tl = Timeline;
			
			for (int i = tl.Length - 1; i >= 0; i --)
				if (tl [i].Event == EventType.HeapResize)
					return max_size = tl [i].SizeHigh;
				
			return max_size = 0;
		}
	}
}
