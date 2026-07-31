#include <windows.h>
#include <stdio.h>
// {F6A7B8C9-5566-7788-99AA-BBCCDDEEFF00}
#define WPP_CONTROL_GUIDS \
    WPP_DEFINE_CONTROL_GUID(WppTimeCtl, (F6A7B8C9,5566,7788,99AA,BBCCDDEEFF00), WPP_DEFINE_BIT(TM))
#define WPP_FLAG_LEVEL_LOGGER(flag, level) WPP_LEVEL_LOGGER(flag)
#define WPP_FLAG_LEVEL_ENABLED(flag, level) (WPP_LEVEL_ENABLED(flag) && WPP_CONTROL(WPP_BIT_##flag).Level >= level)
#define WPP_LEVEL_FLAGS_LOGGER(lvl, flags) WPP_LEVEL_LOGGER(flags)
#define WPP_LEVEL_FLAGS_ENABLED(lvl, flags) (WPP_LEVEL_ENABLED(flags) && WPP_CONTROL(WPP_BIT_##flags).Level >= lvl)
#include "WppTimeEmitter.tmh"
int main()
{
    WPP_INIT_TRACING(L"WppTimeEmitter");
    FILETIME ft; ULARGE_INTEGER u; u.QuadPart = 132223104000000000ULL; // 2020-01-01 00:00:00 UTC
    ft.dwLowDateTime = u.LowPart; ft.dwHighDateTime = u.HighPart;
    DoTraceMessage(TM, "ts=%!TIME!", (LONGLONG)u.QuadPart);
    DoTraceMessage(TM, "cc=%!cccc!", (int)'ABGR');
    WPP_CLEANUP();
    return 0;
}
