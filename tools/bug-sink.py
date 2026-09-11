#!/usr/bin/env python3
"""A stand-in for the Discord webhook, so the upload path can be PROVEN without a channel.

The thing that can silently be wrong about a multipart POST is not the network — it is the body: one missing
CRLF, one part Discord does not recognise, and the request succeeds while the attachment never appears. So this
listens and takes the body apart BY HAND rather than with a tolerant library, because a parser that forgives a
malformed part would hide exactly the bug this is here to catch.

    tools/bug-sink.py 8787 &
    BNB_BUGREPORT_WEBHOOK=http://127.0.0.1:8787/hook godot --path . -- --smoke-bug

Every part it receives is written under /tmp/bnb-bug-sink, so the save and the screenshot can be opened.
"""
import json
import re
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

OUT = Path("/tmp/bnb-bug-sink")


def parts(body: bytes, boundary: bytes):
    """Split a multipart body strictly: --boundary CRLF headers CRLF CRLF data CRLF, closed by --boundary--."""
    if not body.endswith(b"--" + boundary + b"--\r\n"):
        raise ValueError("the body does not end with the closing boundary")
    chunks = body.split(b"--" + boundary + b"\r\n")
    if chunks[0] != b"":
        raise ValueError("there is data before the first boundary")
    for chunk in chunks[1:]:
        head, _, rest = chunk.partition(b"\r\n\r\n")
        if not _:
            raise ValueError("a part has no blank line between its headers and its data")
        data = rest
        for tail in (b"\r\n--" + boundary + b"--\r\n", b"\r\n"):
            if data.endswith(tail):
                data = data[: -len(tail)]
                break
        else:
            raise ValueError("a part's data is not closed by CRLF")
        headers = head.decode("utf-8", "replace")
        name = re.search(r'name="([^"]*)"', headers)
        filename = re.search(r'filename="([^"]*)"', headers)
        yield (name.group(1) if name else "?"), (filename.group(1) if filename else None), data


class Sink(BaseHTTPRequestHandler):
    def do_POST(self):
        length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(length)
        ctype = self.headers.get("Content-Type", "")
        print(f"sink: POST {self.path} {length} bytes, {ctype}")
        boundary = re.search(r"boundary=(\S+)", ctype)
        if "multipart/form-data" not in ctype or not boundary:
            print("sink: !! NOT MULTIPART — Discord would reject this")
            self.send_response(400)
            self.end_headers()
            return

        OUT.mkdir(parents=True, exist_ok=True)
        files = []
        try:
            for name, filename, data in parts(body, boundary.group(1).encode()):
                if name == "payload_json":
                    payload = json.loads(data)
                    print(f"sink:   payload_json content={payload['content']!r}")
                    print(f"sink:   payload_json attachments={payload.get('attachments')}")
                else:
                    (OUT / (filename or name)).write_bytes(data)
                    files.append(f"{name}={filename} {len(data)}B")
        except ValueError as bad:
            print(f"sink: !! MALFORMED BODY — {bad}")
            self.send_response(400)
            self.end_headers()
            return
        print(f"sink:   files: {' · '.join(files) if files else 'NONE — nothing was attached'}")
        print(f"sink:   written to {OUT}")
        self.send_response(204)
        self.end_headers()

    def log_message(self, *_):
        pass


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8787
    print(f"sink: listening on http://127.0.0.1:{port}/hook")
    HTTPServer(("127.0.0.1", port), Sink).serve_forever()
