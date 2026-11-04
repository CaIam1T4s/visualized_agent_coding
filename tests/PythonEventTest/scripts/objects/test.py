# vac:template-version=3
from runtime.element_base import ElementBase
# vac:imports-begin
from vac_runtime import wait  # vac:import {"source":"runtime"}
# vac:imports-end


class Test(ElementBase):
    pass

    # vac:region-begin {"id":"members","kind":"members"}
    # vac:region-end members

    def _vac_on_start(self):
        # vac:region-begin {"id":"start","kind":"start"}
        self.goto(0, 0)
        print("self touch: ", self.touching("dsdf"))

        wait(1)
        copy = self.clone()
        copy.goto(90, 0)
        print("clone touch: ", copy.touching("dsdf"))
        # vac:region-end start



