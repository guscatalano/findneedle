// WppEnumEmitter — list-enums (value->name) + bit-set flags. Name tables are embedded in the TMF.
#include <windows.h>
#include <stdio.h>

// {E5F6A7B8-4455-6677-8899-AABBCCDDEEFF}
#define WPP_CONTROL_GUIDS \
    WPP_DEFINE_CONTROL_GUID(WppEnumCtl, (E5F6A7B8,4455,6677,8899,AABBCCDDEEFF), \
        WPP_DEFINE_BIT(EN))
#define WPP_FLAG_LEVEL_LOGGER(flag, level) WPP_LEVEL_LOGGER(flag)
#define WPP_FLAG_LEVEL_ENABLED(flag, level) (WPP_LEVEL_ENABLED(flag) && WPP_CONTROL(WPP_BIT_##flag).Level >= level)
#define WPP_LEVEL_FLAGS_LOGGER(lvl, flags) WPP_LEVEL_LOGGER(flags)
#define WPP_LEVEL_FLAGS_ENABLED(lvl, flags) (WPP_LEVEL_ENABLED(flags) && WPP_CONTROL(WPP_BIT_##flags).Level >= lvl)


#include "WppEnumEmitter.tmh"

int main()
{
    WPP_INIT_TRACING(L"WppEnumEmitter");
    DoTraceMessage(EN, "b=%!bool! irql=%!irql!", TRUE, 2 /*DPC*/);
    DoTraceMessage(EN, "set=%!b4!", (unsigned long)0x5);
    WPP_CLEANUP();
    return 0;
}
