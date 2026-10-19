#include <windows.h>
#include <windowsx.h>
#include <shobjidl.h>
#include <commctrl.h>
#include <uxtheme.h>
#include <shellapi.h>
#include <new>
#include <algorithm>
#include "Snapshot.h"

// Registration belongs to the app, under HKCU only. No managed runtime, network,
// cache database, or authentication code is loaded into Explorer.
static const CLSID BandId = {0xe3b157cf,0xb755,0x48cf,{0x87,0x15,0x91,0xa4,0x0a,0x51,0x7d,0x62}};
static HINSTANCE module;
static LONG objects = 0;
static const wchar_t WindowClass[] = L"AqiClock.Taskbar.v1";

class DeskBand final : public IDeskBand2, public IObjectWithSite, public IPersistStream, public IInputObject {
    LONG refs = 1;
    IUnknown* site = nullptr;
    IInputObjectSite* inputSite = nullptr;
    HWND window = nullptr;
    HWND tooltipWindow = nullptr;
    HFONT font = nullptr;
    HFONT countdownFont = nullptr;
    DWORD bandId = 0;
    bool vertical = false;
    bool composited = false;
    std::wstring mappingName;
    std::wstring text = L"AQI Clock - open app";
    std::wstring tooltip = L"Click to open AQI Clock";

