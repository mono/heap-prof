
if ENABLE_XDGMIME

xdgmimedir = $(prefix)/share/mime/packages
xdgmime_DATA = mono-heap-prof.xml

install-data-hook:
	$(UPDATE_MIME_DATABASE) $(prefix)/share/mime


uninstall-hook:
	$(UPDATE_MIME_DATABASE) $(prefix)/share/mime

endif
