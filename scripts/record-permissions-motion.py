"""Record the rendered WinUI fixture window; no generated or interpolated frames."""
import ctypes
from ctypes import wintypes
import hashlib
import json
from pathlib import Path
import sys
import time
import uuid
from PIL import Image

evidence = Path(sys.argv[1]).resolve()
state = json.loads((evidence / "window.json").read_text(encoding="utf-8-sig"))
hwnd = wintypes.HWND(state["hwnd"])
user32 = ctypes.WinDLL("user32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
# PrintWindow renders physical pixels; keep the capture bounds in the same DPI space.
user32.SetThreadDpiAwarenessContext.argtypes = [wintypes.HANDLE]
user32.SetThreadDpiAwarenessContext.restype = wintypes.HANDLE
user32.SetThreadDpiAwarenessContext(ctypes.c_void_p(-4))

class Rect(ctypes.Structure):
    _fields_ = [(name, wintypes.LONG) for name in ("left", "top", "right", "bottom")]

class BitmapInfoHeader(ctypes.Structure):
    _fields_ = [("size", wintypes.DWORD), ("width", wintypes.LONG), ("height", wintypes.LONG), ("planes", wintypes.WORD), ("bitcount", wintypes.WORD), ("compression", wintypes.DWORD), ("image_size", wintypes.DWORD), ("xppm", wintypes.LONG), ("yppm", wintypes.LONG), ("used", wintypes.DWORD), ("important", wintypes.DWORD)]

class BitmapInfo(ctypes.Structure):
    _fields_ = [("header", BitmapInfoHeader), ("colors", wintypes.DWORD * 3)]

user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(Rect)]
user32.GetWindowDC.argtypes = [wintypes.HWND]
user32.GetWindowDC.restype = wintypes.HDC
user32.ReleaseDC.argtypes = [wintypes.HWND, wintypes.HDC]
user32.PrintWindow.argtypes = [wintypes.HWND, wintypes.HDC, wintypes.UINT]
gdi32.CreateCompatibleDC.argtypes = [wintypes.HDC]
gdi32.CreateCompatibleDC.restype = wintypes.HDC
gdi32.CreateCompatibleBitmap.argtypes = [wintypes.HDC, ctypes.c_int, ctypes.c_int]
gdi32.CreateCompatibleBitmap.restype = wintypes.HANDLE
gdi32.SelectObject.argtypes = [wintypes.HDC, wintypes.HANDLE]
gdi32.SelectObject.restype = wintypes.HANDLE
gdi32.DeleteObject.argtypes = [wintypes.HANDLE]
gdi32.DeleteDC.argtypes = [wintypes.HDC]
gdi32.GetDIBits.argtypes = [wintypes.HDC, wintypes.HANDLE, wintypes.UINT, wintypes.UINT, ctypes.c_void_p, ctypes.POINTER(BitmapInfo), wintypes.UINT]
rect = Rect()
if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
    raise RuntimeError("Fixture window is unavailable")
width, height = rect.right - rect.left, rect.bottom - rect.top
window_dc = user32.GetWindowDC(hwnd)
memory_dc = gdi32.CreateCompatibleDC(window_dc)
bitmap = gdi32.CreateCompatibleBitmap(window_dc, width, height)
previous = gdi32.SelectObject(memory_dc, bitmap)
gdi32.SelectObject(memory_dc, previous)
info = BitmapInfo()
info.header = BitmapInfoHeader(ctypes.sizeof(BitmapInfoHeader), width, -height, 1, 32, 0, width * height * 4, 0, 0, 0, 0)
pixels = ctypes.create_string_buffer(width * height * 4)

def capture():
    gdi32.SelectObject(memory_dc, bitmap)
    try:
        if not user32.PrintWindow(hwnd, memory_dc, 2):
            raise RuntimeError("PrintWindow failed")
    finally:
        gdi32.SelectObject(memory_dc, previous)
    if gdi32.GetDIBits(memory_dc, bitmap, 0, height, pixels, ctypes.byref(info), 0) != height:
        raise RuntimeError("GetDIBits failed")
    return Image.frombytes("RGB", (width, height), pixels.raw, "raw", "BGRX")

schedule = [(0.4, {"action": "permissionSelect", "index": 1}), (1.15, {"action": "permissionSelect", "index": 0}), (1.95, {"action": "permissionToggle", "code": "reference_materials.create"}), (2.45, {"action": "permissionTab", "index": 1}), (3.10, {"action": "permissionTab", "index": 0}), (3.75, {"action": "permissionSave"})]
frames, moments, hashes, events = [], [], [], []
started = time.perf_counter()
try:
    while time.perf_counter() - started < 5.3:
        elapsed = time.perf_counter() - started
        while schedule and elapsed >= schedule[0][0]:
            _, command = schedule.pop(0)
            command["sequence"] = uuid.uuid4().hex
            (evidence / "command.json").write_text(json.dumps(command, separators=(",", ":")), encoding="utf-8")
            events.append({"at": round(elapsed, 3), "action": command["action"]})
        frame = capture()
        hashes.append(hashlib.sha256(frame.tobytes()).hexdigest())
        frames.append(frame.resize((960, round(height * 960 / width)), Image.Resampling.LANCZOS))
        moments.append(time.perf_counter() - started)
        time.sleep(max(0, 1 / 30 - (time.perf_counter() - started - elapsed)))
finally:
    gdi32.DeleteObject(bitmap)
    gdi32.DeleteDC(memory_dc)
    user32.ReleaseDC(hwnd, window_dc)
durations = [max(10, round((b - a) * 1000)) for a, b in zip(moments, moments[1:])] + [100]
frames[0].save(evidence / "animaciones-winui-fixture.gif", save_all=True, append_images=frames[1:], duration=durations, loop=0, optimize=False)
for label, target in [("seleccion", 0.75), ("permiso", 2.05), ("tabs", 2.55), ("guardando", 4.05), ("confirmado", 4.80)]:
    index = min(range(len(moments)), key=lambda i: abs(moments[i] - target))
    frames[index].save(evidence / ("motion-" + label + ".png"))
metadata = {"fixtureOnly": True, "source": "PrintWindow of the running real WinUI page with isolated data", "capturedWidth": width, "capturedHeight": height, "interpolatedFrames": False, "frames": len(frames), "distinctRenderedFrames": len(set(hashes)), "elapsedSeconds": round(moments[-1], 3), "events": events, "frameTimes": [round(t, 3) for t in moments]}
(evidence / "motion-recording.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
print(json.dumps({key: metadata[key] for key in ("frames", "distinctRenderedFrames", "elapsedSeconds")}))
