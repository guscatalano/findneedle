#include <windows.h>
#include <stdio.h>
// {A7B8C9DA-6677-8899-AABB-CCDDEEFF0011}
#define WPP_CONTROL_GUIDS \
    WPP_DEFINE_CONTROL_GUID(WppMiscCtl, (A7B8C9DA,6677,8899,AABB,CCDDEEFF0011), WPP_DEFINE_BIT(MS))
#define WPP_FLAG_LEVEL_LOGGER(flag, level) WPP_LEVEL_LOGGER(flag)
#define WPP_FLAG_LEVEL_ENABLED(flag, level) (WPP_LEVEL_ENABLED(flag) && WPP_CONTROL(WPP_BIT_##flag).Level >= level)
#define WPP_LEVEL_FLAGS_LOGGER(lvl, flags) WPP_LEVEL_LOGGER(flags)
#define WPP_LEVEL_FLAGS_ENABLED(lvl, flags) (WPP_LEVEL_ENABLED(flags) && WPP_CONTROL(WPP_BIT_##flags).Level >= lvl)
#include "WppMiscEmitter.tmh"
int main()
{
    WPP_INIT_TRACING(L"WppMiscEmitter");
    DoTraceMessage(MS, "dbl=%!DOUBLE! delta=%!delta!", 3.140625, (LONGLONG)50000000);
    DoTraceMessage(MS, "raw a=%!ARSTR! w=%!ARWSTR!", "RawAnsi", L"RawWide");
    WPP_CLEANUP();
    return 0;
}
