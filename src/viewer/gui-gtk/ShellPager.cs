using System;
using System.Collections;
using Gtk;

class ShellPager : Notebook {
	public readonly Shell Parent;
	
	public ShellPager (Shell p)
	{
		Parent = p;
	}
	
	public int AppendPage (ShellComponent sc)
	{
		TabLabel l = new TabLabel ("");
		int pos = AppendPage (sc, l);
		
		// Workaround for #72475
		ShellPager _this = this;
		ShellComponent _sc = sc;
		l.Button.Clicked += delegate {
			_sc.HideAll ();
			_this.RemovePage (pos);
			_sc.Dispose ();
		};
		
		return pos;
	}
	
	
	
	public void TitleChanged (ShellComponent sc)
	{
		((TabLabel) GetTabLabel (sc)).Label.Text = sc.Title;
	}
	
	class TabLabel : HBox
	{
		private Label title;
		private Button btn;
		
		public TabLabel (string label) : base (false, 2)
		{
			title = new Label (label);
			
			this.PackStart (title, true, true, 0);
			
			btn = new Button ();
			btn.Add (new Gtk.Image (Stock.Close, IconSize.Menu));
			btn.Relief = ReliefStyle.None;
			btn.SetSizeRequest (18, 18);
			this.PackStart (btn, false, false, 2);
			this.ClearFlag (WidgetFlags.CanFocus);

			this.ShowAll ();
			

		}
		
		public Label Label
		{
			get { return title; }
			set { title = value; }
		}
		
		public Button Button
		{
			get { return btn; }
		}
	}
}