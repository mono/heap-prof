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
		int ev = Metadata.GetTimelineBefore (EventType.Checkpoint, max_t);
		ContextDataTabulator tab;
		
		if (ev != -1)
			tab = new ContextDataTabulator (this, Metadata.GetTimeline (ev).FilePos, max_t);
		else
			tab = new ContextDataTabulator (this, 0, max_t);
		
		tab.Read ();
		
		return tab.ContextData;
		
	}
	
	class ContextDataTabulator : ProfileReader {
		public int [] ContextData;
		public ContextDataTabulator (Profile p, long s, int e) : base (p, s, e)
		{
			if (s == 0)
				ContextData = new int [ContextTableSize];
		}
			
		protected override void Checkpoint (int time, int event_num)
		{
			if (ContextData != null)
				base.Checkpoint (time, event_num);
			
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
}