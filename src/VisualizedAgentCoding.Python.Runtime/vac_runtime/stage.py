"""Validated object state and bounded-rate stage publication for one run."""
import math
from pathlib import Path
import threading
import unicodedata

from . import clock
from .messages import validate_name

_DEFAULTS = dict(x=0, y=0, size=100, direction=90, visible=True, layer=0, click_enabled=True)
_LIMITS = dict(x=(-100000, 100000), y=(-100000, 100000), size=(1, 1000), direction=(-180, 180), layer=(-10000, 10000))
_MISSING = object()
# Python attribute names stay snake_case; the wire protocol is camelCase.
_WIRE = dict(click_enabled='clickEnabled')
_WIRE_REVERSE = {wire: name for name, wire in _WIRE.items()}


def validate_costume_name(name):
    if (not isinstance(name, str) or not name or name != name.strip()
            or len(name.encode('utf-16-le')) // 2 > 64
            or any(unicodedata.category(char) == 'Cc' for char in name)):
        raise ValueError('造型名称须为 1–64 个字符，不能包含控制字符或首尾空白。')
    return name


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

    @property
    def is_clone(self):
        """True for a runtime clone, False for the project-owned role."""
        return bool(self.__dict__.get('_vac_is_clone', False))

    @property
    def costume(self):
        """Read-only display name of the active costume."""
        active = self.__dict__.get('_vac_active_costume')
        return None if active is None else active.get('name')

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

    def _vac_bind(self, layout, activate, pointer=None, animate=None, animate_direction=None, animate_size=None,
                  clone=None, delete_clone=None):
        costumes = []
        names = set()
        for raw in layout.get('costumes') or []:
            if not isinstance(raw, dict):
                raise ValueError('造型数据无效。')
            identity, name = raw.get('id'), validate_costume_name(raw.get('name'))
            image_path, normalize = raw.get('imagePath'), raw.get('normalizeImage', False)
            if not isinstance(identity, str) or not identity or name in names:
                raise ValueError('造型身份或名称重复。')
            if not isinstance(image_path, str) or type(normalize) is not bool:
                raise ValueError('造型图片路径或适配标记无效。')
            names.add(name)
            costumes.append(dict(id=identity, name=name, image_path=image_path, normalize=normalize))
        by_name = {costume['name']: costume for costume in costumes}
        active = None
        if costumes:
            initial_id = layout.get('initialCostumeId')
            active = next((costume for costume in costumes if costume['id'] == initial_id), None)
            if active is None:
                raise ValueError('角色初始造型不存在。')
        elif layout.get('imagePath'):
            active = dict(id=None, name=None, image_path=layout['imagePath'],
                          normalize=bool(layout.get('normalizeImage', False)))
        values = dict(_DEFAULTS)
        values.update({k: validate(k, layout[_WIRE.get(k, k)]) for k in _DEFAULTS if _WIRE.get(k, k) in layout})
        values.update(getattr(type(self), '_vac_initial', {}))
        values.update(self.__dict__.get('_vac_values', {}))
        self.__dict__['_vac_values'] = values
        self.__dict__['_vac_activate'] = activate
        self.__dict__['_vac_pointer'] = pointer
        self.__dict__['_vac_animate'] = animate
        self.__dict__['_vac_animate_direction'] = animate_direction
        self.__dict__['_vac_animate_size'] = animate_size
        self.__dict__['_vac_clone'] = clone
        self.__dict__['_vac_delete_clone'] = delete_clone
        self.__dict__['_vac_costumes'] = by_name
        self.__dict__['_vac_active_costume'] = active

    def _vac_switch_costume(self, name):
        validate_costume_name(name)
        costumes = self.__dict__.get('_vac_costumes')
        if costumes is None:
            raise RuntimeError('角色尚未接入运行舞台。')
        target = costumes.get(name)
        if target is None:
            raise ValueError(f'造型不存在：{name}')
        current = self.__dict__.get('_vac_active_costume')
        if current is None or current.get('id') != target['id']:
            self.__dict__['_vac_active_costume'] = dict(target)



