"""VAC product execution API, versioned independently of project files."""
API_VERSION = 1
from .messages import broadcast


class ElementBase:
    def __init__(self):
        self.x = 0
        self.y = 0
        self.visible = True


class BackdropBase:
    def __init__(self):
        self.visible = True
