"""Private project-entry bridge. User-facing event APIs will use this session boundary."""
from contextlib import contextmanager
import threading


_active = None


@contextmanager
def entry_session(execute):
    global _active
    if _active is not None:
        raise RuntimeError('A project entry session is already active')
    session = {'execute': execute, 'thread': threading.get_ident(), 'started': False}
    _active = session
    try:
        yield
        if not session['started']:
            raise RuntimeError('System entry did not start the project')
    finally:
        _active = None


def run_project():
    session = _active
    if session is None or session['thread'] != threading.get_ident():
        raise RuntimeError('System entry requires the active project execution thread')
    if session['started']:
        raise RuntimeError('System entry may only start the project once')
    session['started'] = True
    session['execute']()
