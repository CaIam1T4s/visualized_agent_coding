# vac:template-version=3
from runtime.element_base import ElementBase
# vac:imports-begin
from vac_runtime import broadcast  # vac:import {"source":"runtime"}
from vac_runtime import timer  # vac:import {"source":"runtime"}
from vac_runtime import switch_backdrop  # vac:import {"source":"runtime"}
from vac_runtime import wait  # vac:import {"source":"runtime"}
# vac:imports-end


class E(ElementBase):
    pass

    # vac:region-begin {"id":"members","kind":"members"}
    # vac:region-end members

    def _vac_on_start(self):
        # vac:region-begin {"id":"start","kind":"start"}
        print("before", timer())
        switch_backdrop("backdrop")
        print("after", timer())
        # vac:region-end start

    def initialize(self):
        # vac:region-begin {"id":"initialize","kind":"initialize"}
        pass
        # vac:region-end initialize



    def _vac_event_click_4a6e62df40404c91a3bf379ce99a0c16(self):
        # vac:region-begin {"id":"click_4a6e62df40404c91a3bf379ce99a0c16","kind":"event","eventName":"sprite-click"}
        print("before ", timer())
        self.glide_to(100, 0, 1)
        print('after wait', timer())
        broadcast("move-done")
        # vac:region-end click_4a6e62df40404c91a3bf379ce99a0c16






