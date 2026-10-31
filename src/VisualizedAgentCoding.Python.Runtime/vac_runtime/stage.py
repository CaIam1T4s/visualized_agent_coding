"""Validated object state and bounded-rate stage publication for one run."""
import math
from pathlib import Path
import threading

from . import clock

_DEFAULTS = dict(x=0, y=0, size=100, direction=90, visible=True, layer=0, click_enabled=True)
_LIMITS = dict(x=(-100000, 100000), y=(-100000, 100000), size=(1, 1000), direction=(-180, 180), layer=(-10000, 10000))
_MISSING = object()
# Python attribute names stay snake_case; the wire protocol is camelCase.
_WIRE = dict(click_enabled='clickEnabled')
_WIRE_REVERSE = {wire: name for name, wire in _WIRE.items()}


def validate(name, value):
    if name in ('visible', 'click_enabled'):
        if type(value) is not bool:
            raise ValueError(name + ' 必须为布尔值。')
    elif (type(value) not in (int, float) or not math.isfinite(value)
          or not _LIMITS[name][0] <= value <= _LIMITS[name][1]
          or name == 'layer' and type(value) is not int):
        raise ValueError(name + ' 超出有效范围。')
    return value


def finite_number(name, value):
    if type(value) not in (int, float) or not math.isfinite(value):
        raise ValueError(name + ' 必须为有限数值。')
    return float(value)


def normalize_direction(value):
    value = math.fmod(float(value), 360.0)
    if value > 180:
        value -= 360
    elif value < -180:
        value += 360
    return value


def stage_property(name):
    def get(self):
        return self.__dict__.get('_vac_values', {}).get(name, getattr(type(self), '_vac_initial', {}).get(name, _DEFAULTS[name]))
    def set(self, value):
        self.__dict__.setdefault('_vac_values', {})[name] = validate(name, value)
    return property(get, set)


class StageBase:
    @property
    def timer(self):
        return clock.timer()

    x = stage_property('x')
    y = stage_property('y')
    size = stage_property('size')
    direction = stage_property('direction')
    visible = stage_property('visible')
    layer = stage_property('layer')
    click_enabled = stage_property('click_enabled')
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

    def _vac_bind(self, layout, activate, pointer=None, animate=None):
        values = dict(_DEFAULTS)
        values.update({k: validate(k, layout[_WIRE.get(k, k)]) for k in _DEFAULTS if _WIRE.get(k, k) in layout})
        values.update(getattr(type(self), '_vac_initial', {}))
        values.update(self.__dict__.get('_vac_values', {}))
        self.__dict__['_vac_values'] = values
        self.__dict__['_vac_activate'] = activate
        self.__dict__['_vac_pointer'] = pointer
        self.__dict__['_vac_animate'] = animate



