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

#define leu32 GUINT32_TO_LE
#define lnatu32 GUINT32_FROM_LE
#define leu64 GINT64_TO_LE

typedef enum {
	MONO_TYPE_NAME_FORMAT_IL,
	MONO_TYPE_NAME_FORMAT_REFLECTION,
	MONO_TYPE_NAME_FORMAT_FULL_NAME,
	MONO_TYPE_NAME_FORMAT_ASSEMBLY_QUALIFIED
} MonoTypeNameFormat;

char*
mono_type_get_name_full (MonoType *type, MonoTypeNameFormat format);

typedef enum {
	HEAP_PROF_EVENT_GC = 0,
	HEAP_PROF_EVENT_RESIZE_HEAP = 1,
	HEAP_PROF_EVENT_CHECKPOINT = 2
} HeapProfEvent;

typedef guint32 HeapProfEvent32;

typedef struct AllocRec AllocRec;

#define PACK __attribute__ ((packed))

typedef struct PACK {
	guint64 alloc_pos;
	guint32 time;
	guint32 alloc_ctx;
} HeapProfGcFreedRec;

struct AllocRec {
	AllocRec* next;
	MonoObject* obj;
	HeapProfGcFreedRec rec;
};

struct _MonoProfiler {
	FILE* out;
	char* file;
	
	GPtrArray* klass_table;
	GHashTable* klass_to_table_idx;
	
	GPtrArray* method_table;
	GHashTable* method_to_table_idx;
	
	GPtrArray* bt_table;
	GHashTable* bt_to_table_idx;
	
	GPtrArray* ctx_table;
	GHashTable* ctx_to_table_idx;
	

	AllocRec* live_allocs;
	guint32 t_zero;
	guint64 foffset;
	
	int context_live_objects_size;
	int* context_live_objects;
	
	int type_live_data_size;
	int* type_live_data;
	
	int type_max_size;
	guint64* type_max;
	
	int total_live_bytes;
	
	GPtrArray* timeline;
	
	guint64 last_checkpoint;
};



static CRITICAL_SECTION hp_lock;
#define hp_lock_enter() EnterCriticalSection (&hp_lock)
#define hp_lock_leave() LeaveCriticalSection (&hp_lock)

/* binary file format */
static const guint8 heap_prof_dump_sig [] = {
	0x68, 0x30, 0xa4, 0x57, 0x18, 0xec, 0xd6, 0xa1,
	0x61, 0x9c, 0x1d, 0x43, 0xe1, 0x47, 0x27, 0xb6
};

static const guint8 heap_prof_md_sig [] = {
	0xe4, 0x37, 0x29, 0x60, 0x3e, 0x31, 0x89, 0x12, 
	0xaa, 0x93, 0xc8, 0x76, 0xf4, 0x6a, 0x95, 0x11
};

static const guint32 heap_prof_version = 6;

#define BT_SIZE 5

#define CHECKPOINT_SPACING (1024*1024) /* 1 MB */

typedef struct PACK {
	guint8 signature [16];
	guint32 version;
} HeapProfHeader;

typedef struct PACK {
	guint32 time;
	guint32 alloc_ctx;
} HeapProfAllocationRec;


typedef struct PACK {
	guint32 time;
	HeapProfEvent32 event;
	guint32 event_num;
	
	guint32 context_size;
	guint32 type_size;
} HeapProfCheckpointRec;

typedef struct PACK {
	guint32 time;
	HeapProfEvent32 event;
	guint32 event_num;
	
	HeapProfGcFreedRec freed [MONO_ZERO_LEN_ARRAY];
} HeapProfGCRec;

typedef struct PACK {
	guint32 time;
	HeapProfEvent32 event;
	guint32 event_num;
	
	guint32 new_size;
} HeapProfHeapResizeRec;

typedef struct PACK {
	guint32 time;
	HeapProfEvent32 event;
	guint32 size_high;
	guint32 size_low;
	
	guint64 file_pos;
} HeapProfTimelineRec;

static guint32
get_delta_t (MonoProfiler *p)
{
	return GetTickCount () - p->t_zero;
}

static guint64
prof_write (MonoProfiler* p, gconstpointer data, guint32 size)
{
	guint32 offset = p->foffset;
	p->foffset += size;
	fwrite (data, size, 1, p->out);
	return offset;
}

