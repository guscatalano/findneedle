// WppEmitter — a tiny user-mode app that emits WPP software-trace events, used only to build the
// test fixture: an .etl that contains WPP events (which decode via tracefmt + this binary's TMF) so
// the load/filter tests can exercise the "with vs without WPP symbols" paths. Paired with LogETWApp
// (TraceLogging) under one ETW capture to produce a mixed WPP+TraceLogging .etl.
//
// Build: tools\WppEmitter\build.ps1 (runs tracewpp -> cl -> link -> tracepdb to emit the .tmf).

#include <windows.h>
#include <stdio.h>
#include <stdlib.h>

// Control GUID for this provider. {A7C8B3D2-1E4F-4A6B-9C2D-3F5E6A7B8C9D}
#define WPP_CONTROL_GUIDS                                              \
    WPP_DEFINE_CONTROL_GUID(                                           \
        WppEmitterCtl, (A7C8B3D2, 1E4F, 4A6B, 9C2D, 3F5E6A7B8C9D),     \
        WPP_DEFINE_BIT(TRACE_GENERAL)                                  \
        WPP_DEFINE_BIT(TRACE_DETAIL))

#define WPP_FLAG_LEVEL_LOGGER(flag, level) WPP_LEVEL_LOGGER(flag)
#define WPP_FLAG_LEVEL_ENABLED(flag, level) \
    (WPP_LEVEL_ENABLED(flag) && WPP_CONTROL(WPP_BIT_##flag).Level >= level)
#define WPP_LEVEL_FLAGS_LOGGER(lvl, flags) WPP_LEVEL_LOGGER(flags)
#define WPP_LEVEL_FLAGS_ENABLED(lvl, flags) \
    (WPP_LEVEL_ENABLED(flags) && WPP_CONTROL(WPP_BIT_##flags).Level >= lvl)

// tracewpp generates this from the DoTraceMessage calls below.
#include "WppEmitter.tmh"

int main(int argc, char** argv)
{
    WPP_INIT_TRACING(L"WppEmitter");

    long count = (argc > 1) ? atol(argv[1]) : 100000;
    for (long i = 0; i < count; i++)
    {
        DoTraceMessage(TRACE_GENERAL,
            "WppEmitter work item id=%d status=0x%x phase=startup provider=WppEmitter", i, (unsigned)(i & 0xff));
        if ((i & 1) == 0)
        {
            DoTraceMessage(TRACE_DETAIL,
                "WppEmitter detail seq=%d note=processing-record value=%d category=Detail", i, i % 7919);
        }
    }

    WPP_CLEANUP();
    return 0;
}
