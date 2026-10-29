import json
import os
import subprocess
import sys
import unittest

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MAIN = os.path.join(HERE, 'main.py')
NL = chr(10)
MAX_MESSAGE_BYTES = 8 * 1024 * 1024

ERR_PARSE = -32700
ERR_INVALID_REQUEST = -32600
ERR_METHOD_NOT_FOUND = -32601
ERR_INVALID_PARAMS = -32602
ERR_VERSION = -32002
ERR_TOO_LARGE = -32004
ERR_NOT_HELLO = -32005


def request(message_id, method, params=None):
    message = {'jsonrpc': '2.0', 'id': message_id, 'method': method}
    if params is not None:
        message['params'] = params
    return json.dumps(message, ensure_ascii=False)


def hello(versions):
    return request('1', 'helper.hello', {'protocolVersions': versions})


def parse_request(message_id, **fields):
    params = {'uri': 'file:///a.py', 'version': 1, 'source': 'x = 1'}
    params.update(fields)
    return request(message_id, 'python.parse', params)


HELLO = hello([1])
SHUTDOWN = request('9', 'helper.shutdown')


def run_helper(*chunks):
    # str chunks are sent as one line each; bytes are sent verbatim.
    data = b''
    for chunk in chunks:
        if isinstance(chunk, bytes):
            data += chunk
        else:
            data += (chunk + NL).encode('utf-8')
    proc = subprocess.run([sys.executable, MAIN], input=data, capture_output=True, timeout=60)
    lines = proc.stdout.decode('utf-8').splitlines()
    responses = [json.loads(line) for line in lines if line.strip()]
    return proc.returncode, responses


