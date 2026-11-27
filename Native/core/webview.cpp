#include "webview.hpp"
#include <shlobj.h>
#include <cwctype>
#include <string>
#include "WebView2.h"
#ifdef __MINGW32__
#include "wv2_uuids.inc"
#endif
#include "win.hpp"

namespace sp {
namespace {

// Универсальный обработчик событий WebView2: COM-объект, который вызывает std::function.
template <class Iface, class... A>
struct Cb final : Iface {
    std::function<HRESULT(A...)> fn;
    LONG ref = 1;
    explicit Cb(std::function<HRESULT(A...)> f) : fn(std::move(f)) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID r, void** o) override {
        if (IsEqualIID(r, IID_IUnknown) || IsEqualIID(r, __uuidof(Iface))) { *o = static_cast<Iface*>(this); AddRef(); return S_OK; }
        *o = nullptr; return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return (ULONG)InterlockedIncrement(&ref); }
    ULONG STDMETHODCALLTYPE Release() override { LONG r = InterlockedDecrement(&ref); if (!r) delete this; return (ULONG)r; }
    HRESULT STDMETHODCALLTYPE Invoke(A... a) override { return fn ? fn(a...) : S_OK; }
};

template <class T> struct Rel { void operator()(T* p) const { if (p) p->Release(); } };
template <class T> using Ptr = std::unique_ptr<T, Rel<T>>;

// host из адреса вида https://host[:port]/...
static std::wstring hostOf(const std::wstring& u) {
    size_t s = u.find(L"://"); if (s == std::wstring::npos) return L"";
    s += 3; size_t e = u.find_first_of(L"/:?#", s);
    std::wstring h = u.substr(s, e == std::wstring::npos ? std::wstring::npos : e - s);
    for (auto& c : h) c = (wchar_t)towlower(c);
    return h;
}
static bool hostMatches(const std::wstring& host, const std::wstring& allowed) {
    if (host == allowed) return true;
    return host.size() > allowed.size() && host.compare(host.size() - allowed.size(), allowed.size(), allowed) == 0 && host[host.size() - allowed.size() - 1] == L'.';
}

typedef HRESULT (STDAPICALLTYPE* CreateEnvFn)(PCWSTR, PCWSTR, ICoreWebView2EnvironmentOptions*, ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler*);
typedef HRESULT (STDAPICALLTYPE* VersionFn)(PCWSTR, LPWSTR*);

std::wstring loaderPath() {
    wchar_t buf[MAX_PATH * 2] = {};
    DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", buf, (DWORD)std::size(buf));
    std::wstring dir = (n ? std::wstring(buf, n) : L".") + L"\\GhostShell\\GhostStyle";
    gs::makeDirs(dir);
    return dir + L"\\WebView2Loader.dll";
}

// Достаём вшитый загрузчик и подгружаем.
HMODULE loadLoader() {
    static HMODULE mod = nullptr;
    if (mod) return mod;
    std::wstring path = loaderPath();
    HRSRC r = FindResourceW(nullptr, L"WV2LOADER", MAKEINTRESOURCEW(10) /* RT_RCDATA */);
    if (r) {
        HGLOBAL g = LoadResource(nullptr, r);
        DWORD sz = SizeofResource(nullptr, r);
        const void* data = LockResource(g);
        std::string cur;
        if (data && sz && (!gs::readFile(path, cur) || cur.size() != sz || memcmp(cur.data(), data, sz) != 0)) gs::writeFileAtomic(path, std::string((const char*)data, sz));
    }
    mod = LoadLibraryW(path.c_str());
    return mod;
}

}  // namespace

struct WebView::Impl {
    Ptr<ICoreWebView2Environment> env;
    Ptr<ICoreWebView2Controller> ctl;
    Ptr<ICoreWebView2> view;
    std::function<void(const std::string&)> onMessage;
    std::wstring error;
    bool ready = false;
    std::shared_ptr<bool> alive = std::make_shared<bool>(true);
    bool block = false, filterAdded = false;
    std::wstring allowedHost;
    EventRegistrationToken tokMsg{}, tokRes{}, tokNav{}, tokWin{}, tokPerm{}, tokDl{}, tokNavStart{};
    std::function<void()> onNavigated;
    bool navRestricted = false; std::wstring navHost;
};

WebView::WebView() : p_(new Impl) {}
WebView::~WebView() { close(); }

