using System;
using System.Collections;
using Gtk;

class BacktraceViewerWindow : Window {
	TimeData data;
	TypeTabulator t;
	BacktraceTabulator bt;
	
	BacktraceNodeStore ns;
	NodeView nv;
	
	VBox box;
	
	
	
	public BacktraceViewerWindow (TimeData data, TypeTabulator t) : base ("")
	{
		this.data = data;
		this.t = t;
		this.bt = new BacktraceTabulator (t, data.ContextData);
		
		box = new VBox ();
		box.Spacing = 12;
		
		this.Add (box);
		
		box.PackStart (CreateHeader (), false, false, 0);
		
		ns = new BacktraceNodeStore (data, t, bt);
		
		ScrolledWindow sw = new ScrolledWindow ();
		sw.Add (ns.GetNodeView ());
		box.PackStart (sw, true, true, 0);
		
		Title = string.Format ("Heap at {0} ms", data.Time);
	}
	
	Widget CreateHeader ()
	{
		VBox vb = new VBox ();
		vb.Spacing = 12;
		vb.BorderWidth = 12;
		
		Label l = new Label (string.Format ("<b>Heap at {0} ms</b>", data.Time));
		l.Xalign = 0;
		
		l.UseMarkup = true;
		
		vb.PackStart (l, false, false, 0);
		
		HBox hb = new HBox ();
		hb.Spacing = 12;
		l = new Label ("Heap Size:");
		l.Xalign = 0;
		l.Xpad = 12;
		
		hb.PackStart (l, false, false, 0);
		
		l = new Label (FormatHelper.BytesToString (data.TotalSize));
		l.Xalign = 0;
		hb.PackStart (l, false, false, 0);
		
		vb.PackStart (hb, false, false, 0);
		
		return vb;
	}
}

class BacktraceNodeStore : NodeStore {
	TimeData data;
	TypeTabulator t;
	BacktraceTabulator bt;
	
	public BacktraceNodeStore (TimeData data, TypeTabulator t, BacktraceTabulator bt) : base (typeof (BacktraceNode))
	{
		this.data = data;
		this.t = t;
		this.bt = bt;
		
		foreach (AllocNode an in bt.type_nodes) {
			BacktraceNode n = new BacktraceNode (data, t, an);
			ProcessNode (n);
			AddNode (n);
		}
	}
	
	void ProcessNode (BacktraceNode n)
	{
		AllocNode p = n.an;
		
		if (p.Children == null)
			return;
		
		foreach (AllocNode an in p.Children) {
			BacktraceNode nn = new BacktraceNode (data, t, an);
			ProcessNode (nn);
			n.AddChild (nn);
		}
	}
	
	public NodeView GetNodeView ()
	{
		NodeView nv = new NodeView (this);
		nv.HeadersVisible = false;
		nv.AppendColumn ("Size",        new CellRendererText (), new NodeCellDataFunc (GetNumBytes));
		nv.AppendColumn ("Num objects", new CellRendererText (), new NodeCellDataFunc (GetNumObjects));
		nv.AppendColumn ("Percent",     new CellRendererText (), new NodeCellDataFunc (GetPercent));
		
		nv.AppendColumn ("Source",      new CellRendererText (), "text", 0);

		
		return nv;
	}
	
	private void GetNumBytes (TreeViewColumn col, CellRenderer cell, ITreeNode node)
	{
		CellRendererText c = (CellRendererText) cell;
		BacktraceNode n = (BacktraceNode) node;
		

		
		c.Text = FormatHelper.BytesToString (n.an.n_bytes);
	}
	
	private void GetPercent (TreeViewColumn col, CellRenderer cell, ITreeNode node)
	{
		CellRendererText c = (CellRendererText) cell;
		BacktraceNode n = (BacktraceNode) node;
		
		c.Text = String.Format ("{0:p}", (double) n.an.n_bytes / (double) n.data.TotalSize);
	}
	
	private void GetNumObjects (TreeViewColumn col, CellRenderer cell, ITreeNode node)
	{
		CellRendererText c = (CellRendererText) cell;
		BacktraceNode n = (BacktraceNode) node;
		c.Text = n.an.n_allocs.ToString ();
	}
	

}

[TreeNode (ColumnCount = 1)]
class BacktraceNode : TreeNode {
	public TimeData data;
	TypeTabulator t;
	public AllocNode an;

	public BacktraceNode (TimeData data, TypeTabulator t, AllocNode an)
	{
		this.data = data;
		this.t = t;
		this.an = an;
	}
	
	[TreeNodeValue (Column = 0)]
	public string Name {
		get {
			if (an.bt_len == 0)
				return t.GetTypeName (an.type);
			else
				return t.GetMethodName (an.bt [an.bt_len - 1]);
		}
	}
}

class FormatHelper {
	public static string BytesToString (int cb)
	{
		const int K = 1024;
		const int M = 1024 * K;
		
		if (cb > M)
			return String.Format ("{0:0.0} MB", (double) cb / (double) M);
		else if (cb > K)
			return String.Format ("{0:0.0} KB", (double) cb / (double) K);
		else
			return String.Format ("{0} bytes", cb);
	}
}