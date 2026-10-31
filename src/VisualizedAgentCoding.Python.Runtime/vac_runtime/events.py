"""Session-scoped bounded input queue; dispatch belongs to the project main thread."""
import queue
import threading


class EventInbox:
    def __init__(self, session_id):
        self.session_id = session_id
        self.pending = queue.Queue(maxsize=64)
        self.stopped = threading.Event()
        self.stop_requested = threading.Event()
        self.accepting = False
        self.lock = threading.Lock()

    def offer(self, command):
        with self.lock:
            if (not self.accepting or self.stopped.is_set() or not isinstance(command, dict)
                    or command.get('sessionId') != self.session_id):
                return False
            kind = command.get('kind')
            if kind == 'sprite-click' and isinstance(command.get('objectId'), str):
                item = ('sprite-click', command['objectId'])
            elif kind == 'stage-click':
                item = ('stage-click', None)
            elif kind == 'key' and isinstance(command.get('key'), str):
                item = ('key', command['key'])
            else:
                return False
            try:
                self.pending.put_nowait(item)
                return True
            except queue.Full:
                return False

    def stop(self, requested=False):
        if requested:
            self.stop_requested.set()
        with self.lock:
            self.accepting = False
            self.stopped.set()
            while True:
                try:
                    self.pending.get_nowait()
                except queue.Empty:
                    break

    def run(self, handlers, emit, stage, keys=None):
        with self.lock:
            if self.stopped.is_set():
                return
            self.accepting = True
        emit('interactive-ready')
        try:
            while not self.stopped.is_set():
                try:
                    kind, identity = self.pending.get(timeout=0.05)
                except queue.Empty:
                    continue
                if self.stopped.is_set():
                    break
                if kind == 'key':
                    # Key events are global: every object bound to the key runs, in binding order.
                    targets = list((keys or {}).get(identity, ()))
                    if identity != 'any':
                        targets.extend((keys or {}).get('any', ()))
                    for target in targets:
                        target[1]()
                    if stage:
                        stage.publish()
                    continue
                if kind == 'stage-click':
                    # Background clicks target whichever backdrop is active at dispatch time.
                    if stage is None or stage.backdrop is None:
                        continue
                    identity = stage.backdrop
                target = handlers.get(identity)
                if target is not None:
                    instance, callback = target
                    if instance.click_enabled:
                        callback()
                        if stage:
                            stage.publish()
        finally:
            self.stop()
