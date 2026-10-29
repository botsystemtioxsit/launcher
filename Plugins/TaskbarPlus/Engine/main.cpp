// Движок Taskbar Plus для Windows 11: DLL, которая подключается к панели задач внутри Проводника
// и меняет её оформление через публичный интерфейс XAML Diagnostics (xamlom.h из Windows SDK).
//
// Как это устроено:
//  1. Плагин (C#) вешает на поток панели задач хук (SetWindowsHookEx) из этой DLL — так Windows сама загружает её в Проводник.
//  2. Хук один раз запускает поток, который просит XAML панели подключить к ней наш «анализатор» (TAP) —
//     InitializeXamlDiagnosticsEx из Windows.UI.Xaml.dll.
//  3. XAML создаёт наш COM-класс (DllGetClassObject → IObjectWithSite::SetSite) и отдаёт IXamlDiagnostics.
//  4. Мы подписываемся на изменения дерева элементов (AdviseVisualTreeChange) и меняем нужные элементы (styler.cpp).
// Настройки читаются из файла, состояние пишется в файл-статус; общение с плагином больше ничем не идёт.
#include "styler.h"
#include <atomic>
#include <new>
#include <thread>

// Случайный идентификатор нашего анализатора, чужие не используем.
static const CLSID CLSID_GhostShellTap = { 0x2d3ba304, 0xf503, 0x4ae7, { 0xb8, 0x7c, 0x6e, 0xb1, 0x87, 0xf0, 0x73, 0x57 } };

static HINSTANCE g_module = nullptr;
static std::atomic<bool> g_started{ false };
static std::atomic<bool> g_connected{ false };
static std::atomic<int> g_connectTries{ 0 };
static std::atomic<bool> g_stopWatcher{ false };

// ---------- статус для плагина ----------
static void WriteStatus(const wchar_t* state, const wchar_t* error)
{
    auto st = Styler::GetStats();
    wchar_t modulePath[MAX_PATH] = L"";
    GetModuleFileNameW(g_module, modulePath, MAX_PATH);
    std::wstring out;
    auto put = [&](const wchar_t* key, const std::wstring& val) { out += key; out += L"="; out += val; out += L"\r\n"; };
    auto num = [](long long v) { return std::to_wstring(v); };
    put(L"state", state);
    put(L"engine", GS_ENGINE_VERSION);
    put(L"pid", num(GetCurrentProcessId()));
    put(L"tick", num((long long)GetTickCount64()));
    put(L"module", modulePath);
    put(L"nodes", num(st.nodes));
    put(L"bgfill", num(st.bgFill));
    put(L"stroke", num(st.bgStroke));
    put(L"apps", num(st.apps));
    put(L"widgets", num(st.widgets));
    put(L"system", num(st.system));
    put(L"hosts", num(st.hosts));
    put(L"islands", num(st.islands));
    put(L"rects", num(st.rects));
    put(L"rectnames", st.rectNames);
    put(L"applied", num(st.applied ? 1 : 0));
    put(L"queue", num(st.queueOk ? 1 : 0));
    put(L"tries", num(g_connectTries.load()));
    put(L"error", error ? error : L"");
    WriteTextAtomic(DataDir() + L"\\taskbar-plus-engine-status.txt", ToUtf8(out));
}

// ---------- COM: подписка на дерево элементов ----------
class TreeCallback final : public IVisualTreeServiceCallback2
{
    LONG refs_ = 1;
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IVisualTreeServiceCallback) || riid == __uuidof(IVisualTreeServiceCallback2))
        {
            *ppv = static_cast<IVisualTreeServiceCallback2*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return (ULONG)InterlockedIncrement(&refs_); }
    ULONG STDMETHODCALLTYPE Release() override
    {
        ULONG n = (ULONG)InterlockedDecrement(&refs_);
        if (n == 0) delete this;
        return n;
    }
    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation, VisualElement element, VisualMutationType mutationType) override
    {
        return Styler::OnChange(relation, element, mutationType);
    }
    HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) override { return S_OK; }
};

