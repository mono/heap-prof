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

#define leu32 GUINT32_TO_LE
#define leu64 GINT64_TO_LE


typedef struct AllocRec AllocRec;

typedef struct {
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

static const guint32 heap_prof_version = 2;

#define BT_SIZE 5

typedef struct {
	guint8 signature [16];
	guint32 version;
} HeapProfHeader;

typedef struct {
	guint32 time;
	guint32 alloc_ctx;
} HeapProfAllocationRec;



typedef struct {
	guint32 time;
	guint32 gc_num;
	HeapProfGcFreedRec freed [MONO_ZERO_LEN_ARRAY];
} HeapProfGCRec;



static guint32
get_delta_t (MonoProfiler *p)
{
	return GetTickCount () - p->t_zero;
}

static guint64
write (MonoProfiler* p, gconstpointer data, guint32 size)
{
	guint32 offset = p->foffset;
	p->foffset += size;
	fwrite (data, size, 1, p->out);
	return offset;
}

typedef struct {
	MonoMethod* methods [BT_SIZE];
} Backtrace;

typedef struct {
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
	
	return idx_plus_one - 1;
}

static guint32
get_type_idx (MonoProfiler *p, MonoClass* klass)
{
	guint32 idx_plus_one;
	
	if (!(idx_plus_one = GPOINTER_TO_UINT (g_hash_table_lookup (p->klass_to_table_idx, klass)))) {
		char* name = mono_type_get_full_name (mono_class_get_type (klass));
		g_ptr_array_add (p->klass_table, name);
		idx_plus_one = p->klass_table->len;
		
		g_hash_table_insert (p->klass_to_table_idx, klass, idx_plus_one);
	}
	
	return idx_plus_one - 1;
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
			
		g_ptr_array_add (p->bt_table, ibt);
		idx_plus_one = p->bt_table->len;
		
		g_hash_table_insert (p->bt_to_table_idx, g_memdup (bt, sizeof (*bt)), idx_plus_one);
	}
	
	return idx_plus_one - 1;
}