typedef struct {
	MonoMethod* methods [BT_SIZE];
} Backtrace;

typedef struct PACK {
	guint32 len;
	guint32 methods [BT_SIZE];
} IdxBacktrace;

typedef struct {
	MonoClass* klass;
	guint32 size;
	Backtrace bt;
} AllocationCtx;

typedef struct {
	guint32 klass;
	guint32 size;
	guint32 bt;
} IdxAllocationCtx;

#define resize_array(arr, old_size, new_size) do { \
	gpointer __x = g_malloc0 ((new_size) * sizeof (*arr)); \
	if (arr) \
		memcpy (__x, arr, (old_size) * sizeof (*arr)); \
	arr = __x; \
	old_size = (new_size); \
} while (0)

	
static guint32
get_method_idx (MonoProfiler *p, MonoMethod* m)
{
	guint32 idx_plus_one;
	
	if (!(idx_plus_one = GPOINTER_TO_UINT (g_hash_table_lookup (p->method_to_table_idx, m)))) {
		char* name = mono_method_full_name (m, TRUE);
		g_ptr_array_add (p->method_table, name);
		idx_plus_one = p->method_table->len;
		
		g_hash_table_insert (p->method_to_table_idx, m, idx_plus_one);
	}
	
	return leu32 (idx_plus_one - 1);
}

static guint32
get_type_idx (MonoProfiler *p, MonoClass* klass)
{
	guint32 idx_plus_one;
	
	if (!(idx_plus_one = GPOINTER_TO_UINT (g_hash_table_lookup (p->klass_to_table_idx, klass)))) {
		char* name = mono_type_get_name_full (mono_class_get_type (klass), MONO_TYPE_NAME_FORMAT_FULL_NAME);
		g_ptr_array_add (p->klass_table, name);
		idx_plus_one = p->klass_table->len;
		
		g_hash_table_insert (p->klass_to_table_idx, klass, idx_plus_one);
		
		if (idx_plus_one > p->type_live_data_size) {
			resize_array (p->type_live_data, p->type_live_data_size, MAX (p->type_live_data_size << 1, idx_plus_one));
			resize_array (p->type_max, p->type_max_size, MAX (p->type_max_size << 1, idx_plus_one));
		}
	}
	
	return leu32 (idx_plus_one - 1);
}


static guint32
get_bt_idx (MonoProfiler *p, Backtrace* bt)
{
	guint32 idx_plus_one;
	
	if (!(idx_plus_one = GPOINTER_TO_UINT (g_hash_table_lookup (p->bt_to_table_idx, bt)))) {
		
		IdxBacktrace* ibt = g_new0 (IdxBacktrace, 1);
		for (ibt->len = 0; ibt->len < BT_SIZE; ibt->len ++) {
			if (! bt->methods [ibt->len])
				break;
			ibt->methods [ibt->len] = get_method_idx (p, bt->methods [ibt->len]);
		}
		
		ibt->len = leu32 (ibt->len);
			
		g_ptr_array_add (p->bt_table, ibt);
		idx_plus_one = p->bt_table->len;
		
		g_hash_table_insert (p->bt_to_table_idx, g_memdup (bt, sizeof (*bt)), idx_plus_one);
	}
	
	return leu32 (idx_plus_one - 1);
}


static guint32
get_ctx_idx (MonoProfiler *p, AllocationCtx* ctx)
{
	guint32 idx_plus_one;
	
	if (!(idx_plus_one = GPOINTER_TO_UINT (g_hash_table_lookup (p->ctx_to_table_idx, ctx)))) {
		
		IdxAllocationCtx* ictx = g_new0 (IdxAllocationCtx, 1);
		
		ictx->klass = get_type_idx (p, ctx->klass);
		ictx->size = leu32 (ctx->size);
		ictx->bt = get_bt_idx (p, &ctx->bt);
			
		g_ptr_array_add (p->ctx_table, ictx);
		idx_plus_one = p->ctx_table->len;
		
		g_hash_table_insert (p->ctx_to_table_idx, g_memdup (ctx, sizeof (*ctx)), idx_plus_one);
		

		if (idx_plus_one > p->context_live_objects_size)
			resize_array (p->context_live_objects, p->context_live_objects_size, MAX (p->context_live_objects_size << 1, idx_plus_one));
	}
	
	return leu32 (idx_plus_one - 1);
}

