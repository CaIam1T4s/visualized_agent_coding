"""Private project-entry bridge and session boundary for public message requests."""
from contextlib import contextmanager
import threading

from . import clock


_active = None


@contextmanager
def entry_session(execute):
    global _active
    if _active is not None:
        raise RuntimeError('A project entry session is already active')
    session = {'execute': execute, 'thread': threading.get_ident(), 'started': False,
               'router': None, 'messages_open': False, 'stage': None}
    _active = session
    clock.ensure_started()
    try:
        yield
        if not session['started']:
            raise RuntimeError('System entry did not start the project')
    finally:
        session['router'] = None
        session['messages_open'] = False
        session['stage'] = None
        _active = None
        clock.stop()


def run_project():
    session = _active
    if session is None or session['thread'] != threading.get_ident():
        raise RuntimeError('System entry requires the active project execution thread')
    if session['started']:
        raise RuntimeError('System entry may only start the project once')
    session['started'] = True
    session['execute']()


def install_router(router):
    """Called by the entry-owned lifecycle before any initialization runs."""
    if _active is None:
        raise RuntimeError('Message routing requires the system project entry')
    _active['router'] = router


def install_stage(stage):
    """Called by the entry-owned lifecycle once the run stage exists."""
    if _active is None:
        raise RuntimeError('舞台切换需要系统项目入口。')
    _active['stage'] = stage


def switch_backdrop(name):
    """Switch the active backdrop from any object in the current run."""
    session = _active
    if session is None or session['thread'] != threading.get_ident():
        raise RuntimeError('背景切换只能在项目运行主线程中调用。')
    if session['stage'] is None:
        raise RuntimeError('舞台尚未就绪。')
    session['stage'].switch_backdrop(name)


def open_messages():
    _active['messages_open'] = True


def dispatch_message(name):
    session = _active
    if session is None or session['thread'] != threading.get_ident():
        raise RuntimeError('消息只能在项目运行主线程中发送。')
    if not session['messages_open'] or session['router'] is None:
        raise RuntimeError('对象初始化完成前不能发送消息。')
    session['router'].broadcast(name)
