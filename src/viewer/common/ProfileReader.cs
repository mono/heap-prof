using System;
using System.IO;

public abstract class ProfileReader {
	
	public Profile Profile;
	BinaryReader br;
	int end_t = int.MaxValue;
	long startpos;
	
	public ProfileReader (Profile p)
	{
		Profile = p;
	}
	
	public ProfileReader (Profile p, int start_t, int end_t)
	{
		
		int ev = p.Metadata.GetTimelineBefore (EventType.Checkpoint, start_t);
		
		if (ev != -1)
			startpos = p.Metadata.GetTimeline (ev).FilePos;
		else
			startpos = 0;
		
		this.end_t = end_t;
		Profile = p;
	}
	
	int context_size;
	int type_size;
	
	public void Read ()
	{
		using (br = new BinaryReader (File.OpenRead (Profile.Filename))) {
			
			ProfilerSignature.ReadHeader (br, true);
			
			if (startpos != 0)
				br.BaseStream.Seek (startpos, SeekOrigin.Begin);
		
			while (true) {
				int time = br.ReadInt32 ();
				
				// eof
				if (time == -1)
					return;
				
				bool is_alloc = (time & (int)(1 << 31)) == 0;
				time &= int.MaxValue;
				
				// we are done
				if (time > end_t)
					return;
				
				if (is_alloc) {
					// allocation
					int ctx = br.ReadInt32 ();
					
					AllocationSeen (time, GetContext (ctx), br.BaseStream.Position);
					
				} else {
					
					
					EventType event_type = (EventType) br.ReadInt32 ();
					int event_num = br.ReadInt32 ();
					
					switch (event_type) {
					case EventType.GC:
						GcSeen (time, event_num);
						break;
					case EventType.HeapResize:
						GcHeapResize (time, event_num, br.ReadInt32 ());
						break;
					case EventType.Checkpoint:
						context_size = br.ReadInt32 ();
						type_size = br.ReadInt32 ();
						
						Checkpoint (time, event_num);

						break;
					}						
				}
					
			}
		}
	}
	
	protected void ReadGcFreed ()
	{
		while (true) {
			long pos = br.ReadInt64 ();
			int alloc_time = br.ReadInt32 ();
			int alloc_ctx = br.ReadInt32 ();
			
			if (pos == 0 && alloc_time == 0 && alloc_ctx == 0)
				return;
			
			GcFreedSeen (alloc_time, GetContext (alloc_ctx), pos);
		}
	}
	
	protected abstract void AllocationSeen (int time, Context ctx, long pos);
	protected abstract void GcSeen (int time, int event_num);
	protected abstract void GcFreedSeen (int time, Context ctx, long pos);
		
	protected virtual void GcHeapResize (int time, int event_num, int new_size)
	{
	}
	
	
	protected void ReadCheckpoint (out int [] type_data, out int [] ctx_insts)
	{
		ctx_insts = new int [ContextTableSize];
		type_data = new int [TypeTableSize];
		
		for (int i = 0; i < context_size; i ++)
			ctx_insts [i] = br.ReadInt32 ();
		
		
		for (int i = 0; i < type_size; i ++)
			type_data [i] = br.ReadInt32 ();
	}
		
	protected virtual void Checkpoint (int time, int event_num)
	{
		int cb = (context_size + type_size) * 4;
		br.BaseStream.Seek (cb, SeekOrigin.Current);
	}
	
	
	public string GetTypeName (int idx)
	{
		return Profile.GetTypeName (idx);
	}
	
	public string GetMethodName (int idx)
	{
		return Profile.GetMethodName (idx);
	}
	
	public int [] GetBacktrace (int idx)
	{
		return Profile.GetBacktrace (idx);
	}
	
	public Context GetContext (int idx)
	{
		return Profile.GetContext (idx);
	}
	
	public int TypeTableSize {
		get { return Profile.TypeTableSize; }
	}
	
	public int ContextTableSize {
		get { return Profile.ContextTableSize; }
	}
	
	public Timeline [] Timeline {
		get { return Profile.Timeline; }
	}
}

public class Metadata {
	
	const int BacktraceSize = 5;

	
	string [] typeTable;
	string [] methodTable;
	int [][] backtraceTable;
	Context [] contextTable;
	Timeline [] timeline;
	long [] type_total_allocs;
	
	public int TypeTableSize { get { return typeTable.Length; } }
	public int ContextTableSize { get { return contextTable.Length; } }
	public Timeline [] Timeline { get { return timeline; } }
	public long [] TypeTotalAllocs { get { return type_total_allocs; } }
	
	public string GetTypeName (int idx)
	{
		return typeTable [idx];
	}
	
	public string GetMethodName (int idx)
	{
		return methodTable [idx];
	}
	
	public int [] GetBacktrace (int idx)
	{
		return backtraceTable [idx];
	}
	
	public Context GetContext (int idx)
	{
		return contextTable [idx];
	}
	