static guint32
get_ctx_idx (MonoProfiler *p, AllocationCtx* ctx)
{
	guint32 idx_plus_one;
	
	if (!(idx_plus_one = GPOINTER_TO_UINT (g_hash_table_lookup (p->ctx_to_table_idx, ctx)))) {
		
		IdxAllocationCtx* ictx = g_new0 (IdxAllocationCtx, 1);
		
		ictx->klass = get_type_idx (p, ctx->klass);
		ictx->size = ctx->size;
		ictx->bt = get_bt_idx (p, &ctx->bt);
			
		g_ptr_array_add (p->ctx_table, ictx);
		idx_plus_one = p->ctx_table->len;
		
		g_hash_table_insert (p->ctx_to_table_idx, g_memdup (ctx, sizeof (*ctx)), idx_plus_one);
	}
	
	return idx_plus_one - 1;
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
write_allocation (MonoProfiler *p, MonoObject *obj, MonoClass *klass)
{
	AllocBTData btd = {0};
	AllocationCtx c = {0};
	guint32 offset;
	HeapProfAllocationRec rec;
	AllocRec* arec = g_new0 (AllocRec, 1);
	
	btd.c = &c;
	
	mono_stack_walk_no_il (get_bt, &btd);

	c.klass = klass;
	c.size = mono_object_get_size (obj);	
	
	hp_lock_enter ();

	rec.time = leu32  (get_delta_t (p));
	rec.alloc_ctx = leu32  (get_ctx_idx (p, &c));

	offset = write (p, &rec, sizeof (rec));
	
	arec->rec.time = rec.time;
	arec->rec.alloc_ctx = rec.alloc_ctx;
	arec->rec.alloc_pos = leu64 (offset);
	arec->obj = obj;
	arec->next = p->live_allocs;
	p->live_allocs = arec;
	
	hp_lock_leave ();
}

static void
prof_marks_set (MonoProfiler *p, int gc_num)
{
	HeapProfGCRec rec;

	hp_lock_enter ();
	
	rec.time = leu32 (get_delta_t (p) | (1 << 31));
	rec.gc_num = leu32 (gc_num);

	write (p, &rec, sizeof (rec));
	
	AllocRec *l, *next = NULL, *prev = NULL;
	for (l = p->live_allocs; l; l = next) {
		next = l->next;
		
		if (! mono_profiler_mark_set (l->obj)) {
			write (p, &l->rec, sizeof (l->rec));
			
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
		write (p, &null, sizeof (null));
	}
	
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

		write (p, &b, sizeof (b));
		v = high;
	} while (v);
}

static void
write_string_table (MonoProfiler* p, GPtrArray* arr)
{
	int i;
	guint32 size = leu32 (arr->len);
	write (p, &size, sizeof (size));
	
	for (i = 0; i < arr->len; i ++) {
		char* s = g_ptr_array_index (arr, i);
		int l = strlen (s);
		
		write_enc_int (p, l);
		write (p, s, l);
	}
}

static void
write_bt_table (MonoProfiler* p)
{
	GPtrArray* arr = p->bt_table;
	int i;
	guint32 size = leu32 (arr->len);
	write (p, &size, sizeof (size));
	
	for (i = 0; i < arr->len; i ++) {
		IdxBacktrace* b = g_ptr_array_index (arr, i);
		write (p, b, sizeof (*b));
	}
}

static void
write_ctx_table (MonoProfiler* p)
{
	GPtrArray* arr = p->ctx_table;
	int i;
	guint32 size = leu32 (arr->len);
	
	write (p, &size, sizeof (size));
	
	for (i = 0; i < arr->len; i ++) {
		IdxAllocationCtx* c = g_ptr_array_index (arr, i);

		
		write (p, c, sizeof (*c));
	}
}

static void
write_meta_header (MonoProfiler* p)
{
	HeapProfHeader h;
	
	memcpy (h.signature, heap_prof_md_sig, sizeof (heap_prof_md_sig));
	h.version = leu32 (heap_prof_version);
	write (p, &h, sizeof (h));

}

static void
write_metadata_file (MonoProfiler* p)
{
	write_meta_header (p);
	
	write_string_table (p, p->klass_table);
	write_string_table (p, p->method_table);
	write_bt_table (p);
	write_ctx_table (p);
}


static void
mono_heap_prof_shutdown (MonoProfiler *p)
{
	guint32 eofevent = -1;
	guint64 meta_offset;
	
	meta_offset = write (p, &eofevent, sizeof (eofevent)) + sizeof (eofevent);
	
	write_metadata_file (p);
	
	meta_offset = leu64 (meta_offset);
	
	write (p, &meta_offset, sizeof (meta_offset));
	
	fclose (p->out);
}

static void
write_header (MonoProfiler* p)
{
	HeapProfHeader h;
	
	memcpy (h.signature, heap_prof_dump_sig, sizeof (heap_prof_dump_sig));
	h.version = leu32 (heap_prof_version);
	write (p, &h, sizeof (h));
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
mono_profiler_startup (const char *desc)
{
	const char* file;
	char* dump_file;
	MonoProfiler* p = g_new0 (MonoProfiler, 1);
	
	InitializeCriticalSection (&hp_lock);
	
	g_assert (! strncmp (desc, "heap", 4));
	
	if (strncmp (desc, "heap:", 5))
		g_error ("You need to specify an output file for the heap profiler with --profile=heap:outfile");
	
	p->file = strdup (desc + 5);
	
	p->klass_to_table_idx  = g_hash_table_new (NULL, NULL);
	p->method_to_table_idx = g_hash_table_new (NULL, NULL);
	p->bt_to_table_idx     = g_hash_table_new (bt_hash, bt_eq);
	p->ctx_to_table_idx    = g_hash_table_new (ctx_hash, ctx_eq);
	
	p->klass_table  = g_ptr_array_new ();
	p->method_table = g_ptr_array_new ();
	p->bt_table     = g_ptr_array_new ();
	p->ctx_table    = g_ptr_array_new ();
	
	
	p->out = fopen (p->file, "w+");
	p->t_zero = GetTickCount ();
	
	write_header (p);
	
	mono_profiler_install_allocation (write_allocation);
	mono_profiler_install_gc (prof_marks_set);
	mono_profiler_set_events (MONO_PROFILE_ALLOCATIONS | MONO_PROFILE_GC);
	
	mono_profiler_install (p, mono_heap_prof_shutdown);
}