class Tap final : public IObjectWithSite
{
    LONG refs_ = 1;
    IUnknown* site_ = nullptr;
public:
    ~Tap() { if (site_) site_->Release(); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IObjectWithSite))
        {
            *ppv = static_cast<IObjectWithSite*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return (ULONG)InterlockedIncrement(&refs_); }
    ULONG STDMETHODCALLTYPE Release() override
    {
        ULONG n = (ULONG)InterlockedDecrement(&refs_);
        if (n == 0) delete this;
        return n;
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID riid, void** ppv) override
    {
        if (!site_) { if (ppv) *ppv = nullptr; return E_FAIL; }
        return site_->QueryInterface(riid, ppv);
    }
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) override
    {
        try
        {
            if (site_) { site_->Release(); site_ = nullptr; }
            if (!site) return S_OK;
            site_ = site;
            site_->AddRef();

            IXamlDiagnostics* diag = nullptr;
            if (FAILED(site->QueryInterface(__uuidof(IXamlDiagnostics), (void**)&diag)) || !diag)
            {
                Log(L"SetSite: нет IXamlDiagnostics");
                return S_OK;
            }
            Styler::SetDiagnostics(diag); // держит свою ссылку
            diag->Release();

            // Подписку нельзя делать прямо здесь: этот вызов идёт из потока XAML, а подписка ждёт ответа от него же.
            IUnknown* keep = site_;
            keep->AddRef();
            std::thread([keep]()
            {
                CoInitializeEx(nullptr, COINIT_MULTITHREADED);
                // просим расширенный интерфейс; на старых Windows хватит и базового
                IVisualTreeService3* service3 = nullptr;
                HRESULT hr = keep->QueryInterface(__uuidof(IVisualTreeService3), (void**)&service3);
                IVisualTreeService* service = service3;
                if (FAILED(hr) || !service) hr = keep->QueryInterface(__uuidof(IVisualTreeService), (void**)&service);
                if (SUCCEEDED(hr) && service)
                {
                    auto* cb = new TreeCallback(); // ссылку держит служба XAML
                    hr = service->AdviseVisualTreeChange(cb);
                    cb->Release();
                    Log(L"подписка на дерево элементов: 0x%08X", (unsigned)hr);
                    if (SUCCEEDED(hr)) g_connected = true;
                    service->Release();
                }
                else Log(L"нет IVisualTreeService: 0x%08X", (unsigned)hr);
                keep->Release();
                CoUninitialize();
            }).detach();
        }
        catch (...) { Log(L"SetSite: неожиданная ошибка"); }
        return S_OK;
    }
};

class Factory final : public IClassFactory
{
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IClassFactory)) { *ppv = static_cast<IClassFactory*>(this); return S_OK; }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return 2; }   // живёт всё время работы процесса
    ULONG STDMETHODCALLTYPE Release() override { return 1; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        auto* tap = new (std::nothrow) Tap();
        if (!tap) return E_OUTOFMEMORY;
        HRESULT hr = tap->QueryInterface(riid, ppv);
        tap->Release();
        return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) override { return S_OK; }
};

static Factory g_factory;

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, LPVOID* ppv)
{
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (clsid != CLSID_GhostShellTap) return CLASS_E_CLASSNOTAVAILABLE;
    return g_factory.QueryInterface(riid, ppv);
}

STDAPI DllCanUnloadNow() { return S_FALSE; } // выгружать нельзя: код работает внутри Проводника

// ---------- запуск ----------
typedef HRESULT(WINAPI* InitXamlDiagFn)(LPCWSTR endPointName, DWORD pid, LPCWSTR wszDllXamlDiagnostics, LPCWSTR wszTAPDllName, CLSID tapClsid, LPCWSTR wszInitializationData);

