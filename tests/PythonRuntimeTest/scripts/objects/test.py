# vac:template-version=3
from runtime.element_base import ElementBase
# vac:imports-begin
import scripts.user.input_test as input_test  # vac:import {"source":"project","scriptId":"script-74db4ae9c83848779ee313ff701f1c87"}
# vac:imports-end


class Test(ElementBase):
    pass

    # vac:region-begin {"id":"members","kind":"members"}
    # vac:region-end members

    def _vac_on_start(self):
        # vac:region-begin {"id":"start","kind":"start"}
        input_test.foo()
        # vac:region-end start

    def initialize(self):
        # vac:region-begin {"id":"initialize","kind":"initialize"}
        print("init")
        # vac:region-end initialize
