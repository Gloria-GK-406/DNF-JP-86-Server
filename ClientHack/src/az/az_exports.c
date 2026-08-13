#include <windows.h>

#if defined(_MSC_VER)
#pragma warning(push)
#pragma warning(disable: 4152)
#endif

static void __cdecl ts_noop_cdecl(void) {}
static void __stdcall ts_noop_2(int a, int b) { (void)a; (void)b; }
static void __stdcall ts_noop_3(int a, int b, int c) { (void)a; (void)b; (void)c; }
static void __stdcall ts_out_zero(int a, int b, int c, int d, int e, int f, int* out)
{
    (void)a; (void)b; (void)c; (void)d; (void)e; (void)f;
    if (out != NULL) *out = 0;
}
static int __stdcall ts_return_zero(int a) { (void)a; return 0; }
static int __stdcall ts_return_one(int a) { (void)a; return 1; }

static void* g_vtable_a[10] = {
    ts_noop_3, ts_noop_cdecl, ts_noop_2, ts_noop_cdecl, ts_noop_cdecl,
    ts_noop_cdecl, ts_noop_cdecl, ts_noop_cdecl, ts_out_zero, NULL
};
static unsigned int g_vtable_b_data[6];
static void* g_vtable_b[20] = {
    ts_return_zero, ts_noop_cdecl, ts_noop_cdecl, ts_noop_cdecl, ts_noop_cdecl,
    ts_noop_cdecl, ts_noop_cdecl, ts_noop_cdecl, ts_noop_cdecl, ts_noop_cdecl,
    ts_noop_cdecl, ts_noop_2, ts_noop_cdecl, ts_noop_cdecl, ts_noop_cdecl,
    ts_noop_cdecl, ts_return_one, g_vtable_b_data, ts_noop_3, ts_noop_cdecl
};
static void* g_tersafe_object[9];
static INIT_ONCE g_tersafe_once = INIT_ONCE_STATIC_INIT;

static BOOL CALLBACK ts_initialize(PINIT_ONCE once, PVOID parameter, PVOID* context)
{
    (void)once; (void)parameter; (void)context;
    g_tersafe_object[0] = g_vtable_a;
    g_tersafe_object[4] = g_vtable_b;
    return TRUE;
}

void** __cdecl CreateObj(char type)
{
    InitOnceExecuteOnce(&g_tersafe_once, ts_initialize, NULL, NULL);
    if (type == 3) return &g_tersafe_object[0];
    if (type == 7) return &g_tersafe_object[4];
    return NULL;
}

void __cdecl ClearRSInfo(void) {}
void __cdecl PutRSInfo(void) {}
void __cdecl Test(void) {}
const char* __cdecl ijlErrorStr(int error) { (void)error; return "OK"; }
void __cdecl ijlFree(void* value) { (void)value; }
int __cdecl ijlGetLibVersion(void) { return 0; }
int __cdecl ijlInit(void* value) { (void)value; return 0; }
int __cdecl ijlRead(void* value, int flags) { (void)value; (void)flags; return 0; }
int __cdecl ijlWrite(void* value, int flags) { (void)value; (void)flags; return 0; }

#if defined(_MSC_VER)
#pragma warning(pop)
#endif