static void
record_obj (MonoProfiler* p, guint32 ctx_idx, gboolean is_alloc)
{
	guint32 cidx = lnatu32 (ctx_idx);
	IdxAllocationCtx* ctx = g_ptr_array_index (p->ctx_table, cidx);
	guint32 tidx = lnatu32 (ctx->klass);
	guint32 size = lnatu32 (ctx->size);
	
	if (is_alloc) {
		p->total_live_bytes += size;
		p->type_live_data [tidx] += size;
		p->context_live_objects [cidx] ++;
		
		p->type_max [tidx] = MAX (p->type_max [tidx], p->type_live_data [tidx]);
	} else {
		p->total_live_bytes -= size;
		p->type_live_data [tidx] -= size;
		p->context_live_objects [cidx] --;
	}
}

typedef struct {
	AllocationCtx* c;
	int pos;
} AllocBTData;

static gboolean
get_bt (MonoMethod *m, gint no, gint ilo, gboolean managed, AllocBTData* data)
{
	if (!managed)
		return FALSE;
	
	data->c->bt.methods [data->pos++] = m;
	
	return data->pos == BT_SIZE;
}


static void
write_checkpoint_if_needed (MonoProfiler* p)
{
	HeapProfCheckpointRec rec;
	HeapProfTimelineRec* trec;
	
	guint32* ctx_rec;
	guint32* type_rec;
	
	guint64 pos;
	guint32 time = get_delta_t (p);
	int i;
	
	if (p->last_checkpoint + CHECKPOINT_SPACING > p->foffset)
		return;
	
	trec = g_new0 (HeapProfTimelineRec, 1);
	rec.time = leu32 (time | (1 << 31));
	rec.event = leu32 (HEAP_PROF_EVENT_CHECKPOINT);
	rec.event_num = p->timeline->len + 1;
	rec.context_size = leu32 (p->ctx_table->len);
	rec.type_size = leu32 (p->klass_table->len);
	
	ctx_rec = g_newa (guint32, p->ctx_table->len);
	type_rec = g_newa (guint32, p->klass_table->len);
	
	for (i = 0; i < p->ctx_table->len; i ++)
		ctx_rec [i] = leu32 (p->context_live_objects [i]);
	
	for (i = 0; i < p->klass_table->len; i ++)
		type_rec [i] = leu32 (p->type_live_data [i]);
	
	pos = prof_write (p, &rec, sizeof (rec));
	prof_write (p, ctx_rec, sizeof (*ctx_rec) * p->ctx_table->len);
	prof_write (p, type_rec, sizeof (*type_rec) * p->klass_table->len);
	
	trec->time = leu32 (time);
	trec->event = leu32 (HEAP_PROF_EVENT_CHECKPOINT);
	trec->file_pos = leu64 (pos);
	
	g_ptr_array_add (p->timeline, trec);
	
	p->last_checkpoint = p->foffset;
}

static void
write_allocation (MonoProfiler *p, MonoObject *obj, MonoClass *klass)
{
	AllocBTData btd = {0};
	AllocationCtx c = {0};
	guint32 offset;
	HeapProfAllocationRec rec;
	guint32 ctx_idx;
	AllocRec* arec = g_new0 (AllocRec, 1);
	
	btd.c = &c;
	
	mono_stack_walk_no_il (get_bt, &btd);

	c.klass = klass;
	c.size = mono_object_get_size (obj);	
	
	hp_lock_enter ();
	ctx_idx = get_ctx_idx (p, &c);

	rec.time = leu32  (get_delta_t (p));
	rec.alloc_ctx = ctx_idx;

	offset = prof_write (p, &rec, sizeof (rec));
	
	arec->rec.time = rec.time;
	arec->rec.alloc_ctx = rec.alloc_ctx;
	arec->rec.alloc_pos = leu64 (offset);
	arec->obj = obj;
	arec->next = p->live_allocs;
	p->live_allocs = arec;
	
	record_obj (p, ctx_idx, TRUE);
	
	write_checkpoint_if_needed (p);
	
	hp_lock_leave ();
}

extern gboolean mono_object_is_alive (MonoObject* o);