static std::wstring hrText(const wchar_t* what, HRESULT hr) { wchar_t b[96]; swprintf(b, 96, L"%ls: 0x%08lX", what, (unsigned long)hr); return b; }

bool WebView::available() {
    HMODULE m = loadLoader();
    if (!m) return false;
    auto fn = (VersionFn)(void*)GetProcAddress(m, "GetAvailableCoreWebView2BrowserVersionString");
    if (!fn) return false;
    LPWSTR ver = nullptr;
    HRESULT hr = fn(nullptr, &ver);
    bool ok = SUCCEEDED(hr) && ver && *ver;
    if (ver) CoTaskMemFree(ver);
    return ok;
}

std::wstring WebView::lastError() const { return p_->error; }
bool WebView::isReady() const { return p_->ready; }

void WebView::create(HWND host, const std::wstring& userData, std::function<void(bool)> ready) {
    Impl* p = p_.get();
    auto alive = p->alive;
    HMODULE m = loadLoader();
    auto fn = m ? (CreateEnvFn)(void*)GetProcAddress(m, "CreateCoreWebView2EnvironmentWithOptions") : nullptr;
    if (!fn) { p->error = L"нет WebView2Loader.dll"; if (ready) ready(false); return; }
    gs::makeDirs(userData);
    SetEnvironmentVariableW(L"WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", L"--autoplay-policy=no-user-gesture-required --disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection");
    auto onEnv = new Cb<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler, HRESULT, ICoreWebView2Environment*>(
        [p, host, ready, alive](HRESULT hr, ICoreWebView2Environment* env) -> HRESULT {
            if (!*alive) return S_OK;
            if (FAILED(hr) || !env) { p->error = hrText(L"среда WebView2", hr); if (ready) ready(false); return S_OK; }
            env->AddRef(); p->env.reset(env);
            auto onCtl = new Cb<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler, HRESULT, ICoreWebView2Controller*>(
                [p, ready, alive](HRESULT hr2, ICoreWebView2Controller* c) -> HRESULT {
                    if (!*alive) { if (c) c->Close(); return S_OK; }
                    if (FAILED(hr2) || !c) { p->error = hrText(L"браузер WebView2", hr2); if (ready) ready(false); return S_OK; }
                    c->AddRef(); p->ctl.reset(c);
                    ICoreWebView2* v = nullptr;
                    c->get_CoreWebView2(&v);
                    p->view.reset(v);
                    if (!v) { p->error = L"нет окна браузера"; if (ready) ready(false); return S_OK; }
                    ICoreWebView2Settings* st = nullptr;
                    if (SUCCEEDED(v->get_Settings(&st)) && st) {
                        st->put_AreDefaultContextMenusEnabled(FALSE); st->put_AreDevToolsEnabled(FALSE); st->put_IsStatusBarEnabled(FALSE);
                        st->put_IsZoomControlEnabled(FALSE); st->put_AreDefaultScriptDialogsEnabled(FALSE); st->put_IsBuiltInErrorPageEnabled(FALSE);
                        st->Release();
                    }
                    ICoreWebView2Controller2* c2 = nullptr;
                    if (SUCCEEDED(c->QueryInterface(IID_PPV_ARGS(&c2))) && c2) { COREWEBVIEW2_COLOR black{255, 0, 0, 0}; c2->put_DefaultBackgroundColor(black); c2->Release(); }
                    // страница не открывает окон, не просит разрешений, ничего не скачивает
                    v->add_NewWindowRequested(new Cb<ICoreWebView2NewWindowRequestedEventHandler, ICoreWebView2*, ICoreWebView2NewWindowRequestedEventArgs*>(
                        [](ICoreWebView2*, ICoreWebView2NewWindowRequestedEventArgs* a) -> HRESULT { a->put_Handled(TRUE); return S_OK; }), &p->tokWin);
                    v->add_PermissionRequested(new Cb<ICoreWebView2PermissionRequestedEventHandler, ICoreWebView2*, ICoreWebView2PermissionRequestedEventArgs*>(
                        [](ICoreWebView2*, ICoreWebView2PermissionRequestedEventArgs* a) -> HRESULT { a->put_State(COREWEBVIEW2_PERMISSION_STATE_DENY); return S_OK; }), &p->tokPerm);
                    v->add_WebMessageReceived(new Cb<ICoreWebView2WebMessageReceivedEventHandler, ICoreWebView2*, ICoreWebView2WebMessageReceivedEventArgs*>(
                        [p](ICoreWebView2*, ICoreWebView2WebMessageReceivedEventArgs* a) -> HRESULT {
                            LPWSTR js = nullptr;
                            if (SUCCEEDED(a->get_WebMessageAsJson(&js)) && js) { if (p->onMessage) p->onMessage(gs::narrow(js)); CoTaskMemFree(js); }
                            return S_OK;
                        }), &p->tokMsg);
                    ICoreWebView2_4* v4 = nullptr;
                    if (SUCCEEDED(v->QueryInterface(IID_PPV_ARGS(&v4))) && v4) {
                        v4->add_DownloadStarting(new Cb<ICoreWebView2DownloadStartingEventHandler, ICoreWebView2*, ICoreWebView2DownloadStartingEventArgs*>(
                            [](ICoreWebView2*, ICoreWebView2DownloadStartingEventArgs* a) -> HRESULT { a->put_Cancel(TRUE); return S_OK; }), &p->tokDl);
                        v4->Release();
                    }
                    p->ready = true;
                    if (ready) ready(true);
                    return S_OK;
                });
            p->env->CreateCoreWebView2Controller(host, onCtl);
            onCtl->Release();
            return S_OK;
        });
    HRESULT hr = fn(nullptr, userData.c_str(), nullptr, onEnv);
    onEnv->Release();
    if (FAILED(hr)) { p->error = hrText(L"запуск WebView2", hr); if (ready) ready(false); }
}

