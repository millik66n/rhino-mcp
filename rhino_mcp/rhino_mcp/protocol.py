"""Length-prefixed Rhino bridge protocol with reconnect support."""

from __future__ import annotations

import itertools
import json
import socket
import struct
import threading
from contextlib import suppress
from dataclasses import dataclass
from typing import Any

HEADER = struct.Struct("!I")
MAX_MESSAGE_BYTES = 64 * 1024 * 1024


class BridgeError(RuntimeError):
    """Base error returned by a Rhino bridge."""


class BridgeUnavailable(BridgeError):
    """The Rhino bridge is not accepting connections."""


class ProtocolError(BridgeError):
    """The bridge sent an invalid response."""


def encode_frame(message: dict[str, Any]) -> bytes:
    body = json.dumps(message, separators=(",", ":")).encode("utf-8")
    if len(body) > MAX_MESSAGE_BYTES:
        raise ProtocolError("message exceeds 64 MiB limit")
    return HEADER.pack(len(body)) + body


def receive_exact(sock: socket.socket, size: int) -> bytes:
    chunks: list[bytes] = []
    remaining = size
    while remaining:
        data = sock.recv(min(64 * 1024, remaining))
        if not data:
            raise BridgeUnavailable("Rhino closed the connection")
        chunks.append(data)
        remaining -= len(data)
    return b"".join(chunks)


def decode_frame(sock: socket.socket) -> dict[str, Any]:
    (length,) = HEADER.unpack(receive_exact(sock, HEADER.size))
    if length <= 0 or length > MAX_MESSAGE_BYTES:
        raise ProtocolError("invalid bridge frame length")
    try:
        value = json.loads(receive_exact(sock, length).decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ProtocolError("bridge returned invalid JSON") from exc
    if not isinstance(value, dict):
        raise ProtocolError("bridge response must be an object")
    return value


@dataclass(slots=True)
class BridgeEndpoint:
    host: str = "127.0.0.1"
    port: int = 9876
    connect_timeout: float = 2.0
    request_timeout: float = 45.0


class BridgeConnection:
    """Thread-safe persistent connection using protocol v2."""

    def __init__(self, endpoint: BridgeEndpoint):
        self.endpoint = endpoint
        self._socket: socket.socket | None = None
        self._lock = threading.RLock()
        self._ids = itertools.count(1)

    @property
    def connected(self) -> bool:
        return self._socket is not None

    def connect(self) -> None:
        with self._lock:
            if self._socket is not None:
                return
            try:
                sock = socket.create_connection(
                    (self.endpoint.host, self.endpoint.port),
                    timeout=self.endpoint.connect_timeout,
                )
                sock.settimeout(self.endpoint.request_timeout)
                sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                self._socket = sock
            except OSError as exc:
                self.close()
                raise BridgeUnavailable(
                    "Open Rhino and check that the Rhino MCP strip says Bridge connected, "
                    "then try again. Run RhinoMCPRestart in Rhino if the bridge is stopped. "
                    f"No bridge is listening on {self.endpoint.host}:{self.endpoint.port}."
                ) from exc

    def close(self) -> None:
        with self._lock:
            if self._socket is not None:
                with suppress(OSError):
                    self._socket.close()
            self._socket = None

    def request(
        self,
        command: str,
        params: dict[str, Any] | None = None,
        *,
        retry: bool = True,
    ) -> dict[str, Any]:
        request_id = next(self._ids)
        payload = {
            "protocol": 2,
            "id": request_id,
            "type": command,
            "params": params or {},
        }
        attempts = 2 if retry else 1
        with self._lock:
            for attempt in range(attempts):
                try:
                    self.connect()
                    assert self._socket is not None
                    self._socket.sendall(encode_frame(payload))
                    response = decode_frame(self._socket)
                    if response.get("id") not in (None, request_id):
                        raise ProtocolError("bridge response ID did not match request")
                    if response.get("status") == "error":
                        raise BridgeError(str(response.get("message", "Unknown Rhino error")))
                    return response
                except (OSError, BridgeUnavailable, ProtocolError) as exc:
                    self.close()
                    if attempt + 1 == attempts:
                        if isinstance(exc, BridgeError):
                            raise
                        raise BridgeUnavailable(
                            "The Rhino connection was interrupted. The bridge will reconnect "
                            "automatically; retry this operation."
                        ) from exc
            raise BridgeUnavailable("Rhino bridge request failed")

    def ping(self) -> dict[str, Any]:
        return self.request("health", retry=True)
