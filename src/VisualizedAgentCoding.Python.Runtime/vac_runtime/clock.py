"""Run-scoped monotonic timer shared by project code and timer events."""
import math
import time

_start = None


def ensure_started():
    global _start
    if _start is None:
        _start = time.monotonic()


def stop():
    global _start
    _start = None


def seconds():
    if _start is None:
        raise RuntimeError('计时器尚未启动。')
    return time.monotonic() - _start


def timer():
    """Return seconds elapsed since the current project run started."""
    return seconds()


def parse_spec(argument):
    """Parse a timer argument such as ``1.5`` or ``every:1.5``."""
    text = argument
    repeat = text.startswith('every:')
    if repeat:
        text = text[len('every:'):]
    try:
        seconds_value = float(text)
    except (TypeError, ValueError):
        raise ValueError('计时器参数无效。') from None
    if not math.isfinite(seconds_value) or not 0.01 <= seconds_value <= 86400:
        raise ValueError('计时器秒数超出有效范围。')
    return repeat, seconds_value
