using System;
using System.Collections;
using Gtk;


class Shell : Window {
	VBox main_box;
	Statusbar sb;
	
	ActionGroup shell_commands;
	UIManager uim = null;

	ActionEntry[] entries;
	
	ShellPager pager;
	
	public static void Main (string[] args)
	{
		Shell s;
		Application.Init ();
		s = new Shell ();
		
		if (args.Length == 1)
			s.Open (args [0]);
		s.ShowAll ();
		Application.Run ();
	}
	
	public Shell () : base ("Bllah")
	{
		
		entries = new ActionEntry[] {
			new ActionEntry ("FileMenu", null, "_File", null, null, null),
			new ActionEntry ("OpenAction", Stock.Open, null, "<control>O", "Open a profile...", new EventHandler (OnOpen)),
			new ActionEntry ("QuitAction", Stock.Quit, null, "<control>Q", "Quit the application", delegate { Application.Quit (); }),
		};

		DefaultSize = new Gdk.Size (700, 700);
		DeleteEvent += delegate { Application.Quit (); };
		
		main_box = new VBox (false, 0);
		Add (main_box);
		
		shell_commands = new ActionGroup ("TestActions");
		shell_commands.Add (entries);
		
		uim = new UIManager ();
		uim.AddWidget += delegate (object obj, AddWidgetArgs args) {
			args.Widget.Show ();
			main_box.PackStart (args.Widget, false, true, 0);
		};
		
		
		uim.ConnectProxy += OnProxyConnect;
		uim.InsertActionGroup (shell_commands, 0);
		uim.AddUiFromResource ("shell-ui.xml");
		AddAccelGroup (uim.AccelGroup);
		
		sb = new Statusbar ();
		main_box.PackEnd (sb, false, true, 0);

		pager = new ShellPager (this);
		main_box.PackEnd (pager, true, true, 0);
	}

	public void Open (string fn)
	{
		Profile p = new Profile (fn);
		p.ReadMetadata ();

		Add (new TypeGraphComponent (p));
	}
	
	void OnOpen (object obj, EventArgs args)
	{
		string s = null;
		
		using (FileChooserDialog fd = new FileChooserDialog ("Select a profile", this, FileChooserAction.Open)) {
			fd.AddButton (Gtk.Stock.Cancel, Gtk.ResponseType.Cancel);
			fd.AddButton (Gtk.Stock.Open, Gtk.ResponseType.Ok);
			
			FileFilter filter_all = new FileFilter ();
			filter_all.AddPattern ("*");
			filter_all.Name = "All Files";
			
			FileFilter filter_prof = new FileFilter ();
			filter_prof.AddMimeType ("application/x-mono-heap-prof");
			filter_prof.Name = "Mono Heap Profiles";
			
			fd.AddFilter (filter_all);
			fd.AddFilter (filter_prof);
			fd.Filter = filter_prof;
			
			if (fd.Run () == (int) ResponseType.Ok)
				s = fd.Filename;
			
			fd.Destroy ();
		}
		
		if (s != null)
			Open (s);
	}
	
	void OnProxyConnect (object obj, ConnectProxyArgs args)
	{
		if (args.Proxy is MenuItem) {
			Item itm = (Item) args.Proxy;
			Action a = args.Action;
			
			itm.Selected += delegate {
				if (a.Tooltip != null)
					sb.Push (0, a.Tooltip);
			};
			
			itm.Deselected += delegate {
				sb.Pop (0);
			};
		}
	}
	
	public void Add (ShellComponent sc)
	{
		sc.parent = this;
		int pos = pager.AppendPage (sc, new Label (""));
		TitleChanged (sc);
		
		pager.ShowAll ();
		pager.CurrentPage = pos;
	}
	
	public void TitleChanged (ShellComponent sc)
	{
		((Label) pager.GetTabLabel (sc)).Text = sc.Title;
	}
}