    int Scale(int value) const { return MulDiv(value, window ? GetDpiForWindow(window) : 96, 96); }
    void UpdateFont() {
        if (font) DeleteObject(font);
        if (countdownFont) DeleteObject(countdownFont);
        NONCLIENTMETRICSW metrics = {};
        metrics.cbSize = sizeof(metrics);
        SystemParametersInfoForDpi(SPI_GETNONCLIENTMETRICS, sizeof(metrics), &metrics, 0, window ? GetDpiForWindow(window) : 96);
        font = CreateFontIndirectW(&metrics.lfMessageFont);
        metrics.lfMessageFont.lfWeight = FW_SEMIBOLD;
        countdownFont = CreateFontIndirectW(&metrics.lfMessageFont);
    }
    void NotifySizeChanged() {
        IOleCommandTarget* commands = nullptr;
        if (site && SUCCEEDED(site->QueryInterface(IID_PPV_ARGS(&commands)))) {
            VARIANTARG argument = {};
            argument.vt = VT_I4;
            argument.lVal = static_cast<LONG>(bandId);
            commands->Exec(&CGID_DeskBand, DBID_BANDINFOCHANGED, OLECMDEXECOPT_DODEFAULT, &argument, nullptr);
            commands->Release();
        }
    }
    void Refresh() {
        ClockSnapshot snapshot = {};
        std::wstring nextText = L"AQI Clock - open app";
        std::wstring nextTooltip = L"AQI Clock is not running. Click to open it.";
        if (ReadSnapshot(mappingName, snapshot)) {
            nextText = snapshot.text;
            nextTooltip = snapshot.tooltip;
        }
        if (text != nextText || tooltip != nextTooltip) {
            text = nextText;
            tooltip = nextTooltip;
            SetWindowTextW(window, text.c_str());
            TOOLINFOW info = {};
            info.cbSize = sizeof(info);
            info.hwnd = window;
            info.uId = reinterpret_cast<UINT_PTR>(window);
            info.lpszText = tooltip.data();
            SendMessageW(tooltipWindow, TTM_UPDATETIPTEXTW, 0, reinterpret_cast<LPARAM>(&info));
            InvalidateRect(window, nullptr, FALSE);
        }
    }
    void OpenApp() {
        HANDLE activation = OpenEventW(EVENT_MODIFY_STATE, FALSE, L"Local\\AqiClock.Activate");
        if (activation) {
            // The user clicked Explorer's band, so grant this app permission to
            // become foreground before signaling its asynchronous activation.
            // Without this handoff Windows can merely flash the taskbar button.
            DWORD processId = 0, processBytes = sizeof(processId);
            wchar_t registeredPath[32768];
            DWORD pathBytes = sizeof(registeredPath);
            const wchar_t* key = L"Software\\AQIClock\\TaskbarDisplay";
            if (RegGetValueW(HKEY_CURRENT_USER, key, L"AppProcessId", RRF_RT_REG_DWORD, nullptr, &processId, &processBytes) == ERROR_SUCCESS &&
                RegGetValueW(HKEY_CURRENT_USER, key, L"AppPath", RRF_RT_REG_SZ, nullptr, registeredPath, &pathBytes) == ERROR_SUCCESS) {
                DWORD appSession = 0, shellSession = 0;
                HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, processId);
                if (process) {
                    wchar_t runningPath[32768];
                    DWORD runningLength = ARRAYSIZE(runningPath);
                    const wchar_t* leaf = wcsrchr(registeredPath, L'\\');
                    if (leaf && _wcsicmp(leaf + 1, L"AqiClock.App.exe") == 0 &&
                        QueryFullProcessImageNameW(process, 0, runningPath, &runningLength) &&
                        ProcessIdToSessionId(processId, &appSession) && ProcessIdToSessionId(GetCurrentProcessId(), &shellSession) && appSession == shellSession) {
                        std::wstring installedPath(registeredPath, static_cast<size_t>(leaf - registeredPath + 1));
                        installedPath += L"current\\AqiClock.App.exe";
                        if (_wcsicmp(runningPath, registeredPath) == 0 || _wcsicmp(runningPath, installedPath.c_str()) == 0)
                            AllowSetForegroundWindow(processId);
                    }
                    CloseHandle(process);
                }
            }
            SetEvent(activation); CloseHandle(activation); return;
        }
        wchar_t path[32768];
        DWORD bytes = sizeof(path);
        if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\AQIClock\\TaskbarDisplay", L"AppPath",
            RRF_RT_REG_SZ, nullptr, path, &bytes) == ERROR_SUCCESS) {
            // Only launch the app executable, never a shell command or snapshot text.
            const wchar_t* leaf = wcsrchr(path, L'\\');
            if (leaf && _wcsicmp(leaf + 1, L"AqiClock.App.exe") == 0 && GetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES)
                ShellExecuteW(window, L"open", path, L"--show", nullptr, SW_SHOWNORMAL);
        }
    }
    void FocusChanged(BOOL focused) {
        if (inputSite) inputSite->OnFocusChangeIS(static_cast<IDeskBand2*>(this), focused);
        InvalidateRect(window, nullptr, FALSE);
    }
    bool Paint(HDC dc, HPAINTBUFFER buffer, bool allowTransparency) {
        if (!dc) return false;
        RECT rect;
        GetClientRect(window, &rect);
        if (rect.right <= 0 || rect.bottom <= 0) return true;
        HIGHCONTRASTW contrast = {};
        contrast.cbSize = sizeof(contrast);
        SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(contrast), &contrast, 0);
        DWORD light = 0, size = sizeof(light);
        RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
            L"SystemUsesLightTheme", RRF_RT_REG_DWORD, nullptr, &light, &size);
        bool highContrast = (contrast.dwFlags & HCF_HIGHCONTRASTON) != 0;
        COLORREF background = highContrast ? GetSysColor(COLOR_BTNFACE) : light ? RGB(240,240,240) : RGB(32,32,32);
        COLORREF foreground = highContrast ? GetSysColor(COLOR_BTNTEXT) : light ? RGB(24,24,24) : RGB(250,250,250);
        HTHEME theme = allowTransparency && composited && !highContrast && buffer ? OpenThemeData(window, L"BUTTON") : nullptr;
        if (!theme) {
            HBRUSH brush = CreateSolidBrush(background);
            FillRect(dc, &rect, brush);
            DeleteObject(brush);
            if (!highContrast) DrawThemeParentBackground(window, dc, &rect);
        }
        bool rendered = true;
        auto drawText = [&](const wchar_t* value, int length, RECT area, UINT flags, bool emphasis) {
            SelectObject(dc, emphasis && countdownFont ? countdownFont : font ? font : GetStockObject(DEFAULT_GUI_FONT));
            if (area.right <= area.left || area.bottom <= area.top) return;
            if (theme) {
                DTTOPTS options = {};
                options.dwSize = sizeof(options);
                options.dwFlags = DTT_COMPOSITED | DTT_TEXTCOLOR;
                options.crText = foreground;
                if (FAILED(DrawThemeTextEx(theme, dc, 0, 0, value, length, flags, &area, &options))) rendered = false;
            } else DrawTextW(dc, value, length, &area, flags);
        };
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, foreground);
        HGDIOBJ oldFont = SelectObject(dc, font ? font : GetStockObject(DEFAULT_GUI_FONT));
        InflateRect(&rect, -Scale(8), 0);
        // Explorer can stretch the last toolbar across unused taskbar space.
        // Keep the clock together beside the tray instead of spreading its
        // label and countdown across that reserved area.
        if (!vertical) rect.left = std::max(rect.left, rect.right - Scale(194));
        const auto divider = text.rfind(L" | ");
        if (divider != std::wstring::npos && vertical) {
            RECT top = rect, bottom = rect;
            top.bottom = rect.bottom / 2;
            bottom.top = top.bottom;
            drawText(text.c_str(), static_cast<int>(divider), top, DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS | DT_NOPREFIX, false);
            drawText(text.c_str() + divider + 3, -1, bottom, DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS | DT_NOPREFIX, true);
        } else if (divider != std::wstring::npos) {
            const wchar_t* remaining = text.c_str() + divider + 3;
            SIZE extent = {};
            SelectObject(dc, countdownFont ? countdownFont : font ? font : GetStockObject(DEFAULT_GUI_FONT));
            GetTextExtentPoint32W(dc, remaining, static_cast<int>(wcslen(remaining)), &extent);
            RECT countdown = rect;
            countdown.left = std::max(rect.left, rect.right - extent.cx);
            drawText(remaining, -1, countdown, DT_SINGLELINE | DT_VCENTER | DT_RIGHT | DT_END_ELLIPSIS | DT_NOPREFIX, true);
            rect.right = std::max(rect.left, countdown.left - Scale(10));
            drawText(text.c_str(), static_cast<int>(divider), rect, DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS | DT_NOPREFIX, false);
        } else drawText(text.c_str(), -1, rect, DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS | DT_NOPREFIX | (vertical ? 0 : DT_RIGHT), false);
        SelectObject(dc, oldFont);
        if (GetFocus() == window) {
            GetClientRect(window, &rect); InflateRect(&rect,-2,-2);
            HBRUSH brush = CreateSolidBrush(foreground);
            FrameRect(dc, &rect, brush); DeleteObject(brush);
            if (theme) {
                RECT edges[] = {{rect.left,rect.top,rect.right,rect.top+1}, {rect.left,rect.bottom-1,rect.right,rect.bottom},
                    {rect.left,rect.top,rect.left+1,rect.bottom}, {rect.right-1,rect.top,rect.right,rect.bottom}};
                for (const RECT& edge : edges) BufferedPaintSetAlpha(buffer, &edge, 255);
            }
        }
        if (theme) CloseThemeData(theme);
        else if (buffer) BufferedPaintSetAlpha(buffer, nullptr, 255);
        return rendered;
    }
    void Render(HDC destination) {
        if (!destination) return;
        // Clear ARGB to transparent, then draw premultiplied themed glyphs.
        // Explorer supplies its own taskbar tint/transparency behind the band.
        if (SUCCEEDED(BufferedPaintInit())) {
            RECT rect;
            GetClientRect(window, &rect);
            HDC bufferedDc = nullptr;
            HPAINTBUFFER buffer = BeginBufferedPaint(destination, &rect, BPBF_TOPDOWNDIB, nullptr, &bufferedDc);
            if (buffer) {
                BufferedPaintClear(buffer, nullptr);
                if (!Paint(bufferedDc, buffer, true)) {
                    BufferedPaintClear(buffer, nullptr);
                    Paint(bufferedDc, buffer, false);
                }
                EndBufferedPaint(buffer, TRUE);
            } else Paint(destination, nullptr, false);
            BufferedPaintUnInit();
        } else Paint(destination, nullptr, false);
    }
    static LRESULT CALLBACK WindowProc(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam) {
        auto self = reinterpret_cast<DeskBand*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
        if (message == WM_NCCREATE) {
            self = static_cast<DeskBand*>(reinterpret_cast<CREATESTRUCTW*>(lparam)->lpCreateParams);
            self->window = hwnd;
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        }
        if (!self) return DefWindowProcW(hwnd, message, wparam, lparam);
        // No C++ exception may escape a window procedure hosted by Explorer.
        try { switch (message) {
        case WM_TIMER: self->Refresh(); return 0;
        case WM_PAINT: { PAINTSTRUCT ps; HDC dc = BeginPaint(hwnd, &ps); self->Render(dc); EndPaint(hwnd, &ps); return 0; }
        case WM_PRINTCLIENT: self->Render(reinterpret_cast<HDC>(wparam)); return 0;
        case WM_ERASEBKGND: return 1;
        case WM_MOUSEACTIVATE: return MA_NOACTIVATE;
        case WM_LBUTTONUP: self->OpenApp(); return 0;
        case WM_KEYDOWN: if (wparam == VK_RETURN || wparam == VK_SPACE) { self->OpenApp(); return 0; } break;
        case WM_SETFOCUS: self->FocusChanged(TRUE); return 0;
        case WM_KILLFOCUS: self->FocusChanged(FALSE); return 0;
        case WM_DPICHANGED_AFTERPARENT: self->UpdateFont(); self->NotifySizeChanged(); InvalidateRect(hwnd, nullptr, TRUE); return 0;
        case WM_SETTINGCHANGE: case WM_THEMECHANGED:
            self->UpdateFont(); InvalidateRect(hwnd, nullptr, TRUE); return 0;
        case WM_NCDESTROY: SetWindowLongPtrW(hwnd, GWLP_USERDATA, 0); self->window = nullptr; break;
        } } catch (...) { return 0; }
        return DefWindowProcW(hwnd, message, wparam, lparam);
    }
    void Destroy() {
        if (tooltipWindow) { DestroyWindow(tooltipWindow); tooltipWindow = nullptr; }
        if (window) DestroyWindow(window);
        if (font) { DeleteObject(font); font = nullptr; }
        if (countdownFont) { DeleteObject(countdownFont); countdownFont = nullptr; }
    }
