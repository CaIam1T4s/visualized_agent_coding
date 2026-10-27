import json
import platform
import sys

import ast_service
from protocol import (
    ERR_INVALID_PARAMS,
    ERR_INVALID_REQUEST,
    ERR_METHOD_NOT_FOUND,
    ERR_NOT_HELLO,
    ERR_PARSE,
    ERR_TOO_LARGE,
    ERR_VERSION,
    MAX_MESSAGE_BYTES,
    SUPPORTED_VERSIONS,
    ProtocolError,
    encode,
    fail,
    ok,
)

CAPABILITIES = ['parse']


def handle_hello(params, state):
    requested = params.get('protocolVersions')
    if not isinstance(requested, list) or not all(isinstance(v, int) for v in requested):
        raise ProtocolError(ERR_INVALID_PARAMS, 'protocolVersions must be an integer list')
    common = [v for v in requested if v in SUPPORTED_VERSIONS]
    if not common:
        raise ProtocolError(ERR_VERSION, 'no common protocol version')
    state['negotiated'] = max(common)
    return {
        'protocolVersion': state['negotiated'],
        'pythonVersion': platform.python_version(),
        'capabilities': CAPABILITIES,
    }


def handle_parse(params, state):
    if state['negotiated'] is None:
        raise ProtocolError(ERR_NOT_HELLO, 'helper.hello must be called first')
    uri = params.get('uri')
    version = params.get('version')
    source = params.get('source')
    if not isinstance(uri, str) or not isinstance(version, int) or not isinstance(source, str):
        raise ProtocolError(ERR_INVALID_PARAMS, 'expected uri:string, version:int, source:string')
    result = ast_service.parse(source)
    return {
        'uri': uri,
        'version': version,
        'diagnostics': result['diagnostics'],
        'symbols': result['symbols'],
    }


def dispatch(message, state):
    message_id = message.get('id')
    method = message.get('method')
    params = message.get('params') or {}
    if not isinstance(params, dict):
        return fail(message_id, ERR_INVALID_PARAMS, 'params must be an object'), False
    try:
        if method == 'helper.hello':
            return ok(message_id, handle_hello(params, state)), False
        if method == 'python.parse':
            return ok(message_id, handle_parse(params, state)), False
        if method == 'helper.shutdown':
            return ok(message_id, {}), True
        raise ProtocolError(ERR_METHOD_NOT_FOUND, f'unknown method: {method}')
    except ProtocolError as err:
        return fail(message_id, err.code, err.message), False


def write(stream, message):
    stream.write(encode(message).encode('utf-8'))
    stream.flush()


def main():
    state = {'negotiated': None}
    stdin = sys.stdin.buffer
    stdout = sys.stdout.buffer
    while True:
        raw = stdin.readline(MAX_MESSAGE_BYTES + 1)
        if not raw:
            break
        if len(raw) > MAX_MESSAGE_BYTES:
            write(stdout, fail(None, ERR_TOO_LARGE, 'message exceeds limit'))
            break
        try:
            message = json.loads(raw.decode('utf-8'))
        except (UnicodeDecodeError, json.JSONDecodeError):
            write(stdout, fail(None, ERR_PARSE, 'invalid JSON'))
            continue
        if not isinstance(message, dict):
            write(stdout, fail(None, ERR_INVALID_REQUEST, 'message must be an object'))
            continue
        if 'id' not in message:
            # Notifications such as $/cancelRequest are ignored: requests are served synchronously.
            continue
        response, stop = dispatch(message, state)
        write(stdout, response)
        if stop:
            break


if __name__ == '__main__':
    main()
