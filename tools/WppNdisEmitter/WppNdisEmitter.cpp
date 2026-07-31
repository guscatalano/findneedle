#include <windows.h>
#include <stdio.h>
// {DAEBFC0D-99AA-BBCC-DDEE-FF0011223344}
#define WPP_CONTROL_GUIDS \
    WPP_DEFINE_CONTROL_GUID(WppNdisCtl, (DAEBFC0D,99AA,BBCC,DDEE,FF0011223344), WPP_DEFINE_BIT(ND))
#define WPP_FLAG_LEVEL_LOGGER(flag, level) WPP_LEVEL_LOGGER(flag)
#define WPP_FLAG_LEVEL_ENABLED(flag, level) (WPP_LEVEL_ENABLED(flag) && WPP_CONTROL(WPP_BIT_##flag).Level >= level)
#define WPP_LEVEL_FLAGS_LOGGER(lvl, flags) WPP_LEVEL_LOGGER(flags)
#define WPP_LEVEL_FLAGS_ENABLED(lvl, flags) (WPP_LEVEL_ENABLED(flags) && WPP_CONTROL(WPP_BIT_##flags).Level >= lvl)
#include "WppNdisEmitter.tmh"
int main()
{
    WPP_INIT_TRACING(L"WppNdisEmitter");
    DoTraceMessage(ND, "nst=%!NDIS_STATUS! noid=%!NDIS_OID!", (int)0x4001000B, (unsigned)0x00010101);
    WPP_CLEANUP();
    return 0;
}
