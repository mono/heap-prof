using System;
using System.IO;



public abstract class ProfileReader {
	
	Metadata mr;
	BinaryReader br;
	string name;
	
	public ProfileReader (string name)
	{
		this.name = name;
		mr = new Metadata (name);
		//mr.Dump ();
	}
	
	public void Read ()
	{
		using (br = new BinaryReader (File.OpenRead (name))) {
			ProfilerSignature.ReadHeader (br, true);
			
			while (true) {
				int time = br.ReadInt32 ();
				
				// end of file
				if (time == -1)
					return;
				
				if ((time & (int)(1 << 31)) == 0) {
					// allocation
					int ctx = br.ReadInt32 ();
					
					AllocationSeen (time, GetContext (ctx), br.BaseStream.Position);
					
				} else {
					time &= int.MaxValue;
					
					int gc_num = br.ReadInt32 ();
					
					GcSeen (time, gc_num);
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
	protected abstract void GcSeen (int time, int gc_num);
	protected abstract void GcFreedSeen (int time, Context ctx, long pos);
	
	public string GetTypeName (int idx)
	{
		return mr.GetTypeName (idx);
	}
	
	public string GetMethodName (int idx)
	{
		return mr.GetMethodName (idx);
	}
	
	public int [] GetBacktrace (int idx)
	{
		return mr.GetBacktrace (idx);
	}
	
	public Context GetContext (int idx)
	{
		return mr.GetContext (idx);
	}
	
	
	public int TypeTableSize { get { return mr.TypeTableSize; } }
	public int ContextTableSize { get { return mr.ContextTableSize; } }

	
}

public class Metadata {
	
	const int BacktraceSize = 5;

	
	string [] typeTable;
	string [] methodTable;
	int [][] backtraceTable;
	Context [] contextTable;
	
	public int TypeTableSize { get { return typeTable.Length; } }
	public int ContextTableSize { get { return contextTable.Length; } }
	
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
	
	
	public Metadata (string name)
	{
		using (BinaryReader br = new BinaryReader (File.OpenRead (name))) {
			
			br.BaseStream.Seek (-8, SeekOrigin.End);
			br.BaseStream.Seek (br.ReadInt64 (), SeekOrigin.Begin);
			
			ProfilerSignature.ReadHeader (br, false);
			
			typeTable = ReadStringTable (br);
			methodTable = ReadStringTable (br);
			backtraceTable = ReadBacktraceTable (br);
			contextTable = ReadContextTable (br);
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
	
	const int Version = 2;
	
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

public struct Context {
	public int Id;
	public int Type;
	public int Size;
	public int Backtrace;
}