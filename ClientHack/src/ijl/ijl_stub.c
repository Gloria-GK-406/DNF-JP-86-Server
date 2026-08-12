#include <stddef.h>

const char* __cdecl ijlErrorStr(int error) { (void)error; return "IJL compatibility stub"; }
void __cdecl ijlFree(void* value) { (void)value; }
int __cdecl ijlGetLibVersion(void) { return 0; }
int __cdecl ijlInit(void* value) { (void)value; return 0; }
int __cdecl ijlRead(void* value, int flags) { (void)value; (void)flags; return 0; }
int __cdecl ijlWrite(void* value, int flags) { (void)value; (void)flags; return 0; }
