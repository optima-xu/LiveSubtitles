"""Exercise the real setup page and downloader using an isolated, rate-limited local file."""
import ctypes
from ctypes import wintypes as win
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import re
import subprocess
import tempfile
import threading
import time
from uuid import uuid4

ROOT = Path(__file__).resolve().parents[2]
DATA = bytes(range(256)) * 32768
user = ctypes.windll.user32
callback = ctypes.WINFUNCTYPE(win.BOOL, win.HWND, win.LPARAM)
user.EnumWindows.argtypes = [callback, win.LPARAM]
user.EnumChildWindows.argtypes = [win.HWND, callback, win.LPARAM]
user.SendMessageTimeoutW.argtypes = [win.HWND, win.UINT, win.WPARAM, win.LPARAM, win.UINT, win.UINT, ctypes.POINTER(ctypes.c_size_t)]


def wait_for(check, description, timeout=40):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        result = check()
        if result:
            return result
        time.sleep(0.1)
    raise AssertionError(f"Timed out waiting for {description}")


def windows(parent=None):
    handles = []
    visit = callback(lambda hwnd, _: handles.append(hwnd) or True)
    if parent:
        user.EnumChildWindows(parent, visit, 0)
    else:
        user.EnumWindows(visit, 0)
    return handles


def caption(hwnd):
    buf = ctypes.create_unicode_buffer(1024)
    result = ctypes.c_size_t()
    user.SendMessageTimeoutW(hwnd, 0x000D, len(buf), ctypes.addressof(buf), 2, 200, ctypes.byref(result))
    return buf.value


def run_test(tmp, server, ranges):
    ident = "LiveSubtitlesProgress" + uuid4().hex[:8]
    compiler = ROOT / "dist/build-tools/InnoSetup-7.1.1/ISCC.exe"
    original = (ROOT / "installer/LiveSubtitles.iss").read_text(encoding="utf-8-sig")
    messages = original.split("[CustomMessages]", 1)[1].split("[Tasks]", 1)[0]
    resources = tmp / "resources"
    resources.mkdir()
    lock = json.loads((ROOT / "resources/models.lock.json").read_text())
    lock["ollama"]["version"] = "0.0.0"  # This fixture only needs the VAD download.
    lock["vad"].update(url=f"http://127.0.0.1:{server.server_port}/sample", size_bytes=len(DATA), sha256=hashlib.sha256(DATA).hexdigest())
    (resources / "models.lock.json").write_text(json.dumps(lock), encoding="utf-8")
    dest = tmp / "chosen folder"
    runtime = dest / ".runtime"
    runtime.mkdir(parents=True)
    # Fail before the next network request, after the resumed VAD has been verified.
    (runtime / "asr-models").write_text("fixture")
    script = tmp / "fixture.iss"
    script.write_text(f"""[Setup]
AppName={ident}
AppVersion=1
DefaultDirName={dest}
PrivilegesRequired=lowest
SetupArchitecture=x64
Uninstallable=no
CreateUninstallRegKey=no
OutputBaseFilename=probe
DisableProgramGroupPage=yes
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\\ChineseSimplified.isl"
[CustomMessages]{messages}
[Files]
Source: "{ROOT / 'Install.ps1'}"; DestDir: "{{app}}"
Source: "{resources / 'models.lock.json'}"; DestDir: "{{app}}\\resources"
[Code]
#include "{ROOT / 'installer/ModelPreparation.iss'}"
""", encoding="utf-8-sig")
    subprocess.run([str(compiler), "/Q", f"/O{tmp}", str(script)], check=True)
    exe = str(tmp / "probe.exe")
    process = subprocess.Popen([exe, "/SILENT", "/LANG=english", f"/DIR={dest}", f"/LOG={tmp / 'first.log'}"])
    partial = runtime / "vad/silero_vad.onnx.partial"
    try:
        hwnd = wait_for(lambda: next((h for h in windows() if user.IsWindowVisible(h) and ident in caption(h)), None), "wizard")
        wait_for(lambda: partial.exists() and partial.stat().st_size > 256 * 1024, "receiving bytes")
        wait_for(lambda: any(re.search(r"MiB.*\([1-9]\d*%\)", caption(h)) for h in windows(hwnd)), "non-zero visible percentage")
        for command, check in [(0xF020, user.IsIconic), (0xF120, lambda h: not user.IsIconic(h)),
                               (0xF030, user.IsZoomed), (0xF120, lambda h: not user.IsZoomed(h))]:
            user.PostMessageW(hwnd, 0x0112, command, 0)
            wait_for(lambda: check(hwnd), "window state", 5)
        pid = win.DWORD()
        user.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        user.PostMessageW(hwnd, 0x0010, 0, 0)

        def confirm_window():
            for h in windows():
                other = win.DWORD()
                user.GetWindowThreadProcessId(h, ctypes.byref(other))
                if h != hwnd and other.value == pid.value and user.IsWindowVisible(h) and user.GetDlgItem(h, 6):
                    return h
            return None

        dialog = wait_for(confirm_window, "confirmation after Close", 5)
        user.PostMessageW(user.GetDlgItem(dialog, 6), 0x00F5, 0, 0)  # BM_CLICK, Yes
        assert process.wait(timeout=15) != 0
        previous = partial.stat().st_size
        time.sleep(0.5)
        assert 0 < previous < len(DATA) and partial.stat().st_size == previous
        assert not (dest / "app/LiveSubtitles.exe").exists()
        print(f"Visible progress, minimize, maximize and confirmed cancellation passed at {previous} bytes.")
        result = subprocess.run([exe, "/VERYSILENT", "/SUPPRESSMSGBOXES", f"/DIR={dest}", f"/LOG={tmp / 'resume.log'}"], timeout=75)
        assert result.returncode == 7 and previous in ranges, f"Expected range {previous}, observed {ranges}"
        assert (runtime / "vad/silero_vad.onnx").read_bytes() == DATA
        print("Resumed from the exact saved byte offset and verified the complete file.")
    except BaseException:
        for name in ("first.log", "resume.log"):
            log = tmp / name
            if log.exists():
                raw = log.read_bytes()
                print(raw.decode("utf-16" if raw.startswith(b"\xff\xfe") else "utf-8-sig", errors="replace")[-3200:])
        raise
    finally:
        if process.poll() is None:
            subprocess.run(["taskkill", "/T", "/F", "/PID", str(process.pid)], capture_output=True)


if __name__ == "__main__":
    offsets = []

    class Source(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def do_GET(self):
            start = int(self.headers.get("Range", "bytes=0-")[6:-1])
            offsets.append(start)
            self.send_response(206 if start else 200)
            self.send_header("Content-Length", str(len(DATA) - start))
            self.send_header("Accept-Ranges", "bytes")
            if start:
                self.send_header("Content-Range", f"bytes {start}-{len(DATA)-1}/{len(DATA)}")
            self.end_headers()
            try:
                for pos in range(start, len(DATA), 65536):
                    self.wfile.write(DATA[pos:pos + 65536])
                    time.sleep(0.08)
            except (ConnectionResetError, BrokenPipeError, ConnectionAbortedError):
                pass

    with ThreadingHTTPServer(("127.0.0.1", 0), Source) as http, tempfile.TemporaryDirectory(prefix="LS-progress-") as folder:
        threading.Thread(target=http.serve_forever, daemon=True).start()
        try:
            run_test(Path(folder), http, offsets)
        finally:
            http.shutdown()
