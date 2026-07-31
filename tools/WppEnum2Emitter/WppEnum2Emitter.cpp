#include <windows.h>
#include <stdio.h>
#include "WppEnum2Cfg.h"
#define WPP_CONTROL_GUIDS \
    WPP_DEFINE_CONTROL_GUID(WppEnum2Ctl, (B8C9DAEB,7788,99AA,BBCC,DDEEFF001122), WPP_DEFINE_BIT(E2))
#define WPP_FLAG_LEVEL_LOGGER(flag, level) WPP_LEVEL_LOGGER(flag)
#define WPP_FLAG_LEVEL_ENABLED(flag, level) (WPP_LEVEL_ENABLED(flag) && WPP_CONTROL(WPP_BIT_##flag).Level >= level)
#define WPP_LEVEL_FLAGS_LOGGER(lvl, flags) WPP_LEVEL_LOGGER(flags)
#define WPP_LEVEL_FLAGS_ENABLED(lvl, flags) (WPP_LEVEL_ENABLED(flags) && WPP_CONTROL(WPP_BIT_##flags).Level >= lvl)
#include "WppEnum2Emitter.tmh"
int main()
{
    WPP_INIT_TRACING(L"WppEnum2Emitter");
    MYSTATE s = StateActive; MYFLAGS f = (MYFLAGS)(FlagRead | FlagExec);
    DoTraceMessage(E2, "state=%!MyState! flags=%!MyFlags!", s, f);
    WPP_CLEANUP();
    return 0;
}
