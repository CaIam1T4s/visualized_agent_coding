import json

PROTOCOL_VERSION = 1
SUPPORTED_VERSIONS = [1]
MAX_MESSAGE_BYTES = 8 * 1024 * 1024

ERR_PARSE = -32700
ERR_INVALID_REQUEST = -32600
ERR_METHOD_NOT_FOUND = -32601
ERR_INVALID_PARAMS = -32602
ERR_VERSION = -32002
ERR_NOT_HELLO = -32005
ERR_TOO_LARGE = -32004


class ProtocolError(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code
        self.message = message


def ok(message_id, result):
    return {'jsonrpc': '2.0', 'id': message_id, 'result': result}


def fail(message_id, code, message):
    return {'jsonrpc': '2.0', 'id': message_id, 'error': {'code': code, 'message': message}}


def encode(message):
    return json.dumps(message, ensure_ascii=False, separators=(',', ':')) + '\n'
