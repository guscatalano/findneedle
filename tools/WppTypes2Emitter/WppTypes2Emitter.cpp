// WppTypes2Emitter — round 2: counted strings, SID, GUID aliases, WINERROR, IP/port, i64 hex/octal.
#include <windows.h>
#include <winternl.h>   // UNICODE_STRING / ANSI_STRING
#include <sddl.h>
#include <stdio.h>

// {D4E5F6A7-3344-5566-7788-99AABBCCDDEE}
#define WPP_CONTROL_GUIDS \
    WPP_DEFINE_CONTROL_GUID(WppT2Ctl, (D4E5F6A7,3344,5566,7788,99AABBCCDDEE), \
        WPP_DEFINE_BIT(T2))
#define WPP_FLAG_LEVEL_LOGGER(flag, level) WPP_LEVEL_LOGGER(flag)
#define WPP_FLAG_LEVEL_ENABLED(flag, level) (WPP_LEVEL_ENABLED(flag) && WPP_CONTROL(WPP_BIT_##flag).Level >= level)
#define WPP_LEVEL_FLAGS_LOGGER(lvl, flags) WPP_LEVEL_LOGGER(flags)
#define WPP_LEVEL_FLAGS_ENABLED(lvl, flags) (WPP_LEVEL_ENABLED(flags) && WPP_CONTROL(WPP_BIT_##flags).Level >= lvl)
#include "WppTypes2Emitter.tmh"

int main()
{
    WPP_INIT_TRACING(L"WppTypes2Emitter");

    ANSI_STRING as; char ab[] = "CountedAnsi"; as.Buffer = ab; as.Length = (USHORT)strlen(ab); as.MaximumLength = as.Length + 1;
    UNICODE_STRING us; WCHAR wb[] = L"CountedWide"; us.Buffer = wb; us.Length = (USHORT)(wcslen(wb)*sizeof(WCHAR)); us.MaximumLength = us.Length + sizeof(WCHAR);
    DoTraceMessage(T2, "counted a=%!ANSTR! w=%!USTR!", &as, &us);

    BYTE sidbuf[SECURITY_MAX_SID_SIZE]; DWORD cb = sizeof(sidbuf);
    CreateWellKnownSid(WinLocalSystemSid, NULL, sidbuf, &cb);
    DoTraceMessage(T2, "sid=%!sid!", (PSID)sidbuf);

    GUID clsid = { 0xAABBCCDD, 0xEEFF, 0x1122, { 0x33,0x44,0x55,0x66,0x77,0x88,0x99,0x00 } };
    DoTraceMessage(T2, "clsid=%!CLSID! iid=%!IID!", &clsid, &clsid);

    DoTraceMessage(T2, "werr=%!WINERROR! i64X=0x%I64X i64o=%I64o", (DWORD)ERROR_FILE_NOT_FOUND, (unsigned long long)0xABCDEF, (unsigned long long)0x1FF);
    DoTraceMessage(T2, "ip=%!IPADDR! port=%!PORT!", (ULONG)0x0100007F, (USHORT)0x5000);

    WPP_CLEANUP();
    return 0;
}
