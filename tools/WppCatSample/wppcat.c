// Minimal WPP (Windows software trace preprocessor) producer used to generate a *real* WPP .etl
// fixture (with a matching .tmf) so the tracefmt decode path in ETWPlugin can be tested. Unlike the
// .NET EventSource fixtures (cats-5M.etl), WPP events are NOT self-describing: tracefmt needs the TMF
// extracted from this binary's PDB to format them. See tools/WppCatSample/build-wpp-cat.ps1.
//
// Build is driven by the script: tracewpp generates wppcat.tmh from the DoTraceMessage calls below,
// then cl compiles this with the WDK/SDK headers.

#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <evntrace.h>

// ---- WPP configuration ----------------------------------------------------------------------
// One control GUID ("CatTraceControl") with a single trace flag ("CAT"). The control GUID is what
// a trace session enables (see the script's tracelog -guid). Keep this GUID in sync with the script.
#define WPP_CONTROL_GUIDS \
    WPP_DEFINE_CONTROL_GUID( \
        CatTraceControl, (A1B2C3D4,E5F6,4789,ABCD,1234567890AB), \
        WPP_DEFINE_BIT(CAT))

// Standard flag-based DoTraceMessage(FLAG, "fmt", ...) plumbing.
#define WPP_FLAG_LEVEL_LOGGER(flag, level)  WPP_LEVEL_LOGGER(flag)
#define WPP_FLAG_LEVEL_ENABLED(flag, level) (WPP_LEVEL_ENABLED(flag) && WPP_CONTROL(WPP_BIT_ ## flag).Level >= level)
#define WPP_LEVEL_FLAGS_LOGGER(lvl, flags)  WPP_LEVEL_LOGGER(flags)
#define WPP_LEVEL_FLAGS_ENABLED(lvl, flags) (WPP_LEVEL_ENABLED(flags) && WPP_CONTROL(WPP_BIT_ ## flags).Level >= lvl)

// tracewpp generates this from the DoTraceMessage() calls in this file.
#include "wppcat.tmh"

int main(int argc, char** argv)
{
    int count = (argc > 1) ? atoi(argv[1]) : 100000;
    if (count < 1) count = 1;

    const char* breeds[] = { "Siamese", "Persian", "Maine Coon", "Bengal", "Sphynx", "Ragdoll" };
    const char* colors[] = { "black", "white", "tabby", "calico", "orange", "grey", "tuxedo" };

    WPP_INIT_TRACING(L"wppcat");   // user-mode WPP takes an app name

    for (int i = 0; i < count; i++)
    {
        const char* breed = breeds[i % (int)(sizeof(breeds) / sizeof(breeds[0]))];
        const char* color = colors[i % (int)(sizeof(colors) / sizeof(colors[0]))];
        DoTraceMessage(CAT, "cat #%d breed=%s color=%s", i, breed, color);
        if (i == 1234567)
            DoTraceMessage(CAT, "cat #%d breed=%s color=%s NAME=Mittens", i, breed, color);
        // Periodically yield so the ETW session can flush buffers — without this, emitting millions
        // of messages in a tight loop overruns the buffers and the session drops a lot of events.
        if ((i & 0x1FFFF) == 0x1FFFF)
            Sleep(1);
    }

    WPP_CLEANUP();

    printf("wppcat: emitted %d WPP messages\n", count);
    return 0;
}
