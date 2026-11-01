"""Project-level variables and lists shared by every script in one run."""
import math

_store = None


class ProjectData:
    def __init__(self, payload):
        payload = payload or {}
        self.variables = {}
        self.lists = {}
        for raw in payload.get('variables') or []:
            if not isinstance(raw, dict):
                raise ValueError('变量数据无效。')
            name = raw.get('name')
            kind = raw.get('type')
            value = _parse_variable(kind, raw.get('value'))
            if not isinstance(name, str) or not name or name in self.variables:
                raise ValueError('变量名称无效或重复。')
            self.variables[name] = [kind, value]
        for raw in payload.get('lists') or []:
            if not isinstance(raw, dict):
                raise ValueError('列表数据无效。')
            name = raw.get('name')
            values = raw.get('values')
            if (not isinstance(name, str) or not name or name in self.lists
                    or not isinstance(values, list)
                    or any(not isinstance(value, str) for value in values)):
                raise ValueError('列表名称或初始值无效。')
            self.lists[name] = list(values)

    def variable(self, name, kind=None):
        _validate_name(name)
        found = self.variables.get(name)
        if found is None:
            raise ValueError(f'变量不存在：{name}')
        if kind is not None and found[0] != kind:
            raise TypeError(f'变量 {name} 不是 {kind} 类型。')
        return found[1]

    def set_variable(self, name, value):
        type_name = self.variables.get(name, [None])[0] if isinstance(name, str) else None
        if type_name is None:
            raise ValueError(f'变量不存在：{name}')
        if not _valid_for(type_name, value):
            raise TypeError(f'变量 {name} 需要 {type_name} 类型。')
        self.variables[name][1] = value

    def add_to_variable(self, name, delta):
        if type(delta) not in (int, float) or not math.isfinite(delta):
            raise TypeError('变量变化量必须是有限数字。')
        value = self.variable(name, 'number') + delta
        self.variables[name][1] = float(value)

    def change_variable(self, name, delta):
        """Compatibility alias for projects that still import the old name."""
        self.add_to_variable(name, delta)

    def list(self, name):
        _validate_name(name)
        found = self.lists.get(name)
        if found is None:
            raise ValueError(f'列表不存在：{name}')
        return found

    def append(self, name, value):
        if not isinstance(value, str):
            raise TypeError('列表元素必须是文本。')
        self.list(name).append(value)

    def remove(self, name, index):
        values = self.list(name)
        if type(index) is not int:
            raise TypeError('列表索引必须是整数。')
        if index < 0 or index >= len(values):
            raise IndexError('列表索引超出范围。')
        return values.pop(index)

    def clear(self, name):
        self.list(name).clear()

    def length(self, name):
        return len(self.list(name))

    def values(self, name):
        """A snapshot, so iterating cannot observe or mutate the shared list."""
        return list(self.list(name))

    def get(self, name, index):
        values = self.list(name)
        if type(index) is not int:
            raise TypeError('列表索引必须是整数。')
        if index < 0 or index >= len(values):
            raise IndexError('列表索引超出范围。')
        return values[index]

    def set(self, name, index, value):
        if not isinstance(value, str):
            raise TypeError('列表元素必须是文本。')
        values = self.list(name)
        if type(index) is not int:
            raise TypeError('列表索引必须是整数。')
        if index < 0 or index >= len(values):
            raise IndexError('列表索引超出范围。')
        values[index] = value


def initialize(payload):
    global _store
    _store = ProjectData(payload)


def _active():
    if _store is None:
        raise RuntimeError('项目数据尚未初始化。')
    return _store


def _validate_name(name):
    if not isinstance(name, str) or not name or name != name.strip():
        raise ValueError('变量或列表名称无效。')


def _parse_variable(kind, value):
    if kind == 'number':
        try:
            number = float(value)
        except (TypeError, ValueError):
            raise ValueError('变量初始值不是数字。') from None
        if not math.isfinite(number):
            raise ValueError('变量初始值不是有限数字。')
        return number
    if kind == 'boolean':
        if isinstance(value, bool):
            return value
        if isinstance(value, str) and value.lower() in ('true', 'false'):
            return value.lower() == 'true'
        raise ValueError('变量初始值不是布尔值。')
    if kind == 'text':
        if not isinstance(value, str):
            raise ValueError('变量初始值不是文本。')
        return value
    raise ValueError(f'未知变量类型：{kind}')


def _valid_for(kind, value):
    if kind == 'number':
        return type(value) in (int, float) and math.isfinite(value)
    if kind == 'boolean':
        return type(value) is bool
    if kind == 'text':
        return isinstance(value, str)
    return False


def get_variable(name):
    return _active().variable(name)


def set_variable(name, value):
    _active().set_variable(name, value)


def add_to_variable(name, delta):
    _active().add_to_variable(name, delta)


def change_variable(name, delta):
    """Backward-compatible alias for add_to_variable."""
    add_to_variable(name, delta)


def list_append(name, value):
    _active().append(name, value)


def list_remove(name, index):
    return _active().remove(name, index)


def list_clear(name):
    _active().clear(name)


def list_length(name):
    return _active().length(name)


def list_values(name):
    return _active().values(name)


def list_get(name, index):
    return _active().get(name, index)


def list_set(name, index, value):
    _active().set(name, index, value)