static void ConnectToXaml()
{
    wchar_t modulePath[MAX_PATH];
    GetModuleFileNameW(g_module, modulePath, MAX_PATH);

    HMODULE xamlDll = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    auto init = xamlDll ? (InitXamlDiagFn)GetProcAddress(xamlDll, "InitializeXamlDiagnosticsEx") : nullptr;
    if (!init)
    {
        Log(L"нет InitializeXamlDiagnosticsEx (Windows.UI.Xaml.dll): эта версия Windows не подходит");
        WriteStatus(L"error", L"в этой версии Windows нет XAML Diagnostics");
        return;
    }

    // Панель может ещё грузиться: пробуем до 30 секунд. Каждое имя подключения годится только один раз.
    for (int i = 1; i <= 60 && !g_connected; ++i)
    {
        g_connectTries = i;
        wchar_t name[64];
        _snwprintf_s(name, _TRUNCATE, L"VisualDiagConnection%d", i);
        HRESULT hr = init(name, GetCurrentProcessId(), nullptr, modulePath, CLSID_GhostShellTap, nullptr);
        Log(L"InitializeXamlDiagnosticsEx(%s): 0x%08X", name, (unsigned)hr);
        if (SUCCEEDED(hr))
        {
            // подключение идёт асинхронно: ждём, пока придёт подписка
            for (int w = 0; w < 20 && !g_connected; ++w) Sleep(100);
            if (g_connected) break;
        }
        Sleep(500);
    }
}

static void WatcherLoop()
{
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    FILETIME lastTime{};
    bool haveLast = false;
    int lastDump = 0;
    ULONGLONG lastStatus = 0;
    StylerStats lastStats;

    while (!g_stopWatcher)
    {
        try
        {
            Config cfg;
            FILETIME t{};
            if (LoadConfig(cfg, t) && (!haveLast || CompareFileTime(&t, &lastTime) != 0))
            {
                lastTime = t;
                haveLast = true;
                if (cfg.dump != lastDump) { lastDump = cfg.dump; Styler::WriteTree(); }
                Styler::SetConfig(cfg);
            }
            auto now = GetTickCount64();
            auto stats = Styler::GetStats();
            const bool changed = stats.nodes != lastStats.nodes || stats.bgFill != lastStats.bgFill || stats.apps != lastStats.apps ||
                                 stats.widgets != lastStats.widgets || stats.system != lastStats.system || stats.rects != lastStats.rects ||
                                 stats.islands != lastStats.islands || stats.applied != lastStats.applied || stats.queueOk != lastStats.queueOk;
            if (changed || now - lastStatus > 3000)
            {
                WriteStatus(g_connected ? L"ready" : L"connecting", L"");
                lastStatus = now;
                lastStats = stats;
            }
        }
        catch (...) { Log(L"наблюдатель: неожиданная ошибка"); }
        Sleep(300);
    }
}

static DWORD WINAPI InitThread(LPVOID)
{
    try
    {
        // Пока работает Проводник, DLL должна оставаться в нём: на неё ссылается XAML.
        HMODULE self = nullptr;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN, (LPCWSTR)&InitThread, &self);

        // Вторая копия движка (старая версия, которая уже висит в Проводнике) не нужна: две сразу дрались бы за одни и те же элементы.
        HANDLE guard = CreateMutexW(nullptr, FALSE, L"Local\\GhostShellTaskbarPlus.XamlEngine");
        if (guard && GetLastError() == ERROR_ALREADY_EXISTS)
        {
            Log(L"движок уже загружен в этот Проводник (другая копия) — вторую не запускаю");
            WriteStatus(L"duplicate", L"в Проводнике уже работает другая версия движка; перезапустите панель задач");
            return 0;
        }

        Log(L"движок " GS_ENGINE_VERSION L" загружен в процесс %lu", GetCurrentProcessId());
        CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        std::thread(WatcherLoop).detach();
        ConnectToXaml();
        if (!g_connected)
        {
            Log(L"подключиться к XAML панели не удалось");
            WriteStatus(L"error", L"не удалось подключиться к XAML панели задач");
        }
    }
    catch (...) { Log(L"InitThread: неожиданная ошибка"); }
    return 0;
}

// Хук: Windows вызывает его внутри Проводника, когда окну панели приходит любое сообщение. Нужен только чтобы загрузить DLL.
extern "C" LRESULT CALLBACK GhostShellHookProc(int code, WPARAM wParam, LPARAM lParam)
{
    if (!g_started.exchange(true))
    {
        HANDLE t = CreateThread(nullptr, 0, InitThread, nullptr, 0, nullptr);
        if (t) CloseHandle(t);
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}
