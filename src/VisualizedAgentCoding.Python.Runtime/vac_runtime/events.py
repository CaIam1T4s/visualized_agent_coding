"""Session-scoped bounded input queue and main-thread event pump."""
import math
import queue
import threading

from . import clock


class EventInbox:
    MAX_WAIT_DEPTH = 32

    def __init__(self, session_id):
        self.session_id = session_id
        self.pending = queue.Queue(maxsize=64)
        # Remaining handlers for one key press stay runnable while an earlier
        # handler is suspended in wait().
        self.deferred = queue.Queue()
        self.stopped = threading.Event()
        self.stop_requested = threading.Event()
        self.accepting = False
        self.lock = threading.Lock()
        self.pointer_handler = None
        self.context = None
        self.owner_thread = threading.get_ident()
        self.wait_depth = 0

    def prepare(self, handlers, emit, stage, keys=None, timers=None, backdrops=None):
        with self.lock:
            if self.stopped.is_set():
                return False
            self.context = (handlers, emit, stage, keys or {}, timers or [], backdrops or {})
            self.accepting = True
            return True

    def offer(self, command):
        with self.lock:
            if (not self.accepting or self.stopped.is_set() or not isinstance(command, dict)
                    or command.get('sessionId') != self.session_id):
                return False
            kind = command.get('kind')
            if kind == 'pointer':
                x, y = command.get('x'), command.get('y')
                if (type(x) not in (int, float) or type(y) not in (int, float)
                        or not math.isfinite(x) or not math.isfinite(y)):
                    return False
                handler = self.pointer_handler
                if handler is None:
                    return False
                handler(x, y)
                return True
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
            self.pointer_handler = None
            self.stopped.set()
            while True:
                try:
                    self.pending.get_nowait()
                except queue.Empty:
                    break
            while True:
                try:
                    self.deferred.get_nowait()
                except queue.Empty:
                    break

    def run(self, handlers=None, emit=None, stage=None, keys=None, timers=None, backdrops=None):
        if handlers is not None:
            self.prepare(handlers, emit, stage, keys, timers, backdrops)
        if self.context is None:
            raise RuntimeError('事件循环尚未准备。')
        _, emit, stage, _, _, _ = self.context
        with self.lock:
            if self.stopped.is_set():
                return
            self.accepting = True
        emit('interactive-ready')
        try:
            while not self.stopped.is_set():
                self._pump_once(0.05)
        finally:
            if stage:
                stage.clear_animations()
            self.stop()

    def wait(self, seconds):
        if threading.get_ident() != self.owner_thread:
            raise RuntimeError('wait 只能从项目运行主线程调用。')
        if self.context is None:
            raise RuntimeError('事件循环尚未启动，不能调用 wait。')
        if self.stopped.is_set():
            raise KeyboardInterrupt
        if not self.accepting:
            raise RuntimeError('当前会话不能等待。')
        if self.wait_depth >= self.MAX_WAIT_DEPTH:
            raise RuntimeError('wait 嵌套过深，请检查递归调用。')
        deadline = clock.seconds() + seconds
        self.wait_depth += 1
        try:
            while not self.stopped.is_set():
                remaining = deadline - clock.seconds()
                if remaining <= 0:
                    return
                self._pump_once(min(0.05, remaining))
            raise KeyboardInterrupt
        finally:
            self.wait_depth -= 1

    def _pump_once(self, timeout):
        handlers, emit, stage, keys, timers, backdrops = self.context
        self._run_backdrops(stage, backdrops)
        self._run_timers(timers, stage)
        if stage:
            stage.advance_animations()
        if self.stopped.is_set():
            return
        try:
            item = self.deferred.get_nowait()
        except queue.Empty:
            try:
                item = self.pending.get(timeout=max(0.0, timeout))
            except queue.Empty:
                return
        if self.stopped.is_set():
            return
        self._dispatch(item, handlers, keys, stage)

    def _dispatch(self, item, handlers, keys, stage):
        kind, identity = item
        if kind == 'key-target':
            identity[1]()
            if stage:
                stage.publish()
            return
        if kind == 'key':
            # Key events are global: every object bound to the key runs in binding order.
            # Defer later handlers so an earlier handler's wait() can pump them.
            targets = list((keys or {}).get(identity, ()))
            if identity != 'any':
                targets.extend((keys or {}).get('any', ()))
            if not targets:
                return
            for target in targets[1:]:
                self.deferred.put_nowait(('key-target', target))
            self._dispatch(('key-target', targets[0]), handlers, keys, stage)
            return
        if kind == 'stage-click':
            # Background clicks target whichever backdrop is active at dispatch time.
            if stage is None or stage.backdrop is None:
                return
            identity = stage.backdrop
        target = handlers.get(identity)
        if target is not None:
            instance, callback = target
            if instance.click_enabled:
                callback()
                if stage:
                    stage.publish()

    @staticmethod
    def _run_backdrops(stage, backdrops):
        if stage is None:
            return
        for target in stage.take_switches():
            for instance, callback in (backdrops or {}).get(target, ()):
                callback()
            stage.publish()

    @staticmethod
    def _run_timers(timers, stage):
        if not timers:
            return
        for timer in timers:
            now = clock.seconds()
            if timer['done'] or now < timer['next']:
                continue
            timer['callback']()
            if stage:
                stage.publish()
            if timer['repeat']:
                timer['next'] += timer['interval']
                if timer['next'] <= now:
                    timer['next'] = now + timer['interval']
            else:
                timer['done'] = True