static void
prof_gc_collection (MonoProfiler *p, MonoGCEvent e, int gen)
{
	HeapProfGCRec rec;
	HeapProfTimelineRec* trec;
	
	guint64 pos;
	guint32 time;
	guint32 old_size;
	
	if (e != MONO_GC_EVENT_MARK_END)
		return;
	
	time = get_delta_t (p);
	trec = g_new0 (HeapProfTimelineRec, 1);
	
	hp_lock_enter ();
	
	old_size = p->total_live_bytes;
	
	rec.time = leu32 (time | (1 << 31));
	rec.event = leu32 (HEAP_PROF_EVENT_GC);
	rec.event_num = p->timeline->len + 1;

	pos = prof_write (p, &rec, sizeof (rec));
	
	AllocRec *l, *next = NULL, *prev = NULL;
	for (l = p->live_allocs; l; l = next) {
		next = l->next;
		
		if (! mono_object_is_alive (l->obj)) {
			prof_write (p, &l->rec, sizeof (l->rec));
			
			record_obj (p, l->rec.alloc_ctx, FALSE);
			
			if (prev)
				prev->next = next;
			else
				p->live_allocs = next;
			
			g_free (l);
		} else 
			prev = l;
	}
	
	{
		HeapProfGcFreedRec null = {0};
		prof_write (p, &null, sizeof (null));
	}
	
	trec->time = leu32 (time);
	trec->event = leu32 (HEAP_PROF_EVENT_GC);
	trec->size_high = leu32 (old_size);
	trec->size_low = leu32 (p->total_live_bytes);
	trec->file_pos = leu64 (pos);
	
	g_ptr_array_add (p->timeline, trec);
	
	hp_lock_leave ();
}

static void
prof_heap_resize (MonoProfiler *p, gint64 new_size)
{
	/* FIXME: 64 bit safety for the cast of new_size */
	HeapProfHeapResizeRec rec;
	HeapProfTimelineRec* trec = g_new0 (HeapProfTimelineRec, 1);

	guint64 pos;
	guint32 time = get_delta_t (p);
	
	hp_lock_enter ();
	
	rec.time = leu32 (time | (1 << 31));
	rec.event = leu32 (HEAP_PROF_EVENT_RESIZE_HEAP);
	rec.new_size = leu32 ((guint32) new_size);
	rec.event_num = p->timeline->len + 1;
	
	pos = prof_write (p, &rec, sizeof (rec));
	
	trec->time = leu32 (time);
	trec->event = leu32 (HEAP_PROF_EVENT_RESIZE_HEAP);
	trec->size_high = leu32 ((guint32) new_size);
	trec->file_pos = leu64 (pos);
	
	g_ptr_array_add (p->timeline, trec);
	
	hp_lock_leave ();
}

static void
write_enc_int (MonoProfiler*p, int v)
{
	do {
		int high = (v >> 7) & 0x01ffffff;
		guint8 b = (guint8) (v & 0x7f);

		if (high != 0) {
			b = (guint8) (b | 0x80);
		}

		prof_write (p, &b, sizeof (b));
		v = high;
	} while (v);
}

static void
write_string_table (MonoProfiler* p, GPtrArray* arr)
{
	int i;
	guint32 size = leu32 (arr->len);
	prof_write (p, &size, sizeof (size));
	
	for (i = 0; i < arr->len; i ++) {
		char* s = g_ptr_array_index (arr, i);
		int l = strlen (s);
		
		write_enc_int (p, l);
		prof_write (p, s, l);
	}
}

static void
write_data_table (MonoProfiler* p, GPtrArray* arr, guint32 elesz)
{
	int i;
	guint32 size = leu32 (arr->len);
	prof_write (p, &size, sizeof (size));
	
	for (i = 0; i < arr->len; i ++)
		prof_write (p, g_ptr_array_index (arr, i), elesz);
}

static void
write_meta_header (MonoProfiler* p)
{
	HeapProfHeader h;
	
	memcpy (h.signature, heap_prof_md_sig, sizeof (heap_prof_md_sig));
	h.version = leu32 (heap_prof_version);
	prof_write (p, &h, sizeof (h));

}