public:
    DeskBand() : mappingName(SnapshotName()) { InterlockedIncrement(&objects); }
    ~DeskBand() {
        Destroy();
        if (inputSite) inputSite->Release();
        if (site) site->Release();
        InterlockedDecrement(&objects);
    }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (iid == IID_IUnknown || iid == IID_IOleWindow || iid == IID_IDockingWindow || iid == IID_IDeskBand || iid == IID_IDeskBand2)
            *value = static_cast<IDeskBand2*>(this);
        else if (iid == IID_IObjectWithSite) *value = static_cast<IObjectWithSite*>(this);
        else if (iid == IID_IPersist || iid == IID_IPersistStream) *value = static_cast<IPersistStream*>(this);
        else if (iid == IID_IInputObject) *value = static_cast<IInputObject*>(this);
        if (!*value) return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override { ULONG count = InterlockedDecrement(&refs); if (!count) delete this; return count; }
    HRESULT STDMETHODCALLTYPE GetWindow(HWND* value) override { if (!value) return E_POINTER; *value = window; return S_OK; }
    HRESULT STDMETHODCALLTYPE ContextSensitiveHelp(BOOL) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE ShowDW(BOOL show) override { if (window) ShowWindow(window, show ? SW_SHOWNA : SW_HIDE); return S_OK; }
    HRESULT STDMETHODCALLTYPE CloseDW(DWORD) override { Destroy(); return S_OK; }
    HRESULT STDMETHODCALLTYPE ResizeBorderDW(const RECT*, IUnknown*, BOOL) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetBandInfo(DWORD id, DWORD mode, DESKBANDINFO* info) override {
        if (!info) return E_POINTER;
        bandId = id;
        vertical = (mode & DBIF_VIEWMODE_VERTICAL) != 0;
        if (info->dwMask & DBIM_MINSIZE) info->ptMinSize = {Scale(vertical ? 40 : 120), Scale(vertical ? 48 : 24)};
        if (info->dwMask & DBIM_MAXSIZE) info->ptMaxSize = {-1, -1};
        if (info->dwMask & DBIM_INTEGRAL) info->ptIntegral = {1, 1};
        if (info->dwMask & DBIM_ACTUAL) info->ptActual = {Scale(vertical ? 80 : 210), Scale(vertical ? 48 : 30)};
        if (info->dwMask & DBIM_MODEFLAGS) info->dwModeFlags = DBIMF_NORMAL | DBIMF_VARIABLEHEIGHT | DBIMF_NOMARGINS;
        info->dwMask &= ~(DBIM_TITLE | DBIM_BKCOLOR);
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE CanRenderComposited(BOOL* value) override { if (!value) return E_POINTER; *value = TRUE; return S_OK; }
    HRESULT STDMETHODCALLTYPE SetCompositionState(BOOL value) override {
        composited = value != FALSE;
        if (window) InvalidateRect(window, nullptr, TRUE);
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetCompositionState(BOOL* value) override { if (!value) return E_POINTER; *value = composited; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetClassID(CLSID* value) override { if (!value) return E_POINTER; *value = BandId; return S_OK; }
    HRESULT STDMETHODCALLTYPE IsDirty() override { return S_FALSE; }
    HRESULT STDMETHODCALLTYPE Load(IStream*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE Save(IStream*, BOOL) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE GetSizeMax(ULARGE_INTEGER* size) override { if (!size) return E_POINTER; size->QuadPart = 0; return S_OK; }
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* value) override {
        Destroy();
        if (inputSite) { inputSite->Release(); inputSite = nullptr; }
        if (site) { site->Release(); site = nullptr; }
        if (!value) return S_OK;
        IOleWindow* parentSite = nullptr;
        HRESULT result = value->QueryInterface(IID_PPV_ARGS(&parentSite));
        if (FAILED(result)) return result;
        HWND parent = nullptr;
        result = parentSite->GetWindow(&parent);
        parentSite->Release();
        if (FAILED(result) || !IsWindow(parent)) return E_FAIL;
        WNDCLASSW type = {};
        type.lpfnWndProc = WindowProc;
        type.hInstance = module;
        type.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        type.lpszClassName = WindowClass;
        if (!RegisterClassW(&type) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return HRESULT_FROM_WIN32(GetLastError());
        if (!CreateWindowExW(0, WindowClass, text.c_str(), WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS | WS_TABSTOP,
            0, 0, Scale(210), Scale(30), parent, nullptr, module, this)) return HRESULT_FROM_WIN32(GetLastError());
        site = value; site->AddRef();
        site->QueryInterface(IID_PPV_ARGS(&inputSite));
        UpdateFont();
        INITCOMMONCONTROLSEX controls = {sizeof(controls), ICC_WIN95_CLASSES};
        InitCommonControlsEx(&controls);
        tooltipWindow = CreateWindowExW(WS_EX_TOPMOST, TOOLTIPS_CLASSW, nullptr, WS_POPUP | TTS_ALWAYSTIP | TTS_NOPREFIX,
            CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, window, nullptr, module, nullptr);
        TOOLINFOW info = {};
        info.cbSize = sizeof(info); info.uFlags = TTF_IDISHWND | TTF_SUBCLASS;
        info.hwnd = window; info.uId = reinterpret_cast<UINT_PTR>(window); info.lpszText = tooltip.data();
        SendMessageW(tooltipWindow, TTM_ADDTOOLW, 0, reinterpret_cast<LPARAM>(&info));
        SendMessageW(tooltipWindow, TTM_SETMAXTIPWIDTH, 0, Scale(480));
        Refresh();
        if (!SetTimer(window, 1, 1000, nullptr)) { SetSite(nullptr); return E_FAIL; }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid, void** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        return site ? site->QueryInterface(iid, value) : E_FAIL;
    }
    HRESULT STDMETHODCALLTYPE UIActivateIO(BOOL activate, MSG*) override { if (activate && window) SetFocus(window); return S_OK; }
    HRESULT STDMETHODCALLTYPE HasFocusIO() override { return window && GetFocus() == window ? S_OK : S_FALSE; }
    HRESULT STDMETHODCALLTYPE TranslateAcceleratorIO(MSG* message) override {
        if (message && message->message == WM_KEYDOWN && (message->wParam == VK_RETURN || message->wParam == VK_SPACE)) { OpenApp(); return S_OK; }
        return S_FALSE;
    }
};

class Factory final : public IClassFactory {
    LONG refs = 1;
public:
    Factory() { InterlockedIncrement(&objects); }
    ~Factory() { InterlockedDecrement(&objects); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
        *value = static_cast<IClassFactory*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override { ULONG count = InterlockedDecrement(&refs); if (!count) delete this; return count; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        DeskBand* instance = nullptr;
        try { instance = new(std::nothrow) DeskBand(); }
        catch (...) { return E_OUTOFMEMORY; }
        if (!instance) return E_OUTOFMEMORY;
        HRESULT result = instance->QueryInterface(iid, value);
        instance->Release();
        return result;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override { if (lock) InterlockedIncrement(&objects); else InterlockedDecrement(&objects); return S_OK; }
};

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, void** value) {
    if (!value) return E_POINTER;
    *value = nullptr;
    if (clsid != BandId) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = new(std::nothrow) Factory();
    if (!factory) return E_OUTOFMEMORY;
    HRESULT result = factory->QueryInterface(iid, value);
    factory->Release();
    return result;
}
extern "C" HRESULT __stdcall DllCanUnloadNow() {
    if (objects != 0) return S_FALSE;
    // Remove our class outside DllMain's loader lock before the module is unloaded.
    UnregisterClassW(WindowClass, module);
    return S_OK;
}
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) { module = instance; DisableThreadLibraryCalls(instance); }
    return TRUE;
}
