# vac:template-version=3
from runtime.element_base import ElementBase
# vac:imports-begin
from vac_runtime import broadcast  # vac:import {"source":"runtime"}
from vac_runtime import wait  # vac:import {"source":"runtime"}
# vac:imports-end


class B(ElementBase):
    pass

    # vac:region-begin {"id":"members","kind":"members"}
    # vac:region-end members

    def _vac_on_start(self):
        # vac:region-begin {"id":"start","kind":"start"}
        pass
        # vac:region-end start

    def initialize(self):
        # vac:region-begin {"id":"initialize","kind":"initialize"}
        pass
        self.health = 100
        # vac:region-end initialize