void WebView::navigate(const std::wstring& url) { if (p_->view) p_->view->Navigate(url.c_str()); }

void WebView::mapFolder(const std::wstring& hostName, const std::wstring& folder) {
    if (!p_->view) return;
    ICoreWebView2_3* v3 = nullptr;
    if (SUCCEEDED(p_->view->QueryInterface(IID_PPV_ARGS(&v3))) && v3) {
        v3->SetVirtualHostNameToFolderMapping(hostName.c_str(), folder.c_str(), COREWEBVIEW2_HOST_RESOURCE_ACCESS_KIND_ALLOW);
        v3->Release();
    }
}

void WebView::resize(int w, int h) { if (p_->ctl) { RECT r{0, 0, w, h}; p_->ctl->put_Bounds(r); } }
void WebView::setVisible(bool v) { if (p_->ctl) p_->ctl->put_IsVisible(v ? TRUE : FALSE); }

void WebView::setMuted(bool m) {
    if (!p_->view) return;
    ICoreWebView2_8* v8 = nullptr;
    if (SUCCEEDED(p_->view->QueryInterface(IID_PPV_ARGS(&v8))) && v8) { v8->put_IsMuted(m ? TRUE : FALSE); v8->Release(); }
}

void WebView::postJson(const std::string& utf8) { if (p_->view) p_->view->PostWebMessageAsJson(gs::widen(utf8).c_str()); }
void WebView::setOnMessage(std::function<void(const std::string&)> f) { p_->onMessage = std::move(f); }

void WebView::suspend(bool s) {
    if (!p_->view || !p_->ctl) return;
    ICoreWebView2_3* v3 = nullptr;
    if (FAILED(p_->view->QueryInterface(IID_PPV_ARGS(&v3))) || !v3) return;
    if (s) {
        p_->ctl->put_IsVisible(FALSE);
        auto h = new Cb<ICoreWebView2TrySuspendCompletedHandler, HRESULT, BOOL>([](HRESULT, BOOL) -> HRESULT { return S_OK; });
        v3->TrySuspend(h); h->Release();
    } else { v3->Resume(); p_->ctl->put_IsVisible(TRUE); }
    v3->Release();
}

