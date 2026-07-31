// WppTypesEmitter — exercises the common C++/Win32 + pointer types so we can validate the managed
// decoder against real wire data + tracefmt ground truth.
#include <windows.h>
#include <stdio.h>

// {C3D4E5F6-2233-4455-6677-8899AABBCCDD}
#define WPP_CONTROL_GUIDS                                          \
    WPP_DEFINE_CONTROL_GUID(                                       \
        WppTypesCtl, (C3D4E5F6,2233,4455,6677,8899AABBCCDD),       \
        WPP_DEFINE_BIT(T_ALL))
#define WPP_FLAG_LEVEL_LOGGER(flag, level) WPP_LEVEL_LOGGER(flag)
#define WPP_FLAG_LEVEL_ENABLED(flag, level) \
    (WPP_LEVEL_ENABLED(flag) && WPP_CONTROL(WPP_BIT_##flag).Level >= level)
#define WPP_LEVEL_FLAGS_LOGGER(lvl, flags) WPP_LEVEL_LOGGER(flags)
#define WPP_LEVEL_FLAGS_ENABLED(lvl, flags) \
    (WPP_LEVEL_ENABLED(flags) && WPP_CONTROL(WPP_BIT_##flags).Level >= lvl)
#include "WppTypesEmitter.tmh"

int main()
{
    WPP_INIT_TRACING(L"WppTypesEmitter");

    DoTraceMessage(T_ALL, "sint i=%d neg=%d i64=%I64d", (int)42, (int)-1000, (long long)-5000000000LL);
    DoTraceMessage(T_ALL, "uint u=%u hex=0x%x x64=0x%I64x", (unsigned)4000000000u, (unsigned)0xABCDEF, (unsigned long long)0xDEADBEEFCAFEULL);
    DoTraceMessage(T_ALL, "ptr p=%p null=%p", (void*)(ULONG_PTR)0x7ff012345678ULL, (void*)0);
    DoTraceMessage(T_ALL, "widths z=%05d h=%08x", (int)42, (unsigned)0xBEEF);
    GUID g = { 0x11223344, 0x5566, 0x7788, { 0x99,0xAA,0xBB,0xCC,0xDD,0xEE,0xFF,0x00 } };
    DoTraceMessage(T_ALL, "hr=%!HRESULT! st=%!STATUS! guid=%!GUID!", (HRESULT)E_ACCESSDENIED, (NTSTATUS)0xC0000022L, &g);

    WPP_CLEANUP();
    return 0;
}