class HelperProtocolTests(unittest.TestCase):

    def test_hello_negotiates_protocol_version(self):
        _, responses = run_helper(HELLO)
        self.assertEqual(responses[0]['result']['protocolVersion'], 1)

    def test_invalid_json_returns_parse_error(self):
        _, responses = run_helper('{not json')
        self.assertEqual(responses[0]['error']['code'], ERR_PARSE)
        self.assertIsNone(responses[0]['id'])

    def test_invalid_utf8_returns_parse_error(self):
        _, responses = run_helper(bytes([255, 254]) + NL.encode('utf-8'))
        self.assertEqual(responses[0]['error']['code'], ERR_PARSE)

    def test_non_object_message_is_invalid_request(self):
        _, responses = run_helper(json.dumps([1, 2]))
        self.assertEqual(responses[0]['error']['code'], ERR_INVALID_REQUEST)

    def test_oversized_message_is_rejected_and_stops_helper(self):
        code, responses = run_helper(b'x' * (MAX_MESSAGE_BYTES + 10))
        self.assertEqual(code, 0)
        self.assertEqual(len(responses), 1)
        self.assertEqual(responses[0]['error']['code'], ERR_TOO_LARGE)

    def test_notification_gets_no_response(self):
        notification = json.dumps({'jsonrpc': '2.0', 'method': '$/cancelRequest', 'params': {'id': '1'}})
        _, responses = run_helper(notification, SHUTDOWN)
        self.assertEqual(len(responses), 1)
        self.assertEqual(responses[0]['id'], '9')

    def test_non_object_params_is_invalid_params(self):
        message = json.dumps({'jsonrpc': '2.0', 'id': '1', 'method': 'helper.hello', 'params': [1]})
        _, responses = run_helper(message)
        self.assertEqual(responses[0]['error']['code'], ERR_INVALID_PARAMS)

    def test_unknown_method_is_method_not_found(self):
        _, responses = run_helper(request('1', 'no.such.method'))
        self.assertEqual(responses[0]['error']['code'], ERR_METHOD_NOT_FOUND)

    def test_parse_before_hello_is_rejected(self):
        _, responses = run_helper(parse_request('1'))
        self.assertEqual(responses[0]['error']['code'], ERR_NOT_HELLO)

    def test_hello_rejects_non_integer_versions(self):
        for versions in ['1', [1, 'x'], [True], None]:
            with self.subTest(versions=versions):
                _, responses = run_helper(hello(versions))
                self.assertEqual(responses[0]['error']['code'], ERR_INVALID_PARAMS)

    def test_hello_without_common_version_is_rejected(self):
        _, responses = run_helper(hello([99]))
        self.assertEqual(responses[0]['error']['code'], ERR_VERSION)

    def test_parse_rejects_invalid_params(self):
        bad_cases = [
            {'version': '1'},
            {'version': True},
            {'version': -1},
            {'source': None},
            {'uri': 3},
        ]
        for fields in bad_cases:
            with self.subTest(fields=fields):
                _, responses = run_helper(HELLO, parse_request('2', **fields))
                self.assertEqual(responses[1]['error']['code'], ERR_INVALID_PARAMS)

    def test_syntax_error_is_returned_as_diagnostic_not_error(self):
        source = NL.join(['def f(:', '    pass', ''])
        _, responses = run_helper(HELLO, parse_request('2', source=source))
        response = responses[1]
        self.assertNotIn('error', response)
        self.assertIsNone(response['result']['symbols'])
        diagnostics = response['result']['diagnostics']
        self.assertGreaterEqual(len(diagnostics), 1)
        self.assertEqual(diagnostics[0]['severity'], 'error')
        self.assertEqual(diagnostics[0]['line'], 1)

    def test_columns_count_utf16_units_for_non_ascii_source(self):
        # 'class 甲: pass' is 13 characters (15 UTF-8 bytes), so the end column is 14 in UTF-16 units.
        source = NL.join(['class 甲: pass', ''])
        _, responses = run_helper(HELLO, parse_request('2', source=source))
        cls = responses[1]['result']['symbols']['classes'][0]
        self.assertEqual(cls['range']['endColumn'], 14)

    def test_empty_non_object_params_are_rejected(self):
        for value in [None, [], False, 0]:
            with self.subTest(value=value):
                message = json.dumps({'jsonrpc': '2.0', 'id': '1', 'method': 'helper.hello', 'params': value})
                _, responses = run_helper(message)
                self.assertEqual(responses[0]['error']['code'], ERR_INVALID_PARAMS)

    def test_jsonrpc_version_and_string_id_are_required(self):
        for fields in [{'jsonrpc': '1.0'}, {'id': 7}]:
            with self.subTest(fields=fields):
                message = {'jsonrpc': '2.0', 'id': '1', 'method': 'helper.hello', 'params': {'protocolVersions': [1]}}
                message.update(fields)
                _, responses = run_helper(json.dumps(message))
                self.assertEqual(responses[0]['error']['code'], ERR_INVALID_REQUEST)

    def test_syntax_error_column_is_utf16_after_supplementary_character(self):
        source = 'x = "😀"; y ='
        _, responses = run_helper(HELLO, parse_request('2', source=source))
        diagnostic = responses[1]['result']['diagnostics'][0]
        self.assertEqual(diagnostic['column'], len(source.encode('utf-16-le')) // 2 + 1)

    def test_parse_does_not_execute_imports_or_statements(self):
        source = 'import this_module_does_not_exist\nraise RuntimeError("never execute")'
        _, responses = run_helper(HELLO, parse_request('2', source=source))
        self.assertEqual(responses[1]['result']['diagnostics'], [])


class MultiErrorTests(unittest.TestCase):
    def diagnostics(self, source):
        _, responses = run_helper(HELLO, parse_request('2', source=source))
        result = responses[1]['result']
        self.assertIsNone(result['symbols'])
        return result['diagnostics']

    def test_two_broken_functions_are_reported_in_source_order(self):
        errors = self.diagnostics('def first(:\n    pass\ndef second(:\n    pass\n')
        self.assertEqual([error['line'] for error in errors], [1, 3])
        self.assertEqual([error['parser'] for error in errors], ['native', 'recovery'])

    def test_independent_errors_in_same_function(self):
        errors = self.diagnostics('def f():\n    x =\n    y =\n')
        self.assertEqual([error['line'] for error in errors], [2, 3])

    def test_missing_body_recovery_keeps_the_actual_header_line(self):
        errors = self.diagnostics('value =\ndef start():\n    if True:\n    # end\n')
        self.assertEqual(len(errors), 2)
        self.assertEqual(errors[1]['message'], 'expected an indented block')
        self.assertEqual(errors[1]['relatedLine'], 3)

    def test_recovery_columns_remain_utf16(self):
        source = 'x = "\U0001f600"; y =\nz = "\U0001f600"; w =\n'
        errors = self.diagnostics(source)
        self.assertEqual([error['line'] for error in errors], [1, 2])
        for error, line in zip(errors, source.splitlines()):
            self.assertEqual(error['column'], len(line.encode('utf-16-le')) // 2 + 1)

    def test_valid_modern_syntax_does_not_add_spurious_errors(self):
        for source, error_line in [
            ('x =\nmatch x:\n    case 1: pass\n', 1),
            ('match x:\n    case 1: pass\nx =\n', 3),
        ]:
            with self.subTest(source=source):
                errors = self.diagnostics(source)
                self.assertEqual([error['line'] for error in errors], [error_line])

    def test_unterminated_string_preserves_native_error(self):
        errors = self.diagnostics('x = """unfinished\n')
        self.assertEqual(len(errors), 1)
        self.assertIn('unterminated', errors[0]['message'])

    def test_unclosed_call_does_not_add_a_tail_error_on_hidden_markers(self):
        source = ('class Hero:\n    pass\n    def _vac_on_start(self):\n'
                  '        self.walk(\n        # vac:region-end start\n')
        errors = self.diagnostics(source)
        self.assertEqual(len(errors), 1, errors)
        self.assertEqual(errors[0]['line'], 4)
        self.assertIn('was never closed', errors[0]['message'])

    def test_valid_modern_file_uses_native_parser(self):
        source = 'match 1:\n    case 1: pass\n'
        _, responses = run_helper(HELLO, parse_request('2', source=source))
        self.assertEqual(responses[1]['result']['diagnostics'], [])


if __name__ == '__main__':
    unittest.main()
