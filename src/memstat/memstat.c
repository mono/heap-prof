#include <string.h>
#include <stdio.h>
#include <unistd.h>
#include <glib.h>
#include <pthread.h>
#include <malloc.h>
#include <mono/metadata/profiler.h>

#define KB (1024)
#define MB (KB*1024)

struct _MonoProfiler {
};

static int gc_heap_size = 0;

static void
size_to_units (int size, double* out_size, const char** out_units)
{
	if (size < KB) {
		*out_size = size;
		*out_units = "B";
	} else if (size < MB) {
		*out_size = (double) size / (double) KB;
		*out_units = "KB";
	} else {
		*out_size = (double) size / (double) MB;
		*out_units = "MB";
	}
}

void worker (gpointer dummy)
{
	int i = 0;
	while (TRUE) {
		struct mallinfo stats = mallinfo ();
			
		int gc_live, gc_arena;
		
		double live_size;
		const char* live_units;
		double arena_size;
		const char* arena_units;
		double mmaparena_size;
		const char* mmaparena_units;
		double gc_live_size;
		const char* gc_live_units;
		double gc_arena_size;
		const char* gc_arena_units;
		double total_live_size;
		const char* total_live_units;
		double total_arena_size;
		const char* total_arena_units;
		
		gc_live = mono_gc_get_total_bytes ();
		gc_arena = gc_heap_size;
		
		size_to_units (stats.uordblks + stats.hblkhd, &live_size, &live_units);
		size_to_units (stats.arena, &arena_size, &arena_units);
		size_to_units (stats.hblkhd, &mmaparena_size, &mmaparena_units);
		size_to_units (gc_live, &gc_live_size, &gc_live_units);
		size_to_units (gc_arena, &gc_arena_size, &gc_arena_units);
		size_to_units (stats.uordblks + stats.hblkhd + gc_live, &total_live_size, &total_live_units);
		size_to_units (stats.arena + stats.hblkhd + gc_arena, &total_arena_size, &total_arena_units);
		
		if (i++ % 10 == 0) {
			
			printf ("%s|%s|%s\n", "----------------malloc----------------", "------------gc------------", "---------total---------");
			printf ("%11s %11s %11s   |%11s %11s   |%11s %11s\n", "live", "arena", "mmaparena", "live", "arena", "live", "arena");
		}
		
		printf ("  %6.1f %2s   %6.1f %2s   %6.1f %2s   |  %6.1f %2s   %6.1f %2s   |  %6.1f %2s   %6.1f %2s\n",
		
		live_size, live_units, arena_size, arena_units, mmaparena_size, mmaparena_units, gc_live_size, gc_live_units, gc_arena_size, gc_arena_units,
		total_live_size, total_live_units, total_arena_size, total_arena_units);
		sleep (1);
	}
}

static void
prof_heap_resize (MonoProfiler *p, guint64 new_size)
{
	gc_heap_size = (guint32) new_size;
}

void
mono_profiler_startup (const char *desc)
{
	MonoProfiler* p = g_new0 (MonoProfiler, 1);
	pthread_t tid;
	pthread_create(& tid, NULL, (void *) worker, NULL);
	
	mono_profiler_install_gc (NULL, prof_heap_resize);
	mono_profiler_set_events (MONO_PROFILE_GC);
	
	mono_profiler_install (p, NULL);
}
