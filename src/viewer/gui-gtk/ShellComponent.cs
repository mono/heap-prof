using System;
using System.Collections;
using Gtk;

class ShellComponent : Frame {
	
	public ShellComponent ()
	{
		ShadowType = ShadowType.None;
	}
	
	internal Shell parent;
	
	string title;
	public string Title {
		
		get {
			return title;
		}
		
		set {
			if (parent != null)
				parent.TitleChanged (this);
			
			title = value;
		}
	}
	
	public Shell Parent {
		get {
			return parent;
		}
	}
}
