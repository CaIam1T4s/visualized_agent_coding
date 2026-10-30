"""Validated object state and bounded-rate stage publication for one run."""
import math
import threading

_DEFAULTS = dict(x=0, y=0, size=100, direction=90, visible=True, layer=0)
_LIMITS = dict(x=(-10000, 10000), y=(-10000, 10000), size=(1, 1000), direction=(-180, 180), layer=(-10000, 10000))
_MISSING = object()


def validate(name, value):
    if name == 'visible':
        if type(value) is not bool:
            raise ValueError('visible 必须为布尔值。')
    elif (type(value) not in (int, float) or not math.isfinite(value)
          or not _LIMITS[name][0] <= value <= _LIMITS[name][1]
          or name == 'layer' and type(value) is not int):
        raise ValueError(name + ' 超出有效范围。')
    return value


def stage_property(name):
    def get(self):
        return self.__dict__.get('_vac_values', {}).get(name, getattr(type(self), '_vac_initial', {}).get(name, _DEFAULTS[name]))
    def set(self, value):
        self.__dict__.setdefault('_vac_values', {})[name] = validate(name, value)
    return property(get, set)


class StageBase:
    x = stage_property('x')
    y = stage_property('y')
    size = stage_property('size')
    direction = stage_property('direction')
    visible = stage_property('visible')
    layer = stage_property('layer')
    # Class declarations are explicit parameters too, including zero and False.
    def __init_subclass__(cls, **kwargs):
        super().__init_subclass__(**kwargs)
        initial = {}
        for parent in reversed(cls.__mro__[1:]):
            initial.update(getattr(parent, '_vac_initial', {}))
        for name in _DEFAULTS:
            if name in cls.__dict__:
                initial[name] = validate(name, cls.__dict__[name])
                delattr(cls, name)
        cls._vac_initial = initial

    def __init__(self, *, x=_MISSING, y=_MISSING, size=_MISSING, direction=_MISSING,
                 visible=_MISSING, layer=_MISSING):
        for name, value in locals().copy().items():
            if name in _DEFAULTS and value is not _MISSING:
                setattr(self, name, value)

    def _vac_bind(self, layout, activate):
        values = dict(_DEFAULTS)
        values.update({k: validate(k, layout[k]) for k in _DEFAULTS if k in layout})
        values.update(getattr(type(self), '_vac_initial', {}))
        values.update(self.__dict__.get('_vac_values', {}))
        self.__dict__['_vac_values'] = values
        self.__dict__['_vac_activate'] = activate



class ElementBase(StageBase):
    pass


class BackdropBase(StageBase):
    def activate(self):
        """Select this backdrop by stable object identity for the current run."""
        callback = self.__dict__.get('_vac_activate')
        if callback is None:
            raise RuntimeError('背景尚未接入运行舞台。')
        callback()


class StageSession:
    def __init__(self, request, instances, emit):
        self.request, self.instances, self.emit = request, instances, emit
        initial = request.get('initialStage') or {}
        self.backdrop = initial.get('currentBackdropId')
        self.sequence = 0
        self.previous = None
        self.stopped = threading.Event()
        self.lock = threading.Lock()
        self.failure = None
        layouts = {o['objectId']: o for o in initial.get('objects', [])}
        backdrops = [s['objectId'] for s, _ in instances if s['kind'] == 'backdrop']
        if self.backdrop not in backdrops:
            self.backdrop = next(iter(backdrops), None)
        for script, instance in instances:
            if not isinstance(instance, StageBase):
                raise RuntimeError('项目自定义基类不支持舞台同步，请使用产品运行时基类。')
            def activate(identity=script['objectId']):
                if identity not in backdrops:
                    raise RuntimeError('仅背景可切换舞台背景。')
                self.backdrop = identity
            instance._vac_bind(layouts.get(script['objectId'], {}), activate)
        self.thread = threading.Thread(target=self._sample, daemon=True)

    def publish(self):
        with self.lock:
            objects = [dict(objectId=s['objectId'], **{k: getattr(o, k) for k in _DEFAULTS}) for s, o in self.instances]
            state = (objects, self.backdrop)
            if state == self.previous:
                return
            self.previous = state
            self.sequence += 1
            self.emit('stage', stage=dict(sessionId=self.request['sessionId'], sequence=self.sequence,
                      objects=objects, currentBackdropId=self.backdrop))

    def _sample(self):
        while not self.stopped.wait(1 / 30):
            try:
                self.publish()
            except BaseException as error:
                self.failure = error
                return

    def start(self):
        self.publish()
        self.thread.start()

    def close(self):
        self.stopped.set()
        if self.thread.ident is not None:
            self.thread.join()
        self.publish()
        if self.failure is not None:
            raise self.failure