class ElementBase(StageBase):
    def move(self, steps):
        distance = finite_number('移动距离', steps)
        angle = math.radians(self.direction)
        x = self.x + distance * math.sin(angle)
        y = self.y + distance * math.cos(angle)
        validate('x', x)
        validate('y', y)
        self.x = x
        self.y = y

    def turn(self, degrees):
        self.direction = normalize_direction(self.direction + finite_number('旋转角度', degrees))

    def goto(self, x, y):
        validate('x', x)
        validate('y', y)
        self.x = x
        self.y = y

    def point_in_direction(self, angle):
        self.direction = normalize_direction(finite_number('方向', angle))

    def change_x(self, delta):
        self.x = self.x + finite_number('X 变化量', delta)

    def change_y(self, delta):
        self.y = self.y + finite_number('Y 变化量', delta)

    def point_towards_pointer(self):
        pointer = self.__dict__.get('_vac_pointer')
        if pointer is None:
            return
        x, y, seen = pointer()
        if not seen:
            return
        dx, dy = x - self.x, y - self.y
        if abs(dx) < 1e-9 and abs(dy) < 1e-9:
            return
        self.direction = normalize_direction(math.degrees(math.atan2(dx, dy)))

    def move_to_pointer(self):
        pointer = self.__dict__.get('_vac_pointer')
        if pointer is None:
            return
        x, y, seen = pointer()
        if seen:
            self.goto(x, y)

    def glide_to(self, x, y, seconds):
        validate('x', x)
        validate('y', y)
        duration = finite_number('动画时长', seconds)
        if duration <= 0:
            self.goto(x, y)
            return
        animate = self.__dict__.get('_vac_animate')
        if animate is None:
            self.goto(x, y)
            return
        animate(x, y, duration)

    def glide_by(self, dx, dy, seconds):
        self.glide_to(self.x + finite_number('X 变化量', dx),
                      self.y + finite_number('Y 变化量', dy), seconds)



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
        self.pending_switches = []
        self.backdrop_names = {}
        self.pointer_x = 0.0
        self.pointer_y = 0.0
        self.pointer_seen = False
        self.animations = {}
        self.instances_by_id = {script['objectId']: instance for script, instance in instances
                                if script['kind'] == 'object'}
        layouts = {o['objectId']: o for o in initial.get('objects', [])}
        backdrops = {s['objectId'] for s, _ in instances if s['kind'] == 'backdrop'}
        for script, _ in instances:
            if script['kind'] != 'backdrop':
                continue
            name = Path(script['path']).stem
            if name in self.backdrop_names:
                raise RuntimeError(f'背景名称重复：{name}')
            self.backdrop_names[name] = script['objectId']
        if self.backdrop not in backdrops:
            self.backdrop = next(iter(backdrops), None)
        self.backdrops = backdrops
        for script, instance in instances:
            if not isinstance(instance, StageBase):
                raise RuntimeError('项目自定义基类不支持舞台同步，请使用产品运行时基类。')
            def activate(identity=script['objectId']):
                self.switch_backdrop_id(identity)
            instance._vac_bind(layouts.get(script['objectId'], {}), activate, self.pointer_position,
                lambda x, y, seconds, identity=script['objectId']: self.start_animation(identity, x, y, seconds))
        self.thread = threading.Thread(target=self._sample, daemon=True)

    def start_animation(self, identity, target_x, target_y, seconds):
        instance = self.instances_by_id.get(identity)
        if not isinstance(instance, ElementBase):
            raise RuntimeError('仅角色可播放位移动画。')
        validate('x', target_x)
        validate('y', target_y)
        if seconds <= 0 or (target_x == instance.x and target_y == instance.y):
            self.animations.pop(identity, None)
            instance.goto(target_x, target_y)
            return
        self.animations[identity] = dict(start_x=instance.x, start_y=instance.y,
            target_x=target_x, target_y=target_y, started=clock.seconds(),
            duration=seconds, last_x=instance.x, last_y=instance.y)

    def advance_animations(self):
        changed = False
        now = clock.seconds()
        for identity, tween in list(self.animations.items()):
            instance = self.instances_by_id.get(identity)
            if not isinstance(instance, ElementBase) or abs(instance.x - tween['last_x']) > 1e-6                     or abs(instance.y - tween['last_y']) > 1e-6:
                del self.animations[identity]
                continue
            progress = min(1.0, max(0.0, (now - tween['started']) / tween['duration']))
            x = tween['start_x'] + (tween['target_x'] - tween['start_x']) * progress
            y = tween['start_y'] + (tween['target_y'] - tween['start_y']) * progress
            instance.x = x
            instance.y = y
            tween['last_x'] = x
            tween['last_y'] = y
            changed = True
            if progress >= 1.0:
                instance.x = tween['target_x']
                instance.y = tween['target_y']
                del self.animations[identity]
        if changed:
            self.publish()

    def clear_animations(self):
        self.animations.clear()

    def set_pointer(self, x, y):
        if type(x) not in (int, float) or type(y) not in (int, float) or not math.isfinite(x) or not math.isfinite(y):
            raise ValueError('指针坐标必须为有限数值。')
        with self.lock:
            self.pointer_x = float(x)
            self.pointer_y = float(y)
            self.pointer_seen = True

    def pointer_position(self):
        with self.lock:
            return self.pointer_x, self.pointer_y, self.pointer_seen

    def switch_backdrop_id(self, identity):
        with self.lock:
            if identity not in self.backdrops:
                raise RuntimeError('仅背景可切换舞台背景。')
            if identity == self.backdrop:
                return
            self.backdrop = identity
            self.pending_switches.append(identity)

    def switch_backdrop(self, name):
        identity = self.backdrop_names.get(name)
        if identity is None:
            raise ValueError(f'背景不存在：{name}')
        self.switch_backdrop_id(identity)

    def take_switches(self):
        with self.lock:
            pending = self.pending_switches
            self.pending_switches = []
            return pending

    def publish(self):
        with self.lock:
            # Wire fields are camelCase like objectId/currentBackdropId.
            objects = [dict(objectId=s['objectId'], **{_WIRE.get(k, k): getattr(o, k) for k in _DEFAULTS}) for s, o in self.instances]
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
