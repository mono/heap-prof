CLEANFILES = mono-heap-prof-view.exe mono-heap-prof-view

mono_heap_prof_view_SCRIPTS = mono-heap-prof-view.exe
mono_heap_prof_view_DATA = mono-heap-prof-view.exe.config
mono_heap_prof_viewdir = $(prefix)/lib/mono-heap-prof

common_sources = \
	$(srcdir)/common/TypeTabulator.cs \
	$(srcdir)/common/ProfileReader.cs \
	$(srcdir)/common/BacktraceTabulator.cs \
	$(srcdir)/common/TypeGraphPlotter.cs

gtk_sources = \
	$(srcdir)/gui-gtk/TypeGraphViewer.cs \
	$(srcdir)/gui-gtk/BacktraceViewer.cs



mono_heap_prof_view_sources = $(common_sources) $(gtk_sources)

mono-heap-prof-view.exe : $(mono_heap_prof_view_sources)
	$(MCS) /out:$@ $(mono_heap_prof_view_sources) /r:System.Drawing.dll @GTKSHARP_LIBS@ 

scriptdir = $(bindir)

script_SCRIPTS = mono-heap-prof-view