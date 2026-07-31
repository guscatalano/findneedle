// WppStrEmitter — emits WPP trace events with STRING args (%s ANSI + %ws wide) so we can validate the
// managed decoder's string handling against real WPP wire data (vs tracefmt).
#include <windows.h>
#include <stdio.h>

// {B2C3D4E5-1122-3344-5566-778899AABBCC}
#define WPP_CONTROL_GUIDS                                          \
    WPP_DEFINE_CONTROL_GUID(                                       \
        WppStrCtl, (B2C3D4E5,1122,3344,5566,778899AABBCC),         \
        WPP_DEFINE_BIT(STR_GENERAL))

#define WPP_FLAG_LEVEL_LOGGER(flag, level) WPP_LEVEL_LOGGER(flag)
#define WPP_FLAG_LEVEL_ENABLED(flag, level) \
    (WPP_LEVEL_ENABLED(flag) && WPP_CONTROL(WPP_BIT_##flag).Level >= level)
#define WPP_LEVEL_FLAGS_LOGGER(lvl, flags) WPP_LEVEL_LOGGER(flags)
#define WPP_LEVEL_FLAGS_ENABLED(lvl, flags) \
    (WPP_LEVEL_ENABLED(flags) && WPP_CONTROL(WPP_BIT_##flags).Level >= lvl)

#include "WppStrEmitter.tmh"

int main(int argc, char** argv)
{
    WPP_INIT_TRACING(L"WppStrEmitter");
    const char* names[] = { "alpha", "bravo", "charlie" };
    for (int i = 0; i < 3; i++)
    {
        DoTraceMessage(STR_GENERAL, "strtrace name=%s id=%d tag=END", names[i], i);
    }
    DoTraceMessage(STR_GENERAL, "widetrace user=%ws role=admin", L"root");
    WPP_CLEANUP();
    return 0;
}