	public int GetTimelineBefore (EventType e, int time)
	{
		int last = -1;
		for (int i = 0; i < timeline.Length; i ++) {
			if (timeline [i].Time >= time)
				break;
			
			if (timeline [i].Event == e)
				last = i;
		}
		
		return last;
	}
	
	public Timeline GetTimeline (int idx)
	{
		return timeline [idx];
	}
	
	
	public Metadata (Profile p)
	{
		using (BinaryReader br = new BinaryReader (File.OpenRead (p.Filename))) {
			
			br.BaseStream.Seek (-8, SeekOrigin.End);
			br.BaseStream.Seek (br.ReadInt64 (), SeekOrigin.Begin);
			
			ProfilerSignature.ReadHeader (br, false);
			
			typeTable = ReadStringTable (br);
			methodTable = ReadStringTable (br);
			backtraceTable = ReadBacktraceTable (br);
			contextTable = ReadContextTable (br);
			timeline = ReadTimeline (br);
			type_total_allocs = ReadTypeTotalAllocationsTable (br);
		}
	}
	
	public void Dump ()
	{		
		foreach (Context c in contextTable) {
			Console.WriteLine ("size {0}, type {1}", c.Size, typeTable [c.Type]);
			foreach (int i in backtraceTable [c.Backtrace])
				Console.WriteLine ("  {0}  {1}", i, methodTable [i]);
			Console.WriteLine ();
		}
	}
	
	long [] ReadTypeTotalAllocationsTable (BinaryReader br)
	{
		int sz = br.ReadInt32 ();
		
		long [] ret = new long [sz];
		
		for (int i = 0; i < sz; i ++)
			ret [i] = br.ReadInt64 ();
		
		return ret;
	}
	

	string [] ReadStringTable (BinaryReader br)
	{
		int sz = br.ReadInt32 ();
		
		string [] ret = new string [sz];
		
		for (int i = 0; i < sz; i ++)
			ret [i] = br.ReadString ();
		
		return ret;
	}
	
	int [] [] ReadBacktraceTable (BinaryReader br)
	{
		int sz = br.ReadInt32 ();
		
		int [][] t = new int [sz] [];
		
		for (int i = 0; i < sz; i ++) {
			int szz = br.ReadInt32 ();
			
			t [i] = new int [szz];
			for (int j = 0; j < BacktraceSize; j ++) {
				int n = br.ReadInt32 ();
				if (j < szz)
					t [i] [j] = n;
			}
		}
		return t;
	}
	
	Context [] ReadContextTable (BinaryReader br)
	{
		
		int sz = br.ReadInt32 ();
		Context [] d = new Context [sz];
		
		for (int i = 0; i < sz; i ++) {
			d [i].Id = i;
			d [i].Type = br.ReadInt32 ();
			d [i].Size = br.ReadInt32 ();
			d [i].Backtrace = br.ReadInt32 ();
		}
		
		return d;
	}
	
	Timeline [] ReadTimeline (BinaryReader br)
	{
		
		int sz = br.ReadInt32 ();
		Timeline [] d = new Timeline [sz];
		
		for (int i = 0; i < sz; i ++) {
			d [i].Id = i;
			d [i].Time = br.ReadInt32 ();
			d [i].Event = (EventType) br.ReadInt32 ();
			d [i].SizeHigh = br.ReadInt32 ();
			d [i].SizeLow = br.ReadInt32 ();
			d [i].FilePos = br.ReadInt64 ();
		}
		
		return d;
	}
}

class ProfilerSignature {
	static readonly byte [] DumpSignature = {
		0x68, 0x30, 0xa4, 0x57, 0x18, 0xec, 0xd6, 0xa1,
		0x61, 0x9c, 0x1d, 0x43, 0xe1, 0x47, 0x27, 0xb6
	};
	
	static readonly byte [] MetaSignature = {
		0xe4, 0x37, 0x29, 0x60, 0x3e, 0x31, 0x89, 0x12, 
		0xaa, 0x93, 0xc8, 0x76, 0xf4, 0x6a, 0x95, 0x11
	};
	
	const int Version = 6;
	
	public static void ReadHeader (BinaryReader br, bool is_dump)
	{
		byte [] s = is_dump ? DumpSignature : MetaSignature;
		byte [] sig = br.ReadBytes (s.Length);
		
		for (int i = 0; i < s.Length; i ++) {
			if (sig [i] != s [i])
				throw new Exception ("Invalid file format");
		}
		
		int ver = br.ReadInt32 ();
		if (ver != Version)
			throw new Exception (String.Format ("Wrong version: expected {0}, got {1}", Version, ver));
	}
	
}

public enum EventType {
	GC = 0,
	HeapResize = 1,
	Checkpoint = 2
}

public struct Timeline {
	public int Id;
	public int Time;
	public EventType Event;
	public int SizeHigh, SizeLow;
	public long FilePos;
}

public struct Context {
	public int Id;
	public int Type;
	public int Size;
	public int Backtrace;
}