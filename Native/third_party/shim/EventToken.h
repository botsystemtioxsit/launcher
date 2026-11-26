// Заглушка: в mingw-w64 нет EventToken.h из Windows SDK, нужен только этот тип для WebView2.h
#pragma once
#ifndef GS_EVENT_TOKEN_DEFINED
#define GS_EVENT_TOKEN_DEFINED
typedef struct EventRegistrationToken { __int64 value; } EventRegistrationToken;
#endif
