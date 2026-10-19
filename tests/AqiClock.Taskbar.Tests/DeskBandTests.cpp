#include <windows.h>
#include <shobjidl.h>
#include <uxtheme.h>
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
    Check(SUCCEEDED(band->GetBandInfo(1, DBIF_VIEWMODE_NORMAL, &info)) && info.ptMinSize.x >= 120 && info.ptMinSize.x < info.ptActual.x && !(info.dwMask & DBIM_TITLE), "compact resizable horizontal sizing without duplicate title");
    Check(SUCCEEDED(band->GetBandInfo(1, DBIF_VIEWMODE_VERTICAL, &info)) && info.ptMinSize.y >= 48, "vertical taskbar sizing");
    Check(SUCCEEDED(band->GetBandInfo(1, DBIF_VIEWMODE_NORMAL, &info)), "return to horizontal rendering");
    SetWindowPos(child, nullptr, 0, 0, 240, 30, SWP_NOZORDER | SWP_NOACTIVATE);
    Check(SUCCEEDED(band->ShowDW(TRUE)), "show toolbar without activation");
    HWND focus = GetFocus();

    // Own the activation event only when no live app is using it. Keyboard
    // tests must never activate an unrelated app or launch the installed copy.
    HANDLE activation = CreateEventW(nullptr, TRUE, FALSE, L"Local\\AqiClock.Activate");
    Check(activation != nullptr, "create keyboard activation target");
    if (GetLastError() != ERROR_ALREADY_EXISTS) {
        IInputObject* input = nullptr;
        Check(SUCCEEDED(band->QueryInterface(IID_PPV_ARGS(&input))), "keyboard input interface");
        MSG key = {};
        key.hwnd = child; key.message = WM_KEYDOWN; key.wParam = VK_TAB;
        Check(input->TranslateAcceleratorIO(&key) == S_FALSE && WaitForSingleObject(activation, 0) == WAIT_TIMEOUT,
            "unhandled keys remain available to Explorer");
        for (WPARAM command : {static_cast<WPARAM>(VK_RETURN), static_cast<WPARAM>(VK_SPACE)}) {
            key.wParam = command;
            ResetEvent(activation);
            Check(input->TranslateAcceleratorIO(&key) == S_OK && WaitForSingleObject(activation, 0) == WAIT_OBJECT_0,
                "Explorer keyboard accelerator activates existing app");
            ResetEvent(activation);
            SendMessageW(child, WM_KEYDOWN, command, 0);
            Check(WaitForSingleObject(activation, 0) == WAIT_OBJECT_0, "focused toolbar keyboard activates existing app");
        }
        ResetEvent(activation);
        key.message = WM_KEYUP;
        Check(input->TranslateAcceleratorIO(&key) == S_FALSE && WaitForSingleObject(activation, 0) == WAIT_TIMEOUT,
            "key release does not reactivate app");
        Check(input->TranslateAcceleratorIO(nullptr) == S_FALSE, "empty keyboard message is ignored");
        input->Release();
    } else std::puts("Live app owns activation event; keyboard cases skipped without signaling it.");
    CloseHandle(activation);

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
    // DateTime.UtcNow in the managed publisher uses the precise system clock.
    // A coarse reader can lag behind it and reject a fresh packet as future-dated.
    for (int sample = 0; sample < 1000; ++sample) {
        GetSystemTimePreciseAsFileTime(&utc);
        value.LowPart = utc.dwLowDateTime; value.HighPart = utc.dwHighDateTime;
        view->updatedUtc = static_cast<LONGLONG>(value.QuadPart);
        Check(ReadSnapshot(testMapping, read), "fresh precise-clock publisher timestamps are accepted");
    }
    view->updatedUtc += 1LL * 10000000;
    Check(!ReadSnapshot(testMapping, read), "future-dated snapshots remain rejected");
    view->updatedUtc -= 1LL * 10000000;
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
    BOOL composited = FALSE;
    Check(SUCCEEDED(band->CanRenderComposited(&composited)) && composited, "supports Explorer's composited taskbar without compatibility warning");
    Check(SUCCEEDED(band->SetCompositionState(TRUE)), "enable composited rendering");
    composited = FALSE;
    Check(SUCCEEDED(band->GetCompositionState(&composited)) && composited, "composition state roundtrip");
    SendMessageW(child, WM_PRINTCLIENT, reinterpret_cast<WPARAM>(dc), PRF_CLIENT);
    // Inspect actual output pixels: a solid rectangle must not regress the
    // taskbar's tint, and transparent glyph rendering must remain visible.
    constexpr int captureWidth = 440;
    constexpr int captureHeight = 30;
    SetWindowPos(child, nullptr, 0, 0, captureWidth, captureHeight, SWP_NOZORDER | SWP_NOACTIVATE);
    BITMAPINFO bitmapInfo = {};
    bitmapInfo.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bitmapInfo.bmiHeader.biWidth = captureWidth;
    bitmapInfo.bmiHeader.biHeight = -captureHeight;
    bitmapInfo.bmiHeader.biPlanes = 1;
    bitmapInfo.bmiHeader.biBitCount = 32;
    bitmapInfo.bmiHeader.biCompression = BI_RGB;
    void* pixels = nullptr;
    HBITMAP bitmap = CreateDIBSection(dc, &bitmapInfo, DIB_RGB_COLORS, &pixels, nullptr, 0);
    HDC capture = CreateCompatibleDC(dc);
    Check(bitmap && pixels && capture, "create ARGB rendering capture");
    HGDIOBJ previousBitmap = SelectObject(capture, bitmap);
    std::memset(pixels, 0, captureWidth * captureHeight * 4);
    SendMessageW(child, WM_PRINTCLIENT, reinterpret_cast<WPARAM>(capture), PRF_CLIENT);
    GdiFlush();
    HIGHCONTRASTW contrast = {};
    contrast.cbSize = sizeof(contrast);
    SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(contrast), &contrast, 0);
    if (IsThemeActive() && !(contrast.dwFlags & HCF_HIGHCONTRASTON)) {
        const auto output = static_cast<const DWORD*>(pixels);
        Check((output[0] >> 24) == 0, "composited background preserves transparent corners");
        bool visibleGlyphs = false;
        for (int pixel = 0; pixel < captureWidth * captureHeight; ++pixel) visibleGlyphs |= (output[pixel] >> 24) != 0;
        Check(visibleGlyphs, "composited text contains visible alpha");
        const int unusedWidth = captureWidth - MulDiv(210, GetDpiForWindow(child), 96);
        for (int y = 0; y < captureHeight; ++y) for (int x = 0; x < unusedWidth; ++x)
            Check((output[y * captureWidth + x] >> 24) == 0, "wide toolbar keeps clock at its right edge");
    } else std::puts("Themed transparency case unavailable; opaque fallback still exercised.");
    Check(SUCCEEDED(band->SetCompositionState(FALSE)), "return to opaque rendering");
    std::memset(pixels, 0, captureWidth * captureHeight * 4);
    SendMessageW(child, WM_PRINTCLIENT, reinterpret_cast<WPARAM>(capture), PRF_CLIENT);
    GdiFlush();
    Check((static_cast<const DWORD*>(pixels)[0] >> 24) == 255, "non-composited background remains opaque");
    SelectObject(capture, previousBitmap);
    DeleteObject(bitmap); DeleteDC(capture);
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
