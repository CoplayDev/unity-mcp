#!/usr/bin/env python3
import socket, struct, json, sys

HOST = "127.0.0.1"
PORT = 6400
try:
    SIZE_MB = int(sys.argv[1])
except (IndexError, ValueError):
    SIZE_MB = 5  # e.g., 5 or 10
FILL = "R"
MAX_FRAME = 64 * 1024 * 1024

def recv_exact(sock, n):
    buf = bytearray(n)
    view = memoryview(buf)
    off = 0
    while off < n:
        r = sock.recv_into(view[off:])
        if r == 0:
            raise RuntimeError("socket closed")
        off += r
    return bytes(buf)

def is_valid_json(b):
    try:
        json.loads(b.decode("utf-8"))
        return True
    except Exception:
        return False

def recv_legacy_json(sock, timeout=60):
    sock.settimeout(timeout)
    chunks = []
    while True:
        chunk = sock.recv(65536)
        if not chunk:
            data = b"".join(chunks)
            if not data:
                raise RuntimeError("no data, socket closed")
            return data
        chunks.append(chunk)
        data = b"".join(chunks)
        if data.strip() == b"ping":
            return data
        if is_valid_json(data):
            return data

def main():
    # Cap filler to stay within framing limit (reserve small overhead for JSON)
    safe_max = max(1, MAX_FRAME - 4096)
    filler_len = min(SIZE_MB * 1024 * 1024, safe_max)
    body = {
        "type": "read_console",
        "params": {
            "action": "get",
            "types": ["all"],
            "count": 1000,
            "format": "detailed",
            "includeStacktrace": True,
            "filterText": FILL * filler_len
        }
    }
    body_bytes = json.dumps(body, ensure_ascii=False).encode("utf-8")

    with socket.create_connection((HOST, PORT), timeout=5) as s:
        s.settimeout(2)
        # Read optional greeting
        try:
            greeting = s.recv(256)
        except Exception:
            greeting = b""
        greeting_text = greeting.decode("ascii", errors="ignore").strip()
        print(f"Greeting: {greeting_text or '(none)'}")

        framing = "FRAMING=1" in greeting_text
        print(f"Using framing? {framing}")

        s.settimeout(120)
        if framing:
            header = struct.pack(">Q", len(body_bytes))
            s.sendall(header + body_bytes)
            resp_len = struct.unpack(">Q", recv_exact(s, 8))[0]
            print(f"Response framed length: {resp_len}")
            MAX_RESP = MAX_FRAME
            if resp_len <= 0 or resp_len > MAX_RESP:
                raise RuntimeError(f"invalid framed length: {resp_len} (max {MAX_RESP})")
            resp = recv_exact(s, resp_len)
        else:
            s.sendall(body_bytes)
            resp = recv_legacy_json(s)

        print(f"Response bytes: {len(resp)}")
        print(f"Response head: {resp[:120].decode('utf-8','ignore')}")

if __name__ == "__main__":
    main()


