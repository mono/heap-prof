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
}