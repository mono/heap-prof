/*
 * gc-profiler.c - A heap profiler
 *
 * Author:
 *     Ben Maurer <bmaurer@ximian.com>
 *
 */
#include <string.h>
#include <glib.h>
#include <mono/io-layer/io-layer.h>
#include <mono/metadata/class.h>
#include <mono/metadata/debug-helpers.h>
#include <mono/metadata/object.h>
#include <mono/metadata/profiler.h>
#include <mono/metadata/mono-gc.h>
#include <unistd.h>

typedef struct AllocRec AllocRec;
struct AllocRec {
	AllocRec* next;
	MonoObject* obj;
	int tidx;
	int size;
};

struct _MonoProfiler {
	
	GPtrArray* klass_table;
	GHashTable* klass_to_table_idx;
	
	AllocRec* live_allocs;
	guint32 t_zero;

	
	int type_live_data_size;
	int* type_live_data;
	
	int total_live_bytes;
	
	guint64 last_checkpoint;
};



static CRITICAL_SECTION hp_lock;
#define hp_lock_enter() EnterCriticalSection (&hp_lock)
#define hp_lock_leave() LeaveCriticalSection (&hp_lock)

static guint32
get_delta_t (MonoProfiler *p)
{
	return GetTickCount () - p->t_zero;
}

#define resize_array(arr, old_size, new_size) do { \
	gpointer __x = g_malloc0 ((new_size) * sizeof (*arr)); \
	if (arr) \
		memcpy (__x, arr, (old_size) * sizeof (*arr)); \
	arr = __x; \
	old_size = (new_size); \
} while (0)

	

static guint32
get_type_idx (MonoProfiler *p, MonoClass* klass)
{
	guint32 idx_plus_one;
	
	if (!(idx_plus_one = GPOINTER_TO_UINT (g_hash_table_lookup (p->klass_to_table_idx, klass)))) {
		char* name = mono_type_get_full_name (mono_class_get_type (klass));
		g_ptr_array_add (p->klass_table, name);
		idx_plus_one = p->klass_table->len;
		
		g_hash_table_insert (p->klass_to_table_idx, klass, idx_plus_one);
		
		if (idx_plus_one > p->type_live_data_size)
			resize_array (p->type_live_data, p->type_live_data_size, MAX (p->type_live_data_size << 1, idx_plus_one));
	}
	
	return idx_plus_one - 1;
}

static void
record_obj (MonoProfiler* p, AllocRec* arec, gboolean is_alloc)
{
	if (is_alloc) {
		p->total_live_bytes += arec->size;
		p->type_live_data [arec->tidx] += arec->size;
	} else {
		p->total_live_bytes -= arec->size;
		p->type_live_data [arec->tidx] -= arec->size;
	}
}


static void
check_point (MonoProfiler* p, char* reason)
{
	int i;
	
	printf ("Checkpoint at %d for %s\n", get_delta_t (p), reason);
	
	for (i = 0; i < p->klass_table->len; i ++) {
		if (p->type_live_data [i] > (.01 * p->total_live_bytes))
			printf ("   %s : %d\n", g_ptr_array_index (p->klass_table, i), p->type_live_data [i]);
	}
}

static void
write_allocation (MonoProfiler *p, MonoObject *obj, MonoClass *klass)
{
	AllocRec* arec = g_new0 (AllocRec, 1);

	hp_lock_enter ();
	
	arec->obj = obj;
	arec->tidx = get_type_idx (p, klass);
	arec->size = mono_object_get_size (obj);
	arec->next = p->live_allocs;
	p->live_allocs = arec;
	
	record_obj (p, arec, TRUE);
	
	hp_lock_leave ();
}

extern gboolean mono_object_is_alive (MonoObject* o);

static void
prof_gc_collection (MonoProfiler *p, MonoGCEvent e, int gen)
{
	AllocRec *l, *next = NULL, *prev = NULL;
	
	if (e != MONO_GC_EVENT_MARK_END)
		return;
	
	hp_lock_enter ();
	
	for (l = p->live_allocs; l; l = next) {
		next = l->next;
		
		if (! mono_object_is_alive (l->obj)) {
			
			record_obj (p, l, FALSE);
			
			if (prev)
				prev->next = next;
			else
				p->live_allocs = next;
			
			g_free (l);
		} else 
			prev = l;
	}
	
	check_point (p, "gc");
	
	hp_lock_leave ();
}

static void
prof_heap_resize (MonoProfiler *p, gint64 new_size)
{
	printf ("heap resized to %lld @ %d\n", new_size, get_delta_t (p));
	check_point (p, "heap-resize");
}

static void
mono_heap_prof_shutdown (MonoProfiler *p)
{
	check_point (p, "shutdown");
}

void
mono_profiler_startup (const char *desc)
{
	MonoProfiler* p = g_new0 (MonoProfiler, 1);
	
	InitializeCriticalSection (&hp_lock);
	
	g_assert (! strncmp (desc, "desc-heap", 9));

	p->klass_to_table_idx  = g_hash_table_new (NULL, NULL);
	
	p->klass_table  = g_ptr_array_new ();
	
	
	
	p->t_zero = GetTickCount ();

	
	mono_profiler_install_allocation (write_allocation);
	mono_profiler_install_gc (prof_gc_collection, prof_heap_resize);
	mono_profiler_set_events (MONO_PROFILE_ALLOCATIONS | MONO_PROFILE_GC);
	
	mono_profiler_install (p, mono_heap_prof_shutdown);
}
