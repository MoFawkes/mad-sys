#pragma once
#include <windows.h>
#include <sddl.h>
#include <string>
#include <cstring>

// Version 1: little-endian UTF-16, identical to TaskbarSnapshotPublisher.cs.
// The sequence is odd during a write; the shell never displays a partial update.
struct ClockSnapshot {
    LONG sequence;
    DWORD version;
    LONGLONG updatedUtc;
    wchar_t text[256];
    wchar_t tooltip[768];
};
static_assert(sizeof(ClockSnapshot) == 2064);

inline std::wstring SnapshotName() {
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return {};
    BYTE buffer[1024];
    DWORD size = sizeof(buffer);
    std::wstring name;
    if (GetTokenInformation(token, TokenUser, buffer, size, &size)) {
        LPWSTR sid = nullptr;
        if (ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(buffer)->User.Sid, &sid)) {
            name = L"Local\\AqiClock.Taskbar.v1.";
            name += sid;
            LocalFree(sid);
        }
    }
    CloseHandle(token);
    return name;
}

inline bool ReadSnapshot(const std::wstring& name, ClockSnapshot& result) {
    if (name.empty()) return false;
    HANDLE mapping = OpenFileMappingW(FILE_MAP_READ, FALSE, name.c_str());
    if (!mapping) return false;
    auto view = static_cast<const ClockSnapshot*>(MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, sizeof(ClockSnapshot)));
    bool valid = false;
    if (view) {
        const LONG before = *reinterpret_cast<const volatile LONG*>(&view->sequence);
        MemoryBarrier();
        std::memcpy(&result, view, sizeof(result));
        MemoryBarrier();
        const LONG after = *reinterpret_cast<const volatile LONG*>(&view->sequence);
        FILETIME now;
        // Match DateTime.UtcNow's clock precision in the managed publisher.
        // The coarse API can lag behind a fresh timestamp and make age negative.
        GetSystemTimePreciseAsFileTime(&now);
        ULARGE_INTEGER utc;
        utc.LowPart = now.dwLowDateTime;
        utc.HighPart = now.dwHighDateTime;
        const LONGLONG age = static_cast<LONGLONG>(utc.QuadPart) - result.updatedUtc;
        valid = before != 0 && !(before & 1) && before == after && result.version == 1 &&
            age >= 0 && age <= 5LL * 10000000 && result.text[255] == 0 && result.tooltip[767] == 0;
        UnmapViewOfFile(view);
    }
    CloseHandle(mapping);
    return valid;
}
