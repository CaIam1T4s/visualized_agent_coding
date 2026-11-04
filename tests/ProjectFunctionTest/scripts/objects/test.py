# vac:template-version=3
from runtime.element_base import ElementBase
# vac:imports-begin
import scripts.user.test as test  # vac:import {"source":"project","scriptId":"script-c9f44a6e4c924114b6df590ab02e8af6"}
# vac:imports-end


class Test(ElementBase):
    pass

    # vac:region-begin {"id":"members","kind":"members"}
    # vac:region-end members

    def _vac_on_start(self):
        # vac:region-begin {"id":"start","kind":"start"}
        print("hello")
        # vac:region-end start