// Запрет выхода в интернет: разрешены только виртуальный хост с папкой обоев и встроенные адреса.
void WebView::blockNetwork(bool block, const std::wstring& allowedHost) {
    Impl* p = p_.get();
    p->block = block; p->allowedHost = allowedHost;
    if (!p->view) return;
    if (!block || p->filterAdded) return;
    p->filterAdded = true;
    p->view->AddWebResourceRequestedFilter(L"*", COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL);
    p->view->add_WebResourceRequested(new Cb<ICoreWebView2WebResourceRequestedEventHandler, ICoreWebView2*, ICoreWebView2WebResourceRequestedEventArgs*>(
        [p](ICoreWebView2*, ICoreWebView2WebResourceRequestedEventArgs* a) -> HRESULT {
            if (!p->block) return S_OK;
            ICoreWebView2WebResourceRequest* req = nullptr;
            if (FAILED(a->get_Request(&req)) || !req) return S_OK;
            LPWSTR uri = nullptr; req->get_Uri(&uri);
            std::wstring u = uri ? uri : L"";
            if (uri) CoTaskMemFree(uri);
            req->Release();
            bool allowed = (u.rfind(L"https://", 0) == 0 && hostMatches(hostOf(u), p->allowedHost)) || u.rfind(L"data:", 0) == 0 || u.rfind(L"blob:", 0) == 0 || u.rfind(L"about:", 0) == 0;
            if (!allowed) {
                ICoreWebView2WebResourceResponse* resp = nullptr;
                if (SUCCEEDED(p->env->CreateWebResourceResponse(nullptr, 403, L"Blocked", L"", &resp)) && resp) { a->put_Response(resp); resp->Release(); }
            }
            return S_OK;
        }), &p->tokRes);
}

void WebView::addInitScript(const std::wstring& js, std::function<void()> done) {
    if (!p_->view) { if (done) done(); return; }
    auto h = new Cb<ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler, HRESULT, LPCWSTR>(
        [done, alive = p_->alive](HRESULT, LPCWSTR) -> HRESULT { if (*alive && done) done(); return S_OK; });
    p_->view->AddScriptToExecuteOnDocumentCreated(js.c_str(), h);
    h->Release();
}

void WebView::setOnNavigated(std::function<void()> f) {
    Impl* p = p_.get();
    p->onNavigated = std::move(f);
    if (!p->view || p->tokNav.value) return;
    p->view->add_NavigationCompleted(new Cb<ICoreWebView2NavigationCompletedEventHandler, ICoreWebView2*, ICoreWebView2NavigationCompletedEventArgs*>(
        [p](ICoreWebView2*, ICoreWebView2NavigationCompletedEventArgs*) -> HRESULT { if (p->onNavigated) p->onNavigated(); return S_OK; }), &p->tokNav);
}

void WebView::restrictTo(const std::wstring& hostName) {
    Impl* p = p_.get();
    p->navHost = hostName; p->navRestricted = true;
    if (!p->view || p->tokNavStart.value) return;
    p->view->add_NavigationStarting(new Cb<ICoreWebView2NavigationStartingEventHandler, ICoreWebView2*, ICoreWebView2NavigationStartingEventArgs*>(
        [p](ICoreWebView2*, ICoreWebView2NavigationStartingEventArgs* a) -> HRESULT {
            if (!p->navRestricted) return S_OK;
            LPWSTR uri = nullptr; a->get_Uri(&uri);
            std::wstring u = uri ? uri : L"";
            if (uri) CoTaskMemFree(uri);
            bool ok = (u.rfind(L"https://", 0) == 0 && hostMatches(hostOf(u), p->navHost)) || u.rfind(L"about:", 0) == 0 || u.rfind(L"data:", 0) == 0;
            if (!ok) a->put_Cancel(TRUE);
            return S_OK;
        }), &p->tokNavStart);
}

void WebView::execute(const std::wstring& js, std::function<void(const std::string&)> done) {
    if (!p_->view) { if (done) done(""); return; }
    auto h = new Cb<ICoreWebView2ExecuteScriptCompletedHandler, HRESULT, LPCWSTR>(
        [done, alive = p_->alive](HRESULT hr, LPCWSTR res) -> HRESULT { if (*alive && done) done(SUCCEEDED(hr) && res ? gs::narrow(res) : std::string()); return S_OK; });
    p_->view->ExecuteScript(js.c_str(), h);
    h->Release();
}

void WebView::close() {
    if (!p_) return;
    *p_->alive = false;
    if (p_->view) {
        p_->view->remove_WebMessageReceived(p_->tokMsg);
    }
    if (p_->ctl) p_->ctl->Close();
    p_->view.reset(); p_->ctl.reset(); p_->env.reset();
    p_->ready = false; p_->tokNav = {}; p_->tokNavStart = {}; p_->filterAdded = false;
}

}  // namespace sp
