using System;
using System.Collections;
using Gtk;

class ShellPager : Notebook {
	public readonly Shell Parent;
	
	public ShellPager (Shell p)
	{
		Parent = p;
	}
}