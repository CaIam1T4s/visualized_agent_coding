"""Synchronous, ordered message dispatch for one project-entry session."""
import unicodedata


def validate_name(name):
    if (not isinstance(name, str) or not name or name != name.strip()
            or len(name.encode('utf-16-le')) // 2 > 248
            or any(unicodedata.category(char) == 'Cc' for char in name)):
        raise ValueError('消息名称须为 1–248 个字符，不能包含控制字符或首尾空白。')
    return name


class MessageRouter:
    MAX_DEPTH = 32

    def __init__(self):
        self.handlers = {}
        self.depth = 0

    def register(self, name, handler):
        self.handlers.setdefault(validate_name(name), []).append(handler)

    def broadcast(self, name):
        name = validate_name(name)
        if self.depth >= self.MAX_DEPTH:
            raise RuntimeError('消息嵌套超过 32 层，请检查循环广播。')
        self.depth += 1
        try:
            for handler in self.handlers.get(name, ()):
                handler()
        finally:
            self.depth -= 1


def broadcast(name):
    """Send synchronously through the active system-entry message route."""
    from ._entry import dispatch_message
    dispatch_message(validate_name(name))
