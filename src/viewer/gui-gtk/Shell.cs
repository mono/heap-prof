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
		s.ShowAll ();
		s.Open (args [0]);
		Application.Run ();
	}
	
	public Shell () : base ("Bllah")
	{
		
		entries = new ActionEntry[] {
			new ActionEntry ("FileMenu", null, "_File", null, null, null),
			new ActionEntry ("OpenAction", Stock.Open, null, "<control>O", "Open a profile...", new EventHandler (OnOpen)),
			new ActionEntry ("QuitAction", Stock.Quit, null, "<control>Q", "Quit the application", delegate { Application.Quit (); }),
		};

		DefaultSize = new Gdk.Size (200, 150);
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
		
		sb = new Statusbar ();
		main_box.PackEnd (sb, false, true, 0);

		pager = new ShellPager (this);
		main_box.PackEnd (pager, true, true, 0);
	}

	public void Open (string fn)
	{
		int b = Environment.TickCount;
		TypeTabulator t = new TypeTabulator (fn);
		t.Read ();
		t.Process ();
		//t.Dump ();
		
		Console.WriteLine (Environment.TickCount - b);

		Add (new TypeGraphComponent (t));
	}
	
	void OnOpen (object obj, EventArgs args)
	{
		Console.WriteLine ("open");
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
		pager.CurrentPage = pos;
		pager.ShowAll ();
	}
	
	public void TitleChanged (ShellComponent sc)
	{
		((Label) pager.GetTabLabel (sc)).Text = sc.Title;
	}
}
