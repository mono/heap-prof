using System;
using System.Collections;

class AllocNode : IComparable {
	public int n_allocs;
	public int n_bytes;
	
	public ArrayList Children;
	public AllocNode Parent;
	
	public int type;
	public int [] bt;
	public int bt_len;
		
	public BacktraceTabulator tab;
	
	public AllocNode () {}
	
	public AllocNode (int t, int [] bt, int bt_len, BacktraceTabulator tab)
	{
		this.type = t;
		this.bt = bt;
		this.bt_len = bt_len;
		this.tab = tab;
		
		tab.nodes.Add (this, this);
		
		if (bt_len != 0) {
			Parent = tab.LookupNode (t, bt, bt_len - 1);
			if (Parent.Children == null)
				Parent.Children = new ArrayList ();
			
			Parent.Children.Add (this);
		} else {
			tab.type_nodes.Add (this);
		}
	}
	
	public void RecordAlloc (int c, int b)
	{
		for (AllocNode n = this; n != null; n = n.Parent) {
			n.n_allocs += c;
			n.n_bytes += b;
		}
	}
	
	public override bool Equals (object o)
	{
		AllocNode a = o as AllocNode;
		if (a == null)
			return false;
		
		if (a.type != type || a.bt_len != bt_len)
			return false;
		
		for (int i = 0; i < bt_len; i ++)
			if (a.bt [i] != bt [i])
				return false;
			
		return true;
	}
	
	public override int GetHashCode ()
	{
		int h = type ^ bt_len;
		
		for (int i = 0; i < bt_len; i ++) {
			h ^= bt [i];
			h *= 31;
		}
		
		return h;
	}
	
	public int CompareTo (object o)
	{
		int nb = ((AllocNode) o).n_bytes;
		
		return  nb - n_bytes;
	}
}

class BacktraceTabulator {
	public Hashtable nodes;
	public Profile p;
		
	public ArrayList type_nodes;
	
	public int total_size;
		
	public BacktraceTabulator (Profile p, int [] context_data)
	{
		this.p = p;
		nodes = new Hashtable ();
		type_nodes = new ArrayList ();
		
		for (int i = 0; i < context_data.Length; i ++) {
			
			if (context_data [i] == 0)
				continue;
			
			Context c = p.GetContext (i);
			int [] bt = p.GetBacktrace (c.Backtrace);
			LookupNode (c.Type, bt, bt.Length).RecordAlloc (context_data [i], context_data [i] * c.Size);
			
			total_size += total_size;
		}
		
		SortRecursive (type_nodes);
	}
	
	static void SortRecursive (ArrayList ar)
	{
		if (ar == null)
			return;
		
		ar.Sort ();
		
		foreach (AllocNode an in ar)
			SortRecursive (an.Children);
	}
	
	AllocNode temp_node = new AllocNode ();
	public AllocNode LookupNode (int t, int [] bt, int bt_len)
	{
		temp_node.type = t;
		temp_node.bt = bt;
		temp_node.bt_len = bt_len;
		
		AllocNode ret = nodes [temp_node] as AllocNode;
		
		if (ret != null)
			return ret;
		
		return new AllocNode (t, bt, bt_len, this);
	}
	
	public void Dump ()
	{
		foreach (AllocNode an in type_nodes) {
			
			if (an.n_bytes < total_size * .15)
				continue;
			
			Console.WriteLine ("{0} -- {1} bytes, {2} objects", p.GetTypeName (an.type), an.n_bytes, an.n_allocs);
			
			WriteAllocSitesRecursive (an.Children, "\t");
		}
	}
	
	public void WriteAllocSitesRecursive (ArrayList ar, string pre)
	{
		if (ar == null)
			return;
		
		foreach (AllocNode an in ar) {
			
			
			Console.WriteLine (pre + "{0} -- {1} bytes, {2} objects", p.GetMethodName (an.bt [an.bt_len - 1]), an.n_bytes, an.n_allocs);
			WriteAllocSitesRecursive (an.Children, pre + "\t");
		}
	}
}