class ElementBase(StageBase):
    def clone(self):
        """Create an independent runtime copy of this role and return it."""
        callback = self.__dict__.get('_vac_clone')
        if callback is None:
            raise RuntimeError('角色尚未接入运行舞台。')
        return callback()

    def delete_clone(self):
        """Remove this runtime clone from the stage."""
        callback = self.__dict__.get('_vac_delete_clone')
        if callback is None:
            raise RuntimeError('角色尚未接入运行舞台。')
        return callback()

    def switch_costume(self, name):
        """Select one of this role's costumes by display name."""
        self._vac_switch_costume(name)

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

    def glide_to(self, x, y, seconds, on_finish=None):
        validate('x', x)
        validate('y', y)
        duration = finite_number('动画时长', seconds)
        if on_finish is not None:
            validate_name(on_finish)
        if duration <= 0:
            self.goto(x, y)
            return
        animate = self.__dict__.get('_vac_animate')
        if animate is None:
            self.goto(x, y)
            return
        animate(x, y, duration, on_finish)

    def glide_by(self, dx, dy, seconds, on_finish=None):
        self.glide_to(self.x + finite_number('X 变化量', dx),
                      self.y + finite_number('Y 变化量', dy), seconds, on_finish)

    def rotate_to(self, angle, seconds, on_finish=None):
        target = normalize_direction(finite_number('方向', angle))
        duration = finite_number('动画时长', seconds)
        if on_finish is not None:
            validate_name(on_finish)
        if duration <= 0:
            self.direction = target
            return
        animate = self.__dict__.get('_vac_animate_direction')
        if animate is None:
            self.direction = target
            return
        animate(target, duration, on_finish)

    def rotate_by(self, degrees, seconds, on_finish=None):
        self.rotate_to(self.direction + finite_number('旋转角度', degrees), seconds, on_finish)

    def glide_size_to(self, percent, seconds, on_finish=None):
        validate('size', percent)
        duration = finite_number('动画时长', seconds)
        if on_finish is not None:
            validate_name(on_finish)
        if duration <= 0:
            self.size = percent
            return
        animate = self.__dict__.get('_vac_animate_size')
        if animate is None:
            self.size = percent
            return
        animate(percent, duration, on_finish)

    def glide_size_by(self, delta, seconds, on_finish=None):
        self.glide_size_to(self.size + finite_number('大小变化量', delta), seconds, on_finish)



class BackdropBase(StageBase):
    def activate(self):
        """Select this backdrop by stable object identity for the current run."""
        callback = self.__dict__.get('_vac_activate')
        if callback is None:
            raise RuntimeError('背景尚未接入运行舞台。')
        callback()


