"""Product entry point. User stdio and execution control are separate channels."""
import ast
import importlib
import importlib.abc
import importlib.util
import json
import linecache
import os
from pathlib import Path
import socket
import select
import sys
import threading
import traceback
import types
from vac_runtime._entry import entry_session, install_router, open_messages
from vac_runtime.messages import MessageRouter
from vac_runtime.stage import StageSession
from vac_runtime.events import EventInbox


def exception_payload(error):
    """Version 2: actual source coordinates, no captured locals or user presentation rules."""
    seen, budget = set(), [128]

    def text(value, limit=16000):
        try:
            result = str(value)
        except BaseException:
            result = '<exception message unavailable>'
        return result[:limit]

    def utf16_column(filename, row, byte_column):
        if byte_column is None:
            return None
        line = linecache.getline(filename, row)
        prefix = line.encode('utf-8')[:byte_column].decode('utf-8', errors='ignore')
        return len(prefix.encode('utf-16-le')) // 2 + 1

    def collect(item, depth=0):
        kind, message = type(item).__name__, text(item.msg if isinstance(item, SyntaxError) else item)
        if depth > 12 or id(item) in seen or budget[0] <= 0:
            return dict(type=kind, message=message, frames=[], truncated=True)
        seen.add(id(item))
        budget[0] -= 1
        stack = traceback.extract_tb(item.__traceback__)
        frames = [dict(path=f.filename, line=f.lineno, function=f.name,
                       column=utf16_column(f.filename, f.lineno, getattr(f, 'colno', None)),
                       endLine=getattr(f, 'end_lineno', None),
                       endColumn=utf16_column(f.filename, getattr(f, 'end_lineno', None) or f.lineno,
                                              getattr(f, 'end_colno', None))) for f in stack[-256:]]
        if isinstance(item, SyntaxError) and item.filename and item.lineno:
            line = item.text or linecache.getline(item.filename, item.lineno)
            prefix = line[:max(0, (item.offset or 1) - 1)]
            frames.append(dict(path=item.filename, line=item.lineno, function='<diagnostic>',
                               column=len(prefix.encode('utf-16-le')) // 2 + 1))
        children = getattr(item, 'exceptions', ())
        # Only Python's actual exception groups define hierarchy, not arbitrary custom attributes.
        import builtins
        is_group = isinstance(item, getattr(builtins, 'BaseExceptionGroup', ()))
        cause = collect(item.__cause__, depth + 1) if item.__cause__ is not None else None
        context = collect(item.__context__, depth + 1) if item.__cause__ is None and item.__context__ is not None and not item.__suppress_context__ else None
        group_children = [collect(child, depth + 1) for child in children[:min(128, max(0, budget[0]))]] if is_group else None
        notes = getattr(item, '__notes__', ())
        notes = notes if isinstance(notes, (list, tuple)) else ()
        return dict(type=kind, message=message, frames=frames,
                    cause=cause, context=context,
                    suppressContext=item.__suppress_context__,
                    children=group_children,
                    truncated=len(stack) > 256 or (is_group and len(children) > len(group_children)) or len(notes) > 16,
                    notes=[text(note) for note in notes[:16]])

    try:
        data = collect(error)
    except BaseException:
        data = dict(type=type(error).__name__, message='异常信息无法完整读取，请查看诊断详情。', frames=[], truncated=True)
    try:
        options = dict(max_group_width=128, max_group_depth=12) if sys.version_info >= (3, 11) else {}
        raw = ''.join(traceback.TracebackException.from_exception(error, capture_locals=False, **options).format())
    except BaseException:
        raw = data['type'] + ': ' + data['message']
    if len(raw) > 256000:
        raw = raw[:256000] + '\n[diagnostic truncated]\n'
        data['truncated'] = True
    return data, raw


class SnapshotLoader(importlib.abc.MetaPathFinder, importlib.abc.Loader):
    def __init__(self, root, sources):
        self.modules = {}
        self.packages = {}
        for item in sources:
            path = Path(item['path'])
            name = '.'.join(path.with_suffix('').parts)
            if name.endswith('.__init__'):
                name = name[:-9]
                self.packages[name] = str(root / path.parent)
            self.modules[name] = (str(root / path), item['source'])
            for parent in path.parents:
                if str(parent) != '.':
                    self.packages['.'.join(parent.parts)] = str(root / parent)
        for filename, source in self.modules.values():
            linecache.cache[filename] = (len(source), None, source.splitlines(True), filename)

    def find_spec(self, fullname, path=None, target=None):
        if fullname in self.modules or fullname in self.packages:
            return importlib.util.spec_from_loader(fullname, self, is_package=fullname in self.packages)
        return None

    def create_module(self, spec):
        return None

    def exec_module(self, module):
        name = module.__name__
        if name in self.packages:
            module.__path__ = [self.packages[name]]
            if name == 'runtime':
                module.__path__.append(str(Path(__file__).parent / 'runtime'))
        if name in self.modules:
            filename, source = self.modules[name]
            module.__file__ = filename
            exec(compile(source, filename, 'exec'), module.__dict__)


def run(request, emit, inbox=None):
    # Keep the same logical path as the host snapshot (including selected junction paths).
    root = Path(os.path.abspath(request['root']))
    snapshot = SnapshotLoader(root, request['sources'])
    # Product fallback for templates without project-local compatibility modules.
    snapshot.packages.setdefault('runtime', str(root / 'runtime'))
    sys.meta_path.insert(0, snapshot)
    sys.path.insert(0, str(root))
    os.chdir(root)
    # Compile every source before any user module executes.
    for filename, source in snapshot.modules.values():
        compile(source, filename, 'exec')
    emit('ready')
    main_path = str(root / request['mainScript'])
    main_source = next(s['source'] for s in request['sources'] if s['path'] == request['mainScript'])

    def execute_main():
        main = types.ModuleType('__main__')
        main.__file__, main.__package__ = main_path, None
        sys.modules['__main__'] = main
        sys.argv = [main_path]
        exec(compile(main_source, main_path, 'exec'), main.__dict__)

    if main_source.startswith('# vac:entry-version='):
        # The managed project entry owns the lifecycle invocation. The host validates
        # the complete template before submitting this frozen request.
        with entry_session(lambda: execute_project(request, root, snapshot=snapshot, emit=emit, inbox=inbox)):
            execute_main()
    else:
        # Compatibility for existing low-level execution clients. Desktop project
        # preparation rejects custom entries and upgrades only the exact empty one.
        execute_project(request, root, execute_main, snapshot=snapshot, emit=emit, inbox=inbox)


def execute_project(request, root, legacy_main=None, *, snapshot, emit, inbox=None):
    instances = []
    for script in request['scripts']:
        if script['kind'] not in ('object', 'backdrop'):
            continue
        name = '.'.join(Path(script['path']).with_suffix('').parts)
        filename, source = snapshot.modules[name]
        base = 'ElementBase' if script['kind'] == 'object' else 'BackdropBase'
        names = [n.name for n in ast.parse(source).body if isinstance(n, ast.ClassDef)
                 and any(isinstance(b, ast.Name) and b.id == base for b in n.bases)]
        if len(names) != 1:
            raise RuntimeError(f"{script['path']}：不能唯一确定对象类")
        module = importlib.import_module(name)
        instance = getattr(module, names[0])()
        instance._vac_context = {'object_id': script['objectId'], 'script_id': script['id'],
                                 'project_root': str(root), 'runtime_api': 1}
        instances.append((script, instance))
    # Resolve every start binding before executing any initialization or start body.
    starts = []
    clicks = {}
    router = MessageRouter()
    for script, instance in instances:
        bindings = [h for h in request['handlers'] if h['scriptId'] == script['id'] and h['event'] == 'start']
        methods = [h['handler'] for h in bindings] or ['_vac_on_start']
        for name in dict.fromkeys(methods):
            method = getattr(instance, name, None)
            if callable(method):
                starts.append(method)
            elif bindings:
                raise RuntimeError(f"{script['path']}：启动处理器 {name} 不存在")
        click_bindings = [h for h in request['handlers'] if h['scriptId'] == script['id'] and h['event'] == 'sprite-click']
        if click_bindings:
            if script['kind'] != 'object' or len(click_bindings) != 1:
                raise RuntimeError('Invalid sprite click binding.')
            callback = getattr(instance, click_bindings[0]['handler'], None)
            if not callable(callback):
                raise RuntimeError('Sprite click handler is missing.')
            clicks[script['objectId']] = (instance, callback)
        seen = set()
        for binding in request['handlers']:
            if binding['scriptId'] != script['id'] or binding['event'] != 'broadcast':
                continue
            name = binding['argument']
            if name in seen:
                raise RuntimeError('同一对象存在重复的消息事件。')
            seen.add(name)
            handler = getattr(instance, binding['handler'], None)
            if not callable(handler):
                raise RuntimeError(f"{script['path']}：消息处理器不存在")
            router.register(name, handler)
    if legacy_main is None:
        install_router(router)
    stage = StageSession(request, instances, emit) if request.get('initialStage') is not None else None
    try:
        for script, instance in instances:
            initialize = getattr(instance, 'initialize', None)
            if callable(initialize):
                initialize()
        if stage:
            stage.start()
        if legacy_main is None:
            open_messages()
        if legacy_main is not None:
            legacy_main()
        for method in starts:
            method()
        if request.get('keepAlive'):
            if legacy_main is not None or inbox is None:
                raise RuntimeError('Interactive execution requires the managed project entry.')
            inbox.run(clicks, emit, stage)
        # Keep publication alive for user-created non-daemon workers as well.
        while stage:
            workers = [t for t in threading.enumerate() if not t.daemon and t is not threading.current_thread()]
            if not workers:
                break
            for worker in workers:
                worker.join(timeout=0.1)
    finally:
        if stage:
            stage.close()



def main():
    envelope = json.loads(Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
    request = envelope['request']
    channel = socket.create_connection(('127.0.0.1', envelope['port']), timeout=10)
    channel.settimeout(None)
    stream = channel.makefile('w', encoding='utf-8', newline='\n')
    stream.write(envelope['secret'] + '\n')
    stream.flush()

    protocol_lock = threading.Lock()

    def emit(kind, text='', frames=None, exception=None, raw=None, stage=None):
        with protocol_lock:
            stream.write(json.dumps(dict(kind=kind, text=text, frames=frames, exception=exception,
                                        rawTraceback=raw, protocolVersion=2, stage=stage), ensure_ascii=True) + '\n')
            stream.flush()

    def emit_exception(error):
        data, raw = exception_payload(error)
        emit('exception', frames=data['frames'], exception=data, raw=raw)

    thread_failure = threading.Event()

    def thread_error(args):
        thread_failure.set()
        emit_exception(args.exc_value)
        if request.get('keepAlive'):
            inbox.stop(requested=True)
            import _thread
            _thread.interrupt_main()

    threading.excepthook = thread_error

    finished = threading.Event()
    inbox = EventInbox(request['sessionId'])

    def stop_listener():
        buffer = b''
        try:
            while not finished.is_set():
                if not select.select([channel], [], [], 0.1)[0]:
                    continue
                data = channel.recv(4096)
                if not data:
                    inbox.stop(requested=True)
                    import _thread
                    _thread.interrupt_main()
                    return
                buffer += data
                if len(buffer) > 8192:
                    raise ValueError('Control command exceeds the supported size.')
                while b'\n' in buffer:
                    line, buffer = buffer.split(b'\n', 1)
                    if line == b'stop':
                        inbox.stop(requested=True)
                        import _thread
                        _thread.interrupt_main()
                        return
                    try:
                        command = json.loads(line)
                    except (ValueError, UnicodeError):
                        continue
                    inbox.offer(command)
        except (OSError, ValueError):
            inbox.stop(requested=True)
            import _thread
            _thread.interrupt_main()
    listener = threading.Thread(target=stop_listener, daemon=True)
    listener.start()
    try:
        run(request, emit, inbox)
        # Match CPython's non-daemon thread lifetime while keeping the diagnostic channel open.
        while True:
            workers = [t for t in threading.enumerate() if not t.daemon and t is not threading.current_thread()]
            if not workers:
                break
            for worker in workers:
                worker.join(timeout=0.1)
        if thread_failure.is_set():
            return 1
    except SystemExit as error:
        if error.code not in (None, 0):
            emit_exception(error)
        raise
    except BaseException as error:
        if isinstance(error, (KeyboardInterrupt, EOFError)) and inbox.stop_requested.is_set():
            return 1 if thread_failure.is_set() else 0
        emit_exception(error)
        return 1
    finally:
        inbox.stop()
        finished.set()
        listener.join(timeout=0.5)
        emit('finished')
        stream.close()
        channel.shutdown(socket.SHUT_WR)
        channel.close()
    return 0


if __name__ == '__main__':
    sys.exit(main())
