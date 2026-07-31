#include <windows.h>
#include <stdio.h>
// {C9DAEBFC-8899-AABB-CCDD-EEFF00112233}
#define WPP_CONTROL_GUIDS \
    WPP_DEFINE_CONTROL_GUID(WppBinCtl, (C9DAEBFC,8899,AABB,CCDD,EEFF00112233), WPP_DEFINE_BIT(BN))
#define WPP_FLAG_LEVEL_LOGGER(flag, level) WPP_LEVEL_LOGGER(flag)
#define WPP_FLAG_LEVEL_ENABLED(flag, level) (WPP_LEVEL_ENABLED(flag) && WPP_CONTROL(WPP_BIT_##flag).Level >= level)
#define WPP_LEVEL_FLAGS_LOGGER(lvl, flags) WPP_LEVEL_LOGGER(flags)
#define WPP_LEVEL_FLAGS_ENABLED(lvl, flags) (WPP_LEVEL_ENABLED(flags) && WPP_CONTROL(WPP_BIT_##flags).Level >= lvl)
#include "WppBinEmitter.tmh"
int main()
{
    WPP_INIT_TRACING(L"WppBinEmitter");
    unsigned char data[] = { 0xDE,0xAD,0xBE,0xEF,0x00,0x11,0x22,0x33 };
    DoTraceMessage(BN, "hex=%!BIN!", WppBinary(data, (USHORT)sizeof(data)));
    DoTraceMessage(BN, "due=%!due!", (LONGLONG)132223104000000000LL);
    WPP_CLEANUP();
    return 0;
}
