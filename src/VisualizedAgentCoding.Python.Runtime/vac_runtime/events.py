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
                    or command.get('sessionId') != self.session_id or command.get('kind') != 'sprite-click'
                    or not isinstance(command.get('objectId'), str)):
                return False
            try:
                self.pending.put_nowait(command['objectId'])
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

    def run(self, handlers, emit, stage):
        with self.lock:
            if self.stopped.is_set():
                return
            self.accepting = True
        emit('interactive-ready')
        try:
            while not self.stopped.is_set():
                try:
                    identity = self.pending.get(timeout=0.05)
                except queue.Empty:
                    continue
                if self.stopped.is_set():
                    break
                target = handlers.get(identity)
                if target is not None:
                    instance, callback = target
                    if instance.visible:
                        callback()
                        if stage:
                            stage.publish()
        finally:
            self.stop()