class StageSession:
    MAX_CLONES = 300

    def __init__(self, request, instances, emit, broadcast=None, click_handlers=None, clone_handlers=None):
        self.request, self.instances, self.emit = request, instances, emit
        self.broadcast = broadcast
        self.click_handlers = click_handlers
        self.clone_handlers = clone_handlers
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
        self.scripts_by_id = {script['objectId']: script for script, _ in instances}
        self.clone_count = 0
        layouts = {o['objectId']: o for o in initial.get('objects', [])}
        self.layouts = layouts
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
                lambda x, y, seconds, on_finish, identity=script['objectId']: self.start_position_animation(identity, x, y, seconds, on_finish),
                lambda angle, seconds, on_finish, identity=script['objectId']: self.start_direction_animation(identity, angle, seconds, on_finish),
                lambda value, seconds, on_finish, identity=script['objectId']: self.start_size_animation(identity, value, seconds, on_finish),
                clone=lambda identity=script['objectId']: self.clone_instance(identity),
                delete_clone=lambda identity=script['objectId']: self.delete_instance(identity))
        self.thread = threading.Thread(target=self._sample, daemon=True)

    def start_position_animation(self, identity, target_x, target_y, seconds, on_finish=None):
        instance = self._animation_instance(identity)
        validate('x', target_x)
        validate('y', target_y)
        key = (identity, 'position')
        if seconds <= 0 or (target_x == instance.x and target_y == instance.y):
            self.animations.pop(key, None)
            instance.goto(target_x, target_y)
            return
        if on_finish is not None:
            validate_name(on_finish)
        self.animations[key] = dict(kind='position', start=(instance.x, instance.y),
            target=(target_x, target_y), started=clock.seconds(), duration=seconds,
            last=(instance.x, instance.y), on_finish=on_finish)

    def start_direction_animation(self, identity, target, seconds, on_finish=None):
        instance = self._animation_instance(identity)
        target = normalize_direction(target)
        key = (identity, 'direction')
        delta = normalize_direction(target - instance.direction)
        if seconds <= 0 or delta == 0:
            self.animations.pop(key, None)
            instance.direction = target
            return
        if on_finish is not None:
            validate_name(on_finish)
        self.animations[key] = dict(kind='direction', start=instance.direction,
            target=target, delta=delta, started=clock.seconds(), duration=seconds,
            last=instance.direction, on_finish=on_finish)

    def start_size_animation(self, identity, target, seconds, on_finish=None):
        instance = self._animation_instance(identity)
        validate('size', target)
        key = (identity, 'size')
        if seconds <= 0 or target == instance.size:
            self.animations.pop(key, None)
            instance.size = target
            return
        if on_finish is not None:
            validate_name(on_finish)
        self.animations[key] = dict(kind='size', start=instance.size, target=target,
            started=clock.seconds(), duration=seconds, last=instance.size, on_finish=on_finish)

    def _animation_instance(self, identity):
        instance = self.instances_by_id.get(identity)
        if not isinstance(instance, ElementBase):
            raise RuntimeError('仅角色可播放动画。')
        return instance

    def clone_instance(self, identity):
        """Create one visible, independent role copy with the current runtime state."""
        clone_start = None
        with self.lock:
            source = self.instances_by_id.get(identity)
            if not isinstance(source, ElementBase):
                raise RuntimeError('仅角色可以克隆。')
            if source.__dict__.get('_vac_is_clone'):
                raise RuntimeError('克隆体不能再次克隆。')
            if self.clone_count >= self.MAX_CLONES:
                raise RuntimeError(f'克隆数量已达到上限（{self.MAX_CLONES}）。')
            script = self.scripts_by_id.get(identity)
            if script is None:
                raise RuntimeError('找不到角色脚本信息。')
            self.clone_count += 1
            clone_id = f'clone:{identity}:{self.clone_count}'
            clone_script = dict(script, objectId=clone_id)
            clone = type(source)()
            clone.__dict__['_vac_is_clone'] = True
            clone._vac_context = dict(getattr(source, '_vac_context', {}), object_id=clone_id, clone_of=identity)
            clone._vac_bind(self.layouts.get(identity, {}), lambda: None, self.pointer_position,
                lambda x, y, seconds, on_finish, identity=clone_id: self.start_position_animation(identity, x, y, seconds, on_finish),
                lambda angle, seconds, on_finish, identity=clone_id: self.start_direction_animation(identity, angle, seconds, on_finish),
                lambda value, seconds, on_finish, identity=clone_id: self.start_size_animation(identity, value, seconds, on_finish),
                clone=lambda: self.clone_instance(clone_id),
                delete_clone=lambda: self.delete_instance(clone_id))
            values = source.__dict__.get('_vac_values')
            if values is not None:
                clone.__dict__['_vac_values'] = dict(values)
            active = source.__dict__.get('_vac_active_costume')
            clone.__dict__['_vac_active_costume'] = dict(active) if active is not None else None
            self.scripts_by_id[clone_id] = clone_script
            self.instances_by_id[clone_id] = clone
            self.instances.append((clone_script, clone))
            if self.click_handlers is not None and identity in self.click_handlers:
                source_callback = self.click_handlers[identity][1]
                callback = getattr(clone, getattr(source_callback, '__name__', ''), None)
                if callable(callback):
                    self.click_handlers[clone_id] = (clone, callback)
            if self.clone_handlers is not None and identity in self.clone_handlers:
                source_callback = self.clone_handlers[identity]
                callback = getattr(clone, getattr(source_callback, '__name__', ''), None)
                if callable(callback):
                    clone_start = callback
        self.publish()
        if clone_start is not None:
            clone_start()
        return clone

    def delete_instance(self, identity):
        """Delete one runtime clone and remove all of its event and animation bindings."""
        with self.lock:
            instance = self.instances_by_id.get(identity)
            if instance is None:
                return False
            if not instance.__dict__.get('_vac_is_clone'):
                raise RuntimeError('只能删除克隆体。')
            for index, (script, candidate) in enumerate(self.instances):
                if candidate is instance or script['objectId'] == identity:
                    del self.instances[index]
                    break
            self.instances_by_id.pop(identity, None)
            self.scripts_by_id.pop(identity, None)
            if self.click_handlers is not None:
                self.click_handlers.pop(identity, None)
            for key in [key for key in self.animations if key[0] == identity]:
                self.animations.pop(key, None)
            instance.__dict__['_vac_deleted'] = True
        self.publish()
        return True

    def advance_animations(self):
        changed = False
        finished = []
        now = clock.seconds()
        for key, tween in list(self.animations.items()):
            identity, _ = key
            instance = self.instances_by_id.get(identity)
            if not isinstance(instance, ElementBase):
                del self.animations[key]
                continue
            kind = tween['kind']
            if kind == 'position':
                current = (instance.x, instance.y)
                last = tween['last']
                if abs(current[0] - last[0]) > 1e-6 or abs(current[1] - last[1]) > 1e-6:
                    del self.animations[key]
                    continue
            elif kind == 'direction':
                if abs(instance.direction - tween['last']) > 1e-6:
                    del self.animations[key]
                    continue
            else:
                if abs(instance.size - tween['last']) > 1e-6:
                    del self.animations[key]
                    continue
            progress = min(1.0, max(0.0, (now - tween['started']) / tween['duration']))
            if kind == 'position':
                start, target = tween['start'], tween['target']
                x = start[0] + (target[0] - start[0]) * progress
                y = start[1] + (target[1] - start[1]) * progress
                instance.x = x
                instance.y = y
                tween['last'] = (x, y)
            elif kind == 'direction':
                value = normalize_direction(tween['start'] + tween['delta'] * progress)
                instance.direction = value
                tween['last'] = value
            else:
                value = tween['start'] + (tween['target'] - tween['start']) * progress
                instance.size = value
                tween['last'] = value
            changed = True
            if progress >= 1.0:
                if kind == 'position':
                    instance.x, instance.y = tween['target']
                elif kind == 'direction':
                    instance.direction = tween['target']
                else:
                    instance.size = tween['target']
                if tween.get('on_finish') is not None:
                    finished.append(tween['on_finish'])
                del self.animations[key]
        if changed:
            self.publish()
        for message in finished:
            if self.broadcast is not None:
                self.broadcast(message)

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
            objects = []
            for script, instance in self.instances:
                row = dict(objectId=script['objectId'],
                           **{_WIRE.get(k, k): getattr(instance, k) for k in _DEFAULTS})
                active = instance.__dict__.get('_vac_active_costume')
                if active is not None:
                    row['imagePath'] = active['image_path']
                    row['normalizeImage'] = active['normalize']
                objects.append(row)
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
