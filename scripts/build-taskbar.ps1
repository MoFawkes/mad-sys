param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$tools = Join-Path $root 'artifacts/tooling'
$zig = Join-Path $tools 'zig-x86_64-windows-0.14.1/zig.exe'
if (-not (Test-Path $zig)) {
    New-Item -ItemType Directory -Force $tools | Out-Null
    $archive = Join-Path $tools 'zig-0.14.1.zip'
    Invoke-WebRequest 'https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip' -OutFile $archive
    if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne '554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c') {
        throw 'Zig archive checksum mismatch.'
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $tools -Force
}
$out = Join-Path $root 'artifacts/taskbar'
New-Item -ItemType Directory -Force $out | Out-Null
$common = @('-target', 'x86_64-windows-gnu', '-std=c++17', '-O2', '-Wall', '-Wextra', '-Werror', '-DUNICODE', '-D_UNICODE', '-DWINVER=0x0a00')
& $zig c++ @common -shared (Join-Path $root 'src/AqiClock.Taskbar/DeskBand.cpp') (Join-Path $root 'src/AqiClock.Taskbar/exports.def') -o (Join-Path $out 'AqiClock.Taskbar.dll') -lole32 -luuid -lshell32 -ladvapi32 -lcomctl32 -luxtheme -lgdi32 -luser32
if ($LASTEXITCODE -ne 0) { throw 'Taskbar DLL build failed.' }
$toolchain = Split-Path $zig -Parent
$notices = @(
    'Zig 0.14.1', (Get-Content (Join-Path $toolchain 'LICENSE') -Raw),
    'LLVM libc++', (Get-Content (Join-Path $toolchain 'lib/libcxx/LICENSE.TXT') -Raw),
    'LLVM libc++abi', (Get-Content (Join-Path $toolchain 'lib/libcxxabi/LICENSE.TXT') -Raw),
    'MinGW-w64', (Get-Content (Join-Path $toolchain 'lib/libc/mingw/COPYING') -Raw)
)
$notices -join "`n`n" | Set-Content (Join-Path $out 'THIRD_PARTY_NOTICES.txt') -Encoding utf8
if ($Test) {
    & $zig c++ @common -municode (Join-Path $root 'tests/AqiClock.Taskbar.Tests/DeskBandTests.cpp') -o (Join-Path $out 'DeskBandTests.exe') -lole32 -luuid -luser32 -ladvapi32 -luxtheme -lgdi32
    if ($LASTEXITCODE -ne 0) { throw 'Taskbar test host build failed.' }
    # Match Explorer's common-controls v6 context so the test exercises real tooltip registration.
    Copy-Item -LiteralPath (Join-Path $root 'tests/AqiClock.Taskbar.Tests/DeskBandTests.exe.manifest') -Destination (Join-Path $out 'DeskBandTests.exe.manifest') -Force
    & (Join-Path $out 'DeskBandTests.exe') (Join-Path $out 'AqiClock.Taskbar.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Taskbar component tests failed.' }
}
