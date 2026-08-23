import socket
import threading

import pytest
from rhino_mcp.protocol import (
    BridgeConnection,
    BridgeEndpoint,
    BridgeUnavailable,
    decode_frame,
    encode_frame,
)


def test_frame_round_trip_with_chunked_input():
    left, right = socket.socketpair()
    try:
        expected = {"protocol": 2, "id": 7, "data": {"text": "x" * 100_000}}
        frame = encode_frame(expected)

        def writer():
            for offset in range(0, len(frame), 997):
                left.sendall(frame[offset : offset + 997])

        thread = threading.Thread(target=writer)
        thread.start()
        assert decode_frame(right) == expected
        thread.join()
    finally:
        left.close()
        right.close()


def test_persistent_connection_reuses_one_socket():
    listener = socket.socket()
    listener.bind(("127.0.0.1", 0))
    listener.listen()
    port = listener.getsockname()[1]
    accepted = []

    def server():
        client, _ = listener.accept()
        accepted.append(client)
        with client:
            for _ in range(2):
                request = decode_frame(client)
                client.sendall(
                    encode_frame({"id": request["id"], "status": "ok", "data": {"pong": True}})
                )

    thread = threading.Thread(target=server)
    thread.start()
    connection = BridgeConnection(BridgeEndpoint("127.0.0.1", port, 1, 1))
    try:
        assert connection.request("health")["data"]["pong"]
        assert connection.request("health")["data"]["pong"]
        assert len(accepted) == 1
    finally:
        connection.close()
        listener.close()
        thread.join()


def test_interrupted_mutation_becomes_actionable_bridge_error():
    class BrokenSocket:
        def sendall(self, _):
            raise OSError("connection reset")

        def close(self):
            pass

    connection = BridgeConnection(BridgeEndpoint())
    connection._socket = BrokenSocket()
    with pytest.raises(BridgeUnavailable, match="reconnect automatically"):
        connection.request("create_geometry", retry=False)
