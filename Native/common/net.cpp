#include "net.hpp"
#include <windows.h>
#include <winhttp.h>
#include "win.hpp"

namespace gs {

bool httpFetch(const char* method, const std::string& url, const std::string& reqBody, const std::string& contentType,
               std::string* body, const std::wstring* file, long long maxBytes, std::string& error) {
    std::wstring wurl = widen(url);
    URL_COMPONENTSW uc{}; uc.dwStructSize = sizeof uc;
    wchar_t host[256], path[2048];
    uc.lpszHostName = host; uc.dwHostNameLength = 256; uc.lpszUrlPath = path; uc.dwUrlPathLength = 2048;
    if (!WinHttpCrackUrl(wurl.c_str(), 0, 0, &uc) || uc.nScheme != INTERNET_SCHEME_HTTPS) { error = "адрес не подходит"; return false; }
    HINTERNET s = WinHttpOpen(L"GhostStyle/1", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, nullptr, nullptr, 0);
    if (!s) { error = "нет связи"; return false; }
    WinHttpSetTimeouts(s, 10000, 10000, 20000, 30000);
    HINTERNET c = WinHttpConnect(s, host, uc.nPort, 0);
    HINTERNET r = c ? WinHttpOpenRequest(c, widen(method).c_str(), path, nullptr, nullptr, nullptr, WINHTTP_FLAG_SECURE) : nullptr;
    bool ok = false;
    HANDLE fh = INVALID_HANDLE_VALUE;
    std::wstring hdr = contentType.empty() ? L"" : L"Content-Type: " + widen(contentType);
    if (r && WinHttpSendRequest(r, hdr.empty() ? WINHTTP_NO_ADDITIONAL_HEADERS : hdr.c_str(), hdr.empty() ? 0 : (DWORD)-1L, reqBody.empty() ? nullptr : (LPVOID)reqBody.data(), (DWORD)reqBody.size(), (DWORD)reqBody.size(), 0) && WinHttpReceiveResponse(r, nullptr)) {
        DWORD code = 0, sz = sizeof code;
        WinHttpQueryHeaders(r, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, nullptr, &code, &sz, nullptr);
        if (code < 200 || code > 299) error = "ответ сервера " + std::to_string(code);
        else {
            if (file) fh = CreateFileW(file->c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
            long long total = 0; ok = true;
            for (;;) {
                DWORD avail = 0;
                if (!WinHttpQueryDataAvailable(r, &avail)) { ok = false; error = "обрыв связи"; break; }
                if (!avail) break;
                std::string buf(avail, '\0'); DWORD got = 0;
                if (!WinHttpReadData(r, buf.data(), avail, &got)) { ok = false; error = "обрыв связи"; break; }
                total += got;
                if (total > maxBytes) { ok = false; error = "файл слишком большой"; break; }
                if (body) body->append(buf.data(), got);
                else if (fh != INVALID_HANDLE_VALUE) { DWORD w; WriteFile(fh, buf.data(), got, &w, nullptr); }
            }
        }
    } else error = "нет связи";
    if (fh != INVALID_HANDLE_VALUE) CloseHandle(fh);
    if (r) WinHttpCloseHandle(r);
    if (c) WinHttpCloseHandle(c);
    WinHttpCloseHandle(s);
    return ok;
}

}  // namespace gs