static void
write_type_max_table (MonoProfiler* p)
{
	guint32 size = p->klass_table->len;
	int i;
	guint32 encs = leu32 (size);
	prof_write (p, &encs, sizeof (encs));
	
	for (i = 0; i < size; i ++)
		p->type_max [i] = leu64 (p->type_max [i]);
	
	prof_write (p, p->type_max, size * sizeof (*p->type_max));
}

static void
write_metadata_file (MonoProfiler* p)
{
	write_meta_header (p);
	
	write_string_table (p, p->klass_table);
	write_string_table (p, p->method_table);
	write_data_table (p, p->bt_table, sizeof (IdxBacktrace));
	write_data_table (p, p->ctx_table, sizeof (IdxAllocationCtx));
	write_data_table (p, p->timeline, sizeof (HeapProfTimelineRec));
	write_type_max_table (p);
}


static void
mono_heap_prof_shutdown (MonoProfiler *p)
{
	guint32 eofevent = -1;
	guint64 meta_offset;
	
	meta_offset = prof_write (p, &eofevent, sizeof (eofevent)) + sizeof (eofevent);
	
	write_metadata_file (p);
	
	meta_offset = leu64 (meta_offset);
	
	prof_write (p, &meta_offset, sizeof (meta_offset));
	
	fclose (p->out);
}

static void
write_header (MonoProfiler* p)
{
	HeapProfHeader h;
	
	memcpy (h.signature, heap_prof_dump_sig, sizeof (heap_prof_dump_sig));
	h.version = leu32 (heap_prof_version);
	prof_write (p, &h, sizeof (h));
}

static guint
ctx_hash (const AllocationCtx* c)
{
	const guint* x = c;
	int i, h = 0;
	
	for (i = 0; i < sizeof (*c) / sizeof (*x); i ++) {
		h *= 31;
		h += x [i];
	}
	
	return h;
}

static gboolean
ctx_eq (const AllocationCtx* a, const AllocationCtx* b)
{
	return !memcmp (a, b, sizeof (*a));
}

static guint
bt_hash (const Backtrace* c)
{
	const guint* x = c;
	int i, h = 0;
	
	for (i = 0; i < sizeof (*c) / sizeof (*x); i ++) {
		h *= 31;
		h += x [i];
	}
	
	return h;
}

static gboolean
bt_eq (const Backtrace* a, const Backtrace* b)
{
	return !memcmp (a, b, sizeof (*a));
}

void
do_default_file_name (MonoProfiler* p)
{
	int pid = getpid ();
	int i = 0;
	
	while (TRUE) {
		if (i == 0)
			p->file = g_strdup_printf ("mono-heap-prof.%d", pid);
		else
			p->file = g_strdup_printf ("mono-heap-prof.%d.%d", pid, i);
		
		p->out = fopen (p->file, "w+x");
		
		if (p->out)
			break;
		
		g_free (p->file);
	}
}

void
mono_profiler_startup (const char *desc)
{
	MonoProfiler* p = g_new0 (MonoProfiler, 1);
	
	InitializeCriticalSection (&hp_lock);
	
	g_assert (! strncmp (desc, "heap", 4));
	
	if (strncmp (desc, "heap:", 5))
		do_default_file_name (p);
	else {
		p->file = strdup (desc + 5);
		p->out = fopen (p->file, "w+");
	}
	
	p->klass_to_table_idx  = g_hash_table_new (NULL, NULL);
	p->method_to_table_idx = g_hash_table_new (NULL, NULL);
	p->bt_to_table_idx     = g_hash_table_new (bt_hash, bt_eq);
	p->ctx_to_table_idx    = g_hash_table_new (ctx_hash, ctx_eq);
	
	p->klass_table  = g_ptr_array_new ();
	p->method_table = g_ptr_array_new ();
	p->bt_table     = g_ptr_array_new ();
	p->ctx_table    = g_ptr_array_new ();
	p->timeline     = g_ptr_array_new ();
	
	
	
	p->t_zero = GetTickCount ();
	
	write_header (p);
	
	mono_profiler_install_allocation (write_allocation);
	mono_profiler_install_gc (prof_gc_collection, prof_heap_resize);
	mono_profiler_set_events (MONO_PROFILE_ALLOCATIONS | MONO_PROFILE_GC);
	
	mono_profiler_install (p, mono_heap_prof_shutdown);
}
