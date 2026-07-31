#pragma once
typedef enum _MYSTATE { StateIdle = 0, StateActive = 1, StateError = 2 } MYSTATE;
typedef enum _MYFLAGS { FlagRead = 1, FlagWrite = 2, FlagExec = 4 } MYFLAGS;
// begin_wpp config
// CUSTOM_TYPE(MyState, ItemEnum(_MYSTATE) );
// CUSTOM_TYPE(MyFlags, ItemFlagsEnum(_MYFLAGS) );
// end_wpp
