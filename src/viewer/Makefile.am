CLEANFILES = mono-heap-prof-view.exe mono-heap-prof-view

mono_heap_prof_view_SCRIPTS = mono-heap-prof-view.exe
mono_heap_prof_viewdir = $(prefix)/lib/mono-heap-prof

common_sources = \
	$(srcdir)/common/TypeTabulator.cs \
	$(srcdir)/common/ProfileReader.cs \
	$(srcdir)/common/BacktraceTabulator.cs \
	$(srcdir)/common/TypeGraphPlotter.cs \
	$(srcdir)/common/Profile.cs

gtk_sources = \
	$(srcdir)/gui-gtk/TypeGraphViewer.cs \
	$(srcdir)/gui-gtk/BacktraceViewer.cs \
	$(srcdir)/gui-gtk/Shell.cs \
	$(srcdir)/gui-gtk/ShellComponent.cs \
	$(srcdir)/gui-gtk/ShellPager.cs

gtk_resources = \
	$(srcdir)/gui-gtk/shell-ui.xml
	
gtk_ress= $(foreach res,$(gtk_resources), $(addprefix -resource:,$(res)),$(notdir $(res)))


mono_heap_prof_view_sources = $(common_sources) $(gtk_sources)

mono-heap-prof-view.exe : $(mono_heap_prof_view_sources) $(gtk_resources)
	$(MCS) /out:$@ $(mono_heap_prof_view_sources) $(gtk_ress) /r:System.Drawing.dll @GTKSHARP_LIBS@ 

scriptdir = $(bindir)

script_SCRIPTS = mono-heap-prof-view