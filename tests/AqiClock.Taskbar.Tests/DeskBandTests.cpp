#include <windows.h>
#include <shobjidl.h>
#include <cstdio>
#include <cstdlib>
#include "../../src/AqiClock.Taskbar/Snapshot.h"

static void Check(bool condition, const char* message) {
    if (!condition) { std::fprintf(stderr, "FAIL: %s (Win32 %lu)\n", message, GetLastError()); std::exit(1); }
}

class TestSite final : public IOleWindow, public IInputObjectSite {
    HWND parent;
public:
    explicit TestSite(HWND hwnd) : parent(hwnd) { }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (iid == IID_IUnknown || iid == IID_IOleWindow) *result = static_cast<IOleWindow*>(this);
        if (iid == IID_IInputObjectSite) *result = static_cast<IInputObjectSite*>(this);
        return *result ? S_OK : E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return 2; }
    ULONG STDMETHODCALLTYPE Release() override { return 1; }
    HRESULT STDMETHODCALLTYPE GetWindow(HWND* result) override { if (!result) return E_POINTER; *result = parent; return S_OK; }
    HRESULT STDMETHODCALLTYPE ContextSensitiveHelp(BOOL) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE OnFocusChangeIS(IUnknown*, BOOL) override { return S_OK; }
};

int wmain(int argc, wchar_t** argv) {
    Check(argc == 2, "DLL path argument");
    Check(SUCCEEDED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED)), "STA COM initialization");
    HMODULE dll = LoadLibraryW(argv[1]);
    Check(dll != nullptr, "load native DLL without external runtime dependencies");
    HRESULT(__stdcall* getClass)(REFCLSID, REFIID, void**) = nullptr;
    HRESULT(__stdcall* canUnload)() = nullptr;
    FARPROC getClassAddress = GetProcAddress(dll, "DllGetClassObject");
    FARPROC canUnloadAddress = GetProcAddress(dll, "DllCanUnloadNow");
    static_assert(sizeof(getClass) == sizeof(getClassAddress));
    std::memcpy(&getClass, &getClassAddress, sizeof(getClass));
    std::memcpy(&canUnload, &canUnloadAddress, sizeof(canUnload));
    Check(getClass && canUnload && canUnload() == S_OK, "COM exports and initial unload state");
    CLSID clsid;
    CLSIDFromString(L"{E3B157CF-B755-48CF-8715-91A40A517D62}", &clsid);
    IClassFactory* factory = nullptr;
    Check(SUCCEEDED(getClass(clsid, IID_PPV_ARGS(&factory))), "obtain class factory");
    Check(canUnload() == S_FALSE, "factory pins DLL");
    IDeskBand2* band = nullptr;
    Check(SUCCEEDED(factory->CreateInstance(nullptr, IID_PPV_ARGS(&band))), "create deskband");
    factory->Release();
    IObjectWithSite* objectSite = nullptr;
    Check(SUCCEEDED(band->QueryInterface(IID_PPV_ARGS(&objectSite))), "site interface");
    IPersistStream* persistence = nullptr;
    Check(SUCCEEDED(band->QueryInterface(IID_PPV_ARGS(&persistence))), "persistence interface");
    CLSID stored;
    Check(SUCCEEDED(persistence->GetClassID(&stored)) && stored == clsid, "Explorer can persist toolbar identity");
    ULARGE_INTEGER size;
    Check(SUCCEEDED(persistence->GetSizeMax(&size)) && size.QuadPart == 0, "empty persistence contract");
    persistence->Release();
    HWND parent = CreateWindowExW(0, L"STATIC", L"Taskbar test host", WS_OVERLAPPEDWINDOW, 0, 0, 440, 80, nullptr, nullptr, nullptr, nullptr);
    Check(parent != nullptr, "create isolated parent (not Explorer)");
    TestSite site(parent);
    Check(SUCCEEDED(objectSite->SetSite(static_cast<IOleWindow*>(&site))), "create child in host site");
    HWND child = nullptr;
    Check(SUCCEEDED(band->GetWindow(&child)) && IsWindow(child), "deskband exposes child window");
    DESKBANDINFO info = {};
    info.dwMask = DBIM_MINSIZE | DBIM_MAXSIZE | DBIM_ACTUAL | DBIM_MODEFLAGS | DBIM_TITLE | DBIM_BKCOLOR;
    Check(SUCCEEDED(band->GetBandInfo(1, DBIF_VIEWMODE_NORMAL, &info)) && info.ptMinSize.x >= 170 && !(info.dwMask & DBIM_TITLE), "horizontal sizing without duplicate title");
    Check(SUCCEEDED(band->GetBandInfo(1, DBIF_VIEWMODE_VERTICAL, &info)) && info.ptMinSize.y >= 48, "vertical taskbar sizing");
    SetWindowPos(child, nullptr, 0, 0, 240, 30, SWP_NOZORDER | SWP_NOACTIVATE);
    Check(SUCCEEDED(band->ShowDW(TRUE)), "show toolbar without activation");
    HWND focus = GetFocus();

    // A unique per-process mapping exercises the parser without touching any running app.
    std::wstring testMapping = SnapshotName() + L".test." + std::to_wstring(GetCurrentProcessId());
    HANDLE mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(ClockSnapshot), testMapping.c_str());
    Check(mapping != nullptr, "create test snapshot");
    auto view = static_cast<ClockSnapshot*>(MapViewOfFile(mapping, FILE_MAP_WRITE, 0, 0, sizeof(ClockSnapshot)));
    Check(view != nullptr, "map snapshot");
    FILETIME utc;
    GetSystemTimeAsFileTime(&utc);
    ULARGE_INTEGER value;
    value.LowPart = utc.dwLowDateTime; value.HighPart = utc.dwHighDateTime;
    *view = {};
    view->sequence = 2; view->version = 1; view->updatedUtc = static_cast<LONGLONG>(value.QuadPart);
    wcscpy_s(view->text, L"Lesson 3 | 12:34 left");
    wcscpy_s(view->tooltip, L"Next: Isha at 20:20");
    ClockSnapshot read = {};
    Check(ReadSnapshot(testMapping, read) && wcscmp(read.text, view->text) == 0, "snapshot protocol roundtrip");
    view->sequence = 3;
    Check(!ReadSnapshot(testMapping, read), "partial writes rejected");
    view->sequence = 4; view->updatedUtc -= 6LL * 10000000;
    Check(!ReadSnapshot(testMapping, read), "stopped or crashed publisher expires");
    view->updatedUtc += 6LL * 10000000; view->version = 2;
    Check(!ReadSnapshot(testMapping, read), "unknown protocol rejected");
    view->version = 1; view->text[255] = L'x';
    Check(!ReadSnapshot(testMapping, read), "unbounded text rejected");
    UnmapViewOfFile(view); CloseHandle(mapping);
    Check(!ReadSnapshot(testMapping, read), "closed publisher is unavailable");

    // Exercise the DLL binding only if no real app already owns this snapshot.
    HANDLE live = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(ClockSnapshot), SnapshotName().c_str());
    Check(live != nullptr, "open isolated display binding");
    if (GetLastError() != ERROR_ALREADY_EXISTS) {
        auto published = static_cast<ClockSnapshot*>(MapViewOfFile(live, FILE_MAP_WRITE, 0, 0, sizeof(ClockSnapshot)));
        Check(published != nullptr, "map display binding");
        *published = {};
        published->version = 1; published->updatedUtc = static_cast<LONGLONG>(value.QuadPart);
        wcscpy_s(published->text, L"Lesson 3 | 12:34 left");
        wcscpy_s(published->tooltip, L"Next: Isha at 20:20");
        MemoryBarrier(); published->sequence = 2;
        SendMessageW(child, WM_TIMER, 1, 0);
        wchar_t displayed[256];
        GetWindowTextW(child, displayed, 256);
        Check(wcscmp(displayed, published->text) == 0, "toolbar renders published lesson and countdown");
        Check(GetFocus() == focus, "live update preserves keyboard focus");
        published->sequence = 3; MemoryBarrier();
        wcscpy_s(published->text, L"AQI Clock | Sign in");
        wcscpy_s(published->tooltip, L"Click to sign in to AQI Clock.");
        MemoryBarrier(); published->sequence = 4;
        SendMessageW(child, WM_TIMER, 1, 0);
        GetWindowTextW(child, displayed, 256);
        Check(wcscmp(displayed, published->text) == 0, "sign-out replaces old lesson");
        published->updatedUtc -= 6LL * 10000000;
        SendMessageW(child, WM_TIMER, 1, 0);
        GetWindowTextW(child, displayed, 256);
        Check(wcscmp(displayed, L"AQI Clock - open app") == 0, "expired countdown becomes inactive");
        UnmapViewOfFile(published);
    } else std::puts("Live app owns snapshot; binding case skipped without modifying it.");
    CloseHandle(live);

    // A timer refresh must never steal focus or crash when no publisher exists.
    SendMessageW(child, WM_TIMER, 1, 0);
    Check(GetFocus() == focus, "refresh preserves keyboard focus");
    HDC dc = GetDC(child);
    SendMessageW(child, WM_PRINTCLIENT, reinterpret_cast<WPARAM>(dc), PRF_CLIENT);
    ReleaseDC(child, dc);
    Check(SUCCEEDED(band->CloseDW(0)) && !IsWindow(child), "close destroys child");
    Check(SUCCEEDED(objectSite->SetSite(static_cast<IOleWindow*>(&site))), "reattach after Explorer recreates band");
    Check(SUCCEEDED(objectSite->SetSite(nullptr)), "detach from site");
    objectSite->Release(); band->Release();
    Check(canUnload() == S_OK, "all COM objects and windows released");
    DestroyWindow(parent);
    FreeLibrary(dll); CoUninitialize();
    std::puts("PASS: native COM lifecycle, docking host, rendering, focus and snapshot validation");
    return 0;
}
